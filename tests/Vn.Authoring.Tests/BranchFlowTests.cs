using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// W35 — 갈래 인식. 선택된 갈래의 라인만 접히고, 미선택 블록은 기존 근사(전부) +
/// Unresolved 표시로 남으며, 커서가 있는 갈래는 선택을 덮는다(보고 있는 라인은 사라지지 않는다).
/// </summary>
public class BranchFlowTests
{
    private static DialogueResultLine Line(
        int index, string lineId, string text = "대사",
        ConditionTransitionKind? kind = null, string? name = null)
    {
        return new DialogueResultLine(
            index, lineId, 1, "화자", text,
            kind is { } transitionKind
                ? new DialogueResultTransition(transitionKind, "cd_x", name, "x >= 1")
                : null);
    }

    /// <summary>선택 블록: 일반 → [라벨A, 대사A] [라벨B, 대사B] → EndChoice 라인 → 일반.</summary>
    private static DialogueResultLine[] ChoiceDocument() =>
    [
        Line(0, "ln_0"),
        Line(1, "ln_labelA", "사과를 고른다", ConditionTransitionKind.BeginChoice),
        Line(2, "ln_a1", "사과 갈래 대사"),
        Line(3, "ln_labelB", "포도를 고른다", ConditionTransitionKind.BeginNextOption),
        Line(4, "ln_b1", "포도 갈래 대사"),
        Line(5, "ln_end", "합류 대사", ConditionTransitionKind.EndChoice),
    ];

    private static BranchFlow.Analysis<DialogueResultLine> Analyze(
        IReadOnlyList<DialogueResultLine> lines, StageBranchSelection selection, string? cursor)
    {
        return BranchFlow.Analyze(
            lines,
            line => line.Transition?.Kind,
            line => line.LineId,
            // BranchAwareLines와 같은 라벨 규칙: 선택지=버튼 텍스트, 조건=이름(없으면 식).
            line => line.Transition?.Kind is ConditionTransitionKind.BeginChoice
                or ConditionTransitionKind.BeginNextOption
                ? line.Text
                : line.Transition?.ConditionName ?? line.Transition?.Expression ?? string.Empty,
            selection,
            cursor);
    }

    [Fact]
    public void 미선택_블록은_전부_적용하고_근사임을_표시한다()
    {
        BranchFlow.Analysis<DialogueResultLine> analysis =
            Analyze(ChoiceDocument(), new StageBranchSelection(), cursor: "ln_end");

        Assert.All(analysis.Lines, line => Assert.True(line.Taken || line.Unresolved));
        Assert.True(analysis.Lines[2].Unresolved);  // 갈래 안 라인
        Assert.False(analysis.Lines[0].Unresolved); // 일반 흐름
        Assert.False(analysis.Lines[5].Unresolved); // End 라인부터 일반

        BranchFlow.Block block = Assert.Single(analysis.Blocks);
        Assert.True(block.IsChoice);
        Assert.Null(block.SelectedBranch);
        Assert.Equal(["사과를 고른다", "포도를 고른다"], block.Branches.Select(branch => branch.Label));
    }

    [Fact]
    public void 옵션을_고르면_그_갈래만_접힌다()
    {
        var selection = new StageBranchSelection();
        selection.SelectChoice("ln_labelA", "ln_labelB"); // 포도 갈래 선택

        BranchFlow.Analysis<DialogueResultLine> analysis =
            Analyze(ChoiceDocument(), selection, cursor: "ln_end");

        Assert.False(analysis.Lines[1].Taken); // 사과 라벨
        Assert.False(analysis.Lines[2].Taken); // 사과 대사
        Assert.True(analysis.Lines[3].Taken);  // 포도 라벨
        Assert.True(analysis.Lines[4].Taken);  // 포도 대사
        Assert.True(analysis.Lines[5].Taken);  // 합류
        Assert.All(analysis.Lines, line => Assert.False(line.Unresolved)); // 근사 아님

        Assert.Equal(1, Assert.Single(analysis.Blocks).SelectedBranch);
    }

    [Fact]
    public void 커서가_있는_갈래가_선택을_덮는다()
    {
        var selection = new StageBranchSelection();
        selection.SelectChoice("ln_labelA", "ln_labelB"); // 포도 선택 상태에서

        BranchFlow.Analysis<DialogueResultLine> analysis =
            Analyze(ChoiceDocument(), selection, cursor: "ln_a1"); // 사과 갈래 라인을 본다

        Assert.True(analysis.Lines[2].Taken);  // 보고 있는 라인은 접힌다
        Assert.False(analysis.Lines[4].Taken); // 포도는 이번엔 아님
        Assert.Equal(0, Assert.Single(analysis.Blocks).SelectedBranch);
    }

    /// <summary>W54: 조건 갈래 안에서 닫히는 선택 블록.</summary>
    private static DialogueResultLine[] NestedDocument() =>
    [
        Line(0, "ln_0"),
        Line(1, "ln_if", "조건", ConditionTransitionKind.BeginIf, name: "호감 높음"),
        Line(2, "ln_labelA", "사과", ConditionTransitionKind.BeginChoice),
        Line(3, "ln_a1", "사과 본문"),
        Line(4, "ln_labelB", "포도", ConditionTransitionKind.BeginNextOption),
        Line(5, "ln_b1", "포도 본문"),
        Line(6, "ln_join", "합류", ConditionTransitionKind.EndChoice),
        Line(7, "ln_end", "끝", ConditionTransitionKind.EndIf),
    ];

    [Fact]
    public void 조건_안_선택지는_감싼_블록_전부의_선택을_따른다()
    {
        // W54 — 접히려면 조건도 그 갈래여야 하고, 선택지도 그 옵션이어야 한다.
        var selection = new StageBranchSelection();
        selection.SelectCondition("ln_if", 0);
        selection.SelectChoice("ln_labelA", "ln_labelB"); // 포도

        BranchFlow.Analysis<DialogueResultLine> analysis =
            Analyze(NestedDocument(), selection, cursor: null);

        Assert.Equal(2, analysis.Blocks.Count); // 조건 블록 + 선택 블록
        Assert.False(analysis.Lines[3].Taken);  // 사과 본문 — 다른 옵션
        Assert.True(analysis.Lines[5].Taken);   // 포도 본문
        Assert.True(analysis.Lines[6].Taken);   // 합류 — 선택이 닫혀 조건 안
        Assert.All(analysis.Lines, line => Assert.False(line.Unresolved));

        // 조건을 건너뛰면 안의 선택지도 통째로 안 탄다.
        selection.SelectCondition("ln_if", StageBranchSelection.SkipAllBranches);
        BranchFlow.Analysis<DialogueResultLine> skipped =
            Analyze(NestedDocument(), selection, cursor: null);
        Assert.False(skipped.Lines[5].Taken);
        Assert.False(skipped.Lines[6].Taken);
        Assert.True(skipped.Lines[0].Taken); // 바깥은 그대로

        // 조건이 미선택이면 안의 선택 결과가 있어도 근사 표시가 남는다.
        var partial = new StageBranchSelection();
        partial.SelectChoice("ln_labelA", "ln_labelB");
        BranchFlow.Analysis<DialogueResultLine> approx =
            Analyze(NestedDocument(), partial, cursor: null);
        Assert.True(approx.Lines[5].Taken);
        Assert.True(approx.Lines[5].Unresolved);
    }

    [Fact]
    public void 중첩_안의_커서는_감싼_갈래_전부를_덮는다()
    {
        // 포도 본문을 보고 있으면 조건 갈래와 포도 옵션이 함께 선택으로 덮인다.
        BranchFlow.Analysis<DialogueResultLine> analysis =
            Analyze(NestedDocument(), new StageBranchSelection(), cursor: "ln_b1");

        Assert.True(analysis.Lines[5].Taken);
        Assert.False(analysis.Lines[5].Unresolved);
        Assert.False(analysis.Lines[3].Taken); // 사과 본문은 밀려난다
        Assert.Equal(0, analysis.Blocks[0].SelectedBranch); // 조건 갈래
        Assert.Equal(1, analysis.Blocks[1].SelectedBranch); // 포도 옵션
    }

    [Fact]
    public void 조건_블록은_참_갈래나_전부_거짓을_고를_수_있다()
    {
        DialogueResultLine[] document =
        [
            Line(0, "ln_0"),
            Line(1, "ln_if", "호감 높은 대사", ConditionTransitionKind.BeginIf, name: "호감 높음"),
            Line(2, "ln_if2", "이어지는 대사"),
            Line(3, "ln_elif", "호감 낮은 대사", ConditionTransitionKind.BeginElseIf, name: "호감 낮음"),
            Line(4, "ln_end", "합류", ConditionTransitionKind.EndIf),
        ];

        var selection = new StageBranchSelection();
        selection.SelectCondition("ln_if", 0);

        BranchFlow.Analysis<DialogueResultLine> first = Analyze(document, selection, cursor: "ln_end");
        Assert.True(first.Lines[1].Taken);
        Assert.True(first.Lines[2].Taken);
        Assert.False(first.Lines[3].Taken);
        Assert.Equal(["호감 높음", "호감 낮음"], Assert.Single(first.Blocks).Branches.Select(branch => branch.Label));

        selection.SelectCondition("ln_if", StageBranchSelection.SkipAllBranches);

        BranchFlow.Analysis<DialogueResultLine> skipped = Analyze(document, selection, cursor: "ln_end");
        Assert.False(skipped.Lines[1].Taken);
        Assert.False(skipped.Lines[2].Taken);
        Assert.False(skipped.Lines[3].Taken);
        Assert.True(skipped.Lines[4].Taken); // 합류는 일반 흐름
    }

    [Fact]
    public void 칩_순환은_갈래들을_돌고_조건은_건너뜀을_거쳐_근사로_돌아온다()
    {
        var selection = new StageBranchSelection();
        var block = new BranchFlow.Block(
            "ln_if", IsChoice: false,
            [new BranchFlow.Branch("ln_if", "참"), new BranchFlow.Branch("ln_elif", "다른 참")],
            SelectedBranch: null);

        selection.Cycle(block); // → 0
        Assert.True(selection.TryGetCondition("ln_if", out int index) && index == 0);

        selection.Cycle(block); // → 1
        Assert.True(selection.TryGetCondition("ln_if", out index) && index == 1);

        selection.Cycle(block); // → 건너뜀
        Assert.True(selection.TryGetCondition("ln_if", out index) &&
            index == StageBranchSelection.SkipAllBranches);

        selection.Cycle(block); // → 미선택(근사)
        Assert.False(selection.TryGetCondition("ln_if", out _));
    }

    [Fact]
    public void 갈래_인식_폴드_입력은_선택_라인까지_선택_갈래만_담는다()
    {
        DialogueResult dialogue = new(
            new ResultIdentity("rs_bf", 1, DialogueResult.CurrentSchemaVersion, "sha256:test"),
            "nd_bf", "장면", "sc_bf", 1, "ko-KR",
            ChoiceDocument(),
            Array.Empty<DialogueResultAssignment>(),
            null,
            DateTimeOffset.UnixEpoch);

        var selection = new StageBranchSelection();
        selection.SelectChoice("ln_labelA", "ln_labelA"); // 사과 갈래

        BranchAwareLines.Result result = BranchAwareLines.UpTo(
            dialogue, Array.Empty<PresentationResultBinding>(), "ln_end", selection);

        Assert.Equal(
            ["ln_0", "ln_labelA", "ln_a1", "ln_end"],
            result.FoldLines.Select(line => line.LineId));
        Assert.All(result.FoldLines, line => Assert.False(line.HasBranchTransition)); // 근사 아님
        Assert.Equal(result.FoldLines.Select(line => line.LineId), result.TakenLines.Select(line => line.LineId));

        BranchFlow.Block block = Assert.Single(result.Blocks);
        Assert.Equal("ln_labelA", block.BlockLineId);

        // 커서가 블록 앞이면 블록은 아직 칩에 없다.
        BranchAwareLines.Result before = BranchAwareLines.UpTo(
            dialogue, Array.Empty<PresentationResultBinding>(), "ln_0", selection);
        Assert.Empty(before.Blocks);
    }

    [Fact]
    public void 미선택이면_폴드_입력이_기존_근사와_같고_근사_표시가_남는다()
    {
        DialogueResult dialogue = new(
            new ResultIdentity("rs_bf2", 1, DialogueResult.CurrentSchemaVersion, "sha256:test"),
            "nd_bf2", "장면", "sc_bf2", 1, "ko-KR",
            ChoiceDocument(),
            Array.Empty<DialogueResultAssignment>(),
            null,
            DateTimeOffset.UnixEpoch);

        BranchAwareLines.Result result = BranchAwareLines.UpTo(
            dialogue, Array.Empty<PresentationResultBinding>(), "ln_end", new StageBranchSelection());

        // 기존 LinesUpTo와 같은 라인 집합(전부) — 갈래 라인에만 근사 표시.
        Assert.Equal(
            MiniStageFold.LinesUpTo(dialogue, Array.Empty<PresentationResultBinding>(), "ln_end")
                .Select(line => line.LineId),
            result.FoldLines.Select(line => line.LineId));
        Assert.True(result.FoldLines.First(line => line.LineId == "ln_a1").HasBranchTransition);
        Assert.False(result.FoldLines.First(line => line.LineId == "ln_0").HasBranchTransition);
    }
}
