using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests;

public class EditingTests
{
    [Fact]
    public void 줄을_옮겨도_정체성이_유지된다()
    {
        var sample = new Sample();
        LineBox first = sample.Line("첫째");
        LineBox second = sample.Line("둘째");
        LineBox third = sample.Line("셋째");

        sample.Editor.MoveLine(sample.Dialogue.Id, third.Id, -2);

        // 화면 순서(Index)는 바뀌었지만 각 줄의 Id는 그대로다.
        Assert.Equal(
            new[] { third.Id, first.Id, second.Id },
            sample.Dialogue.Lines.Select(line => line.Id));

        Assert.Equal("셋째", sample.Dialogue.Lines[0].Text);
    }

    [Fact]
    public void 줄을_옮기면_그_줄이_열던_갈래도_함께_옮겨간다()
    {
        var sample = new Sample();
        sample.Line("바깥");
        LineBox opener = sample.Line("if", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Line("안쪽");
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, opener.Id, sample.TargetA.Id);

        // 여는 줄을 맨 위로 올리면 갈래의 시작도 맨 위가 된다.
        sample.Editor.MoveLine(sample.Dialogue.Id, opener.Id, -1);

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);

        Assert.Equal(0, flow.Branches[0].FirstLineIndex);
        Assert.NotNull(flow.Lines[0].Branch);
        Assert.NotNull(flow.Lines[1].Branch);
        Assert.NotNull(flow.Lines[2].Branch);

        // 출구는 갈래에 매여 있으므로 이동에도 살아남는다.
        Assert.Equal(sample.TargetA.Id, flow.Branches[0].ExitTargetNodeId);
    }

    [Fact]
    public void 새_노드는_파일_순서의_마지막에_생긴다()
    {
        var sample = new Sample();
        int before = sample.Project.Nodes.Count;

        // 그래프에서 왼쪽 위에 놓아도 파일에서는 맨 뒤다.
        DialogueNode added = sample.Editor.AddDialogueNode(x: -500, y: -500, name: "나중에 만든 것");

        Assert.Equal(before + 1, sample.Project.Nodes.Count);
        Assert.Same(added, sample.Project.Nodes[^1]);
        Assert.Equal(-500, added.Layout.X);
    }

    [Fact]
    public void 첫_노드가_시작_노드가_된다()
    {
        var editor = new ProjectEditor(new StoryProject());
        SetNode first = editor.AddSetNode();
        editor.AddDialogueNode();

        Assert.Equal(first.Id, editor.Project.StartNodeId);
    }

    [Fact]
    public void 그래프에서_노드를_옮겨도_파일_순서는_그대로다()
    {
        var sample = new Sample();
        List<string> order = sample.Project.Nodes.Select(node => node.Id).ToList();

        sample.Editor.MoveNode(sample.TargetB.Id, 10, 20);

        Assert.Equal(order, sample.Project.Nodes.Select(node => node.Id));
        Assert.Equal(10, sample.TargetB.Layout.X);
    }

    [Fact]
    public void 되돌리기가_편집을_되살린다()
    {
        var sample = new Sample();
        LineBox line = sample.Line("처음");

        sample.Editor.SetLineText(sample.Dialogue.Id, line.Id, "윌로", "고친 뒤");
        Assert.Equal("고친 뒤", sample.Project.FindDialogue(sample.Dialogue.Id)!.Lines[0].Text);

        sample.Editor.Undo();
        Assert.Equal("처음", sample.Project.FindDialogue(sample.Dialogue.Id)!.Lines[0].Text);

        sample.Editor.Redo();
        Assert.Equal("고친 뒤", sample.Project.FindDialogue(sample.Dialogue.Id)!.Lines[0].Text);
    }

    [Fact]
    public void 노드_이동은_되돌리기_기록을_더럽히지_않는다()
    {
        var sample = new Sample();
        LineBox line = sample.Line("처음");
        sample.Editor.SetLineText(sample.Dialogue.Id, line.Id, string.Empty, "고친 뒤");

        // 드래그 한 번에 수십 번 불리는 값이다. 이것이 쌓이면 되돌리기가 쓸모없어진다.
        for (int step = 0; step < 20; step++)
        {
            sample.Editor.MoveNode(sample.Dialogue.Id, step, step);
        }

        sample.Editor.Undo();
        Assert.Equal("처음", sample.Project.FindDialogue(sample.Dialogue.Id)!.Lines[0].Text);
    }

    [Fact]
    public void 편집할_때마다_알린다()
    {
        var sample = new Sample();
        int notifications = 0;
        sample.Editor.Changed += (_, _) => notifications++;

        LineBox line = sample.Editor.AddLine(sample.Dialogue.Id);
        sample.Editor.SetLineText(sample.Dialogue.Id, line.Id, "윌로", "안녕");
        sample.Editor.SetLineTransition(sample.Dialogue.Id, line.Id, LineConditionTransition.BeginIf(sample.ConditionA.Id));

        Assert.Equal(3, notifications);
    }

    [Fact]
    public void 같은_값으로_다시_설정하면_기록을_남기지_않는다()
    {
        var sample = new Sample();
        LineBox line = sample.Line("처음");
        int notifications = 0;
        sample.Editor.Changed += (_, _) => notifications++;

        sample.Editor.SetLineText(sample.Dialogue.Id, line.Id, string.Empty, "처음");

        Assert.Equal(0, notifications);
    }
}
