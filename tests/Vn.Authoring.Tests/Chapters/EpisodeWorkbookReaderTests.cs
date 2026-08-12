using ClosedXML.Excel;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// G2-a — 에피소드 워크북(§3.2 11열)을 정확히 읽고 §3.3 구조 규칙을 잡는다.
///
/// 견본 워크북의 `견본_에피소드 main05.02` 시트가 규격의 실물이다. 그 시트가 챕터 워크북 안에
/// 들어 있어서, 머리글로 시트를 찾는 방식이 실제로 필요하다(첫 시트를 읽으면 `에피소드` 시트를
/// 읽는다).
/// </summary>
public sealed class EpisodeWorkbookReaderTests : IDisposable
{
    private static readonly string[] Labels = ["신뢰높음", "분노누적", "지쳐있음", "복도완료"];
    private static readonly string[] Stats = ["trust", "anger", "fatigue"];

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-episode-tests", Guid.NewGuid().ToString("N"));

    public EpisodeWorkbookReaderTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    // ── 견본 ────────────────────────────────────────────────────────────────

    [Fact]
    public void 견본_에피소드_시트를_머리글로_찾아_오류_없이_읽는다()
    {
        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(SamplePath, Labels, Stats);

        Assert.Empty(model.Errors);
        Assert.Equal("견본_에피소드 main05.02", model.SheetName);
        Assert.Equal(14, model.Rows.Count);
    }

    [Fact]
    public void 행_유형이_규격대로_읽힌다()
    {
        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(SamplePath, Labels, Stats);

        Assert.Equal(EpisodeRowKind.Dialogue, model.FindByIndex(10)!.Kind);
        Assert.Equal(EpisodeRowKind.If, model.FindByIndex(30)!.Kind);
        Assert.Equal(EpisodeRowKind.Choice, model.FindByIndex(70)!.Kind);
        Assert.Equal(EpisodeRowKind.Option, model.FindByIndex(71)!.Kind);

        // IF 행은 라인이 아니다 — LineId가 없고 연출·세이브 타깃이 아니다.
        Assert.Null(model.FindByIndex(30)!.LineId);
        Assert.False(model.FindByIndex(30)!.IsLine);

        // 선택지는 라인이다.
        Assert.Equal("ln_0007", model.FindByIndex(71)!.LineId);
        Assert.True(model.FindByIndex(71)!.IsLine);
    }

    [Fact]
    public void 구간이_INPUT부터_OUT까지_양끝_포함으로_묶인다()
    {
        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(SamplePath, Labels, Stats);

        Assert.Equal(2, model.Sections.Count);

        // §3.3 규칙 3은 검사가 아니라 정의다 — 정의대로 묶였는지를 고정한다.
        EpisodeSection condition = model.Sections[900];
        Assert.Equal([900, 905, 908], condition.Rows.Select(row => row.Index));
        Assert.Equal("40", condition.OutTarget);

        EpisodeSection option = model.Sections[920];
        Assert.Equal([920, 928], option.Rows.Select(row => row.Index));
        Assert.Equal(EpisodeFlow.EndMarker, option.OutTarget);
    }

    [Fact]
    public void 주_흐름은_구간에_속하지_않는_행들이다()
    {
        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(SamplePath, Labels, Stats);

        Assert.Equal(
            [10, 20, 30, 40, 50, 60, 70, 71, 72],
            model.MainFlow.Select(row => row.Index));
    }

    [Fact]
    public void IN은_IF와_OPTION_양쪽에_붙는다()
    {
        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(SamplePath, Labels, Stats);

        Assert.Equal(900, model.FindByIndex(30)!.In);   // 조건이 구간으로
        Assert.Equal(920, model.FindByIndex(71)!.In);   // 선택지 옵션도 구간으로 (G-6c)
        Assert.Null(model.FindByIndex(72)!.In);
    }

    [Fact]
    public void 스탯변화가_정수_증감으로_읽힌다()
    {
        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(SamplePath, Labels, Stats);

        StatDelta fatigue = Assert.Single(model.FindByIndex(20)!.StatChanges);
        Assert.Equal("fatigue", fatigue.Key);
        Assert.Equal(1, fatigue.Amount);

        Assert.Equal("trust", Assert.Single(model.FindByIndex(50)!.StatChanges).Key);
        Assert.Empty(model.FindByIndex(10)!.StatChanges);
    }

    [Fact]
    public void 인덱스가_없는_설명_줄은_표의_행으로_읽지_않고_알린다()
    {
        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(SamplePath, Labels, Stats);

        // 견본 시트 아래쪽 설명문들. 조용히 버리지 않고 알림으로 남긴다(규칙 14).
        Assert.Contains(model.Diagnostics, item =>
            item.Severity == ChapterDiagnosticSeverity.Info &&
            item.Code == ChapterDiagnosticCode.EpisodeIdBlank);
    }

    // ── §3.3 강제 규칙 ──────────────────────────────────────────────────────

    [Fact]
    public void 규칙1_IN_대상에_INPUT이_없으면_오류다()
    {
        var rows = Baseline();
        rows[3][5] = "999";  // IF 행의 IN을 없는 구간으로

        ChapterDiagnostic problem = SingleError(rows);

        Assert.Equal("F", problem.Column);
        Assert.Contains("INPUT 태그가 없습니다", problem.Message);
    }

    [Fact]
    public void 규칙2_INPUT에_짝_OUT이_없으면_오류다()
    {
        var rows = Baseline();
        rows[8][3] = null;  // 구간 끝 행(908)의 OUT 태그 제거
        rows[8][6] = null;

        ChapterDiagnostic problem = SingleError(rows, "짝이 되는 OUT이 없습니다");

        Assert.Equal("D", problem.Column);
        Assert.Contains("짝은 강제", problem.Message);
    }

    [Fact]
    public void 규칙4_한_구간을_둘이_가리키면_오류다()
    {
        var rows = Baseline();
        rows[4][2] = "IF";        // 40행을 두 번째 IF로 바꾼다
        rows[4][4] = "신뢰높음";
        rows[4][5] = "900";       // 같은 구간을 또 가리킨다
        rows[4][1] = null;        // IF 행은 LineId를 갖지 않는다

        ChapterDiagnostic problem = SingleError(rows, "이미 가리키고 있습니다");

        Assert.Contains("구간 재사용 금지", problem.Message);
        Assert.Contains("LineId 전역 유일성", problem.Message);
    }

    [Fact]
    public void 규칙5_구간_안에서_또_IN을_열면_오류다()
    {
        var rows = Baseline();
        // 구간(900~908) 안쪽 행을 IF로 바꿔 IN을 열게 한다.
        rows[6][2] = "IF";
        rows[6][1] = null;
        rows[6][4] = "신뢰높음";
        rows[6][5] = "900";

        ChapterDiagnostic problem = SingleError(rows, "중첩 금지");

        Assert.Equal("F", problem.Column);
        Assert.Contains("경계가 모호", problem.Message);
    }

    [Fact]
    public void 아무도_가리키지_않는_구간은_경고로_알린다()
    {
        var rows = Baseline();
        rows[3][5] = null;  // IF 행의 IN 제거 → 구간이 고아가 된다

        EpisodeWorkbookModel model = Read(rows);

        ChapterDiagnostic warning = Assert.Single(
            model.Diagnostics, item => item.Severity == ChapterDiagnosticSeverity.Warning);

        Assert.Contains("가리키는 IN이 없습니다", warning.Message);
        Assert.Contains("산출물에", warning.Message);
    }

    // ── §3.2 구조 ───────────────────────────────────────────────────────────

    [Fact]
    public void IF_행이_LineId를_가지면_오류다()
    {
        var rows = Baseline();
        rows[3][1] = "ln_9999";

        ChapterDiagnostic problem = SingleError(rows, "라인이 아니므로");

        Assert.Equal("B", problem.Column);
    }

    [Fact]
    public void 미정의_조건라벨은_오류다()
    {
        var rows = Baseline();
        rows[3][4] = "없는라벨";

        ChapterDiagnostic problem = SingleError(rows, "없는라벨");

        Assert.Equal("E", problem.Column);
        Assert.Contains("`조건` 시트", problem.Message);
    }

    [Fact]
    public void 인덱스가_정수가_아니면_오류다()
    {
        var rows = Baseline();
        rows[1][0] = "십";

        ChapterDiagnostic problem = SingleError(rows, "정수가 아닙니다");

        Assert.Equal("A", problem.Column);
        Assert.Contains("10·20·30", problem.Message);
    }

    [Fact]
    public void 인덱스가_역전되면_오류다()
    {
        var rows = Baseline();
        rows[2][0] = "5";  // 앞 행(10)보다 작다

        ChapterDiagnostic problem = SingleError(rows, "보다 작습니다");

        Assert.Equal("A", problem.Column);
    }

    [Fact]
    public void OUT_태그에_목적지가_없으면_오류다()
    {
        var rows = Baseline();
        rows[8][6] = null;

        ChapterDiagnostic problem = SingleError(rows, "나갈 목적지를 적어야");

        Assert.Equal("G", problem.Column);
    }

    [Fact]
    public void 스탯변화의_미등록_키는_오류다()
    {
        var rows = Baseline();
        rows[1][9] = "karma +1";

        ChapterDiagnostic problem = SingleError(rows, "karma");

        Assert.Equal("J", problem.Column);
        Assert.Contains("trust", problem.Message);
    }

    [Fact]
    public void 스탯변화의_소수점은_오류다()
    {
        var rows = Baseline();
        rows[1][9] = "trust +1.5";

        ChapterDiagnostic problem = SingleError(rows, "정수");

        Assert.Equal("J", problem.Column);
    }

    [Fact]
    public void 머리글이_규격과_맞는_시트가_없으면_오류다()
    {
        string path = Path.Combine(_directory, "빈.xlsx");

        using (var workbook = new XLWorkbook())
        {
            workbook.AddWorksheet("아무거나").Cell(1, 1).SetValue("딴것");
            workbook.SaveAs(path);
        }

        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(path, Labels, Stats);

        ChapterDiagnostic problem = Assert.Single(model.Errors);
        Assert.Equal(ChapterDiagnosticCode.SheetMissing, problem.Code);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private ChapterDiagnostic SingleError(string?[][] rows, string? contains = null)
    {
        EpisodeWorkbookModel model = Read(rows);
        List<ChapterDiagnostic> errors = model.Errors
            .Where(item => contains is null || item.Message.Contains(contains, StringComparison.Ordinal))
            .ToList();

        return Assert.Single(errors);
    }

    private EpisodeWorkbookModel Read(string?[][] rows)
    {
        string path = Path.Combine(_directory, $"ep_{Guid.NewGuid():N}.xlsx");

        using (var workbook = new XLWorkbook())
        {
            IXLWorksheet sheet = workbook.AddWorksheet("본문");

            for (int row = 0; row < rows.Length; row++)
            {
                for (int column = 0; column < rows[row].Length; column++)
                {
                    if (rows[row][column] is { Length: > 0 } value)
                    {
                        sheet.Cell(row + 1, column + 1).SetValue(value);
                    }
                }
            }

            workbook.SaveAs(path);
        }

        return EpisodeWorkbookReader.Read(path, Labels, Stats);
    }

    /// <summary>견본과 같은 모양의 최소 에피소드. 각 테스트는 한 칸만 망가뜨린다.</summary>
    private static string?[][] Baseline() =>
    [
        ["인덱스", "LineId", "유형", "태그", "조건라벨", "IN", "OUT", "화자", "내용", "스탯변화", "메모"],
        ["10", "ln_0001", null, null, null, null, null, "윌로", "첫 줄", null, null],
        ["20", "ln_0002", null, null, null, null, null, "라루", "둘째 줄", null, null],
        ["30", null, "IF", null, "신뢰높음", "900", null, null, null, null, null],
        ["40", "ln_0003", null, null, null, null, null, "라루", "수렴 지점", null, null],
        ["50", "ln_0004", null, null, null, null, null, "윌로", "끝 줄", null, null],
        ["900", "ln_0100", null, "INPUT", null, null, null, "윌로", "구간 첫 줄", null, null],
        ["905", "ln_0101", null, null, null, null, null, "라루", "구간 가운데", null, null],
        ["908", "ln_0102", null, "OUT", null, null, "40", "윌로", "구간 끝", null, null]
    ];
}
