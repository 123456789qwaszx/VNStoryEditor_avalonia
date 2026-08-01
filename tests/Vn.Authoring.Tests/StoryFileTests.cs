using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests;

public class StoryFileTests
{
    [Fact]
    public void 프로젝트_전체_노드_순서는_파일_순서와_파일_내_순서를_따른다()
    {
        var project = new StoryProject();
        var first = new StoryFile("sf_a", "A");
        var second = new StoryFile("sf_b", "B");
        project.Files.Add(first);
        project.Files.Add(second);
        var editor = new ProjectEditor(project);

        DialogueNode a1 = editor.AddDialogueNode(first.Id, name: "A1");
        DialogueNode a2 = editor.AddDialogueNode(first.Id, name: "A2");
        DialogueNode b1 = editor.AddDialogueNode(second.Id, name: "B1");

        Assert.Equal(
            new[] { a1.Id, a2.Id, b1.Id },
            project.EnumerateNodes().Select(node => node.Id));
    }

    [Fact]
    public void 새_노드는_지정한_파일의_마지막에만_추가된다()
    {
        var project = new StoryProject();
        var first = new StoryFile("sf_a", "A");
        var second = new StoryFile("sf_b", "B");
        project.Files.Add(first);
        project.Files.Add(second);
        var editor = new ProjectEditor(project);

        DialogueNode existing = editor.AddDialogueNode(first.Id, name: "기존");
        DialogueNode added = editor.AddDialogueNode(second.Id, name: "새 노드");

        Assert.Equal(new[] { existing.Id }, first.Nodes.Select(node => node.Id));
        Assert.Equal(new[] { added.Id }, second.Nodes.Select(node => node.Id));
        Assert.Same(added, second.Nodes[^1]);
    }

    [Fact]
    public void 노드를_다른_파일로_옮겨도_Id와_연결이_유지된다()
    {
        var project = new StoryProject();
        var first = new StoryFile("sf_a", "A");
        var second = new StoryFile("sf_b", "B");
        project.Files.Add(first);
        project.Files.Add(second);
        var editor = new ProjectEditor(project);

        DialogueNode source = editor.AddDialogueNode(first.Id, name: "출발");
        DialogueNode moved = editor.AddDialogueNode(first.Id, name: "이동 대상");
        editor.SetExitTarget(source.Id, Vn.Authoring.Flow.ExitPortKind.Default, null, moved.Id);

        editor.MoveNodeToFile(moved.Id, second.Id);

        Assert.DoesNotContain(first.Nodes, node => node.Id == moved.Id);
        Assert.Same(moved, Assert.Single(second.Nodes));
        Assert.Equal(moved.Id, source.DefaultExitTargetNodeId);
        Assert.Same(second, project.FindFileContainingNode(moved.Id));
    }

    [Fact]
    public void 같은_파일_안에서도_노드_순서를_옮길_수_있다()
    {
        var project = new StoryProject();
        var file = new StoryFile("sf_a", "A");
        project.Files.Add(file);
        var editor = new ProjectEditor(project);

        DialogueNode first = editor.AddDialogueNode(file.Id, name: "첫째");
        DialogueNode second = editor.AddDialogueNode(file.Id, name: "둘째");
        DialogueNode third = editor.AddDialogueNode(file.Id, name: "셋째");

        editor.MoveNodeToFile(first.Id, file.Id, targetIndex: file.Nodes.Count);

        Assert.Equal(
            new[] { second.Id, third.Id, first.Id },
            file.Nodes.Select(node => node.Id));
    }

    [Fact]
    public void 파일이_달라도_실행_간선은_프로젝트_전체에서_계산된다()
    {
        var project = new StoryProject();
        var first = new StoryFile("sf_a", "A");
        var second = new StoryFile("sf_b", "B");
        project.Files.Add(first);
        project.Files.Add(second);
        var editor = new ProjectEditor(project);

        DialogueNode source = editor.AddDialogueNode(first.Id, name: "출발");
        DialogueNode target = editor.AddDialogueNode(second.Id, name: "도착");
        editor.SetExitTarget(source.Id, Vn.Authoring.Flow.ExitPortKind.Default, null, target.Id);

        Vn.Authoring.Flow.ExitPort edge = Assert.Single(
            Vn.Authoring.Flow.NodeConnections.AllConnections(project));

        Assert.Equal(source.Id, edge.NodeId);
        Assert.Equal(target.Id, edge.TargetNodeId);
    }

    [Fact]
    public void 노드_삭제는_소유_파일에서_제거하고_파일_간_출구도_정리한다()
    {
        var project = new StoryProject();
        var first = new StoryFile("sf_a", "A");
        var second = new StoryFile("sf_b", "B");
        project.Files.Add(first);
        project.Files.Add(second);
        var editor = new ProjectEditor(project);

        DialogueNode source = editor.AddDialogueNode(first.Id, name: "출발");
        DialogueNode target = editor.AddDialogueNode(second.Id, name: "도착");
        editor.SetExitTarget(source.Id, Vn.Authoring.Flow.ExitPortKind.Default, null, target.Id);

        editor.RemoveNode(target.Id);

        Assert.Empty(second.Nodes);
        Assert.Null(source.DefaultExitTargetNodeId);
        Assert.Null(project.FindNode(target.Id));
    }

    [Fact]
    public void 프로젝트_전체에서_중복된_노드_Id는_추가할_수_없다()
    {
        var project = new StoryProject();
        var first = new StoryFile("sf_a", "A");
        var second = new StoryFile("sf_b", "B");
        project.Files.Add(first);
        project.Files.Add(second);
        var editor = new ProjectEditor(project);

        editor.AddNode(first.Id, new DialogueNode("nd_same", "첫 노드"));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => editor.AddNode(second.Id, new SetNode("nd_same", "중복 노드")));

        Assert.Contains("중복", error.Message, StringComparison.Ordinal);
        Assert.Empty(second.Nodes);
    }
}
