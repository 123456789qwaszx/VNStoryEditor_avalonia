using ClosedXML.Excel;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>`잠금시 숨김`이 폐지됐다</b> (2026-08-24 소유자: "이미 표시조건과, 해금조건이 있다보니
/// 기능적으로 제거하더라도 아무런 차이가 없어").
///
/// 같은 말을 두 번 하는 칸이었다 — <b>해금조건 + 숨김</b>은 그 식을 <b>표시조건</b>에 적은
/// 것과 결과가 같다. 개념이 둘이면 작가가 어느 쪽으로 적었는지에 따라 같은 이야기가
/// 다르게 보인다.
/// </summary>
public sealed class HideWhenLockedRemovalTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "vn-hide-removal", Guid.NewGuid().ToString("N"));

    public HideWhenLockedRemovalTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void 새_챕터에는_그_열이_없다()
    {
        ChapterWorkbookWriter.EnsureChapterWorkbook(_folder, "ch01", [("trust", "신뢰")]);

        using var book = new XLWorkbook(Path.Combine(_folder, "ch01.xlsx"));
        IXLWorksheet sheet = book.Worksheet(ChapterSheetNames.Edges);

        Assert.Equal(
            ["출발", "도착", "스탯변화", "선택지", "표시조건", "해금조건", "잠금 안내문", "엔딩키"],
            Enumerable.Range(1, 8).Select(column => sheet.Cell(1, column).GetString().Trim()));

        Assert.Equal(string.Empty, sheet.Cell(1, 9).GetString());
    }

    [Fact]
    public void 옛_워크북은_그_열이_걷히고_뒤가_당겨진다()
    {
        // ⚠ 뒤의 두 칸(잠금 안내문·엔딩키)이 한 칸씩 당겨져야 한다 — 안 당겨지면 리더가
        //    "규격의 자리대로" 읽어 <b>안내문 자리에서 FALSE를 읽는</b> 식으로 어긋난다.
        string path = Path.Combine(_folder, "old.xlsx");
        WriteOldShape(path);

        Assert.True(ChapterWorkbookMigrator.Migrate(path).Migrated);

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);

        Assert.Empty(model.Errors);

        ChapterEdge edge = Assert.Single(model.Edges);

        Assert.Equal("신뢰높음", edge.ConditionLabel);
        Assert.Equal("신뢰가 부족하다", edge.LockedMessage);   // FALSE가 여기 오면 안 된다
        Assert.Equal("ch01_true", edge.EndingKey);
    }

    [Fact]
    public void 이행은_한_번으로_끝난다()
    {
        // ⛔ 이행 조건과 이행 내용이 어긋나면 <b>열 때마다</b> 파일을 다시 쓰고 `.bak`이
        //    매번 갈린다 — 사람이 되돌릴 자리를 조용히 잃는다.
        string path = Path.Combine(_folder, "twice.xlsx");
        WriteOldShape(path);

        Assert.True(ChapterWorkbookMigrator.Migrate(path).Migrated);
        Assert.False(ChapterWorkbookMigrator.Migrate(path).Migrated);
    }

    [Fact]
    public void 내보내기는_언제나_false로_나간다()
    {
        // 저쪽 DTO는 안 바꾼다 — 칸을 없애면 옛 진행 JSON이 안 읽힌다. 안 채울 뿐이다.
        string path = Path.Combine(_folder, "export.xlsx");
        WriteOldShape(path);
        ChapterWorkbookMigrator.Migrate(path);

        ChapterExportResult export = ChapterProgressionExporter.Export(
            ChapterWorkbookReader.Read(path), episodesFolder: null);

        Assert.False(export.Refused, string.Join(
            " / ", export.Validation.All.Select(item => item.Message)));

        Assert.Contains("\"HideWhenLocked\": false", export.Json);
        Assert.DoesNotContain("\"HideWhenLocked\": true", export.Json);
    }

    /// <summary>`잠금시 숨김`(G)이 살아 있던 옛 모양 — 그 뒤로 잠금 안내문·엔딩키.</summary>
    private static void WriteOldShape(string path)
    {
        using var book = new XLWorkbook();

        Sheet(book, ChapterSheetNames.Episodes,
            ["EpisodeId", "제목", "종류", "대사엔트리", "X", "Y", "메모"],
            [
                ["시작", "첫 화", "Main", "장면_1", "0", "0", null],
                ["끝", "끝 화", "Main", "장면_2", "200", "0", null]
            ]);

        Sheet(book, ChapterSheetNames.Edges,
            ["출발", "도착", "스탯변화", "선택지", "표시조건", "해금조건",
             "잠금시 숨김", "잠금 안내문", "엔딩키"],
            [["시작", "끝", null, "계속", null, "신뢰높음", "TRUE", "신뢰가 부족하다", "ch01_true"]]);

        Sheet(book, ChapterSheetNames.Conditions,
            ["라벨", "스탯", "연산자", "값", "설명"],
            [["신뢰높음", "trust", ">=", "3", null]]);

        Sheet(book, ChapterSheetNames.Stats,
            ["스탯키", "표시명", "초기값", "최소", "최대", "타입"],
            [
                // 초기 3 — 간선의 해금조건이 `trust >= 3`이라, 0에서 시작하면 도달성 증명이
                // "어떤 경로로도 못 연다"며 내보내기를 막는다(그 판정은 옳다).
                ["trust", "신뢰", "3", "0", "10", null],
                // 규격이 스탯 2~5개를 전제한다(§0) — 하나면 검증이 내보내기를 막는다.
                ["fatigue", "피로", "0", "0", "10", null]
            ]);

        Sheet(book, ChapterSheetNames.Choices, ["인덱스", "대본", "메모"], [["10", "계속", null]]);

        book.SaveAs(path);
    }

    private static void Sheet(XLWorkbook book, string name, string[] headers, string?[][] rows)
    {
        IXLWorksheet sheet = book.AddWorksheet(name);

        for (int column = 0; column < headers.Length; column++)
        {
            sheet.Cell(1, column + 1).SetValue(headers[column]);
        }

        for (int row = 0; row < rows.Length; row++)
        {
            for (int column = 0; column < rows[row].Length; column++)
            {
                if (rows[row][column] is { Length: > 0 } value)
                {
                    sheet.Cell(row + 2, column + 1).SetValue(value);
                }
            }
        }
    }
}
