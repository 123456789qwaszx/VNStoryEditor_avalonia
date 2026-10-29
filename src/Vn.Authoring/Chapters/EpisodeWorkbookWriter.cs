using ClosedXML.Excel;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 에피소드 워크북(산출물)에 <b>셀 단위로</b> 손대는 마지막 자리 — 남은 것은
/// <see cref="SeedFirstLineIfEmpty"/> 하나다.
///
/// ⛔ 화자 칸·내용 칸을 고치던 길(<c>RenameSpeaker</c>·<c>SetLine</c>)은 2026-10-29에 걷혔다
/// (`docs/plans/excel-boundary.md` C4 · R5). 원본은 프로젝트고 워크북은 산출물이다 — 고칠 것은
/// 프로젝트의 줄이고, 워크북은 <see cref="EpisodeScriptOutput"/>이 통째로 다시 낸다. 셀을 고치는
/// 길은 머리글 행(이쪽 1행 · 이미터 2행)처럼 규격을 두 벌로 만들고, 그 두 벌이 갈렸을 때
/// 조용히 실패했다.
///
/// 여기 남은 심기는 <b>빈 유물</b>(에피소드를 지웠다 같은 Id로 다시 더했을 때 남아 있던 파일)에
/// 첫 대사를 놓는 일이다 — 사람이 쓴 글에는 손대지 않는다. 산출물이 프로젝트에서 통째로 나오게
/// 되면 이것도 함께 사라진다(C5).
/// </summary>
public static class EpisodeWorkbookWriter
{
    private const int HeaderRow = 1;

    // 리더와 같은 6열 (v14). ⚠ 이 배열은 <see cref="EpisodeWorkbookReader"/>의 것과 같아야
    // 한다 — 시트를 찾는 근거가 머리글이라, 한쪽만 바뀌면 여기서 시트를 못 찾는다.
    private static readonly string[] Headers =
        ["인덱스", "LineId", "화자", "내용"];

    private const int ColumnIndex = 1;
    private const int ColumnSpeaker = 3;
    private const int ColumnText = 4;

    /// <summary>
    /// 워크북이 <b>사람의 흔적 하나 없이</b> 비어 있으면 첫 대사를 심는다 (2026-08-26).
    ///
    /// <b>왜 필요한가</b> — [＋ 에피소드]는 새 워크북에 첫 대사를 심는데
    /// (<see cref="EpisodeLibrary.EnsureWorkbook"/>의 <c>firstLine</c>), 그 함수는 이미 있는
    /// 파일을 절대 덮어쓰지 않는다. 그런데 <b>에피소드 삭제는 대본 파일을 남기므로</b>, 지운
    /// Id를 다시 더하면 빈 유물이 그대로 재사용되어 첫 대사가 안 심긴다(실사례 — 기능을
    /// 넣은 날 소유자가 바로 밟았다: "여전히 기본 대사는 비어있는 상태야").
    ///
    /// ⚠ <b>흔적이 하나라도 있으면 아무것도 안 한다</b> — 유형(블록)·화자·내용 어느 칸이든.
    /// 사람이 쓰다 만 대본에 툴이 글을 얹으면 "안 쓴 글이 생겼다"가 된다. 미리 깔린
    /// 인덱스(10·20·…)는 흔적이 아니다(템플릿이 깐 것이다).
    /// </summary>
    /// <returns>심었으면 Seeded가 true. 흔적이 있어 물러난 것도 Result는 성공이다.</returns>
    public static (ChapterWriteResult Result, bool Seeded) SeedFirstLineIfEmpty(
        string path, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        bool seeded = false;

        ChapterWriteResult result = Mutate(path, workbook =>
        {
            IXLWorksheet sheet = FindEpisodeSheet(workbook)
                ?? throw new InvalidOperationException(
                    "이 워크북에서 대본 시트를 찾지 못했습니다 — 머리글이 규격과 다릅니다.");

            bool hasContent = sheet.RowsUsed()
                .Where(row => row.RowNumber() > HeaderRow)
                .Any(row =>
                    Cell(sheet, row.RowNumber(), ColumnSpeaker).Length > 0 ||
                    Cell(sheet, row.RowNumber(), ColumnText).Length > 0);

            if (hasContent)
            {
                throw new NothingToWriteException();
            }

            // 인덱스는 깔려 있으면 그대로(2행 = 10), 지워졌으면 다시 놓는다 —
            // 인덱스 없는 행은 표의 일부가 아니라 리더가 조용히 버린다.
            if (Cell(sheet, HeaderRow + 1, ColumnIndex).Length == 0)
            {
                sheet.Cell(HeaderRow + 1, ColumnIndex).SetValue(10);
            }

            sheet.Cell(HeaderRow + 1, ColumnText).SetValue(text);
            seeded = true;
        });

        return (result, seeded);
    }

    /// <summary>쓸 것이 없다는 신호 — <see cref="Mutate"/>의 저장을 건너뛴다.</summary>
    private sealed class NothingToWriteException : Exception;

    private static IXLWorksheet? FindEpisodeSheet(XLWorkbook workbook) =>
        workbook.Worksheets.FirstOrDefault(sheet =>
            Headers.Select((header, offset) =>
                string.Equals(Cell(sheet, HeaderRow, offset + 1), header, StringComparison.Ordinal))
                .All(matches => matches));

    private static string Cell(IXLWorksheet sheet, int row, int column) =>
        sheet.Cell(row, column).GetString().Trim();

    /// <summary>
    /// 메모리 사본에서 고치고 원본에 저장한다 — <see cref="ChapterWorkbookWriter"/>의
    /// 같은 이름 메서드와 같은 규칙이다. 엑셀이 잠갔으면(또는 무엇이든 실패하면) 파일은
    /// 그대로 두고 사유만 돌려준다: 반쯤 쓴 워크북은 없다.
    ///
    /// ⚠ 백업(.bak)은 두지 않는다. 저쪽이 백업을 켜는 것은 <b>지우는 종류의 쓰기</b>인데
    /// (행·간선 삭제) 여기서 하는 것은 빈 파일에 첫 줄을 놓는 일뿐이다.
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
        catch (NothingToWriteException)
        {
            // 고칠 것이 없었다. 파일에 손대지 않은 것이 성공이다.
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
