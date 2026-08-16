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

    // 2026-08-16 소유자 개정 — 인덱스 열 폐지(안 쓰임), 간선의 스탯변화가 C열로,
    // `선택지 라벨`은 `선택지`로, 조건식은 스탯·연산자·값 세 칸으로, 스탯에 타입(선택 꼬리).
    // 구판 파일은 ChapterWorkbookMigrator가 이 모양으로 이행한다.
    // v8 (2026-08-16 소유자) — 표시·해금조건이 에피소드에서 간선으로 옮겨 왔다:
    // "보일지 말지는 이제 간선이 정한다".
    private static readonly string[] EpisodeHeaders =
        ["EpisodeId", "제목", "종류", "대사엔트리", "X", "Y", "엔딩키", "메모", "선택지수"];

    private static readonly string[] EdgeHeaders =
        ["출발", "도착", "스탯변화", "선택지", "표시조건", "해금조건", "잠금시 숨김", "잠금 안내문"];

    private static readonly string[] ConditionHeaders = ["라벨", "스탯", "연산자", "값", "설명"];

    private static readonly string[] StatHeaders = ["스탯키", "표시명", "초기값", "최소", "최대", "타입"];

    private static readonly string[] SpeakerHeaders = ["이름", "캐릭터키", "메모"];

    private static readonly string[] ChoiceHeaders = ["출발", "인덱스", "대본", "메모"];

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

        // 선택지가 간선보다 먼저다 — 간선의 대표 문구(OptionLabel)가 선택지 칸에서 파생된다.
        IReadOnlyList<ChapterChoiceOption> choiceOptions =
            ReadChoiceOptions(workbook, path, episodeIds, diagnostics);
        IReadOnlyList<ChapterEdge> edges =
            ReadEdges(workbook, path, episodeIds, conditionLabels, statKeys, choiceOptions, diagnostics);
        IReadOnlyList<ChapterFixture> fixtures = ReadFixtures(workbook, path, stats, episodeIds, diagnostics);
        IReadOnlyList<ChapterSpeaker> speakers =
            ReadSpeakers(workbook, path, diagnostics, out bool hasSpeakerSheet);

        VerifyClearedTargets(conditions, episodeIds, path, diagnostics);
        VerifyBoolStatUsage(stats, conditions, edges, path, diagnostics);

        return new ChapterGraphModel(
            chapterId,
            path,
            episodes,
            edges,
            conditions,
            stats,
            fixtures,
            diagnostics,
            speakers,
            hasSpeakerSheet,
            choiceOptions);
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

        VerifyHeaders(sheet, EpisodeHeaders, path, diagnostics, optionalTrailing: 1); // 선택지수(K)는 v7 추가

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

            string dialogueEntry = Text(sheet, row, 4);

            if (dialogueEntry.Length == 0)
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.DialogueEntryBlank,
                    path, sheet.Name, row, 4,
                    $"'{episodeId}'의 대사엔트리가 비어 있습니다. 런타임이 재생할 대상이 없습니다."));
            }

            double x = Number(sheet, row, 5, path, episodeId, diagnostics);
            double y = Number(sheet, row, 6, path, episodeId, diagnostics);

            // 선택지수 (I열, v8에서 자리 이동 — 선택) — 이 에피소드의 Option 칸 수. 비면 1이다.
            string choiceCountText =
                string.Equals(Text(sheet, HeaderRow, 9), "선택지수", StringComparison.Ordinal)
                    ? Text(sheet, row, 9)
                    : string.Empty;
            int choiceCount = 1;

            if (choiceCountText.Length > 0 &&
                (!int.TryParse(choiceCountText, NumberStyles.Integer, CultureInfo.InvariantCulture,
                     out choiceCount) || choiceCount < 1))
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Warning,
                    ChapterDiagnosticCode.StatValueNotInteger,
                    path, sheet.Name, row, 9,
                    $"선택지수 '{choiceCountText}'을 1 이상의 정수로 읽지 못해 1로 봅니다."));
                choiceCount = 1;
            }

            // `도달불가 허용`(D3)은 선택 열이다 — 머리글이 그 이름인 자리를 찾아 읽는다.
            int allowColumn = 0;

            for (int column = 9; column <= 12; column++)
            {
                if (string.Equals(Text(sheet, HeaderRow, column), "도달불가 허용", StringComparison.Ordinal))
                {
                    allowColumn = column;
                    break;
                }
            }

            bool allowUnreachable = allowColumn > 0 && Boolean(sheet, row, allowColumn, path, diagnostics);

            episodes.Add(new ChapterEpisode(
                episodeId,
                Text(sheet, row, 2),
                string.Empty, // 인덱스 열 폐지 (2026-08-16) — 내보내기 IndexText만 빈 값으로 남는다
                Text(sheet, row, 3),
                dialogueEntry,
                x,
                y,
                Optional(sheet, row, 7),
                Optional(sheet, row, 8),
                row,
                allowUnreachable)
            {
                ChoiceCount = choiceCount
            });
        }

        return episodes;
    }

    // ── 간선 ────────────────────────────────────────────────────────────────

    private static IReadOnlyList<ChapterEdge> ReadEdges(
        XLWorkbook workbook,
        string path,
        IReadOnlyCollection<string> episodeIds,
        IReadOnlyCollection<string> conditionLabels,
        IReadOnlyCollection<string> statKeys,
        IReadOnlyList<ChapterChoiceOption> choiceOptions,
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

            // 관문 둘 (v8) — 표시조건(E): 목록에 보이려면, 해금조건(F): 고를 수 있으려면.
            string? visibleLabel = Optional(sheet, row, 5);
            string? conditionLabel = Optional(sheet, row, 6);
            RequireConditionLabel(visibleLabel, conditionLabels, path, sheet.Name, row, 5, "표시조건", diagnostics);
            RequireConditionLabel(conditionLabel, conditionLabels, path, sheet.Name, row, 6, "해금조건", diagnostics);

            // 스탯변화 (C열 — 2026-08-16 소유자 개정으로 앞당김) — 이 간선을 타는 순간 1회
            // 커밋. 스탯이 변하는 유일한 자리라, 미등록 키·정수 아님은 여기서 바로 오류다.
            StatDeltaParseResult deltas = StatDeltaParser.Parse(
                Text(sheet, row, 3), statKeys);

            foreach (ConditionParseProblem problem in deltas.Problems)
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    problem.Kind == ConditionProblemKind.UnknownStatKey
                        ? ChapterDiagnosticCode.StatKeyUnknown
                        : ChapterDiagnosticCode.StatValueNotInteger,
                    path, sheet.Name, row, 3,
                    problem.Message));
            }

            // 선택지 (D열, v7) — 짝 칸의 인덱스. 간선 하나에 칸 하나가 1:1이다.
            string? choiceIndex = Optional(sheet, row, 4);

            // 문구는 짝 칸(출발, 인덱스)의 대본 text에서 파생된다. 칸이 없거나 빈 text면
            // null = 보이지 않는 기본(자동 진행).
            string? derivedLabel = choiceIndex is null
                ? null
                : choiceOptions
                    .FirstOrDefault(option =>
                        string.Equals(option.EpisodeId, from, StringComparison.Ordinal) &&
                        string.Equals(option.Index, choiceIndex, StringComparison.Ordinal) &&
                        !option.IsInvisibleDefault)
                    ?.Text;

            edges.Add(new ChapterEdge(
                from,
                to,
                derivedLabel,
                conditionLabel,
                Boolean(sheet, row, 7, path, diagnostics),
                Optional(sheet, row, 8),
                row)
            {
                StatChanges = deltas.Deltas,
                ChoiceIndex = choiceIndex,
                VisibleConditionLabel = visibleLabel
            });
        }

        // 간선 신원 = (출발, 선택지 인덱스) — 같은 칸을 두 간선이 가리키면 어디로 가는지 모호하다.
        foreach (IGrouping<(string, string), ChapterEdge> duplicated in edges
                     .Where(edge => edge.ChoiceIndex is not null)
                     .GroupBy(edge => (edge.FromEpisodeId, edge.ChoiceIndex!))
                     .Where(group => group.Count() > 1))
        {
            diagnostics.Add(Cell(
                ChapterDiagnosticSeverity.Error,
                ChapterDiagnosticCode.OptionEdgeMismatch,
                path, sheet.Name, duplicated.Last().SourceRow, 4,
                $"'{duplicated.Key.Item1}'의 선택지 {duplicated.Key.Item2}를 간선 여러 행이 " +
                "가리킵니다 — 간선 하나에 선택지 칸 하나입니다."));
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

            // 2026-08-16 소유자 개정 — 조건은 스탯(드롭다운)·연산자(드롭다운)·값 세 칸으로
            // 조립한다. 연산자·값이 비어 있으면 스탯 칸 전체를 원문 조건식으로 읽는다
            // (탈출구 — cleared:, 복합식 `a; b`, 구판 이행분이 이 길로 산다).
            string statCell = Text(sheet, row, 2);
            string operatorCell = Text(sheet, row, 3);
            string valueCell = Text(sheet, row, 4);

            string expression;

            if (statCell.Length == 0)
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.ConditionExpressionBlank,
                    path, sheet.Name, row, 2,
                    $"조건 '{label}'의 스탯 칸이 비어 있습니다. 스탯을 고르거나 조건식 원문을 적어 주세요."));

                conditions.Add(new ChapterCondition(label, string.Empty, Optional(sheet, row, 5),
                    Array.Empty<ConditionTerm>(), IsValid: false, row));
                continue;
            }

            if (operatorCell.Length == 0 && valueCell.Length == 0)
            {
                expression = statCell; // 원문 그대로
            }
            else if (string.Equals(operatorCell, "true", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(operatorCell, "false", StringComparison.OrdinalIgnoreCase))
            {
                // bool — 연산자 칸이 곧 값이다. 값 칸은 쓰지 않는다(엑셀에서 회색).
                if (valueCell.Length > 0)
                {
                    diagnostics.Add(Cell(
                        ChapterDiagnosticSeverity.Warning,
                        ChapterDiagnosticCode.ColumnHeaderUnexpected,
                        path, sheet.Name, row, 4,
                        $"조건 '{label}'은 true/false 조건이라 값 칸을 쓰지 않습니다 — 무시했습니다."));
                }

                expression = $"{statCell} == {operatorCell.ToLowerInvariant()}";
            }
            else
            {
                expression = $"{statCell} {operatorCell} {valueCell}";
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
                Optional(sheet, row, 5),
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

        VerifyHeaders(sheet, StatHeaders, path, diagnostics, optionalTrailing: 1); // 타입(F)은 나중 규격

        var stats = new List<ChapterStat>();

        foreach (int row in DataRows(sheet))
        {
            string key = Text(sheet, row, 1);

            if (key.Length == 0)
            {
                continue;
            }

            // 타입 (F열, 선택 — 2026-08-16 소유자). 비면 int. bool은 값 공간이 0/1 하나라
            // 최소·최대 칸을 읽지 않고 경계를 0·1로 고정한다 — 프루버가 그대로 옳게 돈다.
            string typeText = Text(sheet, row, 6);
            ChapterStatType type = ChapterStatType.Int;

            if (string.Equals(typeText, "bool", StringComparison.OrdinalIgnoreCase))
            {
                type = ChapterStatType.Bool;
            }
            else if (typeText.Length > 0 &&
                     !string.Equals(typeText, "int", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Warning,
                    ChapterDiagnosticCode.ColumnHeaderUnexpected,
                    path, sheet.Name, row, 6,
                    $"스탯 '{key}'의 타입 '{typeText}'을 모릅니다 — int로 봅니다. 쓸 수 있는 것: int · bool."));
            }

            if (type == ChapterStatType.Bool)
            {
                string rawInitial = Text(sheet, row, 3);
                int boolInitial = string.Equals(rawInitial, "TRUE", StringComparison.OrdinalIgnoreCase) ||
                                  rawInitial == "1"
                    ? 1
                    : 0;

                stats.Add(new ChapterStat(key, Text(sheet, row, 2), boolInitial, 0, 1, row,
                    ChapterStatType.Bool));

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
                "많아질수록 도달성 증명(G7)의 상태공간이 급격히 커집니다. " +
                "스탯의 원천은 game.definition.json의 변수이고, `스탯` 시트는 그 거울입니다."));
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
            // 픽스처는 테스트 데이터다 — 없어도 챕터는 성립하고, 2026-08-16부터는 새 챕터에
            // 시트를 만들지도 않는다(소유자 임시 제거). 진단도 내지 않는다 — 없는 것이 기본이다.
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

    // ── 화자 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// `화자` 시트(2026-08-16 추가). 이 기능 전에 만든 워크북에는 없다 — 진단 없이 빈
    /// 목록으로 받는다(앱이 챕터 선택 때 시트를 만들어 준다). 픽스처처럼 없어도 챕터는
    /// 성립하고, 있으면 에피소드 워크북 화자 열의 드롭다운 재료가 된다.
    /// </summary>
    private static IReadOnlyList<ChapterSpeaker> ReadSpeakers(
        XLWorkbook workbook,
        string path,
        List<ChapterDiagnostic> diagnostics,
        out bool hasSheet)
    {
        IXLWorksheet? sheet = FindSheet(workbook, ChapterSheetNames.Speakers);
        hasSheet = sheet is not null;

        if (sheet is null)
        {
            return Array.Empty<ChapterSpeaker>();
        }

        VerifyHeaders(sheet, SpeakerHeaders, path, diagnostics);

        var speakers = new List<ChapterSpeaker>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (int row in DataRows(sheet))
        {
            string name = Text(sheet, row, 1);

            if (name.Length == 0)
            {
                continue;
            }

            if (!seen.Add(name))
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Warning,
                    ChapterDiagnosticCode.ColumnHeaderUnexpected,
                    path, sheet.Name, row, 1,
                    $"화자 '{name}'이 두 번 등록되어 있습니다. 첫 행만 씁니다."));
                continue;
            }

            speakers.Add(new ChapterSpeaker(
                name,
                Optional(sheet, row, 2),
                Optional(sheet, row, 3),
                row));
        }

        return speakers;
    }

    /// <summary>
    /// bool 스탯(2026-08-16)의 어휘 검사 — 값 공간이 0/1 하나이므로 조건은 <c>== true/false</c>
    /// 뿐이고, 간선 스탯변화의 증감(<c>+1</c>)은 의미가 없다. 조용히 이상한 값이 되기 전에
    /// 자리를 짚어 말한다(규칙 14).
    /// </summary>
    private static void VerifyBoolStatUsage(
        IReadOnlyList<ChapterStat> stats,
        IReadOnlyList<ChapterCondition> conditions,
        IReadOnlyList<ChapterEdge> edges,
        string path,
        List<ChapterDiagnostic> diagnostics)
    {
        HashSet<string> boolKeys = stats
            .Where(stat => stat.Type == ChapterStatType.Bool)
            .Select(stat => stat.Key)
            .ToHashSet(StringComparer.Ordinal);

        if (boolKeys.Count == 0)
        {
            return;
        }

        foreach (ChapterCondition condition in conditions)
        {
            foreach (ConditionTerm term in condition.Parsed.Where(term =>
                         term.Kind == ConditionTermKind.StatComparison &&
                         boolKeys.Contains(term.Key) &&
                         (term.Comparison != ConditionComparison.Exactly ||
                          term.Value is not (0 or 1))))
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.ConditionExpressionMalformed,
                    path, ChapterSheetNames.Conditions, condition.SourceRow, 3,
                    $"조건 '{condition.Label}': '{term.Key}'는 bool 스탯입니다 — " +
                    "연산자 칸에서 true 또는 false만 고를 수 있습니다."));
            }
        }

        foreach (ChapterEdge edge in edges)
        {
            foreach (StatDelta delta in edge.StatChanges.Where(delta => boolKeys.Contains(delta.Key)))
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.StatValueNotInteger,
                    path, ChapterSheetNames.Edges, edge.SourceRow, 3,
                    $"'{delta.Key}'는 bool 스탯이라 증감(스탯변화)을 쓸 수 없습니다."));
            }
        }
    }

    // ── 선택지 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// `선택지` 시트 (2026-08-16 — 선택지의 정본이 대본에서 챕터로). 행 = 옵션 칸 하나이고
    /// 간선(출발→도착)이 칸을 소유한다. 대본 text는 자유 수정이며 빈 text = 보이지 않는 기본.
    /// 이 기능 전에 만든 워크북에는 시트가 없다 — 진단 없이 빈 목록(Migrator가 만들어 준다).
    /// 반환은 에피소드별 인덱스 순 — 가지 순서 = 읽는 순서.
    /// </summary>
    private static IReadOnlyList<ChapterChoiceOption> ReadChoiceOptions(
        XLWorkbook workbook,
        string path,
        IReadOnlyCollection<string> episodeIds,
        List<ChapterDiagnostic> diagnostics)
    {
        IXLWorksheet? sheet = FindSheet(workbook, ChapterSheetNames.Choices);

        if (sheet is null)
        {
            return Array.Empty<ChapterChoiceOption>();
        }

        VerifyHeaders(sheet, ChoiceHeaders, path, diagnostics);

        var options = new List<ChapterChoiceOption>();
        var seen = new HashSet<(string, string)>();

        foreach (int row in DataRows(sheet))
        {
            string from = Text(sheet, row, 1);
            string index = Text(sheet, row, 2);

            if (from.Length == 0 && index.Length == 0)
            {
                continue;
            }

            if (from.Length == 0 || index.Length == 0)
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Warning,
                    ChapterDiagnosticCode.OptionEdgeMismatch,
                    path, sheet.Name, row, from.Length == 0 ? 1 : 2,
                    "선택지 칸은 출발(근원 에피소드)과 인덱스가 둘 다 있어야 합니다 — " +
                    "인덱스가 간선과 짝하는 신원입니다. 이 행은 읽지 않았습니다."));
                continue;
            }

            if (!episodeIds.Contains(from))
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Warning,
                    ChapterDiagnosticCode.EdgeEndpointUnknown,
                    path, sheet.Name, row, 1,
                    $"선택지 칸의 출발 '{from}'이 에피소드 시트에 없습니다."));
            }

            if (!seen.Add((from, index)))
            {
                diagnostics.Add(Cell(
                    ChapterDiagnosticSeverity.Warning,
                    ChapterDiagnosticCode.OptionEdgeMismatch,
                    path, sheet.Name, row, 2,
                    $"'{from}'의 인덱스 {index} 칸이 두 번 있습니다 — 인덱스는 칸의 신원이라 " +
                    "겹치면 간선이 어느 칸과 짝인지 모호합니다. 첫 행만 씁니다."));
                continue;
            }

            options.Add(new ChapterChoiceOption(
                from, index, Text(sheet, row, 3), Optional(sheet, row, 4), row));
        }

        // 에피소드 안의 순서는 인덱스가 정한다 (10·20·30 방식 — 대본 엑셀과 같은 문법).
        // 숫자가 아니면 시트 순서 그대로 뒤에 선다.
        return options
            .OrderBy(option => option.EpisodeId, StringComparer.Ordinal)
            .ThenBy(option =>
                int.TryParse(option.Index, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                    ? index
                    : int.MaxValue)
            .ThenBy(option => option.SourceRow)
            .ToList();
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

    /// <param name="optionalTrailing">
    /// 끝에서부터 이 개수의 머리글은 없어도 된다(빈칸 허용) — 규격이 늘어나기 전의 파일을
    /// 소음 없이 받기 위해서다. 간선의 `스탯변화`(2026-08-14 추가)가 그 경우다.
    /// </param>
    private static void VerifyHeaders(
        IXLWorksheet sheet,
        IReadOnlyList<string> expected,
        string path,
        List<ChapterDiagnostic> diagnostics,
        int optionalTrailing = 0)
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

            if (actual.Length == 0 && index >= expected.Count - optionalTrailing)
            {
                continue; // 옛 규격 파일 — 나중에 추가된 꼬리 열이 아직 없다. 정상이다.
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
                string extra = Text(sheet, HeaderRow, column);

                // `도달불가 허용`(D3)은 규격이 아는 선택 열이다 — 지나친 것이 아니다.
                if (string.Equals(extra, "도달불가 허용", StringComparison.Ordinal))
                {
                    continue;
                }

                ignored.Add($"{ColumnName(column)}열('{extra}')");
            }

            if (ignored.Count == 0)
            {
                return;
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
