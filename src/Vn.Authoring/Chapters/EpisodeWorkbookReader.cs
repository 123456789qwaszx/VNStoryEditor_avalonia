using System.Globalization;
using ClosedXML.Excel;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 에피소드 워크북(§3.2의 11열)을 <see cref="EpisodeWorkbookModel"/>로 읽고, §3.3의 구조 규칙을
/// 친다 (G2-a).
///
/// <b>시트는 머리글로 찾는다.</b> §3.2가 시트 이름을 정하지 않았고 파일마다 다를 수 있다.
/// 첫 시트를 무조건 읽으면 설명 시트가 앞에 있는 워크북에서 엉뚱한 것을 읽는다 —
/// 견본 워크북이 정확히 그런 모양이다. 대신 <b>머리글 행이 규격과 맞는 첫 시트</b>를 쓴다.
///
/// 평평화는 여기 없다(G2-b). 여기서 끝나는 것은 "표를 정확히 읽고 구조가 성립하는지"까지다.
/// </summary>
public static class EpisodeWorkbookReader
{
    private const int HeaderRow = 1;

    // 6열 (v14, 2026-08-24 소유자). 왼쪽 두 칸이 <b>제어 행의 메타데이터</b>이고,
    // 오른쪽 네 칸이 <b>대사 줄</b>이다:
    //
    //     유형  조건라벨 │ 인덱스  LineId  화자  내용
    //     └─ 구조 ─────┘ └─ 대사 ──────────────────┘
    //
    // 그래서 첫 칸만 훑으면 대사·대사·IF·대사·ENDIF·대사 하는 문법이 그대로 보이고,
    // 인덱스는 <b>플레이어에게 전달되는 대사의 순번</b>이라는 뜻을 갖는다 — 구조를 그리는
    // 행이 번호를 가지면 그 번호가 대사 순번이 아니라 행 순번으로 바뀌어 버린다.
    // 소유자: "IF, ENDIF, SET 같은 행은 스토리 구조를 표현하는 행이지 대사가 아니니까."
    //
    // 앞 규격(v10 LineId 먼저 · v13 인덱스 먼저)의 파일은 이행기가 이 모양으로 옮긴다.
    // v15 (2026-09-16 소유자 — R-C) — `유형`·`조건라벨` 폐지. 조건 블록이 사라지자
    // `유형`의 값은 `대사` 하나뿐이 되었고, `조건라벨`이 가리키던 챕터 `조건` 시트의
    // 라벨은 [2] 진행 스탯이라 런타임이 대사에서 읽는 것을 금지했다. 남은 넷은 전부
    // <b>대사 줄의 것</b>이다 — 신원(인덱스·LineId)과 내용(화자·내용).
    private static readonly string[] Headers =
        ["인덱스", "LineId", "화자", "내용"];

    private const int ColumnIndex = 1;
    private const int ColumnLineId = 2;
    private const int ColumnSpeaker = 3;
    private const int ColumnText = 4;

    /// <exception cref="XlsxReadException">파일을 열 수 없을 때.</exception>
    public static EpisodeWorkbookModel Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new XlsxReadException(path, $"워크북 파일이 없습니다: {path}");
        }

        // ⛔ 파싱 캐시(`WorkbookParseCache`)는 2026-09-16에 걷혔다 (R-D). "대본 하나만
        //    저장해도 그 챕터의 대본을 전부 다시 판다"가 그 캐시의 존재 이유였는데,
        //    다시 읽지 않으면 캐시할 것이 없다. 되살리지 말 것.
        return Parse(path);
    }

    private static EpisodeWorkbookModel Parse(string path)
    {
        using XLWorkbook workbook = Open(path);

        var diagnostics = new List<ChapterDiagnostic>();
        string episodeId = Path.GetFileNameWithoutExtension(path);
        IXLWorksheet? sheet = FindEpisodeSheet(workbook);

        if (sheet is null)
        {
            diagnostics.Add(new ChapterDiagnostic(
                ChapterDiagnosticSeverity.Error,
                ChapterDiagnosticCode.SheetMissing,
                path, null, null, null,
                $"머리글이 규격(§3.2)과 맞는 시트가 없습니다. 첫 행이 " +
                $"'{string.Join(" · ", Headers)}' 여야 합니다."));

            return new EpisodeWorkbookModel(
                episodeId, path, string.Empty,
                Array.Empty<EpisodeRow>(),
                diagnostics);
        }

        IReadOnlyList<EpisodeRow> rows = ReadRows(sheet, path, diagnostics);

        // v15 — 블록 짝 검증(IF~ENDIF)은 조건 블록과 함께 사라졌다. 남은 규칙은
        // "인덱스가 줄의 신원이다" 하나이고, 그것은 행 루프가 지킨다.

        return new EpisodeWorkbookModel(episodeId, path, sheet.Name, rows, diagnostics);
    }

    private static XLWorkbook Open(string path)
    {
        try
        {
            // 핸들은 우리가 연다 — 경로 생성자는 실패해도 핸들을 놓지 않는다.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return new XLWorkbook(stream);
        }
        catch (Exception exception)
        {
            throw new XlsxReadException(path, $"워크북을 읽지 못했습니다: {exception.Message}", exception);
        }
    }

    private static IXLWorksheet? FindEpisodeSheet(XLWorkbook workbook) =>
        workbook.Worksheets.FirstOrDefault(sheet =>
            Headers.Select((header, offset) =>
                string.Equals(Cell(sheet, HeaderRow, offset + 1), header, StringComparison.Ordinal))
                .All(matches => matches));

    // ── 행 ──────────────────────────────────────────────────────────────────

    private static IReadOnlyList<EpisodeRow> ReadRows(
        IXLWorksheet sheet,
        string path,
        List<ChapterDiagnostic> diagnostics)
    {
        var rows = new List<EpisodeRow>();
        var seenIndexes = new Dictionary<int, int>();
        int previousIndex = int.MinValue;

        foreach (int row in DataRows(sheet))
        {
            // v15 — 모든 행이 대사다. 인덱스가 있으면 표의 행이고, 없으면 아니다.
            string rawIndex = Cell(sheet, row, ColumnIndex);

            if (rawIndex.Length == 0)
            {
                // 인덱스가 대사 줄의 신원이라 없는 행은 표의 일부가 아니다.
                // 다만 화자나 내용이 적혀 있다면 그건 설명문이 아니라 <b>버려지는 대사</b>다 —
                // 조용히 넘기면 "여러 줄을 썼는데 안 나온다"가 된다(실사례). 크게 말한다.
                bool looksLikeDialogue =
                    Cell(sheet, row, ColumnSpeaker).Length > 0 ||
                    Cell(sheet, row, ColumnText).Length > 0;

                diagnostics.Add(Cell(
                    looksLikeDialogue
                        ? ChapterDiagnosticSeverity.Warning
                        : ChapterDiagnosticSeverity.Info,
                    ChapterDiagnosticCode.EpisodeIdBlank,
                    path, sheet.Name, row, ColumnIndex,
                    looksLikeDialogue
                        ? "화자·내용이 있는데 인덱스(A열)가 비어 이 행을 건너뜁니다 — " +
                          "A열에 번호를 적어 주세요(위 행보다 큰 수, 10·20·30 방식)."
                        : "인덱스가 없어 표의 행으로 읽지 않았습니다."));
                continue;
            }

            if (!int.TryParse(rawIndex, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                    out int index))
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.StatValueNotInteger,
                    path, sheet.Name, row, ColumnIndex,
                    $"인덱스 '{rawIndex}'가 정수가 아닙니다. 이 값이 대사 줄의 신원이므로 " +
                    "숫자여야 합니다(10·20·30 방식 — G-5)."));
                continue;
            }

            if (seenIndexes.TryGetValue(index, out int firstRow))
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.EpisodeIdDuplicated,
                    path, sheet.Name, row, ColumnIndex,
                    $"인덱스 {index}가 {firstRow}행과 중복입니다. 인덱스가 줄의 신원이라 " +
                    "같은 번호가 둘이면 연출이 어느 줄에 붙는지 정해지지 않습니다."));
                continue;
            }

            seenIndexes[index] = row;

            // v10 — 오름차순은 <b>권고</b>다. 읽는 순서는 시트의 행 순서이고 인덱스는 줄의
            // 신원(연출·세이브가 매달리는 열쇠)일 뿐이다. 번호를 다시 매기면 그 줄에 달린
            // 연출이 통째로 끊기므로, 이행기도 순서를 고치지 않고 번호를 그대로 둔다.
            if (index < previousIndex)
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Info,
                    ChapterDiagnosticCode.EpisodeIdDuplicated,
                    path, sheet.Name, row, ColumnIndex,
                    $"인덱스 {index}가 앞 행({previousIndex})보다 작습니다 — 읽는 순서는 " +
                    "시트의 행 순서라 동작에는 지장이 없지만, 번호가 뒤죽박죽이면 사람이 읽기 어렵습니다."));
            }

            previousIndex = index;

            var parsed = new EpisodeRow(
                index,
                Optional(sheet, row, ColumnLineId),
                Cell(sheet, row, ColumnSpeaker),
                Cell(sheet, row, ColumnText),
                row);

            // v15 — 조건 블록은 폐지됐다. 구판 파일은 이행기가 그 행을 걷지만, <b>손에 익은
            // 사람이 v15 시트에 다시 `IF`를 치는 길</b>이 남는다. 그것을 그대로 실어 보내면
            // 플레이어가 "IF"라는 대사를 듣는다 — 읽는 시점에 짚어야 며칠 뒤 내보내기에서
            // 거부당하는 일이 없다(막는 자리와 권하는 자리를 여기서 합친다).
            if (BlockWord(parsed.Speaker) ||
                (parsed.Speaker.Length == 0 && BlockWord(parsed.Text)))
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.EpisodeConditionBlockRetired,
                    path, sheet.Name, row,
                    parsed.Speaker.Length > 0 ? ColumnSpeaker : ColumnText,
                    "조건 블록(IF·ELSEIF·ENDIF)은 폐지됐습니다 — 대본의 모든 행은 대사입니다. " +
                    "분기가 필요하면 챕터 `간선` 시트의 표시조건·해금조건으로 올려 주세요."));
                continue;
            }

            // 인덱스만 있고 아무것도 안 쓴 행은 표의 일부가 아니다 — 템플릿이 500행까지
            // 미리 깔아 둔 자리라서, 여기서 거르지 않으면 빈자리가 대사로 세어져 엉뚱한
            // 오류를 낸다(실사례). 인덱스는 위의 중복·오름차순 검사에 이미 참여했다.
            if (parsed.IsBlank)
            {
                continue;
            }

            rows.Add(parsed);
        }

        return rows;
    }

    /// <summary>폐지된 조건 블록 낱말. <see cref="EpisodeWorkbookMigrator"/>의 목록과 같아야 한다.</summary>
    private static readonly string[] BlockWords = ["IF", "ELSEIF", "ELSE IF", "ENDIF", "END"];

    private static bool BlockWord(string value) =>
        BlockWords.Contains(value, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<int> DataRows(IXLWorksheet sheet) =>
        sheet.RowsUsed()
            .Select(row => row.RowNumber())
            .Where(row => row > HeaderRow)
            .OrderBy(row => row);

    private static string Cell(IXLWorksheet sheet, int row, int column)
    {
        IXLCell cell = sheet.Cell(row, column);

        return cell.DataType switch
        {
            XLDataType.Blank => string.Empty,
            XLDataType.Number => cell.GetDouble().ToString(CultureInfo.InvariantCulture),
            XLDataType.Boolean => cell.GetBoolean() ? "TRUE" : "FALSE",
            _ => cell.GetString().Trim()
        };
    }

    private static string? Optional(IXLWorksheet sheet, int row, int column)
    {
        string value = Cell(sheet, row, column);
        return value.Length == 0 ? null : value;
    }

    private static ChapterDiagnostic Cell(
        ChapterDiagnosticSeverity severity,
        ChapterDiagnosticCode code,
        string path,
        string sheetName,
        int row,
        int column,
        string message) =>
        new(severity, code, path, sheetName, row,
            XLHelper.GetColumnLetterFromNumber(column), message);
}
