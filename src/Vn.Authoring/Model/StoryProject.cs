namespace Vn.Authoring.Model;

/// <summary>
/// 저작 도구가 다루는 공식 원본.
///
/// 프로젝트는 여러 <see cref="StoryFile"/>을 가지고, 각 파일이 노드를 소유한다.
/// 프로젝트 전체 노드 순서가 필요할 때는 <see cref="EnumerateNodes"/>를 사용한다.
/// 그 순서는 파일 순서 뒤에 각 파일 안의 노드 순서를 이어 붙인 것이다.
/// </summary>
public sealed class StoryProject
{
    /// <summary>StoryFile 소유 구조를 도입한 형식 버전.</summary>
    public const int CurrentFormatVersion = 2;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public string Title { get; set; } = "새 프로젝트";

    public List<StoryFile> Files { get; init; } = new();

    /// <summary>실행 출구가 아닌 Settings·Presentation 공급 관계.</summary>
    public List<NodeLink> Links { get; init; } = new();

    /// <summary>이야기가 시작되는 노드. null이면 아직 정하지 않은 것이다.</summary>
    public string? StartNodeId { get; set; }

    /// <summary>
    /// 프로젝트 전체 노드를 결정적인 순서로 펼친다.
    /// 파일 순서 → 각 파일의 Nodes 순서다.
    /// </summary>
    public IEnumerable<StoryNode> EnumerateNodes()
    {
        return Files.SelectMany(file => file.Nodes);
    }

    public StoryFile? FindFile(string? fileId)
    {
        return fileId is null
            ? null
            : Files.FirstOrDefault(file => string.Equals(file.Id, fileId, StringComparison.Ordinal));
    }

    public StoryFile? FindFileContainingNode(string? nodeId)
    {
        return nodeId is null
            ? null
            : Files.FirstOrDefault(file => file.Nodes.Any(
                node => string.Equals(node.Id, nodeId, StringComparison.Ordinal)));
    }

    public StoryNode? FindNode(string? nodeId)
    {
        return nodeId is null
            ? null
            : EnumerateNodes().FirstOrDefault(
                node => string.Equals(node.Id, nodeId, StringComparison.Ordinal));
    }

    public DialogueNode? FindDialogue(string? nodeId) => FindNode(nodeId) as DialogueNode;

    public NodeLink? FindLink(string? linkId)
    {
        return linkId is null
            ? null
            : Links.FirstOrDefault(link => string.Equals(link.Id, linkId, StringComparison.Ordinal));
    }

    public IEnumerable<NodeLink> EnumerateLinks(NodeLinkKind kind)
    {
        return Links.Where(link => link.Kind == kind);
    }

    /// <summary>
    /// 프로젝트 안의 모든 SetNode 조건. 파일 순서, SetNode 순서, 조건 순서를 지킨다.
    /// 편집·진단용 전체 조회이며 DialogueNode의 드롭다운 범위에는 사용하지 않는다.
    /// Dialogue별 범위는 AvailableConditionResolver가 Settings link를 기준으로 계산한다.
    /// </summary>
    public IEnumerable<ConditionDefinition> EnumerateConditions()
    {
        return EnumerateNodes().OfType<SetNode>().SelectMany(node => node.Conditions);
    }

    public ConditionDefinition? FindCondition(string? conditionId)
    {
        return conditionId is null
            ? null
            : EnumerateConditions()
                .FirstOrDefault(item => string.Equals(item.Id, conditionId, StringComparison.Ordinal));
    }

    public StoryProject Clone()
    {
        return new StoryProject
        {
            FormatVersion = FormatVersion,
            Title = Title,
            StartNodeId = StartNodeId,
            Files = Files.Select(file => file.Clone()).ToList(),
            Links = Links.Select(link => link.Clone()).ToList()
        };
    }
}
