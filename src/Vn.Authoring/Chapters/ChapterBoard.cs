using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 챕터의 에피소드가 <b>판의 어느 대사 노드로 재생되는가</b>를 잇는다.
///
/// ⚠ <b>그 챕터의 판만 본다</b> (2026-10-29 · `docs/plans/excel-boundary.md` C1). EpisodeId는
/// 챕터 안에서만 유일하다(<see cref="EpisodeLibrary"/>) — 2026-10-29까지 여기만 <b>프로젝트 전체</b>의
/// 대사 노드를 EpisodeId 하나로 사전에 넣고 먼저 만난 것을 집었다. 다른 챕터의 같은 Id가 그 챕터의
/// 노드 대신 잡혀 진행 JSON이 엉뚱한 대사를 부를 자리였다. 판은 <see cref="EpisodeNaming.BoardOf"/>로
/// 찾는다 — 챕터 = 판 1:1(G-1 v2)이고, 판이 아직 없으면 노드도 없다.
///
/// ⛔ 한때 <b>간선에 매달린 연출 씬</b>을 찾아 계약의 `ViaNodeId`로 옮기는 일도 했다.
/// 그 칸은 2026-09-17에 <b>양쪽에서 걷혔다</b> — 같은 재생 순서를 에피소드 한 칸으로
/// 말할 수 있으므로(`A —문구→ 연출 —자동→ B`) 곁칸은 두 번째 표현 방법이었다.
/// 옛 프로젝트 호환도 안 본다(2026-09-17 소유자: *"이전의 프로젝트들은 모두 버리고"*).
/// </summary>
internal sealed class ChapterBoard
{
    /// <summary>프로젝트 없이 부른 자리 — <c>ViaNodeId</c>가 전부 빈 문자열로 나간다.</summary>
    private static readonly ChapterBoard Empty = new(null, null);

    private readonly StoryProject? _project;

    /// <summary>그 챕터의 판. 프로젝트는 있는데 판이 아직 없으면 <c>null</c> — 노드도 없다.</summary>
    private readonly StoryFile? _board;

    /// <summary>에피소드 → 그 에피소드의 엑셀 대사 노드. 배선을 찾는 출발점이다.</summary>
    private readonly Dictionary<string, DialogueNode> _excelNodes =
        new(StringComparer.Ordinal);

    /// <summary>
    /// 노드별 실행 포트. 한 에피소드에서 간선이 여럿 나가므로 캐시한다 —
    /// <see cref="NodeConnections.PortsOf"/>는 대본을 합치고 조건을 훑는다.
    /// </summary>
    private readonly Dictionary<string, IReadOnlyList<ExitPort>> _ports =
        new(StringComparer.Ordinal);

    private ChapterBoard(StoryProject? project, string? chapterId)
    {
        _project = project;

        if (project is null || chapterId is null)
        {
            return;
        }

        _board = EpisodeNaming.BoardOf(project, chapterId);

        if (_board is null)
        {
            return;
        }

        foreach (DialogueNode node in _board.Nodes.OfType<DialogueNode>())
        {
            // 같은 에피소드에 노드가 둘일 수는 없지만(동기화가 하나만 세운다), 손으로
            // 만든 프로젝트에서 겹치면 먼저 만난 것을 쓴다. 여기서 던지지 않는 이유는
            // 내보내기가 배선 하나 때문에 챕터 전체를 막을 자리가 아니기 때문이다.
            if (node.MarkedEpisodeId is { } episodeId)
            {
                _excelNodes.TryAdd(episodeId, node);
            }
        }
    }

    /// <param name="chapterId">어느 챕터의 판을 볼 것인가. 그 판 밖의 노드는 보지 않는다.</param>
    public static ChapterBoard For(StoryProject? project, string chapterId) =>
        project is null ? Empty : new ChapterBoard(project, chapterId);

    /// <summary>
    /// 이 에피소드를 재생할 <b>대사 노드</b>. 없으면 <c>null</c>이다.
    ///
    /// ⚠ <c>MarkedEpisodeId</c>를 먼저 본다 — 그것이 <b>진짜 연결</b>이다. 이름으로 찾는 것은
    /// 아직 표식이 안 붙은 구판 프로젝트를 위한 뒷길이고, 판에서 노드를 개명하면 이름은
    /// 갈리지만 표식은 남는다.
    /// </summary>
    public DialogueNode? EpisodeNodeFor(ChapterEpisode episode)
    {
        if (_project is null)
        {
            return null;
        }

        if (_excelNodes.TryGetValue(episode.EpisodeId, out DialogueNode? tagged))
        {
            return tagged;
        }

        string name = EpisodeNaming.NodeNameOf(episode);

        // 이름 뒷길도 <b>그 판 안에서만</b> — 다른 챕터에 같은 이름의 카드가 있어도 남의 것이다.
        return _board?.Nodes.OfType<DialogueNode>()
            .FirstOrDefault(node => string.Equals(node.Name, name, StringComparison.Ordinal));
    }

    /// <summary>
    /// 이 에피소드의 <b>Yarn 노드 이름</b> — 계약의 <c>DialogueEntryId</c>. 판에서 못 찾으면
    /// 빈 문자열이고, 그때는 검증이 이미 오류로 막았다.
    ///
    /// ⚠ <b>이름의 주인은 판의 노드다</b> (2026-08-25). 엑셀의 `대사엔트리` 글자로 지으면
    /// 주인이 둘이 된다 — 판에서 노드를 개명하는 순간 진행 JSON은 옛 이름을 부르고 .yarn은
    /// 새 이름으로 서서, <b>로드·검증·증명이 전부 통과하는데 재생만 안 된다</b>. 이미터가
    /// 그 노드에 붙이는 타이틀과 같은 함수를 지나는 것이 유일한 방어다.
    /// </summary>
    public string EpisodeNodeNameFor(ChapterEpisode episode) =>
        EpisodeNodeFor(episode) is { } node
            ? YarnBundleEmitter.StoryNodeTitleOf(node.Name, node.Id)
            : string.Empty;

}
