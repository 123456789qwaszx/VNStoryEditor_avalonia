using Vn.Authoring.Model;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 판의 <b>대사 노드</b>와 챕터의 <b>에피소드</b>를 잇는 이름 규약 — <b>한 벌이다</b>.
///
/// ⛔ 이 규칙은 2026-09-17까지 <b>열 자리</b>에 손으로 복사돼 있었다(그래프 투영·자리잡기·
/// 대본 탭·연출 그래프·챕터 편집·내보내기). 한 규칙이 여러 곳에 살면 갈린다 — 이 저장소가
/// 반복해서 다친 그 자리다(V1 자동 길 검사, 스탯 검사 두 벌, 챕터 개명 규율 두 벌).
///
/// ⚠ <b>표식이 신원이고 이름은 글자다.</b> <see cref="DialogueNode.Name"/>은 사람이 판에서
/// 고치는 글자라 신원으로 삼으면 개명 때마다 끊긴다. 그래서 <see cref="DialogueNode.ExcelEpisodeId"/>가
/// 먼저고, 이름은 아직 표식이 안 붙은 구판 프로젝트를 위한 뒷길이다.
///
/// R7 P-6부터 <b>챕터 판의 대사 노드는 전부 에피소드</b>라서 이 규약이 늘 성립한다 —
/// 전에는 자유 씬이 규약 밖에 있었다.
/// </summary>
public static class EpisodeNaming
{
    /// <summary>그 노드가 대신하는 에피소드 Id — 표식이 먼저고, 없으면 이름이다.</summary>
    public static string EpisodeIdOf(DialogueNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.ExcelEpisodeId is { Length: > 0 } marked ? marked : node.Name;
    }

    /// <summary>
    /// 그 에피소드를 대신할 노드의 <b>이름</b> — `대사엔트리`가 먼저고, 없으면 EpisodeId다.
    ///
    /// ⚠ <see cref="EpisodeIdOf"/>의 역이 아니다. 판에서 노드를 개명해도 표식은 남으므로
    /// 왕복이 깨질 수 있고, 그것이 정상이다 — 신원은 표식이지 이름이 아니다.
    /// </summary>
    public static string NodeNameOf(ChapterEpisode episode)
    {
        ArgumentNullException.ThrowIfNull(episode);

        return episode.DialogueEntry is { Length: > 0 } entry ? entry : episode.EpisodeId;
    }

    /// <summary>
    /// 이 노드가 대신하는 <b>에피소드</b>. 없으면 <c>null</c>이다.
    ///
    /// 표식으로 먼저 찾고(진짜 신원), 못 찾으면 <b>이름이 대신하는 에피소드</b>를 찾는다 —
    /// 워크북 임포터가 노드를 `대사엔트리`로 짓기 때문에 EpisodeId만 봐서는 못 만난다.
    /// </summary>
    public static ChapterEpisode? EpisodeFor(ChapterDocument chapter, DialogueNode node)
    {
        ArgumentNullException.ThrowIfNull(chapter);
        ArgumentNullException.ThrowIfNull(node);

        string episodeId = EpisodeIdOf(node);

        return chapter.Episodes.FirstOrDefault(episode =>
                   string.Equals(episode.EpisodeId, episodeId, StringComparison.Ordinal))
               ?? chapter.Episodes.FirstOrDefault(episode =>
                   string.Equals(NodeNameOf(episode), node.Name, StringComparison.Ordinal));
    }
}
