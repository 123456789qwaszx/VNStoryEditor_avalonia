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

        VerifyChoiceSlots(chapter, diagnostics);

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

            // 2026-08-16 소유자 — 선택지의 정본이 챕터 `선택지` 시트로 왔다. 대본의
            // CHOICE/OPTION은 폐지 수순이다: 아직 있으면 옮기라고 말한다(막지는 않는다).
            if (model.Rows.Any(row => row.Kind is EpisodeRowKind.Choice or EpisodeRowKind.Option))
            {
                diagnostics.Add(new ChapterDiagnostic(
                    ChapterDiagnosticSeverity.Warning,
                    ChapterDiagnosticCode.OptionEdgeMismatch,
                    path, model.SheetName, null, null,
                    $"'{episode.EpisodeId}' 대본에 CHOICE/OPTION이 있습니다 — 선택지는 이제 " +
                    "챕터 엑셀의 `선택지` 시트에서 만듭니다. 대본의 선택지는 옮긴 뒤 지워 주세요."));
            }
        }

        // 스탯 증감의 원천은 간선이다 (2026-08-14) — 증명기가 챕터 모델에서 직접 읽는다.
        ChapterReachabilityResult reachability = ChapterReachabilityProver.Prove(chapter);

        return new ChapterValidationResult(diagnostics, reachability);
    }

    /// <summary>
    /// 선택지 칸의 구조 검증 (2026-08-16 소유자 — 선택지 시트가 정본).
    ///
    /// - 칸의 주인 간선(출발→도착)이 없으면 경고 — 주인 없는 칸은 화면에 못 나간다.
    /// - 간선의 선택지수와 실제 칸 수가 다르면 경고 — 툴의 다시 읽기가 모자란 칸을 만들어 준다.
    /// - 빈 대본(보이지 않는 기본)은 에피소드당 하나뿐이다 — 둘째부터는 문구를 적으라고 말한다.
    /// - 기본 칸의 간선에 조건이 있으면 경고 — 기본은 "어떤 조건도 안 될 때의 방어장치"라
    ///   조건 없는 간선과 짝이어야 한다.
    /// </summary>
    private static void VerifyChoiceSlots(ChapterGraphModel chapter, List<ChapterDiagnostic> diagnostics)
    {
        foreach (ChapterChoiceOption slot in chapter.ChoiceOptions)
        {
            if (!chapter.Edges.Any(edge =>
                    string.Equals(edge.FromEpisodeId, slot.EpisodeId, StringComparison.Ordinal) &&
                    string.Equals(edge.ToEpisodeId, slot.ToEpisodeId, StringComparison.Ordinal)))
            {
                diagnostics.Add(new ChapterDiagnostic(
                    ChapterDiagnosticSeverity.Warning,
                    ChapterDiagnosticCode.OptionEdgeMismatch,
                    chapter.SourcePath, ChapterSheetNames.Choices, slot.SourceRow, null,
                    $"선택지 칸({slot.EpisodeId}→{slot.ToEpisodeId})의 주인 간선이 없습니다 — " +
                    "간선을 만들거나 이 칸을 지워 주세요."));
            }
        }

        foreach (ChapterEdge edge in chapter.Edges)
        {
            int slots = chapter.ChoiceOptions.Count(slot =>
                string.Equals(slot.EpisodeId, edge.FromEpisodeId, StringComparison.Ordinal) &&
                string.Equals(slot.ToEpisodeId, edge.ToEpisodeId, StringComparison.Ordinal));

            if (chapter.ChoiceOptions.Count > 0 && slots != edge.ChoiceCount)
            {
                diagnostics.Add(new ChapterDiagnostic(
                    ChapterDiagnosticSeverity.Warning,
                    ChapterDiagnosticCode.OptionEdgeMismatch,
                    chapter.SourcePath, ChapterSheetNames.Edges, edge.SourceRow, "D",
                    $"간선 {edge.FromEpisodeId}→{edge.ToEpisodeId}의 선택지수는 {edge.ChoiceCount}인데 " +
                    $"선택지 칸이 {slots}개입니다 — 툴의 [다시 읽기]가 모자란 칸을 만들어 줍니다."));
            }
        }

        foreach (IGrouping<string, ChapterChoiceOption> blanks in chapter.ChoiceOptions
                     .Where(slot => slot.IsInvisibleDefault)
                     .GroupBy(slot => slot.EpisodeId, StringComparer.Ordinal))
        {
            foreach (ChapterChoiceOption extra in blanks.Skip(1))
            {
                diagnostics.Add(new ChapterDiagnostic(
                    ChapterDiagnosticSeverity.Warning,
                    ChapterDiagnosticCode.OptionEdgeMismatch,
                    chapter.SourcePath, ChapterSheetNames.Choices, extra.SourceRow, "D",
                    $"'{blanks.Key}'의 빈 선택지 칸이 여럿입니다 — 빈 칸(보이지 않는 기본)은 " +
                    "에피소드당 하나뿐입니다. 대본 text를 적으면 보이는 선택지가 됩니다."));
            }

            ChapterChoiceOption first = blanks.First();
            ChapterEdge? owner = chapter.Edges.FirstOrDefault(edge =>
                string.Equals(edge.FromEpisodeId, first.EpisodeId, StringComparison.Ordinal) &&
                string.Equals(edge.ToEpisodeId, first.ToEpisodeId, StringComparison.Ordinal));

            if (owner is { ConditionLabel.Length: > 0 })
            {
                diagnostics.Add(new ChapterDiagnostic(
                    ChapterDiagnosticSeverity.Warning,
                    ChapterDiagnosticCode.OptionEdgeMismatch,
                    chapter.SourcePath, ChapterSheetNames.Edges, owner.SourceRow, "E",
                    $"보이지 않는 기본 칸의 간선({owner.FromEpisodeId}→{owner.ToEpisodeId})에 " +
                    $"조건 '{owner.ConditionLabel}'이 걸려 있습니다 — 기본은 어떤 조건도 안 될 때 " +
                    "빠지는 방어장치라 조건이 없어야 합니다."));
            }
        }
    }
}
