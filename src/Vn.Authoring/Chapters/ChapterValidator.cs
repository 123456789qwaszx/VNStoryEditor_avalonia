namespace Vn.Authoring.Chapters;

/// <param name="Reachability">도달성 증명 결과. 에피소드 워크북이 하나도 없어도 돈다(증감 0 가정).</param>
public sealed record ChapterValidationResult(
    IReadOnlyList<ChapterDiagnostic> Diagnostics,
    ChapterReachabilityResult Reachability)
{
    public bool HasErrors =>
        Diagnostics.Any(item => item.Severity == ChapterDiagnosticSeverity.Error) ||
        Reachability.HasErrors;

    public IEnumerable<ChapterDiagnostic> All =>
        Diagnostics.Concat(Reachability.Diagnostics);
}

/// <summary>
/// G7 — 챕터·에피소드를 한꺼번에 검증한다. 구조 검증(리더·평평화가 이미 낸 것 + 교차 검증)과
/// 도달성 증명을 묶어, G8 내보내기가 <b>이 결과 하나만 보고 거부를 결정</b>하게 한다(Gate C 3번).
/// </summary>
public static class ChapterValidator
{
    /// <param name="episodesFolder">에피소드 워크북 폴더. null이거나 없으면 워크북 검증은 건너뛴다.</param>
    public static ChapterValidationResult Validate(ChapterGraphModel chapter, string? episodesFolder)
    {
        ArgumentNullException.ThrowIfNull(chapter);

        var diagnostics = new List<ChapterDiagnostic>(chapter.Diagnostics);
        var deltas = new Dictionary<string, IReadOnlyDictionary<string, StatDeltaRange>>(StringComparer.Ordinal);

        string[] labels = chapter.Conditions.Select(condition => condition.Label).ToArray();
        string[] statKeys = chapter.Stats.Select(stat => stat.Key).ToArray();
        var conditionsByLabel = chapter.Conditions
            .ToDictionary(condition => condition.Label, condition => condition, StringComparer.Ordinal);

        foreach (ChapterEpisode episode in chapter.Episodes)
        {
            string? path = episodesFolder is null
                ? null
                : EpisodeLibrary.PathFor(episodesFolder, episode.EpisodeId);

            if (path is null || !File.Exists(path))
            {
                continue; // 아직 대본이 없는 에피소드는 정상이다 — 스탯을 바꾸지 않는 것으로 다룬다.
            }

            EpisodeWorkbookModel model;

            try
            {
                model = EpisodeWorkbookReader.Read(path, labels, statKeys);
            }
            catch (XlsxReadException exception)
            {
                diagnostics.Add(new ChapterDiagnostic(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.SheetMissing,
                    path, null, null, null,
                    $"에피소드 워크북을 읽지 못했습니다: {exception.Message}"));
                continue;
            }

            diagnostics.AddRange(model.Diagnostics);

            EpisodeFlattenResult flattened = EpisodeFlattener.Flatten(model, conditionsByLabel);
            diagnostics.AddRange(flattened.Diagnostics);

            VerifyOptionsMatchEdges(chapter, episode, model, diagnostics);

            deltas[episode.EpisodeId] = EpisodeStatRangeCalculator.Calculate(model, statKeys);
        }

        ChapterReachabilityResult reachability = ChapterReachabilityProver.Prove(chapter, deltas);

        return new ChapterValidationResult(diagnostics, reachability);
    }

    /// <summary>
    /// 챕터 `간선`의 선택지와 에피소드 `OPTION` 행의 <b>개수·순서 일치</b> (G7 구조 검증).
    ///
    /// 도착지는 간선 시트가, 라벨은 에피소드가 소유한다(단일 소유권) — 두 시트가 같은 선택지를
    /// 말하고 있는지는 순서로 맞춰 본다. 어긋난 채 내보내면 플레이어가 고른 버튼과 실제로
    /// 가는 곳이 달라진다.
    /// </summary>
    private static void VerifyOptionsMatchEdges(
        ChapterGraphModel chapter,
        ChapterEpisode episode,
        EpisodeWorkbookModel model,
        List<ChapterDiagnostic> diagnostics)
    {
        List<ChapterEdge> choiceEdges = chapter.Edges
            .Where(edge =>
                string.Equals(edge.FromEpisodeId, episode.EpisodeId, StringComparison.Ordinal) &&
                !edge.IsPlainAdvance)
            .ToList();

        List<EpisodeRow> options = model.Rows
            .Where(row => row.Kind == EpisodeRowKind.Option)
            .ToList();

        if (choiceEdges.Count == 0 && options.Count == 0)
        {
            return;
        }

        if (choiceEdges.Count != options.Count)
        {
            diagnostics.Add(new ChapterDiagnostic(
                ChapterDiagnosticSeverity.Error,
                ChapterDiagnosticCode.OptionEdgeMismatch,
                chapter.SourcePath,
                ChapterSheetNames.Edges,
                null,
                null,
                $"'{episode.EpisodeId}'의 선택지 개수가 어긋납니다 — 챕터 `간선`에 " +
                $"{choiceEdges.Count}개, 에피소드 워크북 OPTION에 {options.Count}개. " +
                "도착지는 간선 시트가, 라벨은 에피소드가 소유하므로 개수·순서가 같아야 합니다."));

            return;
        }

        for (int index = 0; index < options.Count; index++)
        {
            string? edgeLabel = choiceEdges[index].OptionLabel;
            string optionText = options[index].Text;

            if (string.IsNullOrEmpty(edgeLabel) ||
                string.Equals(edgeLabel, optionText, StringComparison.Ordinal))
            {
                continue;
            }

            diagnostics.Add(new ChapterDiagnostic(
                ChapterDiagnosticSeverity.Warning,
                ChapterDiagnosticCode.OptionEdgeMismatch,
                model.SourcePath,
                model.SheetName,
                options[index].SourceRow,
                "I",
                $"{index + 1}번째 선택지의 문구가 어긋납니다 — 간선 시트는 '{edgeLabel}', " +
                $"에피소드는 '{optionText}'. 순서가 밀렸다면 도착지가 뒤바뀝니다."));
        }
    }
}
