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
public sealed record ExpandedNodeProjection(
    string FileId,
    string NodeId,
    string NodeName,
    GraphNodeKind NodeKind,
    GraphPosition Position,
    IReadOnlyList<GraphOutputPortProjection> OutputPorts)
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
    Presentation
}

public enum GraphOutputPortKind
{
    ExecutionDefault,
    ExecutionBranch,
    Settings,
    Presentation
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
    Presentation
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
