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
