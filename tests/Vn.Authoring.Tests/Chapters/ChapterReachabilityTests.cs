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
    public void 견본_챕터는_증감_없이도_주_경로가_전부_도달_가능하다()
    {
        // 2026-08-14 개정 — 스탯 증감의 원천은 간선이다. 견본의 주 경로에는 스탯 관문이
        // 없으므로 전부 열린다.
        ChapterGraphModel chapter = ChapterWorkbookReader.Read(SamplePath);

        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(chapter);

        Assert.True(result.ExplorationComplete);

        // 시작·직행 경로는 언제나 도달 가능하다.
        Assert.Contains("main05.01", result.ReachableEpisodeIds);
        Assert.Contains("main05.02", result.ReachableEpisodeIds);
        Assert.Contains("main05.03", result.ReachableEpisodeIds);
        Assert.Contains("main05.end", result.ReachableEpisodeIds);

        // ⚠ 2026-08-25 — `attach05.02s`는 이제 도달 <b>불가</b>다. 예전에는 종류가
        // `Attachment`라 증명기가 "간선이 아니라 관문 만족 가능성으로" 따로 통과시켰는데,
        // 코어가 `EpisodeKind`를 지우면서 그 특례가 함께 사라졌다. 들어오는 간선이 없는
        // 섬은 이제 그냥 섬이고, 의도임은 `도달불가 허용`이 적는다 — 그래서 오류가 아니라
        // 알림이다(D3).
        Assert.DoesNotContain("attach05.02s", result.ReachableEpisodeIds);

        ChapterDiagnostic island = Assert.Single(result.Diagnostics,
            item => item.Message.Contains("attach05.02s", StringComparison.Ordinal));

        Assert.NotEqual(ChapterDiagnosticSeverity.Error, island.Severity);
    }

    [Fact]
    public void 스탯_관문_분기는_증감_없는_간선들만으로는_도달_불가이고_원인이_지목된다()
    {
        // 견본 간선에는 스탯변화가 없으므로 trust는 초기값 0에 머문다 → 신뢰높음(trust >= 3)
        // 관문은 영원히 닫혀 있고, 증명이 그 원인 조건까지 지목한다. 간선에 증감을 적으면
        // 그 값 그대로 전이해 판정이 바뀐다 — 이것이 수치 밸런스의 저작 루프다.
        ChapterGraphModel chapter = ChapterWorkbookReader.Read(SamplePath);

        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(chapter);

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
        // 곁길 간선이 trust를 최대 +1까지만 준다 → trust >= 3은 영원히 거짓.
        ChapterGraphModel chapter = Chapter(
            episodes:
            [
                Episode("ep1", row: 2),
                Episode("곁길", row: 3),
                Episode("ep2", row: 4)
            ],
            edges: [("ep1", "곁길", null, 1), ("ep1", "ep2", "신뢰높음", 0)],
            conditions: [("신뢰높음", "trust >= 3")]);

        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(chapter);

        Assert.DoesNotContain("ep2", result.ReachableEpisodeIds);

        ChapterDiagnostic problem = Assert.Single(result.Diagnostics);
        Assert.Equal(ChapterDiagnosticSeverity.Error, problem.Severity);
        Assert.Contains("'신뢰높음'의 'trust >= 3'가 원인입니다", problem.Message);
        Assert.Contains("최대 1", problem.Message);
    }

    [Fact]
    public void 스탯이_쌓이면_열리는_관문은_도달_가능하다()
    {
        // ep1↔ep2를 오가는 간선이 매번 +1을 커밋하면 trust가 쌓여 관문이 열린다 —
        // 상태 탐색이 "여러 번 오간다"를 정말로 세는지 확인한다.
        ChapterGraphModel chapter = Chapter(
            episodes:
            [
                Episode("ep1", row: 2),
                Episode("ep2", row: 3),
                Episode("ep3", row: 4)
            ],
            edges: [("ep1", "ep2", null, 1), ("ep2", "ep1", null, 1), ("ep2", "ep3", "신뢰높음", 0)],
            conditions: [("신뢰높음", "trust >= 3")]);

        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(chapter);

        Assert.Contains("ep3", result.ReachableEpisodeIds);
        Assert.Empty(result.Diagnostics);
    }

    /// <summary>
    /// `cleared:` 연쇄가 있던 자리다 (2026-08-25 폐지). 그 조건은 도달 가능 집합 자체를
    /// 참조해서 고정점 반복이 필요했는데, 깃발 스탯으로 바뀌면서 <b>연쇄가 스탯 관문
    /// 하나로 접혔다</b> — 깃발을 켜는 간선이 도달 불가면 깃발도 영원히 오르지 않고,
    /// 스탯은 걷는 도중에 정해지므로 한 바퀴로 잡힌다.
    /// </summary>
    [Fact]
    public void 아무도_올려주지_않는_스탯_관문은_원인으로_지목된다()
    {
        ChapterGraphModel chapter = Chapter(
            episodes:
            [
                Episode("ep1", row: 2),
                Episode("ep2", row: 3),
                Episode("ep3", row: 4)
            ],
            edges: [("ep1", "ep2", "관문", 0), ("ep1", "ep3", "완료조건", 0)],
            conditions: [("관문", "trust >= 5"), ("완료조건", "trust >= 1")]);

        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(chapter);

        Assert.DoesNotContain("ep2", result.ReachableEpisodeIds);
        Assert.DoesNotContain("ep3", result.ReachableEpisodeIds);

        ChapterDiagnostic chained = Assert.Single(result.Diagnostics,
            item => item.Message.Contains("ep3", StringComparison.Ordinal));

        Assert.Contains("trust", chained.Message);
        Assert.Contains("최대 0까지", chained.Message);
    }

    [Fact]
    public void 들어오는_간선이_없는_에피소드는_그렇게_말한다()
    {
        ChapterGraphModel chapter = Chapter(
            episodes: [Episode("ep1", row: 2), Episode("고아", row: 3)],
            edges: [],
            conditions: []);

        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(chapter);

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

        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(chapter);

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
                Episode("ep2", row: 3)
            ],
            edges: [("ep1", "ep1", null, 1), ("ep1", "ep2", "신뢰높음", 0)],
            conditions: [("신뢰높음", "trust >= 3")],
            statMaximum: 2);

        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(chapter);

        Assert.DoesNotContain("ep2", result.ReachableEpisodeIds);
        Assert.True(result.ExplorationComplete);
    }

    // ── 에피소드별 스탯 폭 (2026-08-17 소유자) ──────────────────────────────
    //
    // "간선을 따라 왔을 때 스탯의 변화량이 노드에 표시되도록. 여러 루트가 있을 때는
    // 최소최대량을 표기." 값은 <b>도착 직후</b>다 — 그 노드로 들어오는 간선의 증감까지
    // 커밋한 뒤. 이미 걷고 있는 완전 탐색에서 같이 재므로 따로 세지 않는다.

    [Fact]
    public void 루트가_하나면_도착_스탯은_값_하나로_고정된다()
    {
        ChapterGraphModel chapter = Chapter(
            episodes: [Episode("ep1", row: 2), Episode("ep2", row: 3)],
            edges: [("ep1", "ep2", null, 2)],
            conditions: []);

        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(chapter);

        ChapterStatSpan span = Assert.Single(result.SpansFor("ep2"));
        Assert.Equal("신뢰", span.DisplayName);
        Assert.True(span.IsFixed);
        Assert.Equal(2, span.Minimum);
        Assert.Equal(2, span.Maximum);
    }

    [Fact]
    public void 들어오는_루트가_여럿이면_최소_최대로_벌어진다()
    {
        // 합류점 ep4에는 +1 길과 +3 길이 들어온다 → 1~3.
        ChapterGraphModel chapter = Chapter(
            episodes:
            [
                Episode("ep1", row: 2),
                Episode("싼길", row: 3),
                Episode("비싼길", row: 4),
                Episode("합류", row: 5)
            ],
            edges:
            [
                ("ep1", "싼길", null, 1),
                ("ep1", "비싼길", null, 3),
                ("싼길", "합류", null, 0),
                ("비싼길", "합류", null, 0)
            ],
            conditions: []);

        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(chapter);

        ChapterStatSpan span = Assert.Single(result.SpansFor("합류"));
        Assert.False(span.IsFixed);
        Assert.Equal(1, span.Minimum);
        Assert.Equal(3, span.Maximum);

        // 갈래 각각은 여전히 고정이다 — 벌어지는 건 합류점뿐이다.
        Assert.True(Assert.Single(result.SpansFor("싼길")).IsFixed);
        Assert.Equal(3, Assert.Single(result.SpansFor("비싼길")).Minimum);
    }

    [Fact]
    public void 시작_에피소드는_스탯_초기값을_그대로_갖는다()
    {
        ChapterGraphModel chapter = Chapter(
            episodes: [Episode("ep1", row: 2), Episode("ep2", row: 3)],
            edges: [("ep1", "ep2", null, 1)],
            conditions: []);

        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(chapter);

        ChapterStatSpan span = Assert.Single(result.SpansFor("ep1"));
        Assert.Equal(0, span.Minimum);
        Assert.Equal(0, span.Maximum);
    }

    [Fact]
    public void 닿을_수_없는_에피소드에는_도착_스탯이_없다()
    {
        ChapterGraphModel chapter = Chapter(
            episodes: [Episode("ep1", row: 2), Episode("고아", row: 3)],
            edges: [],
            conditions: []);

        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(chapter);

        Assert.Empty(result.SpansFor("고아"));
    }

    [Fact]
    public void 오가며_쌓이는_길은_폭이_경계까지_벌어진다()
    {
        // ep1↔ep2를 오가면 trust가 계속 쌓인다 — 폭은 스탯 최대(경계)에서 멈춘다.
        ChapterGraphModel chapter = Chapter(
            episodes: [Episode("ep1", row: 2), Episode("ep2", row: 3)],
            edges: [("ep1", "ep2", null, 1), ("ep2", "ep1", null, 1)],
            conditions: [],
            statMaximum: 4);

        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(chapter);

        ChapterStatSpan span = Assert.Single(result.SpansFor("ep2"));
        Assert.Equal(1, span.Minimum);
        Assert.Equal(4, span.Maximum);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <summary>v8 — 관문은 에피소드가 아니라 들어오는 길이 갖는다(간선 조건으로 준다).</summary>
    private static ChapterEpisode Episode(string id, int row, bool allowUnreachable = false) =>
        new(id, id, "01", $"Story_{id}", 0, 0, null, null, row, allowUnreachable);

    /// <param name="edges">TrustDelta — 그 간선을 타는 순간 커밋되는 trust 증감 (2026-08-14 규칙).</param>
    private static ChapterGraphModel Chapter(
        IReadOnlyList<ChapterEpisode> episodes,
        IReadOnlyList<(string From, string To, string? Condition, int TrustDelta)> edges,
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
                null, index + 2)
            {
                StatChanges = edge.TrustDelta == 0
                    ? []
                    : [new StatDelta("trust", edge.TrustDelta)]
            })
            .ToList();

        return new ChapterGraphModel(
            "test", "test.xlsx", episodes, edgeList, parsed, stats,
            Array.Empty<ChapterFixture>(), Array.Empty<ChapterDiagnostic>());
    }
}
