using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 간선에 매달린 <b>자유 씬</b>을 찾아 계약의 <c>ViaNodeId</c>로 옮긴다 (2026-08-24).
///
/// <b>저작 자리는 엑셀이 아니라 연출 그래프다.</b> 시나리오 작가가 엑셀노드의 선택지
/// 포트에 커스텀 대사 노드를 잇고, 그 배선은 워크북이 아니라 프로젝트에 산다
/// (<see cref="DialogueNode.ChoiceExits"/>). 그래서 마음대로 떼고 붙여도 챕터 구조가
/// 흔들리지 않고 검증이 울지 않는다 — 조건 포트에 자유 씬을 다는 것과 같은 기계다.
///
/// v11에는 `간선` 시트에 `연출` 칸이 있었지만 2026-08-24에 폐지됐다. 같은 것이 엑셀에도
/// 있으면 두 곳에 살고 갈린다 — 되살릴 자리는 반대편이었다.
///
/// <b>화면이 포트를 고르는 규칙을 그대로 쓴다</b>(<c>GraphEditorView.PortFor</c>) —
/// 구판 대본에 문구가 같은 OPTION 줄이 남아 있으면 그 줄의 배선을 쓰고(옛 배선 보존),
/// 없으면 문구를 열쇠로 한 선택지 배선을 쓴다. 순서가 갈리면 <b>작가가 판에서 매단 씬이
/// 산출물에서 조용히 사라진다.</b>
/// </summary>
internal sealed class ChapterBoard
{
    /// <summary>프로젝트 없이 부른 자리 — <c>ViaNodeId</c>가 전부 빈 문자열로 나간다.</summary>
    private static readonly ChapterBoard Empty = new(null);

    private readonly StoryProject? _project;

    /// <summary>에피소드 → 그 에피소드의 엑셀 대사 노드. 배선을 찾는 출발점이다.</summary>
    private readonly Dictionary<string, DialogueNode> _excelNodes =
        new(StringComparer.Ordinal);

    /// <summary>
    /// 노드별 실행 포트. 한 에피소드에서 간선이 여럿 나가므로 캐시한다 —
    /// <see cref="NodeConnections.PortsOf"/>는 대본을 합치고 조건을 훑는다.
    /// </summary>
    private readonly Dictionary<string, IReadOnlyList<ExitPort>> _ports =
        new(StringComparer.Ordinal);

    private ChapterBoard(StoryProject? project)
    {
        _project = project;

        if (project is null)
        {
            return;
        }

        foreach (DialogueNode node in project.EnumerateNodes().OfType<DialogueNode>())
        {
            // 같은 에피소드에 노드가 둘일 수는 없지만(동기화가 하나만 세운다), 손으로
            // 만든 프로젝트에서 겹치면 먼저 만난 것을 쓴다. 여기서 던지지 않는 이유는
            // 내보내기가 배선 하나 때문에 챕터 전체를 막을 자리가 아니기 때문이다.
            if (node.ExcelEpisodeId is { } episodeId)
            {
                _excelNodes.TryAdd(episodeId, node);
            }
        }
    }

    public static ChapterBoard For(StoryProject? project) =>
        project is null ? Empty : new ChapterBoard(project);

    /// <summary>
    /// 이 에피소드를 재생할 <b>대사 노드</b>. 없으면 <c>null</c>이다.
    ///
    /// ⚠ <c>ExcelEpisodeId</c>를 먼저 본다 — 그것이 <b>진짜 연결</b>이다. 이름으로 찾는 것은
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

        string name = episode.DialogueEntry is { Length: > 0 } entry ? entry : episode.EpisodeId;

        return _project.EnumerateNodes().OfType<DialogueNode>()
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

    /// <summary>
    /// 이 길에 매달린 자유 씬의 <b>Yarn 노드 이름</b>. 없으면 빈 문자열이고, 런타임은
    /// 그때 곧장 도착 에피소드로 간다.
    /// </summary>
    public string NodeNameFor(ChapterEpisode episode, ChapterEdge edge) =>
        SceneFor(episode, edge) is { } target
            // ⚠ 이미터의 이름 규칙을 통과시킨다 — 런타임은 이 글자로 YarnProject에서 노드를
            // 찾는다(`ProgressionContentPreflight`가 재생 전에 대조한다). 손으로 이으면 공백
            // 든 씬 이름에서 갈린다. `DialogueEntryId`가 2026-08-23에 겪은 그 자리다.
            ? YarnBundleEmitter.StoryNodeTitleOf(target.Name, target.Id)
            : string.Empty;

    /// <summary>이 길에 매달린 자유 씬 노드. 없으면 <c>null</c>이다.</summary>
    public DialogueNode? SceneFor(ChapterEpisode episode, ChapterEdge edge)
    {
        if (_project is null || edge.HasNoOptionLabel)
        {
            // 문구 없는 길은 리더가 이미 오류로 막았다(v12) — 여기까지 오지 않는다.
            return null;
        }

        if (!_excelNodes.TryGetValue(episode.EpisodeId, out DialogueNode? source))
        {
            // 아직 동기화 전인 에피소드다. 그래프에도 카드가 없으므로 배선도 없다.
            return null;
        }

        return TargetIdFor(source, edge.OptionLabel!) is { } targetId
            ? _project.FindNode(targetId) as DialogueNode
            : null;
    }

    /// <summary>
    /// 배선이 사는 두 자리를 화면과 <b>같은 순서로</b> 본다 — 구판 OPTION 줄이 먼저다.
    /// </summary>
    private string? TargetIdFor(DialogueNode source, string optionLabel)
    {
        ExitPort? legacy = PortsOf(source).FirstOrDefault(port =>
            port.IsChoice &&
            string.Equals(port.ChoiceText, optionLabel, StringComparison.Ordinal));

        if (legacy is not null)
        {
            return legacy.TargetNodeId;
        }

        return source.ChoiceExits.GetValueOrDefault(optionLabel);
    }

    /// <summary>
    /// ⚠ <see cref="GameDefinition"/> 없이 부른다. 내보내기에는 정의가 없고, 여기서 읽는
    /// 것(<c>IsChoice</c>·<c>ChoiceText</c>·<c>TargetNodeId</c>)은 전부 대본에서 나오므로
    /// 정의와 무관하다 — 정의는 조건 어휘를 푸는 데만 쓰인다.
    /// </summary>
    private IReadOnlyList<ExitPort> PortsOf(DialogueNode source)
    {
        if (_ports.TryGetValue(source.Id, out IReadOnlyList<ExitPort>? cached))
        {
            return cached;
        }

        IReadOnlyList<ExitPort> ports = NodeConnections.PortsOf(source, _project!);
        _ports[source.Id] = ports;

        return ports;
    }
}
