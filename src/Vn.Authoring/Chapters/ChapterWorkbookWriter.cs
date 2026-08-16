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
            sheet.Cell(row, 3).SetValue("Main");
            sheet.Cell(row, 4).SetValue(episodeId);
            sheet.Cell(row, 5).SetValue(Math.Round(x, 2));
            sheet.Cell(row, 6).SetValue(Math.Round(y, 2));
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
            episodes.Cell(newRow, 3).SetValue("Main");
            episodes.Cell(newRow, 4).SetValue(newEpisodeId); // 대사엔트리 = EpisodeId (v3 규약)
            episodes.Cell(newRow, 5).SetValue(Math.Round(x, 2));
            episodes.Cell(newRow, 6).SetValue(Math.Round(y, 2));

            // 부모에서 나가는 길 하나 = 선택지 하나 (v9). 문구를 받았으면 그대로 적고,
            // 사전에 없는 낱말이면 사전에도 올려 둔다 — 다음부터 드롭다운에서 고른다.
            AppendEdge(workbook, parentEpisodeId, newEpisodeId, conditionLabel: null, optionLabel);
        });


    /// <summary>
    /// 속성 패널의 저장. null이 아닌 필드만 그 셀에 쓴다.
    /// 표시·해금조건은 v8에서 간선으로 옮겨 갔다 — 여기 없다.
    /// </summary>
    public static ChapterWriteResult UpdateEpisode(
        string path,
        string episodeId,
        string? title = null,
        string? kind = null,
        string? dialogueEntry = null,
        string? endingKey = null,
        string? memo = null,
        bool? allowUnreachable = null) =>
        Mutate(path, workbook =>
        {
            (IXLWorksheet sheet, int row) = RequireEpisodeRow(workbook, episodeId);

            Set(sheet, row, 2, title);
            Set(sheet, row, 3, kind);
            Set(sheet, row, 4, dialogueEntry);
            Set(sheet, row, 7, endingKey);
            Set(sheet, row, 8, memo);

            if (allowUnreachable is { } allowed)
            {
                // `도달불가 허용`은 선택 열(J)이다 — 처음 켤 때 머리글도 함께 만든다 (D3).
                if (!string.Equals(sheet.Cell(1, 10).GetString(), "도달불가 허용", StringComparison.Ordinal))
                {
                    sheet.Cell(1, 10).SetValue("도달불가 허용");
                }

                sheet.Cell(row, 10).SetValue(allowed ? "TRUE" : "FALSE");
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
            if (string.Equals(episodes.Cell(episodeRow, 4).GetString(), oldId, StringComparison.Ordinal))
            {
                episodes.Cell(episodeRow, 4).SetValue(newId);
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

            // `선택지` 시트는 손대지 않는다 (v9) — 문구 사전은 에피소드를 모른다.

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

            // `선택지` 사전은 건드리지 않는다 (v9) — 챕터 전체의 어휘라, 에피소드 하나가
            // 사라졌다고 낱말을 지우면 다른 에피소드의 드롭다운에서도 사라진다.

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
    // v9 (2026-08-17 소유자) — 길 하나가 곧 선택지 하나이고, D열에는 인덱스가 아니라
    // <b>문구 그 자체</b>가 들어간다. 신원은 (출발, 도착, 문구). `선택지` 시트는 어느
    // 에피소드의 소유물도 아닌 전역 사전이라, 여기서 참조하는 것은 드롭다운 목록뿐이다.

    /// <summary>간선 추가 — 그 길에 붙일 문구를 함께 받는다(비면 보이지 않는 기본).</summary>
    public static ChapterWriteResult AddEdge(
        string path,
        string fromEpisodeId,
        string toEpisodeId,
        string? conditionLabel = null,
        string? optionLabel = null) =>
        Mutate(path, workbook =>
            AppendEdge(workbook, fromEpisodeId, toEpisodeId, conditionLabel, optionLabel));

    /// <summary>
    /// 선택지 한 줄의 배선을 고친다 — 문구와 도착을 한 저장으로 (툴 편집 폼의 [수정]).
    /// 찾을 때는 고치기 <b>전</b>의 신원(출발, 도착, 문구)을 쓴다.
    /// </summary>
    public static ChapterWriteResult SetEdgeRoute(
        string path,
        string fromEpisodeId,
        string currentToEpisodeId,
        string? currentOptionLabel,
        string newToEpisodeId,
        string? newOptionLabel) =>
        Mutate(path, workbook =>
        {
            IXLWorksheet edges = RequireSheet(workbook, ChapterSheetNames.Edges);

            IXLRow row = FindEdgeRow(edges, fromEpisodeId, currentToEpisodeId, currentOptionLabel)
                ?? throw new InvalidOperationException(
                    $"간선 {fromEpisodeId}→{currentToEpisodeId}이 없습니다.");

            row.Cell(2).SetValue(newToEpisodeId);
            row.Cell(4).SetValue(newOptionLabel ?? string.Empty);
            EnsureChoiceLabel(workbook, newOptionLabel);
        });

    /// <summary>선택지 사전에 문구 한 줄 (툴의 [＋ 선택지]) — 어느 에피소드의 것도 아니다.</summary>
    public static ChapterWriteResult AddChoiceLabel(string path, string text) =>
        Mutate(path, workbook =>
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("선택지 문구가 비어 있습니다.");
            }

            if (!EnsureChoiceLabel(workbook, text))
            {
                throw new InvalidOperationException($"'{text}'는 이미 선택지 시트에 있습니다.");
            }
        });

    /// <summary>
    /// 선택지 사전에서 문구 한 줄 지우기. <b>간선은 건드리지 않는다</b> — 사전은 어휘집이지
    /// 배선이 아니라서, 이미 그 문구를 쓰고 있는 길은 그대로 산다(드롭다운에서만 사라진다).
    /// </summary>
    public static ChapterWriteResult RemoveChoiceLabel(string path, string text) =>
        Mutate(path, workbook =>
        {
            IXLWorksheet choices = RequireChoiceSheet(workbook);

            IXLRow row = choices.RowsUsed().Skip(1).FirstOrDefault(candidate =>
                    string.Equals(candidate.Cell(2).GetString().Trim(), text, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"선택지 시트에 '{text}'가 없습니다.");

            row.Delete();
        }, backup: true);

    /// <summary>간선 행 한 줄 붙이기 (AddEdge·AddNextEpisode 공용).</summary>
    private static void AppendEdge(
        XLWorkbook workbook, string fromEpisodeId, string toEpisodeId, string? conditionLabel, string? optionLabel)
    {
        IXLWorksheet edges = RequireSheet(workbook, ChapterSheetNames.Edges);

        if (FindEdgeRow(edges, fromEpisodeId, toEpisodeId, optionLabel) is not null)
        {
            throw new InvalidOperationException(
                $"간선 {fromEpisodeId}→{toEpisodeId}이 같은 선택지 문구로 이미 있습니다.");
        }

        int row = NextRow(edges);
        edges.Cell(row, 1).SetValue(fromEpisodeId);
        edges.Cell(row, 2).SetValue(toEpisodeId);
        Set(edges, row, 4, optionLabel);     // 선택지 = 문구 그대로 (v9)
        Set(edges, row, 6, conditionLabel);  // 해금조건 (v8 — E는 표시조건)
        edges.Cell(row, 7).SetValue("FALSE");

        EnsureChoiceLabel(workbook, optionLabel);
    }

    /// <summary>간선 삭제 — 사전의 문구는 남는다(다른 길에서 또 쓴다).</summary>
    public static ChapterWriteResult RemoveEdge(
        string path, string fromEpisodeId, string toEpisodeId, string? optionLabel = null) =>
        Mutate(path, workbook =>
        {
            IXLWorksheet sheet = RequireSheet(workbook, ChapterSheetNames.Edges);

            IXLRow found = FindEdgeRow(sheet, fromEpisodeId, toEpisodeId, optionLabel)
                ?? throw new InvalidOperationException($"간선 {fromEpisodeId}→{toEpisodeId}이 없습니다.");

            found.Delete();
        }, backup: true);

    /// <summary>
    /// 간선 행 찾기 — (출발, 도착)으로 좁히고, <paramref name="optionLabel"/>이 주어지면
    /// 문구까지 맞춘다(같은 도착으로 문구 여럿일 때의 정확한 신원 — v9).
    /// </summary>
    private static IXLRow? FindEdgeRow(
        IXLWorksheet sheet, string fromEpisodeId, string toEpisodeId, string? optionLabel = null) =>
        sheet.RowsUsed().Skip(1).FirstOrDefault(row =>
            string.Equals(row.Cell(1).GetString(), fromEpisodeId, StringComparison.Ordinal) &&
            string.Equals(row.Cell(2).GetString(), toEpisodeId, StringComparison.Ordinal) &&
            (optionLabel is null ||
             string.Equals(row.Cell(4).GetString().Trim(), optionLabel.Trim(), StringComparison.Ordinal)));

    /// <summary>
    /// 간선 한 줄의 속성 편집. null이 아닌 것만 쓴다 (관문 둘 포함 — v8).
    /// <paramref name="optionLabel"/>을 주면 문구도 함께 바뀐다 — 문구는 신원의 일부이므로
    /// (v9) 찾을 때는 <paramref name="matchOptionLabel"/>(고치기 전 값)을 쓴다.
    /// </summary>
    public static ChapterWriteResult UpdateEdge(
        string path,
        string fromEpisodeId,
        string toEpisodeId,
        string? conditionLabel = null,
        bool? hideWhenLocked = null,
        string? lockedMessage = null,
        string? statChanges = null,
        string? matchOptionLabel = null,
        string? visibleConditionLabel = null,
        string? optionLabel = null) =>
        Mutate(path, workbook =>
        {
            IXLWorksheet sheet = RequireSheet(workbook, ChapterSheetNames.Edges);

            IXLRow row = FindEdgeRow(sheet, fromEpisodeId, toEpisodeId, matchOptionLabel)
                ?? throw new InvalidOperationException($"간선 {fromEpisodeId}→{toEpisodeId}이 없습니다.");

            Set(sheet, row.RowNumber(), 5, visibleConditionLabel); // 표시조건
            Set(sheet, row.RowNumber(), 6, conditionLabel);        // 해금조건

            if (hideWhenLocked is { } hide)
            {
                sheet.Cell(row.RowNumber(), 7).SetValue(hide ? "TRUE" : "FALSE");
            }

            Set(sheet, row.RowNumber(), 8, lockedMessage);
            Set(sheet, row.RowNumber(), 3, statChanges); // 스탯변화(C) — 문법 검사는 리더가 한다

            if (optionLabel is not null)
            {
                Set(sheet, row.RowNumber(), 4, optionLabel); // 선택지(D) = 문구 그 자체
                EnsureChoiceLabel(workbook, optionLabel);
            }
        });

    private static IXLWorksheet RequireChoiceSheet(XLWorkbook workbook) =>
        workbook.Worksheets.FirstOrDefault(candidate =>
            candidate.Name == ChapterSheetNames.Choices)
        ?? CreateChoiceSheet(workbook);

    /// <summary>
    /// `선택지` 시트 = 챕터가 함께 쓰는 문구 사전 (v9). 인덱스는 사전 안의 순서일 뿐이라
    /// 옅은 회색이고, 사람이 쓰는 칸(대본)은 넓은 흰 칸이다. 머리글 고정.
    /// </summary>
    internal static IXLWorksheet CreateChoiceSheet(XLWorkbook workbook)
    {
        IXLWorksheet sheet = AddSheetWithHeaders(workbook, ChapterSheetNames.Choices,
            ["인덱스", "대본", "메모"]);

        sheet.Column(1).Width = 8;    // 인덱스 — 사전 안의 순서
        sheet.Column(2).Width = 52;   // 대본 — 사람이 쓰는 칸
        sheet.Column(3).Width = 24;   // 메모

        // 구조 열은 옅게 — "여긴 번호, 저긴 원고"가 한눈에 갈린다.
        sheet.Column(1).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F1F3F4"));
        sheet.Cell(1, 1).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E8EAED"));

        sheet.SheetView.FreezeRows(1);

        return sheet;
    }

    /// <summary>
    /// 문구가 사전에 없으면 한 줄 올린다 — 인덱스는 10·20·30으로 이어 받는다.
    /// 간선이 문구를 직접 쓰므로 이 등재는 <b>다음번 드롭다운을 위한 것</b>이지 배선이 아니다.
    /// </summary>
    /// <returns>새로 올렸으면 참, 이미 있었으면 거짓.</returns>
    private static bool EnsureChoiceLabel(XLWorkbook workbook, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false; // 빈 문구 = 보이지 않는 기본. 사전에 올릴 낱말이 없다.
        }

        IXLWorksheet choices = RequireChoiceSheet(workbook);

        if (choices.RowsUsed().Skip(1).Any(row =>
                string.Equals(row.Cell(2).GetString().Trim(), text.Trim(), StringComparison.Ordinal)))
        {
            return false;
        }

        int maxIndex = choices.RowsUsed().Skip(1)
            .Select(row => int.TryParse(row.Cell(1).GetString(), out int index) ? index : 0)
            .DefaultIfEmpty(0)
            .Max();

        int newRow = NextRow(choices);
        choices.Cell(newRow, 1).SetValue(maxIndex + 10);
        choices.Cell(newRow, 2).SetValue(text.Trim());

        return true;
    }

    // ── 조건 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// `조건` 시트에 한 줄 — 2026-08-16 개정으로 스탯·연산자·값 세 칸이다. 툴 패널의 식
    /// 문자열은 단일 항이면 세 칸으로 분해해 쓰고, 분해할 수 없는 것(cleared:·복합식)은
    /// 스탯 칸에 원문 그대로 둔다(리더의 탈출구와 짝). 검사는 검증기가 읽을 때 한다.
    /// </summary>
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
            WriteConditionCells(sheet, row, expression);
            Set(sheet, row, 5, description);
        });

    public static ChapterWriteResult UpdateCondition(
        string path, string label, string expression, string? description = null) =>
        Mutate(path, workbook =>
        {
            IXLWorksheet sheet = RequireSheet(workbook, ChapterSheetNames.Conditions);

            IXLRow row = sheet.RowsUsed().Skip(1).FirstOrDefault(candidate =>
                    string.Equals(candidate.Cell(1).GetString(), label, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"조건 라벨 '{label}'이 없습니다.");

            WriteConditionCells(sheet, row.RowNumber(), expression);
            Set(sheet, row.RowNumber(), 5, description);
        });

    /// <summary>식 문자열 → 스탯(B)·연산자(C)·값(D). 리더가 다시 조립하면 같은 식이 된다.</summary>
    private static void WriteConditionCells(IXLWorksheet sheet, int row, string expression)
    {
        if (ConditionExpressionParser.TryDecomposeSingle(
                expression, out string statKey, out string operatorText, out string valueText))
        {
            sheet.Cell(row, 2).SetValue(statKey);
            sheet.Cell(row, 3).SetValue(operatorText);
            Set(sheet, row, 4, valueText); // bool이면 빈 값 — 값 칸은 쓰지 않는다
        }
        else
        {
            sheet.Cell(row, 2).SetValue(expression); // 원문 탈출구 (cleared:·복합식)
            Set(sheet, row, 3, string.Empty);
            Set(sheet, row, 4, string.Empty);
        }
    }

    // ── 화자 ────────────────────────────────────────────────────────────────

    private static readonly string[] SpeakerHeaders = ["이름", "캐릭터키", "메모"];

    /// <summary>
    /// `화자` 시트가 없으면 만든다(2026-08-16 이전 워크북의 마이그레이션). 있으면 파일에
    /// 손대지 않고 false를 돌려준다 — 챕터 선택마다 불려도 쓰기는 한 번뿐이라 폴더 감시가
    /// 맴돌지 않는다.
    /// </summary>
    /// <returns>실제로 만들었으면 (true, Ok). 이미 있으면 파일에 손대지 않고 (false, Ok). 실패면 사유.</returns>
    public static (bool Created, ChapterWriteResult Result) EnsureSpeakerSheet(string path)
    {
        // 있음/없음은 읽기로만 확인한다 — 이미 있는 워크북을 다시 저장하는 낭비(와 그로 인한
        // 폴더 감시 재읽기)를 만들지 않는다. 호출자(리더)가 HasSpeakerSheet를 주는 경우에도
        // 파일이 그 사이 바뀌었을 수 있으므로 여기서 한 번 더 본다.
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var probe = new XLWorkbook(stream);

            if (probe.Worksheets.Any(sheet =>
                    string.Equals(sheet.Name, ChapterSheetNames.Speakers, StringComparison.Ordinal)))
            {
                return (false, ChapterWriteResult.Ok);
            }
        }
        catch (Exception exception)
        {
            return (false, ChapterWriteResult.Locked(
                $"워크북을 읽지 못했습니다(파일이 잠겨 있을 수 있습니다): {exception.Message}"));
        }

        ChapterWriteResult result = Mutate(path, workbook =>
        {
            if (workbook.Worksheets.Any(sheet =>
                    string.Equals(sheet.Name, ChapterSheetNames.Speakers, StringComparison.Ordinal)))
            {
                return; // 확인과 쓰기 사이에 누가 만들었다 — 그대로 둔다.
            }

            AddSheetWithHeaders(workbook, ChapterSheetNames.Speakers, SpeakerHeaders);
        });

        return (result.Written, result);
    }

    /// <summary>`화자` 시트에 이름 한 줄. 시트가 없으면(구판) 만들면서 더한다.</summary>
    public static ChapterWriteResult AddSpeaker(
        string path, string name, string? characterId = null, string? memo = null) =>
        Mutate(path, workbook =>
        {
            IXLWorksheet sheet = workbook.Worksheets.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, ChapterSheetNames.Speakers, StringComparison.Ordinal))
                ?? AddSheetWithHeaders(workbook, ChapterSheetNames.Speakers, SpeakerHeaders);

            if (sheet.RowsUsed().Skip(1).Any(row =>
                    string.Equals(row.Cell(1).GetString().Trim(), name.Trim(), StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"화자 '{name}'이 이미 있습니다.");
            }

            int row = NextRow(sheet);
            sheet.Cell(row, 1).SetValue(name.Trim());
            Set(sheet, row, 2, characterId);
            Set(sheet, row, 3, memo);
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

        // 픽스처 시트는 만들지 않는다 (2026-08-16 소유자 — 당장 안 쓰니 임시 제거.
        // 리더는 있으면 여전히 읽는다 — 되살릴 때 시트만 다시 만들면 된다).
        IXLWorksheet episodeSheet = AddSheetWithHeaders(workbook, ChapterSheetNames.Episodes,
            ["EpisodeId", "제목", "종류", "대사엔트리", "X", "Y", "엔딩키", "메모"]);
        IXLWorksheet edgeSheet = AddSheetWithHeaders(workbook, ChapterSheetNames.Edges,
            ["출발", "도착", "스탯변화", "선택지", "표시조건", "해금조건", "잠금시 숨김", "잠금 안내문"]);
        IXLWorksheet conditionSheet =
            AddSheetWithHeaders(workbook, ChapterSheetNames.Conditions, ["라벨", "스탯", "연산자", "값", "설명"]);
        IXLWorksheet statSheet = AddSheetWithHeaders(workbook, ChapterSheetNames.Stats,
            ["스탯키", "표시명", "초기값", "최소", "최대", "타입"]);
        // 선택지 사전 (v9) — 챕터가 함께 쓰는 문구 목록. 간선이 여기서 골라 문구를 적는다.
        IXLWorksheet choiceSheet = CreateChoiceSheet(workbook);
        AddSheetWithHeaders(workbook, ChapterSheetNames.Speakers, SpeakerHeaders);

        ApplyChapterDropdowns(episodeSheet, edgeSheet, conditionSheet, statSheet, choiceSheet);

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
    /// 챕터 워크북의 드롭다운·서식 일습 — 새 챕터 생성과 구판 이행(Migrator)이 같은 것 하나를
    /// 부른다(규약 사본 금지).
    ///
    /// - 에피소드 표시조건(G)·해금조건(H)과 간선 조건(E): `조건` 시트 라벨 열 참조 —
    ///   조건을 더하면 목록이 저절로 는다.
    /// - 조건 스탯(B): `스탯` 시트 키 열 참조. 연산자(C): &lt; &gt; == &gt;= &lt;= + true/false.
    ///   값(D)은 연산자가 true/false면 회색(조건부 서식) — bool 조건은 값 칸을 쓰지 않는다.
    /// - 스탯 타입(F): int/bool.
    /// </summary>
    internal static void ApplyChapterDropdowns(
        IXLWorksheet episodeSheet,
        IXLWorksheet edgeSheet,
        IXLWorksheet conditionSheet,
        IXLWorksheet statSheet,
        IXLWorksheet? choiceSheet = null)
    {
        // v8 — 관문은 간선의 것이다: 표시조건(E)·해금조건(F).
        AddConditionDropdown(edgeSheet, 5);
        AddConditionDropdown(edgeSheet, 6);
        AddYesNoDropdown(edgeSheet, 7);          // 잠금시 숨김

        if (choiceSheet is not null)
        {
            // v9 — 간선의 선택지(D)는 문구 그 자체다. 사전의 대본 열(B)을 통째로 목록에
            // 달아, 어느 에피소드에서든 챕터의 모든 문구를 고른다. 사전에 없는 문구를 손으로
            // 적는 것도 막지 않는다(어휘집이지 자물쇠가 아니다).
            IXLDataValidation edgeChoicePick = edgeSheet.Range(2, 4, DropdownRows, 4).CreateDataValidation();
            edgeChoicePick.List($"='{ChapterSheetNames.Choices}'!$B$2:$B${DropdownRows}", inCellDropdown: true);
            edgeChoicePick.ShowErrorMessage = false;
        }

        conditionSheet.Range(2, 2, DropdownRows, 2).CreateDataValidation()
            .List($"='{ChapterSheetNames.Stats}'!$A$2:$A${DropdownRows}", inCellDropdown: true);
        conditionSheet.Range(2, 3, DropdownRows, 3).CreateDataValidation()
            .List("\"<,>,==,>=,<=,true,false\"", inCellDropdown: true);

        // bool 조건(연산자 = true/false)이면 값 칸이 회색이 된다 — "여긴 안 쓴다"가 보인다.
        conditionSheet.Range(2, 4, DropdownRows, 4)
            .AddConditionalFormat()
            .WhenIsTrue("=OR($C2=\"true\",$C2=\"false\")")
            .Fill.SetBackgroundColor(XLColor.FromHtml("#D9D9D9"));

        statSheet.Range(2, 6, DropdownRows, 6).CreateDataValidation()
            .List("\"int,bool\"", inCellDropdown: true);
    }

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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 압도적으로 흔한 원인 하나를 이름으로 부른다 (2026-08-16 실사례) — 챕터를
            // 더블클릭해 엑셀로 열어 둔 채 툴에서 고치면 모든 쓰기가 여기로 떨어졌는데,
            // "파일이 잠겨 있거나 규칙 위반" + 영어 예외라 사람이 원인을 알 수 없었다.
            return ChapterWriteResult.Locked(
                $"엑셀이 '{Path.GetFileName(path)}'를 열고 있어 툴이 쓰지 못했습니다 — " +
                "엑셀에서 그 파일을 닫고 다시 시도해 주세요.");
        }
        catch (Exception exception)
        {
            return ChapterWriteResult.Locked($"워크북에 쓰지 못했습니다: {exception.Message}");
        }
    }

    /// <summary>
    /// 다른 앱(대개 엑셀)이 이 워크북을 열고 있는가 — <b>쓰지 않고</b> 알아본다.
    /// 열려 있으면 툴의 모든 편집이 거부되므로, 누르기 전에 화면이 먼저 말해 줄 수 있다.
    /// </summary>
    public static bool IsLockedByAnotherApp(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
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
