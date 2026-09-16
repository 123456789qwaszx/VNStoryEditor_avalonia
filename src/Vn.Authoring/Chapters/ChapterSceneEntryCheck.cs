using ClosedXML.Excel;

namespace Vn.Authoring.Chapters;

/// <summary>
/// <b>장면은 한 자리에서만 시작한다</b> (V2 · 2026-09-16, <c>docs/plans/R6.md</c> §9).
///
/// 밖에서 들어오는 자리가 장면의 <b>루트</b>이고, 롤백이 되돌아갈 곳과 이어하기가 재개할
/// 곳이 거기다. 둘이면 어느 쪽으로 되돌아갈지가 정해지지 않는다.
///
/// ⛔ <b>새 불변식이 아니다.</b> 코어의 <c>ChapterInvariants.VerifySceneEntries</c>와 같은
/// 규칙이다 — 챕터 시작도 밖에서 들어오는 길로 세고, 장면 안에서 움직이는 간선은 안 센다.
/// 여기서 하는 일은 그것을 <b>내보내기 관문보다 먼저, 셀을 짚어</b> 말해 주는 것뿐이다
/// (V1과 같은 이유: 관문에서야 알면 어디를 고칠지 되짚어야 한다).
///
/// ⚠ <b>판정은 <see cref="IsEntry"/> 하나다.</b> <see cref="ChapterGraphModel.IsSceneRoot"/>와
/// <see cref="ChapterSceneGrouping"/>(화면의 ⌂·⚠ 표식)이 같은 것을 부른다 — 화면이 짚는 자리와
/// 진단이 짚는 자리가 갈리면 사람은 어느 쪽을 믿을지 모른다.
/// </summary>
public static class ChapterSceneEntryCheck
{
    /// <summary>
    /// 밖에서 들어오는 자리인가 — <b>챕터 시작</b>이거나 <b>다른 장면에서 오는 간선의 도착</b>.
    /// </summary>
    /// <remarks>
    /// 챕터 시작은 <c>episodes[0]</c>이다(규격에 시작 열이 없어 읽는 순서가 곧 정의다 —
    /// <see cref="ChapterGraphModel.StartEpisode"/>와 같은 규칙이어야 한다).
    /// </remarks>
    public static bool IsEntry(
        IReadOnlyList<ChapterEpisode> episodes,
        IReadOnlyList<ChapterEdge> edges,
        ChapterEpisode episode)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(episode);

        if (episodes.Count > 0 && ReferenceEquals(episodes[0], episode))
        {
            return true;
        }

        return edges.Any(edge =>
            string.Equals(edge.ToEpisodeId, episode.EpisodeId, StringComparison.Ordinal) &&
            episodes.FirstOrDefault(candidate =>
                string.Equals(candidate.EpisodeId, edge.FromEpisodeId, StringComparison.Ordinal))
                is { } source &&
            !string.Equals(source.EffectiveSceneId, episode.EffectiveSceneId, StringComparison.Ordinal));
    }

    /// <summary>
    /// 들어오는 자리가 둘 이상인 장면을 짚는다. 정상이면 빈 목록이다.
    ///
    /// 짚는 자리는 <b>두 번째 자리를 만든 간선</b>의 `도착` 칸이다 — 첫 자리가 아니라 그것이
    /// 고칠 곳이고, 코어도 같은 자리를 짚는다.
    /// </summary>
    public static IReadOnlyList<ChapterDiagnostic> Of(
        IReadOnlyList<ChapterEpisode> episodes,
        IReadOnlyList<ChapterEdge> edges,
        string path)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentNullException.ThrowIfNull(edges);

        // 장면 → 들어오는 자리들. 같은 자리로 여러 간선이 들어오는 것은 정상이라 자리만 센다.
        var landings = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        // 장면 → 두 번째 자리를 만든 간선. 없으면(시작점뿐이면) 짚을 행이 없다.
        var offending = new Dictionary<string, ChapterEdge>(StringComparer.Ordinal);

        void Land(ChapterEpisode episode, ChapterEdge? by)
        {
            if (!landings.TryGetValue(episode.EffectiveSceneId, out List<string>? into))
            {
                into = [];
                landings[episode.EffectiveSceneId] = into;
            }

            if (into.Contains(episode.EpisodeId, StringComparer.Ordinal))
            {
                return;
            }

            into.Add(episode.EpisodeId);

            if (into.Count == 2 && by is not null)
            {
                offending[episode.EffectiveSceneId] = by;
            }
        }

        // 챕터 시작도 밖에서 들어오는 길이다 — 챕터가 그 장면을 여는 자리.
        if (episodes.Count > 0)
        {
            Land(episodes[0], by: null);
        }

        foreach (ChapterEdge edge in edges)
        {
            ChapterEpisode? from = episodes.FirstOrDefault(item =>
                string.Equals(item.EpisodeId, edge.FromEpisodeId, StringComparison.Ordinal));
            ChapterEpisode? to = episodes.FirstOrDefault(item =>
                string.Equals(item.EpisodeId, edge.ToEpisodeId, StringComparison.Ordinal));

            // 도착이 실재하지 않는 간선은 다른 진단이 이미 잡았다.
            if (from is null || to is null ||
                string.Equals(from.EffectiveSceneId, to.EffectiveSceneId, StringComparison.Ordinal))
            {
                continue;
            }

            Land(to, edge);
        }

        var diagnostics = new List<ChapterDiagnostic>();

        foreach ((string sceneId, List<string> entries) in landings)
        {
            if (entries.Count <= 1)
            {
                continue;
            }

            offending.TryGetValue(sceneId, out ChapterEdge? edge);

            diagnostics.Add(new ChapterDiagnostic(
                ChapterDiagnosticSeverity.Error,
                ChapterDiagnosticCode.SceneHasManyEntries,
                path,
                ChapterSheetNames.Edges,
                edge is { SourceRow: > 0 } ? edge.SourceRow : null,
                edge is null ? null : XLHelper.GetColumnLetterFromNumber(2),
                $"장면 '{sceneId}'에 밖에서 들어오는 자리가 {entries.Count}개입니다: " +
                $"{string.Join(", ", entries)}. 장면은 한 자리에서만 시작해야 합니다 — " +
                "롤백이 되돌아갈 곳과 이어하기가 재개할 곳이 그 자리입니다. " +
                "나머지 착지점은 다른 장면으로 나누세요."));
        }

        return diagnostics;
    }
}
