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
    /// <param name="episodesFolder">
    /// <b>그 챕터의</b> 대본 폴더 <c>episodes/{ChapterId}/</c> (2026-08-16 — 챕터별 격리).
    /// null이거나 없으면 워크북 검증은 건너뛴다.
    /// </param>
    public static ChapterValidationResult Validate(ChapterGraphModel chapter, string? episodesFolder)
    {
        ArgumentNullException.ThrowIfNull(chapter);

        var diagnostics = new List<ChapterDiagnostic>(chapter.Diagnostics);

        string[] labels = chapter.Conditions.Select(condition => condition.Label).ToArray();
        var conditionsByLabel = chapter.Conditions
            .ToDictionary(condition => condition.Label, condition => condition, StringComparer.Ordinal);

        VerifyPlainAdvances(chapter, diagnostics);

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

            // CHOICE/OPTION은 v10에서 규격에서 빠졌다 — 이제 리더가 그 행을 읽는 자리에서
            // "선택지 시트로 옮기라"고 직접 말한다(위의 model.Diagnostics에 실려 온다).
        }

        // 스탯 증감의 원천은 간선이다 (2026-08-14) — 증명기가 챕터 모델에서 직접 읽는다.
        ChapterReachabilityResult reachability = ChapterReachabilityProver.Prove(chapter);

        return new ChapterValidationResult(diagnostics, reachability);
    }

    /// <summary>
    /// 보이지 않는 기본의 구조 검증 (v9 — 칸이 사라졌으니 셀 것도 없다). 남은 규칙은 하나:
    /// <b>문구 없는 간선(자동 진행)은 에피소드당 하나뿐이고, 그 길에는 관문이 없어야 한다.</b>
    /// 어떤 선택지도 고를 수 없을 때 빠지는 방어장치라, 그것마저 조건이 걸리면 갇힌다.
    ///
    /// 선택지 사전(`선택지` 시트)은 검증하지 않는다 — 배선이 아니라 어휘집이라, 안 쓰는
    /// 낱말이 남아 있는 것은 잘못이 아니다.
    /// </summary>
    private static void VerifyPlainAdvances(ChapterGraphModel chapter, List<ChapterDiagnostic> diagnostics)
    {
        foreach (IGrouping<string, ChapterEdge> group in chapter.Edges
                     .Where(edge => edge.IsPlainAdvance)
                     .GroupBy(edge => edge.FromEpisodeId, StringComparer.Ordinal))
        {
            foreach (ChapterEdge extra in group.Skip(1))
            {
                diagnostics.Add(new ChapterDiagnostic(
                    ChapterDiagnosticSeverity.Warning,
                    ChapterDiagnosticCode.OptionEdgeMismatch,
                    chapter.SourcePath, ChapterSheetNames.Edges, extra.SourceRow, "D",
                    $"'{group.Key}'에 문구 없는 간선이 여럿입니다 — 보이지 않는 기본은 " +
                    "에피소드당 하나뿐입니다. 선택지 문구를 적으면 보이는 선택지가 됩니다."));
            }

            ChapterEdge fallback = group.First();

            if (fallback.HasGate)
            {
                diagnostics.Add(new ChapterDiagnostic(
                    ChapterDiagnosticSeverity.Warning,
                    ChapterDiagnosticCode.OptionEdgeMismatch,
                    chapter.SourcePath, ChapterSheetNames.Edges, fallback.SourceRow, "E",
                    $"보이지 않는 기본({fallback.FromEpisodeId}→{fallback.ToEpisodeId})에 관문이 " +
                    "걸려 있습니다 — 어떤 선택지도 안 될 때 빠지는 자리라 조건이 없어야 합니다."));
            }
        }
    }
}
