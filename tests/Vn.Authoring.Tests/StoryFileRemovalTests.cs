using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests;

/// <summary>
/// W61 — 시나리오 파일(판) 통째 제거. 안 노드가 맺던 연결·출구는 노드 삭제와
/// 같은 규칙 하나로 정리되고, 마지막 파일은 거부되며, 되돌리기 한 번으로 복구된다.
/// </summary>
public class StoryFileRemovalTests
{
    [Fact]
    public void 파일_제거는_연결과_시작_노드를_정리하고_대본은_남긴다()
    {
        var editor = new ProjectEditor(new StoryProject());

        // 첫 노드가 시작점이 된다 — 사라질 판에 시작점을 둔 채 제거해 본다.
        StoryFile doomed = editor.AddStoryFile("1장");
        DialogueNode removed = editor.AddDialogueNode(doomed.Id, name: "사라질 장면");
        string removedScriptId = removed.ScriptId!;

        StoryFile keeper = editor.AddStoryFile("2장");
        DialogueNode survivor = editor.AddDialogueNode(keeper.Id, name: "남는 장면");

        // 판 사이 연결: 남는 노드 → 사라질 노드. 제거 후에는 출구가 정리되어야 한다.
        editor.SetExitTarget(survivor.Id, ExitPortKind.Default, branchOpenLineId: null, targetNodeId: removed.Id);
        Assert.Equal(removed.Id, editor.Project.StartNodeId);

        editor.RemoveStoryFile(doomed.Id);

        Assert.Null(editor.Project.FindFile(doomed.Id));
        Assert.Null(editor.Project.FindNode(removed.Id));
        Assert.Null(editor.Project.FindDialogue(survivor.Id)!.DefaultExitTargetNodeId);
        Assert.DoesNotContain(editor.Project.Links, link =>
            link.TargetNodeId == removed.Id || link.SourceNodeId == removed.Id);
        Assert.Equal(survivor.Id, editor.Project.StartNodeId); // 시작점은 남은 대사 노드로 옮겨 간다
        Assert.NotNull(editor.Project.FindScript(removedScriptId)); // 대본은 지우지 않는다 — 규칙 14
    }

    [Fact]
    public void 마지막_파일은_제거를_거부하고_되돌리기는_통째로_복구한다()
    {
        var editor = new ProjectEditor(new StoryProject());
        StoryFile only = editor.AddStoryFile("1장");
        editor.AddDialogueNode(only.Id, name: "장면");

        Assert.Throws<InvalidOperationException>(() => editor.RemoveStoryFile(only.Id));

        StoryFile second = editor.AddStoryFile("2장");
        DialogueNode node = editor.AddDialogueNode(second.Id, name: "복구될 장면");

        editor.RemoveStoryFile(second.Id);
        Assert.Null(editor.Project.FindFile(second.Id));

        editor.Undo();

        StoryFile restored = Assert.IsType<StoryFile>(editor.Project.FindFile(second.Id));
        Assert.Equal("2장", restored.Name);
        Assert.NotNull(editor.Project.FindNode(node.Id)); // 노드까지 한 번에 돌아온다
    }
}
