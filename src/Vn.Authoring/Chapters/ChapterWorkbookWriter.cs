using ClosedXML.Excel;

namespace Vn.Authoring.Chapters;

/// <summary>쓰기 한 번의 결과. 실패는 예외가 아니라 사유다 — 엑셀이 잠근 것은 오류가 아니라 일상이다.</summary>
public sealed record ChapterWriteResult(bool Written, string? Failure)
{
    public static ChapterWriteResult Ok { get; } = new(true, null);

    public static ChapterWriteResult Locked(string reason) => new(false, reason);
}

/// <summary>
/// 챕터 워크북에 쓰는 유일한 자리 (G-2 v2, 2026-08-12 소유자 개정).
///
/// <b>엑셀이 계속 원본이다.</b> 툴의 그래프 편집은 전부 여기로 모여 해당 셀만 고친다 —
/// 셀 단위 외과수술이며, 시트 전체를 다시 만들지 않으므로 서식·드롭다운·사람이 적어 둔
/// 다른 칸이 그대로 남는다. 대량 작업(대사)은 계속 엑셀에서 하고, 구조·조건 같은 소량
/// 작업만 이 경로를 지난다.
///
/// <b>자동 레이아웃·자동 재번호는 없다.</b> 사람이(또는 사람의 드래그가) 준 값만 그대로 쓴다.
///
/// 쓰기는 LineId 되쓰기와 같은 규칙이다: 메모리 사본으로 열고(경로 생성자는 실패 시 핸들을
/// 놓지 않는다), 엑셀이 잠갔으면 쓰지 않고 사유를 돌려준다. 저장이 끝나면 폴더 감시가
/// 다시 읽어 화면이 따라온다 — 쓰는 쪽이 화면을 직접 고칠 필요가 없다.
/// </summary>
public static class ChapterWorkbookWriter
{
    // ── 위치 (드래그) ───────────────────────────────────────────────────────

    /// <summary>드래그로 놓은 자리. 그 에피소드 행의 F(X)·G(Y) 두 셀만 쓴다.</summary>
    public static ChapterWriteResult SetEpisodePosition(
        string path, string episodeId, double x, double y) =>
        Mutate(path, workbook =>
        {
            (IXLWorksheet sheet, int row) = RequireEpisodeRow(workbook, episodeId);
            sheet.Cell(row, 6).SetValue(Math.Round(x, 2));
            sheet.Cell(row, 7).SetValue(Math.Round(y, 2));
        });

    // ── 에피소드 ────────────────────────────────────────────────────────────

    /// <summary>새 에피소드 행을 `에피소드` 시트 끝에 더한다. Id는 호출자가 정한다(자동 발명 금지).</summary>
    public static ChapterWriteResult AddEpisode(
        string path, string episodeId, string title, double x, double y) =>
        Mutate(path, workbook =>
        {
            IXLWorksheet sheet = RequireSheet(workbook, ChapterSheetNames.Episodes);

            if (FindRow(sheet, episodeId) is not null)
            {
                throw new InvalidOperationException($"EpisodeId '{episodeId}'가 이미 있습니다.");
            }

            int row = NextRow(sheet);
            sheet.Cell(row, 1).SetValue(episodeId);
            sheet.Cell(row, 2).SetValue(title);
            sheet.Cell(row, 4).SetValue("Main");
            sheet.Cell(row, 6).SetValue(Math.Round(x, 2));
            sheet.Cell(row, 7).SetValue(Math.Round(y, 2));
            // 대사엔트리(E)는 비워 둔다 — 비면 오류로 잡히므로 사람이 채울 때까지 검증이 알려 준다.
        });

    /// <summary>속성 패널의 [적용]. null이 아닌 필드만 그 셀에 쓴다.</summary>
    public static ChapterWriteResult UpdateEpisode(
        string path,
        string episodeId,
        string? title = null,
        string? index = null,
        string? kind = null,
        string? dialogueEntry = null,
        string? visibleConditionLabel = null,
        string? unlockConditionLabel = null,
        string? endingKey = null,
        string? memo = null,
        bool? allowUnreachable = null) =>
        Mutate(path, workbook =>
        {
            (IXLWorksheet sheet, int row) = RequireEpisodeRow(workbook, episodeId);

            Set(sheet, row, 2, title);
            Set(sheet, row, 3, index);
            Set(sheet, row, 4, kind);
            Set(sheet, row, 5, dialogueEntry);
            Set(sheet, row, 8, visibleConditionLabel);
            Set(sheet, row, 9, unlockConditionLabel);
            Set(sheet, row, 10, endingKey);
            Set(sheet, row, 11, memo);

            if (allowUnreachable is { } allowed)
            {
                // `도달불가 허용`은 선택 열(L)이다 — 처음 켤 때 머리글도 함께 만든다 (D3).
                if (!string.Equals(sheet.Cell(1, 12).GetString(), "도달불가 허용", StringComparison.Ordinal))
                {
                    sheet.Cell(1, 12).SetValue("도달불가 허용");
                }

                sheet.Cell(row, 12).SetValue(allowed ? "TRUE" : "FALSE");
            }
        });

    /// <summary>
    /// EpisodeId 개명. <b>`간선`의 출발·도착과 픽스처 고정 선택은 함께 따라간다</b> — 신원이
    /// 바뀌었는데 참조가 남으면 유령 간선이 된다. 단 조건식의 <c>cleared:</c>는 건드리지 않는다:
    /// 식은 사람 소유라 툴이 고쳐 주지 않는다(자동 추측 금지). 그런 참조가 있으면 실패로 알린다.
    /// </summary>
    public static ChapterWriteResult RenameEpisode(string path, string oldId, string newId) =>
        Mutate(path, workbook =>
        {
            IXLWorksheet conditions = RequireSheet(workbook, ChapterSheetNames.Conditions);

            foreach (IXLRow row in conditions.RowsUsed().Skip(1))
            {
                if (row.Cell(2).GetString().Contains($"cleared:{oldId}", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"조건 '{row.Cell(1).GetString()}'의 식이 cleared:{oldId}를 참조합니다. " +
                        "조건식은 사람 소유라 툴이 고치지 않습니다 — `조건` 시트를 먼저 고쳐 주세요.");
                }
            }

            (IXLWorksheet episodes, int episodeRow) = RequireEpisodeRow(workbook, oldId);

            if (FindRow(episodes, newId) is not null)
            {
                throw new InvalidOperationException($"EpisodeId '{newId}'가 이미 있습니다.");
            }

            episodes.Cell(episodeRow, 1).SetValue(newId);

            IXLWorksheet edges = RequireSheet(workbook, ChapterSheetNames.Edges);

            foreach (IXLRow row in edges.RowsUsed().Skip(1))
            {
                foreach (int column in new[] { 1, 2 })
                {
                    if (string.Equals(row.Cell(column).GetString(), oldId, StringComparison.Ordinal))
                    {
                        row.Cell(column).SetValue(newId);
                    }
                }
            }

            if (workbook.Worksheets.FirstOrDefault(sheet =>
                    sheet.Name == ChapterSheetNames.Fixtures) is { } fixtures)
            {
                foreach (IXLCell cell in fixtures.CellsUsed(cell =>
                             cell.GetString().Contains(oldId, StringComparison.Ordinal) &&
                             cell.Address.RowNumber > 1))
                {
                    cell.SetValue(cell.GetString()
                        .Replace($"{oldId}→", $"{newId}→", StringComparison.Ordinal)
                        .Replace($"→{oldId}", $"→{newId}", StringComparison.Ordinal));
                }
            }
        });

    /// <summary>
    /// 에피소드 행 삭제. 그 에피소드를 끝점으로 하는 간선과, 픽스처 고정 선택에서 그 에피소드를
    /// 지나는 항목도 함께 지운다 — 참조가 남으면 리더가 유령으로 잡는다(그게 옳다).
    /// </summary>
    public static ChapterWriteResult RemoveEpisode(string path, string episodeId) =>
        Mutate(path, workbook =>
        {
            (IXLWorksheet episodes, int row) = RequireEpisodeRow(workbook, episodeId);
            episodes.Row(row).Delete();

            IXLWorksheet edges = RequireSheet(workbook, ChapterSheetNames.Edges);

            foreach (IXLRow edgeRow in edges.RowsUsed().Skip(1)
                         .Where(candidate =>
                             string.Equals(candidate.Cell(1).GetString(), episodeId, StringComparison.Ordinal) ||
                             string.Equals(candidate.Cell(2).GetString(), episodeId, StringComparison.Ordinal))
                         .ToList())
            {
                edgeRow.Delete();
            }

            if (workbook.Worksheets.FirstOrDefault(sheet =>
                    sheet.Name == ChapterSheetNames.Fixtures) is not { } fixtures)
            {
                return;
            }

            // 고정 선택 셀은 "a→b; c→d" 목록이다 — 지운 에피소드를 지나는 항목만 걷어낸다.
            foreach (IXLCell cell in fixtures.CellsUsed(candidate =>
                         candidate.Address.RowNumber > 1 &&
                         candidate.GetString().Contains('→', StringComparison.Ordinal)))
            {
                string[] kept = cell.GetString()
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(entry =>
                        !entry.Split('→', 2).Select(side => side.Trim())
                            .Contains(episodeId, StringComparer.Ordinal))
                    .ToArray();

                cell.SetValue(string.Join("; ", kept));
            }
        });

    // ── 간선 ────────────────────────────────────────────────────────────────

    public static ChapterWriteResult AddEdge(
        string path,
        string fromEpisodeId,
        string toEpisodeId,
        string? optionLabel = null,
        string? conditionLabel = null) =>
        Mutate(path, workbook =>
        {
            IXLWorksheet sheet = RequireSheet(workbook, ChapterSheetNames.Edges);

            bool duplicate = sheet.RowsUsed().Skip(1).Any(row =>
                string.Equals(row.Cell(1).GetString(), fromEpisodeId, StringComparison.Ordinal) &&
                string.Equals(row.Cell(2).GetString(), toEpisodeId, StringComparison.Ordinal));

            if (duplicate)
            {
                throw new InvalidOperationException(
                    $"간선 {fromEpisodeId}→{toEpisodeId}이 이미 있습니다.");
            }

            int row = NextRow(sheet);
            sheet.Cell(row, 1).SetValue(fromEpisodeId);
            sheet.Cell(row, 2).SetValue(toEpisodeId);
            Set(sheet, row, 3, optionLabel);
            Set(sheet, row, 4, conditionLabel);
            sheet.Cell(row, 5).SetValue("FALSE");
        });

    public static ChapterWriteResult RemoveEdge(string path, string fromEpisodeId, string toEpisodeId) =>
        Mutate(path, workbook =>
        {
            IXLWorksheet sheet = RequireSheet(workbook, ChapterSheetNames.Edges);

            IXLRow? found = sheet.RowsUsed().Skip(1).FirstOrDefault(row =>
                string.Equals(row.Cell(1).GetString(), fromEpisodeId, StringComparison.Ordinal) &&
                string.Equals(row.Cell(2).GetString(), toEpisodeId, StringComparison.Ordinal));

            if (found is null)
            {
                throw new InvalidOperationException($"간선 {fromEpisodeId}→{toEpisodeId}이 없습니다.");
            }

            found.Delete();
        });

    /// <summary>간선 한 줄의 속성 편집. null이 아닌 것만 쓴다.</summary>
    public static ChapterWriteResult UpdateEdge(
        string path,
        string fromEpisodeId,
        string toEpisodeId,
        string? optionLabel = null,
        string? conditionLabel = null,
        bool? hideWhenLocked = null,
        string? lockedMessage = null) =>
        Mutate(path, workbook =>
        {
            IXLWorksheet sheet = RequireSheet(workbook, ChapterSheetNames.Edges);

            IXLRow row = sheet.RowsUsed().Skip(1).FirstOrDefault(candidate =>
                    string.Equals(candidate.Cell(1).GetString(), fromEpisodeId, StringComparison.Ordinal) &&
                    string.Equals(candidate.Cell(2).GetString(), toEpisodeId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"간선 {fromEpisodeId}→{toEpisodeId}이 없습니다.");

            Set(sheet, row.RowNumber(), 3, optionLabel);
            Set(sheet, row.RowNumber(), 4, conditionLabel);

            if (hideWhenLocked is { } hide)
            {
                sheet.Cell(row.RowNumber(), 5).SetValue(hide ? "TRUE" : "FALSE");
            }

            Set(sheet, row.RowNumber(), 6, lockedMessage);
        });

    // ── 조건 ────────────────────────────────────────────────────────────────

    /// <summary>`조건` 시트에 라벨↔식 한 줄. 식의 내용은 검사하지 않는다 — 검증기가 읽을 때 잡는다.</summary>
    public static ChapterWriteResult AddCondition(
        string path, string label, string expression, string? description = null) =>
        Mutate(path, workbook =>
        {
            IXLWorksheet sheet = RequireSheet(workbook, ChapterSheetNames.Conditions);

            if (sheet.RowsUsed().Skip(1).Any(row =>
                    string.Equals(row.Cell(1).GetString(), label, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"조건 라벨 '{label}'이 이미 있습니다.");
            }

            int row = NextRow(sheet);
            sheet.Cell(row, 1).SetValue(label);
            sheet.Cell(row, 2).SetValue(expression);
            Set(sheet, row, 3, description);
        });

    public static ChapterWriteResult UpdateCondition(
        string path, string label, string expression, string? description = null) =>
        Mutate(path, workbook =>
        {
            IXLWorksheet sheet = RequireSheet(workbook, ChapterSheetNames.Conditions);

            IXLRow row = sheet.RowsUsed().Skip(1).FirstOrDefault(candidate =>
                    string.Equals(candidate.Cell(1).GetString(), label, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"조건 라벨 '{label}'이 없습니다.");

            row.Cell(2).SetValue(expression);
            Set(sheet, row.RowNumber(), 3, description);
        });

    // ── 새 챕터 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// §3.1 규격의 빈 챕터 워크북을 만든다. 있으면 아무것도 하지 않는다 — 덮어쓰지 않는다.
    /// 스탯 시트는 game.definition의 변수로 채우고, 없으면 비워 두되 검증이 "2~5개" 경고로 알린다.
    /// </summary>
    public static bool EnsureChapterWorkbook(
        string folder, string chapterId, IReadOnlyList<(string Key, string Name)>? stats = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(chapterId);

        string path = Path.Combine(folder, chapterId + ".xlsx");

        if (File.Exists(path))
        {
            return false;
        }

        Directory.CreateDirectory(folder);

        using var workbook = new XLWorkbook();

        AddSheetWithHeaders(workbook, ChapterSheetNames.Episodes,
            ["EpisodeId", "제목", "인덱스", "종류", "대사엔트리", "X", "Y", "표시조건", "해금조건", "엔딩키", "메모"]);
        AddSheetWithHeaders(workbook, ChapterSheetNames.Edges,
            ["출발", "도착", "선택지 라벨", "조건", "잠금시 숨김", "잠금 안내문"]);
        AddSheetWithHeaders(workbook, ChapterSheetNames.Conditions, ["라벨", "조건식", "설명"]);
        IXLWorksheet statSheet = AddSheetWithHeaders(workbook, ChapterSheetNames.Stats,
            ["스탯키", "표시명", "초기값", "최소", "최대"]);
        AddSheetWithHeaders(workbook, ChapterSheetNames.Fixtures,
            ["픽스처명", "활성", "고정 선택 (에피소드ID→도착ID)"]);

        int statRow = 2;

        foreach ((string key, string name) in stats ?? Array.Empty<(string, string)>())
        {
            statSheet.Cell(statRow, 1).SetValue(key);
            statSheet.Cell(statRow, 2).SetValue(name);
            statSheet.Cell(statRow, 3).SetValue(0);
            statSheet.Cell(statRow, 4).SetValue(0);
            statSheet.Cell(statRow, 5).SetValue(10);
            statRow++;
        }

        workbook.SaveAs(path);
        return true;
    }

    private static IXLWorksheet AddSheetWithHeaders(XLWorkbook workbook, string name, string[] headers)
    {
        IXLWorksheet sheet = workbook.AddWorksheet(name);

        for (int column = 1; column <= headers.Length; column++)
        {
            IXLCell cell = sheet.Cell(1, column);
            cell.SetValue(headers[column - 1]);
            cell.Style.Font.SetBold(true);
            cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E8EAED"));
        }

        return sheet;
    }

    // ── 공통 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 메모리 사본에서 고치고 원본에 저장한다. 엑셀이 잠갔으면(또는 무엇이든 실패하면)
    /// 파일은 그대로 두고 사유만 돌려준다 — 반쯤 쓴 워크북은 없다.
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
        catch (Exception exception)
        {
            return ChapterWriteResult.Locked(
                $"워크북에 쓰지 못했습니다(파일이 잠겨 있거나 규칙 위반): {exception.Message}");
        }
    }

    private static (IXLWorksheet Sheet, int Row) RequireEpisodeRow(XLWorkbook workbook, string episodeId)
    {
        IXLWorksheet sheet = RequireSheet(workbook, ChapterSheetNames.Episodes);

        return (sheet, FindRow(sheet, episodeId)
            ?? throw new InvalidOperationException($"에피소드 '{episodeId}' 행이 없습니다."));
    }

    private static IXLWorksheet RequireSheet(XLWorkbook workbook, string name) =>
        workbook.Worksheets.FirstOrDefault(sheet =>
            string.Equals(sheet.Name, name, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"'{name}' 시트가 없습니다.");

    private static int? FindRow(IXLWorksheet sheet, string episodeId) =>
        sheet.RowsUsed().Skip(1)
            .FirstOrDefault(row =>
                string.Equals(row.Cell(1).GetString(), episodeId, StringComparison.Ordinal))
            ?.RowNumber();

    private static int NextRow(IXLWorksheet sheet) =>
        (sheet.LastRowUsed()?.RowNumber() ?? 1) + 1;

    private static void Set(IXLWorksheet sheet, int row, int column, string? value)
    {
        if (value is null)
        {
            return; // null = 바꾸지 않는다. 지우려면 빈 문자열을 준다.
        }

        if (value.Length == 0)
        {
            sheet.Cell(row, column).Clear(XLClearOptions.Contents);
        }
        else
        {
            sheet.Cell(row, column).SetValue(value);
        }
    }
}
