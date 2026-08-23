using ClosedXML.Excel;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 실제 스프레드시트 프로그램이 저장한 워크북의 <b>셀 타입</b>을 리더가 견디는지 고정한다.
///
/// 배경(D12): 이 기계에 Microsoft Excel도 LibreOffice도 없어 "엑셀이 저장한 파일"을 만들 수 없었다.
/// 그래서 그 요구가 실제로 막으려던 위험 — <b>모든 값이 문자열로 들어온다고 가정하는 리더</b> —
/// 를 셀 타입 쪽에서 직접 친다.
///
/// 견본(`chapter-graph-sample.xlsx`)은 openpyxl이 만든 파일이고 X·Y를 <c>t="n"</c> 숫자 셀로 담고
/// 있어 숫자 경로는 이미 견본 테스트가 덮는다. 여기서 더 덮는 것은 <b>공유 문자열·불리언·수식</b>이다.
/// 엑셀은 문자열을 거의 언제나 공유 문자열 표에 넣고, 체크박스형 열을 불리언으로 저장한다.
/// </summary>
public sealed class ChapterWorkbookCellTypeTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-chapter-celltype", Guid.NewGuid().ToString("N"));

    public ChapterWorkbookCellTypeTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 숫자와_불리언_셀이_문자열_셀과_같게_읽힌다()
    {
        string path = Path.Combine(_directory, "typed.xlsx");

        using (var workbook = new XLWorkbook())
        {
            IXLWorksheet episodes = workbook.AddWorksheet("에피소드");
            Header(episodes,
                "EpisodeId", "제목", "종류", "대사엔트리",
                "X", "Y", "엔딩키", "메모");

            episodes.Cell(2, 1).SetValue("ep1");
            episodes.Cell(2, 2).SetValue("첫 화");
            episodes.Cell(2, 3).SetValue("Main");
            episodes.Cell(2, 4).SetValue("Story_ep1");
            episodes.Cell(2, 5).SetValue(0);       // 숫자
            episodes.Cell(2, 6).SetValue(0);

            episodes.Cell(3, 1).SetValue("ep2");
            episodes.Cell(3, 2).SetValue("둘째 화");
            episodes.Cell(3, 3).SetValue("Main");
            episodes.Cell(3, 4).SetValue("Story_ep2");
            episodes.Cell(3, 5).SetValue(-120.0);  // 음수 실수값이지만 정수다
            episodes.Cell(3, 6).SetValue(170);

            IXLWorksheet edges = workbook.AddWorksheet("간선");
            Header(edges, "출발", "도착", "스탯변화", "선택지", "표시조건", "해금조건", "잠금시 숨김", "잠금 안내문");
            edges.Cell(2, 1).SetValue("ep1");
            edges.Cell(2, 2).SetValue("ep2");
            edges.Cell(2, 4).SetValue("계속");   // v12 — 문구 없는 길은 폐지됐다
            edges.Cell(2, 7).SetValue(true);       // 불리언 — 문자열 "TRUE"가 아니다 (v8: 잠금시 숨김 = G)

            IXLWorksheet conditions = workbook.AddWorksheet("조건");
            Header(conditions, "라벨", "스탯", "연산자", "값", "설명");
            conditions.Cell(2, 1).SetValue("신뢰높음");
            conditions.Cell(2, 2).SetValue("trust");
            conditions.Cell(2, 3).SetValue(">=");
            conditions.Cell(2, 4).SetValue(3);     // 숫자 셀 — 값 칸이 텍스트가 아니어도 읽힌다
            conditions.Cell(2, 5).SetValue("보통 조건");

            IXLWorksheet stats = workbook.AddWorksheet("스탯");
            Header(stats, "스탯키", "표시명", "초기값", "최소", "최대", "타입");
            stats.Cell(2, 1).SetValue("trust");
            stats.Cell(2, 2).SetValue("신뢰");
            stats.Cell(2, 3).SetValue(0);
            stats.Cell(2, 4).SetValue(0);
            stats.Cell(2, 5).SetValue(10);
            stats.Cell(3, 1).SetValue("anger");
            stats.Cell(3, 2).SetValue("분노");
            stats.Cell(3, 3).SetValue(0);
            stats.Cell(3, 4).SetValue(0);
            stats.Cell(3, 5).SetValue(10);

            IXLWorksheet fixtures = workbook.AddWorksheet("픽스처");
            Header(fixtures, "픽스처명", "활성", "trust", "anger", "고정 선택 (에피소드ID→도착ID)");
            fixtures.Cell(2, 1).SetValue("기본");
            fixtures.Cell(2, 2).SetValue(false);   // 불리언
            fixtures.Cell(2, 3).SetValue(0);
            fixtures.Cell(2, 4).SetValue(0);
            fixtures.Cell(2, 5).SetValue("ep1→ep2");

            workbook.SaveAs(path);
        }

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);

        Assert.Empty(model.Errors);

        // 숫자 셀이 좌표로 들어온다 — 문자열 가정이면 여기서 0이 된다.
        Assert.Equal(-120d, model.FindEpisode("ep2")!.X);
        Assert.Equal(170d, model.FindEpisode("ep2")!.Y);

        // 불리언 셀이 참/거짓으로 들어온다.
        Assert.True(model.Edges[0].HideWhenLocked);
        Assert.False(model.Fixtures[0].IsActive);

        // 조건식이 그대로 파싱된다.
        ChapterCondition condition = model.FindCondition("신뢰높음")!;
        Assert.Equal("trust >= 3", condition.Expression);
        Assert.True(condition.IsValid);
        Assert.Equal(3, Assert.Single(condition.Parsed).Value);
    }

    [Fact]
    public void 공유_문자열로_저장된_워크북도_읽힌다()
    {
        // 엑셀은 텍스트를 거의 언제나 공유 문자열 표(sharedStrings.xml)에 모은다.
        // 같은 문자열을 여러 셀에서 반복해 표가 실제로 쓰이도록 만든다.
        string path = Path.Combine(_directory, "shared.xlsx");
        XlsxTestWorkbook.Write(_directory, "shared.xlsx",
            ("에피소드", [
                ["EpisodeId", "제목", "종류", "대사엔트리", "X", "Y", "엔딩키", "메모"],
                ["ep1", "Main", "Main", "Story_ep1", "0", "0", null, null, null, "Main"],
                ["ep2", "Main", "Main", "Story_ep2", "200", "0", null, null, null, "Main"]
            ]),
            ("간선", [
                ["출발", "도착", "스탯변화", "선택지", "표시조건", "해금조건", "잠금시 숨김", "잠금 안내문"],
                ["ep1", "ep2", null, "계속", null, null, "FALSE", null]
            ]),
            ("조건", [
                ["라벨", "스탯", "연산자", "값", "설명"],
                ["신뢰높음", "trust", ">=", "3", "Main"]
            ]),
            ("스탯", [
                ["스탯키", "표시명", "초기값", "최소", "최대", "타입"],
                ["trust", "신뢰", "0", "0", "10", null],
                ["anger", "분노", "0", "0", "10", null]
            ]),
            ("픽스처", [
                ["픽스처명", "활성", "trust", "anger", "고정 선택 (에피소드ID→도착ID)"],
                ["기본", "TRUE", "0", "0", "ep1→ep2"]
            ]));

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);

        Assert.Empty(model.Errors);
        Assert.Equal(2, model.Episodes.Count);
        Assert.Equal("Main", model.FindEpisode("ep1")!.Title);
        Assert.Equal(200d, model.FindEpisode("ep2")!.X);
    }

    [Fact]
    public void 계산된_값이_없는_수식_셀은_비었다고_하지_않고_이유를_말한다()
    {
        string path = Path.Combine(_directory, "uncached.xlsx");

        using (var workbook = new XLWorkbook())
        {
            IXLWorksheet episodes = workbook.AddWorksheet("에피소드");
            Header(episodes,
                "EpisodeId", "제목", "종류", "대사엔트리",
                "X", "Y", "엔딩키", "메모");
            episodes.Cell(2, 1).SetValue("ep1");
            episodes.Cell(2, 4).SetValue("Story_ep1");
            // 결과를 캐시하지 않은 수식 — 파일 안에서는 빈 칸이다.
            episodes.Cell(2, 2).FormulaA1 = "=\"제\" & \"목\"";

            Header(workbook.AddWorksheet("간선"),
                "출발", "도착", "스탯변화", "선택지", "표시조건", "해금조건", "잠금시 숨김", "잠금 안내문");
            Header(workbook.AddWorksheet("조건"), "라벨", "스탯", "연산자", "값", "설명");

            IXLWorksheet stats = workbook.AddWorksheet("스탯");
            Header(stats, "스탯키", "표시명", "초기값", "최소", "최대", "타입");
            stats.Cell(2, 1).SetValue("trust");
            stats.Cell(2, 5).SetValue(10);
            stats.Cell(3, 1).SetValue("anger");
            stats.Cell(3, 5).SetValue(10);

            Header(workbook.AddWorksheet("픽스처"),
                "픽스처명", "활성", "trust", "anger", "고정 선택 (에피소드ID→도착ID)");

            workbook.SaveAs(path);
        }

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);

        ChapterDiagnostic problem = Assert.Single(
            model.Errors, item => item.Code == ChapterDiagnosticCode.FormulaWithoutCachedValue);

        Assert.Equal("에피소드", problem.Sheet);
        Assert.Equal(2, problem.Row);
        Assert.Equal("B", problem.Column);
        Assert.Contains("엑셀에서 한 번 열어 저장", problem.Message);
    }

    private static void Header(IXLWorksheet sheet, params string[] names)
    {
        for (int index = 0; index < names.Length; index++)
        {
            sheet.Cell(1, index + 1).SetValue(names[index]);
        }
    }
}
