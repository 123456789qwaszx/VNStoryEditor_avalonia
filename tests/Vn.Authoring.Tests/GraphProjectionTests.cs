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
                Assert.Equal(2, row.IncomingCount);
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

        Assert.Equal(setA.Id, execution.SourceNodeId);
        Assert.Equal(dialogueB.Id, execution.TargetNodeId);
        Assert.Equal(GraphEndpointKind.ExpandedNodeOutput, execution.Source.Kind);
        Assert.Equal(GraphEndpointKind.CollapsedFileNodeInput, execution.Target.Kind);
        Assert.Equal(0, execution.Target.ProxyRowIndex);
    }

    [Fact]
    public void Settings_link도_접힌_파일의_실제_대사_행으로_투영된다()
    {
        var (project, fileA, _, setA, _, dialogueB) = BuildProject();

        GraphProjection projection = GraphProjectionBuilder.Build(
            project,
            new HashSet<string>(new[] { fileA.Id }, StringComparer.Ordinal));

        GraphConnectionProjection settings = projection.Connections.Single(
            connection => connection.Kind == GraphConnectionKind.Settings);

        Assert.Equal(setA.Id, settings.SourceNodeId);
        Assert.Equal(dialogueB.Id, settings.TargetNodeId);
        Assert.NotNull(settings.LinkId);
        Assert.Null(settings.ExecutionPort);
        Assert.Equal(GraphEndpointKind.CollapsedFileNodeInput, settings.Target.Kind);
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

        Assert.Equal(setA.Id, execution.SourceNodeId);
        Assert.Equal(dialogueB.Id, execution.TargetNodeId);
        Assert.Equal(GraphEndpointKind.CollapsedFileNodeOutput, execution.Source.Kind);
        Assert.Equal(0, execution.Source.ProxyRowIndex);
        Assert.Equal(GraphEndpointKind.ExpandedNodeInput, execution.Target.Kind);
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

        Assert.Equal(setA.Id, execution.Source.NodeId);
        Assert.Equal(dialogueB.Id, execution.Target.NodeId);
        Assert.Equal(GraphEndpointKind.CollapsedFileNodeOutput, execution.Source.Kind);
        Assert.Equal(GraphEndpointKind.CollapsedFileNodeInput, execution.Target.Kind);
    }

    [Fact]
    public void 펼쳐진_SetNode에는_실행_출구와_Settings_출구가_별도_포트로_보인다()
    {
        var (project, fileA, _, setA, _, _) = BuildProject();

        GraphProjection projection = GraphProjectionBuilder.Build(
            project,
            new HashSet<string>(new[] { fileA.Id }, StringComparer.Ordinal));

        ExpandedNodeProjection node = projection.Items
            .OfType<ExpandedNodeProjection>()
            .Single(item => item.NodeId == setA.Id);

        Assert.Collection(
            node.OutputPorts,
            port => Assert.Equal(GraphOutputPortKind.ExecutionDefault, port.Kind),
            port => Assert.Equal(GraphOutputPortKind.Settings, port.Kind));
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
        Assert.Equal(2, all.Connections.Count);

        // SetNode를 숨기면 그 노드에서 나가는 실행·Settings 간선이 전부 사라진다.
        GraphProjection filtered = GraphProjectionBuilder.Build(
            project,
            new HashSet<string>(new[] { fileA.Id, fileB.Id }, StringComparer.Ordinal),
            definition: null,
            new GraphFilter(ShowSet: false));

        Assert.Empty(filtered.Connections);
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

        // fileA를 접는다. 필터로 fileA의 SetNode가 빠지면... SetNode가 간선의 끝이므로
        // 간선 자체가 사라져야 하고, 남은 행 번호는 필터 후 기준이어야 한다.
        GraphProjection projection = GraphProjectionBuilder.Build(
            project,
            new HashSet<string>(StringComparer.Ordinal),
            definition: null,
            new GraphFilter(ShowSet: false));

        CollapsedFileProjection proxyA = projection.Items
            .OfType<CollapsedFileProjection>()
            .First();

        // SetNode가 빠졌으므로 dialogueA가 0행이다.
        Assert.Equal("nd_a", proxyA.Nodes[0].NodeId);
        Assert.Empty(projection.Connections);
        _ = fileB;
        _ = setA;
        _ = dialogueB;
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
            Layout = new NodeLayout { X = 80, Y = 100 },
            DefaultExitTargetNodeId = "nd_b"
        };
        var dialogueA = new DialogueNode("nd_a", "대사 A")
        {
            Layout = new NodeLayout { X = 340, Y = 100 }
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
