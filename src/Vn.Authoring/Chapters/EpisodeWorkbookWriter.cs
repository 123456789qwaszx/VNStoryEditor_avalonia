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

    // 리더와 같은 6열 (v14). ⚠ 이 배열은 <see cref="EpisodeWorkbookReader"/>의 것과 같아야
    // 한다 — 시트를 찾는 근거가 머리글이라, 한쪽만 바뀌면 여기서 시트를 못 찾는다.
    private static readonly string[] Headers =
        ["유형", "조건라벨", "인덱스", "LineId", "화자", "내용"];

    private const int ColumnKind = 1;
    private const int ColumnIndex = 3;
    private const int ColumnSpeaker = 5;
    private const int ColumnText = 6;

    /// <summary>
    /// 대사 한 줄의 화자·내용을 고친다. 행은 <b>인덱스(A열)로</b> 찾는다 — 줄의 신원이
    /// 인덱스이기 때문이다(G-5). 엑셀 행 번호로 찾으면 사람이 시트에서 행을 하나 끼우는
    /// 순간 엉뚱한 줄을 덮는다.
    /// </summary>
    /// <param name="index">C열 값. 노드의 <c>ExcelLineMap</c>이 LineId를 이 값에 묶어 둔다.</param>
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

    /// <summary>
    /// 블록 행(IF·ELSEIF·ENDIF)에 남은 인덱스를 비운다 (v14, 2026-08-24 소유자: "툴이
    /// 지워 준다").
    ///
    /// <b>왜 지워 줘야 하나</b> — 템플릿이 2~500행에 10·20·30을 <b>미리 깔아 두기</b>
    /// 때문이다. 사람이 42행에 IF를 치면 그 칸에는 이미 410이 적혀 있고, 그건 사람의
    /// 잘못이 아니다. 그래서 리더는 오류로 세우지 않고(빨간 줄은 고칠 것에만 쓴다) 여기서
    /// 조용히 치운다. <b>다만 조용히 하지는 않는다</b> — 몇 칸을 비웠는지 돌려주고 보고에 싣는다.
    ///
    /// ⚠ 비우는 것은 <b>인덱스 한 칸뿐이다.</b> 같은 행의 화자·내용이 잘못 적혀 있어도
    /// 여기서 지우지 않는다 — 그건 사람이 <b>쓴 글</b>이라, 지우면 "썼는데 사라졌다"가 된다.
    /// 그쪽은 리더가 오류로 짚어 사람이 고르게 한다(소유자: "잘못된 걸로. 아니면 작성을
    /// 못하게 막는 것도 좋아" — 손에서 막는 것은 엑셀 빗장이 맡는다).
    /// </summary>
    /// <returns>비운 칸 수. 쓸 것이 없으면 0이고 파일에 손대지 않는다.</returns>
    public static (ChapterWriteResult Result, int Cleared) ClearBlockRowIndexes(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        int cleared = 0;

        ChapterWriteResult result = Mutate(path, workbook =>
        {
            IXLWorksheet sheet = FindEpisodeSheet(workbook)
                ?? throw new InvalidOperationException(
                    "이 워크북에서 대본 시트를 찾지 못했습니다 — 머리글이 규격과 다릅니다.");

            foreach (IXLRow row in sheet.RowsUsed().Where(item => item.RowNumber() > HeaderRow))
            {
                int number = row.RowNumber();
                string kind = Cell(sheet, number, ColumnKind);

                if (kind.Length == 0 || string.Equals(kind, "대사", StringComparison.Ordinal))
                {
                    continue;
                }

                if (Cell(sheet, number, ColumnIndex).Length == 0)
                {
                    continue;
                }

                sheet.Cell(number, ColumnIndex).Clear(XLClearOptions.Contents);
                cleared++;
            }

            // 지울 것이 없으면 저장 자체를 하지 않는다 — 안 바뀐 파일을 다시 쓰면 감시가
            // 깨어나고 내용 해시가 바뀌어, 아무 일도 없었는데 전부 다시 읽는다.
            if (cleared == 0)
            {
                throw new NothingToWriteException();
            }
        });

        return cleared == 0 ? (ChapterWriteResult.Ok, 0) : (result, cleared);
    }

    /// <summary>
    /// 화자 개명을 이 워크북의 화자 칸(E열)까지 끌고 간다 (2026-08-24 소유자 — "화자의 이름을
    /// 편집한 경우도 연결이 이어지도록").
    ///
    /// [화자] 탭이 이름을 갈면 <b>드롭다운 목록만</b> 새것이 되고, 이미 셀에 적힌 옛 이름은
    /// 그대로 남아 미등록이 된다. 그 칸이 곧 대사 줄의 화자이므로 초상화 매핑이 끊기고,
    /// 공백 있는 이름은 파서가 산문으로 읽어 대사와 합쳐진다.
    ///
    /// <b>여는 것은 여전히 화자 한 칸뿐이다</b> — 줄을 더하거나 지우지 않고, 블록 행(IF·
    /// ELSEIF·ENDIF)은 건너뛴다(그 행에는 화자가 있을 수 없다). 글자가 <b>정확히</b> 같은
    /// 칸만 바꾼다: 사람이 손으로 적은 다른 이름을 추측해 고치지 않는다.
    /// </summary>
    /// <returns>바꾼 칸 수. 하나도 없으면 파일에 손대지 않는다.</returns>
    public static (ChapterWriteResult Result, int Changed) RenameSpeaker(
        string path, string oldName, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string from = (oldName ?? string.Empty).Trim();
        string to = (newName ?? string.Empty).Trim();

        if (from.Length == 0 || to.Length == 0 ||
            string.Equals(from, to, StringComparison.Ordinal))
        {
            return (ChapterWriteResult.Ok, 0);
        }

        int changed = 0;

        ChapterWriteResult result = Mutate(path, workbook =>
        {
            IXLWorksheet sheet = FindEpisodeSheet(workbook)
                ?? throw new InvalidOperationException(
                    "이 워크북에서 대본 시트를 찾지 못했습니다 — 머리글이 규격과 다릅니다.");

            foreach (IXLRow row in sheet.RowsUsed().Where(item => item.RowNumber() > HeaderRow))
            {
                int number = row.RowNumber();
                string kind = Cell(sheet, number, ColumnKind);

                if (kind.Length > 0 && !string.Equals(kind, "대사", StringComparison.Ordinal))
                {
                    continue;   // 블록 행 — 화자 칸이 열려 있지 않은 자리다.
                }

                if (!string.Equals(Cell(sheet, number, ColumnSpeaker), from, StringComparison.Ordinal))
                {
                    continue;
                }

                Set(sheet, number, ColumnSpeaker, to);
                changed++;
            }

            // 안 바뀐 파일을 다시 쓰면 감시가 깨어나고 내용 해시가 달라져, 아무 일도 없었는데
            // 그 챕터의 대본을 전부 다시 읽는다.
            if (changed == 0)
            {
                throw new NothingToWriteException();
            }
        });

        // 못 썼으면 센 것도 없던 일이다 — 파일은 옛 이름 그대로다.
        return result.Written ? (result, changed) : (result, 0);
    }

    /// <summary>쓸 것이 없다는 신호 — <see cref="Mutate"/>의 저장을 건너뛴다.</summary>
    private sealed class NothingToWriteException : Exception;

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
