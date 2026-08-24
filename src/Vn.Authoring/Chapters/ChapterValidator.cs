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
        VerifyScenesFilled(chapter, project, diagnostics);

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
    /// 재생할 줄이 하나도 없는 노드를 <b>오류로</b> 짚는다 — 에피소드의 것이든, 간선에
    /// 매단 자유 씬이든 (2026-08-25).
    ///
    /// ⛔ <b>경고에서 오류로 올렸다.</b> 처음에는 "빈 씬도 들어갔다 곧장 나오면 그만"이라고
    /// 보고 경고로 두었는데, <b>Yarn 컴파일러가 줄 없는 노드를 YarnProject에 아예 넣지
    /// 않는다</b>(실측). 그러면 진행 JSON이 부르는 이름이 저쪽에 없어 유니티의 사전 대조가
    /// <b>재생을 통째로 막는다</b> — 빈 씬 하나가 챕터 전체를 못 돌게 한다.
    ///
    /// ⚠ 커맨드만 있고 <b>대사가 빈 줄</b>은 여기서 세지 않는다 — 그것이 순수 연출 씬의
    /// 정상 모양이고, 이미터가 보이지 않는 글자로 채워 내므로 노드가 살아남는다.
    /// 여기가 보는 것은 <b>줄 자체가 없는</b> 노드다.
    /// </summary>
    private static void VerifyScenesFilled(
        ChapterGraphModel chapter,
        StoryProject? project,
        List<ChapterDiagnostic> diagnostics)
    {
        if (project is null)
        {
            return;
        }

        ChapterBoard board = ChapterBoard.For(project);

        foreach (ChapterEpisode episode in chapter.Episodes)
        {
            // ① 에피소드 자신의 대사 노드. 없는 것은 `VerifyDialogueEntriesOnBoard`가 짚으므로
            //    여기서는 <b>있는데 비었을 때</b>만 본다 — 같은 말을 두 번 하지 않는다.
            if (board.EpisodeNodeFor(episode) is { } own && !HasPlayableLine(project, own))
            {
                diagnostics.Add(new ChapterDiagnostic(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.EpisodeSceneEmpty,
                    chapter.SourcePath,
                    ChapterSheetNames.Episodes,
                    episode.SourceRow,
                    "C",
                    $"'{episode.EpisodeId}'의 대사 노드 '{own.Name}'에 재생할 줄이 하나도 " +
                    "없습니다. 줄 없는 노드는 Yarn이 YarnProject에 넣지 않아서, 이대로 " +
                    "내보내면 게임이 그 노드를 찾지 못해 재생을 시작하지 못합니다 — " +
                    "카드를 더블클릭해 대본을 한 줄이라도 적어 주세요."));
            }

            // ② 그 에피소드에서 나가는 길에 매단 자유 씬.
            foreach (ChapterEdge edge in chapter.Edges.Where(item =>
                         string.Equals(item.FromEpisodeId, episode.EpisodeId, StringComparison.Ordinal)))
            {
                if (board.SceneFor(episode, edge) is not { } scene || HasPlayableLine(project, scene))
                {
                    continue;
                }

                diagnostics.Add(new ChapterDiagnostic(
                    ChapterDiagnosticSeverity.Error,
                    ChapterDiagnosticCode.ViaSceneEmpty,
                    chapter.SourcePath,
                    ChapterSheetNames.Edges,
                    edge.SourceRow,
                    "D",
                    $"'{episode.EpisodeId}'→'{edge.ToEpisodeId}' 길에 매단 연출 씬 " +
                    $"'{scene.Name}'에 재생할 줄이 하나도 없습니다. 줄 없는 노드는 " +
                    "YarnProject에 실리지 않아 게임이 그 씬을 찾지 못합니다 — " +
                    "연출 그래프에서 채우거나, 안 쓸 것이면 선을 떼 주세요."));
            }
        }
    }

    /// <summary>재생될 줄이 하나라도 있는가 — 본문이 비어도 줄은 줄이다(연출이 매달린다).</summary>
    private static bool HasPlayableLine(StoryProject project, DialogueNode scene) =>
        project.FindScript(scene.ScriptId) is { } document &&
        document.Lines.Count > 0 &&
        document.ActiveLines.Any();

    /// <summary>
    /// ⛔ <b>에피소드가 부를 대사 노드가 그 판에 실제로 있는가</b> (2026-08-23).
    ///
    /// 노드가 없으면 그 .yarn이 아예 안 나가고 진행 JSON만 <b>없는 노드를 부른다</b> —
    /// 로드·검증·도달성 증명은 전부 통과하고 재생만 안 된다. 유니티의 사전 대조가 잡는
    /// 그 모양이고, <b>여기서 잡으면 유니티까지 안 간다.</b>
    ///
    /// ⚠ <b>2026-08-25에 "안 연 챕터는 봐준다"를 걷었다.</b> 판이 없거나 비어 있으면 이
    /// 관문을 통째로 건너뛰었는데, 그 면제가 실제로 사고를 냈다 — 진행 JSON이 그대로 나가고
    /// 게임에서만 죽었다. 판과 노드는 <b>프로젝트 파일에 남으므로</b>(세션 상태가 아니다)
    /// 한 번 연 챕터는 계속 통과한다. 옛 주석이 걱정하던 "동기화는 한 챕터, 내보내기는 전
    /// 챕터"라는 비대칭은 그쪽을 향한 것이었다.
    /// </summary>
    private static void VerifyDialogueEntriesOnBoard(
        ChapterGraphModel chapter,
        StoryProject? project,
        List<ChapterDiagnostic> diagnostics)
    {
        if (project is null || chapter.Episodes.Count == 0)
        {
            return;
        }

        // ⚠ <b>"판이 없으면 건너뛴다"를 걷었다</b> (2026-08-25). 판이 없거나 비어 있으면
        //    그 챕터의 <b>모든</b> 에피소드가 부를 노드를 잃는데, 조용히 통과시키니
        //    내보내기가 나가고 유니티 사전 대조가 재생을 통째로 막았다(실사례).
        //    한 번도 안 연 챕터도 <b>내보내는 순간</b>에는 같은 사고다 — 안 열었다는 것이
        //    면제 사유가 될 수 없다. 대신 무엇을 하면 되는지를 말한다.
        ChapterBoard board = ChapterBoard.For(project);

        List<ChapterEpisode> missing = chapter.Episodes
            .Where(episode => board.EpisodeNodeFor(episode) is null)
            .ToList();

        if (missing.Count == chapter.Episodes.Count)
        {
            diagnostics.Add(new ChapterDiagnostic(
                ChapterDiagnosticSeverity.Error,
                ChapterDiagnosticCode.DialogueEntryNodeMissing,
                chapter.SourcePath,
                ChapterSheetNames.Episodes,
                null,
                null,
                $"'{chapter.ChapterId}' 판에 대사 노드가 하나도 없습니다 — 이대로 내보내면 " +
                "진행 JSON이 부르는 노드가 전부 없어 게임이 재생을 시작하지 못합니다. " +
                "챕터를 한 번 열어 대본을 동기화해 주세요."));

            return;   // 에피소드마다 같은 말을 반복하지 않는다.
        }

        foreach (ChapterEpisode episode in missing)
        {
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
