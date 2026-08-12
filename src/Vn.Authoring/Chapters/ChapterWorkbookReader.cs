using System.Globalization;
using ClosedXML.Excel;
using Vn.Authoring.Definition;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 챕터 워크북(§3.1의 5시트)을 <see cref="ChapterGraphModel"/>로 읽는다 (G1).
///
/// <b>읽기 전용이다.</b> 이 클래스는 워크북에 쓰지 않는다 — 위치도 조건도 엑셀이 소유하고(G-2),
/// 뷰의 드래그가 파일로 돌아가는 경로는 존재하지 않는다.
///
/// <b>엑셀 접근은 ClosedXML 하나로 한다.</b> 손수 만든 OOXML 리더는 제거했다 — xlsx를 읽는 길이
/// 둘이면 규약 사본이고, G5가 드롭다운·색·시트보호를 쓴 워크북을 <i>생성</i>하는 순간
/// 손수 쓰기는 손수 읽기보다 훨씬 위험해진다.
///
/// <b>열은 자리로 읽고 머리글로 검산한다.</b> 규격이 A~K 자리를 못박았으므로 자리로 읽되,
/// 머리글이 다르면 경고를 낸다. 머리글로만 읽으면 규격에 없는 배치를 조용히 받아들이게 되고,
/// 자리로만 읽으면 열이 하나 밀렸을 때 엉뚱한 값을 조용히 먹는다.
///
/// <b>머리글 행의 첫 빈 칸까지가 데이터 블록이다.</b> 빈 칸 하나를 띄우고 적은 안내문
/// (견본의 `⚠ 읽기전용 미러 …`)은 표 밖의 글이라 읽지 않는다. 반면 블록 <i>안</i>에 규격에
/// 없는 열이 붙어 있으면 그건 표의 일부로 보이므로 무엇을 지나쳤는지 진단으로 남긴다.
/// </summary>
public static class ChapterWorkbookReader
{
    private const int HeaderRow = 1;

    private static readonly string[] EpisodeHeaders =
        ["EpisodeId", "제목", "인덱스", "종류", "대사엔트리", "X", "Y", "표시조건", "해금조건", "엔딩키", "메모"];

    private static readonly string[] EdgeHeaders =
        ["출발", "도착", "선택지 라벨", "조건", "잠금시 숨김", "잠금 안내문"];

    private static readonly string[] ConditionHeaders = ["라벨", "조건식", "설명"];

    private static readonly string[] StatHeaders = ["스탯키", "표시명", "초기값", "최소", "최대"];

    /// <exception cref="XlsxReadException">파일을 열 수 없을 때. 데이터 오류가 아니라 접근 실패다.</exception>
    public static ChapterGraphModel Read(string path, GameDefinition? definition = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new XlsxReadException(path, $"워크북 파일이 없습니다: {path}");
        }

        using XLWorkbook workbook = OpenWorkbook(path);

        var diagnostics = new List<ChapterDiagnostic>();
        string chapterId = Path.GetFileNameWithoutExtension(path);

        foreach (IXLWorksheet sheet in workbook.Worksheets)
        {
            if (!ChapterSheetNames.All.Contains(sheet.Name))
            {
                diagnostics.Add(Diagnostic(
                    ChapterDiagnosticSeverity.Info,
                    ChapterDiagnosticCode.SheetIgnored,
                    path,
                    sheet.Name,
                    null,
                    null,
                    "규격(§3.1)에 없는 시트라 읽지 않았습니다."));
            }
        }

        // 스탯이 먼저다 — 조건식이 스탯키를 검사하고, 픽스처 열이 스탯키로 이름 붙는다.
        IReadOnlyList<ChapterStat> stats = ReadStats(workbook, path, definition, diagnostics);
        HashSet<string> statKeys = stats.Select(stat => stat.Key).ToHashSet(StringComparer.Ordinal);

        IReadOnlyList<ChapterCondition> conditions = ReadConditions(workbook, path, statKeys, diagnostics);
        HashSet<string> conditionLabels =
            conditions.Select(condition => condition.Label).ToHashSet(StringComparer.Ordinal);

        IReadOnlyList<ChapterEpisode> episodes = ReadEpisodes(workbook, path, conditionLabels, diagnostics);
        HashSet<string> episodeIds =
            episodes.Select(episode => episode.EpisodeId).ToHashSet(StringComparer.Ordinal);

        IReadOnlyList<ChapterEdge> edges = ReadEdges(workbook, path, episodeIds, conditionLabels, diagnostics);
        IReadOnlyList<ChapterFixture> fixtures = ReadFixtures(workbook, path, stats, episodeIds, diagnostics);

        VerifyClearedTargets(conditions, episodeIds, path, diagnostics);

        return new ChapterGraphModel(
            chapterId,
            path,
            episodes,
            edges,
            conditions,
            stats,
            fixtures,
            diagnostics);
    }

    /// <summary>
    /// 열기 실패는 종류를 가리지 않고 하나로 모은다. 호출자(<see cref="ChapterLibrary"/>)가
    /// 챕터 하나를 건너뛸지 정하려면 "이 파일을 못 열었다"만 알면 되고, ClosedXML이 내는
    /// 예외 종류를 저작 코드가 알아야 할 이유가 없다.
    ///
    /// <b>파일 핸들은 우리가 연다 — <c>new XLWorkbook(path)</c>를 쓰지 않는다.</b> 경로 생성자는
    /// xlsx가 아닌 파일에서 예외를 던지면서 <b>열어 둔 핸들을 놓지 않는다</b>(확인함: 그 뒤
    /// 삭제·덮어쓰기가 전부 막힌다). 챕터 폴더에 깨진 워크북이 하나 있으면 파일 감시가 저장마다
    /// 다시 읽으므로 핸들이 계속 쌓인다. 우리 <c>using</c> 안에서 열면 실패해도 반드시 풀린다.
    ///
    /// <c>FileShare.ReadWrite</c>인 이유: 기획자가 엑셀에서 워크북을 열어 둔 채로도 읽어야 한다.
    /// 그게 이 레이어의 일상적인 상태다.
    ///
    /// 스트림을 바로 닫아도 되는 이유: <see cref="XLWorkbook"/>은 생성자에서 통째로 읽어들인다
    /// (닫은 뒤에도 셀이 읽히는 것을 확인함). 지연 읽기가 아니다.
    /// </summary>
    private static XLWorkbook OpenWorkbook(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return new XLWorkbook(stream);
        }
        catch (Exception exception)
        {
            throw new XlsxReadException(path, $"워크북을 읽지 못했습니다: {exception.Message}", exception);
        }
    }

    // ── 에피소드 ────────────────────────────────────────────────────────────

    private static IReadOnlyList<ChapterEpisode> ReadEpisodes(
        XLWorkbook workbook,
        string path,
        IReadOnlyCollection<string> conditionLabels,
        List<ChapterDiagnostic> diagnostics)
    {
        IXLWorksheet? sheet = RequireSheet(workbook, ChapterSheetNames.Episodes, path, diagnostics);

        if (sheet is null)
        {
            return Array.Empty<ChapterEpisode>();
        }

        VerifyHeaders(sheet, EpisodeHeaders, path, diagnostics);

        var episodes = new List<ChapterEpisode>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (int row in DataRows(sheet))
        {
            string episodeId = Text(sheet, row, 1);

            if (episodeId.Length == 0)
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.EpisodeIdBlank,
                    path, sheet.Name, row, 1,
                    "EpisodeId가 비어 있습니다. 이 행은 읽지 않았습니다."));
                continue;
            }

            if (seen.TryGetValue(episodeId, out int firstRow))
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.EpisodeIdDuplicated,
                    path, sheet.Name, row, 1,
                    $"EpisodeId '{episodeId}'가 {firstRow}행과 중복입니다. 이 행은 읽지 않았습니다."));
                continue;
            }

            seen[episodeId] = row;

            string dialogueEntry = Text(sheet, row, 5);

            if (dialogueEntry.Length == 0)
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.DialogueEntryBlank,
                    path, sheet.Name, row, 5,
                    $"'{episodeId}'의 대사엔트리가 비어 있습니다. 런타임이 재생할 대상이 없습니다."));
            }

            double x = Number(sheet, row, 6, path, episodeId, diagnostics);
            double y = Number(sheet, row, 7, path, episodeId, diagnostics);

            string? visible = Optional(sheet, row, 8);
            string? unlock = Optional(sheet, row, 9);

            RequireConditionLabel(visible, conditionLabels, path, sheet.Name, row, 8, "표시조건", diagnostics);
            RequireConditionLabel(unlock, conditionLabels, path, sheet.Name, row, 9, "해금조건", diagnostics);

            episodes.Add(new ChapterEpisode(
                episodeId,
                Text(sheet, row, 2),
                Text(sheet, row, 3),
                Text(sheet, row, 4),
                dialogueEntry,
                x,
                y,
                visible,
                unlock,
                Optional(sheet, row, 10),
                Optional(sheet, row, 11),
                row));
        }

        return episodes;
    }

    // ── 간선 ────────────────────────────────────────────────────────────────

    private static IReadOnlyList<ChapterEdge> ReadEdges(
        XLWorkbook workbook,
        string path,
        IReadOnlyCollection<string> episodeIds,
        IReadOnlyCollection<string> conditionLabels,
        List<ChapterDiagnostic> diagnostics)
    {
        IXLWorksheet? sheet = RequireSheet(workbook, ChapterSheetNames.Edges, path, diagnostics);

        if (sheet is null)
        {
            return Array.Empty<ChapterEdge>();
        }

        VerifyHeaders(sheet, EdgeHeaders, path, diagnostics);

        var edges = new List<ChapterEdge>();

        foreach (int row in DataRows(sheet))
        {
            string from = Text(sheet, row, 1);
            string to = Text(sheet, row, 2);

            if (from.Length == 0 || to.Length == 0)
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.EdgeEndpointBlank,
                    path, sheet.Name, row, from.Length == 0 ? 1 : 2,
                    "간선의 출발·도착은 둘 다 있어야 합니다. 이 행은 읽지 않았습니다."));
                continue;
            }

            RequireEpisode(from, episodeIds, path, sheet.Name, row, 1, "출발", diagnostics);
            RequireEpisode(to, episodeIds, path, sheet.Name, row, 2, "도착", diagnostics);

            string? conditionLabel = Optional(sheet, row, 4);
            RequireConditionLabel(conditionLabel, conditionLabels, path, sheet.Name, row, 4, "조건", diagnostics);

            edges.Add(new ChapterEdge(
                from,
                to,
                Optional(sheet, row, 3),
                conditionLabel,
                Boolean(sheet, row, 5, path, diagnostics),
                Optional(sheet, row, 6),
                row));
        }

        return edges;
    }

    // ── 조건 ────────────────────────────────────────────────────────────────

    private static IReadOnlyList<ChapterCondition> ReadConditions(
        XLWorkbook workbook,
        string path,
        IReadOnlyCollection<string> statKeys,
        List<ChapterDiagnostic> diagnostics)
    {
        IXLWorksheet? sheet = RequireSheet(workbook, ChapterSheetNames.Conditions, path, diagnostics);

        if (sheet is null)
        {
            return Array.Empty<ChapterCondition>();
        }

        VerifyHeaders(sheet, ConditionHeaders, path, diagnostics);

        var conditions = new List<ChapterCondition>();

        foreach (int row in DataRows(sheet))
        {
            string label = Text(sheet, row, 1);

            if (label.Length == 0)
            {
                continue;
            }

            string expression = Text(sheet, row, 2);

            if (expression.Length == 0)
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.ConditionExpressionBlank,
                    path, sheet.Name, row, 2,
                    $"조건 '{label}'의 조건식이 비어 있습니다."));

                conditions.Add(new ChapterCondition(label, expression, Optional(sheet, row, 3),
                    Array.Empty<ConditionTerm>(), IsValid: false, row));
                continue;
            }

            ConditionParseResult parsed = ConditionExpressionParser.Parse(expression, statKeys);

            foreach (ConditionParseProblem problem in parsed.Problems)
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    MapConditionProblem(problem.Kind),
                    path, sheet.Name, row, 2,
                    $"조건 '{label}': {problem.Message}"));
            }

            conditions.Add(new ChapterCondition(
                label,
                expression,
                Optional(sheet, row, 3),
                parsed.Terms,
                parsed.IsValid,
                row));
        }

        return conditions;
    }

    private static ChapterDiagnosticCode MapConditionProblem(ConditionProblemKind kind) => kind switch
    {
        ConditionProblemKind.UnknownStatKey => ChapterDiagnosticCode.StatKeyUnknown,
        ConditionProblemKind.ValueNotInteger => ChapterDiagnosticCode.StatValueNotInteger,
        ConditionProblemKind.Empty => ChapterDiagnosticCode.ConditionExpressionBlank,
        _ => ChapterDiagnosticCode.ConditionExpressionMalformed
    };

    // ── 스탯 ────────────────────────────────────────────────────────────────

    private static IReadOnlyList<ChapterStat> ReadStats(
        XLWorkbook workbook,
        string path,
        GameDefinition? definition,
        List<ChapterDiagnostic> diagnostics)
    {
        IXLWorksheet? sheet = RequireSheet(workbook, ChapterSheetNames.Stats, path, diagnostics);

        if (sheet is null)
        {
            return Array.Empty<ChapterStat>();
        }

        VerifyHeaders(sheet, StatHeaders, path, diagnostics);

        var stats = new List<ChapterStat>();

        foreach (int row in DataRows(sheet))
        {
            string key = Text(sheet, row, 1);

            if (key.Length == 0)
            {
                continue;
            }

            int initial = Integer(sheet, row, 3, path, key, "초기값", diagnostics);
            int minimum = Integer(sheet, row, 4, path, key, "최소", diagnostics);
            int maximum = Integer(sheet, row, 5, path, key, "최대", diagnostics);

            if (minimum > maximum)
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.StatRangeInvalid,
                    path, sheet.Name, row, 4,
                    $"스탯 '{key}'의 최소({minimum})가 최대({maximum})보다 큽니다. " +
                    "이 범위는 도달성 증명(G7)의 탐색 경계라 비어 있으면 안 됩니다."));
            }
            else if (initial < minimum || initial > maximum)
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.StatRangeInvalid,
                    path, sheet.Name, row, 3,
                    $"스탯 '{key}'의 초기값({initial})이 최소~최대({minimum}~{maximum}) 밖입니다."));
            }

            if (definition is not null &&
                !definition.Variables.Any(variable =>
                    string.Equals(variable.Name, key, StringComparison.Ordinal)))
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Warning,
                    ChapterDiagnosticCode.StatMissingFromGameDefinition,
                    path, sheet.Name, row, 1,
                    $"스탯 '{key}'가 game.definition.json에 없습니다. " +
                    "이 시트는 읽기전용 미러이고 원천은 정의 파일입니다(§3.1)."));
            }

            stats.Add(new ChapterStat(key, Text(sheet, row, 2), initial, minimum, maximum, row));
        }

        if (stats.Count is < 2 or > 5)
        {
            diagnostics.Add(Diagnostic(
                ChapterDiagnosticSeverity.Warning,
                ChapterDiagnosticCode.StatCountOutOfRange,
                path, sheet.Name, null, null,
                $"Tier 2 스탯이 {stats.Count}개입니다. 규격은 2~5개를 전제합니다(§0) — " +
                "많아질수록 도달성 증명(G7)의 상태공간이 급격히 커집니다."));
        }

        return stats;
    }

    // ── 픽스처 ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<ChapterFixture> ReadFixtures(
        XLWorkbook workbook,
        string path,
        IReadOnlyList<ChapterStat> stats,
        IReadOnlyCollection<string> episodeIds,
        List<ChapterDiagnostic> diagnostics)
    {
        IXLWorksheet? sheet = FindSheet(workbook, ChapterSheetNames.Fixtures);

        if (sheet is null)
        {
            // 픽스처는 테스트 데이터다 — 없어도 챕터는 성립한다.
            diagnostics.Add(Diagnostic(
                ChapterDiagnosticSeverity.Info,
                ChapterDiagnosticCode.SheetMissing,
                path, ChapterSheetNames.Fixtures, null, null,
                "픽스처 시트가 없습니다. 재생루트 하이라이트(G6)를 쓰지 않는 챕터로 봅니다."));

            return Array.Empty<ChapterFixture>();
        }

        // 스탯 열은 이름이 고정이 아니라 `스탯` 시트가 정한다 — 머리글로 찾는다.
        int width = DataWidth(sheet);
        var statColumns = new Dictionary<int, string>();
        int choiceColumn = 0;

        for (int column = 3; column <= width; column++)
        {
            string header = Text(sheet, HeaderRow, column);

            if (header.StartsWith("고정 선택", StringComparison.Ordinal))
            {
                choiceColumn = column;
                continue;
            }

            if (stats.Any(stat => string.Equals(stat.Key, header, StringComparison.Ordinal)))
            {
                statColumns[column] = header;
                continue;
            }

            if (header.Length > 0)
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Warning,
                    ChapterDiagnosticCode.FixtureStatColumnUnknown,
                    path, sheet.Name, HeaderRow, column,
                    $"'{header}'는 `스탯` 시트에 없는 스탯키입니다. 이 열은 읽지 않았습니다."));
            }
        }

        var fixtures = new List<ChapterFixture>();

        foreach (int row in DataRows(sheet))
        {
            string name = Text(sheet, row, 1);

            if (name.Length == 0)
            {
                continue;
            }

            var values = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach ((int column, string key) in statColumns)
            {
                values[key] = Integer(sheet, row, column, path, name, key, diagnostics);
            }

            var choices = new List<ChapterFixtureChoice>();

            if (choiceColumn > 0)
            {
                foreach (ChapterFixtureChoice choice in
                         ParseChoices(Text(sheet, row, choiceColumn), path, sheet.Name, row, choiceColumn, diagnostics))
                {
                    RequireEpisode(choice.From, episodeIds, path, sheet.Name, row, choiceColumn, "고정 선택 출발", diagnostics);
                    RequireEpisode(choice.To, episodeIds, path, sheet.Name, row, choiceColumn, "고정 선택 도착", diagnostics);
                    choices.Add(choice);
                }
            }

            fixtures.Add(new ChapterFixture(
                name,
                Boolean(sheet, row, 2, path, diagnostics),
                values,
                choices,
                row));
        }

        return fixtures;
    }

    /// <summary>"main05.02→main05.03; a→b" 형태. 화살표는 →와 -&gt; 둘 다 받는다.</summary>
    private static IEnumerable<ChapterFixtureChoice> ParseChoices(
        string text,
        string path,
        string sheetName,
        int row,
        int column,
        List<ChapterDiagnostic> diagnostics)
    {
        if (text.Length == 0)
        {
            yield break;
        }

        foreach (string entry in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = entry.Trim();
            string[] sides = trimmed.Split(['→'], 2);

            if (sides.Length != 2)
            {
                sides = trimmed.Split("->", 2, StringSplitOptions.None);
            }

            if (sides.Length != 2 || sides[0].Trim().Length == 0 || sides[1].Trim().Length == 0)
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Warning,
                    ChapterDiagnosticCode.FixtureChoiceMalformed,
                    path, sheetName, row, column,
                    $"고정 선택 '{trimmed}'을 '출발→도착'으로 읽지 못했습니다. 이 항목은 읽지 않았습니다."));
                continue;
            }

            yield return new ChapterFixtureChoice(sides[0].Trim(), sides[1].Trim());
        }
    }

    // ── 공통 ────────────────────────────────────────────────────────────────

    private static void VerifyClearedTargets(
        IReadOnlyList<ChapterCondition> conditions,
        IReadOnlyCollection<string> episodeIds,
        string path,
        List<ChapterDiagnostic> diagnostics)
    {
        foreach (ChapterCondition condition in conditions)
        {
            foreach (ConditionTerm term in condition.Parsed
                         .Where(item => item.Kind == ConditionTermKind.EpisodeCleared))
            {
                if (episodeIds.Contains(term.Key))
                {
                    continue;
                }

                // 다른 챕터의 에피소드를 가리킬 수 있으므로 오류가 아니라 경고다.
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Warning,
                    ChapterDiagnosticCode.EdgeEndpointUnknown,
                    path, ChapterSheetNames.Conditions, condition.SourceRow, 2,
                    $"조건 '{condition.Label}'의 cleared:{term.Key}가 이 챕터에 없는 에피소드입니다. " +
                    "다른 챕터의 것이면 정상입니다."));
            }
        }
    }

    private static IXLWorksheet? FindSheet(XLWorkbook workbook, string sheetName) =>
        workbook.Worksheets.FirstOrDefault(sheet =>
            string.Equals(sheet.Name, sheetName, StringComparison.Ordinal));

    private static IXLWorksheet? RequireSheet(
        XLWorkbook workbook,
        string sheetName,
        string path,
        List<ChapterDiagnostic> diagnostics)
    {
        IXLWorksheet? sheet = FindSheet(workbook, sheetName);

        if (sheet is null)
        {
            diagnostics.Add(Diagnostic(
                ChapterDiagnosticSeverity.Error,
                ChapterDiagnosticCode.SheetMissing,
                path, sheetName, null, null,
                $"'{sheetName}' 시트가 없습니다. 규격 §3.1의 5시트가 모두 있어야 합니다."));
        }

        return sheet;
    }

    /// <summary>머리글 행의 첫 빈 칸 앞까지가 데이터 블록이다.</summary>
    private static int DataWidth(IXLWorksheet sheet)
    {
        int width = 0;
        int last = sheet.Row(HeaderRow).LastCellUsed()?.Address.ColumnNumber ?? 0;

        for (int column = 1; column <= last; column++)
        {
            if (Text(sheet, HeaderRow, column).Length == 0)
            {
                break;
            }

            width = column;
        }

        return width;
    }

    /// <summary>
    /// 수식은 있는데 계산된 값이 없는 셀을 알린다.
    ///
    /// 엑셀은 저장할 때 수식의 결과를 함께 담지만, 프로그램으로 만든 워크북은 수식만 적고
    /// 결과를 비워 두는 일이 있다. 그런 셀은 파일 안에서 <b>빈 칸</b>이라 그냥 두면
    /// "조건식이 비어 있습니다" 같은 엉뚱한 보고가 나가고, 기획자는 자기 화면에 글자가
    /// 보이는데 왜 비었다는지 알 수 없다. 무엇이 문제인지 그대로 말한다(규칙 14).
    /// </summary>
    private static void ReportUncachedFormulas(
        IXLWorksheet sheet,
        string path,
        List<ChapterDiagnostic> diagnostics)
    {
        foreach (IXLCell cell in sheet.CellsUsed(XLCellsUsedOptions.All, cell => cell.HasFormula))
        {
            if (CellText(cell).Length > 0)
            {
                continue;
            }

            diagnostics.Add(Cell(
                ChapterDiagnosticSeverity.Error,
                ChapterDiagnosticCode.FormulaWithoutCachedValue,
                path, sheet.Name, cell.Address.RowNumber, cell.Address.ColumnNumber,
                "수식은 있는데 계산된 값이 저장돼 있지 않습니다. " +
                "이 워크북을 엑셀에서 한 번 열어 저장하면 값이 함께 담깁니다."));
        }
    }

    private static void VerifyHeaders(
        IXLWorksheet sheet,
        IReadOnlyList<string> expected,
        string path,
        List<ChapterDiagnostic> diagnostics)
    {
        ReportUncachedFormulas(sheet, path, diagnostics);

        for (int index = 0; index < expected.Count; index++)
        {
            int column = index + 1;
            string actual = Text(sheet, HeaderRow, column);

            if (string.Equals(actual, expected[index], StringComparison.Ordinal))
            {
                continue;
            }

            diagnostics.Add(Cell(
                ChapterDiagnosticSeverity.Warning,
                ChapterDiagnosticCode.ColumnHeaderUnexpected,
                path, sheet.Name, HeaderRow, column,
                $"머리글이 '{expected[index]}'가 아니라 '{(actual.Length == 0 ? "(빈칸)" : actual)}'입니다. " +
                "값은 규격의 자리대로 읽었으므로, 열이 밀렸다면 아래 값들이 어긋납니다."));
        }

        int width = DataWidth(sheet);

        if (width > expected.Count)
        {
            var ignored = new List<string>();

            for (int column = expected.Count + 1; column <= width; column++)
            {
                ignored.Add($"{ColumnName(column)}열('{Text(sheet, HeaderRow, column)}')");
            }

            diagnostics.Add(Diagnostic(
                ChapterDiagnosticSeverity.Info,
                ChapterDiagnosticCode.ColumnHeaderUnexpected,
                path, sheet.Name, HeaderRow, null,
                $"규격 밖의 열을 읽지 않았습니다: {string.Join(", ", ignored)}"));
        }
    }

    /// <summary>
    /// 머리글 아래에서 <b>글자가 있는</b> 행. 서식만 남고 값이 없는 행은 데이터가 아니다 —
    /// 엑셀에서 지운 행이 "빈 행 하나"로 남아 EpisodeId 누락 오류를 뿜으면 거짓 경보가 된다.
    /// </summary>
    private static IEnumerable<int> DataRows(IXLWorksheet sheet) =>
        sheet.RowsUsed()
            .Select(row => row.RowNumber())
            .Where(row => row > HeaderRow && RowHasText(sheet, row))
            .OrderBy(row => row);

    private static bool RowHasText(IXLWorksheet sheet, int row)
    {
        IXLRangeRow used = sheet.Row(row).RowUsed();
        return used.Cells().Any(cell => CellText(cell).Length > 0);
    }

    /// <summary>
    /// 셀 하나의 표시 문자열. 숫자는 <b>고정 문화권</b>으로 찍는다 — 사용자 문화권에 맡기면
    /// 소수점이 쉼표가 되는 곳에서 좌표·스탯이 조용히 다르게 읽힌다.
    /// </summary>
    private static string CellText(IXLCell cell) => cell.DataType switch
    {
        XLDataType.Blank => string.Empty,
        XLDataType.Number => cell.GetDouble().ToString(CultureInfo.InvariantCulture),
        XLDataType.Boolean => cell.GetBoolean() ? "TRUE" : "FALSE",
        _ => cell.GetString().Trim()
    };

    private static string Text(IXLWorksheet sheet, int row, int column) =>
        CellText(sheet.Cell(row, column));

    private static string? Optional(IXLWorksheet sheet, int row, int column)
    {
        string value = Text(sheet, row, column);
        return value.Length == 0 ? null : value;
    }

    private static double Number(
        IXLWorksheet sheet,
        int row,
        int column,
        string path,
        string owner,
        List<ChapterDiagnostic> diagnostics)
    {
        string raw = Text(sheet, row, column);

        if (raw.Length == 0)
        {
            return 0;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return value;
        }

        diagnostics.Add(Cell(
            ChapterDiagnosticSeverity.Error,
            ChapterDiagnosticCode.PositionNotNumeric,
            path, sheet.Name, row, column,
            $"'{owner}'의 위치 값 '{raw}'이 숫자가 아닙니다. 0으로 두었습니다."));

        return 0;
    }

    private static int Integer(
        IXLWorksheet sheet,
        int row,
        int column,
        string path,
        string owner,
        string field,
        List<ChapterDiagnostic> diagnostics)
    {
        string raw = Text(sheet, row, column);

        if (raw.Length == 0)
        {
            return 0;
        }

        if (int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
        {
            return value;
        }

        bool looksDecimal =
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

        diagnostics.Add(Cell(
            ChapterDiagnosticSeverity.Error,
            ChapterDiagnosticCode.StatValueNotInteger,
            path, sheet.Name, row, column,
            looksDecimal
                ? $"'{owner}'의 {field} '{raw}'은 정수가 아닙니다. 스탯은 정수 고정입니다(G-3) — " +
                  "소수는 경계값 비교를 어긋나게 하고 도달성 증명을 불가능하게 만듭니다."
                : $"'{owner}'의 {field} '{raw}'을 정수로 읽지 못했습니다."));

        return 0;
    }

    private static bool Boolean(
        IXLWorksheet sheet,
        int row,
        int column,
        string path,
        List<ChapterDiagnostic> diagnostics)
    {
        string raw = Text(sheet, row, column);

        if (raw.Length == 0)
        {
            return false;
        }

        if (string.Equals(raw, "TRUE", StringComparison.OrdinalIgnoreCase) || raw == "1")
        {
            return true;
        }

        if (string.Equals(raw, "FALSE", StringComparison.OrdinalIgnoreCase) || raw == "0")
        {
            return false;
        }

        diagnostics.Add(Cell(
            ChapterDiagnosticSeverity.Warning,
            ChapterDiagnosticCode.BooleanNotRecognized,
            path, sheet.Name, row, column,
            $"'{raw}'을 TRUE/FALSE로 읽지 못했습니다. FALSE로 두었습니다."));

        return false;
    }

    private static void RequireConditionLabel(
        string? label,
        IReadOnlyCollection<string> known,
        string path,
        string sheetName,
        int row,
        int column,
        string field,
        List<ChapterDiagnostic> diagnostics)
    {
        if (string.IsNullOrEmpty(label) || known.Contains(label))
        {
            return;
        }

        diagnostics.Add(Cell(
            ChapterDiagnosticSeverity.Error,
            ChapterDiagnosticCode.ConditionLabelUndefined,
            path, sheetName, row, column,
            $"{field} '{label}'이 `조건` 시트에 없습니다. 라벨↔식의 원천은 `조건` 시트입니다(G-7)."));
    }

    private static void RequireEpisode(
        string episodeId,
        IReadOnlyCollection<string> known,
        string path,
        string sheetName,
        int row,
        int column,
        string field,
        List<ChapterDiagnostic> diagnostics)
    {
        if (known.Contains(episodeId))
        {
            return;
        }

        diagnostics.Add(Cell(
            ChapterDiagnosticSeverity.Error,
            ChapterDiagnosticCode.EdgeEndpointUnknown,
            path, sheetName, row, column,
            $"{field} '{episodeId}'가 `에피소드` 시트에 없습니다."));
    }

    /// <summary>1 → "A", 28 → "AB". 열 이름 규약은 ClosedXML의 것 하나를 쓴다.</summary>
    private static string ColumnName(int column) =>
        column > 0 ? XLHelper.GetColumnLetterFromNumber(column) : "?";

    private static ChapterDiagnostic Cell(
        ChapterDiagnosticSeverity severity,
        ChapterDiagnosticCode code,
        string path,
        string sheetName,
        int row,
        int column,
        string message) =>
        new(severity, code, path, sheetName, row, ColumnName(column), message);

    private static ChapterDiagnostic Diagnostic(
        ChapterDiagnosticSeverity severity,
        ChapterDiagnosticCode code,
        string path,
        string? sheetName,
        int? row,
        string? column,
        string message) =>
        new(severity, code, path, sheetName, row, column, message);
}
