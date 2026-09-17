using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>스탯 삭제 — 쓰는 곳이 있으면 거절하고 어디인지 말한다</b> (2026-09-17 소유자).
///
/// ⛔ <b>개명과 달리 답이 없다.</b> 개명은 참조가 새 이름을 따라가면 끝이지만, 삭제는
/// <c>trust +1</c>이 적힌 간선을 만났을 때 툴이 정할 수 없다 — 그 항만 뺄지(작가의 의도가
/// 사라진다), 간선을 걷을지(구조가 바뀐다). 조건식은 아예 뺄 수가 없다.
///
/// ⚠ 나중에 진단이 잡아 준다는 것으로는 부족하다. 문구가 *"`스탯` 시트에 없는
/// 스탯키입니다"* — <b>오타를 가정한 안내</b>라 방금 지운 사람에게는 틀린 말이고, 진단은
/// 검증할 때나 보인다. <b>의도가 머릿속에 있는 순간</b>에 말해야 한다.
/// </summary>
public sealed class StatRemoveTests
{
    [Fact]
    public void 아무_데서도_안_쓰면_지워진다()
    {
        ProjectEditor editor = World();

        Assert.True(editor.RemoveChapterStat("ch01", "unused").Applied);
        Assert.DoesNotContain(Chapter(editor).Stats, stat => stat.Key == "unused");
    }

    [Fact]
    public void 쓰는_곳이_있으면_거절하고_몇_곳인지_말한다()
    {
        ProjectEditor editor = World();

        StatRemoveOutcome outcome = editor.RemoveChapterStat("ch01", "trust");

        Assert.False(outcome.Applied);
        Assert.Equal(2, outcome.Uses.Count);
        Assert.Contains("2곳", outcome.Refusal!, StringComparison.Ordinal);
        Assert.Contains(Chapter(editor).Stats, stat => stat.Key == "trust");
    }

    [Fact]
    public void 거절이_어디인지_짚어_준다()
    {
        // ⛔ *"쓰이고 있습니다"*로 끝내면 사람이 챕터를 뒤져야 한다 — 거절이 <b>목록을
        //    들고 와야</b> 그 자체로 정리 안내가 된다.
        ProjectEditor editor = World();

        IReadOnlyList<ChapterUse> uses = editor.RemoveChapterStat("ch01", "trust").Uses;

        ChapterUse edge = Assert.Single(uses, use => use.Kind == ChapterUseKind.Edge);
        Assert.Contains("root → a", edge.Where, StringComparison.Ordinal);
        Assert.Contains("믿는다", edge.Where, StringComparison.Ordinal);
        Assert.Contains("trust +1", edge.Detail, StringComparison.Ordinal);

        ChapterUse condition = Assert.Single(uses, use => use.Kind == ChapterUseKind.Condition);
        Assert.Contains("신뢰높음", condition.Where, StringComparison.Ordinal);
        Assert.Equal("trust >= 3", condition.Detail);
    }

    [Fact]
    public void 이름이_닮은_키는_안_센다()
    {
        // 단순 포함 검사면 `trustworthy`가 `trust`를 쓰는 것으로 세어 못 지우게 막는다.
        ProjectEditor editor = World();
        Chapter(editor).Stats.Add(new ChapterStat("solo", "solo", 0, 0, 10, SourceRow: 0));
        editor.AddChapterCondition("ch01", "닮은것", "solotype >= 1");

        Assert.Empty(editor.FindStatUses("ch01", "solo"));
    }

    [Fact]
    public void 픽스처_시작값은_막지_않고_함께_치운다()
    {
        // 키로 든 사전이고 없으면 초기값으로 읽히며 내보내기에도 안 섞인다 — 잃을 저작이 없다.
        ProjectEditor editor = World();
        Chapter(editor).Fixtures.Add(new ChapterFixture(
            "판", IsActive: true,
            Stats: new Dictionary<string, int> { ["unused"] = 5, ["fatigue"] = 1 },
            Choices: [], SourceRow: 0));

        StatRemoveOutcome outcome = editor.RemoveChapterStat("ch01", "unused");

        Assert.True(outcome.Applied);
        Assert.Equal(1, outcome.Fixtures);

        IReadOnlyDictionary<string, int> stats = Chapter(editor).Fixtures.Single().Stats;
        Assert.False(stats.ContainsKey("unused"));
        Assert.Equal(1, stats["fatigue"]);
    }

    [Fact]
    public void 없는_스탯은_거절한다()
    {
        StatRemoveOutcome outcome = World().RemoveChapterStat("ch01", "없는키");

        Assert.False(outcome.Applied);
        Assert.Empty(outcome.Uses);
    }

    [Fact]
    public void 거절하면_한_글자도_안_건드린다()
    {
        ProjectEditor editor = World();
        string before = ProjectSnapshotCodec.Encode(editor.Project);

        Assert.False(editor.RemoveChapterStat("ch01", "trust").Applied);

        Assert.Equal(before, ProjectSnapshotCodec.Encode(editor.Project));
    }

    [Fact]
    public void 쓰는_곳을_비우면_그제야_지워진다()
    {
        // 거절이 곧 정리 안내다 — 목록을 비우고 다시 누르면 지워진다.
        ProjectEditor editor = World();

        editor.UpdateChapterCondition("ch01", "신뢰높음", "fatigue >= 3");
        editor.UpdateEdge("ch01", "root", "a", statChanges: string.Empty, matchOptionLabel: "믿는다");

        Assert.Empty(editor.FindStatUses("ch01", "trust"));
        Assert.True(editor.RemoveChapterStat("ch01", "trust").Applied);
    }

    // ── 다 — 쓰는 곳을 비우고 지우기 (2026-09-17 소유자) ────────────────────

    [Fact]
    public void 비우고_지우면_간선은_그_항만_빠진다()
    {
        // ⛔ 길 자체를 걷으면 <b>이야기 구조가 바뀐다</b> — 지우려던 것은 스탯이다.
        ProjectEditor editor = World();
        editor.UpdateEdge(
            "ch01", "root", "a", statChanges: "trust +1; fatigue -1", matchOptionLabel: "믿는다");

        StatRemoveOutcome outcome = editor.RemoveChapterStat("ch01", "trust", clearUses: true);

        Assert.True(outcome.Applied);
        Assert.Equal(1, outcome.EdgesCleared);

        ChapterEdge edge = Assert.Single(Chapter(editor).Edges);
        Assert.Equal(["fatigue"], edge.StatChanges.Select(delta => delta.Key));
    }

    [Fact]
    public void 비우고_지우면_조건은_식만_비고_남는다()
    {
        // ⛔ 조건을 통째로 지우면 그것을 쓰는 간선의 표시/해금이 함께 풀려 <b>연쇄가 한
        //    단계 더</b> 간다 — 지우는 사람이 안 본 자리까지 바뀐다.
        ProjectEditor editor = World();

        StatRemoveOutcome outcome = editor.RemoveChapterStat("ch01", "trust", clearUses: true);

        Assert.Equal(1, outcome.ConditionsCleared);

        ChapterCondition condition = Assert.Single(Chapter(editor).Conditions);
        Assert.Equal("신뢰높음", condition.Label);
        Assert.Equal(string.Empty, condition.Expression);
        Assert.False(condition.IsValid);
    }

    [Fact]
    public void 빈_식은_검증이_짚어_사람이_채운다()
    {
        // 비우고 끝내면 아무도 모른다 — 빈 식이 <b>할 일이 남았다</b>고 말해야 한다.
        ProjectEditor editor = World();

        editor.RemoveChapterStat("ch01", "trust", clearUses: true);

        Assert.Contains(
            Chapter(editor).ToGraphModel("ch01", null).Diagnostics,
            item => item.Message.Contains("비어 있습니다", StringComparison.Ordinal));
    }

    [Fact]
    public void 판에_공급된_사본도_Id를_지킨_채_비워진다()
    {
        // ⚠ 갈래가 이 Id로 잇는다. 식이 비면 이미터가 `<<if false>>`로 내므로 갈래는
        //    <b>서 있되 안 탄다</b> — 조용히 꺼져 있는 것이 사라지는 것보다 낫다.
        (ProjectEditor editor, ConditionDefinition supplied) = WorldWithSupply();
        string id = supplied.Id;

        editor.RemoveChapterStat("ch01", "trust", clearUses: true);

        Assert.Equal(id, supplied.Id);
        Assert.Equal(string.Empty, supplied.Expression);
    }

    [Fact]
    public void 되돌리기_한_번에_전부_돌아간다()
    {
        // ⛔ 갈라 두면 스탯만 사라지고 간선·조건은 비워진 <b>반쪽</b>이 남는다.
        ProjectEditor editor = World();
        string before = ProjectSnapshotCodec.Encode(editor.Project);

        editor.RemoveChapterStat("ch01", "trust", clearUses: true);
        editor.Undo();

        Assert.Equal(before, ProjectSnapshotCodec.Encode(editor.Project));
    }

    [Fact]
    public void 안_비우면_여전히_거절한다()
    {
        // 기본값은 <b>가</b>다 — 비우는 것은 사람이 한 번 더 눌러야 한다.
        Assert.False(World().RemoveChapterStat("ch01", "trust").Applied);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <summary>위에 더해, 판과 공급된 조건까지 선 상태.</summary>
    private static (ProjectEditor Editor, ConditionDefinition Supplied) WorldWithSupply()
    {
        ProjectEditor editor = World();
        string fileId = editor.EnsureChapterBoard("ch01");

        ChapterBoardSupply.SupplyChapterConditionsToBoard(
            editor,
            new Vn.Authoring.Definition.GameDefinition(),
            fileId,
            editor.FindChapter("ch01")!.ToGraphModel("ch01", null));

        return (editor, editor.Project.EnumerateNodes().OfType<SetNode>()
            .SelectMany(node => node.Conditions).Single());
    }

    private static ChapterDocument Chapter(ProjectEditor editor) => editor.FindChapter("ch01")!;

    /// <summary>`trust`(간선·조건이 쓴다) · `fatigue` · `unused`(아무도 안 쓴다).</summary>
    private static ProjectEditor World()
    {
        var editor = new ProjectEditor(new StoryProject());
        ChapterDocument chapter = editor.EnsureChapter("ch01");

        chapter.Stats.Add(new ChapterStat("trust", "trust", 0, 0, 10, SourceRow: 0));
        chapter.Stats.Add(new ChapterStat("fatigue", "fatigue", 0, 0, 10, SourceRow: 0));
        chapter.Stats.Add(new ChapterStat("unused", "unused", 0, 0, 10, SourceRow: 0));

        editor.AddEpisode("ch01", "root", title: "root", 0, 0);
        editor.AddEpisode("ch01", "a", title: "a", 0, 0);
        editor.AddEdge("ch01", "root", "a", optionLabel: "믿는다", statChanges: "trust +1");
        editor.AddChapterCondition("ch01", "신뢰높음", "trust >= 3");

        return editor;
    }
}
