using Vn.Authoring.Model;

namespace Vn.Authoring.Chapters;

/// <summary>
/// <b>진행이 착지하는 카드와 옆으로만 불려 가는 곁가지를 가른다</b> — <b>한 벌이다</b>
/// (R7 P-6 · 결정 ⑤ · 2026-09-17).
///
/// ⛔ 전에는 이 구분을 <c>MarkedEpisodeId is null</c>이 대신했다 — *에피소드면 척추,
/// 아니면 자유 씬*. 그 갈래가 <b>엑셀노드/자유노드의 구분 그 자체</b>였고, 없어졌다:
/// 챕터 판의 대사 노드는 전부 에피소드다.
///
/// 남는 진짜 갈래는 <b>진행이 그 카드에 착지하는가</b>다.
///
/// <list type="bullet">
/// <item><b>척추</b> — 챕터 간선이 들어오거나 챕터의 시작인 에피소드. 여기로 점프하면
/// 표시/해금·cleared 기록을 지나치고 처음부터 다시 재생한다.</item>
/// <item><b>곁가지</b> — 들어오는 간선이 하나도 없는 카드. 대본의
/// <c>&lt;&lt;detour&gt;&gt;</c>가 옆으로 불러 재생하고 돌려보낸다(「분기 추가」가 세우는
/// 것이 바로 이것이다). 진행을 지나칠 것이 없다.</item>
/// </list>
///
/// ⚠ 같은 규칙이 <b>가드레일</b>(엑셀노드로 향하는 출구)과 <b>철도</b>(곁가지 웹 걷기)에
/// 각각 한 벌씩 살고 있었다. 갈리면 한쪽은 경고하고 한쪽은 안 그리는 상태가 된다.
/// </summary>
public static class ChapterSpine
{
    /// <summary>
    /// 진행이 착지하는 에피소드Id들 — 간선의 도착과 챕터의 시작.
    ///
    /// ⚠ <c>ChapterDocument</c>와 <c>ChapterGraphModel</c> 둘 다에서 부르므로 조각을 받는다.
    /// </summary>
    public static HashSet<string> LandedOn(
        IReadOnlyList<ChapterEpisode> episodes, IReadOnlyList<ChapterEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentNullException.ThrowIfNull(edges);

        var landed = new HashSet<string>(
            edges.Select(edge => edge.ToEpisodeId), StringComparer.Ordinal);

        // ⚠ 시작 에피소드는 들어오는 간선이 없지만 진행이 <b>거기서 시작한다</b> —
        //    도달성 증명·내보내기와 같은 규칙(`ChapterGraphModel.StartEpisode`)이다.
        if (episodes.Count > 0)
        {
            landed.Add(episodes[0].EpisodeId);
        }

        return landed;
    }

    /// <summary>
    /// 이 카드가 <b>곁가지</b>인가 — 진행이 착지하지 않는 카드.
    /// 표식이 아예 없으면(챕터 판이 아닌 낙서판) 곁가지다.
    /// </summary>
    public static bool IsSideBranch(IReadOnlySet<string> landedOn, DialogueNode node)
    {
        ArgumentNullException.ThrowIfNull(landedOn);
        ArgumentNullException.ThrowIfNull(node);

        return node.MarkedEpisodeId is not { Length: > 0 } episodeId || !landedOn.Contains(episodeId);
    }
}
