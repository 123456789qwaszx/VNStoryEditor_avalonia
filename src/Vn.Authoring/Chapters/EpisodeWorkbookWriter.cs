using ClosedXML.Excel;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 에피소드 워크북에 쓰는 <b>유일한 자리</b> (2026-08-24 소유자: "연출 그래프에서도 대사를
/// 편집하는 게 편하다").
///
/// <b>왜 이것이 있어야 했나</b> — 예전에는 없었고, 그게 옳았다. 연출 그래프의 엑셀노드는
/// 본문이 잠겨 있었고 README도 <i>"대사 본문·화자 → 도구는 여기에 쓰지 않습니다"</i>라고
/// 적었다. 그런데 잠금을 <b>그냥 풀 수는 없다</b>: <see cref="EpisodeSyncService.Sync"/>는
/// 동기화마다 워크북 내용으로 노드 본문을 통째로 덮어쓰고, 그 동기화는 챕터를 고를 때마다·
/// 워크북이 저장될 때마다 돈다. 잠금만 풀면 사람이 고친 글이 다음 동기화에 <b>증발한다</b> —
/// 이 코드베이스가 "가장 나쁜 화면"이라고 부르는 바로 그것이다.
///
/// 그래서 편집을 여는 값은 <b>편집을 엑셀 셀로 곧장 내보내는 것</b>이다. 챕터 그래프가
/// 2026-08-12에 통과한 그 길과 같다(G-2 v2): 원본은 여전히 엑셀이고, 툴의 편집은 해당
/// 셀만 고치는 외과수술이다. 그러면 다음 동기화가 읽는 값이 이미 사람이 쓴 값이라 되돌릴
/// 것이 없다.
///
/// <b>여는 것은 두 칸뿐이다</b> (소유자 결정) — 화자(E열)와 내용(F열). 줄을 더하고 지우고
/// 옮기는 것, 조건 블록(IF~ENDIF)은 <b>엑셀 소유 그대로다</b>. 그것들은 인덱스 재배치와
/// 빈 템플릿 행 처리가 얽혀 있어, 열면 표의 구조를 두 곳이 갖게 된다.
/// </summary>
public static class EpisodeWorkbookWriter
{
    private const int HeaderRow = 1;

    // 리더와 같은 6열 (v10). ⚠ 이 배열은 <see cref="EpisodeWorkbookReader"/>의 것과 같아야
    // 한다 — 시트를 찾는 근거가 머리글이라, 한쪽만 바뀌면 여기서 시트를 못 찾는다.
    private static readonly string[] Headers =
        ["인덱스", "유형", "LineId", "조건라벨", "화자", "내용"];

    private const int ColumnIndex = 1;
    private const int ColumnKind = 2;
    private const int ColumnSpeaker = 5;
    private const int ColumnText = 6;

    /// <summary>
    /// 대사 한 줄의 화자·내용을 고친다. 행은 <b>인덱스(A열)로</b> 찾는다 — 줄의 신원이
    /// 인덱스이기 때문이다(G-5). 엑셀 행 번호로 찾으면 사람이 시트에서 행을 하나 끼우는
    /// 순간 엉뚱한 줄을 덮는다.
    /// </summary>
    /// <param name="index">A열 값. 노드의 <c>ExcelLineMap</c>이 LineId를 이 값에 묶어 둔다.</param>
    public static ChapterWriteResult SetLine(
        string path, int index, string? speaker, string? text)
    {
        return Mutate(path, workbook =>
        {
            IXLWorksheet sheet = FindEpisodeSheet(workbook)
                ?? throw new InvalidOperationException(
                    "이 워크북에서 대본 시트를 찾지 못했습니다 — 머리글이 규격과 다릅니다.");

            int row = FindRowByIndex(sheet, index)
                ?? throw new InvalidOperationException(
                    $"인덱스 {index}인 행이 없습니다 — 엑셀에서 그 줄이 지워졌을 수 있습니다.");

            // ⛔ 대사 행만 고친다. IF·ELSEIF·ENDIF 행에 화자·내용을 쓰면 리더가 그 블록을
            // 다르게 읽는다 — 열어 준 적 없는 문이므로 여기서 막는다(문이 둘이면 빗장도 둘).
            string kind = Cell(sheet, row, ColumnKind);

            if (kind.Length > 0 &&
                !string.Equals(kind, "대사", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"인덱스 {index}는 '{kind}' 행이라 여기서 고칠 수 없습니다 — " +
                    "조건 블록은 엑셀에서 고칩니다.");
            }

            Set(sheet, row, ColumnSpeaker, speaker);
            Set(sheet, row, ColumnText, text);
        });
    }

    private static IXLWorksheet? FindEpisodeSheet(XLWorkbook workbook) =>
        workbook.Worksheets.FirstOrDefault(sheet =>
            Headers.Select((header, offset) =>
                string.Equals(Cell(sheet, HeaderRow, offset + 1), header, StringComparison.Ordinal))
                .All(matches => matches));

    private static int? FindRowByIndex(IXLWorksheet sheet, int index) =>
        sheet.RowsUsed()
            .Where(row => row.RowNumber() > HeaderRow)
            .FirstOrDefault(row =>
                int.TryParse(
                    Cell(sheet, row.RowNumber(), ColumnIndex),
                    out int value) && value == index)
            ?.RowNumber();

    private static string Cell(IXLWorksheet sheet, int row, int column) =>
        sheet.Cell(row, column).GetString().Trim();

    private static void Set(IXLWorksheet sheet, int row, int column, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            sheet.Cell(row, column).Clear(XLClearOptions.Contents);
            return;
        }

        sheet.Cell(row, column).SetValue(value);
    }

    /// <summary>
    /// 메모리 사본에서 고치고 원본에 저장한다 — <see cref="ChapterWorkbookWriter"/>의
    /// 같은 이름 메서드와 같은 규칙이다. 엑셀이 잠갔으면(또는 무엇이든 실패하면) 파일은
    /// 그대로 두고 사유만 돌려준다: 반쯤 쓴 워크북은 없다.
    ///
    /// ⚠ 백업(.bak)은 두지 않는다. 저쪽이 백업을 켜는 것은 <b>지우는 종류의 쓰기</b>인데
    /// (행·간선 삭제) 여기서 여는 것은 두 칸의 덮어쓰기뿐이고, 그 원본은 사람이 방금 보고
    /// 있던 글이다. 대사 한 자 고칠 때마다 .bak을 굴리면 그게 더 시끄럽다.
    /// </summary>
    private static ChapterWriteResult Mutate(string path, Action<XLWorkbook> edit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            using var memory = new MemoryStream();

            using (var stream = new FileStream(
                       path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                stream.CopyTo(memory);
            }

            memory.Position = 0;

            using var workbook = new XLWorkbook(memory);
            edit(workbook);
            workbook.SaveAs(path);

            return ChapterWriteResult.Ok;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ChapterWriteResult.Locked(
                $"엑셀이 '{Path.GetFileName(path)}'를 열고 있어 툴이 쓰지 못했습니다 — " +
                "엑셀에서 그 파일을 닫고 다시 시도해 주세요.");
        }
        catch (Exception exception)
        {
            return ChapterWriteResult.Locked($"대본 워크북에 쓰지 못했습니다: {exception.Message}");
        }
    }
}
