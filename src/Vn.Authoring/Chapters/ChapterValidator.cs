using Vn.Authoring.Model;

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
    /// <param name="project">
    /// 있으면 <b>`대사엔트리`가 실재하는 대사노드를 가리키는지</b>까지 본다
    /// (2026-08-23 · <see cref="VerifyDialogueEntriesOnBoard"/>). null이면 그 검사를
    /// 건너뛴다 — 판을 볼 수 없는 자리(콘솔·테스트)에서도 나머지 검증은 돌아야 한다.
    /// </param>
    public static ChapterValidationResult Validate(
        ChapterGraphModel chapter,
        string? episodesFolder,
        StoryProject? project = null)
    {
        ArgumentNullException.ThrowIfNull(chapter);

        var diagnostics = new List<ChapterDiagnostic>(chapter.Diagnostics);

        VerifyDialogueEntriesOnBoard(chapter, project, diagnostics);
        VerifyViaScenesFilled(chapter, project, diagnostics);

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

            // CHOICE/OPTION은 v10에서 규격에서 빠졌다 — 이제 리더가 그 행을 읽는 자리에서
            // "선택지 시트로 옮기라"고 직접 말한다(위의 model.Diagnostics에 실려 온다).
        }

        // 스탯 증감의 원천은 간선이다 (2026-08-14) — 증명기가 챕터 모델에서 직접 읽는다.
        ChapterReachabilityResult reachability = ChapterReachabilityProver.Prove(chapter);

        return new ChapterValidationResult(diagnostics, reachability);
    }

    /// <summary>
    /// ⛔ <b>`대사엔트리`가 가리키는 대사노드가 그 챕터의 판에 실제로 있는가</b> (2026-08-23).
    ///
    /// 내보내기의 <c>DialogueEntryId</c>는 이 글자를 이미터 규칙에 통과시킨 것이고
    /// (정규화 — 2026-08-24까지는 <c>Story_</c> 접두도 붙었다), yarn 타이틀도 <b>같은 노드
    /// 이름</b>에서 나온다. 그래서
    /// 노드가 없으면 파일이 아예 안 나가고, 진행 JSON만 <b>없는 노드를 부른다</b> —
    /// 로드·검증·도달성 증명은 전부 통과하고 재생만 안 된다. 2026-08-23에 유니티의
    /// 사전 대조가 잡은 그 모양이다. <b>여기서 잡으면 유니티까지 안 간다.</b>
    ///
    /// ⚠ <b>안 연 챕터를 거부하지 않는다.</b> 에피소드 동기화는 <b>고른 챕터 하나만</b>
    /// 돌고(<see cref="EpisodeSyncRunner"/>) 내보내기는 <b>전 챕터</b>를 돈다
    /// (<see cref="ChapterExportService.ExportAll"/>). 그 비대칭 때문에, 판이 없거나
    /// 판에 대사노드가 하나도 없는 챕터는 <b>아직 동기화 전</b>이지 잘못된 것이 아니다 —
    /// 그때 거부하면 오늘 잘 나가던 챕터가 전부 막힌다.
    ///
    /// 판에 노드가 <b>하나라도</b> 있으면 그 챕터는 한 번은 동기화됐다는 뜻이고,
    /// 그때부터 빠진 이름은 진짜 빠진 것이다.
    /// </summary>
    /// <summary>
    /// 간선에 매달린 자유 씬이 <b>비어 있는지</b> 알린다 (2026-08-25).
    ///
    /// 판에서 선을 잇는 것과 씬을 채우는 것은 다른 손놀림이라, 이어만 두고 아직 안 채운
    /// 상태가 정상적으로 존재한다. 그 자체는 잘못이 아니지만 <b>어느 길이 남았는지 한 자리에서
    /// 보이지 않으면</b> 열 개 중 하나가 빈 채로 출시된다.
    ///
    /// <b>오류가 아니라 경고다</b> — 빈 씬도 재생은 된다(들어갔다 곧장 나온다). 막는 것이
    /// 아니라 세어 보여 주는 자리다.
    ///
    /// ⚠ 커맨드만 있고 대사가 빈 줄은 <b>여기서 세지 않는다</b> — 그것이 순수 연출 씬의
    /// 정상 모양이다. 여기가 보는 것은 <b>재생할 줄이 하나도 없는</b> 씬이고, 빈 줄 하나하나는
    /// 이미터가 따로 짚는다.
    /// </summary>
    private static void VerifyViaScenesFilled(
        ChapterGraphModel chapter,
        StoryProject? project,
        List<ChapterDiagnostic> diagnostics)
    {
        if (project is null)
        {
            return;
        }

        ViaScenes via = ViaScenes.For(project);

        foreach (ChapterEpisode episode in chapter.Episodes)
        {
            foreach (ChapterEdge edge in chapter.Edges.Where(item =>
                         string.Equals(item.FromEpisodeId, episode.EpisodeId, StringComparison.Ordinal)))
            {
                if (via.SceneFor(episode, edge) is not { } scene || HasPlayableLine(project, scene))
                {
                    continue;
                }

                diagnostics.Add(new ChapterDiagnostic(
                    ChapterDiagnosticSeverity.Warning,
                    ChapterDiagnosticCode.ViaSceneEmpty,
                    chapter.SourcePath,
                    ChapterSheetNames.Edges,
                    edge.SourceRow,
                    "D",
                    $"'{episode.EpisodeId}'→'{edge.ToEpisodeId}' 길에 매단 연출 씬 " +
                    $"'{scene.Name}'이 비어 있습니다 — 재생할 줄이 하나도 없습니다. " +
                    "연출 그래프에서 채우거나, 안 쓸 것이면 선을 떼 주세요."));
            }
        }
    }

    /// <summary>재생될 줄이 하나라도 있는가 — 본문이 비어도 줄은 줄이다(연출이 매달린다).</summary>
    private static bool HasPlayableLine(StoryProject project, DialogueNode scene) =>
        project.FindScript(scene.ScriptId) is { } document &&
        document.Lines.Count > 0 &&
        document.ActiveLines.Any();

    private static void VerifyDialogueEntriesOnBoard(
        ChapterGraphModel chapter,
        StoryProject? project,
        List<ChapterDiagnostic> diagnostics)
    {
        if (project is null)
        {
            return;
        }

        StoryFile? board = project.Files.FirstOrDefault(file =>
            string.Equals(file.Name, chapter.ChapterId, StringComparison.Ordinal));

        if (board is null)
        {
            return;   // 한 번도 안 연 챕터 — 판 자체가 없다.
        }

        var names = board.Nodes
            .OfType<DialogueNode>()
            .Select(node => node.Name)
            .ToHashSet(StringComparer.Ordinal);

        if (names.Count == 0)
        {
            return;   // 판은 섰지만 아직 동기화 전이다.
        }

        foreach (ChapterEpisode episode in chapter.Episodes)
        {
            if (string.IsNullOrWhiteSpace(episode.DialogueEntry) ||
                names.Contains(episode.DialogueEntry))
            {
                continue;
            }

            diagnostics.Add(new ChapterDiagnostic(
                ChapterDiagnosticSeverity.Error,
                ChapterDiagnosticCode.DialogueEntryNodeMissing,
                chapter.SourcePath,
                ChapterSheetNames.Episodes,
                episode.SourceRow,
                "C",   // v13 (2026-08-25) — `종류`가 걷히며 대사엔트리가 D → C로 당겨졌다.
                $"'{episode.EpisodeId}'의 대사엔트리 '{episode.DialogueEntry}'에 해당하는 " +
                $"대사노드가 '{chapter.ChapterId}' 판에 없습니다. 이대로 내보내면 진행 JSON이 " +
                "존재하지 않는 대사 노드를 부르고, 로드는 통과하는데 재생만 안 됩니다. " +
                "챕터 그래프에서 이 챕터를 한 번 고르면 동기화가 노드를 만듭니다."));
        }
    }

}
