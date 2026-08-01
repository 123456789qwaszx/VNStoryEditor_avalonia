using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;

namespace Vn.Authoring.Graph;

/// <summary>
/// StoryProject와 workspace의 펼침 상태에서 GraphView용 읽기 모델을 만든다.
///
/// 이 객체는 공식 데이터를 수정하지 않는다. 파일을 접어도 연결의 실제 source/target NodeId는
/// 그대로 유지하고, endpoint만 FileProxy의 해당 노드 행으로 바꾼다.
/// </summary>
public static class GraphProjectionBuilder
{
    private const double EmptyProxyStartX = 80;
    private const double EmptyProxyStartY = 80;
    private const double EmptyProxyGapX = 280;

    public static GraphProjection Build(
        StoryProject project,
        IReadOnlySet<string> expandedFileIds,
        GameDefinition? definition = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(expandedFileIds);

        var fileByNodeId = new Dictionary<string, StoryFile>(StringComparer.Ordinal);
        var rowIndexByNodeId = new Dictionary<string, int>(StringComparer.Ordinal);
        var portsByNodeId = new Dictionary<string, IReadOnlyList<GraphOutputPortProjection>>(StringComparer.Ordinal);

        foreach (StoryFile file in project.Files)
        {
            for (int index = 0; index < file.Nodes.Count; index++)
            {
                StoryNode node = file.Nodes[index];
                fileByNodeId[node.Id] = file;
                rowIndexByNodeId[node.Id] = index;
                portsByNodeId[node.Id] = BuildPorts(node, project, definition);
            }
        }

        IReadOnlyList<RawConnection> rawConnections = BuildRawConnections(project, portsByNodeId);
        Dictionary<string, int> incoming = CountByNode(rawConnections.Select(item => item.TargetNodeId));
        Dictionary<string, int> outgoing = CountByNode(rawConnections.Select(item => item.SourceNodeId));

        var items = new List<GraphItemProjection>();

        for (int fileIndex = 0; fileIndex < project.Files.Count; fileIndex++)
        {
            StoryFile file = project.Files[fileIndex];

            if (expandedFileIds.Contains(file.Id))
            {
                foreach (StoryNode node in file.Nodes)
                {
                    items.Add(new ExpandedNodeProjection(
                        file.Id,
                        node.Id,
                        node.Name,
                        KindOf(node),
                        new GraphPosition(node.Layout.X, node.Layout.Y),
                        portsByNodeId[node.Id]));
                }

                continue;
            }

            IReadOnlyList<CollapsedNodeEntry> entries = file.Nodes
                .Select(node => new CollapsedNodeEntry(
                    node.Id,
                    node.Name,
                    KindOf(node),
                    incoming.GetValueOrDefault(node.Id),
                    outgoing.GetValueOrDefault(node.Id)))
                .ToList();

            items.Add(new CollapsedFileProjection(
                file.Id,
                file.Name,
                file.RelativePath,
                ProxyPosition(file, fileIndex),
                entries));
        }

        var connections = new List<GraphConnectionProjection>();

        foreach (RawConnection raw in rawConnections)
        {
            if (!fileByNodeId.TryGetValue(raw.SourceNodeId, out StoryFile? sourceFile) ||
                !fileByNodeId.TryGetValue(raw.TargetNodeId, out StoryFile? targetFile))
            {
                continue;
            }

            bool sourceExpanded = expandedFileIds.Contains(sourceFile.Id);
            bool targetExpanded = expandedFileIds.Contains(targetFile.Id);

            var source = new GraphEndpointProjection(
                sourceFile.Id,
                raw.SourceNodeId,
                sourceExpanded
                    ? GraphEndpointKind.ExpandedNodeOutput
                    : GraphEndpointKind.CollapsedFileNodeOutput,
                sourceExpanded ? raw.PortKey : null,
                sourceExpanded ? null : rowIndexByNodeId[raw.SourceNodeId]);

            var target = new GraphEndpointProjection(
                targetFile.Id,
                raw.TargetNodeId,
                targetExpanded
                    ? GraphEndpointKind.ExpandedNodeInput
                    : GraphEndpointKind.CollapsedFileNodeInput,
                null,
                targetExpanded ? null : rowIndexByNodeId[raw.TargetNodeId]);

            connections.Add(new GraphConnectionProjection(
                raw.Key,
                raw.Kind,
                raw.SourceNodeId,
                raw.TargetNodeId,
                raw.Label,
                raw.PaletteIndex,
                raw.LinkId,
                raw.ExecutionPort,
                source,
                target));
        }

        return new GraphProjection(items, connections);
    }

    private static IReadOnlyList<GraphOutputPortProjection> BuildPorts(
        StoryNode node,
        StoryProject project,
        GameDefinition? definition)
    {
        var ports = NodeConnections.PortsOf(node, project, definition)
            .Select(exit => new GraphOutputPortProjection(
                PortKey(exit),
                exit.Kind == ExitPortKind.Default
                    ? GraphOutputPortKind.ExecutionDefault
                    : GraphOutputPortKind.ExecutionBranch,
                exit.NodeId,
                exit.Label,
                exit.PaletteIndex,
                exit.IsConnected,
                exit))
            .ToList();

        if (node is SetNode)
        {
            bool connected = project.Links.Any(link =>
                link.Kind == NodeLinkKind.Settings &&
                link.IsEnabled &&
                string.Equals(link.SourceNodeId, node.Id, StringComparison.Ordinal));

            ports.Add(new GraphOutputPortProjection(
                SettingsPortKey(node.Id),
                GraphOutputPortKind.Settings,
                node.Id,
                "조건 공급",
                -1,
                connected,
                null));
        }

        return ports;
    }

    private static IReadOnlyList<RawConnection> BuildRawConnections(
        StoryProject project,
        IReadOnlyDictionary<string, IReadOnlyList<GraphOutputPortProjection>> portsByNodeId)
    {
        var result = new List<RawConnection>();

        foreach (StoryNode node in project.EnumerateNodes())
        {
            IReadOnlyList<GraphOutputPortProjection> ports = portsByNodeId[node.Id];

            foreach (GraphOutputPortProjection port in ports)
            {
                if (port.ExecutionPort is not ExitPort exit ||
                    exit.TargetNodeId is not string targetNodeId)
                {
                    continue;
                }

                result.Add(new RawConnection(
                    $"execution:{exit.NodeId}:{exit.Kind}:{exit.BranchOpenLineId ?? "default"}",
                    exit.Kind == ExitPortKind.Default
                        ? GraphConnectionKind.ExecutionDefault
                        : GraphConnectionKind.ExecutionBranch,
                    node.Id,
                    targetNodeId,
                    port.Key,
                    port.Label,
                    port.PaletteIndex,
                    null,
                    exit));
            }
        }

        foreach (NodeLink link in project.Links.Where(link =>
                     link.Kind == NodeLinkKind.Settings && link.IsEnabled))
        {
            result.Add(new RawConnection(
                $"link:{link.Id}",
                GraphConnectionKind.Settings,
                link.SourceNodeId,
                link.TargetNodeId,
                SettingsPortKey(link.SourceNodeId),
                "조건 공급",
                -1,
                link.Id,
                null));
        }

        return result;
    }

    private static Dictionary<string, int> CountByNode(IEnumerable<string> nodeIds)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (string nodeId in nodeIds)
        {
            counts[nodeId] = counts.GetValueOrDefault(nodeId) + 1;
        }

        return counts;
    }

    private static GraphNodeKind KindOf(StoryNode node)
    {
        return node switch
        {
            DialogueNode => GraphNodeKind.Dialogue,
            SetNode => GraphNodeKind.Set,
            _ => throw new NotSupportedException($"지원하지 않는 노드 타입입니다: {node.GetType().Name}")
        };
    }

    private static GraphPosition ProxyPosition(StoryFile file, int fileIndex)
    {
        if (file.Nodes.Count == 0)
        {
            return new GraphPosition(
                EmptyProxyStartX + (fileIndex * EmptyProxyGapX),
                EmptyProxyStartY);
        }

        // 접기 전 노드들이 있던 영역의 중심에 프록시를 둔다. 실제 노드 Layout은 바꾸지 않는다.
        return new GraphPosition(
            file.Nodes.Average(node => node.Layout.X),
            file.Nodes.Average(node => node.Layout.Y));
    }

    private static string PortKey(ExitPort exit)
    {
        return exit.Kind == ExitPortKind.Default
            ? $"execution:{exit.NodeId}:default"
            : $"execution:{exit.NodeId}:branch:{exit.BranchOpenLineId}";
    }

    private static string SettingsPortKey(string nodeId) => $"settings:{nodeId}";

    private sealed record RawConnection(
        string Key,
        GraphConnectionKind Kind,
        string SourceNodeId,
        string TargetNodeId,
        string PortKey,
        string Label,
        int PaletteIndex,
        string? LinkId,
        ExitPort? ExecutionPort);
}
