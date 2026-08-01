namespace Vn.Authoring.Model;

/// <summary>
/// 실행 순서와는 다른 노드 간 공급 관계의 종류.
///
/// 실행 연결은 여전히 <see cref="StoryNode.DefaultExitTargetNodeId"/>와
/// <see cref="DialogueNode.BranchExits"/>가 소유한다. 이 목록에는 다음 노드를 정하지 않고
/// 다른 노드의 저작 데이터를 공급하는 관계만 둔다.
/// </summary>
public enum NodeLinkKind
{
    /// <summary>SetNode가 DialogueNode에 조건과 assignment를 공급한다.</summary>
    Settings
}

/// <summary>
/// 실행 출구와 분리된 타입 있는 노드 연결.
///
/// 노드 이름이나 파일 경로가 아니라 프로젝트 전체에서 안정된 NodeId를 가리킨다.
/// 여러 SetNode가 한 DialogueNode에 연결될 수 있으므로 <see cref="Order"/>로 합성 순서를
/// 명시한다.
/// </summary>
public sealed class NodeLink
{
    public NodeLink(
        string? id = null,
        NodeLinkKind kind = NodeLinkKind.Settings,
        string sourceNodeId = "",
        string targetNodeId = "")
    {
        Id = id ?? Identifier.Link();
        Kind = kind;
        SourceNodeId = sourceNodeId;
        TargetNodeId = targetNodeId;
    }

    public string Id { get; }

    public NodeLinkKind Kind { get; set; }

    public string SourceNodeId { get; set; }

    public string TargetNodeId { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>같은 대상에 여러 공급 노드가 붙을 때의 결정적 순서.</summary>
    public int Order { get; set; }

    public NodeLink Clone()
    {
        return new NodeLink(Id, Kind, SourceNodeId, TargetNodeId)
        {
            IsEnabled = IsEnabled,
            Order = Order
        };
    }
}
