using System.Text.Json;
using Ked.Progression;
using Ked.Progression.Dto;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 두 도달성 증명기가 <b>같은 답을 내는가</b> — 툴의 <see cref="ChapterReachabilityProver"/>와
/// 코어의 <see cref="ChapterReachability"/>.
///
/// <b>왜 대조부터 하나 (로드맵 T3)</b> — 툴의 증명기를 코어 것으로 갈아 끼우는 것이 목표인데,
/// 그 전에 둘이 같은 답인지 모른 채 바꾸면 <b>바꾼 날 무엇이 달라졌는지 아무도 모른다.</b>
/// 게다가 저쪽은 이쪽 증명기를 **오라클로 삼아** 자기 코퍼스를 고정해 뒀다
/// (`progression-handoff.md` §5) — 두 답이 갈리면 그 코퍼스도 함께 흔들린다.
///
/// ⚠ 이 하네스가 재는 것은 증명기 둘만이 아니다. 코어 쪽 답은 <b>계약 전체를 관통해서</b>
/// 얻는다:
///
/// <code>
/// ChapterGraphModel → 내보내기 → JSON → 코어 로더 → ChapterProgression → 코어 증명
/// ChapterGraphModel → 툴 증명
/// </code>
///
/// 그래서 이 파일이 넘어지면 원인이 증명기일 수도, <b>내보내기 필드 하나</b>일 수도 있다.
/// 그것이 값이다 — 계약이 실제로 같은 것을 말하는지가 여기서 한 번에 드러난다.
/// </summary>
public sealed class ChapterReachabilityEquivalenceTests
{
    [Fact]
    public void 단순한_한_줄은_둘_다_전부_도달_가능하다() => AssertAgree(Chapter(
        [Episode("ep1"), Episode("ep2"), Episode("ep3")],
        [("ep1", "ep2", null, 0), ("ep2", "ep3", null, 0)],
        []));

    [Fact]
    public void 스탯이_쌓여야_열리는_관문에서_같은_답을_낸다() => AssertAgree(Chapter(
        [Episode("시작"), Episode("중간"), Episode("끝")],
        [("시작", "중간", null, 3), ("중간", "끝", "신뢰3이상", 0)],
        [("신뢰3이상", "trust >= 3")]));

    [Fact]
    public void 영원히_닫히는_관문에서_같은_답을_낸다()
    {
        // 증감이 어디에도 없으니 trust는 0에서 못 움직인다 — 둘 다 "도달 불가"여야 한다.
        // ⚠ 이 케이스가 이 하네스의 핵심이다: **거짓 경보 없음**이 코어의 불변식이고
        // (`principles.md` §6), 툴이 그것과 다른 답을 내면 저쪽 불변식이 깨진 것처럼 보인다.
        AssertAgree(Chapter(
            [Episode("시작"), Episode("끝")],
            [("시작", "끝", "신뢰5이상", 0)],
            [("신뢰5이상", "trust >= 5")]));
    }

    [Fact]
    public void 들어오는_간선이_없는_에피소드에서_같은_답을_낸다() => AssertAgree(Chapter(
        [Episode("시작"), Episode("외딴섬")],
        [],
        []));

    [Fact]
    public void 길이_여럿이면_도착_스탯_폭이_같게_벌어진다()
    {
        // 폭(StatSpan)까지 대조한다 — 도달 가능 여부만 같고 폭이 다르면, 관문 판정이
        // 갈리는 것은 시간 문제다.
        AssertAgree(Chapter(
            [Episode("시작"), Episode("믿는길"), Episode("혼자길"), Episode("합류")],
            [
                ("시작", "믿는길", null, 3),
                ("시작", "혼자길", null, 1),
                ("믿는길", "합류", null, 0),
                ("혼자길", "합류", null, 0)
            ],
            []));
    }

    [Fact]
    public void 오가며_쌓이는_길에서_같은_답을_낸다()
    {
        // 순환이 있으면 폭이 경계까지 벌어진다 — 탐색이 끊기는 자리라 둘이 갈리기 쉽다.
        AssertAgree(Chapter(
            [Episode("시작"), Episode("돌기"), Episode("끝")],
            [
                ("시작", "돌기", null, 1),
                ("돌기", "시작", null, 1),
                ("돌기", "끝", "신뢰8이상", 0)
            ],
            [("신뢰8이상", "trust >= 8")]));
    }

    [Fact]
    public void 스탯_경계_위에서_같은_답을_낸다()
    {
        // clamp가 갈리면 답이 갈린다 — 계약서 §G7이 처음부터 지목한 자리다
        // ("툴의 증명은 Clamp로 걷는데 런타임이 다른 경계로 clamp하면…").
        AssertAgree(Chapter(
            [Episode("시작"), Episode("중간"), Episode("끝")],
            [("시작", "중간", null, 9), ("중간", "끝", "신뢰10이상", 0)],
            [("신뢰10이상", "trust >= 10")],
            statMaximum: 5));
    }

    [Fact]
    public void 도달_불가에_걸린_cleared_조건에서_같은_답을_낸다() => AssertAgree(Chapter(
        [Episode("시작"), Episode("못가는곳"), Episode("끝")],
        [("시작", "끝", "못가는곳클리어", 0)],
        [("못가는곳클리어", "cleared:못가는곳")]));

    // ── ⚠ 대조하다 드러난 갈림 — 증명기가 아니라 **검증 심각도**다 ────────────

    [Fact]
    public void 툴이_경고로_넘긴_챕터를_코어는_거부한다()
    {
        // ⛔ **열린 구멍이다.** 문구 없는 간선(보이지 않는 기본)에 관문이 걸리면
        // 툴은 **경고**로 넘겨 JSON을 내보내는데, 코어 로더는 **오류**로 거부한다.
        // 즉 툴이 "실을 수 없는 것을 내보낸다" — 자기 규율을 어기는 자리다.
        //
        // ⚠ 심각도를 올려 보았고(2026-08-23), 그러면 **정상적인 편집 흐름이 막힌다**:
        // 툴의 `AddEdge`는 문구 없이도 길을 놓을 수 있고 그것이 "보이지 않는 기본"의
        // 정의다. 무엇을 막을지는 제품 결정이라 여기서 정하지 않고, 지금 사실만 못 박는다.
        //
        // 이 테스트가 깨지는 날 = 누군가 그 결정을 내린 날이다. 그때 이 주석을 지운다.
        ChapterGraphModel chapter = Chapter(
            [Episode("시작"), Episode("끝")],
            [("시작", "끝", "신뢰5이상", 0)],
            [("신뢰5이상", "trust >= 5")],
            plainAdvance: true);

        // 툴: 경고만 — 오류가 아니므로 내보내기 관문을 통과한다.
        ChapterValidationResult validation = ChapterValidator.Validate(chapter, episodesFolder: null);

        Assert.Contains(
            validation.All,
            item => item.Code == ChapterDiagnosticCode.OptionEdgeMismatch &&
                    item.Severity == ChapterDiagnosticSeverity.Warning);

        // 코어: 거부.
        ChapterProgressionDto dto = ExportToDto(chapter);
        ProgressionLoadResult load = ProgressionLoader.Load(dto);

        Assert.False(load.IsValid, "코어가 실었다면 이 구멍은 이미 메워진 것이다");
        Assert.Contains(load.Diagnostics, item => item.ToString().Contains("자동 진행"));
    }

    // ── 대조 ────────────────────────────────────────────────────────────────

    private static void AssertAgree(ChapterGraphModel chapter)
    {
        ChapterReachabilityResult tool = ChapterReachabilityProver.Prove(chapter);
        ReachabilityResult core = ProveWithCore(chapter);

        Assert.Equal(
            tool.ReachableEpisodeIds.OrderBy(id => id, StringComparer.Ordinal),
            core.ReachableEpisodeIds.OrderBy(id => id, StringComparer.Ordinal));

        Assert.Equal(tool.ExplorationComplete, core.ExplorationComplete);

        foreach (string episodeId in tool.ReachableEpisodeIds.OrderBy(id => id, StringComparer.Ordinal))
        {
            Assert.Equal(
                tool.SpansFor(episodeId)
                    .Select(span => (span.Key, span.Minimum, span.Maximum))
                    .OrderBy(span => span.Key, StringComparer.Ordinal),
                core.SpansFor(episodeId)
                    .Select(span => (span.Key, span.Minimum, span.Maximum))
                    .OrderBy(span => span.Key, StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// 코어 쪽 답 — <b>내보내기·JSON·로더를 전부 지나서</b> 얻는다. 지름길로 모델을 직접
    /// 세우면 정작 계약이 갈린 것을 못 본다.
    /// </summary>
    private static ReachabilityResult ProveWithCore(ChapterGraphModel chapter)
    {
        ProgressionLoadResult load = ProgressionLoader.Load(ExportToDto(chapter));

        Assert.True(
            load.IsValid,
            "코어 로더가 이 JSON을 못 실었다: " +
            string.Join(" / ", load.Diagnostics.Select(item => item.ToString())));

        return ChapterReachability.Prove(load.Chapter);
    }

    /// <summary>
    /// 내보내기를 지나 코어 DTO까지. <b>지름길로 DTO를 손으로 세우지 않는다</b> —
    /// 그러면 정작 계약이 갈린 것을 못 본다.
    /// </summary>
    private static ChapterProgressionDto ExportToDto(ChapterGraphModel chapter)
    {
        // ⚠ 검증 관문을 지나지 않는다. 이 하네스는 **도달 불가가 있는 챕터**를 일부러
        // 넣는데, 그런 챕터는 내보내기가 옳게 거부한다(G8). 여기서 보려는 것은 거부
        // 규칙이 아니라 두 증명기의 답이라, 빈 검증 결과를 주어 JSON만 얻는다.
        var noErrors = new ChapterValidationResult(
            [],
            new ChapterReachabilityResult(new HashSet<string>(StringComparer.Ordinal), [], true));

        ChapterExportResult export = ChapterProgressionExporter.ExportValidated(chapter, noErrors);
        Assert.False(export.Refused, "하네스가 JSON을 못 얻으면 대조 자체가 성립하지 않는다");

        ChapterProgressionDto? dto =
            JsonSerializer.Deserialize<ChapterProgressionDto>(export.Json!);

        Assert.NotNull(dto);

        return dto!;
    }

    // ── 픽스처 ──────────────────────────────────────────────────────────────

    private static ChapterEpisode Episode(string id) =>
        new(id, id, "", "Main", id, 0, 0, null, null, 2);

    private static ChapterGraphModel Chapter(
        IReadOnlyList<ChapterEpisode> episodes,
        IReadOnlyList<(string From, string To, string? Condition, int TrustDelta)> edges,
        IReadOnlyList<(string Label, string Expression)> conditions,
        int statMaximum = 10,
        bool plainAdvance = false)
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

        // ⚠ 간선마다 **문구를 준다.** 문구 없는 간선은 "보이지 않는 기본"(자동 진행)이고,
        // 그 자리엔 관문이 없어야 하며 에피소드당 하나뿐이다. 처음에 문구를 안 줬더니
        // 코어 로더가 여덟 중 여섯을 거부해 대조가 성립조차 안 됐다 — 재려던 것은
        // 증명기인데 픽스처가 규격을 어겨 그 앞에서 멈춘 것이다.
        List<ChapterEdge> edgeList = edges
            .Select((edge, index) => new ChapterEdge(
                edge.From, edge.To, plainAdvance ? null : $"{edge.To}로", edge.Condition,
                HideWhenLocked: false, null, index + 2)
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
