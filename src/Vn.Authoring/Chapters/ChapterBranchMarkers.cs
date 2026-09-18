using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 한 챕터에 뚫린 <b>분기 표식</b>을 모으는 유일한 자리 (2026-09-18).
///
/// 표식은 <see cref="DialogueLineExtension.DetourTargetNodeId"/> — <b>대사 줄</b>에 산다.
/// 연출 그래프가 [＋ 분기]로 뚫고, 챕터 그래프는 그것을 <b>읽어서 그린다</b>.
///
/// ⛔ <b>챕터 그래프는 표식을 만들지도 떼지도 않는다.</b> 뚫고 떼는 것은 연출 그래프의
/// 일이다 — 같은 것의 주인이 둘이 되면 갈린다(이 저장소가 이번 달에만 네 번 다친 자리다:
/// 에피소드 만들기 · 지우기 · 카드 찾기 · 판 찾기).
///
/// <b>간선이 아니다.</b> 챕터의 <see cref="ChapterEdge"/>는 <i>선택지</i>이고 문구·스탯변화·
/// 관문을 진다. 분기는 그중 아무것도 없다 (2026-09-18 소유자: *"조작의 단순함을 위해서,
/// 분기로 이어지는 것은 선택지와 다르게 라벨이나 스탯변화를 주지도 않을거야"*) —
/// <b>뚫어뒀다면 반드시 행해지는 통로</b>이고, 막는 것은 다녀온 쪽의 첫머리 조건이다
/// (<see cref="BranchEntryCondition"/>).
/// </summary>
public static class ChapterBranchMarkers
{
    /// <param name="FromEpisodeId">분기가 뚫린 에피소드.</param>
    /// <param name="ToEpisodeId">다녀올 에피소드.</param>
    /// <param name="LineId">그 줄 — 표식의 신원이다(한 에피소드가 분기를 여럿 가질 수 있다).</param>
    /// <param name="ToNodeId">다녀올 카드의 NodeId. 조건을 읽고 쓰는 쪽이 이것을 쓴다.</param>
    public sealed record Marker(
        string FromEpisodeId,
        string ToEpisodeId,
        string LineId,
        string ToNodeId);

    /// <summary>
    /// 그 챕터의 분기 전부. 챕터가 없거나 판이 없으면 빈 목록이다.
    ///
    /// <b>대본에 적힌 차례 그대로</b> 선다 — 판에서 읽는 차례와 카드에서 보는 차례가 같아야
    /// 한다(<c>NodeConnections</c>가 분기 포트를 세우는 규칙과 같다).
    ///
    /// ⚠ <b>이 챕터 안에서 끝나는 표식만</b> 낸다. 다녀올 노드가 다른 판에 있으면(구판
    /// 프로젝트의 자유 씬이 그랬다) 챕터 그래프가 그릴 카드가 없으므로 뺀다 — 없는 카드로
    /// 선을 그으면 판이 거짓말을 한다.
    /// </summary>
    public static IReadOnlyList<Marker> For(StoryProject project, string chapterId)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (EpisodeNaming.BoardOf(project, chapterId) is not { } board)
        {
            return [];
        }

        var onBoard = board.Nodes.OfType<DialogueNode>()
            .ToDictionary(node => node.Id, EpisodeNaming.EpisodeIdOf, StringComparer.Ordinal);

        var markers = new List<Marker>();

        foreach (DialogueNode card in board.Nodes.OfType<DialogueNode>())
        {
            string from = EpisodeNaming.EpisodeIdOf(card);

            // 대본의 줄 차례로 훑는다 — 확장(LineExtensions)의 순서는 손댄 차례라 사람이
            // 읽는 차례가 아니다.
            foreach (ScriptLine line in project.FindScript(card.ScriptId)?.ActiveLines ?? [])
            {
                if (card.FindExtension(line.Id)?.DetourTargetNodeId is not { Length: > 0 } target ||
                    !onBoard.TryGetValue(target, out string? to))
                {
                    continue;
                }

                markers.Add(new Marker(from, to, line.Id, target));
            }
        }

        return markers;
    }
}
