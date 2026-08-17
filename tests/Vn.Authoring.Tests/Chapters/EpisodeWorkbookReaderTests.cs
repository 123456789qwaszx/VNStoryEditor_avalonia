using ClosedXML.Excel;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// G2-a — 에피소드 워크북(§3.2 6열, v10)을 정확히 읽고 블록 규칙을 잡는다.
///
/// 견본 워크북의 `견본_에피소드 main05.02` 시트가 규격의 실물이다. 그 시트가 챕터 워크북 안에
/// 들어 있어서, 머리글로 시트를 찾는 방식이 실제로 필요하다(첫 시트를 읽으면 `에피소드` 시트를
/// 읽는다).
///
/// <b>v10에서 규칙이 하나로 줄었다</b> — 구판의 §3.3 규칙 1·2·4·5·6(구간 대상 존재 · INPUT/OUT
/// 짝 · 구간 재사용 금지 · 중첩 금지 · OUT 대조)은 <c>IF</c>~<c>ENDIF</c> 짝 하나로 대체됐다.
/// </summary>
public sealed class EpisodeWorkbookReaderTests : IDisposable
{
    private static readonly string[] Labels = ["신뢰높음", "분노누적", "지쳐있음", "복도완료"];

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
        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(SamplePath, Labels);

        Assert.Empty(model.Errors);
        Assert.Equal("견본_에피소드 main05.02", model.SheetName);
        Assert.Equal(11, model.Rows.Count);
    }

    [Fact]
    public void 행_유형이_규격대로_읽힌다()
    {
        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(SamplePath, Labels);

        Assert.Equal(EpisodeRowKind.Dialogue, model.FindByIndex(10)!.Kind);
        Assert.Equal(EpisodeRowKind.If, model.FindByIndex(30)!.Kind);
        Assert.Equal(EpisodeRowKind.End, model.FindByIndex(70)!.Kind);
        Assert.Equal(EpisodeRowKind.ElseIf, model.FindByIndex(80)!.Kind);

        // IF·ENDIF는 라인이 아니다 — LineId가 없고 연출·세이브 타깃이 아니다.
        Assert.False(model.FindByIndex(30)!.IsLine);
        Assert.False(model.FindByIndex(70)!.IsLine);
        Assert.True(model.FindByIndex(10)!.IsLine);
    }

    [Fact]
    public void 견본은_중첩과_ELSEIF를_함께_보여_준다()
    {
        // 구판에서는 중첩이 금지였다 — 규격의 실물이 그 변화를 담는다.
        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(SamplePath, Labels);

        Assert.Equal("신뢰높음", model.FindByIndex(30)!.ConditionLabel);
        Assert.Equal("지쳐있음", model.FindByIndex(50)!.ConditionLabel);   // 두 겹 안
        Assert.Equal("분노누적", model.FindByIndex(80)!.ConditionLabel);   // 같은 체인의 다른 갈래
        Assert.Empty(model.Errors);
    }

    // ── 블록 짝 (v10의 유일한 구조 규칙) ────────────────────────────────────

    [Fact]
    public void IF가_ENDIF로_안_닫히면_오류다()
    {
        var rows = Baseline();
        rows[8] = ["80", null, null, null, "윌로", "닫는 줄이었던 자리"];

        ChapterDiagnostic problem = SingleError(rows, "닫히지 않았습니다");

        Assert.Equal("C", problem.Column);
        Assert.Contains("어디까지가 조건 안인지", problem.Message);
    }

    [Fact]
    public void 짝_없는_ENDIF는_오류다()
    {
        var rows = Baseline();
        rows[2] = ["20", null, "ENDIF", null, null, null];  // 열린 IF가 없는 자리

        ChapterDiagnostic problem = SingleError(rows, "닫을 IF가 없는");

        Assert.Equal("C", problem.Column);
    }

    [Fact]
    public void 블록_둘이_나란히_있으면_문제가_없다()
    {
        var rows = Baseline();

        EpisodeWorkbookModel model = Read(rows);

        Assert.Empty(model.Errors);
        Assert.Equal(2, model.Rows.Count(row => row.Kind == EpisodeRowKind.If));
        Assert.Equal(2, model.Rows.Count(row => row.Kind == EpisodeRowKind.End));
    }

    [Fact]
    public void 중첩된_블록도_문제가_없다()
    {
        // 구판 규칙 5(중첩 금지)의 폐지를 고정한다. 작가 판의 줄이 전환 여럿을 담게
        // 아래층을 고쳐서 열렸다 — 겹쳐 닫는 <<endif>>들이 한 줄 앞에 몰려도 된다.
        var rows = Baseline();
        rows[5] = ["50", null, "IF", "지쳐있음", null, null];   // 첫 ENDIF 자리에 IF
        // 이제 IF가 셋(30·50·60)이라 닫는 줄도 셋이어야 한다.
        rows = [.. rows, ["100", null, "ENDIF", null, null, null], ["110", null, "ENDIF", null, null, null]];

        Assert.Empty(Read(rows).Errors);
    }

    [Fact]
    public void ELSEIF는_열린_IF가_있어야_한다()
    {
        var rows = Baseline();
        rows[1] = ["10", null, "ELSEIF", "신뢰높음", null, null];

        ChapterDiagnostic problem = SingleError(rows, "열린 IF가 없는 ELSEIF");

        Assert.Equal("C", problem.Column);
    }

    [Fact]
    public void IF에_조건라벨이_없으면_오류다()
    {
        var rows = Baseline();
        rows[3][3] = null;

        ChapterDiagnostic problem = SingleError(rows, "조건라벨이 없습니다");

        Assert.Equal("D", problem.Column);
        Assert.Contains("무엇을 가르는지", problem.Message);
    }

    [Fact]
    public void ENDIF에_대사를_적으면_오류다()
    {
        var rows = Baseline();
        rows[5][4] = "윌로";
        rows[5][5] = "여기 붙이면 어느 쪽인가";

        ChapterDiagnostic problem = SingleError(rows, "블록을 닫기만 합니다");

        Assert.Equal("F", problem.Column);
    }

    // ── §3.2 구조 ───────────────────────────────────────────────────────────

    [Fact]
    public void IF_행이_LineId를_가지면_오류다()
    {
        var rows = Baseline();
        rows[3][1] = "ln_9999";

        ChapterDiagnostic problem = SingleError(rows, "라인이 아니므로");

        Assert.Equal("B", problem.Column);
        Assert.Contains("IF 행", problem.Message);
    }

    [Fact]
    public void 미정의_조건라벨은_오류다()
    {
        var rows = Baseline();
        rows[3][3] = "없는라벨";

        ChapterDiagnostic problem = SingleError(rows, "없는라벨");

        Assert.Equal("D", problem.Column);
        Assert.Contains("`조건` 시트", problem.Message);
    }

    [Fact]
    public void 대사_행에_조건라벨을_붙이면_오류다()
    {
        var rows = Baseline();
        rows[1][3] = "신뢰높음";

        ChapterDiagnostic problem = SingleError(rows, "IF 행에만 붙습니다");

        Assert.Equal("D", problem.Column);
    }

    [Fact]
    public void 유형_대사는_빈칸과_같은_뜻이다()
    {
        // 2026-08-17 소유자 — 드롭다운에서 고를 수 있어야 한다. 빈칸도 그대로 대사다.
        var rows = Baseline();
        rows[1][2] = "대사";

        EpisodeWorkbookModel model = Read(rows);

        Assert.Empty(model.Errors);
        Assert.Equal(EpisodeRowKind.Dialogue, model.FindByIndex(10)!.Kind);
    }

    [Fact]
    public void END도_ENDIF와_같은_뜻으로_받는다()
    {
        // 정본은 ENDIF지만 사람이 END를 치는 것도 흔하다 — 뜻이 하나뿐이라 받아도 안전하다.
        var rows = Baseline();
        rows[5][2] = "END";

        Assert.Empty(Read(rows).Errors);
    }

    [Fact]
    public void 폐지된_CHOICE_OPTION은_어디로_가야_하는지까지_말한다()
    {
        // v9에서 선택지의 주인이 챕터 시트로 갔다 — 대본에 남은 낱말은 옮기다 만 흔적이다.
        var rows = Baseline();
        rows[1][2] = "OPTION";

        ChapterDiagnostic problem = SingleError(rows, "폐지됐습니다");

        Assert.Equal("C", problem.Column);
        Assert.Contains("`선택지` 시트", problem.Message);
        Assert.Contains("`간선` 시트", problem.Message);
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
    public void 인덱스_역전은_이제_알림이다()
    {
        // v10 — 읽는 순서는 시트의 행 순서다. 인덱스는 줄의 신원일 뿐이라 역전이 동작을
        // 바꾸지 않는다. 이행기가 구간을 옮기며 번호를 그대로 두는 것도 이 완화 덕이다.
        var rows = Baseline();
        rows[2][0] = "5";

        EpisodeWorkbookModel model = Read(rows);

        Assert.Empty(model.Errors);
        Assert.Contains(model.Diagnostics, item =>
            item.Severity == ChapterDiagnosticSeverity.Info &&
            item.Message.Contains("보다 작습니다", StringComparison.Ordinal));
    }

    [Fact]
    public void 인덱스_중복은_여전히_오류다()
    {
        // 신원이 겹치면 연출이 어느 줄에 붙는지 정해지지 않는다.
        var rows = Baseline();
        rows[2][0] = "10";

        ChapterDiagnostic problem = SingleError(rows, "중복입니다");

        Assert.Equal("A", problem.Column);
    }

    [Fact]
    public void 인덱스가_없는_설명_줄은_표의_행으로_읽지_않고_알린다()
    {
        var rows = Baseline();
        rows = [.. rows, [null, null, null, null, null, "이건 설명문입니다"]];

        EpisodeWorkbookModel model = Read(rows);

        Assert.Contains(model.Diagnostics, item =>
            item.Code == ChapterDiagnosticCode.EpisodeIdBlank &&
            item.Message.Contains("A열에 번호를 적어", StringComparison.Ordinal));
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

        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(path, Labels);

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

        return EpisodeWorkbookReader.Read(path, Labels);
    }

    /// <summary>견본과 같은 모양의 최소 에피소드(중첩 포함). 각 테스트는 한 칸만 망가뜨린다.</summary>
    private static string?[][] Baseline() =>
    [
        ["인덱스", "LineId", "유형", "조건라벨", "화자", "내용"],
        ["10", "ln_0001", null, null, "윌로", "첫 줄"],
        ["20", "ln_0002", null, null, "라루", "둘째 줄"],
        ["30", null, "IF", "신뢰높음", null, null],
        ["40", "ln_0003", null, null, "윌로", "첫 블록 안"],
        ["50", null, "ENDIF", null, null, null],
        ["60", null, "IF", "지쳐있음", null, null],
        ["70", "ln_0004", null, null, "라루", "둘째 블록 안"],
        ["80", null, "ENDIF", null, null, null],
        ["90", "ln_0005", null, null, "윌로", "끝 줄"]
    ];
}
