using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 깊이 기반 분기 배치 (챕터 v2 8단계). 소유자 피드백 그대로의 사례를 고정한다:
/// "root에서 뻗어나온 두 에피소드 둘이서 다시 연결되는" 합류가 터무니없어 보이지 않아야 하고,
/// 서로 다른 부모의 자식이 겹치지 않아야 한다.
/// </summary>
public sealed class ChapterBranchPlannerTests
{
    private static ChapterEpisode Episode(string id, double x, double y) =>
        new(id, id, "10", "Main", $"Story_{id}", x, y,
            EndingKey: null, Memo: null, SourceRow: 2, AllowUnreachable: false);

    private static ChapterEdge Edge(string from, string to) =>
        new(from, to, OptionLabel: null, ConditionLabel: null,
            LockedMessage: null, SourceRow: 2);

    private static ChapterGraphModel Model(
        IReadOnlyList<ChapterEpisode> episodes, IReadOnlyList<ChapterEdge> edges) =>
        new("ch", "ch.xlsx", episodes, edges, [], [], [], []);

    [Fact]
    public void 합류_노드의_깊이는_가장_깊은_부모_기준이다()
    {
        // root → a → c,  root → b → (b2) → c  — c는 depth 2가 아니라 3이어야
        // b2 → c 간선이 뒤로 꺾이지 않는다.
        ChapterGraphModel model = Model(
            [Episode("root", 0, 0), Episode("a", 220, 0), Episode("b", 220, 110),
             Episode("b2", 440, 110), Episode("c", 440, 0)],
            [Edge("root", "a"), Edge("root", "b"), Edge("b", "b2"),
             Edge("a", "c"), Edge("b2", "c")]);

        IReadOnlyDictionary<string, int> depths = ChapterBranchPlanner.Depths(model);

        Assert.Equal(0, depths["root"]);
        Assert.Equal(1, depths["a"]);
        Assert.Equal(1, depths["b"]);
        Assert.Equal(2, depths["b2"]);
        Assert.Equal(3, depths["c"]);
    }

    [Fact]
    public void 서로_다른_부모의_자식이_같은_열에서_겹치지_않는다()
    {
        // 옛 결함 그대로의 사례: 같은 열의 두 부모(a, b)가 각자 자식을 만들면
        // 옛 규칙(부모Y + 형제수×110)으로는 정확히 같은 자리에 겹쳤다.
        var episodes = new List<ChapterEpisode>
        {
            Episode("root", 0, 0), Episode("a", 220, 0), Episode("b", 220, 110)
        };
        var edges = new List<ChapterEdge> { Edge("root", "a"), Edge("root", "b") };

        (double ax, double ay) = ChapterBranchPlanner.SuggestPlacement(
            Model(episodes, edges), "a");
        episodes.Add(Episode("a1", ax, ay));
        edges.Add(Edge("a", "a1"));

        (double bx, double by) = ChapterBranchPlanner.SuggestPlacement(
            Model(episodes, edges), "b");

        Assert.Equal(ax, bx);                                  // 같은 깊이 → 같은 열
        Assert.True(Math.Abs(ay - by) >= ChapterBranchPlanner.RowHeight,
            $"두 자식이 겹쳤다: a1의 Y={ay}, b1의 Y={by}");
    }

    [Fact]
    public void 제안_열은_깊이가_정한다_부모가_왼쪽에_있어도()
    {
        // 부모가 손으로 왼쪽에 옮겨져 있어도 새 노드는 자기 깊이 열로 간다 —
        // depth 구분이 배치에서 무너지지 않는다.
        ChapterGraphModel model = Model(
            [Episode("root", 0, 0), Episode("mid", 40, 110)],   // mid를 root 근처로 끌어다 둠
            [Edge("root", "mid")]);

        (double x, _) = ChapterBranchPlanner.SuggestPlacement(model, "mid");

        Assert.Equal(2 * ChapterBranchPlanner.ColumnWidth, x);  // depth(mid)=1 → 열 2
    }

    [Fact]
    public void 순환이_있어도_깊이_계산이_멈춘다()
    {
        ChapterGraphModel model = Model(
            [Episode("root", 0, 0), Episode("a", 220, 0), Episode("b", 440, 0)],
            [Edge("root", "a"), Edge("a", "b"), Edge("b", "a")]);

        IReadOnlyDictionary<string, int> depths = ChapterBranchPlanner.Depths(model);

        Assert.True(depths["a"] < model.Episodes.Count);
        Assert.True(depths["b"] < model.Episodes.Count);
    }

    [Fact]
    public void 화면_배치는_열이_깊이고_고아는_아래_줄에_선다()
    {
        // 뷰는 이 배치를 그릴 때마다 다시 계산한다(v3) — 엑셀 X·Y와 무관하게 흐름이 자리를 정한다.
        ChapterGraphModel model = Model(
            [Episode("root", 10, 20), Episode("a", 900, -50), Episode("b", 30, 400),
             Episode("c", 500, 0), Episode("orphan", 777, 888)],
            [Edge("root", "a"), Edge("root", "b"), Edge("a", "c"), Edge("b", "c")]);

        IReadOnlyDictionary<string, (double X, double Y)> placed =
            ChapterBranchPlanner.Layout(model);

        // 열 = 깊이 (원점 기준). 합류 c는 열 2.
        Assert.Equal((0d, 0d), placed["root"]);
        Assert.Equal(220d, placed["a"].X);
        Assert.Equal(220d, placed["b"].X);
        Assert.Equal(440d, placed["c"].X);

        // 같은 열 안 순서 = 시트 행 순서 (a가 b보다 먼저 적힘).
        Assert.Equal(0d, placed["a"].Y);
        Assert.Equal(ChapterBranchPlanner.RowHeight, placed["b"].Y);

        // 간선 없는 고아는 그래프 아래 줄에 따로 선다 — 겹쳐 숨기지 않는다.
        Assert.True(placed["orphan"].Y > placed["b"].Y + ChapterBranchPlanner.RowHeight,
            $"고아가 그래프와 겹친다: Y={placed["orphan"].Y}");
    }
}
