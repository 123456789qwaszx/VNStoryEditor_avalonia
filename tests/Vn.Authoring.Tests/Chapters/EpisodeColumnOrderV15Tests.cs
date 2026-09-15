using ClosedXML.Excel;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>규격 v15</b> (2026-09-16 소유자 — R-C) — 대본 시트는 넷이고 <b>넷 다 대사 줄의 것</b>이다:
///
/// <code>
///     인덱스  LineId  화자  내용
///     └ 신원 ──────┘  └ 내용 ──┘
/// </code>
///
/// <c>유형</c>·<c>조건라벨</c>과 그 둘이 그리던 조건 블록이 폐지됐다. 이제 대본의 모든 행이
/// 대사다.
///
/// 이 파일이 지키는 것은 <b>둘</b>이다:
///   ① 자리 — 넷의 순서, 그리고 앞 규격(v14·v13·v10)의 파일이 이 자리로 옮겨 오는가.
///   ② <b>이행이 원고를 다치지 않는가</b> — 대사의 인덱스·LineId가 그대로인가. 이것이
///      R-C의 진짜 관문이다. 앞의 것은 틀려도 고치면 되지만 이행은 남의 원고를 건드린다.
/// </summary>
public sealed class EpisodeColumnOrderV15Tests : IDisposable
{
    private static readonly string[] V15 = ["인덱스", "LineId", "화자", "내용"];
    private static readonly string[] V14 = ["유형", "조건라벨", "인덱스", "LineId", "화자", "내용"];
    private static readonly string[] V13 = ["인덱스", "유형", "LineId", "조건라벨", "화자", "내용"];
    private static readonly string[] V10 = ["인덱스", "LineId", "유형", "조건라벨", "화자", "내용"];

    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "vn-v15", Guid.NewGuid().ToString("N"));

    public EpisodeColumnOrderV15Tests() => Directory.CreateDirectory(_folder);

    // ⛔ 정적 캐시를 지우지 않는다 — 나란히 도는 다른 클래스의 것까지 지운다. 열쇠가
    // 내용 해시라 지울 이유도 없다(파일을 쓰면 저절로 빗나간다).
    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    // ── ① 자리 ──────────────────────────────────────────────────────────────

    [Fact]
    public void 새_대본은_v15_자리로_선다()
    {
        EpisodeLibrary.EnsureWorkbook(_folder, "ep1");

        using var book = new XLWorkbook(EpisodeLibrary.FindExisting(_folder, "ep1")!);
        IXLWorksheet sheet = book.Worksheets.First();

        Assert.Equal(V15, Enumerable.Range(1, 4).Select(c => sheet.Cell(1, c).GetString()));

        // 다섯째 칸은 비어 있다 — 폐지된 두 칸이 뒤로 밀려 남지 않았다.
        Assert.Equal("", sheet.Cell(1, 5).GetString());

        // 인덱스 사다리는 A열에 깔린다 — 그 옆 칸(화자·내용)만 채우면 되는 손맛.
        Assert.Equal(10, sheet.Cell(2, 1).GetDouble());
        Assert.Equal(20, sheet.Cell(3, 1).GetDouble());
    }

    [Theory]
    [InlineData("v14")]
    [InlineData("v13")]
    [InlineData("v10")]
    public void 앞_규격의_파일은_넷만_남기고_옮겨_온다(string shape)
    {
        // ⚠ 짝마다 맞바꾸기를 따로 두지 않는다. 줄어드는 이행이라 <b>남길 넷을 머리글로
        //    찾아 옮기면</b> 어느 판에서 오든 같은 코드가 처리한다 — 그래서 이 테스트가
        //    세 모양을 같은 단언으로 지난다.
        string path = Path.Combine(_folder, $"{shape}.xlsx");

        (string[] headers, string?[][] rows) = shape switch
        {
            "v14" =>
            (V14, (string?[][])
            [
                [null, null, "10", "ln_0001", "윌로", "복도는 조용했다"],
                ["IF", "신뢰높음", null, null, null, null],
                [null, null, "40", "ln_0002", "라루", "조건 안"],
                ["ENDIF", null, null, null, null, null]
            ]),
            "v13" =>
            (V13, (string?[][])
            [
                ["10", null, "ln_0001", null, "윌로", "복도는 조용했다"],
                ["20", "IF", null, "신뢰높음", null, null],
                ["40", null, "ln_0002", null, "라루", "조건 안"],
                ["50", "ENDIF", null, null, null, null]
            ]),
            _ =>
            (V10, (string?[][])
            [
                ["10", "ln_0001", null, null, "윌로", "복도는 조용했다"],
                ["20", null, "IF", "신뢰높음", null, null],
                ["40", "ln_0002", null, null, "라루", "조건 안"],
                ["50", null, "ENDIF", null, null, null]
            ])
        };

        Write(path, headers, rows);

        Assert.True(EpisodeWorkbookMigrator.Migrate(path).Migrated);

        using var book = new XLWorkbook(path);
        IXLWorksheet sheet = book.Worksheets.First();

        Assert.Equal(V15, Enumerable.Range(1, 4).Select(c => sheet.Cell(1, c).GetString()));
        Assert.Equal("", sheet.Cell(1, 5).GetString());

        // 값이 제 낱말을 따라왔고, 블록 행은 통째로 빠졌다 — 대사 둘이 잇달아 선다.
        Assert.Equal("10", sheet.Cell(2, 1).GetString());
        Assert.Equal("ln_0001", sheet.Cell(2, 2).GetString());
        Assert.Equal("윌로", sheet.Cell(2, 3).GetString());
        Assert.Equal("복도는 조용했다", sheet.Cell(2, 4).GetString());

        Assert.Equal("40", sheet.Cell(3, 1).GetString());
        Assert.Equal("ln_0002", sheet.Cell(3, 2).GetString());
        Assert.Equal("조건 안", sheet.Cell(3, 4).GetString());
    }

    [Fact]
    public void 이미_v15인_파일은_손대지_않는다()
    {
        // 옮길 것이 없는데 옮기면 그만큼 틀릴 자리가 는다 — 그리고 파일이 바뀌면
        // 감시가 깨어나 아무 일도 없었는데 전부 다시 읽는다.
        string path = Path.Combine(_folder, "already.xlsx");
        Write(path, V15, [["10", "ln_0001", "윌로", "첫 줄"]]);

        byte[] before = File.ReadAllBytes(path);

        Assert.False(EpisodeWorkbookMigrator.Migrate(path).Migrated);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    // ── ② 이행이 원고를 다치지 않는다 ───────────────────────────────────────

    [Fact]
    public void 이행해도_대사의_인덱스와_LineId가_그대로다()
    {
        // ⛔ R-C의 진짜 관문. 인덱스는 줄의 신원이고 프로젝트의 ExcelLineMap이 그 번호로
        //    LineId를 붙들고 있다 — 다시 매기면 그 줄에 달아 둔 연출이 통째로 끊긴다.
        //    블록 행을 걷으면 번호가 뜨문뜨문해지는데(10·40·90), 그 뜨문함이 정상이다.
        string path = Path.Combine(_folder, "roundtrip.xlsx");

        Write(path, V14,
        [
            [null, null, "10", "ln_0001", "윌로", "복도는 조용했다"],
            ["IF", "신뢰높음", null, null, null, null],
            [null, null, "40", "ln_0003", "라루", "조건 안"],
            ["ENDIF", null, null, null, null, null],
            [null, null, "90", "ln_0005", "윌로", "끝 줄"]
        ]);

        Assert.True(EpisodeWorkbookMigrator.Migrate(path).Migrated);

        // 이행 결과를 <b>리더로</b> 되읽는다 — 파일이 규격에 맞는지까지 함께 증명된다.
        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(path);

        Assert.Empty(model.Errors);
        Assert.Equal([10, 40, 90], model.Rows.Select(row => row.Index));
        Assert.Equal(["ln_0001", "ln_0003", "ln_0005"], model.Rows.Select(row => row.LineId));
        Assert.Equal(["복도는 조용했다", "조건 안", "끝 줄"], model.Rows.Select(row => row.Text));
    }

    [Fact]
    public void 걷어낸_블록_행_수를_보고한다()
    {
        // 사람의 원고에서 행이 사라진 일이다 — 수를 밝히지 않으면 조용한 손실이 된다.
        string path = Path.Combine(_folder, "dropped.xlsx");

        Write(path, V14,
        [
            [null, null, "10", "ln_0001", "윌로", "첫 줄"],
            ["IF", "신뢰높음", null, null, null, null],
            [null, null, "40", "ln_0002", "라루", "조건 안"],
            ["ELSEIF", "분노누적", null, null, null, null],
            [null, null, "60", "ln_0003", "라루", "다른 쪽"],
            ["ENDIF", null, null, null, null, null]
        ]);

        EpisodeWorkbookMigrator.MigrationResult result = EpisodeWorkbookMigrator.Migrate(path);

        Assert.True(result.Migrated);
        Assert.NotNull(result.Failure);
        Assert.Contains("3행", result.Failure);
        Assert.Contains("간선", result.Failure);
    }

    [Fact]
    public void CHOICE_OPTION_행의_문구는_남는다()
    {
        // 선택지의 주인은 v9부터 챕터 시트지만, 툴이 임의로 없애면 사람이 쓴 문구가
        // 사라진다 — 대사 행으로 옮겨 두면 글이 보존된다.
        string path = Path.Combine(_folder, "choice.xlsx");

        Write(path, V14,
        [
            [null, null, "10", "ln_0001", "윌로", "어느 쪽으로 갈까"],
            ["CHOICE", null, "20", null, null, "갈림길"],
            ["OPTION", null, "30", null, null, "왼쪽으로"]
        ]);

        Assert.True(EpisodeWorkbookMigrator.Migrate(path).Migrated);

        using var book = new XLWorkbook(path);
        IXLWorksheet sheet = book.Worksheets.First();

        Assert.Equal("갈림길", sheet.Cell(3, 4).GetString());
        Assert.Equal("왼쪽으로", sheet.Cell(4, 4).GetString());
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static void Write(string path, string[] headers, string?[][] rows)
    {
        using var book = new XLWorkbook();
        IXLWorksheet sheet = book.AddWorksheet("대본");

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

        book.SaveAs(path);
    }
}
