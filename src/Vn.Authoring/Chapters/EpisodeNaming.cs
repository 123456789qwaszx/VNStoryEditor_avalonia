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
/// 고치는 글자라 신원으로 삼으면 개명 때마다 끊긴다. 그래서 <see cref="DialogueNode.MarkedEpisodeId"/>가
/// 먼저고, 이름은 아직 표식이 안 붙은 구판 프로젝트를 위한 뒷길이다.
///
/// R7 P-6부터 <b>챕터 판의 대사 노드는 전부 에피소드</b>라서 이 규약이 늘 성립한다 —
/// 전에는 자유 씬이 규약 밖에 있었다.
/// </summary>
public static class EpisodeNaming
{
    /// <summary>
    /// 이 판이 곧 <b>챕터</b>라면 그 챕터. 작가의 낙서판이면 <c>null</c>이다.
    ///
    /// ⚠ <b>챕터 = 판 1:1이고, 잇는 것은 이름이다</b> (G-1 v2). 이 한 줄이 「이 카드가
    /// 에피소드인가」를 가르므로 <b>여기 한 곳에만</b> 둔다 — 새 프로젝트는 <c>기본 파일</c>
    /// 이라는 챕터 아닌 판으로 시작하고, 거기 세운 카드는 진행에 안 실린다.
    /// </summary>
    public static ChapterDocument? ChapterOfBoard(StoryProject project, StoryFile board)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(board);

        return project.Chapters.FirstOrDefault(chapter =>
            string.Equals(chapter.ChapterId, board.Name, StringComparison.Ordinal));
    }

    /// <summary>
    /// 그 챕터의 <b>판</b>. 없으면 <c>null</c>이다 — 만들지 않는다
    /// (만드는 것은 <c>ProjectEditor.EnsureChapterBoard</c>다).
    ///
    /// ⛔ <see cref="ChapterOfBoard"/>의 역이고 <b>같은 한 줄</b>이다. 2026-09-18까지
    /// <b>아홉 곳</b>에 손으로 복사돼 있었다(개명·삭제·자리잡기·화면 셋…). 한 줄짜리라
    /// 복사가 쉬웠고, 쉬운 만큼 어디서 어떻게 찾는지가 갈릴 자리였다.
    /// </summary>
    public static StoryFile? BoardOf(StoryProject project, string chapterId)
    {
        ArgumentNullException.ThrowIfNull(project);

        return project.Files.FirstOrDefault(file =>
            string.Equals(file.Name, chapterId, StringComparison.Ordinal));
    }

    /// <summary>
    /// 그 에피소드를 <b>지고 있는 카드</b>. 없으면 <c>null</c>이다.
    ///
    /// ⚠ <b>그 챕터의 판에서만</b> 찾는다. EpisodeId는 챕터 안에서만 유일하므로, 프로젝트
    /// 전체를 훑으면 다른 챕터의 같은 Id를 집는다.
    ///
    /// ⛔ 2026-09-18까지 <b>네 벌</b>이었고 <b>서로 답이 달랐다</b>: 대본 탭·개명은
    /// <i>표식 또는 이름</i>, 무대 프리뷰는 <i>표식만</i>. 표식이 없는 구판 카드에서
    /// 한쪽은 찾고 한쪽은 못 찾았다.
    ///
    /// ⚠ <see cref="ChapterBoard.EpisodeNodeFor"/>는 <b>다섯째가 아니다</b> — 그쪽은
    /// 내보내기용으로 <i>프로젝트 전체</i>를 훑고 이름 뒷길도 `대사엔트리`를 쓴다. 질문이
    /// 달라서 남겨 둔 것이고, 그 사실이 <see cref="ChapterBoard"/> 머리에 적혀 있다.
    /// </summary>
    public static DialogueNode? CardFor(StoryProject project, string chapterId, string episodeId)
    {
        ArgumentNullException.ThrowIfNull(project);

        return BoardOf(project, chapterId)?.Nodes.OfType<DialogueNode>()
            .FirstOrDefault(node =>
                string.Equals(EpisodeIdOf(node), episodeId, StringComparison.Ordinal));
    }

    /// <summary>
    /// 이 카드가 선 <b>챕터</b>. 챕터 판이 아니면 <c>null</c>이고, 그때 이 카드는
    /// <b>에피소드가 아니다</b>.
    /// </summary>
    public static ChapterDocument? ChapterOf(StoryProject project, StoryNode node)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(node);

        return project.FindFileContainingNode(node.Id) is { } board
            ? ChapterOfBoard(project, board)
            : null;
    }

    /// <summary>그 노드가 대신하는 에피소드 Id — 표식이 먼저고, 없으면 이름이다.</summary>
    public static string EpisodeIdOf(DialogueNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.MarkedEpisodeId is { Length: > 0 } marked ? marked : node.Name;
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
