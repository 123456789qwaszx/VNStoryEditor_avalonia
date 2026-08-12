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

    private static readonly string[] Headers =
        ["인덱스", "LineId", "유형", "태그", "조건라벨", "IN", "OUT", "화자", "내용", "스탯변화", "메모"];

    private const int ColumnIndex = 1;
    private const int ColumnLineId = 2;
    private const int ColumnKind = 3;
    private const int ColumnTag = 4;
    private const int ColumnConditionLabel = 5;
    private const int ColumnIn = 6;
    private const int ColumnOut = 7;
    private const int ColumnSpeaker = 8;
    private const int ColumnText = 9;
    private const int ColumnStatChanges = 10;
    private const int ColumnMemo = 11;

    /// <param name="conditionLabels">챕터 `조건` 시트의 라벨 (G-7). 여기 없는 라벨은 오류다.</param>
    /// <param name="statKeys">챕터 `스탯` 시트의 키. `스탯변화`가 이걸로 검사된다.</param>
    /// <exception cref="XlsxReadException">파일을 열 수 없을 때.</exception>
    public static EpisodeWorkbookModel Read(
        string path,
        IReadOnlyCollection<string>? conditionLabels = null,
        IReadOnlyCollection<string>? statKeys = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new XlsxReadException(path, $"워크북 파일이 없습니다: {path}");
        }

        conditionLabels ??= Array.Empty<string>();
        statKeys ??= Array.Empty<string>();

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
                new Dictionary<int, EpisodeSection>(),
                diagnostics);
        }

        IReadOnlyList<EpisodeRow> rows =
            ReadRows(sheet, path, conditionLabels, statKeys, diagnostics);

        IReadOnlyDictionary<int, EpisodeSection> sections =
            BuildSections(sheet, path, rows, diagnostics);

        VerifySectionCalls(sheet.Name, path, rows, sections, diagnostics);
        VerifyChoiceBlock(sheet.Name, path, rows, sections, diagnostics);

        return new EpisodeWorkbookModel(episodeId, path, sheet.Name, rows, sections, diagnostics);
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
        IReadOnlyCollection<string> conditionLabels,
        IReadOnlyCollection<string> statKeys,
        List<ChapterDiagnostic> diagnostics)
    {
        var rows = new List<EpisodeRow>();
        var seenIndexes = new Dictionary<int, int>();
        int previousIndex = int.MinValue;

        foreach (int row in DataRows(sheet))
        {
            string rawIndex = Cell(sheet, row, ColumnIndex);

            if (rawIndex.Length == 0)
            {
                // 인덱스 없는 행은 IN/OUT이 가리킬 수 없으므로 표의 일부가 아니다.
                // 견본의 설명문 줄들이 여기 걸린다 — 오류가 아니라 알림이다.
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Info,
                    ChapterDiagnosticCode.EpisodeIdBlank,
                    path, sheet.Name, row, ColumnIndex,
                    "인덱스가 없어 표의 행으로 읽지 않았습니다."));
                continue;
            }

            if (!int.TryParse(rawIndex, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                    out int index))
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.StatValueNotInteger,
                    path, sheet.Name, row, ColumnIndex,
                    $"인덱스 '{rawIndex}'가 정수가 아닙니다. IN/OUT이 이 값으로 서로를 가리키므로 " +
                    "숫자여야 합니다(10·20·30 방식 — G-5)."));
                continue;
            }

            if (seenIndexes.TryGetValue(index, out int firstRow))
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.EpisodeIdDuplicated,
                    path, sheet.Name, row, ColumnIndex,
                    $"인덱스 {index}가 {firstRow}행과 중복입니다. IN/OUT이 어느 쪽을 가리키는지 " +
                    "결정할 수 없습니다."));
                continue;
            }

            seenIndexes[index] = row;

            if (index < previousIndex)
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.EpisodeIdDuplicated,
                    path, sheet.Name, row, ColumnIndex,
                    $"인덱스 {index}가 앞 행({previousIndex})보다 작습니다. 표는 인덱스 오름차순이어야 " +
                    "구간의 앞뒤가 정해집니다."));
            }

            previousIndex = index;

            EpisodeRowKind kind = ReadKind(sheet, row, path, diagnostics);
            EpisodeRowTag tag = ReadTag(sheet, row, path, diagnostics);
            string? lineId = Optional(sheet, row, ColumnLineId);
            string? conditionLabel = Optional(sheet, row, ColumnConditionLabel);

            if (kind == EpisodeRowKind.If && lineId is not null)
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.ColumnHeaderUnexpected,
                    path, sheet.Name, row, ColumnLineId,
                    "IF 행은 라인이 아니므로 LineId를 가질 수 없습니다(소유자 확정). " +
                    "연출·세이브 타깃이 아닙니다."));
            }

            if (conditionLabel is not null &&
                kind is not (EpisodeRowKind.If or EpisodeRowKind.Option))
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.ConditionLabelUndefined,
                    path, sheet.Name, row, ColumnConditionLabel,
                    "조건라벨은 IF·OPTION 행에만 붙습니다(§3.2)."));
            }
            else if (conditionLabel is not null && !conditionLabels.Contains(conditionLabel))
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.ConditionLabelUndefined,
                    path, sheet.Name, row, ColumnConditionLabel,
                    $"조건라벨 '{conditionLabel}'이 챕터 `조건` 시트에 없습니다. " +
                    "라벨↔식의 원천은 그 시트입니다(G-7)."));
            }

            int? sectionCall = ReadIn(sheet, row, kind, path, diagnostics);
            string? outTarget = ReadOut(sheet, row, tag, path, diagnostics);

            StatDeltaParseResult deltas =
                StatDeltaParser.Parse(Cell(sheet, row, ColumnStatChanges), statKeys);

            foreach (ConditionParseProblem problem in deltas.Problems)
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    problem.Kind == ConditionProblemKind.UnknownStatKey
                        ? ChapterDiagnosticCode.StatKeyUnknown
                        : ChapterDiagnosticCode.StatValueNotInteger,
                    path, sheet.Name, row, ColumnStatChanges,
                    problem.Message));
            }

            rows.Add(new EpisodeRow(
                index,
                lineId,
                kind,
                tag,
                conditionLabel,
                sectionCall,
                outTarget,
                Cell(sheet, row, ColumnSpeaker),
                Cell(sheet, row, ColumnText),
                deltas.Deltas,
                Optional(sheet, row, ColumnMemo),
                row));
        }

        return rows;
    }

    private static EpisodeRowKind ReadKind(
        IXLWorksheet sheet, int row, string path, List<ChapterDiagnostic> diagnostics)
    {
        string raw = Cell(sheet, row, ColumnKind);

        return raw switch
        {
            "" => EpisodeRowKind.Dialogue,
            "IF" => EpisodeRowKind.If,
            "CHOICE" => EpisodeRowKind.Choice,
            "OPTION" => EpisodeRowKind.Option,
            _ => Unknown()
        };

        EpisodeRowKind Unknown()
        {
            diagnostics.Add(Cell(
                ChapterDiagnosticSeverity.Error,
                ChapterDiagnosticCode.ColumnHeaderUnexpected,
                path, sheet.Name, row, ColumnKind,
                $"유형 '{raw}'을 모릅니다. 빈칸(대사) · IF · CHOICE · OPTION 중 하나여야 합니다."));

            return EpisodeRowKind.Dialogue;
        }
    }

    private static EpisodeRowTag ReadTag(
        IXLWorksheet sheet, int row, string path, List<ChapterDiagnostic> diagnostics)
    {
        string raw = Cell(sheet, row, ColumnTag);

        return raw switch
        {
            "" => EpisodeRowTag.None,
            "INPUT" => EpisodeRowTag.Input,
            "OUT" => EpisodeRowTag.Out,
            _ => Unknown()
        };

        EpisodeRowTag Unknown()
        {
            diagnostics.Add(Cell(
                ChapterDiagnosticSeverity.Error,
                ChapterDiagnosticCode.ColumnHeaderUnexpected,
                path, sheet.Name, row, ColumnTag,
                $"태그 '{raw}'을 모릅니다. 빈칸 · INPUT · OUT 중 하나여야 합니다."));

            return EpisodeRowTag.None;
        }
    }

    private static int? ReadIn(
        IXLWorksheet sheet, int row, EpisodeRowKind kind, string path,
        List<ChapterDiagnostic> diagnostics)
    {
        string raw = Cell(sheet, row, ColumnIn);

        if (raw.Length == 0)
        {
            return null;
        }

        if (kind is not (EpisodeRowKind.If or EpisodeRowKind.Option))
        {
            diagnostics.Add(Cell(
                ChapterDiagnosticSeverity.Error,
                ChapterDiagnosticCode.ColumnHeaderUnexpected,
                path, sheet.Name, row, ColumnIn,
                "IN은 IF·OPTION 행에만 붙습니다 (G-6c)."));

            return null;
        }

        if (int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
        {
            return value;
        }

        diagnostics.Add(Cell(
            ChapterDiagnosticSeverity.Error,
            ChapterDiagnosticCode.StatValueNotInteger,
            path, sheet.Name, row, ColumnIn,
            $"IN '{raw}'이 정수가 아닙니다. 들어갈 구간의 시작 인덱스여야 합니다."));

        return null;
    }

    private static string? ReadOut(
        IXLWorksheet sheet, int row, EpisodeRowTag tag, string path,
        List<ChapterDiagnostic> diagnostics)
    {
        string raw = Cell(sheet, row, ColumnOut);

        if (raw.Length == 0)
        {
            if (tag == EpisodeRowTag.Out)
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.ColumnHeaderUnexpected,
                    path, sheet.Name, row, ColumnOut,
                    "OUT 태그가 붙은 행은 나갈 목적지를 적어야 합니다 — 인덱스이거나 END입니다."));
            }

            return null;
        }

        if (tag != EpisodeRowTag.Out)
        {
            diagnostics.Add(Cell(
                ChapterDiagnosticSeverity.Error,
                ChapterDiagnosticCode.ColumnHeaderUnexpected,
                path, sheet.Name, row, ColumnOut,
                "OUT 값은 OUT 태그가 붙은 행에만 적습니다(§3.2)."));

            return null;
        }

        if (string.Equals(raw, EpisodeFlow.EndMarker, StringComparison.OrdinalIgnoreCase))
        {
            return EpisodeFlow.EndMarker;
        }

        if (int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
        {
            return raw;
        }

        diagnostics.Add(Cell(
            ChapterDiagnosticSeverity.Error,
            ChapterDiagnosticCode.StatValueNotInteger,
            path, sheet.Name, row, ColumnOut,
            $"OUT '{raw}'을 읽지 못했습니다. 나갈 목적지 인덱스이거나 {EpisodeFlow.EndMarker}여야 합니다."));

        return null;
    }

    // ── 구간 (§3.3) ─────────────────────────────────────────────────────────

    /// <summary>
    /// <c>INPUT</c>에서 시작해 <c>OUT</c>까지를 한 구간으로 묶는다 — <b>양끝 포함</b>이 정의다.
    /// 규칙 2(짝 강제)는 여기서 잡힌다: <c>OUT</c>을 만나기 전에 다음 <c>INPUT</c>이나 표의 끝이
    /// 오면 짝이 없는 것이다.
    /// </summary>
    private static IReadOnlyDictionary<int, EpisodeSection> BuildSections(
        IXLWorksheet sheet,
        string path,
        IReadOnlyList<EpisodeRow> rows,
        List<ChapterDiagnostic> diagnostics)
    {
        var sections = new Dictionary<int, EpisodeSection>();
        var open = new List<EpisodeRow>();
        EpisodeRow? start = null;

        foreach (EpisodeRow row in rows)
        {
            if (row.Tag == EpisodeRowTag.Input)
            {
                if (start is not null)
                {
                    diagnostics.Add(Cell(
                        ChapterDiagnosticSeverity.Error,
                        ChapterDiagnosticCode.ColumnHeaderUnexpected,
                        path, sheet.Name, start.SourceRow, ColumnTag,
                        $"INPUT(인덱스 {start.Index})에 짝이 되는 OUT이 없습니다. " +
                        $"{row.SourceRow}행에서 다음 INPUT이 시작됩니다 — 짝은 강제입니다(§3.3 규칙 2)."));
                }

                start = row;
                open = [row];
                continue;
            }

            if (start is null)
            {
                continue;
            }

            open.Add(row);

            if (row.Tag != EpisodeRowTag.Out)
            {
                continue;
            }

            sections[start.Index] = new EpisodeSection(
                start.Index, open, row.OutTarget, CalledFromRow: null);

            start = null;
            open = [];
        }

        if (start is not null)
        {
            diagnostics.Add(Cell(
                ChapterDiagnosticSeverity.Error,
                ChapterDiagnosticCode.ColumnHeaderUnexpected,
                path, sheet.Name, start.SourceRow, ColumnTag,
                $"INPUT(인덱스 {start.Index})에 짝이 되는 OUT이 없습니다 — 표가 그대로 끝납니다. " +
                "짝은 강제입니다(§3.3 규칙 2)."));
        }

        return sections;
    }

    /// <summary>
    /// 규칙 1(<c>IN</c> 대상에 <c>INPUT</c>이 있는가) · 규칙 4(구간 재사용 금지) ·
    /// 규칙 5(중첩 금지)를 친다.
    /// </summary>
    private static void VerifySectionCalls(
        string sheetName,
        string path,
        IReadOnlyList<EpisodeRow> rows,
        IReadOnlyDictionary<int, EpisodeSection> sections,
        List<ChapterDiagnostic> diagnostics)
    {
        var owners = new Dictionary<int, EpisodeRow>();
        var insideSection = sections.Values
            .SelectMany(section => section.Rows)
            .ToDictionary(row => row.Index, row => row);

        foreach (EpisodeRow row in rows.Where(item => item.CallsSection))
        {
            int target = row.In!.Value;

            // 규칙 5 — 구간 안에서 또 IN이 열리면 안쪽 OUT이 어느 쌍을 닫는지 정해지지 않는다.
            if (insideSection.ContainsKey(row.Index))
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.ColumnHeaderUnexpected,
                    path, sheetName, row.SourceRow, ColumnIn,
                    "구간 안에서 또 IN을 열 수 없습니다(§3.3 규칙 5 — 중첩 금지). " +
                    "엑셀의 인덱스·태그 모델로는 중첩 구간의 경계가 모호해집니다."));
                continue;
            }

            // 규칙 1 — 가리킨 인덱스에 INPUT이 있어야 한다.
            if (!sections.ContainsKey(target))
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.ColumnHeaderUnexpected,
                    path, sheetName, row.SourceRow, ColumnIn,
                    $"IN={target}이지만 인덱스 {target} 행에 INPUT 태그가 없습니다(§3.3 규칙 1)."));
                continue;
            }

            // 규칙 4 — 한 구간은 한 진입점의 소유다.
            if (owners.TryGetValue(target, out EpisodeRow? first))
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.ColumnHeaderUnexpected,
                    path, sheetName, row.SourceRow, ColumnIn,
                    $"구간 {target}을 {first.SourceRow}행이 이미 가리키고 있습니다(§3.3 규칙 4 — " +
                    "구간 재사용 금지). 둘이 쓰면 평평화가 복제를 낳고 LineId 전역 유일성이 깨집니다."));
                continue;
            }

            owners[target] = row;
        }

        // 아무도 가리키지 않는 구간은 산출물에서 사라진다 — 조용히 빠뜨리지 않는다.
        foreach ((int startIndex, EpisodeSection section) in sections)
        {
            if (!owners.ContainsKey(startIndex))
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Warning,
                    ChapterDiagnosticCode.ColumnHeaderUnexpected,
                    path, sheetName, section.First.SourceRow, ColumnTag,
                    $"구간 {startIndex}을 가리키는 IN이 없습니다. 이 구간은 평평화 산출물에 " +
                    "나오지 않습니다."));
            }
        }
    }

    /// <summary>`CHOICE` 블록은 파일 맨 끝에 0개 또는 1개다 (§3.2 마지막 라인 특별취급).</summary>
    private static void VerifyChoiceBlock(
        string sheetName,
        string path,
        IReadOnlyList<EpisodeRow> rows,
        IReadOnlyDictionary<int, EpisodeSection> sections,
        List<ChapterDiagnostic> diagnostics)
    {
        List<EpisodeRow> choices = rows.Where(row => row.Kind == EpisodeRowKind.Choice).ToList();

        if (choices.Count > 1)
        {
            foreach (EpisodeRow extra in choices.Skip(1))
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.ColumnHeaderUnexpected,
                    path, sheetName, extra.SourceRow, ColumnKind,
                    $"CHOICE 블록은 파일에 0개 또는 1개입니다(§3.2). 첫 블록은 " +
                    $"{choices[0].SourceRow}행에 있습니다."));
            }
        }

        if (choices.Count == 0)
        {
            return;
        }

        // 선택지 뒤에 구간 밖 대사가 오면 맨 끝이 아니다.
        // 구간 소속은 태그로 판정할 수 없다 — 구간 가운데 줄에는 태그가 없다.
        EpisodeRow choice = choices[0];
        var inSection = sections.Values
            .SelectMany(section => section.Rows)
            .Select(row => row.Index)
            .ToHashSet();

        foreach (EpisodeRow after in rows.Where(row =>
                     row.Index > choice.Index &&
                     row.Kind == EpisodeRowKind.Dialogue &&
                     !inSection.Contains(row.Index)))
        {
            diagnostics.Add(Cell(
                ChapterDiagnosticSeverity.Error,
                ChapterDiagnosticCode.ColumnHeaderUnexpected,
                path, sheetName, after.SourceRow, ColumnIndex,
                "CHOICE 블록 뒤에 구간 밖 대사가 있습니다. 선택지는 파일 맨 끝이어야 합니다(§3.2)."));
        }
    }

    // ── 셀 ──────────────────────────────────────────────────────────────────

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
