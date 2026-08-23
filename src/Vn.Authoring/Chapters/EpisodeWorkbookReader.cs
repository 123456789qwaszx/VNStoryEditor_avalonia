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
    private static readonly string[] Headers =
        ["유형", "조건라벨", "인덱스", "LineId", "화자", "내용"];

    private const int ColumnKind = 1;
    private const int ColumnConditionLabel = 2;
    private const int ColumnIndex = 3;
    private const int ColumnLineId = 4;
    private const int ColumnSpeaker = 5;
    private const int ColumnText = 6;

    /// <param name="conditionLabels">챕터 `조건` 시트의 라벨 (G-7). 여기 없는 라벨은 오류다.</param>
    /// <exception cref="XlsxReadException">파일을 열 수 없을 때.</exception>
    public static EpisodeWorkbookModel Read(
        string path,
        IReadOnlyCollection<string>? conditionLabels = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new XlsxReadException(path, $"워크북 파일이 없습니다: {path}");
        }

        IReadOnlyCollection<string> labels = conditionLabels ?? Array.Empty<string>();

        // 내용이 그대로면 답도 그대로다 (2026-08-24 성능) — 읽기는 순수 함수다.
        // 대본 하나만 저장해도 그 챕터의 대본을 전부 다시 파고들던 것이, 실측
        // "변경 없는 동기화"의 91%였다(`WorkbookParseCache`).
        //
        // ⚠ 조건 라벨도 열쇠에 넣는다 — 같은 파일이라도 라벨 목록이 다르면 진단이 다르다.
        return WorkbookParseCache.Read(
            path,
            variant: string.Join("|", labels.OrderBy(label => label, StringComparer.Ordinal)),
            () => Parse(path, labels));
    }

    private static EpisodeWorkbookModel Parse(
        string path, IReadOnlyCollection<string> conditionLabels)
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

        IReadOnlyList<EpisodeRow> rows =
            ReadRows(sheet, path, conditionLabels, diagnostics);

        VerifyBlocks(sheet.Name, path, rows, diagnostics);

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
        IReadOnlyCollection<string> conditionLabels,
        List<ChapterDiagnostic> diagnostics)
    {
        var rows = new List<EpisodeRow>();
        var seenIndexes = new Dictionary<int, int>();
        int previousIndex = int.MinValue;

        foreach (int row in DataRows(sheet))
        {
            // ⚠ <b>유형을 먼저 읽는다</b> (v14). 예전에는 인덱스가 먼저였고 인덱스 없는
            // 행은 표에서 빠졌는데, 이제 블록 행에는 인덱스가 <b>없는 것이 정상</b>이라
            // 그 순서로는 IF·ENDIF가 통째로 사라진다. 무슨 줄인지가 나머지를 좌우하므로
            // 읽는 순서도 시트의 열 순서와 같아졌다.
            EpisodeRowKind kind = ReadKind(sheet, row, path, diagnostics);
            string rawIndex = Cell(sheet, row, ColumnIndex);
            int? index = null;

            if (kind is EpisodeRowKind.Dialogue)
            {
                if (rawIndex.Length == 0)
                {
                    // 인덱스가 대사 줄의 신원이라 없는 행은 표의 일부가 아니다.
                    // 다만 화자나 내용이 적혀 있다면 그건 설명문이 아니라 **버려지는 대사**다 —
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
                            ? "화자·내용이 있는데 인덱스(C열)가 비어 이 행을 건너뜁니다 — " +
                              "C열에 번호를 적어 주세요(위 행보다 큰 수, 10·20·30 방식)."
                            : "인덱스가 없어 표의 행으로 읽지 않았습니다."));
                    continue;
                }

                if (!int.TryParse(rawIndex, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                        out int parsedIndex))
                {
                    diagnostics.Add(Cell(
                        ChapterDiagnosticSeverity.Error,
                        ChapterDiagnosticCode.StatValueNotInteger,
                        path, sheet.Name, row, ColumnIndex,
                        $"인덱스 '{rawIndex}'가 정수가 아닙니다. 이 값이 대사 줄의 신원이므로 " +
                        "숫자여야 합니다(10·20·30 방식 — G-5)."));
                    continue;
                }

                if (seenIndexes.TryGetValue(parsedIndex, out int firstRow))
                {
                    diagnostics.Add(Cell(
                        ChapterDiagnosticSeverity.Error,
                        ChapterDiagnosticCode.EpisodeIdDuplicated,
                        path, sheet.Name, row, ColumnIndex,
                        $"인덱스 {parsedIndex}가 {firstRow}행과 중복입니다. 인덱스가 줄의 신원이라 " +
                        "같은 번호가 둘이면 연출이 어느 줄에 붙는지 정해지지 않습니다."));
                    continue;
                }

                seenIndexes[parsedIndex] = row;
                index = parsedIndex;
            }
            else if (rawIndex.Length > 0)
            {
                // 블록 행에 남은 번호 (v14). <b>오류로 세우지 않는다</b> — 템플릿이 2~500행에
                // 번호를 미리 깔아 두므로, 사람이 그 자리에 IF를 치는 순간 제 잘못도 아닌
                // 빨간 줄을 보게 된다. 대신 <b>툴이 지운다</b>(소유자 결정 2026-08-24):
                // <see cref="EpisodeWorkbookWriter.ClearBlockRowIndexes"/>가 동기화에서
                // 그 칸을 비우고, 그 사실은 보고에 적힌다. 읽기는 순수 함수라 여기서 쓰지 않는다.
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Info,
                    ChapterDiagnosticCode.BlockRowIndexStray,
                    path, sheet.Name, row, ColumnIndex,
                    $"{Word(kind)} 행의 인덱스 '{rawIndex}'는 대사 순번이 아니므로 툴이 비웁니다 — " +
                    "인덱스는 플레이어에게 전달되는 대사의 번호입니다(v14)."));
            }

            // v10 — 오름차순은 이제 <b>권고</b>다. 읽는 순서는 시트의 행 순서이고 인덱스는
            // 줄의 신원(연출·세이브가 매달리는 열쇠)일 뿐이다. 구판에서 오름차순이 규칙이었던
            // 이유는 IN/OUT이 인덱스로 구간의 앞뒤를 정했기 때문인데, 블록에는 그 일이 없다.
            // 이행기가 구간을 제자리로 옮길 때 번호를 그대로 두는 것도 이 완화 덕이다 —
            // 번호를 다시 매기면 그 줄에 달린 연출이 통째로 끊긴다.
            //
            // ⚠ 블록 행은 이 검사에 참여하지 않는다 (v14) — 번호가 없으니 견줄 것이 없다.
            if (index is { } current)
            {
                if (current < previousIndex)
                {
                    diagnostics.Add(Cell(
                        ChapterDiagnosticSeverity.Info,
                        ChapterDiagnosticCode.EpisodeIdDuplicated,
                        path, sheet.Name, row, ColumnIndex,
                        $"인덱스 {current}가 앞 행({previousIndex})보다 작습니다 — 읽는 순서는 " +
                        "시트의 행 순서라 동작에는 지장이 없지만, 번호가 뒤죽박죽이면 사람이 읽기 어렵습니다."));
                }

                previousIndex = current;
            }

            string? lineId = Optional(sheet, row, ColumnLineId);
            string? conditionLabel = Optional(sheet, row, ColumnConditionLabel);

            if (kind is not EpisodeRowKind.Dialogue && lineId is not null)
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.ColumnHeaderUnexpected,
                    path, sheet.Name, row, ColumnLineId,
                    $"{Word(kind)} 행은 라인이 아니므로 LineId를 가질 수 없습니다(소유자 확정). " +
                    "연출·세이브 타깃이 아닙니다."));
            }

            if (conditionLabel is not null && kind is not (EpisodeRowKind.If or EpisodeRowKind.ElseIf))
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.ConditionLabelUndefined,
                    path, sheet.Name, row, ColumnConditionLabel,
                    "조건라벨은 IF·ELSEIF 행에만 붙습니다(§3.2)."));
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

            // 블록 행(IF·ELSEIF·ENDIF)은 <b>흐름만</b> 그린다 — 화자·내용을 여기서 막는다
            // (2026-08-24 소유자: "화자와 내용을 … If,ElseIf,EndIf일 경우 안 적게 막아줘").
            //
            // ⚠ 예전에는 ENDIF만 막았다. IF·ELSEIF에 적은 글은 그때 <b>조용히 버려졌다</b> —
            // 평평화가 블록 행의 화자·내용을 안 보기 때문이다. 작가에게는 "썼는데 안 나온다"로
            // 나타난다(같은 모양의 사고가 이 표에서 여러 번 났다).
            if (kind is not EpisodeRowKind.Dialogue &&
                (Cell(sheet, row, ColumnSpeaker).Length > 0 || Cell(sheet, row, ColumnText).Length > 0))
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.ColumnHeaderUnexpected,
                    path, sheet.Name, row, ColumnText,
                    $"{Word(kind)} 행은 블록의 흐름만 그립니다 — 화자·내용을 적으면 그 대사가 " +
                    "어느 쪽에 속하는지 모호해지고, 산출에는 실리지 않습니다. " +
                    "대사는 그 위나 아래의 자기 행에 적습니다."));
            }

            var parsed = new EpisodeRow(
                index,
                lineId,
                kind,
                conditionLabel,
                Cell(sheet, row, ColumnSpeaker),
                Cell(sheet, row, ColumnText),
                row);

            // 인덱스만 있고 아무것도 안 쓴 행은 표의 일부가 아니다 — 템플릿이 500행까지
            // (아래 IsBlank는 대사 행만 고르므로 블록 행은 걸리지 않는다)
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

    private static EpisodeRowKind ReadKind(
        IXLWorksheet sheet, int row, string path, List<ChapterDiagnostic> diagnostics)
    {
        string raw = Cell(sheet, row, ColumnKind);

        return raw switch
        {
            // 빈칸과 `대사`는 같은 뜻이다 (2026-08-17 소유자: "유형에서 IF와 END밖에 못
            // 고른다"). 드롭다운에 `대사`가 있어야 셀을 지우는 대신 <b>고를</b> 수 있다.
            "" or "대사" => EpisodeRowKind.Dialogue,
            "IF" => EpisodeRowKind.If,
            "ELSEIF" or "ELSE IF" => EpisodeRowKind.ElseIf,

            // `ENDIF`가 정본이다 (2026-08-17 소유자) — `END` 혼자면 "에피소드가 여기서
            // 끝난다"로 읽힌다(구판 OUT 열의 END가 정확히 그 뜻이었다). 사람이 그냥 END를
            // 치는 것도 흔하므로 같은 뜻으로 받는다 — 뜻이 하나뿐이라 주인이 갈리지 않는다.
            "ENDIF" or "END" => EpisodeRowKind.End,

            // v9에서 선택지의 주인이 챕터 `간선` 시트로 옮겨 갔다 — 대본에 남은 이 낱말은
            // 옮기다 만 흔적이다. 어디로 가야 하는지까지 말한다.
            "CHOICE" or "OPTION" => Moved(),
            _ => Unknown()
        };

        EpisodeRowKind Moved()
        {
            diagnostics.Add(Cell(
                ChapterDiagnosticSeverity.Error,
                ChapterDiagnosticCode.OptionEdgeMismatch,
                path, sheet.Name, row, ColumnKind,
                $"'{raw}'은 대본에서 폐지됐습니다(v9). 선택지는 챕터 엑셀의 `선택지` 시트에 " +
                "문구를 적고 `간선` 시트에서 길을 잇습니다 — 이 행은 지워 주세요."));

            return EpisodeRowKind.Dialogue;
        }

        EpisodeRowKind Unknown()
        {
            diagnostics.Add(Cell(
                ChapterDiagnosticSeverity.Error,
                ChapterDiagnosticCode.ColumnHeaderUnexpected,
                path, sheet.Name, row, ColumnKind,
                $"유형 '{raw}'을 모릅니다. 대사(빈칸도 같음) · IF · ELSEIF · ENDIF 중 하나여야 합니다."));

            return EpisodeRowKind.Dialogue;
        }
    }

    private static string Word(EpisodeRowKind kind) => kind switch
    {
        EpisodeRowKind.If => "IF",
        EpisodeRowKind.ElseIf => "ELSEIF",
        EpisodeRowKind.End => "ENDIF",
        _ => "대사"
    };

    // ── 조건 블록 (v10) ─────────────────────────────────────────────────────

    /// <summary>
    /// <c>IF</c>와 <c>END</c>가 짝을 이루는지 — <b>이것이 규칙의 전부다</b> (v10).
    ///
    /// 구판의 규칙 1·2·4·5·6(구간 대상 존재 · INPUT/OUT 짝 · 구간 재사용 금지 · 중첩 금지 ·
    /// OUT 대조)은 전부 사라졌다. 블록은 자기 범위를 제자리에서 말하므로 가리킬 대상도,
    /// 재사용할 구간도, 대조할 선언도 없다. <b>중첩은 이제 허용</b>이다 — 여는 순서의
    /// 역순으로 닫히는 것이 짝의 정의이고, 그건 셀 수 있다.
    /// </summary>
    private static void VerifyBlocks(
        string sheetName,
        string path,
        IReadOnlyList<EpisodeRow> rows,
        List<ChapterDiagnostic> diagnostics)
    {
        var open = new Stack<EpisodeRow>();

        foreach (EpisodeRow row in rows)
        {
            switch (row.Kind)
            {
                case EpisodeRowKind.If:
                    if (row.ConditionLabel is null)
                    {
                        diagnostics.Add(Cell(
                            ChapterDiagnosticSeverity.Error,
                            ChapterDiagnosticCode.ConditionLabelUndefined,
                            path, sheetName, row.SourceRow, ColumnConditionLabel,
                            "IF 행에 조건라벨이 없습니다. 챕터 `조건` 시트의 라벨을 골라 주세요 — " +
                            "조건 없는 IF는 무엇을 가르는지 말하지 않습니다."));
                    }

                    // 중첩은 허용이다 (2026-08-17) — 줄 하나가 전환 여럿을 담게 아래층을
                    // 고쳤다. 겹쳐 닫는 <<endif>>들이 같은 줄 앞에 몰려도 순서대로 재생된다.
                    open.Push(row);
                    break;

                case EpisodeRowKind.ElseIf:
                    if (open.Count == 0)
                    {
                        diagnostics.Add(Cell(
                            ChapterDiagnosticSeverity.Error,
                            ChapterDiagnosticCode.ColumnHeaderUnexpected,
                            path, sheetName, row.SourceRow, ColumnKind,
                            "열린 IF가 없는 ELSEIF입니다. 위쪽에 짝이 되는 IF 행이 있어야 합니다."));
                        break;
                    }

                    if (row.ConditionLabel is null)
                    {
                        diagnostics.Add(Cell(
                            ChapterDiagnosticSeverity.Error,
                            ChapterDiagnosticCode.ConditionLabelUndefined,
                            path, sheetName, row.SourceRow, ColumnConditionLabel,
                            "ELSEIF 행에 조건라벨이 없습니다. 챕터 `조건` 시트의 라벨을 골라 주세요."));
                    }

                    break;

                case EpisodeRowKind.End:
                    if (open.Count == 0)
                    {
                        diagnostics.Add(Cell(
                            ChapterDiagnosticSeverity.Error,
                            ChapterDiagnosticCode.ColumnHeaderUnexpected,
                            path, sheetName, row.SourceRow, ColumnKind,
                            "닫을 IF가 없는 ENDIF입니다. 위쪽에 짝이 되는 IF 행이 있어야 합니다."));
                        break;
                    }

                    open.Pop();
                    break;
            }
        }

        foreach (EpisodeRow unclosed in open)
        {
            diagnostics.Add(Cell(
                ChapterDiagnosticSeverity.Error,
                ChapterDiagnosticCode.ColumnHeaderUnexpected,
                path, sheetName, unclosed.SourceRow, ColumnKind,
                $"IF(인덱스 {unclosed.Index})가 ENDIF로 닫히지 않았습니다 — 표가 그대로 끝납니다. " +
                "블록은 반드시 닫아야 어디까지가 조건 안인지 정해집니다."));
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
