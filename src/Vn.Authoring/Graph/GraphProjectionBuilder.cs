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

        // 배관 노드는 카드를 그리지 않는다:
        // - A 계층 격리 (2026-08-15 소유자) — 챕터 조건 공급 설정노드는 동기화의 배관이지
        //   작가의 데이터가 아니다. 식(스탯 변수)이 연출 그래프에 노출되면 안 된다.
        //   공급 자체는 데이터에 살아 있어 조건 라벨과 <<if>> 역조회는 변함없다.
        // - 연출 노드 (2026-08-21 소유자) — 발행·배선이 자동화됐다
        //   (ProjectEditor.EnsurePresentationChannel). 입구는 무대 프리뷰의 선택기이고
        //   그래프에는 카드도 결과·공급 배선도 그리지 않는다. 데이터는 그대로 산다.
        // - 연출 공급 노드 (같은 날, 소유자: "연출 공급을 제거해") — 이 노드가 잇던
        //   상대가 연출 노드인데 그쪽이 숨었다. 공급 데이터는 살아 있어 커맨드 범위·
        //   프리셋 해석(AvailablePresentationCommandResolver)은 변함없이 돈다.
        var hiddenPlumbingNodeIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (StoryFile file in project.Files)
        {
            for (int index = 0; index < file.Nodes.Count; index++)
            {
                StoryNode node = file.Nodes[index];
                fileByNodeId[node.Id] = file;
                rowIndexByNodeId[node.Id] = index;
                kindByNodeId[node.Id] = KindOf(node);
                portsByNodeId[node.Id] = BuildPorts(node, project, definition);

                if (node is PresentationNode or CommandSupplyNode ||
                    Chapters.EpisodeSyncService.IsConditionSupplyNode(node, file))
                {
                    hiddenPlumbingNodeIds.Add(node.Id);
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
                    if (!filter.Shows(KindOf(node)) || hiddenPlumbingNodeIds.Contains(node.Id))
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
                .Where(node => filter.Shows(KindOf(node)) && !hiddenPlumbingNodeIds.Contains(node.Id))
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

            // 간선 정합: 한쪽 끝 노드가 필터나 배관 숨김으로 안 보이면 간선도 숨는다.
            if (!filter.Shows(kindByNodeId[raw.SourceNodeId]) ||
                !filter.Shows(kindByNodeId[raw.TargetNodeId]) ||
                hiddenPlumbingNodeIds.Contains(raw.SourceNodeId) ||
                hiddenPlumbingNodeIds.Contains(raw.TargetNodeId))
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

        // 연출·연출 공급 노드의 포트와 대사 노드의 발행 결과 포트는 2026-08-21에 사라졌다 —
        // 발행·배선이 자동이 되면서(EnsurePresentationChannel) 그 카드들이 배관으로 숨었고,
        // 끌어서 잇던 포트들은 이을 주체가 없다.

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

        // 커맨드 공급·연출 공급 링크와 결과 스냅샷 간선은 2026-08-21에 그리기를 멈췄다 —
        // 연출·연출 공급 노드가 배관으로 숨어 양 끝 중 하나가 늘 없는 간선이었다.
        // 데이터(링크·Source)는 그대로 살아 내보내기 짝(NodeExportResolver)과 커맨드
        // 범위 해석(AvailablePresentationCommandResolver)이 계속 쓴다.

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
