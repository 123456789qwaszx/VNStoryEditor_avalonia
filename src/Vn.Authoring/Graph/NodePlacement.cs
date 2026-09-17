using Vn.Authoring.Chapters;
using Vn.Authoring.Model;

namespace Vn.Authoring.Graph;

/// <summary>
/// <b>새 대사 노드가 설 자리</b> — 제 장면의 줄에 (R7 P-4 · <c>docs/plans/R7.md</c>).
///
/// ⛔ <b>부르는 자리가 셋이다</b>: [챕터 그래프]의 [＋ 에피소드] · [대본] 탭의 [＋ 대본] ·
/// 대본 워크북 임포터. 셋 다 지금까지 자리를 안 주고 원점에 세웠고, 그래서 판을 열면
/// 카드가 <b>한 곳에 쌓여</b> 챕터 프레임도 장면 영역도 뜻을 잃었다. 규칙이 셋이면 갈리므로
/// 한 벌로 둔다.
///
/// ⚠ <b>이미 선 카드는 안 옮긴다.</b> 자리는 사람이 끌어 정하는 것이고, 여는 때마다 다시
/// 줄을 세우면 그 손이 지워진다 — 여기서 정하는 것은 <b>새로 서는 카드</b>의 첫 자리뿐이다.
/// </summary>
public static class NodePlacement
{
    /// <summary>옆 카드까지의 거리 — 카드 너비(210)에 여백을 더한 값.</summary>
    public const double Column = 320;

    /// <summary>장면 한 줄의 높이. 카드와 그 아래 선택지 가지가 함께 들어간다.</summary>
    public const double SceneRow = 260;

    /// <summary>
    /// 그 에피소드의 노드가 설 자리.
    ///
    /// 같은 장면에 이미 카드가 있으면 <b>그 줄의 오른쪽 끝</b>에, 없으면 <b>제 장면의 줄</b>
    /// 왼쪽 끝에 선다. 챕터를 못 찾거나 에피소드가 아니면 판의 오른쪽 끝에 둔다 — 겹치지만
    /// 않으면 된다.
    /// </summary>
    public static (double X, double Y) For(StoryProject project, string chapterId, string episodeId)
    {
        ArgumentNullException.ThrowIfNull(project);

        StoryFile? board = Chapters.EpisodeNaming.BoardOf(project, chapterId);

        ChapterDocument? chapter = project.Chapters.FirstOrDefault(item =>
            string.Equals(item.ChapterId, chapterId, StringComparison.Ordinal));

        if (board is null || chapter is null)
        {
            return (RightOf(board), 0);
        }

        IReadOnlyList<ChapterScene> scenes = ChapterSceneGrouping.Of(chapter.ToGraphModel(chapterId));

        int row = -1;

        for (int index = 0; index < scenes.Count; index++)
        {
            if (scenes[index].Episodes.Any(episode =>
                    string.Equals(episode.EpisodeId, episodeId, StringComparison.Ordinal)))
            {
                row = index;
                break;
            }
        }

        if (row < 0)
        {
            // 챕터에 없는 이름이다 — 진행에 안 실리는 노드이므로 줄을 안 준다.
            return (RightOf(board), 0);
        }

        var inScene = new HashSet<string>(
            scenes[row].Episodes.Select(episode => episode.EpisodeId), StringComparer.Ordinal);

        List<DialogueNode> neighbours = board.Nodes.OfType<DialogueNode>()
            .Where(node => inScene.Contains(EpisodeOf(node)))
            .ToList();

        // 이웃이 있으면 그 줄을 따른다 — 사람이 그 장면을 통째로 옮겨 뒀을 수 있다.
        return neighbours.Count > 0
            ? (neighbours.Max(node => node.Layout.X) + Column, neighbours.Min(node => node.Layout.Y))
            : (0, row * SceneRow);
    }

    /// <summary>판의 오른쪽 끝 — 겹치지만 않게 둘 때.</summary>
    private static double RightOf(StoryFile? board) =>
        board is null || board.Nodes.Count == 0
            ? 0
            : board.Nodes.Max(node => node.Layout.X) + Column;

    /// <summary>그 노드가 대신하는 에피소드 — 규약은 <see cref="Chapters.EpisodeNaming"/> 한 벌이다.</summary>
    private static string EpisodeOf(DialogueNode node) => Chapters.EpisodeNaming.EpisodeIdOf(node);
}
