using Vn.Authoring.Flow;

namespace Vn.Authoring.Graph;

/// <summary>
/// GraphView가 그릴 수 있도록 StoryProject를 펼친 읽기 모델.
///
/// 공식 원본은 StoryProject와 NodeId 연결이다. 파일 접힘은 그 원본을 바꾸지 않고,
/// 실제 NodeCard 대신 FileProxy의 특정 노드 행을 endpoint로 선택하는 화면 투영일 뿐이다.
/// </summary>
public sealed class GraphProjection
{
    public GraphProjection(
        IReadOnlyList<GraphItemProjection> items,
        IReadOnlyList<GraphConnectionProjection> connections)
    {
        Items = items;
        Connections = connections;
    }

    public IReadOnlyList<GraphItemProjection> Items { get; }

    public IReadOnlyList<GraphConnectionProjection> Connections { get; }
}

public readonly record struct GraphPosition(double X, double Y);

public abstract record GraphItemProjection(string FileId, GraphPosition Position);

/// <summary>펼쳐진 StoryFile 안에서 실제 노드 카드로 표시할 항목.</summary>
/// <param name="Badge">
/// 카드에 붙는 짧은 부가 정보. 대사 노드는 발행한 최신 버전을, 연출 노드는 읽고 있는
/// 대사 결과 버전을 보여 준다. 표시용 문자열이며 여기서 데이터 의미를 역추론하지 않는다.
/// </param>
public sealed record ExpandedNodeProjection(
    string FileId,
    string NodeId,
    string NodeName,
    GraphNodeKind NodeKind,
    GraphPosition Position,
    IReadOnlyList<GraphOutputPortProjection> OutputPorts,
    string? Badge = null)
    : GraphItemProjection(FileId, Position);

/// <summary>접힌 StoryFile 하나를 대신하는 회색 프록시.</summary>
public sealed record CollapsedFileProjection(
    string FileId,
    string FileName,
    string RelativePath,
    GraphPosition Position,
    IReadOnlyList<CollapsedNodeEntry> Nodes)
    : GraphItemProjection(FileId, Position);

/// <summary>
/// FileProxy 안의 실제 노드 행. 행을 눌렀을 때 선택할 대상과 간선 anchor를 NodeId로 유지한다.
/// </summary>
public sealed record CollapsedNodeEntry(
    string NodeId,
    string NodeName,
    GraphNodeKind NodeKind,
    int IncomingCount,
    int OutgoingCount);

public enum GraphNodeKind
{
    Dialogue,
    Set,
    Presentation,

    /// <summary>PresentationNode에 커맨드 범주·프리셋을 공급하는 노드.</summary>
    CommandSupply
}

/// <summary>
/// 그래프에 어떤 노드 종류를 그릴지. 화면이 아니라 투영이 거른다 — 무엇이 보이는지의
/// 규칙이 그리는 코드 안에 섞이면 테스트할 자리가 사라진다.
///
/// <b>간선 정합 규칙:</b> 간선의 어느 한쪽 끝 노드가 필터로 숨으면 그 간선도 함께 숨는다.
/// 허공에 매달린 간선을 그리지 않는다.
/// </summary>
public sealed record GraphFilter(
    bool ShowDialogue = true,
    bool ShowSet = true,
    bool ShowPresentation = true,
    bool ShowCommandSupply = true,
    bool ShowResultConnections = true)
{
    public static GraphFilter All { get; } = new();

    /// <summary>DialogueNode만 남기는 흐름 보기.</summary>
    public static GraphFilter FlowOnly { get; } = new(
        ShowDialogue: true,
        ShowSet: false,
        ShowPresentation: false,
        ShowCommandSupply: false,
        ShowResultConnections: false);

    public bool Shows(GraphNodeKind kind) => kind switch
    {
        GraphNodeKind.Dialogue => ShowDialogue,
        GraphNodeKind.Set => ShowSet,
        GraphNodeKind.Presentation => ShowPresentation,
        GraphNodeKind.CommandSupply => ShowCommandSupply,
        _ => true
    };
}

public enum GraphOutputPortKind
{
    ExecutionDefault,
    ExecutionBranch,
    Settings,

    /// <summary>이 대사 노드가 발행한 결과. 연출 노드가 그 결과를 입력으로 읽는다.</summary>
    PublishedResult
}

/// <summary>펼쳐진 노드 카드가 표시할 출력 포트.</summary>
public sealed record GraphOutputPortProjection(
    string Key,
    GraphOutputPortKind Kind,
    string NodeId,
    string Label,
    int PaletteIndex,
    bool IsConnected,
    ExitPort? ExecutionPort);

public enum GraphConnectionKind
{
    ExecutionDefault,
    ExecutionBranch,
    Settings,

    /// <summary>
    /// 발행된 대사 결과가 연출 노드의 <b>읽기 전용 입력</b>이 되는 관계.
    /// 실행 흐름이 아니라 저작 의존성이며, 데이터는 링크가 아니라
    /// <c>PresentationNode.Source</c>가 소유한다.
    /// </summary>
    ResultSnapshot
}

public enum GraphEndpointKind
{
    ExpandedNodeOutput,
    ExpandedNodeInput,
    CollapsedFileNodeOutput,
    CollapsedFileNodeInput
}

/// <summary>
/// 간선 한쪽 끝이 화면에서 어디에 붙는지 나타낸다.
/// NodeId는 언제나 실제 노드이며, FileProxy로 투영돼도 FileId로 대체되지 않는다.
/// </summary>
public sealed record GraphEndpointProjection(
    string FileId,
    string NodeId,
    GraphEndpointKind Kind,
    string? PortKey,
    int? ProxyRowIndex);

public sealed record GraphConnectionProjection(
    string Key,
    GraphConnectionKind Kind,
    string SourceNodeId,
    string TargetNodeId,
    string Label,
    int PaletteIndex,
    string? LinkId,
    ExitPort? ExecutionPort,
    GraphEndpointProjection Source,
    GraphEndpointProjection Target);
