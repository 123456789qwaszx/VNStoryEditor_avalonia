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

        IReadOnlyList<StatUse> uses = editor.RemoveChapterStat("ch01", "trust").Uses;

        StatUse edge = Assert.Single(uses, use => use.Kind == StatUseKind.Edge);
        Assert.Contains("root → a", edge.Where, StringComparison.Ordinal);
        Assert.Contains("믿는다", edge.Where, StringComparison.Ordinal);
        Assert.Contains("trust +1", edge.Detail, StringComparison.Ordinal);

        StatUse condition = Assert.Single(uses, use => use.Kind == StatUseKind.Condition);
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

    // ── 기반 ────────────────────────────────────────────────────────────────

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
