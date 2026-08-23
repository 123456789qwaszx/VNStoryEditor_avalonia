using ClosedXML.Excel;
using Vn.Authoring.Chapters;
using Path = System.IO.Path;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 깃발을 켜고 끈다 (2026-08-19 소유자).
///
/// <b>그전까지 bool 스탯은 값이 변할 방법이 없었다.</b> `StatDelta`는 증감 전용이고 양쪽
/// 저장소가 bool 증감을 오류로 막았으므로(§G4), 선언한 초기값이 곧 영원한 값이었다 —
/// 조건에서 읽을 수는 있는데 아무도 켤 수 없는 깃발이었다.
/// 저쪽 `docs/handoff.md` §6-4가 같은 구멍을 짚으며 "소유자가 정해야 한다"고 적어 두었다.
///
/// 고른 답은 <b>지정(Set)</b>이다. 증감은 깃발에 맞는 낱말이 아니다 — `met_willow +1`은
/// "만난 횟수"로 읽히고 `+2`는 뜻이 없는데도 조용히 같은 결과가 된다.
///
/// 규격: <c>docs/work-orders/bool-stat-orders.md</c>.
/// </summary>
public sealed class BoolStatSetTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-bool-stat", Guid.NewGuid().ToString("N"));

    public BoolStatSetTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    // ── 문법 ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("met true", 1)]
    [InlineData("met false", 0)]
    [InlineData("met TRUE", 1)]
    [InlineData("met = true", 1)]     // 등호를 붙이는 쪽이 자연스러워 실제로 그렇게 적는다
    public void 깃발_표기를_지정으로_읽는다(string text, int expected)
    {
        StatDeltaParseResult result = StatDeltaParser.Parse(text, ["met", "trust"]);

        Assert.True(result.IsValid, string.Join(" / ", result.Problems.Select(p => p.Message)));

        StatDelta delta = Assert.Single(result.Deltas);
        Assert.Equal("met", delta.Key);
        Assert.True(delta.IsSet);
        Assert.Equal(expected, delta.Amount);
    }

    [Fact]
    public void 증감과_지정이_한_칸에_섞여도_읽는다()
    {
        StatDeltaParseResult result = StatDeltaParser.Parse("trust +2; met true", ["met", "trust"]);

        Assert.True(result.IsValid);
        Assert.Equal(
            [("trust", 2, false), ("met", 1, true)],
            result.Deltas.Select(d => (d.Key, d.Amount, d.IsSet)).ToArray());
    }

    [Fact]
    public void 깃발_표기라도_없는_키는_그대로_오류다()
    {
        // 지정이 생겼다고 오타를 봐주지 않는다 — 없는 키를 켜면 아무 일도 안 일어나고,
        // 그 버그는 재생해 봐도 안 보인다.
        StatDeltaParseResult result = StatDeltaParser.Parse("업는키 true", ["met"]);

        Assert.False(result.IsValid);
        Assert.Equal(ConditionProblemKind.UnknownStatKey, Assert.Single(result.Problems).Kind);
    }

    // ── 규칙 ────────────────────────────────────────────────────────────────

    [Fact]
    public void bool에_증감을_쓰면_대신_쓸_말을_알려_준다()
    {
        ChapterGraphModel model = Read("met +1");

        ChapterDiagnostic error = Assert.Single(Errors(model));
        Assert.Contains("met true", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 정수_스탯은_true_false로_정할_수_없다()
    {
        // 소유자 결정 (2026-08-19) — 지정은 bool에만 연다. 정수까지 열면 줄마다 "더할
        // 것인가 정할 것인가"를 고르게 되고, 스탯이 쌓이는 값이라는 성질이 흐려진다.
        ChapterGraphModel model = Read("trust true");

        ChapterDiagnostic error = Assert.Single(Errors(model));
        Assert.Contains("trust +1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 한_간선이_같은_깃발을_두_번_정하면_오류다()
    {
        // 증감은 여러 번이 곧 합계라 뜻이 있지만, 지정은 어느 쪽이 이기는지 아무도 모른다.
        ChapterGraphModel model = Read("met true; met false");

        Assert.Contains(Errors(model), item => item.Message.Contains("두 번 정합니다", StringComparison.Ordinal));
    }

    [Fact]
    public void 깃발을_켜는_챕터는_정상이다()
    {
        ChapterGraphModel model = Read("met true");

        Assert.Empty(Errors(model));

        StatDelta delta = Assert.Single(model.Edges[0].StatChanges);
        Assert.True(delta.IsSet);
    }

    // ── 증명 ────────────────────────────────────────────────────────────────

    [Fact]
    public void 지정은_지금_값을_보지_않는다()
    {
        // 깃발을 켜는 것이 "이전에 켜져 있었는가"에 따라 달라지면 그건 켜는 게 아니다.
        ChapterGraphModel model = Read("met false");

        int met = model.Stats.Select((stat, index) => (stat.Key, index))
            .Single(pair => pair.Key == "met").index;

        int[] before = new int[model.Stats.Count];
        before[met] = 1;   // 이미 켜져 있다

        int[] after = ChapterReachabilityProver.ApplyDeltas(model, before, model.Edges[0].StatChanges);

        Assert.Equal(0, after[met]);
    }

    [Fact]
    public void 켜진_깃발이_잠긴_관문을_연다()
    {
        // 이 기능이 없던 동안 실제로 막혀 있던 것 — 깃발을 보는 관문은 초기값이 거짓이면
        // 영원히 잠겨, 그 뒤 에피소드가 <b>도달 불가</b>로 잡혔다.
        ChapterGraphModel model = ReadWithGate(statChange: "met true", gateLabel: "만났음");

        Assert.Empty(Errors(model));

        ChapterReachabilityResult reach = ChapterReachabilityProver.Prove(model);

        Assert.Contains("끝", reach.ReachableEpisodeIds);
    }

    [Fact]
    public void 깃발을_안_켜면_그_관문_뒤는_여전히_도달_불가다()
    {
        // 위 테스트가 "증명이 그냥 다 통과시킨다"로 통과하는 것이 아님을 붙든다.
        ChapterGraphModel model = ReadWithGate(statChange: null, gateLabel: "만났음");

        ChapterReachabilityResult reach = ChapterReachabilityProver.Prove(model);

        Assert.DoesNotContain("끝", reach.ReachableEpisodeIds);
    }

    // ── 내보내기 ────────────────────────────────────────────────────────────

    [Fact]
    public void 깃발을_켜고_끄는_간선이_Op_Set으로_나간다()
    {
        // 2026-08-19 ~ 08-23 — 계약의 `StatChange`에 '정하기'를 실을 칸이 없어서, 깃발을
        // 쓰는 챕터는 내보내기를 **통째로 거부**했다(`BoolSetNotCarried`). 조용히 빼고
        // 내면 깃발이 영원히 안 켜지고 그 깃발을 보던 관문이 영원히 잠기는데, JSON에
        // 도착한 뒤에는 아무도 그것을 볼 수 없기 때문이다.
        //
        // `ked-progression` **0.2.0**에 `StatChangeDto.Op`가 섰다 — 그래서 거부를 지우고
        // 그 자리를 이 테스트가 이어받는다. 거부가 사라진 것보다 **값이 제대로 실리는
        // 것**이 계약이다.
        Assert.Equal(("Set", 1), OnlyStatChange(Read("met true")));

        // 끄는 쪽도 같은 길로 나간다 — 0이 '안 바꿈'이 아니라 '거짓으로 정함'이다.
        Assert.Equal(("Set", 0), OnlyStatChange(Read("met false")));
    }

    [Fact]
    public void 깃발을_안_쓰는_챕터는_그대로_나간다()
    {
        // 거부가 넓게 걸리면 아무 상관 없는 챕터까지 못 나간다.
        ChapterGraphModel model = Read("trust +1");

        Assert.False(
            ChapterProgressionExporter.Export(model, episodesFolder: null).Refused);

        // ⚠ 정수 증감은 `Add`를 **비우지 않고 명시**한다 — 저쪽은 빈 문자열도 더하기로
        // 읽지만(구 JSON 호환), 적어 두면 "아무도 안 정한 것"과 "더하기로 정한 것"이
        // JSON에서 구별된다.
        Assert.Equal("Add", OnlyStatChange(model).Op);
    }

    [Fact]
    public void 간선의_종류가_JSON에_실린다()
    {
        // v11 `종류` 열 — **누가 고르나**. 이 칸이 없으면 저쪽은 문구가 비었는지로
        // 추론할 수밖에 없어서, 문구를 실수로 지운 것과 의도한 자동 진행이 데이터로
        // 구별되지 않는다(D5).
        //
        // ⚠ 저쪽 `EpisodeOptionDto`에 아직 이 칸이 없다(0.2.0) — 지금은 나가기만 하고
        // 아무 일도 하지 않는다. 그래도 값의 주인은 저작이므로 여기서 붙들어 둔다.
        Assert.Equal("Auto", OnlyOptionKind(Read("trust +1")));
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static IEnumerable<ChapterDiagnostic> Errors(ChapterGraphModel model) =>
        model.Diagnostics.Where(item => item.Severity == ChapterDiagnosticSeverity.Error);

    /// <summary>내보내고 첫 간선 첫 스탯변화의 (Op, Amount).</summary>
    private static (string Op, int Amount) OnlyStatChange(ChapterGraphModel model)
    {
        using System.Text.Json.JsonDocument document = Exported(model);
        System.Text.Json.JsonElement change = document.RootElement
            .GetProperty("Nodes")[0].GetProperty("NextOptions")[0]
            .GetProperty("StatChanges")[0];

        return (change.GetProperty("Op").GetString()!, change.GetProperty("Amount").GetInt32());
    }

    /// <summary>내보내고 첫 간선의 `Kind`.</summary>
    private static string OnlyOptionKind(ChapterGraphModel model)
    {
        using System.Text.Json.JsonDocument document = Exported(model);

        return document.RootElement
            .GetProperty("Nodes")[0].GetProperty("NextOptions")[0]
            .GetProperty("Kind").GetString()!;
    }

    /// <summary>내보내기 — 거부되면 사유를 그대로 들고 넘어진다.</summary>
    private static System.Text.Json.JsonDocument Exported(ChapterGraphModel model)
    {
        ChapterExportResult result = ChapterProgressionExporter.Export(model, episodesFolder: null);

        Assert.False(result.Refused,
            string.Join(" / ", result.Validation.All.Select(item => item.Message)));

        return System.Text.Json.JsonDocument.Parse(result.Json!);
    }

    /// <summary>ep1 → 끝 간선 하나에 그 스탯변화를 적은 챕터.</summary>
    private ChapterGraphModel Read(string statChange) =>
        Build(
            [["ep1", "끝", statChange, null, null, null, "FALSE", null, "자동", null, null]],
            conditions: []);

    /// <summary>ep1 →(관문) 끝. 관문은 깃발이 켜져 있어야 열린다.</summary>
    private ChapterGraphModel ReadWithGate(string? statChange, string gateLabel) =>
        Build(
            [
                ["ep1", "중간", statChange, "간다", null, null, "FALSE", null, "선택지", null, null],
                ["중간", "끝", null, null, null, gateLabel, "FALSE", null, "자동", null, null]
            ],
            conditions: [[gateLabel, "met", "true", null, null]],
            episodes: ["ep1", "중간", "끝"]);

    private ChapterGraphModel Build(
        string?[][] edgeRows,
        string?[][] conditions,
        string[]? episodes = null)
    {
        string path = Path.Combine(_directory, $"ch{Guid.NewGuid():N}.xlsx");
        string[] ids = episodes ?? ["ep1", "끝"];

        using (var workbook = new XLWorkbook())
        {
            Sheet(workbook, ChapterSheetNames.Episodes,
                ["EpisodeId", "제목", "종류", "대사엔트리", "X", "Y", "메모"],
                [.. ids.Select((id, index) =>
                    new string?[] { id, id, "Main", $"Story_{index}", $"{index * 200}", "0", null })]);

            Sheet(workbook, ChapterSheetNames.Edges,
                [
                    "출발", "도착", "스탯변화", "선택지", "표시조건", "해금조건",
                    "잠금시 숨김", "잠금 안내문", "종류", "엔딩키", "연출"
                ],
                edgeRows);

            Sheet(workbook, ChapterSheetNames.Conditions,
                ["라벨", "스탯", "연산자", "값", "설명"], conditions);

            // `met`은 bool, `trust`는 정수 — 두 규칙이 갈리는 자리를 한 챕터에서 본다.
            Sheet(workbook, ChapterSheetNames.Stats,
                ["스탯키", "표시명", "초기값", "최소", "최대", "타입"],
                [
                    ["met", "만났음", "0", "0", "1", "bool"],
                    ["trust", "신뢰", "0", "0", "5", null]
                ]);

            Sheet(workbook, ChapterSheetNames.Choices, ["인덱스", "대본", "메모"], []);

            workbook.SaveAs(path);
        }

        return ChapterWorkbookReader.Read(path);
    }

    private static void Sheet(
        XLWorkbook workbook, string name, string?[] headers, IReadOnlyList<string?[]> rows)
    {
        IXLWorksheet sheet = workbook.AddWorksheet(name);

        for (int column = 0; column < headers.Length; column++)
        {
            sheet.Cell(1, column + 1).SetValue(headers[column]);
        }

        for (int row = 0; row < rows.Count; row++)
        {
            for (int column = 0; column < rows[row].Length; column++)
            {
                if (rows[row][column] is { } value)
                {
                    sheet.Cell(row + 2, column + 1).SetValue(value);
                }
            }
        }
    }
}
