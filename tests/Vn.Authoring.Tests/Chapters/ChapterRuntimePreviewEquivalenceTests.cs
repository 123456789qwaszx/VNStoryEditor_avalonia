using Ked.Progression;
using Vn.Authoring.Chapters;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests.Chapters;

public sealed class ChapterRuntimePreviewEquivalenceTests
{
    [Fact]
    public void 표시실패는_제외하고_해금실패는_잠근_채_보인다()
    {
        ChapterGraphModel chapter = ChoiceChapter(initial: 0);
        var run = new ChapterRunState(chapter);

        ChapterRunAdvance advance = Assert.IsType<ChapterRunAdvance>(run.Resolve("ep1"));

        Assert.Equal(ChapterAdvanceKind.AwaitPlayerChoice, advance.Kind);
        Assert.Equal(1, advance.HiddenCount);
        Assert.Equal(["잠김", "열림"], advance.Options.Select(x => x.Edge.OptionLabel).ToArray());
        Assert.False(advance.Options[0].IsSelectable);
        Assert.Equal("아직 부족함", advance.Options[0].LockedReason);
        Assert.True(advance.Options[1].IsSelectable);
    }

    [Fact]
    public void 표시됐어도_고를_수_있는_간선이_없으면_챕터가_끝난다()
    {
        ChapterGraphModel chapter = ChoiceChapter(initial: 0, includeOpen: false);
        ChapterRunAdvance advance = Assert.IsType<ChapterRunAdvance>(
            new ChapterRunState(chapter).Resolve("ep1"));

        Assert.Equal(ChapterAdvanceKind.ChapterEnded, advance.Kind);
        Assert.Empty(advance.Options);
    }

    [Fact]
    public void Auto는_선택_UI가_아니라_자동_진행으로_판정된다()
    {
        ChapterEdge auto = new ChapterEdge("ep1", "ep2", null, null, null, 2) { Auto = true };
        ChapterGraphModel chapter = Model(
            [Episode("ep1", 2, "scene"), Episode("ep2", 3, "scene")], [auto], [], []);

        ChapterRunAdvance advance = Assert.IsType<ChapterRunAdvance>(
            new ChapterRunState(chapter).Resolve("ep1"));

        Assert.Equal(ChapterAdvanceKind.AutoAdvance, advance.Kind);
        Assert.Same(auto, Assert.Single(advance.Options).Edge);
    }

    [Fact]
    public void 같은_Scene의_pending을_fold한_결과는_순차_Commit과_같다()
    {
        ChapterStat trust = new("trust", "신뢰", 0, 0, 5, 2);
        ChapterEdge first = new ChapterEdge("ep1", "ep2", "첫째", null, null, 2)
            { StatChanges = [new StatDelta("trust", 2)] };
        ChapterEdge second = new ChapterEdge("ep2", "ep3", "둘째", null, null, 3)
            { StatChanges = [new StatDelta("trust", 4)] };
        ChapterGraphModel chapter = Model(
            [Episode("ep1", 2, "scene"), Episode("ep2", 3, "scene"), Episode("ep3", 4, "scene")],
            [first, second], [], [trust]);
        var run = new ChapterRunState(chapter);

        run.Commit(first);
        run.Commit(second);

        Assert.Equal(2, run.PendingChoiceCount);
        Assert.Equal(5, Assert.Single(run.Values).Value); // 0+2+4, 런타임 정의가 5로 clamp
    }

    [Fact]
    public void Scene을_나가면_pending이_다음_Scene_entry로_확정된다()
    {
        ChapterEdge edge = new("ep1", "ep2", "다음", null, null, 2);
        ChapterGraphModel chapter = Model(
            [Episode("ep1", 2, "a"), Episode("ep2", 3, "b")], [edge], [], []);
        var run = new ChapterRunState(chapter);

        run.Commit(edge);

        Assert.Equal(0, run.PendingChoiceCount);
    }

    [Fact]
    public void Yarn_로컬_변수는_허용하고_진행_스탯은_산출_전에_막는다()
    {
        var document = new RenderedDocument(
            "node", ResultIdentity.Working(1, "hash"), null,
            [
                Segment("local", variable: "열쇠"),
                Segment("condition", expression: "$열쇠 == true and $trust >= 2"),
                Segment("stat-set", variable: "trust")
            ]);
        var problems = new List<YarnBundleProblem>();

        YarnBundleEmitter.ValidateProgressionStatReferences(
            document, new HashSet<string>(["trust"], StringComparer.Ordinal), problems);

        YarnBundleProblem problem = Assert.Single(problems);
        Assert.True(problem.IsBlocking);
        Assert.Contains("trust", problem.Message);
        Assert.DoesNotContain("열쇠", problem.Message);
    }

    private static RenderedSegment Segment(string id, string? variable = null, string? expression = null) =>
        new(id, variable is null ? RenderedSegmentKind.ConditionBegin : RenderedSegmentKind.SetAssignment,
            variable is null ? DocumentLayer.Conditions : DocumentLayer.SetAssignments,
            new RenderSourceReference(), Expression: expression, Variable: variable);

    private static ChapterGraphModel ChoiceChapter(int initial, bool includeOpen = true)
    {
        ChapterCondition gate = new(
            "신뢰2", "trust >= 2", null,
            [new ConditionTerm(ConditionTermKind.StatComparison, "trust", ConditionComparison.AtLeast, 2)],
            true, 2);
        var edges = new List<ChapterEdge>
        {
            new("ep1", "ep2", "숨김", null, null, 2) { VisibleConditionLabel = "신뢰2" },
            new("ep1", "ep3", "잠김", "신뢰2", "아직 부족함", 3)
        };
        if (includeOpen) edges.Add(new ChapterEdge("ep1", "ep4", "열림", null, null, 4));

        return Model(
            [
                Episode("ep1", 2, "a"),
                Episode("ep2", 3, "b", allowUnreachable: true),
                Episode("ep3", 4, "c", allowUnreachable: true),
                Episode("ep4", 5, "d", allowUnreachable: !includeOpen)
            ],
            edges, [gate], [new ChapterStat("trust", "신뢰", initial, 0, 5, 2)]);
    }

    private static ChapterEpisode Episode(
        string id, int row, string scene, bool allowUnreachable = false) =>
        new(id, id, "", id, 0, 0, null, row, allowUnreachable) { SceneId = scene };

    private static ChapterGraphModel Model(
        IReadOnlyList<ChapterEpisode> episodes,
        IReadOnlyList<ChapterEdge> edges,
        IReadOnlyList<ChapterCondition> conditions,
        IReadOnlyList<ChapterStat> stats) =>
        new("ch", "", episodes, edges, conditions, stats, [], []);
}
