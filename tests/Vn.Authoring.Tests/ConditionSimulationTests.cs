using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// W36-b — 조건 값 시뮬. 식 평가기는 지원 문법만 읽고(그 밖은 정직하게 실패),
/// 시뮬 패스는 문서 순서대로 set을 누적하며 참인 첫 갈래를 자동 판정한다.
/// 수동 선택이 언제나 자동을 덮는다.
/// </summary>
public class ConditionSimulationTests
{
    // ── 식 평가기 ─────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> Values = new(StringComparer.Ordinal)
    {
        ["favor"] = "3",
        ["flag"] = "true",
        ["route"] = "willo",
    };

    [Theory]
    [InlineData("favor >= 2", true)]
    [InlineData("$favor >= 2", true)]
    [InlineData("favor < 2", false)]
    [InlineData("favor == 3", true)]
    [InlineData("favor != 3", false)]
    [InlineData("flag == true", true)]
    [InlineData("flag == false", false)]
    [InlineData("$flag", true)]
    [InlineData("route == \"willo\"", true)]
    [InlineData("route != 'laru'", true)]
    [InlineData("2 <= favor", true)]
    public void 지원_문법은_평가된다(string expression, bool expected)
    {
        Assert.True(ConditionExpression.TryEvaluate(expression, Values, out bool result));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("favor >= 2 and flag")]   // 논리 결합 — 지원 밖
    [InlineData("visited(\"scene\")")]    // 함수 — 지원 밖
    [InlineData("unknown >= 1")]          // 미지 변수
    [InlineData("route > 'a'")]           // 문자열 대소 비교 — 지원 밖
    [InlineData("")]
    public void 지원_밖_문법은_정직하게_실패한다(string expression)
    {
        Assert.False(ConditionExpression.TryEvaluate(expression, Values, out _));
    }

    // ── 시뮬 패스 ─────────────────────────────────────────────────────────

    private static DialogueResultLine Line(
        int index, string lineId, ConditionTransitionKind? kind = null, string? expression = null,
        params (string Variable, SetOperatorKind Op, string Value)[] sets)
    {
        return new DialogueResultLine(
            index, lineId, 1, "화자", "대사",
            kind is { } transitionKind
                ? new DialogueResultTransition(transitionKind, "cd_x", "조건", expression)
                : null,
            BranchExitTargetNodeId: null,
            sets.Select(set => new DialogueResultSetOperation(set.Variable, set.Op, set.Value)).ToList());
    }

    private static ConditionSimulation.Result Decide(
        IReadOnlyList<DialogueResultLine> lines,
        StageBranchSelection manual,
        params (string Variable, string Value)[] initial)
    {
        return ConditionSimulation.Decide(
            lines,
            line => line.Transition?.Kind,
            line => line.LineId,
            line => line.Transition?.Expression,
            line => line.Sets.Select(operation =>
                (operation.Variable, operation.Operator, operation.Value)),
            initial,
            manual);
    }

    /// <summary>set favor+=3 → [favor>=2 | favor>=0] → 합류.</summary>
    private static DialogueResultLine[] Document() =>
    [
        Line(0, "ln_0", sets: ("favor", SetOperatorKind.Add, "3")),
        Line(1, "ln_if", ConditionTransitionKind.BeginIf, "favor >= 2"),
        Line(2, "ln_elif", ConditionTransitionKind.BeginElseIf, "favor >= 0"),
        Line(3, "ln_end", ConditionTransitionKind.EndIf),
    ];

    [Fact]
    public void 참인_첫_갈래가_자동으로_선택된다()
    {
        ConditionSimulation.Result result = Decide(
            Document(), new StageBranchSelection(), ("favor", "0"));

        // favor 0 + 3 = 3 → 첫 갈래(>=2)가 참.
        Assert.True(result.Effective.TryGetCondition("ln_if", out int branch));
        Assert.Equal(0, branch);
        Assert.Contains("ln_if", result.AutoBlocks);
        Assert.Empty(result.UndecidableBlocks);
    }

    [Fact]
    public void 시작값이_다르면_다른_갈래가_잡히고_전부_거짓이면_건너뛴다()
    {
        ConditionSimulation.Result second = Decide(
            Document(), new StageBranchSelection(), ("favor", "-4"));

        // -4 + 3 = -1 → 첫 갈래 거짓, 둘째(>=0)도 거짓 → 건너뜀.
        Assert.True(second.Effective.TryGetCondition("ln_if", out int branch));
        Assert.Equal(StageBranchSelection.SkipAllBranches, branch);

        ConditionSimulation.Result third = Decide(
            Document(), new StageBranchSelection(), ("favor", "-3"));

        // -3 + 3 = 0 → 둘째 갈래(>=0)가 참.
        Assert.True(third.Effective.TryGetCondition("ln_if", out branch));
        Assert.Equal(1, branch);
    }

    [Fact]
    public void 수동_선택이_자동을_덮는다()
    {
        var manual = new StageBranchSelection();
        manual.SelectCondition("ln_if", 1);

        ConditionSimulation.Result result = Decide(
            Document(), manual, ("favor", "0")); // 자동이라면 0번이 잡힐 값

        Assert.True(result.Effective.TryGetCondition("ln_if", out int branch));
        Assert.Equal(1, branch);
        Assert.DoesNotContain("ln_if", result.AutoBlocks);
    }

    [Fact]
    public void 평가_불가_식이_있으면_그_블록은_판정하지_않는다()
    {
        DialogueResultLine[] document =
        [
            Line(0, "ln_if", ConditionTransitionKind.BeginIf, "visited(\"scene\")"),
            Line(1, "ln_elif", ConditionTransitionKind.BeginElseIf, "favor >= 0"),
            Line(2, "ln_end", ConditionTransitionKind.EndIf),
        ];

        ConditionSimulation.Result result = Decide(
            document, new StageBranchSelection(), ("favor", "0"));

        Assert.False(result.Effective.TryGetCondition("ln_if", out _)); // 근사로 남는다
        Assert.Contains("ln_if", result.UndecidableBlocks);
        Assert.Empty(result.AutoBlocks);
    }

    [Fact]
    public void 앞_갈래의_set이_뒤_블록_판정에_반영된다()
    {
        DialogueResultLine[] document =
        [
            Line(0, "ln_if1", ConditionTransitionKind.BeginIf, "favor >= 0",
                ("favor", SetOperatorKind.Add, "5")), // 참 갈래 안의 set
            Line(1, "ln_end1", ConditionTransitionKind.EndIf),
            Line(2, "ln_if2", ConditionTransitionKind.BeginIf, "favor >= 5"),
            Line(3, "ln_end2", ConditionTransitionKind.EndIf),
        ];

        ConditionSimulation.Result result = Decide(
            document, new StageBranchSelection(), ("favor", "0"));

        // 첫 블록: favor 0 → >=0 참(자동), set +5 적용 → 둘째 블록: favor 5 → >=5 참.
        Assert.True(result.Effective.TryGetCondition("ln_if2", out int branch));
        Assert.Equal(0, branch);
    }

    [Fact]
    public void 건너뛴_갈래의_set은_누적되지_않는다()
    {
        DialogueResultLine[] document =
        [
            Line(0, "ln_if1", ConditionTransitionKind.BeginIf, "favor >= 10",
                ("favor", SetOperatorKind.Add, "100")), // 거짓 갈래 안의 set — 무시돼야 한다
            Line(1, "ln_end1", ConditionTransitionKind.EndIf),
            Line(2, "ln_if2", ConditionTransitionKind.BeginIf, "favor >= 50"),
            Line(3, "ln_end2", ConditionTransitionKind.EndIf),
        ];

        ConditionSimulation.Result result = Decide(
            document, new StageBranchSelection(), ("favor", "0"));

        Assert.True(result.Effective.TryGetCondition("ln_if2", out int branch));
        Assert.Equal(StageBranchSelection.SkipAllBranches, branch); // +100이 안 들어갔다
    }
}
