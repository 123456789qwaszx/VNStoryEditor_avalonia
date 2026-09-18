using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>분기는 통로, 조건은 다녀온 쪽</b> (2026-09-18 소유자: *"분기는 뚫어뒀다면 반드시
/// 행해지는 통로이고, 그 통로로 갔더니 조건으로 막혀있다는 게 논리상 맞고 이해하기
/// 편합니다"*).
///
/// <b>왜 이 모양인가</b> — 소유자: *"If, ElseIf, EndIf를 달아두는 것 보다는 Detour로 조건
/// 분기가 일어나는 마커를 끼워두고 그곳에서 조건과 내용을 추가한다는게 유지보수 측면에서
/// 얼마나 유리한지 알것입니다."* 부르는 쪽 대본에는 <c>&lt;&lt;detour&gt;&gt;</c> 한 줄뿐이고,
/// 짝을 맞춰야 하는 <c>If</c>/<c>EndIf</c>가 남의 대본에 흩어지지 않는다.
///
/// ⭐ <b>성립하면 돈다</b>(허가). 모델에 부정이 없으므로 이 극성이 곧 저장 형태다.
/// </summary>
public sealed class BranchEntryConditionTests
{
    [Fact]
    public void 아무_조건도_없는_분기는_여기서_처음_걸_수_있다()
    {
        (ProjectEditor editor, _, DialogueNode branch) = World();

        BranchEntryCondition.State before = BranchEntryCondition.Read(editor.Project, branch.Id);

        Assert.Null(before.Label);
        Assert.True(before.Editable);

        Assert.Null(BranchEntryCondition.Set(editor, branch.Id, "신뢰높음"));

        Assert.Equal("신뢰높음", BranchEntryCondition.Read(editor.Project, branch.Id).Label);
    }

    [Fact]
    public void 걸린_조건은_본문을_통째로_감싼다()
    {
        // 첫 줄에 BeginIf, 마지막 줄 뒤에 EndIf — 조건이 거짓이면 본문이 통째로 건너뛰이고,
        // 노드 끝이 곧 <<return>>이라 곧바로 돌아간다.
        (ProjectEditor editor, _, DialogueNode branch) = World();

        BranchEntryCondition.Set(editor, branch.Id, "신뢰높음");

        string firstLineId = editor.Project.FindScript(branch.ScriptId)!.ActiveLines.First().Id;

        LineConditionTransition opened =
            Assert.Single(branch.FindExtension(firstLineId)!.Transitions);
        LineConditionTransition closed = Assert.Single(branch.TrailingTransitions);

        Assert.Equal(ConditionTransitionKind.BeginIf, opened.Kind);
        Assert.Equal(ConditionTransitionKind.EndIf, closed.Kind);
    }

    [Fact]
    public void 조건을_거는_것도_무르는_것도_한_번이다()
    {
        // ⚠ 줄과 꼬리를 따로 쓰면 한 번 물렀을 때 BeginIf만 남은 짝 없는 대본이 선다.
        (ProjectEditor editor, _, DialogueNode branch) = World();
        string branchId = branch.Id;

        BranchEntryCondition.Set(editor, branchId, "신뢰높음");
        editor.Undo();

        // ⚠ 되돌리기는 스냅샷을 <b>다시 푼다</b> — 들고 있던 노드 객체는 낡았다. Id로 다시 찾는다.
        Assert.Null(BranchEntryCondition.Read(editor.Project, branchId).Label);
        Assert.Empty(editor.Project.FindDialogue(branchId)!.TrailingTransitions);
    }

    [Fact]
    public void 조건을_비우면_걷힌다()
    {
        (ProjectEditor editor, _, DialogueNode branch) = World();

        BranchEntryCondition.Set(editor, branch.Id, "신뢰높음");
        Assert.Null(BranchEntryCondition.Set(editor, branch.Id, null));

        Assert.Null(BranchEntryCondition.Read(editor.Project, branch.Id).Label);
        Assert.Empty(branch.TrailingTransitions);
    }

    [Fact]
    public void 챕터에_없는_조건은_추측해_잇지_않는다()
    {
        // 오타가 조용히 새 조건이 되면, 영영 참이 안 되는 분기가 판에 남는다.
        (ProjectEditor editor, _, DialogueNode branch) = World();

        string? failure = BranchEntryCondition.Set(editor, branch.Id, "신뢰노픔");

        Assert.NotNull(failure);
        Assert.Null(BranchEntryCondition.Read(editor.Project, branch.Id).Label);
    }

    [Fact]
    public void 손으로_지은_조건_구조는_여기서_안_건드린다()
    {
        // ⛔ 작가가 대사 편집기에서 갈래를 얹었다면, 그 구조를 덮는 순간 짝이 어긋난 대본이
        //    된다. 읽기만 하고 사유를 말한다.
        (ProjectEditor editor, _, DialogueNode branch) = World();

        string firstLineId = editor.Project.FindScript(branch.ScriptId)!.ActiveLines.First().Id;
        editor.SetLineTransitions(branch.Id, firstLineId, [LineConditionTransition.BeginChoice()]);

        BranchEntryCondition.State state = BranchEntryCondition.Read(editor.Project, branch.Id);

        Assert.False(state.Editable);
        Assert.NotNull(state.Blocked);
        Assert.NotNull(BranchEntryCondition.Set(editor, branch.Id, "신뢰높음"));
    }

    [Fact]
    public void 부르는_쪽은_여전히_조건_없는_detour_한_줄이다()
    {
        // ⛔ 여기가 이 설계의 요점이다 — 조건을 걸어도 부르는 대본은 안 바뀐다.
        (ProjectEditor editor, DialogueNode caller, DialogueNode branch) = World();

        BranchEntryCondition.Set(editor, branch.Id, "신뢰높음");

        string yarn = DocumentPreviewFormatter.Format(
            WorkingDialoguePreview.Compose(editor.Project, caller.Id));

        Assert.Contains($"<<detour {branch.Id}>>", yarn, StringComparison.Ordinal);
        Assert.DoesNotContain("<<if", yarn, StringComparison.Ordinal);
    }

    [Fact]
    public void 다녀온_쪽_대본은_조건으로_감싸여_나간다()
    {
        // 조건이 거짓이면 본문이 통째로 건너뛰이고, 노드 끝이 곧 돌아가는 자리다 —
        // <c>&lt;&lt;detour&gt;&gt;</c>로 들어왔으므로 끝나면 부른 자리로 돌아간다.
        //
        // ⚠ 식은 챕터 조건의 <b>Yarn 번역</b>이다(`trust >= 3` → `stat("trust") >= 3`).
        //   기획자가 시트에 적는 글자와 대본에 나가는 글자는 서로 다르고, 옮기는 자리는
        //   `ConditionYarnTranslator` 하나다.
        (ProjectEditor editor, _, DialogueNode branch) = World();

        BranchEntryCondition.Set(editor, branch.Id, "신뢰높음");

        string yarn = DocumentPreviewFormatter.Format(
            WorkingDialoguePreview.Compose(editor.Project, branch.Id));

        Assert.Contains("<<if stat(\"trust\") >= 3>>", yarn, StringComparison.Ordinal);
        Assert.Contains("<<endif>>", yarn, StringComparison.Ordinal);
    }

    /// <summary>
    /// 분기 하나를 뚫어 둔 챕터. `조건` 시트의 `신뢰높음`이 판에 공급돼 있다.
    ///
    /// ⚠ 카드를 따로 세우지 않는다 — <c>AddEpisode</c>가 이미 그 챕터 판에 카드를 만든다
    /// (2026-09-18에 두 길이 하나로 합쳐졌다). 또 만들면 이름이 같은 카드가 두 장 서고,
    /// 이름으로 찾는 코드가 <b>다른 장</b>을 집는다.
    /// </summary>
    private static (ProjectEditor Editor, DialogueNode Caller, DialogueNode Branch) World()
    {
        var editor = new ProjectEditor(new StoryProject());

        editor.EnsureChapter("ch01");
        string fileId = editor.EnsureChapterBoard("ch01");
        editor.AddEpisode("ch01", "root", title: "root", 0, 0);

        ChapterDocument chapter = editor.FindChapter("ch01")!;
        chapter.Stats.Add(new ChapterStat("trust", "신뢰", 0, 0, 10, SourceRow: 2));
        editor.AddChapterCondition("ch01", "신뢰높음", "trust >= 3");

        ChapterBoardSupply.SupplyChapterConditionsToBoard(
            editor, Definition.GameDefinition.Empty, fileId,
            chapter.ToGraphModel("ch01.xlsx"));

        DialogueNode caller = EpisodeNaming.CardFor(editor.Project, "ch01", "root")!;
        string lineId = editor.Project.FindScript(caller.ScriptId)!.ActiveLines.First().Id;

        return (editor, caller, editor.AddBranchMarker(caller.Id, lineId));
    }
}
