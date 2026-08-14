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
    // ── 에피소드 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 새 에피소드 행을 `에피소드` 시트 끝에 더한다. Id는 호출자가 정한다(자동 발명 금지).
    /// 대사엔트리는 <b>EpisodeId와 같게</b> 자동으로 채운다(v3 규약) — 기획자가 관리할 값이
    /// 아니고, 에피소드 워크북도 EpisodeId로 이름 붙으므로 하나면 된다. 엑셀에서 다르게
    /// 적으면 그 값이 이긴다(사람이 이긴다).
    /// </summary>
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
            sheet.Cell(row, 5).SetValue(episodeId);
            sheet.Cell(row, 6).SetValue(Math.Round(x, 2));
            sheet.Cell(row, 7).SetValue(Math.Round(y, 2));
        });

    /// <summary>
    /// 다음 에피소드 추가 — 분기 저작의 핵심 동작. 행 추가와 간선 연결이 <b>한 번의 저장</b>이다
    /// (둘 중 하나만 성공한 채 남지 않는다).
    ///
    /// x·y는 호출자가 <see cref="ChapterBranchPlanner.SuggestPlacement"/>로 계산해서 넘긴다.
    /// 화면 배치는 깊이 레이아웃이 소유하므로(v3) 이 셀은 내보내기의 Position에나 쓰인다.
    /// </summary>
    public static ChapterWriteResult AddNextEpisode(
        string path,
        string parentEpisodeId,
        string newEpisodeId,
        string title,
        double x,
        double y,
        string? optionLabel = null) =>
        Mutate(path, workbook =>
        {
            IXLWorksheet episodes = RequireSheet(workbook, ChapterSheetNames.Episodes);

            if (FindRow(episodes, newEpisodeId) is not null)
            {
                throw new InvalidOperationException($"EpisodeId '{newEpisodeId}'가 이미 있습니다.");
            }

            if (FindRow(episodes, parentEpisodeId) is null)
            {
                throw new InvalidOperationException($"부모 에피소드 '{parentEpisodeId}'가 없습니다.");
            }

            int newRow = NextRow(episodes);
            episodes.Cell(newRow, 1).SetValue(newEpisodeId);
            episodes.Cell(newRow, 2).SetValue(title);
            episodes.Cell(newRow, 4).SetValue("Main");
            episodes.Cell(newRow, 5).SetValue(newEpisodeId); // 대사엔트리 = EpisodeId (v3 규약)
            episodes.Cell(newRow, 6).SetValue(Math.Round(x, 2));
            episodes.Cell(newRow, 7).SetValue(Math.Round(y, 2));

            IXLWorksheet edges = RequireSheet(workbook, ChapterSheetNames.Edges);
            int edgeRow = NextRow(edges);
            edges.Cell(edgeRow, 1).SetValue(parentEpisodeId);
            edges.Cell(edgeRow, 2).SetValue(newEpisodeId);
            Set(edges, edgeRow, 3, optionLabel);
            edges.Cell(edgeRow, 5).SetValue("FALSE");
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

            // 대사엔트리 = EpisodeId 규약(v3)을 따르던 행이면 함께 따라간다.
            // 사람이 다르게 적어 둔 값은 건드리지 않는다.
            if (string.Equals(episodes.Cell(episodeRow, 5).GetString(), oldId, StringComparison.Ordinal))
            {
                episodes.Cell(episodeRow, 5).SetValue(newId);
            }

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
        }, backup: true);

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
        }, backup: true);

    /// <summary>간선 한 줄의 속성 편집. null이 아닌 것만 쓴다.</summary>
    public static ChapterWriteResult UpdateEdge(
        string path,
        string fromEpisodeId,
        string toEpisodeId,
        string? optionLabel = null,
        string? conditionLabel = null,
        bool? hideWhenLocked = null,
        string? lockedMessage = null,
        string? statChanges = null) =>
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
            Set(sheet, row.RowNumber(), 7, statChanges); // 스탯변화 — 문법 검사는 리더가 한다
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

        IXLWorksheet episodeSheet = AddSheetWithHeaders(workbook, ChapterSheetNames.Episodes,
            ["EpisodeId", "제목", "인덱스", "종류", "대사엔트리", "X", "Y", "표시조건", "해금조건", "엔딩키", "메모"]);
        IXLWorksheet edgeSheet = AddSheetWithHeaders(workbook, ChapterSheetNames.Edges,
            ["출발", "도착", "선택지 라벨", "조건", "잠금시 숨김", "잠금 안내문", "스탯변화"]);
        AddSheetWithHeaders(workbook, ChapterSheetNames.Conditions, ["라벨", "조건식", "설명"]);

        // 조건 라벨을 적는 세 열은 `조건` 시트의 라벨 열을 가리키는 드롭다운이 된다.
        // 범위 참조라 조건을 더하면 목록이 저절로 따라온다 — 라벨은 손으로 적는 순간
        // 오타가 유령 참조가 되므로, 엑셀 단에서 막는 편이 검증기가 뒤늦게 잡는 것보다 낫다.
        AddConditionDropdown(episodeSheet, 8);   // 표시조건
        AddConditionDropdown(episodeSheet, 9);   // 해금조건
        AddConditionDropdown(edgeSheet, 4);      // 간선의 조건
        AddYesNoDropdown(edgeSheet, 5);          // 잠금시 숨김
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

    /// <summary>
    /// 챕터 개명 = 워크북 파일 이름 바꾸기. 시트 안에는 챕터 Id가 없으므로(파일 이름이 곧 Id)
    /// 이동 하나로 끝난다. 에피소드 워크북은 에피소드 Id로 이름 붙으므로 무관하다.
    /// 판(StoryFile) 이름은 호출자(셸)가 따라 바꾼다 — 챕터=판 1:1.
    /// </summary>
    public static ChapterWriteResult RenameChapterWorkbook(string folder, string oldId, string newId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newId);

        string source = Path.Combine(folder, oldId + ".xlsx");
        string target = Path.Combine(folder, newId + ".xlsx");

        if (!File.Exists(source))
        {
            return ChapterWriteResult.Locked($"챕터 워크북이 없습니다: {source}");
        }

        if (File.Exists(target))
        {
            return ChapterWriteResult.Locked($"챕터 '{newId}'가 이미 있습니다.");
        }

        try
        {
            File.Move(source, target);
            return ChapterWriteResult.Ok;
        }
        catch (Exception exception)
        {
            return ChapterWriteResult.Locked(
                $"챕터 이름을 바꾸지 못했습니다(파일이 잠겨 있을 수 있습니다): {exception.Message}");
        }
    }

    /// <summary>템플릿이 드롭다운을 까는 행 수. 이 아래로 더 쓰면 사람이 직접 적는다.</summary>
    private const int DropdownRows = 200;

    /// <summary>
    /// 그 열을 `조건` 시트 라벨 열(A2:A200)을 가리키는 목록으로 만든다. 정적 목록이 아니라
    /// <b>범위 참조</b>다 — 조건이 늘면 목록도 는다. 빈 칸(조건 없음)은 허용한다.
    /// </summary>
    private static void AddConditionDropdown(IXLWorksheet sheet, int column) =>
        sheet.Range(2, column, DropdownRows, column).CreateDataValidation()
            .List($"='{ChapterSheetNames.Conditions}'!$A$2:$A${DropdownRows}", inCellDropdown: true);

    private static void AddYesNoDropdown(IXLWorksheet sheet, int column) =>
        sheet.Range(2, column, DropdownRows, column).CreateDataValidation()
            .List("\"TRUE,FALSE\"", inCellDropdown: true);

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
    /// <param name="backup">
    /// 쓰기 전의 원본을 <c>{파일}.bak</c>으로 남길지. 툴 편집에는 Ctrl+Z가 없으므로
    /// <b>지우는 종류의 쓰기</b>(행·간선 삭제)는 이걸 켠다 — 실수해도 .bak을 .xlsx로
    /// 되돌리면 그만이다. 백업은 마지막 파괴적 쓰기 직전 상태 하나만 남는다(굴림).
    /// </param>
    private static ChapterWriteResult Mutate(string path, Action<XLWorkbook> edit, bool backup = false)
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

            if (backup)
            {
                // 이미 읽어 둔 버퍼로 쓴다 — 원본에 손대지 않고, 잠금과도 부딪치지 않는다.
                File.WriteAllBytes(path + ".bak", memory.ToArray());
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
