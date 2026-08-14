using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// G7 도달성 증명 — 상태 = (에피소드, 스탯 정수 벡터)의 완전 탐색 (§0.5 보증 2).
/// Gate C 1번: 인위적으로 도달 불가를 만든 견본에서 <b>원인 조건까지 지목</b>해 검출한다.
/// </summary>
public sealed class ChapterReachabilityTests
{
    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    // ── 견본 — 전부 도달 가능 ───────────────────────────────────────────────

    [Fact]
    public void 견본_챕터는_증감_0으로도_주_경로가_전부_도달_가능하다()
    {
        // 2026-08-14 개정 — 에피소드는 더 이상 스탯을 바꾸지 않는다(J열 폐지). 검증기가
        // 실제로 쓰는 모양 그대로, 증감 0으로 증명한다: 스탯 관문이 없는 주 경로와
        // cleared: 관문은 여전히 전부 열린다.
        ChapterGraphModel chapter = ChapterWorkbookReader.Read(SamplePath);

        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(
            chapter,
            new Dictionary<string, IReadOnlyDictionary<string, StatDeltaRange>>(StringComparer.Ordinal));

        Assert.True(result.ExplorationComplete);

        // 시작·직행 경로는 언제나 도달 가능하다.
        Assert.Contains("main05.01", result.ReachableEpisodeIds);
        Assert.Contains("main05.02", result.ReachableEpisodeIds);
        Assert.Contains("main05.03", result.ReachableEpisodeIds);
        Assert.Contains("main05.end", result.ReachableEpisodeIds);

        // 복도완료(cleared:main05.02)로 열리는 부착 에피소드도 도달 가능하다.
        Assert.Contains("attach05.02s", result.ReachableEpisodeIds);
    }

    [Fact]
    public void 스탯_관문_분기는_증감_0에서_도달_불가이고_원인이_지목된다()
    {
        // 스탯을 바꾸는 경로가 없으므로 trust는 초기값 0에 머문다 → 신뢰높음(trust >= 3)
        // 관문은 영원히 닫혀 있고, 증명이 그 원인 조건까지 지목한다. 수치 조정의 새 경로가
        // 정해지면 그 증감이 이 증명으로 다시 흘러들어 판정이 바뀐다.
        ChapterGraphModel chapter = ChapterWorkbookReader.Read(SamplePath);

        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(
            chapter,
            new Dictionary<string, IReadOnlyDictionary<string, StatDeltaRange>>(StringComparer.Ordinal));

        Assert.DoesNotContain("branch05.02A", result.ReachableEpisodeIds);

        ChapterDiagnostic problem = Assert.Single(result.Diagnostics,
            item => item.Message.Contains("branch05.02A", StringComparison.Ordinal));

        Assert.Equal(ChapterDiagnosticSeverity.Error, problem.Severity);
        Assert.Equal(ChapterDiagnosticCode.EpisodeUnreachable, problem.Code);

        // 원인 조건까지 지목한다 — 어느 조건이, 왜 불가능한지.
        Assert.Contains("trust >= 3", problem.Message);
        Assert.Contains("최대 0", problem.Message);
    }

    // ── 인위적 도달 불가 (Gate C 1번) ───────────────────────────────────────

    [Fact]
    public void 스탯이_모자라_영원히_닫히는_관문은_원인_조건과_함께_검출된다()
    {
        ChapterGraphModel chapter = Chapter(
            episodes:
            [
                Episode("ep1", row: 2),
                Episode("ep2", row: 3, unlock: "신뢰높음")
            ],
            edges: [("ep1", "ep2", null)],
            conditions: [("신뢰높음", "trust >= 3")]);

        // ep1은 trust를 최대 +1만 준다 → trust >= 3은 영원히 거짓.
        var deltas = Deltas(("ep1", "trust", 0, 1));

        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(chapter, deltas);

        Assert.DoesNotContain("ep2", result.ReachableEpisodeIds);

        ChapterDiagnostic problem = Assert.Single(result.Diagnostics);
        Assert.Equal(ChapterDiagnosticSeverity.Error, problem.Severity);
        Assert.Contains("'신뢰높음'의 'trust >= 3'가 원인입니다", problem.Message);
        Assert.Contains("최대 1", problem.Message);
    }

    [Fact]
    public void 스탯이_쌓이면_열리는_관문은_도달_가능하다()
    {
        // 같은 구조인데 ep1↔ep2 사이를 오갈 수 있으면 trust가 쌓여 관문이 열린다 —
        // 상태 탐색이 "여러 번 플레이"를 정말로 세는지 확인한다.
        ChapterGraphModel chapter = Chapter(
            episodes:
            [
                Episode("ep1", row: 2),
                Episode("ep2", row: 3),
                Episode("ep3", row: 4, unlock: "신뢰높음")
            ],
            edges: [("ep1", "ep2", null), ("ep2", "ep1", null), ("ep2", "ep3", null)],
            conditions: [("신뢰높음", "trust >= 3")]);

        var deltas = Deltas(("ep1", "trust", 0, 1), ("ep2", "trust", 0, 1));

        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(chapter, deltas);

        Assert.Contains("ep3", result.ReachableEpisodeIds);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void 도달_불가한_에피소드에_걸린_cleared_조건은_연쇄_원인으로_지목된다()
    {
        ChapterGraphModel chapter = Chapter(
            episodes:
            [
                Episode("ep1", row: 2),
                Episode("ep2", row: 3, unlock: "관문"),
                Episode("ep3", row: 4, unlock: "완료조건")
            ],
            edges: [("ep1", "ep2", null), ("ep1", "ep3", null)],
            conditions: [("관문", "trust >= 5"), ("완료조건", "cleared:ep2")]);

        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(
            chapter, Deltas(("ep1", "trust", 0, 1)));

        Assert.DoesNotContain("ep2", result.ReachableEpisodeIds);
        Assert.DoesNotContain("ep3", result.ReachableEpisodeIds);

        ChapterDiagnostic chained = Assert.Single(result.Diagnostics,
            item => item.Message.Contains("ep3", StringComparison.Ordinal));

        Assert.Contains("cleared:ep2", chained.Message);
        Assert.Contains("자체가 도달 불가", chained.Message);
    }

    [Fact]
    public void 들어오는_간선이_없는_에피소드는_그렇게_말한다()
    {
        ChapterGraphModel chapter = Chapter(
            episodes: [Episode("ep1", row: 2), Episode("고아", row: 3)],
            edges: [],
            conditions: []);

        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(
            chapter, Deltas());

        ChapterDiagnostic problem = Assert.Single(result.Diagnostics);
        Assert.Contains("들어오는 간선이", problem.Message);
    }

    [Fact]
    public void 도달불가_허용이_켜진_에피소드는_오류가_아니라_알림이다()
    {
        // D3 — 의도적 도달 불가(폐기 예정·미완성)는 명시 예외로 두되, 그 사실은 표시된다.
        ChapterGraphModel chapter = Chapter(
            episodes:
            [
                Episode("ep1", row: 2),
                Episode("보류", row: 3, allowUnreachable: true)
            ],
            edges: [],
            conditions: []);

        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(chapter, Deltas());

        ChapterDiagnostic notice = Assert.Single(result.Diagnostics);
        Assert.Equal(ChapterDiagnosticSeverity.Info, notice.Severity);
        Assert.Contains("도달불가 허용", notice.Message);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void 스탯_경계는_탐색_경계다_최대를_넘는_가정은_세지_않는다()
    {
        // trust 최대가 2인데 조건이 trust >= 3이면, 아무리 쌓아도 잘려서 도달 불가다.
        ChapterGraphModel chapter = Chapter(
            episodes:
            [
                Episode("ep1", row: 2),
                Episode("ep2", row: 3, unlock: "신뢰높음")
            ],
            edges: [("ep1", "ep1", null), ("ep1", "ep2", null)],
            conditions: [("신뢰높음", "trust >= 3")],
            statMaximum: 2);

        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(
            chapter, Deltas(("ep1", "trust", 0, 1)));

        Assert.DoesNotContain("ep2", result.ReachableEpisodeIds);
        Assert.True(result.ExplorationComplete);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static ChapterEpisode Episode(
        string id, int row, string? unlock = null, bool allowUnreachable = false) =>
        new(id, id, "01", "Main", $"Story_{id}", 0, 0, null, unlock, null, null, row, allowUnreachable);

    private static ChapterGraphModel Chapter(
        IReadOnlyList<ChapterEpisode> episodes,
        IReadOnlyList<(string From, string To, string? Condition)> edges,
        IReadOnlyList<(string Label, string Expression)> conditions,
        int statMaximum = 10)
    {
        var stats = new List<ChapterStat>
        {
            new("trust", "신뢰", Initial: 0, Minimum: 0, Maximum: statMaximum, SourceRow: 2)
        };

        var statKeys = stats.Select(stat => stat.Key).ToHashSet(StringComparer.Ordinal);

        List<ChapterCondition> parsed = conditions
            .Select((pair, index) =>
            {
                ConditionParseResult result = ConditionExpressionParser.Parse(pair.Expression, statKeys);
                return new ChapterCondition(
                    pair.Label, pair.Expression, null, result.Terms, result.IsValid, index + 2);
            })
            .ToList();

        List<ChapterEdge> edgeList = edges
            .Select((edge, index) => new ChapterEdge(
                edge.From, edge.To, null, edge.Condition,
                HideWhenLocked: false, null, index + 2))
            .ToList();

        return new ChapterGraphModel(
            "test", "test.xlsx", episodes, edgeList, parsed, stats,
            Array.Empty<ChapterFixture>(), Array.Empty<ChapterDiagnostic>());
    }

    private static Dictionary<string, IReadOnlyDictionary<string, StatDeltaRange>> Deltas(
        params (string EpisodeId, string Stat, int Min, int Max)[] entries)
    {
        var deltas = new Dictionary<string, IReadOnlyDictionary<string, StatDeltaRange>>(StringComparer.Ordinal);

        foreach ((string episodeId, string stat, int min, int max) in entries)
        {
            deltas[episodeId] = new Dictionary<string, StatDeltaRange>(StringComparer.Ordinal)
            {
                [stat] = new StatDeltaRange(min, max)
            };
        }

        return deltas;
    }
}
