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

        string[] labels = chapter.Conditions.Select(condition => condition.Label).ToArray();
        var conditionsByLabel = chapter.Conditions
            .ToDictionary(condition => condition.Label, condition => condition, StringComparer.Ordinal);

        foreach (ChapterEpisode episode in chapter.Episodes)
        {
            string? path = episodesFolder is null
                ? null
                : EpisodeLibrary.FindExisting(episodesFolder, episode.EpisodeId);

            if (path is null)
            {
                continue; // 아직 대본이 없는 에피소드는 정상이다 — 스탯을 바꾸지 않는 것으로 다룬다.
            }

            EpisodeWorkbookModel model;

            try
            {
                model = EpisodeWorkbookReader.Read(path, labels);
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
        }

        // 스탯 증감의 원천은 간선이다 (2026-08-14) — 증명기가 챕터 모델에서 직접 읽는다.
        ChapterReachabilityResult reachability = ChapterReachabilityProver.Prove(chapter);

        return new ChapterValidationResult(diagnostics, reachability);
    }

    /// <summary>
    /// 챕터 `간선`의 선택지와 에피소드 `OPTION` 행을 <b>라벨로</b> 맞춰 본다 (G7 구조 검증).
    ///
    /// 도착지는 간선 시트가, 라벨은 에피소드가 소유한다(단일 소유권). 짝은 라벨 정확 일치다 —
    /// 순서·개수 일치를 요구하던 옛 규칙은 2단계의 포트 규칙("안 이은 옵션 = 에피소드 종료",
    /// 소유자 승인)과 충돌해, 옵션이 IN으로 안에서 해소되는 멀쩡한 시트에 오류를 냈다(실사례).
    ///
    /// - 간선 없는 옵션 = 합법이다. IN이 있으면 안에서 흐르고, 없으면 에피소드 종료다.
    /// - 어떤 옵션과도 라벨이 안 맞는 간선 = 오류다. 그 도착지는 영원히 안 쓰인다(유령 간선).
    /// - IN으로 해소되는 옵션에 간선까지 있으면 = 경고다. 안이 이기므로 간선이 죽는다.
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

        if (choiceEdges.Count == 0)
        {
            return; // 안 이은 옵션 = 에피소드 종료 — 검사할 짝이 없다.
        }

        List<EpisodeRow> options = model.Rows
            .Where(row => row.Kind == EpisodeRowKind.Option)
            .ToList();

        foreach (ChapterEdge edge in choiceEdges)
        {
            EpisodeRow? match = options.FirstOrDefault(option =>
                string.Equals(option.Text, edge.OptionLabel, StringComparison.Ordinal));

            if (match is null)
            {
                diagnostics.Add(new ChapterDiagnostic(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.OptionEdgeMismatch,
                    chapter.SourcePath,
                    ChapterSheetNames.Edges,
                    edge.SourceRow,
                    null,
                    $"간선의 선택지 '{edge.OptionLabel}'({edge.FromEpisodeId}→{edge.ToEpisodeId})가 " +
                    $"에피소드 '{episode.EpisodeId}'의 OPTION에 없습니다 — 이 도착지는 영원히 " +
                    "안 쓰입니다. 문구 오타이거나 옵션이 지워진 것입니다."));

                continue;
            }

            if (match.CallsSection)
            {
                diagnostics.Add(new ChapterDiagnostic(
                    ChapterDiagnosticSeverity.Warning,
                    ChapterDiagnosticCode.OptionEdgeMismatch,
                    model.SourcePath,
                    model.SheetName,
                    match.SourceRow,
                    "F",
                    $"선택지 '{match.Text}'는 IN={match.In}으로 에피소드 안에서 흐르는데, " +
                    $"챕터 간선({edge.FromEpisodeId}→{edge.ToEpisodeId})도 이 문구를 가리킵니다 — " +
                    "안이 이기므로 그 간선은 쓰이지 않습니다. 둘 중 하나를 지워 주세요."));
            }
        }
    }
}
