using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Graph;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

/// <summary>
/// PresentationNode가 Dialogue 내용을 복제하지 않고 안정된 LineId와 ordered command만
/// 소유한다는 계약. 대상 줄이 사라져도 binding은 자료 손실 없이 orphan으로 남는다.
/// </summary>
public class PresentationNodeTests
{
    [Fact]
    public void PresentationNode는_대사를_복사하지_않고_LineId만_참조한다()
    {
        var context = BuildContext();
        LineBox line = context.Editor.AddLine(context.DialogueA.Id);
        context.Editor.SetLineText(context.DialogueA.Id, line.Id, "라루", "원래 대사");
        PresentationLineBinding binding = context.Editor.AddPresentationBinding(
            context.PresentationA.Id,
            line.Id);

        context.Editor.SetLineText(context.DialogueA.Id, line.Id, "라루", "수정된 대사");

        Assert.Equal(line.Id, binding.LineId);
        Assert.Equal("수정된 대사", context.DialogueA.Lines.Single().Text);
        Assert.Empty(binding.Commands);
        Assert.Null(typeof(PresentationLineBinding).GetProperty("Text"));
        Assert.Null(typeof(PresentationLineBinding).GetProperty("Speaker"));
    }

    [Fact]
    public void Presentation_command는_작성_순서와_이동_순서를_저장한다()
    {
        var context = BuildContext();
        LineBox line = context.Editor.AddLine(context.DialogueA.Id);
        PresentationCommandInstance first = context.Editor.AddPresentationCommand(
            context.PresentationA.Id,
            line.Id,
            "camera.closeup");
        PresentationCommandInstance second = context.Editor.AddPresentationCommand(
            context.PresentationA.Id,
            line.Id,
            "acting.smile");
        PresentationCommandInstance third = context.Editor.AddPresentationCommand(
            context.PresentationA.Id,
            line.Id,
            "screen.flash");

        context.Editor.MovePresentationCommand(context.PresentationA.Id, third.Id, delta: -2);

        Assert.Equal(
            new[] { third.Id, first.Id, second.Id },
            context.PresentationA.Bindings.Single().Commands.Select(command => command.Id));

        StoryProject restored = ProjectSnapshotCodec.Decode(
            ProjectSnapshotCodec.Encode(context.Project));
        PresentationNode restoredPresentation = Assert.IsType<PresentationNode>(
            restored.FindNode(context.PresentationA.Id));

        Assert.Equal(
            new[] { third.Id, first.Id, second.Id },
            restoredPresentation.Bindings.Single().Commands.Select(command => command.Id));
    }

    [Fact]
    public void PresentationNode_하나는_Dialogue_하나만_대상으로_가지고_Dialogue에는_여러_연출이_붙는다()
    {
        var context = BuildContext();
        PresentationNode presentationB = context.Editor.AddPresentationNode(
            context.File.Id,
            name: "연출 B");

        NodeLink firstLink = context.Editor.SetPresentationTarget(
            context.PresentationA.Id,
            context.DialogueA.Id)!;
        NodeLink movedLink = context.Editor.SetPresentationTarget(
            context.PresentationA.Id,
            context.DialogueB.Id)!;
        NodeLink secondPresentationLink = context.Editor.SetPresentationTarget(
            presentationB.Id,
            context.DialogueB.Id)!;

        Assert.Same(firstLink, movedLink);
        Assert.Equal(context.DialogueB.Id, movedLink.TargetNodeId);
        Assert.Single(context.Project.Links, link =>
            link.Kind == NodeLinkKind.Presentation &&
            link.SourceNodeId == context.PresentationA.Id);
        Assert.Equal(2, context.Project.Links.Count(link =>
            link.Kind == NodeLinkKind.Presentation &&
            link.TargetNodeId == context.DialogueB.Id));
        Assert.NotEqual(movedLink.SourceNodeId, secondPresentationLink.SourceNodeId);
    }

    [Fact]
    public void Dialogue_줄을_삭제해도_binding은_삭제되지_않고_orphan으로_보존된다()
    {
        var context = BuildContext();
        LineBox line = context.Editor.AddLine(context.DialogueA.Id);
        context.Editor.SetPresentationTarget(context.PresentationA.Id, context.DialogueA.Id);
        PresentationCommandInstance command = context.Editor.AddPresentationCommand(
            context.PresentationA.Id,
            line.Id,
            "acting.surprised");

        context.Editor.RemoveLine(context.DialogueA.Id, line.Id);

        PresentationLineBinding binding = Assert.Single(context.PresentationA.Bindings);
        Assert.Equal(line.Id, binding.LineId);
        Assert.Equal(command.Id, Assert.Single(binding.Commands).Id);

        ResolvedPresentationBinding resolved = Assert.Single(
            PresentationBindingResolver.Resolve(context.Project, context.PresentationA));
        Assert.True(resolved.IsOrphan);
        Assert.Null(resolved.Line);
        Assert.Equal(context.DialogueA.Id, resolved.DialogueNodeId);
    }

    [Fact]
    public void Dialogue_줄_순서가_바뀌어도_binding은_같은_LineId를_따라간다()
    {
        var context = BuildContext();
        LineBox first = context.Editor.AddLine(context.DialogueA.Id);
        LineBox second = context.Editor.AddLine(context.DialogueA.Id);
        context.Editor.SetPresentationTarget(context.PresentationA.Id, context.DialogueA.Id);
        context.Editor.AddPresentationBinding(context.PresentationA.Id, second.Id);

        context.Editor.MoveLine(context.DialogueA.Id, second.Id, delta: -1);

        ResolvedPresentationBinding resolved = Assert.Single(
            PresentationBindingResolver.Resolve(context.Project, context.PresentationA));
        Assert.False(resolved.IsOrphan);
        Assert.Same(second, resolved.Line);
        Assert.Equal(new[] { second.Id, first.Id }, context.DialogueA.Lines.Select(line => line.Id));
    }

    [Fact]
    public void PresentationNode와_link는_StoryFile_manifest_snapshot을_왕복한다()
    {
        var context = BuildContext();
        LineBox line = context.Editor.AddLine(context.DialogueA.Id);
        context.Editor.SetPresentationTarget(context.PresentationA.Id, context.DialogueA.Id);
        PresentationCommandInstance command = context.Editor.AddPresentationCommand(
            context.PresentationA.Id,
            line.Id,
            "camera.focus",
            new Dictionary<string, string>
            {
                ["zTarget"] = "hero",
                ["aDuration"] = "0.4"
            },
            note: "눈으로 이동");
        context.Editor.SetPresentationCommandEnabled(
            context.PresentationA.Id,
            command.Id,
            enabled: false);

        string storyJson = StoryFileJson.Write(context.File);
        string manifestJson = ProjectManifestJson.Write(context.Project);
        StoryProject restored = ProjectSnapshotCodec.Decode(
            ProjectSnapshotCodec.Encode(context.Project));

        Assert.Contains("\"kind\": \"presentation\"", storyJson, StringComparison.Ordinal);
        Assert.Contains("\"aDuration\"", storyJson, StringComparison.Ordinal);
        Assert.Contains("\"zTarget\"", storyJson, StringComparison.Ordinal);
        Assert.True(
            storyJson.IndexOf("\"aDuration\"", StringComparison.Ordinal) <
            storyJson.IndexOf("\"zTarget\"", StringComparison.Ordinal));
        Assert.Contains("\"kind\": \"presentation\"", manifestJson, StringComparison.Ordinal);

        PresentationNode restoredNode = Assert.IsType<PresentationNode>(
            restored.FindNode(context.PresentationA.Id));
        PresentationCommandInstance restoredCommand = Assert.Single(
            Assert.Single(restoredNode.Bindings).Commands);
        Assert.Equal(command.Id, restoredCommand.Id);
        Assert.False(restoredCommand.IsEnabled);
        Assert.Equal("눈으로 이동", restoredCommand.Note);
        Assert.Equal("0.4", restoredCommand.Arguments["aDuration"]);

        NodeLink restoredLink = Assert.Single(restored.Links, link =>
            link.Kind == NodeLinkKind.Presentation);
        Assert.Equal(context.PresentationA.Id, restoredLink.SourceNodeId);
        Assert.Equal(context.DialogueA.Id, restoredLink.TargetNodeId);
    }

    [Fact]
    public void 저장은_Presentation_link_타입과_소스당_하나_제약을_검증한다()
    {
        var context = BuildContext();
        context.Project.Links.Add(new NodeLink(
            "lk_wrong",
            NodeLinkKind.Presentation,
            context.DialogueA.Id,
            context.DialogueB.Id));

        Assert.Throws<InvalidDataException>(() => ProjectSnapshotCodec.Encode(context.Project));

        context.Project.Links.Clear();
        context.Project.Links.Add(new NodeLink(
            "lk_one",
            NodeLinkKind.Presentation,
            context.PresentationA.Id,
            context.DialogueA.Id));
        context.Project.Links.Add(new NodeLink(
            "lk_two",
            NodeLinkKind.Presentation,
            context.PresentationA.Id,
            context.DialogueB.Id));

        Assert.Throws<InvalidDataException>(() => ProjectSnapshotCodec.Encode(context.Project));
    }

    [Fact]
    public void GraphProjection은_PresentationNode와_연출_공급_간선을_별도로_표시한다()
    {
        var context = BuildContext();
        context.Editor.SetPresentationTarget(context.PresentationA.Id, context.DialogueA.Id);

        GraphProjection projection = GraphProjectionBuilder.Build(
            context.Project,
            new HashSet<string>(new[] { context.File.Id }, StringComparer.Ordinal));

        ExpandedNodeProjection presentation = projection.Items
            .OfType<ExpandedNodeProjection>()
            .Single(item => item.NodeId == context.PresentationA.Id);
        GraphOutputPortProjection port = Assert.Single(presentation.OutputPorts);
        GraphConnectionProjection connection = Assert.Single(projection.Connections, item =>
            item.Kind == GraphConnectionKind.Presentation);

        Assert.Equal(GraphNodeKind.Presentation, presentation.NodeKind);
        Assert.Equal(GraphOutputPortKind.Presentation, port.Kind);
        Assert.True(port.IsConnected);
        Assert.Equal(context.PresentationA.Id, connection.SourceNodeId);
        Assert.Equal(context.DialogueA.Id, connection.TargetNodeId);
        Assert.Equal(GraphEndpointKind.ExpandedNodeOutput, connection.Source.Kind);
        Assert.Equal(GraphEndpointKind.ExpandedNodeInput, connection.Target.Kind);
    }

    [Fact]
    public void PresentationNode만_먼저_추가해도_시작_노드가_되지_않는다()
    {
        var project = new StoryProject();
        var file = new StoryFile("sf_only", "연출 먼저");
        project.Files.Add(file);
        var editor = new Vn.Authoring.Editing.ProjectEditor(project);

        editor.AddPresentationNode(file.Id);
        Assert.Null(project.StartNodeId);

        DialogueNode dialogue = editor.AddDialogueNode(file.Id);
        Assert.Equal(dialogue.Id, project.StartNodeId);
    }

    [Fact]
    public void Presentation_Command_추가와_드롭다운_변경은_Content_변경으로_알린다()
    {
        var context = BuildContext();
        LineBox line = context.Editor.AddLine(context.DialogueA.Id);
        ProjectChangedEventArgs? change = null;
        context.Editor.Changed += (_, args) => change = args;

        PresentationCommandInstance command = context.Editor.AddPresentationCommand(
            context.PresentationA.Id,
            line.Id,
            "camera.closeup");

        Assert.Equal(ProjectChangeKind.PresentationContent, change!.Kind);

        change = null;
        context.Editor.SetPresentationCommandDefinition(
            context.PresentationA.Id,
            command.Id,
            "camera.wide",
            new Dictionary<string, string> { ["preset"] = "wide" });

        Assert.Equal(ProjectChangeKind.PresentationContent, change!.Kind);
        Assert.Equal("camera.wide", command.DefinitionId);
        Assert.Equal("wide", command.Arguments["preset"]);
    }

    private static PresentationContext BuildContext()
    {
        var project = new StoryProject { Title = "Presentation" };
        var file = new StoryFile("sf_presentation", "연출", "story/presentation.vnstory.json");
        project.Files.Add(file);
        var editor = new Vn.Authoring.Editing.ProjectEditor(project);
        DialogueNode dialogueA = editor.AddDialogueNode(file.Id, name: "대사 A");
        dialogueA.Lines.Clear();
        DialogueNode dialogueB = editor.AddDialogueNode(file.Id, name: "대사 B");
        dialogueB.Lines.Clear();
        PresentationNode presentationA = editor.AddPresentationNode(file.Id, name: "연출 A");

        return new PresentationContext(
            project,
            file,
            editor,
            dialogueA,
            dialogueB,
            presentationA);
    }

    private sealed record PresentationContext(
        StoryProject Project,
        StoryFile File,
        Vn.Authoring.Editing.ProjectEditor Editor,
        DialogueNode DialogueA,
        DialogueNode DialogueB,
        PresentationNode PresentationA);
}
