using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// W39 — 재생이 타는 실행 경로. 갈래 선택 기준으로 안 타는 라인이 빠지고,
/// 확정 선택된 갈래의 출구는 갈래가 닫히는 순간 경로를 끊으며(합성기와 같은 규칙),
/// 출구를 탄 갈래가 없으면 기본 출구가 다음 노드다. 미선택(근사) 블록의 출구는
/// 태우지 않는다 — 추측하지 않는다.
/// </summary>
public class PlaybackPathTests
{
    private static DialogueResultLine Line(
        int index, string lineId, string text = "대사",
        ConditionTransitionKind? kind = null, string? branchExit = null)
    {
        return new DialogueResultLine(
            index, lineId, 1, "화자", text,
            kind is { } transitionKind
                ? new DialogueResultTransition(transitionKind, "cd_x", null, "x >= 1")
                : null,
            BranchExitTargetNodeId: branchExit);
    }

    private static PlaybackPath.Result Trace(
        IReadOnlyList<DialogueResultLine> lines,
        StageBranchSelection selection,
        string? defaultExit = "nd_default",
        string? cursor = null)
    {
        return PlaybackPath.Trace(
            lines,
            line => line.Transition?.Kind,
            line => line.LineId,
            line => line.BranchExitTargetNodeId,
            defaultExit,
            selection,
            cursor);
    }

    /// <summary>선택 블록: 일반 → [라벨A(출구 nd_a), 대사A] [라벨B, 대사B] → EndChoice → 일반.</summary>
    private static DialogueResultLine[] ChoiceDocument(string? exitA = null, string? exitB = null) =>
    [
        Line(0, "ln_0"),
        Line(1, "ln_labelA", "사과를 고른다", ConditionTransitionKind.BeginChoice, exitA),
        Line(2, "ln_a1", "사과 갈래 대사"),
        Line(3, "ln_labelB", "포도를 고른다", ConditionTransitionKind.BeginNextOption, exitB),
        Line(4, "ln_b1", "포도 갈래 대사"),
        Line(5, "ln_end", "합류 대사", ConditionTransitionKind.EndChoice),
    ];

    [Fact]
    public void 갈래가_없으면_전_라인이_경로이고_기본_출구가_다음이다()
    {
        DialogueResultLine[] document = [Line(0, "ln_0"), Line(1, "ln_1")];

        PlaybackPath.Result path = Trace(document, new StageBranchSelection());

        Assert.Equal(["ln_0", "ln_1"], path.LineIds);
        Assert.Equal("nd_default", path.ExitTargetNodeId);
        Assert.False(path.ExitViaBranch);
    }

    [Fact]
    public void 선택된_갈래의_라인만_경로에_남는다()
    {
        var selection = new StageBranchSelection();
        selection.SelectChoice("ln_labelA", "ln_labelB"); // 포도 갈래

        PlaybackPath.Result path = Trace(ChoiceDocument(), selection);

        Assert.Equal(["ln_0", "ln_labelB", "ln_b1", "ln_end"], path.LineIds);
        Assert.Equal("nd_default", path.ExitTargetNodeId);
    }

    [Fact]
    public void 선택된_갈래의_출구는_갈래가_닫히는_순간_경로를_끊는다()
    {
        var selection = new StageBranchSelection();
        selection.SelectChoice("ln_labelA", "ln_labelA"); // 사과 갈래 (출구 nd_a)

        PlaybackPath.Result path = Trace(ChoiceDocument(exitA: "nd_a"), selection);

        // 사과 갈래의 마지막 라인까지가 경로 — 합류 대사(ln_end)는 실행되지 않는다.
        Assert.Equal(["ln_0", "ln_labelA", "ln_a1"], path.LineIds);
        Assert.Equal("nd_a", path.ExitTargetNodeId);
        Assert.True(path.ExitViaBranch);
    }

    [Fact]
    public void 안_탄_갈래의_출구는_경로에_영향이_없다()
    {
        var selection = new StageBranchSelection();
        selection.SelectChoice("ln_labelA", "ln_labelB"); // 포도 갈래 — 사과 출구는 안 탄다

        PlaybackPath.Result path = Trace(ChoiceDocument(exitA: "nd_a"), selection);

        Assert.Equal(["ln_0", "ln_labelB", "ln_b1", "ln_end"], path.LineIds);
        Assert.Equal("nd_default", path.ExitTargetNodeId);
        Assert.False(path.ExitViaBranch);
    }

    [Fact]
    public void 미선택_블록은_근사대로_전부_타되_출구는_태우지_않는다()
    {
        PlaybackPath.Result path = Trace(ChoiceDocument(exitA: "nd_a"), new StageBranchSelection());

        Assert.Equal(
            ["ln_0", "ln_labelA", "ln_a1", "ln_labelB", "ln_b1", "ln_end"],
            path.LineIds);
        Assert.Equal("nd_default", path.ExitTargetNodeId); // 추측하지 않는다
        Assert.False(path.ExitViaBranch);
    }

    [Fact]
    public void 조건의_elseif_갈래_출구도_그_갈래를_골랐을_때만_탄다()
    {
        DialogueResultLine[] document =
        [
            Line(0, "ln_0"),
            Line(1, "ln_if", "높은 대사", ConditionTransitionKind.BeginIf),
            Line(2, "ln_elif", "낮은 대사", ConditionTransitionKind.BeginElseIf, branchExit: "nd_low"),
            Line(3, "ln_end", "합류", ConditionTransitionKind.EndIf),
            Line(4, "ln_tail", "뒷 대사"),
        ];

        var selection = new StageBranchSelection();
        selection.SelectCondition("ln_if", 1); // elseif 갈래

        PlaybackPath.Result path = Trace(document, selection);

        Assert.Equal(["ln_0", "ln_elif"], path.LineIds); // 합류·뒷 대사는 실행되지 않는다
        Assert.Equal("nd_low", path.ExitTargetNodeId);
        Assert.True(path.ExitViaBranch);

        selection.SelectCondition("ln_if", 0); // if 갈래 — elseif 출구는 안 탄다
        PlaybackPath.Result other = Trace(document, selection);
        Assert.Equal(["ln_0", "ln_if", "ln_end", "ln_tail"], other.LineIds);
        Assert.Equal("nd_default", other.ExitTargetNodeId);
    }

    [Fact]
    public void 전부_거짓_선택이면_갈래_라인_없이_기본_출구다()
    {
        var selection = new StageBranchSelection();
        selection.SelectCondition("ln_if", StageBranchSelection.SkipAllBranches);

        DialogueResultLine[] document =
        [
            Line(0, "ln_0"),
            Line(1, "ln_if", "높은 대사", ConditionTransitionKind.BeginIf, branchExit: "nd_high"),
            Line(2, "ln_end", "합류", ConditionTransitionKind.EndIf),
        ];

        PlaybackPath.Result path = Trace(document, selection);

        Assert.Equal(["ln_0", "ln_end"], path.LineIds);
        Assert.Equal("nd_default", path.ExitTargetNodeId);
    }

    [Fact]
    public void 커서가_있는_갈래는_선택을_덮고_그_출구를_탄다()
    {
        var selection = new StageBranchSelection();
        selection.SelectChoice("ln_labelA", "ln_labelB"); // 포도 선택 상태에서

        PlaybackPath.Result path = Trace(
            ChoiceDocument(exitA: "nd_a"), selection, cursor: "ln_a1"); // 사과 라인을 본다

        Assert.Equal(["ln_0", "ln_labelA", "ln_a1"], path.LineIds);
        Assert.Equal("nd_a", path.ExitTargetNodeId);
    }

    [Fact]
    public void 조건_갈래_출구는_안의_선택지를_다_지나고_나서_끊는다()
    {
        // W54 — 조건 안 선택지 줄도 바깥 조건 갈래에 속하므로, 조건 출구는
        // 선택지를 다 지난 뒤(EndIf 라인 앞)에 실행된다.
        DialogueResultLine[] document =
        [
            Line(0, "ln_0"),
            Line(1, "ln_if", "조건", ConditionTransitionKind.BeginIf, branchExit: "nd_high"),
            Line(2, "ln_labelA", "사과", ConditionTransitionKind.BeginChoice),
            Line(3, "ln_a1", "사과 본문"),
            Line(4, "ln_join", "합류", ConditionTransitionKind.EndChoice),
            Line(5, "ln_endif", "끝", ConditionTransitionKind.EndIf),
            Line(6, "ln_tail", "뒷 대사"),
        ];

        var selection = new StageBranchSelection();
        selection.SelectCondition("ln_if", 0);
        selection.SelectChoice("ln_labelA", "ln_labelA");

        PlaybackPath.Result path = Trace(document, selection);

        // 선택지 본문·합류까지 전부 탄 뒤에야 조건 출구가 경로를 끊는다.
        Assert.Equal(["ln_0", "ln_if", "ln_labelA", "ln_a1", "ln_join"], path.LineIds);
        Assert.Equal("nd_high", path.ExitTargetNodeId);
        Assert.True(path.ExitViaBranch);
    }

    [Fact]
    public void 문서가_갈래로_끝나_닫히지_않아도_출구를_버리지_않는다()
    {
        DialogueResultLine[] document =
        [
            Line(0, "ln_0"),
            Line(1, "ln_labelA", "사과", ConditionTransitionKind.BeginChoice, branchExit: "nd_a"),
            Line(2, "ln_a1", "사과 대사"), // EndChoice 없이 문서 끝
        ];

        var selection = new StageBranchSelection();
        selection.SelectChoice("ln_labelA", "ln_labelA");

        PlaybackPath.Result path = Trace(document, selection);

        Assert.Equal(["ln_0", "ln_labelA", "ln_a1"], path.LineIds);
        Assert.Equal("nd_a", path.ExitTargetNodeId);
        Assert.True(path.ExitViaBranch);
    }
}
