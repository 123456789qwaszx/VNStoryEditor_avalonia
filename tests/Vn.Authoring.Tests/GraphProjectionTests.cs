using Vn.Authoring.Graph;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests;

public class GraphProjectionTests
{
    [Fact]
    public void 펼친_파일은_실제_노드이고_접힌_파일은_FileProxy다()
    {
        var (project, fileA, fileB, setA, _, dialogueB) = BuildProject();

        GraphProjection projection = GraphProjectionBuilder.Build(
            project,
            new HashSet<string>(new[] { fileA.Id }, StringComparer.Ordinal));

        Assert.Collection(
            projection.Items,
            first => Assert.Equal(setA.Id, Assert.IsType<ExpandedNodeProjection>(first).NodeId),
            second => Assert.IsType<ExpandedNodeProjection>(second),
            third =>
            {
                CollapsedFileProjection proxy = Assert.IsType<CollapsedFileProjection>(third);
                Assert.Equal(fileB.Id, proxy.FileId);
                CollapsedNodeEntry row = Assert.Single(proxy.Nodes);
                Assert.Equal(dialogueB.Id, row.NodeId);
                Assert.Equal(1, row.IncomingCount); // 실행 간선 하나 — 조건 공급선은 안 그린다
            });
    }

    [Fact]
    public void 펼친_노드에서_접힌_파일로_가는_간선은_실제_NodeId를_유지한다()
    {
        var (project, fileA, _, setA, _, dialogueB) = BuildProject();

        GraphProjection projection = GraphProjectionBuilder.Build(
            project,
            new HashSet<string>(new[] { fileA.Id }, StringComparer.Ordinal));

        GraphConnectionProjection execution = projection.Connections.Single(
            connection => connection.Kind == GraphConnectionKind.ExecutionDefault);

        Assert.Equal("nd_a", execution.SourceNodeId);
        Assert.Equal(dialogueB.Id, execution.TargetNodeId);
        Assert.Equal(GraphEndpointKind.ExpandedNodeOutput, execution.Source.Kind);
        Assert.Equal(GraphEndpointKind.CollapsedFileNodeInput, execution.Target.Kind);
        Assert.Equal(0, execution.Target.ProxyRowIndex);
        _ = setA;
    }

    [Fact]
    public void 조건_공급선은_그리지_않는다()
    {
        // 2026-08-17 소유자 — 작가의 조건·변수는 챕터 단위 전역이다. 범위가 판 전체이므로
        // 선을 그리면 대사노드 수만큼 거미줄이 되고, 그 선이 무엇을 정하지도 않는다.
        // 구판 프로젝트에 남은 Settings 링크 데이터는 조용히 무시된다.
        var (project, fileA, _, _, _, _) = BuildProject();

        GraphProjection projection = GraphProjectionBuilder.Build(
            project,
            new HashSet<string>(new[] { fileA.Id }, StringComparer.Ordinal));

        Assert.DoesNotContain(projection.Connections,
            connection => connection.Kind == GraphConnectionKind.Settings);
    }

    [Fact]
    public void 소스_파일이_접히면_간선_시작점도_그_노드_행으로_투영된다()
    {
        var (project, _, fileB, setA, _, dialogueB) = BuildProject();

        GraphProjection projection = GraphProjectionBuilder.Build(
            project,
            new HashSet<string>(new[] { fileB.Id }, StringComparer.Ordinal));

        GraphConnectionProjection execution = projection.Connections.Single(
            connection => connection.Kind == GraphConnectionKind.ExecutionDefault);

        Assert.Equal("nd_a", execution.SourceNodeId);
        Assert.Equal(dialogueB.Id, execution.TargetNodeId);
        Assert.Equal(GraphEndpointKind.CollapsedFileNodeOutput, execution.Source.Kind);
        Assert.Equal(1, execution.Source.ProxyRowIndex);
        Assert.Equal(GraphEndpointKind.ExpandedNodeInput, execution.Target.Kind);
        _ = setA;
    }

    [Fact]
    public void 두_파일이_모두_접혀도_각_FileProxy의_실제_노드_행끼리_연결된다()
    {
        var (project, _, _, setA, _, dialogueB) = BuildProject();

        GraphProjection projection = GraphProjectionBuilder.Build(
            project,
            new HashSet<string>(StringComparer.Ordinal));

        GraphConnectionProjection execution = projection.Connections.Single(
            connection => connection.Kind == GraphConnectionKind.ExecutionDefault);

        Assert.Equal("nd_a", execution.Source.NodeId);
        Assert.Equal(dialogueB.Id, execution.Target.NodeId);
        Assert.Equal(GraphEndpointKind.CollapsedFileNodeOutput, execution.Source.Kind);
        Assert.Equal(GraphEndpointKind.CollapsedFileNodeInput, execution.Target.Kind);
        _ = setA;
    }

    [Fact]
    public void 펼쳐진_SetNode에는_Settings_공급_포트만_보인다()
    {
        // SetNode는 조건 공급자다 — 실행 출구 포트는 없다.
        var (project, fileA, _, setA, _, _) = BuildProject();

        GraphProjection projection = GraphProjectionBuilder.Build(
            project,
            new HashSet<string>(new[] { fileA.Id }, StringComparer.Ordinal));

        ExpandedNodeProjection node = projection.Items
            .OfType<ExpandedNodeProjection>()
            .Single(item => item.NodeId == setA.Id);

        GraphOutputPortProjection port = Assert.Single(node.OutputPorts);
        Assert.Equal(GraphOutputPortKind.Settings, port.Kind);
    }

    [Fact]
    public void 필터로_숨은_노드는_카드에서도_접힌_행에서도_빠진다()
    {
        var (project, fileA, _, setA, dialogueA, dialogueB) = BuildProject();

        GraphProjection projection = GraphProjectionBuilder.Build(
            project,
            new HashSet<string>(new[] { fileA.Id }, StringComparer.Ordinal),
            definition: null,
            new GraphFilter(ShowSet: false));

        // 펼친 파일: SetNode 카드가 빠진다.
        Assert.DoesNotContain(
            projection.Items.OfType<ExpandedNodeProjection>(),
            item => item.NodeId == setA.Id);
        Assert.Contains(
            projection.Items.OfType<ExpandedNodeProjection>(),
            item => item.NodeId == dialogueA.Id);

        // 접힌 파일의 행 목록도 같은 필터를 지난다 (B 파일에는 Dialogue만 있어 그대로).
        CollapsedFileProjection proxy = projection.Items.OfType<CollapsedFileProjection>().Single();
        Assert.Equal(dialogueB.Id, Assert.Single(proxy.Nodes).NodeId);
    }

    [Fact]
    public void 간선_정합_한쪽_끝이_숨으면_간선도_숨는다()
    {
        var (project, fileA, fileB, _, _, _) = BuildProject();

        GraphProjection all = GraphProjectionBuilder.Build(
            project,
            new HashSet<string>(new[] { fileA.Id, fileB.Id }, StringComparer.Ordinal));
        Assert.Single(all.Connections); // 조건 공급선은 안 그린다 (2026-08-17 — 범위가 판 전체)

        // SetNode를 숨겨도 실행 간선은 그대로 — 조건 공급선은 애초에 없다.
        GraphProjection filtered = GraphProjectionBuilder.Build(
            project,
            new HashSet<string>(new[] { fileA.Id, fileB.Id }, StringComparer.Ordinal),
            definition: null,
            new GraphFilter(ShowSet: false));

        GraphConnectionProjection remaining = Assert.Single(filtered.Connections);
        Assert.Equal(GraphConnectionKind.ExecutionDefault, remaining.Kind);
    }

    [Fact]
    public void 흐름_보기는_대사_노드와_실행_간선만_남긴다()
    {
        var (project, fileA, fileB, _, dialogueA, dialogueB) = BuildProject();
        dialogueA.DefaultExitTargetNodeId = dialogueB.Id;

        GraphProjection projection = GraphProjectionBuilder.Build(
            project,
            new HashSet<string>(new[] { fileA.Id, fileB.Id }, StringComparer.Ordinal),
            definition: null,
            GraphFilter.FlowOnly);

        Assert.All(
            projection.Items.OfType<ExpandedNodeProjection>(),
            item => Assert.Equal(GraphNodeKind.Dialogue, item.NodeKind));
        GraphConnectionProjection connection = Assert.Single(projection.Connections);
        Assert.Equal(GraphConnectionKind.ExecutionDefault, connection.Kind);
        Assert.Equal(dialogueA.Id, connection.SourceNodeId);
    }

    [Fact]
    public void 필터로_행이_빠진_FileProxy의_간선은_남은_행_번호로_다시_붙는다()
    {
        var (project, _, fileB, setA, _, dialogueB) = BuildProject();

        // 두 파일 모두 접은 채 SetNode를 숨긴다. proxyA에서 SetNode 행이 빠지므로
        // dialogueA가 0행이 되고, 실행 간선의 끝도 그 새 행 번호에 붙어야 한다.
        GraphProjection projection = GraphProjectionBuilder.Build(
            project,
            new HashSet<string>(StringComparer.Ordinal),
            definition: null,
            new GraphFilter(ShowSet: false));

        CollapsedFileProjection proxyA = projection.Items
            .OfType<CollapsedFileProjection>()
            .First();

        Assert.Equal("nd_a", Assert.Single(proxyA.Nodes).NodeId);

        GraphConnectionProjection execution = Assert.Single(projection.Connections);
        Assert.Equal("nd_a", execution.Source.NodeId);
        Assert.Equal(0, execution.Source.ProxyRowIndex);
        Assert.Equal(dialogueB.Id, execution.Target.NodeId);
        _ = fileB;
        _ = setA;
    }

    [Fact]
    public void 직교_간선은_수평과_수직_선분만_만든다()
    {
        IReadOnlyList<GraphPosition> points = OrthogonalEdgeRouter.Route(
            new GraphPosition(100, 100),
            new GraphPosition(20, 260));

        Assert.Equal(4, points.Count);

        for (int index = 1; index < points.Count; index++)
        {
            GraphPosition previous = points[index - 1];
            GraphPosition current = points[index];
            Assert.True(previous.X == current.X || previous.Y == current.Y);
        }

        Assert.Equal(new GraphPosition(100, 100), points[0]);
        Assert.Equal(new GraphPosition(20, 260), points[^1]);
        Assert.True(points[1].X > 100);
    }

    private static (
        StoryProject Project,
        StoryFile FileA,
        StoryFile FileB,
        SetNode SetA,
        DialogueNode DialogueA,
        DialogueNode DialogueB) BuildProject()
    {
        var project = new StoryProject { Title = "GraphProjection" };
        var fileA = new StoryFile("sf_a", "A", "story/a.vnstory.json");
        var fileB = new StoryFile("sf_b", "B", "story/b.vnstory.json");
        var setA = new SetNode("nd_set", "설정")
        {
            Layout = new NodeLayout { X = 80, Y = 100 }
        };
        var dialogueA = new DialogueNode("nd_a", "대사 A")
        {
            Layout = new NodeLayout { X = 340, Y = 100 },
            // 실행 흐름은 DialogueNode만 정한다. SetNode는 조건 공급자일 뿐이다.
            DefaultExitTargetNodeId = "nd_b"
        };
        var dialogueB = new DialogueNode("nd_b", "대사 B")
        {
            Layout = new NodeLayout { X = 650, Y = 260 }
        };

        fileA.Nodes.Add(setA);
        fileA.Nodes.Add(dialogueA);
        fileB.Nodes.Add(dialogueB);
        project.Files.Add(fileA);
        project.Files.Add(fileB);
        project.Links.Add(new NodeLink(
            "lk_settings",
            NodeLinkKind.Settings,
            setA.Id,
            dialogueB.Id));

        return (project, fileA, fileB, setA, dialogueA, dialogueB);
    }
}
