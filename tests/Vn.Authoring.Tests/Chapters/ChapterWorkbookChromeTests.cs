using ClosedXML.Excel;
using Vn.Authoring.Chapters;
using Path = System.IO.Path;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 새로 만든 챕터도 견본처럼 보인다 (2026-08-18 소유자 보고).
///
/// 소유자가 견본(ch05)과 자기가 만든 챕터를 나란히 열고 말했다 — 견본은 "보기 좋은데"
/// 새 것은 "밋밋한" 것이었다. 겉모습(머리글 고정·자동 필터·열 너비)이 견본에만 손으로
/// 들어가 있었고 코드에는 없었다. <b>기획자가 매일 여는 것은 견본이 아니라 자기 챕터다.</b>
/// </summary>
public sealed class ChapterWorkbookChromeTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-chapter-chrome", Guid.NewGuid().ToString("N"));

    private string ChapterPath => Path.Combine(_directory, "ch01.xlsx");

    public ChapterWorkbookChromeTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 새로_만든_챕터의_모든_시트가_겉모습을_갖춘다()
    {
        ChapterWorkbookWriter.EnsureChapterWorkbook(_directory, "ch01", [("trust", "신뢰")]);

        using var workbook = new XLWorkbook(ChapterPath);

        foreach (string name in
                 new[]
                 {
                     ChapterSheetNames.Episodes, ChapterSheetNames.Edges,
                     ChapterSheetNames.Conditions, ChapterSheetNames.Stats,
                     ChapterSheetNames.Choices
                 })
        {
            IXLWorksheet sheet = workbook.Worksheet(name);

            Assert.True(sheet.AutoFilter.IsEnabled, $"`{name}` 시트에 자동 필터가 없다");
            Assert.True(sheet.SheetView.SplitRow == 1, $"`{name}` 시트의 머리글이 고정되지 않았다");

            // 넓다가 아니라 <b>정했다</b>를 본다 — `선택지`의 인덱스 열처럼 일부러 좁은
            // 칸도 있어서, 크기로 재면 의도한 좁음과 손 안 댄 기본값이 같아 보인다.
            Assert.True(
                Math.Abs(sheet.Column(1).Width - workbook.ColumnWidth) > 0.01,
                $"`{name}` 시트의 A열이 기본 너비({workbook.ColumnWidth}) 그대로다");
        }
    }

    [Fact]
    public void 규격_바깥의_칸에는_칠도_격자도_없다()
    {
        // ⛔ 2026-08-25 소유자 보고 — "에피소드 시트에서 g열이 색이 칠해져 있는거랑,
        //    간선에서 i,j,k열이 색이 칠해져있는 것 정리해줘."
        //
        //    폐지된 열(에피소드 `종류`, 간선 `잠금시 숨김`·`종류`·`연출`)의 자리에 격자와
        //    배경이 남아 있었다. 사람 눈에는 그것이 곧 "쓰라는 칸"이라, 없는 칸에 적고
        //    저장했는데 아무 일도 안 일어나는 상태가 된다.
        ChapterWorkbookWriter.EnsureChapterWorkbook(_directory, "ch01", [("trust", "신뢰")]);

        using var workbook = new XLWorkbook(ChapterPath);

        // 에피소드는 일곱 칸(A~G — v14의 `이벤트키`까지), 간선은 일곱 칸(A~G)이다.
        AssertBlank(workbook.Worksheet(ChapterSheetNames.Episodes), 8);
        AssertBlank(workbook.Worksheet(ChapterSheetNames.Edges), 8);
        AssertBlank(workbook.Worksheet(ChapterSheetNames.Edges), 9);
        AssertBlank(workbook.Worksheet(ChapterSheetNames.Edges), 10);
    }

    /// <summary>그 칸이 <b>칸처럼 보이지 않는가</b> — 배경도 격자도 없어야 한다.</summary>
    private static void AssertBlank(IXLWorksheet sheet, int column)
    {
        IXLStyle style = sheet.Cell(2, column).Style;

        // ⚠ 색을 견주지 않는다 — ClosedXML은 "칠 없음"을 인덱스 64로 들고 있어
        //    `XLColor.NoColor`와 글자로 비교하면 같은 것이 달라 보인다. 무늬가 곧 칠이다.
        Assert.Equal(XLFillPatternValues.None, style.Fill.PatternType);
        Assert.Equal(XLBorderStyleValues.None, style.Border.LeftBorder);
        Assert.Equal(XLBorderStyleValues.None, style.Border.TopBorder);
    }

    [Fact]
    public void 남은_열들은_읽을_수_있는_너비를_갖는다()
    {
        // 기본 9로 두면 `잠금 안내문`이 잘려 무슨 칸인지 안 보인다.
        ChapterWorkbookWriter.EnsureChapterWorkbook(_directory, "ch01", [("trust", "신뢰")]);

        using var workbook = new XLWorkbook(ChapterPath);
        IXLWorksheet edges = workbook.Worksheet(ChapterSheetNames.Edges);

        Assert.True(edges.Column(7).Width >= 20);  // 잠금 안내문
    }

    [Fact]
    public void 겉모습_없는_구판을_열면_이행이_입혀_준다()
    {
        // 이미 만들어 둔 챕터들이 밋밋한 채로 남으면 "모두 반영"이 아니다.
        ChapterWorkbookWriter.EnsureChapterWorkbook(_directory, "ch01", [("trust", "신뢰")]);
        StripChrome(ChapterPath);

        Assert.True(ChapterWorkbookMigrator.Migrate(ChapterPath).Migrated);

        using var workbook = new XLWorkbook(ChapterPath);
        Assert.True(workbook.Worksheet(ChapterSheetNames.Episodes).AutoFilter.IsEnabled);
        Assert.True(workbook.Worksheet(ChapterSheetNames.Edges).Column(7).Width >= 20); // 잠금 안내문
    }

    [Fact]
    public void 열은_이미_걷혔는데_칠만_남은_파일도_이행이_씻어_준다()
    {
        // ⛔ 2026-08-25 — 소유자 손에 실제로 있던 모양이다. v13 이행(에피소드 `종류` 폐지)은
        //    이미 지나갔으므로 열 이름으로 부르는 조건은 전부 조용하다. 그런데 그 자리의
        //    배경과 격자는 그대로 남아 있다 — 걷을 때 칠은 안 걷었기 때문이다.
        //
        //    이 시험이 없으면 "새로 만든 챕터만 깨끗하고 쓰던 챕터는 영영 그대로"가 된다.
        ChapterWorkbookWriter.EnsureChapterWorkbook(_directory, "ch01", [("trust", "신뢰")]);

        using (var stale = new XLWorkbook(ChapterPath))
        {
            IXLWorksheet episodes = stale.Worksheet(ChapterSheetNames.Episodes);
            // v14부터 규격 바깥은 여덟째부터다(`이벤트키`가 일곱째를 차지했다).
            episodes.Range(1, 8, 40, 8).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#333F50"));
            stale.Save();
        }

        Assert.True(ChapterWorkbookMigrator.Migrate(ChapterPath).Migrated);

        using var after = new XLWorkbook(ChapterPath);
        AssertBlank(after.Worksheet(ChapterSheetNames.Episodes), 8);

        // 그리고 그 이행이 스스로를 다시 부르지 않는다 — 아니면 열 때마다 `.bak`이 갈린다.
        Assert.False(ChapterWorkbookMigrator.Migrate(ChapterPath).Migrated);
    }

    [Fact]
    public void 겉모습을_갖춘_파일은_다시_이행하지_않는다()
    {
        // 이행 조건과 이행 내용이 어긋나면 <b>열 때마다</b> 파일을 다시 쓰고 `.bak`이
        // 매번 갈린다 — 사람이 되돌릴 자리를 조용히 잃는다.
        ChapterWorkbookWriter.EnsureChapterWorkbook(_directory, "ch01", [("trust", "신뢰")]);

        Assert.False(ChapterWorkbookMigrator.Migrate(ChapterPath).Migrated);
    }

    [Fact]
    public void 이행이_사람이_걸어_둔_필터_조건을_지우지_않는다()
    {
        ChapterWorkbookWriter.EnsureChapterWorkbook(_directory, "ch01", [("trust", "신뢰")]);
        StripChrome(ChapterPath);

        // 사람이 `간선` 시트에만 자기 필터를 걸어 둔 상태 — 다른 시트는 아직 밋밋하다.
        using (var workbook = new XLWorkbook(ChapterPath))
        {
            workbook.Worksheet(ChapterSheetNames.Edges).Range(1, 1, 1, 11).SetAutoFilter();
            workbook.Save();
        }

        ChapterWorkbookMigrator.Migrate(ChapterPath);

        using var after = new XLWorkbook(ChapterPath);
        Assert.True(after.Worksheet(ChapterSheetNames.Edges).AutoFilter.IsEnabled);
        Assert.True(after.Worksheet(ChapterSheetNames.Episodes).AutoFilter.IsEnabled);
    }

    [Fact]
    public void 새_챕터가_견본과_같은_서식_언어를_쓴다()
    {
        // 소유자가 "밋밋하다"고 한 것의 정체 — 고정·필터·너비가 아니라 <b>글꼴과 색</b>이었다.
        // 첫 시도에서 기하만 맞추고 서식을 빠뜨려 "전혀 반영이 안 된다"는 보고를 다시 받았다.
        ChapterWorkbookWriter.EnsureChapterWorkbook(_directory, "ch01", [("trust", "신뢰")]);

        using var workbook = new XLWorkbook(ChapterPath);
        IXLWorksheet episodes = workbook.Worksheet(ChapterSheetNames.Episodes);

        // 머리글 — 굵은 흰 글자 + 진한 배경.
        IXLStyle header = episodes.Cell(1, 1).Style;
        Assert.True(header.Font.Bold);
        Assert.Equal(XLColor.White, header.Font.FontColor);
        Assert.Equal(XLColor.FromHtml("#333F50"), header.Fill.BackgroundColor);

        // 본문 — 맑은 고딕 10 + 얇은 회색 격자.
        IXLStyle body = episodes.Cell(2, 2).Style;
        Assert.Equal("맑은 고딕", body.Font.FontName);
        Assert.Equal(10, body.Font.FontSize);
        Assert.Equal(XLBorderStyleValues.Thin, body.Border.BottomBorder);

        // 메모(G) — 기울인 옅은 회색 9pt. 데이터가 아니라 곁말이다.
        // v13에서 `종류`가 걷히며 G → F로 갔다가(2026-08-25), v14 열 순서 개정으로
        // 맨 뒤(G)로 돌아왔다 — 곁말은 표의 끝이 제자리다.
        IXLStyle note = episodes.Cell(2, 7).Style;
        Assert.True(note.Font.Italic);
        Assert.Equal(9, note.Font.FontSize);

        // EpisodeId(A) — 회색. "여긴 남을 가리키는 칸"
        Assert.Equal(XLColor.FromHtml("#808080"), episodes.Cell(2, 1).Style.Font.FontColor);
    }

    [Fact]
    public void 시트마다_머리글_색이_다르다()
    {
        // 시트 일곱 개가 다 비슷하게 생기면 탭을 잘못 눌렀다는 것을 알 수 없다.
        ChapterWorkbookWriter.EnsureChapterWorkbook(_directory, "ch01", [("trust", "신뢰")]);

        using var workbook = new XLWorkbook(ChapterPath);

        XLColor[] colours =
        [
            .. new[]
            {
                ChapterSheetNames.Episodes, ChapterSheetNames.Conditions,
                ChapterSheetNames.Stats, ChapterSheetNames.Choices
            }.Select(name => workbook.Worksheet(name).Cell(1, 1).Style.Fill.BackgroundColor)
        ];

        Assert.Equal(colours.Length, colours.Distinct().Count());
    }

    /// <summary>겉모습을 벗긴다 — 코드가 겉모습을 입히기 전에 만들어진 파일의 모습이다.</summary>
    private static void StripChrome(string path)
    {
        using var workbook = new XLWorkbook(path);

        foreach (IXLWorksheet sheet in workbook.Worksheets)
        {
            sheet.AutoFilter.Clear();
            sheet.SheetView.FreezeRows(0);

            for (int column = 1; column <= 12; column++)
            {
                sheet.Column(column).Width = 9;
            }
        }

        workbook.Save();
    }
}
