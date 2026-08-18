using ClosedXML.Excel;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 간선의 `종류`·`엔딩키`·`연출` (v11, 2026-08-18) — 규격은
/// <c>docs/work-orders/edge-presentation-orders.md</c>.
///
/// 세 축이 <b>직교</b>한다: 누가 고르나(종류) · 끝나는가(엔딩키) · 무엇을 재생하나(연출).
/// 하나의 enum으로 뭉치면 "선택지 → 엔딩"이나 "자동 → 엔딩" 중 하나가 표현 불가가 된다.
/// </summary>
public sealed class EdgeKindAndEndingTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-edge-v11", Guid.NewGuid().ToString("N"));

    public EdgeKindAndEndingTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 네_조합이_모두_표현된다()
    {
        ChapterGraphModel model = Read(
            ["ep1", "ep2", null, "믿는다", null, null, "FALSE", null, "선택지", null, null],
            ["ep1", "ep2", null, null, null, null, "FALSE", null, "자동", null, "페이드"],
            ["ep2", "끝", null, "이대로 떠난다", null, null, "FALSE", null, "선택지", "ch_bad", null],
            ["ep2", "끝", null, null, null, null, "FALSE", null, "자동", "ch_bad", "암전"]);

        Assert.Empty(Errors(model));

        ChapterEdge[] edges = model.Edges.ToArray();

        Assert.Equal(EdgeKind.Choice, edges[0].Kind);
        Assert.False(edges[0].IsEnding);

        Assert.Equal(EdgeKind.Auto, edges[1].Kind);
        Assert.False(edges[1].IsEnding);
        Assert.Equal("페이드", edges[1].PresentationNodeName);

        Assert.Equal(EdgeKind.Choice, edges[2].Kind);
        Assert.True(edges[2].IsEnding);
        Assert.Equal("ch_bad", edges[2].EndingKey);

        Assert.Equal(EdgeKind.Auto, edges[3].Kind);
        Assert.True(edges[3].IsEnding);
        Assert.Equal("암전", edges[3].PresentationNodeName);
    }

    [Fact]
    public void 종류가_선택지인데_문구가_비면_오류다()
    {
        // `ked-progression` D5 — 이 검사가 없으면 **문구를 실수로 지운 것**이
        // **의도한 자동 진행**과 구별되지 않는다.
        ChapterGraphModel model = Read(
            ["ep1", "ep2", null, null, null, null, "FALSE", null, "선택지", null, null]);

        ChapterDiagnostic error = Assert.Single(
            Errors(model), item => item.Code == ChapterDiagnosticCode.EdgeKindMismatch);

        Assert.Contains("문구가 비었습니다", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 종류가_자동인데_문구가_있으면_오류다()
    {
        ChapterGraphModel model = Read(
            ["ep1", "ep2", null, "믿는다", null, null, "FALSE", null, "자동", null, null]);

        Assert.Single(Errors(model), item => item.Code == ChapterDiagnosticCode.EdgeKindMismatch);
    }

    [Fact]
    public void 종류가_비면_문구를_보고_정한다()
    {
        // 구판 호환 — 이행기가 채우지 못한 파일도 예전과 같이 읽혀야 한다.
        ChapterGraphModel model = Read(
            ["ep1", "ep2", null, "믿는다", null, null, "FALSE", null, null, null, null],
            ["ep1", "ep2", null, null, null, null, "FALSE", null, null, null, null]);

        Assert.Empty(Errors(model));
        Assert.Equal(EdgeKind.Choice, model.Edges.ElementAt(0).Kind);
        Assert.Equal(EdgeKind.Auto, model.Edges.ElementAt(1).Kind);
    }

    [Fact]
    public void 모르는_종류는_조용히_고치지_않는다()
    {
        ChapterGraphModel model = Read(
            ["ep1", "ep2", null, "믿는다", null, null, "FALSE", null, "엔딩", null, null]);

        ChapterDiagnostic error = Assert.Single(
            Errors(model), item => item.Code == ChapterDiagnosticCode.EdgeKindUnknown);

        Assert.Contains("`선택지` 또는 `자동`", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 같은_도착의_엔딩키가_다르면_오류다()
    {
        // ⚠ 검증 소유 경계의 예외 — 계약에서 엔딩키는 도착 에피소드 하나에 하나이고,
        // JSON에 도착한 시점에는 이미 키가 하나라 수입기가 이것을 볼 수 없다.
        ChapterGraphModel model = Read(
            ["ep1", "끝", null, "이대로", null, null, "FALSE", null, "선택지", "ch_bad", null],
            ["ep2", "끝", null, "저대로", null, null, "FALSE", null, "선택지", "ch_true", null]);

        ChapterDiagnostic error = Assert.Single(
            Errors(model), item => item.Code == ChapterDiagnosticCode.EndingKeyConflict);

        Assert.Contains("ch_bad", error.Message, StringComparison.Ordinal);
        Assert.Contains("ch_true", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 같은_도착에_같은_엔딩키_여럿은_정상이다()
    {
        // 여러 길이 한 엔딩으로 모이는 것은 흔한 패턴이다.
        ChapterGraphModel model = Read(
            ["ep1", "끝", null, "이대로", null, null, "FALSE", null, "선택지", "ch_bad", null],
            ["ep2", "끝", null, "저대로", null, null, "FALSE", null, "선택지", "ch_bad", null]);

        Assert.Empty(Errors(model));
    }

    [Fact]
    public void 엔딩키는_도착_에피소드로_옮겨_실린다()
    {
        // 저작에서는 간선의 것, 계약에서는 도착 에피소드의 것 — 내보내기가 번역한다.
        ChapterGraphModel model = Read(
            ["ep1", "ep2", null, null, null, null, "FALSE", null, "자동", null, null],
            ["ep2", "끝", null, null, null, null, "FALSE", null, "자동", "ch_bad", null]);

        Assert.Empty(Errors(model));

        ChapterExportResult result = ChapterProgressionExporter.Export(model, episodesFolder: null);
        Assert.False(result.Refused,
            string.Join(" / ", result.Validation.All.Select(item => item.Message)));

        using var document = System.Text.Json.JsonDocument.Parse(result.Json!);

        System.Text.Json.JsonElement ending = document.RootElement
            .GetProperty("Nodes")
            .EnumerateArray()
            .Single(node => node.GetProperty("EpisodeId").GetString() == "끝");

        Assert.Equal("ch_bad", ending.GetProperty("EndingKey").GetString());
        Assert.True(ending.GetProperty("IsChapterEndingCandidate").GetBoolean());
    }

    [Fact]
    public void 연출_이름이_ViaNodeId라는_이름으로_나간다()
    {
        // v11 §6. ⚠ 이 테스트는 <b>키 이름 글자 자체</b>를 붙든다 (`ked-progression` 요청).
        //
        // 저작 쪽 이름은 `PresentationNodeName`이고 계약 쪽은 `ViaNodeId`다. 틀린 이름으로
        // 내면 저쪽 역직렬화기가 모르는 속성을 <b>기본으로 무시</b>해서 값이 빈 문자열로
        // 남는다 — 오류 하나 없이 연출만 사라진다. 저쪽의 "연출이 비었습니다" 경고도
        // 못 잡는다: 그건 모델을 보는 검사이고 모델에는 값이 멀쩡히 있다.
        ChapterGraphModel model = Read(
            ["ep1", "ep2", null, "믿는다", null, null, "FALSE", null, "선택지", null, "fade_trust"],
            ["ep2", "끝", null, null, null, null, "FALSE", null, "자동", "ch_bad", "엔딩 ch_bad"]);

        Assert.Empty(Errors(model));

        ChapterExportResult result = ChapterProgressionExporter.Export(model, episodesFolder: null);
        Assert.False(result.Refused,
            string.Join(" / ", result.Validation.All.Select(item => item.Message)));

        using var document = System.Text.Json.JsonDocument.Parse(result.Json!);

        System.Text.Json.JsonElement Option(string episodeId) =>
            document.RootElement.GetProperty("Nodes").EnumerateArray()
                .Single(node => node.GetProperty("EpisodeId").GetString() == episodeId)
                .GetProperty("NextOptions").EnumerateArray().Single();

        Assert.Equal("fade_trust", Option("ep1").GetProperty("ViaNodeId").GetString());

        // 자동 진행 간선에도 붙는다 — 엔딩 전이가 바로 그 모양이다.
        Assert.Equal("엔딩 ch_bad", Option("ep2").GetProperty("ViaNodeId").GetString());
    }

    [Fact]
    public void 연출이_없는_간선은_ViaNodeId가_빈_문자열이다()
    {
        // 키 자체는 언제나 나간다 — 없을 때만 사라지면 "빠뜨린 것"과 "없는 것"이 같아진다.
        ChapterGraphModel model = Read(
            ["ep1", "ep2", null, "믿는다", null, null, "FALSE", null, "선택지", null, null],
            ["ep2", "끝", null, null, null, null, "FALSE", null, "자동", null, null]);

        ChapterExportResult result = ChapterProgressionExporter.Export(model, episodesFolder: null);
        Assert.False(result.Refused,
            string.Join(" / ", result.Validation.All.Select(item => item.Message)));

        using var document = System.Text.Json.JsonDocument.Parse(result.Json!);

        Assert.Equal(
            string.Empty,
            document.RootElement.GetProperty("Nodes").EnumerateArray()
                .Single(node => node.GetProperty("EpisodeId").GetString() == "ep1")
                .GetProperty("NextOptions").EnumerateArray().Single()
                .GetProperty("ViaNodeId").GetString());
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static IEnumerable<ChapterDiagnostic> Errors(ChapterGraphModel model) =>
        model.Diagnostics.Where(item => item.Severity == ChapterDiagnosticSeverity.Error);

    /// <summary>간선 행들을 담은 워크북을 만들어 읽는다. 에피소드 셋(ep1·ep2·끝)은 고정이다.</summary>
    private ChapterGraphModel Read(params string?[][] edgeRows)
    {
        string path = Path.Combine(_directory, $"ch{Guid.NewGuid():N}.xlsx");

        using (var workbook = new XLWorkbook())
        {
            Sheet(workbook, ChapterSheetNames.Episodes,
                ["EpisodeId", "제목", "종류", "대사엔트리", "X", "Y", "메모"],
                [
                    ["ep1", "첫 화", "Main", "Story_ep1", "0", "0", null],
                    ["ep2", "둘째 화", "Main", "Story_ep2", "200", "0", null],
                    ["끝", "마지막", "Main", "Story_end", "400", "0", null]
                ]);

            Sheet(workbook, ChapterSheetNames.Edges,
                [
                    "출발", "도착", "스탯변화", "선택지", "표시조건", "해금조건",
                    "잠금시 숨김", "잠금 안내문", "종류", "엔딩키", "연출"
                ],
                edgeRows);

            Sheet(workbook, ChapterSheetNames.Conditions, ["라벨", "스탯", "연산자", "값", "설명"], []);
            // 스탯 둘 — 규격이 2~5개를 전제하므로 비우면 내보내기가 거부된다(§0).
            Sheet(workbook, ChapterSheetNames.Stats,
                ["스탯키", "표시명", "초기값", "최소", "최대", "타입"],
                [
                    ["trust", "신뢰", "0", "0", "5", null],
                    ["anger", "분노", "0", "0", "5", null]
                ]);
            Sheet(workbook, ChapterSheetNames.Choices, ["인덱스", "대본", "메모"], []);

            workbook.SaveAs(path);
        }

        return ChapterWorkbookReader.Read(path);
    }

    private static void Sheet(
        XLWorkbook workbook, string name, string[] headers, IReadOnlyList<string?[]> rows)
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
