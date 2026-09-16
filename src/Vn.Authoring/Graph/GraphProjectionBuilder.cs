using Vn.Authoring.Chapters;
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
                portsByNodeId[node.Id] = BuildPorts(node, file, project, definition);

                if (node is PresentationNode or CommandSupplyNode ||
                    Chapters.ChapterBoardSupply.IsConditionSupplyNode(node, file))
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

            IReadOnlyList<CollapsedSceneGroup> scenes = SceneGroups(project, file, entries);

            items.Add(new CollapsedFileProjection(
                file.Id,
                file.Name,
                file.RelativePath,
                ProxyPosition(file, fileIndex),
                // ⚠ 묶음을 평평하게 편 것이 곧 행 순서다 — 묶으면서 순서가 바뀌므로
                //   `entries`를 그대로 두면 행 번호와 화면이 어긋난다.
                scenes.SelectMany(scene => scene.Entries).ToList(),
                scenes));
        }

        // 접힌 파일의 행 번호는 필터로 남은 항목 기준이다. 원본 인덱스를 쓰면 간선 끝이
        // 숨은 행을 가리켜 허공에 붙는다.
        //
        // ⚠ <b>장면 머리글도 한 행을 차지한다</b> (R6 S-3). 머리글을 안 세면 그 아래 노드의
        //    간선이 한 칸씩 위로 붙는다 — 화면과 기하가 같은 셈법을 써야 한다.
        foreach (GraphItemProjection item in items)
        {
            if (item is not CollapsedFileProjection proxy)
            {
                continue;
            }

            int row = 0;

            foreach (CollapsedSceneGroup scene in proxy.Scenes)
            {
                if (scene.HasHeader)
                {
                    row++;
                }

                foreach (CollapsedNodeEntry entry in scene.Entries)
                {
                    rowIndexByNodeId[entry.NodeId] = row++;
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
        StoryFile file,
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

        // 설정노드의 "이 챕터에 공급 중" 포트는 2026-08-22에 사라졌다 (소유자) —
        // 공급 범위가 판 전체가 된 2026-08-17 이후로 이을 곳이 없어 <b>사실 표시</b>로만
        // 남아 있었다. 카드가 늘 켜진 포트를 달고 있으면 "여기서 무언가를 이을 수 있다"고
        // 말하는 셈이라 오히려 거짓말이었다. 공급은 같은 판에 선 것으로 이미 성립한다.
        //
        // 연출·연출 공급 노드의 포트와 대사 노드의 발행 결과 포트는 2026-08-21에 사라졌다 —
        // 발행·배선이 자동이 되면서(EnsurePresentationChannel) 그 카드들이 배관으로 숨었고,
        // 끌어서 잇던 포트들은 이을 주체가 없다.

        ports.AddRange(ChoiceSlots(node, file, project));

        return ports;
    }

    /// <summary>화면이 <b>새로</b> 낼 수 있는 선택지 칸 수 (R7 §2 ③ — 소유자: "딱 3개").</summary>
    private const int ChoiceSlots3 = 3;

    /// <summary>
    /// <b>챕터 간선 슬롯</b> — 이 에피소드에서 나가는 선택지들 (R7 P-1 · 2026-09-16).
    ///
    /// 앞칸부터 <b>이미 있는 간선</b>, 남은 칸은 빈 슬롯이다. 라벨을 적어야 살아나고
    /// (§2 ③), 이어 붙는 순간 고쳐지는 것은 <c>ChapterDocument.Edges</c>다(§2 ①).
    ///
    /// ⚠ <b>간선이 셋을 넘으면 넘는 대로 전부 낸다.</b> 3은 <b>새로 만드는 칸</b>의 수이지
    /// 상한이 아니다 — 엑셀에서 넷을 만든 챕터의 넷째를 화면이 숨기면 사람은 <b>사라진 줄</b>
    /// 안다. 상한을 모델에 박는 것은 v9가 없앤 `선택지수` 칸을 되살리는 일이다.
    ///
    /// ⚠ <b>에피소드 노드만</b> — 자유 씬은 챕터의 진행에 안 실리므로 놓을 자리가 없다
    /// (R6의 `장면 밖`과 같은 규율).
    /// </summary>
    private static IEnumerable<GraphOutputPortProjection> ChoiceSlots(
        StoryNode node, StoryFile file, StoryProject project)
    {
        if (node is not DialogueNode dialogue ||
            project.Chapters.FirstOrDefault(item =>
                string.Equals(item.ChapterId, file.Name, StringComparison.Ordinal)) is not { } chapter)
        {
            yield break;
        }

        // 노드 → 에피소드는 표식이 먼저고 없으면 이름이다 — 대본 탭·장면 묶기와 같은 규칙이다.
        string episodeId = dialogue.ExcelEpisodeId is { Length: > 0 } marked ? marked : dialogue.Name;

        if (!chapter.Episodes.Any(episode =>
                string.Equals(episode.EpisodeId, episodeId, StringComparison.Ordinal)))
        {
            yield break;
        }

        int slot = 0;

        foreach (ChapterEdge edge in chapter.Edges.Where(item =>
                     string.Equals(item.FromEpisodeId, episodeId, StringComparison.Ordinal)))
        {
            yield return Choice(new GraphChoicePort(
                chapter.ChapterId,
                episodeId,
                slot++,
                edge.OptionLabel ?? string.Empty,
                edge.ToEpisodeId,
                NodeOf(file, edge.ToEpisodeId),
                edge.Auto), dialogue.Id);
        }

        for (; slot < ChoiceSlots3; slot++)
        {
            yield return Choice(new GraphChoicePort(
                chapter.ChapterId, episodeId, slot,
                Label: string.Empty, ToEpisodeId: null, ToNodeId: null, IsAuto: false), dialogue.Id);
        }
    }

    private static GraphOutputPortProjection Choice(GraphChoicePort choice, string nodeId) =>
        new($"choice:{choice.Slot}",
            GraphOutputPortKind.Choice,
            nodeId,
            choice.Label,
            choice.Slot,
            !choice.IsEmpty,
            ExecutionPort: null,
            choice);

    /// <summary>
    /// 그 에피소드의 대사 노드 — <b>같은 판에서만</b> 찾는다.
    ///
    /// ⚠ 프로젝트 전체를 이름으로 훑으면 다른 챕터의 같은 Id가 걸린다(개명이 그 함정을
    /// 이미 한 번 밟았다 — 2026-08-25). 못 찾아도 간선은 있다: <b>대본이 없는 것과 길이
    /// 없는 것은 다르다.</b>
    /// </summary>
    private static string? NodeOf(StoryFile file, string episodeId) =>
        file.Nodes.OfType<DialogueNode>().FirstOrDefault(node =>
            string.Equals(
                node.ExcelEpisodeId is { Length: > 0 } marked ? marked : node.Name,
                episodeId,
                StringComparison.Ordinal))?.Id;

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

                // 챕터 에피소드 표식 — 카드만 봐도 자유 씬과 갈린다. 줄 수는 타임라인 읽기의
                // 눈금이다(T1) — 어느 에피소드가 무거운지 카드에서 보인다.
                //
                // ⚠ 옛 문구는 "📄 엑셀"이었고 뜻은 <b>"이 본문은 엑셀 소유라 잠겼다"</b>였다.
                //    2026-09-16에 그 잠금이 사라지면서(R-E) 남은 뜻은 <b>소속</b>뿐이다 —
                //    "엑셀"이라고 적어 두면 카드가 없는 잠금을 계속 말하게 된다.
                if (dialogue.ExcelEpisodeId is null)
                {
                    return badge;
                }

                int lineCount = project.FindScript(dialogue.ScriptId)?.ActiveLines.Count() ?? 0;
                return $"📄 에피소드 · {badge} · {lineCount}줄";
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

    /// <summary>
    /// 접힌 판의 행들을 <b>장면으로 묶는다</b> (R6 S-3 · 2026-09-16).
    ///
    /// ⛔ <b>장면은 여기서도 엔티티가 아니다</b> — 노드에서 에피소드로, 에피소드에서
    /// <c>SceneId</c>로 가는 투영이다. 판 이름이 챕터 Id와 같은 것이 그 다리이고, 그것이
    /// <c>EnsureChapterBoard</c>가 세우는 규약이다.
    ///
    /// ⚠ <b>묶지 않는 경우 둘</b>: ① 챕터를 못 찾는 판(작가의 자유 판) ② 장면ID를 하나도
    /// 안 적은 챕터. ②는 대본 탭 트리와 <b>같은 규칙</b>이다 — 그대로 묶으면 에피소드 수만큼
    /// 장면 머리글이 생겨 프록시가 통째로 노이즈가 된다(<c>docs/plans/R6-explorer.md</c> §2).
    ///
    /// ⚠ <b>자유 씬은 장면 밖이다.</b> 에피소드가 아닌 대사노드·설정·연출 노드는 챕터의
    /// 진행에 안 실리므로 장면 경계도 없다 — 맨 뒤 묶음으로 모은다.
    /// </summary>
    private static IReadOnlyList<CollapsedSceneGroup> SceneGroups(
        StoryProject project, StoryFile file, IReadOnlyList<CollapsedNodeEntry> entries)
    {
        ChapterDocument? chapter = project.Chapters.FirstOrDefault(item =>
            string.Equals(item.ChapterId, file.Name, StringComparison.Ordinal));

        if (chapter is null)
        {
            return [Single(entries)];
        }

        IReadOnlyList<ChapterScene> scenes = ChapterSceneGrouping.Of(
            chapter.ToGraphModel(chapter.ChapterId));

        if (scenes.Count == 0 || scenes.All(scene => scene.IsDefault))
        {
            return [Single(entries)];
        }

        // 노드 → 에피소드는 이름이나 엑셀 표식으로 잇는다 — 대본 탭이 쓰는 규칙과 같아야
        // 한다(아니면 같은 에피소드를 두 화면이 다르게 짚는다).
        var sceneOf = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (ChapterScene scene in scenes)
        {
            foreach (ChapterEpisode episode in scene.Episodes)
            {
                sceneOf[episode.EpisodeId] = scene.SceneId;
            }
        }

        string? SceneFor(CollapsedNodeEntry entry) =>
            project.FindNode(entry.NodeId) is DialogueNode { ExcelEpisodeId: { } episodeId } &&
            sceneOf.TryGetValue(episodeId, out string? sceneId)
                ? sceneId
                : sceneOf.TryGetValue(entry.NodeName, out string? byName) ? byName : null;

        var groups = new List<CollapsedSceneGroup>();

        foreach (ChapterScene scene in scenes)
        {
            List<CollapsedNodeEntry> inScene = entries
                .Where(entry => string.Equals(SceneFor(entry), scene.SceneId, StringComparison.Ordinal))
                .ToList();

            if (inScene.Count > 0)
            {
                groups.Add(new CollapsedSceneGroup(scene.SceneId, scene.DisplayName, inScene));
            }
        }

        List<CollapsedNodeEntry> outside = entries.Where(entry => SceneFor(entry) is null).ToList();

        if (outside.Count > 0)
        {
            groups.Add(new CollapsedSceneGroup(string.Empty, "장면 밖", outside));
        }

        return groups;
    }

    private static CollapsedSceneGroup Single(IReadOnlyList<CollapsedNodeEntry> entries) =>
        new(string.Empty, string.Empty, entries);

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
