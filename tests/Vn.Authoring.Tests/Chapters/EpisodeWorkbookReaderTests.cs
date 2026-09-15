using ClosedXML.Excel;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// G2-a — 에피소드 워크북(§3.2 4열, v15)을 정확히 읽는다.
///
/// <b>v15에서 구조 규칙이 전부 사라졌다</b> — `유형`·`조건라벨`이 폐지되면서 그 둘이 그리던
/// 조건 블록(IF~ENDIF 짝·중첩·블록 행 빗장)이 함께 없어졌다. 이제 대본의 모든 행은 대사이고,
/// 리더가 지키는 규칙은 <b>"인덱스가 줄의 신원이다"</b> 하나뿐이다.
///
/// 시트를 <b>머리글로</b> 찾는 방식은 그대로다 — 대본 시트가 첫 시트가 아닌 워크북이 실제로
/// 있기 때문이다(설명 시트가 앞에 오는 경우).
/// </summary>
public sealed class EpisodeWorkbookReaderTests : IDisposable
{
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

    // ── 시트 찾기 ───────────────────────────────────────────────────────────

    [Fact]
    public void 대본_시트를_머리글로_찾는다()
    {
        // ⚠ 첫 시트를 무조건 읽으면 안 된다 — 설명 시트가 앞에 오는 워크북이 실제로 있다.
        // 규격의 실물이 필요하면 이미터 산출물을 쓴다(`docs/chapter-graph-sample.xlsx`는
        // 구판이라 열면 이행된다).
        string path = Path.Combine(_directory, "앞에딴시트.xlsx");

        using (var workbook = new XLWorkbook())
        {
            workbook.AddWorksheet("읽어주세요").Cell(1, 1).SetValue("이 시트는 대본이 아니다");

            IXLWorksheet script = workbook.AddWorksheet("대본");
            Fill(script, Baseline());

            workbook.SaveAs(path);
        }

        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(path);

        Assert.Empty(model.Errors);
        Assert.Equal("대본", model.SheetName);
        Assert.Equal(4, model.Rows.Count);
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

        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(path);

        ChapterDiagnostic problem = Assert.Single(model.Errors);
        Assert.Equal(ChapterDiagnosticCode.SheetMissing, problem.Code);
    }

    // ── 모든 행이 대사다 (v15) ──────────────────────────────────────────────

    [Fact]
    public void 모든_행이_대사로_읽힌다()
    {
        EpisodeWorkbookModel model = Read(Baseline());

        Assert.Empty(model.Errors);
        Assert.All(model.Rows, row => Assert.True(row.IsLine));
        Assert.Equal([10, 20, 40, 90], model.Rows.Select(row => row.Index));
        Assert.Equal("윌로", model.FindByIndex(10)!.Speaker);
    }

    // ── 인덱스가 줄의 신원이다 ──────────────────────────────────────────────

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
    public void 인덱스_중복은_오류다()
    {
        // 신원이 겹치면 연출이 어느 줄에 붙는지 정해지지 않는다.
        var rows = Baseline();
        rows[2][0] = "10";

        ChapterDiagnostic problem = SingleError(rows, "중복입니다");

        Assert.Equal("A", problem.Column);
    }

    [Fact]
    public void 인덱스_역전은_알림이다()
    {
        // v10 — 읽는 순서는 시트의 행 순서다. 인덱스는 줄의 신원일 뿐이라 역전이 동작을
        // 바꾸지 않는다. 이행기가 블록 행을 걷으며 번호를 그대로 두는 것도 이 완화 덕이다.
        var rows = Baseline();
        rows[2][0] = "5";

        EpisodeWorkbookModel model = Read(rows);

        Assert.Empty(model.Errors);
        Assert.Contains(model.Diagnostics, item =>
            item.Severity == ChapterDiagnosticSeverity.Info &&
            item.Message.Contains("보다 작습니다", StringComparison.Ordinal));
    }

    [Fact]
    public void 인덱스가_없는데_대사가_적혀_있으면_경고한다()
    {
        // 조용히 넘기면 "여러 줄을 썼는데 안 나온다"가 된다(실사례).
        var rows = Baseline();
        rows = [.. rows, [null, null, "윌로", "번호를 안 붙인 줄"]];

        EpisodeWorkbookModel model = Read(rows);

        Assert.Contains(model.Diagnostics, item =>
            item.Code == ChapterDiagnosticCode.EpisodeIdBlank &&
            item.Severity == ChapterDiagnosticSeverity.Warning &&
            item.Message.Contains("A열에 번호를 적어", StringComparison.Ordinal));
    }

    [Fact]
    public void 인덱스만_있고_빈_행은_표의_행이_아니다()
    {
        // 템플릿이 미리 깔아 둔 번호 자리 — 대사로 세면 엉뚱한 오류가 난다(실사례).
        var rows = Baseline();
        rows = [.. rows, ["100", null, null, null]];

        EpisodeWorkbookModel model = Read(rows);

        Assert.Empty(model.Errors);
        Assert.Equal(4, model.Rows.Count);
        Assert.Null(model.FindByIndex(100));
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
            Fill(workbook.AddWorksheet("본문"), rows);
            workbook.SaveAs(path);
        }

        return EpisodeWorkbookReader.Read(path);
    }

    private static void Fill(IXLWorksheet sheet, string?[][] rows)
    {
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
    }

    /// <summary>v15 4열 대본. 각 테스트는 한 칸만 망가뜨린다.</summary>
    private static string?[][] Baseline() =>
    [
        ["인덱스", "LineId", "화자", "내용"],
        ["10", "ln_0001", "윌로", "첫 줄"],
        ["20", "ln_0002", "라루", "둘째 줄"],
        ["40", "ln_0003", "윌로", "셋째 줄"],
        ["90", "ln_0005", "윌로", "끝 줄"]
    ];
}
