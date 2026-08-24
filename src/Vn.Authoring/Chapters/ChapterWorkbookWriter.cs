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
            sheet.Cell(row, 3).SetValue(episodeId);
            sheet.Cell(row, 4).SetValue(Math.Round(x, 2));
            sheet.Cell(row, 5).SetValue(Math.Round(y, 2));
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
            episodes.Cell(newRow, 3).SetValue(newEpisodeId); // 대사엔트리 = EpisodeId (v3 규약)
            episodes.Cell(newRow, 4).SetValue(Math.Round(x, 2));
            episodes.Cell(newRow, 5).SetValue(Math.Round(y, 2));

            // 부모에서 나가는 길 하나 = 선택지 하나 (v9). 문구를 받았으면 그대로 적고,
            // 사전에 없는 낱말이면 사전에도 올려 둔다 — 다음부터 드롭다운에서 고른다.
            AppendEdge(workbook, parentEpisodeId, newEpisodeId, conditionLabel: null, optionLabel);
        });


    /// <summary>
    /// 속성 패널의 저장. null이 아닌 필드만 그 셀에 쓴다.
    /// 표시·해금조건은 v8에서 간선으로 옮겨 갔다 — 여기 없다.
    /// </summary>
    /// <remarks>
    /// ⚠ 2026-08-25에 열 번호를 바로잡았다. <c>종류</c> 폐지로 뒤가 한 칸 당겨진 것과 함께,
    /// <b>엔딩키를 쓰던 자리가 실은 `메모` 칸이었다</b> — v11에서 엔딩키가 간선으로 옮겨
    /// 갔는데 이 쓰기만 옛 자리에 남아, <c>UpdateEpisode(endingKey: …)</c>가 메모를 덮어쓰고
    /// 있었다. 에피소드 시트에 엔딩키 칸은 없으므로 그 인자를 걷는다.
    /// </remarks>
    public static ChapterWriteResult UpdateEpisode(
        string path,
        string episodeId,
        string? title = null,
        string? dialogueEntry = null,
        string? memo = null,
        bool? allowUnreachable = null) =>
        Mutate(path, workbook =>
        {
            (IXLWorksheet sheet, int row) = RequireEpisodeRow(workbook, episodeId);

            Set(sheet, row, 2, title);
            Set(sheet, row, 3, dialogueEntry);
            Set(sheet, row, 6, memo);

            if (allowUnreachable is { } allowed)
            {
                // `도달불가 허용`은 선택 열이다 — 처음 켤 때 머리글도 함께 만든다 (D3).
                // 리더가 머리글 이름으로 찾으므로 자리는 규격 칸 바로 뒤면 된다.
                const int AllowColumn = 7;

                if (!string.Equals(
                        sheet.Cell(1, AllowColumn).GetString(), "도달불가 허용", StringComparison.Ordinal))
                {
                    sheet.Cell(1, AllowColumn).SetValue("도달불가 허용");
                }

                sheet.Cell(row, AllowColumn).SetValue(allowed ? "TRUE" : "FALSE");
            }
        });

    /// <summary>
    /// EpisodeId 개명. <b>`간선`의 출발·도착과 픽스처 고정 선택은 함께 따라간다</b> — 신원이
    /// 바뀌었는데 참조가 남으면 유령 간선이 된다.
    ///
    /// 폐지된 <c>cleared:</c>(2026-08-25)가 옛 워크북에 남아 있으면 개명을 <b>막는다</b>.
    /// 식은 사람 소유라 툴이 고쳐 주지 않고(자동 추측 금지), 어차피 그 식은 이미 파서가
    /// 오류로 짚고 있다 — 개명으로 참조만 더 낡게 만들 이유가 없다.
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
                        "cleared: 는 2026-08-25에 폐지됐습니다 — `조건` 시트에서 Bool 스탯으로 " +
                        "먼저 바꿔 주세요. 조건식은 사람 소유라 툴이 고치지 않습니다.");
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
            if (string.Equals(episodes.Cell(episodeRow, 3).GetString(), oldId, StringComparison.Ordinal))
            {
                episodes.Cell(episodeRow, 3).SetValue(newId);
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
        string? optionLabel = null,
        string? statChanges = null) =>
        Mutate(path, workbook =>
            AppendEdge(workbook, fromEpisodeId, toEpisodeId, conditionLabel, optionLabel, statChanges));

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
        string? newOptionLabel,
        string? statChanges = null) =>
        Mutate(path, workbook =>
        {
            IXLWorksheet edges = RequireSheet(workbook, ChapterSheetNames.Edges);

            IXLRow row = FindEdgeRow(edges, fromEpisodeId, currentToEpisodeId, currentOptionLabel)
                ?? throw new InvalidOperationException(
                    $"간선 {fromEpisodeId}→{currentToEpisodeId}이 없습니다.");

            row.Cell(2).SetValue(newToEpisodeId);
            row.Cell(4).SetValue(newOptionLabel ?? string.Empty);
            Set(edges, row.RowNumber(), 3, statChanges); // 배선과 증감을 한 저장으로
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
        XLWorkbook workbook, string fromEpisodeId, string toEpisodeId,
        string? conditionLabel, string? optionLabel, string? statChanges = null)
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
        Set(edges, row, 3, statChanges);     // 스탯변화 — 길을 여는 그 저장에 같이 실린다
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

            Set(sheet, row.RowNumber(), 7, lockedMessage);   // 잠금 안내문 (H→G, v12+)
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

    // ── 화자 시트 폐지 (2026-08-23) ─────────────────────────────────────────
    //
    // 소유자: "챕터 엑셀을 눌러보면, 엑셀 내 어떤 것에서도 화자를 사용하지 않는다. 즉
    // 애초부터 챕터엑셀에 화자가 들어갈 이유가 전혀없다."
    //
    // 사실 확인이 그대로였다 — `조건` 시트는 간선의 표시조건·해금조건이 실제로 가리키지만
    // `화자`는 <b>어느 시트도, 검증도, 도달성 증명도, 내보내기도 안 본다.</b> 툴이 대본
    // 드롭다운에 쓰려고 남의 파일에 얹어 둔 사전이었고, 그래서 챕터마다 따로 적어야 하는
    // 값만 치렀다. 이제 화자는 <b>툴의 [화자] 탭</b>에서 `game.definition.json`에 적힌다.
    //
    // 여기 남은 것은 <b>지우는 길 하나</b>다. 만드는 길(EnsureSpeakerSheet)과 툴이 한 줄씩
    // 더하던 길(AddSpeaker)은 함께 사라졌다 — 없는 규격에 쓰는 문은 두지 않는다.

    /// <summary>
    /// 구판 `화자` 시트를 지운다 — 이행이 이름을 정의 파일로 옮긴 <b>직후에만</b> 부른다.
    ///
    /// 있음/없음은 읽기로만 확인한다 — 없는 시트를 지우겠다고 파일을 다시 저장하면 폴더 감시가
    /// 맴돈다(재읽기마다 불리는 자리다). 원본은 <c>.bak</c>으로 남긴다: 시트를 통째로 없애는
    /// 유일한 자리라 되돌릴 곳이 있어야 한다.
    ///
    /// ⚠ 부르는 쪽이 순서를 지켜야 한다: <b>지우기가 성공한 뒤에</b> 정의 파일에 저장한다.
    /// 반대로 하면 잠긴 워크북이 다음 재읽기에서 지운 이름을 되살린다.
    /// </summary>
    /// <returns>실제로 지웠으면 (true, Ok). 이미 없으면 파일에 손대지 않고 (false, Ok). 실패면 사유.</returns>
    public static (bool Removed, ChapterWriteResult Result) RemoveSpeakerSheet(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var probe = new XLWorkbook(stream);

            if (!probe.Worksheets.Any(sheet =>
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
            IXLWorksheet? sheet = workbook.Worksheets.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, ChapterSheetNames.Speakers, StringComparison.Ordinal));

            sheet?.Delete();
        }, backup: true);

        return (result.Written, result);
    }

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
            // v11 — `엔딩키`가 간선으로 옮겨 갔다. 엔딩이라는 한 개념이 (노드의 키) +
            // (간선의 연출)로 갈리지 않게, 간선 한 행에 모은다.
            ["EpisodeId", "제목", "대사엔트리", "X", "Y", "메모"]);
        IXLWorksheet edgeSheet = AddSheetWithHeaders(workbook, ChapterSheetNames.Edges,
            [
                "출발", "도착", "스탯변화", "선택지", "표시조건", "해금조건", "잠금 안내문",
                // v12 (2026-08-24) — `종류`·`연출` 폐지. 엔딩키만 남는다.
                // 같은 날 `잠금시 숨김`도 폐지 — 그 식을 표시조건에 적으면 결과가 같다.
                "엔딩키"
            ]);
        IXLWorksheet conditionSheet =
            AddSheetWithHeaders(workbook, ChapterSheetNames.Conditions, ["라벨", "스탯", "연산자", "값", "설명"]);
        IXLWorksheet statSheet = AddSheetWithHeaders(workbook, ChapterSheetNames.Stats,
            ["스탯키", "표시명", "초기값", "최소", "최대", "타입"]);
        // 선택지 사전 (v9) — 챕터가 함께 쓰는 문구 목록. 간선이 여기서 골라 문구를 적는다.
        IXLWorksheet choiceSheet = CreateChoiceSheet(workbook);

        // `화자` 시트는 여기서 사라졌다 (2026-08-23) — 챕터가 안 쓰는 사전이었다.
        // 화자는 툴 [화자] 탭 → `game.definition.json`.

        ApplyChapterDropdowns(episodeSheet, edgeSheet, conditionSheet, statSheet, choiceSheet);
        ApplyChapterChrome(workbook);

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

    // ── 시트 겉모습 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 챕터 워크북의 <b>겉모습</b> (2026-08-18 소유자 보고 — "새로 만든 건 밋밋하다").
    ///
    /// 소유자가 견본(ch05)과 자기가 만든 챕터를 나란히 열고 말했다. 견본을 뜯어 보니
    /// 손으로 만든 <b>서식 언어 하나</b>가 통째로 들어 있었고, 코드에는 그중 아무것도 없었다:
    ///
    /// <list type="bullet">
    /// <item>본문 글꼴 <b>맑은 고딕 10</b> (기본 Calibri 11이 아니다)</item>
    /// <item>머리글 = <b>굵은 흰 글자 + 시트마다 다른 진한 배경</b>. 탭을 잘못 눌렀다는 것을
    ///       색 하나로 안다 — 시트 일곱 개가 다 비슷하게 생기면 그게 안 보인다</item>
    /// <item>데이터 칸에 <b>얇은 회색 격자</b>(#BFBFBF)</item>
    /// <item>Id를 <b>참조</b>하는 열은 회색 — "여긴 내가 짓는 이름이 아니라 저기 있는 것을
    ///       가리키는 칸"</item>
    /// <item>메모는 <b>기울인 옅은 회색 9pt</b> — 데이터가 아니라 곁말이다</item>
    /// </list>
    ///
    /// <b>만들 때와 이행할 때가 같은 함수를 부른다.</b> 겉모습을 두 곳에 적으면 한쪽만
    /// 고쳐지는 날이 오고, 그날 다시 "어떤 건 예쁘고 어떤 건 밋밋하다"가 된다.
    ///
    /// 값은 견본에서 그대로 떠 왔다 — 소유자가 좋다고 한 것이 그 파일이므로, 새로 고르는
    /// 것보다 <b>이미 합격한 값을 옮기는</b> 편이 맞다. 견본에 없던 것만 새로 정했다:
    /// v11의 세 열(`종류`·`엔딩키`·`연출`)과, 견본을 만든 뒤에 생긴 두 사전 시트
    /// (`선택지`·`화자` — 파일 안에 이미 있던 남색 #1F4E79을 쓴다).
    /// </summary>
    internal static void ApplyChapterChrome(XLWorkbook workbook)
    {
        // 머리글 색이 곧 시트의 신원이다: 남藍 = 그래프 구조 · 초록 = 조건 · 회색 = 읽기전용
        // 미러 · 주황 = 테스트 데이터 · 청 = 어휘 사전.
        Chrome(workbook, ChapterSheetNames.Episodes, "#333F50",
            [14, 22, 12, 20, 7, 7, 20], reference: [1], note: [7]);

        Chrome(workbook, ChapterSheetNames.Edges, "#333F50",
            [14, 14, 14, 26, 26, 14, 12, 26, 10, 16, 22], reference: [1, 2], note: [8]);

        Chrome(workbook, ChapterSheetNames.Conditions, "#548235",
            [18, 26, 26, 26, 44], reference: [2], note: [5]);

        // `스탯`은 game.definition.json의 읽기전용 미러다 — 옅은 회색 바탕이 "여긴 원천이
        // 아니다"를 말한다.
        Chrome(workbook, ChapterSheetNames.Stats, "#7F7F7F",
            [14, 14, 10, 8, 8, 10], reference: [1], body: "#F2F2F2");

        Chrome(workbook, ChapterSheetNames.Choices, "#1F4E79",
            [8, 52, 24], reference: [1], note: [3]);

        Chrome(workbook, ChapterSheetNames.Fixtures, "#C55A11",
            [16, 8, 9, 9, 9, 40], body: "#FCE4D6", note: [6]);
    }

    /// <summary>본문 글꼴. 견본 전체가 이것이라 숫자 칸도 한글 칸도 같은 줄에 앉는다.</summary>
    private const string BodyFont = "맑은 고딕";

    /// <summary>서식을 미리 깔아 둘 행 수. 드롭다운이 닿는 곳까지가 "표"다.</summary>
    private const int ChromeRows = DropdownRows;

    /// <summary>
    /// 시트 하나에 서식을 입힌다. 없는 시트는 조용히 넘긴다 — `픽스처`처럼 있을 수도 없을
    /// 수도 있는 시트가 있고, 겉모습 때문에 이행이 실패하는 것은 값이 맞지 않는다.
    /// </summary>
    /// <param name="headerFill">머리글 배경. 시트마다 다르다 — 그게 이 색의 일이다.</param>
    /// <param name="reference">회색으로 낮출 열(1-기반) — 남의 Id를 가리키는 칸.</param>
    /// <param name="note">기울인 옅은 회색으로 낮출 열 — 메모·설명.</param>
    /// <param name="body">데이터 칸 배경. 없으면 흰색.</param>
    private static void Chrome(
        XLWorkbook workbook,
        string sheetName,
        string headerFill,
        int[] widths,
        int[]? reference = null,
        int[]? note = null,
        string? body = null)
    {
        IXLWorksheet? sheet = workbook.Worksheets
            .FirstOrDefault(candidate => candidate.Name == sheetName);

        if (sheet is null)
        {
            return;
        }

        IXLRange table = sheet.Range(1, 1, ChromeRows, widths.Length);

        table.Style.Font.SetFontName(BodyFont);
        table.Style.Font.SetFontSize(10);
        table.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        table.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
        table.Style.Border.SetOutsideBorderColor(XLColor.FromHtml("#BFBFBF"));
        table.Style.Border.SetInsideBorderColor(XLColor.FromHtml("#BFBFBF"));

        if (body is not null)
        {
            table.Style.Fill.SetBackgroundColor(XLColor.FromHtml(body));
        }

        foreach (int column in reference ?? [])
        {
            sheet.Range(2, column, ChromeRows, column).Style.Font
                 .SetFontColor(XLColor.FromHtml("#808080"));
        }

        foreach (int column in note ?? [])
        {
            IXLStyle style = sheet.Range(2, column, ChromeRows, column).Style;
            style.Font.SetItalic(true);
            style.Font.SetFontSize(9);
            style.Font.SetFontColor(XLColor.FromHtml("#7F7F7F"));
        }

        // 머리글은 맨 마지막에 — 위의 열 단위 서식이 1행까지 훑고 지나간다.
        IXLRange header = sheet.Range(1, 1, 1, widths.Length);
        header.Style.Font.SetBold(true);
        header.Style.Font.SetItalic(false);
        header.Style.Font.SetFontSize(10);
        header.Style.Font.SetFontColor(XLColor.White);
        header.Style.Fill.SetBackgroundColor(XLColor.FromHtml(headerFill));

        for (int column = 1; column <= widths.Length; column++)
        {
            sheet.Column(column).Width = widths[column - 1];
        }

        // 머리글 고정 — 아래로 내려가도 어느 칸인지 보인다. 열이 열한 개까지 늘어난
        // `간선` 시트에서는 이게 없으면 무슨 칸에 적는지 알 수 없다.
        sheet.SheetView.FreezeRows(1);

        // 자동 필터 — 기획자가 "엔딩 간선만" "이 에피소드에서 나가는 것만" 추려 보는 손잡이다.
        // 이미 걸려 있으면 그대로 둔다(사람이 걸어 둔 조건을 지우지 않는다).
        if (!sheet.AutoFilter.IsEnabled)
        {
            sheet.Range(1, 1, Math.Max(sheet.LastRowUsed()?.RowNumber() ?? 1, 1), widths.Length)
                 .SetAutoFilter();
        }
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
