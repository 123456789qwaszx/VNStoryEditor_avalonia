namespace Vn.Authoring.Model;

/// <summary>
/// 실행 순서와는 다른 노드 간 공급 관계의 종류.
///
/// 실행 연결은 여전히 <see cref="StoryNode.DefaultExitTargetNodeId"/>와
/// <see cref="DialogueNode.BranchExits"/>가 소유한다. 이 목록에는 다음 노드를 정하지 않고
/// 다른 노드의 저작 데이터를 공급하는 관계만 둔다.
///
/// 연출은 더 이상 여기에 없다. PresentationNode는 편집 중인 노드가 아니라 발행된
/// <see cref="Results.DialogueResult"/>를 읽으므로, 그 관계는 링크가 아니라
/// <see cref="PresentationNode.Source"/>가 소유하고 그래프는 그것을 계산해 그린다.
/// </summary>
public enum NodeLinkKind
{
    /// <summary>SetNode가 DialogueNode에 조건과 assignment를 공급한다.</summary>
    Settings,

    /// <summary>CommandSupplyNode가 PresentationNode에 커맨드 범주와 프리셋을 공급한다.</summary>
    CommandSupply,

    /// <summary>
    /// PresentationNode가 <b>발행한 결과</b>를 DialogueNode에 공급한다.
    /// 내보내기는 이 연결로 짝을 찾는다 — 명시적 합성 레코드를 따로 만들지 않는다.
    /// 형식 버전 2의 live Presentation link와 다르다: 여기서 흐르는 것은 편집 중인
    /// 노드가 아니라 얼어붙은 PresentationResult이고, 그 결과의 Source가 어느 대사
    /// 결과와 짝인지를 이미 못박고 있다.
    /// </summary>
    PresentationSupply
}

/// <summary>
/// 실행 출구와 분리된 타입 있는 노드 연결.
///
/// 노드 이름이나 파일 경로가 아니라 프로젝트 전체에서 안정된 NodeId를 가리킨다.
/// 여러 공급 노드가 한 DialogueNode에 연결될 수 있으므로 <see cref="Order"/>로 합성 순서를
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
