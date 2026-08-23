using ClosedXML.Excel;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>규격 v14</b> (2026-08-24 소유자) — 왼쪽 두 칸이 <b>제어 행의 메타데이터</b>,
/// 오른쪽 네 칸이 <b>대사 줄</b>이다:
///
/// <code>
///     유형  조건라벨 │ 인덱스  LineId  화자  내용
///     └─ 구조 ─────┘ └─ 대사 ──────────────────┘
/// </code>
///
/// 소유자의 말: <i>"인덱스는 대사 라인 번호이고, 유형/조건은 제어 행의 메타데이터다.
/// … IF, ENDIF, SET 같은 행은 스토리 구조를 표현하는 행이지 대사가 아니니까."</i>
///
/// 그래서 이 파일이 지키는 것은 <b>둘</b>이다:
///   ① 자리 — 여섯 칸의 순서, 그리고 앞 규격의 파일이 이 자리로 옮겨 오는가.
///   ② 뜻 — <b>대사 행만 번호를 갖는가</b>. 이것이 v14의 본체다.
/// </summary>
public sealed class EpisodeColumnOrderV14Tests : IDisposable
{
    private static readonly string[] V14 = ["유형", "조건라벨", "인덱스", "LineId", "화자", "내용"];
    private static readonly string[] V13 = ["인덱스", "유형", "LineId", "조건라벨", "화자", "내용"];
    private static readonly string[] V10 = ["인덱스", "LineId", "유형", "조건라벨", "화자", "내용"];

    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "vn-v14", Guid.NewGuid().ToString("N"));

    public EpisodeColumnOrderV14Tests() => Directory.CreateDirectory(_folder);

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
    public void 새_대본은_v14_자리로_선다()
    {
        EpisodeLibrary.EnsureWorkbook(_folder, "ep1");

        using var book = new XLWorkbook(EpisodeLibrary.FindExisting(_folder, "ep1")!);
        IXLWorksheet sheet = book.Worksheets.First();

        Assert.Equal(V14, Enumerable.Range(1, 6).Select(c => sheet.Cell(1, c).GetString()));

        // 인덱스 사다리는 C열에 깔린다 — 그 옆 칸(화자·내용)만 채우면 되는 손맛.
        Assert.Equal(10, sheet.Cell(2, 3).GetDouble());
        Assert.Equal(20, sheet.Cell(3, 3).GetDouble());
    }

    [Theory]
    [InlineData("v13")]
    [InlineData("v10")]
    public void 앞_규격의_파일은_열만_제자리로_옮겨_온다(string shape)
    {
        // ⚠ 짝마다 맞바꾸기를 따로 두지 않는다. 셋 다 <b>같은 여섯 낱말의 순열</b>이라,
        //    이행기는 머리글을 읽어 어느 칸이 무엇인지 알아낸 뒤 v14 자리로 옮긴다.
        //    그래서 이 테스트가 두 모양을 <b>같은 코드로</b> 지난다.
        string path = Path.Combine(_folder, $"{shape}.xlsx");
        string[] headers = shape == "v13" ? V13 : V10;

        Write(path, headers, shape == "v13"
            ?
            [
                ["10", null, "ln_0001", null, "윌로", "복도는 조용했다"],
                ["20", "IF", null, "신뢰높음", null, null],
                ["30", null, "ln_0002", null, "라루", "조건 안"],
                ["40", "ENDIF", null, null, null, null]
            ]
            :
            [
                ["10", "ln_0001", null, null, "윌로", "복도는 조용했다"],
                ["20", null, "IF", "신뢰높음", null, null],
                ["30", "ln_0002", null, null, "라루", "조건 안"],
                ["40", null, "ENDIF", null, null, null]
            ]);

        Assert.True(EpisodeWorkbookMigrator.Migrate(path).Migrated);

        using var book = new XLWorkbook(path);
        IXLWorksheet sheet = book.Worksheets.First();

        Assert.Equal(V14, Enumerable.Range(1, 6).Select(c => sheet.Cell(1, c).GetString()));

        // 값이 제 낱말을 따라갔다 — 첫 대사 줄.
        Assert.Equal("", sheet.Cell(2, 1).GetString());            // 유형(빈칸 = 대사)
        Assert.Equal("10", sheet.Cell(2, 3).GetString());          // 인덱스
        Assert.Equal("ln_0001", sheet.Cell(2, 4).GetString());     // LineId
        Assert.Equal("복도는 조용했다", sheet.Cell(2, 6).GetString());

        // IF 줄 — 라벨은 따라오고 <b>번호는 지워졌다</b>.
        Assert.Equal("IF", sheet.Cell(3, 1).GetString());
        Assert.Equal("신뢰높음", sheet.Cell(3, 2).GetString());
        Assert.Equal("", sheet.Cell(3, 3).GetString());
        Assert.Equal("", sheet.Cell(5, 3).GetString());            // ENDIF도
    }

    [Fact]
    public void 이미_v14인_파일은_손대지_않는다()
    {
        // 옮길 것이 없는데 옮기면 그만큼 틀릴 자리가 는다 — 그리고 파일이 바뀌면
        // 감시가 깨어나 아무 일도 없었는데 전부 다시 읽는다.
        string path = Path.Combine(_folder, "already.xlsx");
        Write(path, V14, [[null, null, "10", "ln_0001", "윌로", "첫 줄"]]);

        byte[] before = File.ReadAllBytes(path);

        Assert.False(EpisodeWorkbookMigrator.Migrate(path).Migrated);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    // ── ② 뜻 — 번호는 대사 줄의 것이다 ──────────────────────────────────────

    [Fact]
    public void 블록_행은_번호를_안_갖는다()
    {
        // ⛔ v14의 본체. 인덱스가 "플레이어에게 전달되는 대사의 순번"이 되려면
        //    구조를 그리는 행은 번호를 가지면 안 된다.
        EpisodeWorkbookModel model = Read(
        [
            [null, null, "10", null, "윌로", "첫 줄"],
            ["IF", "신뢰높음", null, null, null, null],
            [null, null, "20", null, "라루", "조건 안"],
            ["ENDIF", null, null, null, null, null],
            [null, null, "30", null, "윌로", "끝 줄"]
        ]);

        Assert.Empty(model.Errors);
        Assert.Equal([10, null, 20, null, 30], model.Rows.Select(row => row.Index));
    }

    [Fact]
    public void 블록_행에_번호가_남아_있어도_오류가_아니고_읽히지도_않는다()
    {
        // 템플릿이 2~500행에 번호를 미리 깔아 두므로, 사람이 그 자리에 IF를 치면 번호가
        // 남는다 — <b>제 잘못이 아니라서</b> 빨간 줄로 세우지 않는다(소유자 결정).
        EpisodeWorkbookModel model = Read(
        [
            [null, null, "10", null, "윌로", "첫 줄"],
            ["IF", "신뢰높음", "20", null, null, null],
            ["ENDIF", null, "30", null, null, null]
        ]);

        Assert.Empty(model.Errors);
        Assert.DoesNotContain(model.Diagnostics, item =>
            item.Severity == ChapterDiagnosticSeverity.Warning);

        // 그래도 <b>모델에는 안 실린다</b> — 뜻이 없는 번호다.
        Assert.Equal([10, null, null], model.Rows.Select(row => row.Index));

        // 치울 것이 있다는 것은 <b>코드</b>로 말한다 — 동기화가 이 열쇠로 찾는다.
        Assert.Equal(2, model.Diagnostics.Count(item =>
            item.Code == ChapterDiagnosticCode.BlockRowIndexStray));
    }

    [Fact]
    public void 툴이_블록_행의_번호를_지운다()
    {
        // 소유자: "툴이 지워 준다." 지우는 것은 <b>인덱스 한 칸뿐</b>이다.
        string path = Path.Combine(_folder, "stray.xlsx");

        Write(path, V14,
        [
            [null, null, "10", null, "윌로", "첫 줄"],
            ["IF", "신뢰높음", "20", null, null, null],
            ["ENDIF", null, "30", null, null, null]
        ]);

        (ChapterWriteResult result, int cleared) =
            EpisodeWorkbookWriter.ClearBlockRowIndexes(path);

        Assert.True(result.Written);
        Assert.Equal(2, cleared);

        using var book = new XLWorkbook(path);
        IXLWorksheet sheet = book.Worksheets.First();

        Assert.Equal("", sheet.Cell(3, 3).GetString());
        Assert.Equal("", sheet.Cell(4, 3).GetString());

        // 대사 줄의 번호는 그대로다 — 그 번호에 연출이 매달려 있다.
        Assert.Equal("10", sheet.Cell(2, 3).GetString());
        Assert.Equal("신뢰높음", sheet.Cell(3, 2).GetString());   // 라벨도 그대로
    }

    [Fact]
    public void 지울_것이_없으면_파일에_손대지_않는다()
    {
        // ⚠ 안 바뀐 파일을 다시 쓰면 감시가 깨어나고 내용 해시가 바뀐다 — 아무 일도
        //    없었는데 그 챕터의 대본을 전부 다시 판다.
        string path = Path.Combine(_folder, "clean.xlsx");

        Write(path, V14,
        [
            [null, null, "10", null, "윌로", "첫 줄"],
            ["IF", "신뢰높음", null, null, null, null]
        ]);

        byte[] before = File.ReadAllBytes(path);

        (ChapterWriteResult result, int cleared) =
            EpisodeWorkbookWriter.ClearBlockRowIndexes(path);

        Assert.True(result.Written);
        Assert.Equal(0, cleared);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void 엑셀이_인덱스_칸에도_빗장을_건다()
    {
        // 손에서 먼저 막는다 — 리더가 규칙의 주인이지만, 앞잡이가 있으면 애초에 안 적는다.
        EpisodeLibrary.EnsureWorkbook(_folder, "ep_guard");

        using var book = new XLWorkbook(EpisodeLibrary.FindExisting(_folder, "ep_guard")!);
        IXLWorksheet sheet = book.Worksheets.First();

        IXLDataValidation guard = sheet.DataValidations.Single(validation =>
            validation.AllowedValues == XLAllowedValues.Custom &&
            validation.Ranges.Any(range => range.RangeAddress.FirstAddress.ColumnNumber == 3));

        Assert.Contains("$A2", guard.Value);   // 유형 열을 가리킨다
        Assert.Contains("대사", guard.Value);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private EpisodeWorkbookModel Read(string?[][] rows)
    {
        string path = Path.Combine(_folder, $"read_{Guid.NewGuid():N}.xlsx");
        Write(path, V14, rows);

        return EpisodeWorkbookReader.Read(path, ["신뢰높음"]);
    }

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
