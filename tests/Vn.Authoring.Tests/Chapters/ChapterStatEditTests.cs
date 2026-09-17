using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>스탯과 조건을 툴에서 정의한다</b> (2026-09-17 소유자: *"이제 엑셀이 아닌 챕터그래프에서
/// 직접 조건과 스탯을 정의하려고 합니다"*).
///
/// R-A~R-F가 이미 워크북을 <b>산출물</b>로 만들었고 스탯·조건은 <c>ChapterDocument</c>에
/// 산다 — 그래서 이건 새 정본이 아니라 <b>마지막 남은 창구</b>를 옮기는 일이다.
/// </summary>
public sealed class ChapterStatEditTests
{
    [Fact]
    public void 스탯을_세운다()
    {
        ProjectEditor editor = World();

        ChapterStat stat = editor.AddChapterStat(
            "ch01", "courage", displayName: "용기", initial: 2, minimum: 0, maximum: 5);

        Assert.Equal("용기", stat.DisplayName);
        Assert.Contains(Chapter(editor).Stats, item => item.Key == "courage");
    }

    [Fact]
    public void 표시이름을_안_주면_키를_쓴다()
    {
        Assert.Equal("courage", World().AddChapterStat("ch01", "courage").DisplayName);
    }

    [Fact]
    public void 키가_겹치면_거절한다()
    {
        // ⛔ 같은 키가 둘이면 어느 행이 이기는지 말할 수 없다 — 진단으로 넘길 상태가 아니다.
        ProjectEditor editor = World();

        Assert.Throws<InvalidOperationException>(() => editor.AddChapterStat("ch01", "trust"));
        Assert.Single(Chapter(editor).Stats, stat => stat.Key == "trust");
    }

    [Fact]
    public void 공백만_다른_키는_이제_서로_다른_스탯이다()
    {
        // ⚠ <b>뒤집혔다</b> (2026-09-17, 런타임 회신 §6.1). `호감 도`와 `호감_도`는 한때
        //    내보낼 때 정규화돼 게임에서 하나가 됐고, 그래서 막았다. `stat("키")`의 인자가
        //    <b>문자열 리터럴</b>이 되면서 정규화가 없어졌으니 막을 이유도 없다.
        ProjectEditor editor = World();
        editor.AddChapterStat("ch01", "호감_도");

        editor.AddChapterStat("ch01", "호감 도");

        Assert.Equal(2, Chapter(editor).Stats.Count(stat => stat.Key.StartsWith("호감", StringComparison.Ordinal)));
    }

    [Fact]
    public void 공백이_든_키가_대사에_원본_그대로_나간다()
    {
        // ⛔ <b>진행 JSON은 키를 원본 그대로 낸다.</b> 대사만 정규화하면 둘이 갈리고,
        //    산출도 컴파일도 통과한 뒤 <b>그 대사가 재생될 때</b> 런타임에서 터진다
        //    (런타임 회신 §6.1이 잡았다).
        ProjectEditor editor = World();
        editor.AddChapterStat("ch01", "호감도 (윌로우)");
        editor.AddChapterCondition("ch01", "친함", "호감도 (윌로우) >= 3");

        Assert.Equal(
            "stat(\"호감도 (윌로우)\") >= 3",
            ConditionYarnTranslator.Translate(
                Chapter(editor).ToGraphModel("ch01", null).Conditions.Single(item => item.Label == "친함")).Yarn);
    }

    [Fact]
    public void 경계가_어긋나도_막지_않고_진단에_맡긴다()
    {
        // ⚠ <c>ChapterDocument</c>가 이미 짚는다. 여기서만 막으면 툴로 만든 것과 워크북에서
        //    읽은 것이 <b>서로 다른 규칙</b>을 살게 되고, 값을 채워 가는 중에 막힌다.
        ProjectEditor editor = World();

        editor.AddChapterStat("ch01", "이상함", initial: 9, minimum: 3, maximum: 1);

        Assert.Contains(Chapter(editor).Stats, stat => stat.Key == "이상함");
        Assert.Contains(
            Chapter(editor).ToGraphModel("ch01", null).Diagnostics,
            item => item.Message.Contains("이상함", StringComparison.Ordinal));
    }

    [Fact]
    public void 값과_표시이름을_고친다()
    {
        ProjectEditor editor = World();

        editor.UpdateChapterStat("ch01", "trust", displayName: "신뢰도", maximum: 20);

        ChapterStat stat = Chapter(editor).Stats.Single(item => item.Key == "trust");
        Assert.Equal("신뢰도", stat.DisplayName);
        Assert.Equal(20, stat.Maximum);

        // 안 준 것은 그대로다 — `UpdateEpisode`와 같은 손버릇.
        Assert.Equal(0, stat.Minimum);
    }

    [Fact]
    public void 고치기로는_키를_못_바꾼다()
    {
        // ⛔ 키를 바꾸는 것은 참조를 끌고 가는 일이라 `RenameChapterStat`의 몫이다 —
        //    여기서 슬쩍 바꾸면 간선·조건·픽스처가 옛 이름을 가리킨 채 남는다.
        Assert.DoesNotContain(
            typeof(ProjectEditor).GetMethod(nameof(ProjectEditor.UpdateChapterStat))!.GetParameters(),
            parameter => parameter.Name is "newKey" or "key" && parameter.Position > 1);
    }

    // ── 조건 삭제 ───────────────────────────────────────────────────────────

    [Fact]
    public void 아무_데서도_안_쓰는_조건은_지워진다()
    {
        ProjectEditor editor = World();
        editor.AddChapterCondition("ch01", "안쓰는것", "trust >= 9");

        Assert.True(editor.RemoveChapterCondition("ch01", "안쓰는것").Applied);
        Assert.DoesNotContain(Chapter(editor).Conditions, item => item.Label == "안쓰는것");
    }

    [Fact]
    public void 간선이_쓰는_조건은_거절하고_어디인지_짚는다()
    {
        ProjectEditor editor = World();
        editor.UpdateEdge("ch01", "root", "a", conditionLabel: "신뢰높음", matchOptionLabel: "믿는다");

        ChapterConditionRemoveOutcome outcome = editor.RemoveChapterCondition("ch01", "신뢰높음");

        Assert.False(outcome.Applied);
        ChapterUse use = Assert.Single(outcome.Uses);
        Assert.Equal(ChapterUseKind.Edge, use.Kind);
        Assert.Contains("root → a", use.Where, StringComparison.Ordinal);
        Assert.Equal("해금조건", use.Detail);
    }

    [Fact]
    public void 표시조건과_해금조건을_함께_쓰면_한_줄로_말한다()
    {
        ProjectEditor editor = World();
        editor.UpdateEdge(
            "ch01", "root", "a",
            conditionLabel: "신뢰높음", visibleConditionLabel: "신뢰높음", matchOptionLabel: "믿는다");

        Assert.Equal(
            "표시조건 · 해금조건",
            Assert.Single(editor.FindConditionUses("ch01", "신뢰높음")).Detail);
    }

    [Fact]
    public void 아무도_안_쓰면_판의_공급_사본도_함께_걷는다()
    {
        // 남겨 두면 <b>정의 없는 조건</b>이 드롭다운에 계속 보인다.
        ProjectEditor editor = WorldWithBoard();

        ChapterConditionRemoveOutcome outcome = editor.RemoveChapterCondition("ch01", "신뢰높음");

        Assert.True(outcome.Applied);
        Assert.Equal(1, outcome.SuppliedRemoved);
        Assert.Empty(editor.Project.EnumerateNodes().OfType<SetNode>()
            .SelectMany(node => node.Conditions));
    }

    [Fact]
    public void 대사_갈래가_붙들고_있으면_거절한다()
    {
        // ⭐ 여기가 진단이 못 잡는 자리다. 챕터 조건이 사라져도 공급 조건은 남고, 갈래는
        //    계속 살아 있는데 <b>무엇을 묻는지는 아무 데도 없다</b>.
        ProjectEditor editor = WorldWithBoard();

        ConditionDefinition supplied = editor.Project.EnumerateNodes().OfType<SetNode>()
            .SelectMany(node => node.Conditions).Single();

        string fileId = editor.Project.Files.Single(file => file.Name == "ch01").Id;
        DialogueNode node = editor.AddDialogueNode(fileId, 0, 0, "root");
        string lineId = editor.Project.FindScript(node.ScriptId)!.ActiveLines.First().Id;

        editor.SetLineTransition(
            node.Id,
            lineId,
            new LineConditionTransition(ConditionTransitionKind.BeginIf, supplied.Id));

        ChapterConditionRemoveOutcome outcome = editor.RemoveChapterCondition("ch01", "신뢰높음");

        Assert.False(outcome.Applied);
        ChapterUse use = Assert.Single(outcome.Uses, item => item.Kind == ChapterUseKind.DialogueBranch);
        Assert.Contains("root", use.Where, StringComparison.Ordinal);
    }

    [Fact]
    public void 거절하면_한_글자도_안_건드린다()
    {
        ProjectEditor editor = World();
        editor.UpdateEdge("ch01", "root", "a", conditionLabel: "신뢰높음", matchOptionLabel: "믿는다");

        string before = ProjectSnapshotCodec.Encode(editor.Project);

        Assert.False(editor.RemoveChapterCondition("ch01", "신뢰높음").Applied);
        Assert.Equal(before, ProjectSnapshotCodec.Encode(editor.Project));
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static ChapterDocument Chapter(ProjectEditor editor) => editor.FindChapter("ch01")!;

    private static ProjectEditor World()
    {
        var editor = new ProjectEditor(new StoryProject());
        ChapterDocument chapter = editor.EnsureChapter("ch01");

        chapter.Stats.Add(new ChapterStat("trust", "trust", 0, 0, 10, SourceRow: 0));

        editor.AddEpisode("ch01", "root", title: "root", 0, 0);
        editor.AddEpisode("ch01", "a", title: "a", 0, 0);
        editor.AddEdge("ch01", "root", "a", optionLabel: "믿는다");
        editor.AddChapterCondition("ch01", "신뢰높음", "trust >= 3");

        return editor;
    }

    /// <summary>위에 더해, 판과 공급된 조건까지 선 상태.</summary>
    private static ProjectEditor WorldWithBoard()
    {
        ProjectEditor editor = World();
        string fileId = editor.EnsureChapterBoard("ch01");

        ChapterBoardSupply.SupplyChapterConditionsToBoard(
            editor,
            new Vn.Authoring.Definition.GameDefinition(),
            fileId,
            editor.FindChapter("ch01")!.ToGraphModel("ch01", null));

        return editor;
    }
}
