using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

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
        GameDefinition? definition = null,
        GraphFilter? filter = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(expandedFileIds);

        filter ??= GraphFilter.All;

        var fileByNodeId = new Dictionary<string, StoryFile>(StringComparer.Ordinal);
        var rowIndexByNodeId = new Dictionary<string, int>(StringComparer.Ordinal);
        var kindByNodeId = new Dictionary<string, GraphNodeKind>(StringComparer.Ordinal);
        var portsByNodeId = new Dictionary<string, IReadOnlyList<GraphOutputPortProjection>>(StringComparer.Ordinal);

        // A 계층 격리 (2026-08-15 소유자) — 챕터 조건 공급 설정노드는 동기화의 배관이지
        // 작가의 데이터가 아니다. 식(스탯 변수)이 연출 그래프에 노출되면 안 되므로
        // 카드도 공급 링크도 그리지 않는다. 공급 자체는 데이터에 그대로 살아 있어서
        // 조건 드롭다운의 라벨과 <<if>> 역조회는 변함없이 동작한다.
        var hiddenSupplyNodeIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (StoryFile file in project.Files)
        {
            for (int index = 0; index < file.Nodes.Count; index++)
            {
                StoryNode node = file.Nodes[index];
                fileByNodeId[node.Id] = file;
                rowIndexByNodeId[node.Id] = index;
                kindByNodeId[node.Id] = KindOf(node);
                portsByNodeId[node.Id] = BuildPorts(node, project, definition);

                if (Chapters.EpisodeSyncService.IsConditionSupplyNode(node, file))
                {
                    hiddenSupplyNodeIds.Add(node.Id);
                }
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
                    if (!filter.Shows(KindOf(node)) || hiddenSupplyNodeIds.Contains(node.Id))
                    {
                        continue;
                    }

                    items.Add(new ExpandedNodeProjection(
                        file.Id,
                        node.Id,
                        node.Name,
                        KindOf(node),
                        new GraphPosition(node.Layout.X, node.Layout.Y),
                        portsByNodeId[node.Id],
                        BadgeFor(node, project)));
                }

                continue;
            }

            IReadOnlyList<CollapsedNodeEntry> entries = file.Nodes
                .Where(node => filter.Shows(KindOf(node)) && !hiddenSupplyNodeIds.Contains(node.Id))
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

        // 접힌 파일의 행 번호는 필터로 남은 항목 기준이다. 원본 인덱스를 쓰면 간선 끝이
        // 숨은 행을 가리켜 허공에 붙는다.
        foreach (GraphItemProjection item in items)
        {
            if (item is CollapsedFileProjection proxy)
            {
                for (int row = 0; row < proxy.Nodes.Count; row++)
                {
                    rowIndexByNodeId[proxy.Nodes[row].NodeId] = row;
                }
            }
        }

        var connections = new List<GraphConnectionProjection>();

        foreach (RawConnection raw in rawConnections)
        {
            if (!fileByNodeId.TryGetValue(raw.SourceNodeId, out StoryFile? sourceFile) ||
                !fileByNodeId.TryGetValue(raw.TargetNodeId, out StoryFile? targetFile))
            {
                continue;
            }

            // 간선 정합: 한쪽 끝 노드가 필터나 공급 숨김으로 안 보이면 간선도 숨는다.
            if (!filter.Shows(kindByNodeId[raw.SourceNodeId]) ||
                !filter.Shows(kindByNodeId[raw.TargetNodeId]) ||
                hiddenSupplyNodeIds.Contains(raw.SourceNodeId) ||
                hiddenSupplyNodeIds.Contains(raw.TargetNodeId))
            {
                continue;
            }

            if (raw.Kind == GraphConnectionKind.ResultSnapshot && !filter.ShowResultConnections)
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
        IEnumerable<Vn.Authoring.Flow.ExitPort> exits = NodeConnections.PortsOf(node, project, definition);

        // T2 (철도 배선) — 엑셀노드의 선택지 옵션 포트와 기본 출구는 카드가 아니라
        // 짝 간선의 칩에 산다. IF 갈래 출구는 카드에 남는다(내부 곁가지는 간선과 무관).
        if (node is DialogueNode { ExcelEpisodeId: not null })
        {
            exits = exits.Where(exit =>
                exit.Kind == Vn.Authoring.Flow.ExitPortKind.Branch && !exit.IsChoice);
        }

        var ports = exits
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
            // 공급 범위가 판(챕터) 전체가 된 뒤로(2026-08-17) 이 포트는 이을 곳이 없다 —
            // 같은 판에 서 있는 것만으로 이미 미치고 있다. 포트는 "이 판에 공급 중"이라는
            // 사실 표시로만 남는다(늘 켜짐).
            ports.Add(new GraphOutputPortProjection(
                SettingsPortKey(node.Id),
                GraphOutputPortKind.Settings,
                node.Id,
                "이 챕터에 공급 중",
                -1,
                true,
                null));
        }

        // 공급 간선은 Settings와 같은 비실행 연결이다. 포트 종류도 같은 것을 쓴다 —
        // 화면이 새 종류를 몰라도 그려진다.
        if (node is CommandSupplyNode)
        {
            bool connected = project.Links.Any(link =>
                link.Kind == NodeLinkKind.CommandSupply &&
                link.IsEnabled &&
                string.Equals(link.SourceNodeId, node.Id, StringComparison.Ordinal));

            ports.Add(new GraphOutputPortProjection(
                SupplyPortKey(node.Id),
                GraphOutputPortKind.Settings,
                node.Id,
                "커맨드 공급",
                -1,
                connected,
                null));
        }

        // 연출 노드는 발행한 결과를 대사 노드에 되돌려 공급한다. 내보내기 짝은 이 연결이 정한다.
        if (node is PresentationNode)
        {
            bool connected = project.Links.Any(link =>
                link.Kind == NodeLinkKind.PresentationSupply &&
                link.IsEnabled &&
                string.Equals(link.SourceNodeId, node.Id, StringComparison.Ordinal));

            ports.Add(new GraphOutputPortProjection(
                PresentationSupplyPortKey(node.Id),
                GraphOutputPortKind.Settings,
                node.Id,
                "연출 공급",
                -1,
                connected,
                null));
        }

        // 발행 결과 포트는 대사 노드 쪽에 붙는다. 연출은 이 결과를 읽는 소비자이지
        // 대사에 무언가를 공급하는 쪽이 아니다. 화살표 방향이 의존 방향과 같아야 한다.
        if (node is DialogueNode)
        {
            DialogueResult? latest = project.Results.DialogueResultsOf(node.Id).LastOrDefault();
            bool consumed = latest is not null && project.EnumerateNodes()
                .OfType<PresentationNode>()
                .Any(presentation => presentation.Source is { } source &&
                    string.Equals(source.ResultId, latest.Identity.ResultId, StringComparison.Ordinal));

            ports.Add(new GraphOutputPortProjection(
                ResultPortKey(node.Id),
                GraphOutputPortKind.PublishedResult,
                node.Id,
                latest is null ? "발행 없음" : $"결과 v{latest.Identity.Version}",
                -1,
                consumed,
                null));
        }

        return ports;
    }

    /// <summary>
    /// 카드에 붙는 짧은 부가 정보. 발행 버전과 읽는 버전을 즉시 알 수 있게 한다.
    /// 모든 발행 버전을 카드로 펼치면 몇 번만 발행해도 그래프가 결과로 덮인다.
    /// </summary>
    private static string? BadgeFor(StoryNode node, StoryProject project)
    {
        switch (node)
        {
            case DialogueNode dialogue:
            {
                DialogueResult? latest = project.Results.DialogueResultsOf(dialogue.Id).LastOrDefault();
                string script = project.FindScript(dialogue.ScriptId)?.Name ?? "대본 없음";
                string badge = latest is null ? script : $"{script} · v{latest.Identity.Version} 발행";

                // 엑셀노드 표식 — 카드만 봐도 "이 본문은 엑셀 소유"임이 보여야,
                // 열어 보고 나서야 잠긴 것을 아는 헛걸음이 없다. 줄 수는 타임라인 읽기의
                // 눈금이다(T1) — 어느 에피소드가 무거운지 카드에서 보인다.
                if (dialogue.ExcelEpisodeId is null)
                {
                    return badge;
                }

                int lineCount = project.FindScript(dialogue.ScriptId)?.ActiveLines.Count() ?? 0;
                return $"📄 엑셀 · {badge} · {lineCount}줄";
            }

            case PresentationNode presentation:
            {
                if (presentation.Source is not { } source)
                {
                    return "입력 결과 없음";
                }

                DialogueResult? dialogueResult = project.Results.FindDialogue(
                    source.ResultId,
                    source.Version);

                return dialogueResult is null
                    ? $"{source.Label} (없음)"
                    : $"{dialogueResult.SourceNodeName} v{source.Version} 읽는 중";
            }

            case CommandSupplyNode supply:
                return $"{supply.Categories.Count}개 범주 · {supply.Presets.Count}개 프리셋";

            default:
                return null;
        }
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

        // 조건 공급선은 그리지 않는다 (2026-08-17 소유자 — "챕터 단위로 전역에 쓰이는거야").
        // 범위가 판 전체이므로 선을 그리면 대사노드 수만큼 거미줄이 되고, 그 선이 무엇을
        // 정하지도 않는다. 구판 프로젝트에 남은 Settings 링크 데이터는 조용히 무시된다.

        foreach (NodeLink link in project.Links.Where(link =>
                     link.Kind == NodeLinkKind.CommandSupply && link.IsEnabled))
        {
            result.Add(new RawConnection(
                $"link:{link.Id}",
                GraphConnectionKind.Settings,
                link.SourceNodeId,
                link.TargetNodeId,
                SupplyPortKey(link.SourceNodeId),
                "커맨드 공급",
                -1,
                link.Id,
                null));
        }

        foreach (NodeLink link in project.Links.Where(link =>
                     link.Kind == NodeLinkKind.PresentationSupply && link.IsEnabled))
        {
            result.Add(new RawConnection(
                $"link:{link.Id}",
                GraphConnectionKind.Settings,
                link.SourceNodeId,
                link.TargetNodeId,
                PresentationSupplyPortKey(link.SourceNodeId),
                "연출 공급",
                -1,
                link.Id,
                null));
        }

        // 연출이 어느 대사 결과를 읽는지는 링크가 아니라 계산이다. 결과를 낳은 노드가
        // 아직 프로젝트에 있을 때만 간선을 그린다. 없으면 뱃지에 "(없음)"으로 남는다.
        foreach (PresentationNode presentation in project.EnumerateNodes().OfType<PresentationNode>())
        {
            if (presentation.Source is not { } source)
            {
                continue;
            }

            DialogueResult? dialogue = project.Results.FindDialogue(source.ResultId, source.Version);

            if (dialogue is null || project.FindDialogue(dialogue.SourceNodeId) is null)
            {
                continue;
            }

            result.Add(new RawConnection(
                $"result:{presentation.Id}",
                GraphConnectionKind.ResultSnapshot,
                dialogue.SourceNodeId,
                presentation.Id,
                ResultPortKey(dialogue.SourceNodeId),
                $"결과 v{source.Version}",
                -1,
                null,
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
            PresentationNode => GraphNodeKind.Presentation,
            CommandSupplyNode => GraphNodeKind.CommandSupply,
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

    private static string SupplyPortKey(string nodeId) => $"supply:{nodeId}";

    private static string PresentationSupplyPortKey(string nodeId) => $"presSupply:{nodeId}";

    private static string ResultPortKey(string nodeId) => $"result:{nodeId}";

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
