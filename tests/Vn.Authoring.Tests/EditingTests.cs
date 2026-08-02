using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Tests;

public class EditingTests
{
    [Fact]
    public void 줄을_옮겨도_정체성이_유지된다()
    {
        var sample = new Sample();
        string first = sample.Line("첫째");
        string second = sample.Line("둘째");
        string third = sample.Line("셋째");

        sample.Editor.MoveScriptLine(sample.Script.Id, third, -2);

        DialogueScript script = DialogueScriptResolver.Resolve(sample.Project, sample.Dialogue);

        // 화면 순서(Index)는 바뀌었지만 각 줄의 Id는 그대로다.
        Assert.Equal(
            new[] { third, first, second },
            script.Lines.Select(line => line.LineId));

        Assert.Equal("셋째", script.Lines[0].Text);
    }

    [Fact]
    public void 줄을_옮기면_그_줄이_열던_갈래도_함께_옮겨간다()
    {
        var sample = new Sample();
        sample.Line("바깥");
        string opener = sample.Line("if", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Line("안쪽");
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, opener, sample.TargetA.Id);

        // 여는 줄을 맨 위로 올리면 갈래의 시작도 맨 위가 된다.
        sample.Editor.MoveScriptLine(sample.Script.Id, opener, -1);

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
        int before = sample.File.Nodes.Count;

        // 그래프에서 왼쪽 위에 놓아도 지정한 파일에서는 맨 뒤다.
        DialogueNode added = sample.Editor.AddDialogueNode(
            sample.File.Id,
            x: -500,
            y: -500,
            name: "나중에 만든 것");

        Assert.Equal(before + 1, sample.File.Nodes.Count);
        Assert.Same(added, sample.File.Nodes[^1]);
        Assert.Equal(-500, added.Layout.X);
    }

    [Fact]
    public void 첫_노드가_시작_노드가_된다()
    {
        var project = new StoryProject();
        var file = new StoryFile("sf_start", "시작 파일");
        project.Files.Add(file);
        var editor = new ProjectEditor(project);
        SetNode first = editor.AddSetNode(file.Id);
        editor.AddDialogueNode(file.Id);

        Assert.Equal(first.Id, editor.Project.StartNodeId);
    }

    [Fact]
    public void 그래프에서_노드를_옮겨도_파일_순서는_그대로다()
    {
        var sample = new Sample();
        List<string> order = sample.Project.EnumerateNodes().Select(node => node.Id).ToList();

        sample.Editor.MoveNode(sample.TargetB.Id, 10, 20);

        Assert.Equal(order, sample.Project.EnumerateNodes().Select(node => node.Id));
        Assert.Equal(10, sample.TargetB.Layout.X);
    }

    [Fact]
    public void 되돌리기가_편집을_되살린다()
    {
        var sample = new Sample();
        string line = sample.Line("처음");

        sample.Editor.SetScriptLineText(sample.Script.Id, line, "윌로", "고친 뒤");
        Assert.Equal("고친 뒤", TextOf(sample, line));

        sample.Editor.Undo();
        Assert.Equal("처음", TextOf(sample, line));

        sample.Editor.Redo();
        Assert.Equal("고친 뒤", TextOf(sample, line));
    }

    [Fact]
    public void 노드_이동은_되돌리기_기록을_더럽히지_않는다()
    {
        var sample = new Sample();
        string line = sample.Line("처음");
        sample.Editor.SetScriptLineText(sample.Script.Id, line, string.Empty, "고친 뒤");

        // 드래그 한 번에 수십 번 불리는 값이다. 이것이 쌓이면 되돌리기가 쓸모없어진다.
        for (int step = 0; step < 20; step++)
        {
            sample.Editor.MoveNode(sample.Dialogue.Id, step, step);
        }

        sample.Editor.Undo();
        Assert.Equal("처음", TextOf(sample, line));
    }

    [Fact]
    public void 편집할_때마다_알린다()
    {
        var sample = new Sample();
        int notifications = 0;
        sample.Editor.Changed += (_, _) => notifications++;

        ScriptLine line = sample.Editor.InsertScriptLine(sample.Script.Id);
        sample.Editor.SetScriptLineText(sample.Script.Id, line.Id, "윌로", "안녕");
        sample.Editor.SetLineTransition(
            sample.Dialogue.Id,
            line.Id,
            LineConditionTransition.BeginIf(sample.ConditionA.Id));

        Assert.Equal(3, notifications);
    }

    [Fact]
    public void 같은_값으로_다시_설정하면_기록을_남기지_않는다()
    {
        var sample = new Sample();
        string line = sample.Line("처음");
        int notifications = 0;
        sample.Editor.Changed += (_, _) => notifications++;

        sample.Editor.SetScriptLineText(sample.Script.Id, line, string.Empty, "처음");

        Assert.Equal(0, notifications);
    }

    /// <summary>
    /// 대사 노드는 본문을 소유하지 않는다. 한 줄을 고쳤을 때 바뀌는 것은 대본 하나뿐이다.
    /// </summary>
    [Fact]
    public void 화자와_대사의_권위는_대본에만_있다()
    {
        var sample = new Sample();
        string line = sample.Line("처음");

        sample.Editor.SetScriptLineText(sample.Script.Id, line, "윌로", "고친 뒤");

        Assert.Equal(new LocalizedLine("윌로", "고친 뒤"), sample.Script.Text(line));

        // 이 줄에는 조건이 없으므로 노드에 아무 항목도 남지 않는다. 본문 복사본은 어디에도 없다.
        Assert.Empty(sample.Dialogue.LineExtensions);
    }

    /// <summary>같은 대본을 두 노드가 읽어도 본문은 한 벌이다.</summary>
    [Fact]
    public void 같은_대본을_읽는_두_노드는_같은_본문을_본다()
    {
        var sample = new Sample();
        sample.Line("공유");
        DialogueNode other = sample.Editor.AddDialogueNode(
            sample.File.Id,
            name: "다른 장면",
            scriptId: sample.Script.Id);

        sample.Editor.SetScriptLineText(
            sample.Script.Id,
            sample.Script.Lines[0].Id,
            "라루",
            "한 곳만 바뀐다");

        Assert.Equal(
            "한 곳만 바뀐다",
            DialogueScriptResolver.Resolve(sample.Project, sample.Dialogue).Lines[0].Text);
        Assert.Equal(
            "한 곳만 바뀐다",
            DialogueScriptResolver.Resolve(sample.Project, other).Lines[0].Text);
    }

    private static string TextOf(Sample sample, string lineId) =>
        sample.Project.FindScript(sample.Script.Id)!.Text(lineId).Text;
    [Fact]
    public void 조건_이름과_식_수정은_구조_변경이_아니다()
    {
        var sample = new Sample();
        ProjectChangedEventArgs? change = null;
        sample.Editor.Changed += (_, args) => change = args;

        sample.Editor.UpdateCondition(sample.ConditionA.Id, "새 이름", "$favor >= 10");

        Assert.NotNull(change);
        Assert.Equal(ProjectChangeKind.ConditionDefinition, change!.Kind);
        Assert.False(change!.NeedsInspectorRebuild);
        Assert.True(change!.NeedsGraphRebuild);
    }

    [Fact]
    public void 조건_수정은_그래프_라벨과_Dialogue_선택지에_즉시_반영된다()
    {
        var sample = new Sample();
        string opener = sample.Line(
            "조건 시작",
            LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Editor.SetExitTarget(
            sample.Dialogue.Id,
            ExitPortKind.Branch,
            opener,
            sample.TargetA.Id);

        sample.Editor.UpdateCondition(sample.ConditionA.Id, "호감 매우 높음", "$favor >= 10");

        ExitPort branchPort = Assert.Single(
            NodeConnections.PortsOf(sample.Dialogue, sample.Project),
            port => port.Kind == ExitPortKind.Branch);
        Assert.Equal("호감 매우 높음", branchPort.Label);

        ConditionChoice choice = Assert.Single(
            ConditionChoices.For(preceding: null, sample.Dialogue, sample.Project),
            item => string.Equals(item.ConditionId, sample.ConditionA.Id, StringComparison.Ordinal));
        Assert.Equal("호감 매우 높음", choice.Label);
    }

    [Fact]
    public void assignment_값만_바꾸면_현재_편집_행을_다시_만들지_않는다()
    {
        var sample = new Sample();
        sample.Editor.SetAssignments(
            sample.SetNode.Id,
            new[] { new VariableAssignment { Variable = "$favor", Value = "1" } });

        ProjectChangedEventArgs? change = null;
        sample.Editor.Changed += (_, args) => change = args;

        sample.Editor.SetAssignments(
            sample.SetNode.Id,
            new[] { new VariableAssignment { Variable = "$favor", Value = "2" } });

        Assert.NotNull(change);
        Assert.Equal(ProjectChangeKind.Content, change!.Kind);
        Assert.False(change!.NeedsInspectorRebuild);
        Assert.False(change!.NeedsGraphRebuild);
        Assert.Equal("2", sample.SetNode.Assignments[0].Value);
    }

    [Fact]
    public void assignment_행_개수가_바뀌면_구조_변경이다()
    {
        var sample = new Sample();
        ProjectChangedEventArgs? change = null;
        sample.Editor.Changed += (_, args) => change = args;

        sample.Editor.SetAssignments(
            sample.SetNode.Id,
            new[] { new VariableAssignment { Variable = "$favor", Value = "1" } });

        Assert.NotNull(change);
        Assert.Equal(ProjectChangeKind.Structure, change!.Kind);
        Assert.True(change!.NeedsInspectorRebuild);
        Assert.True(change!.NeedsGraphRebuild);
    }

    [Fact]
    public void 같은_조건과_assignment를_다시_설정하면_알리지_않는다()
    {
        var sample = new Sample();
        sample.Editor.SetAssignments(
            sample.SetNode.Id,
            new[] { new VariableAssignment { Variable = "$favor", Value = "1" } });

        int notifications = 0;
        sample.Editor.Changed += (_, _) => notifications++;

        sample.Editor.UpdateCondition(
            sample.ConditionA.Id,
            sample.ConditionA.Name,
            sample.ConditionA.Expression);
        sample.Editor.SetAssignments(
            sample.SetNode.Id,
            new[] { new VariableAssignment { Variable = "$favor", Value = "1" } });

        Assert.Equal(0, notifications);
    }

    [Fact]
    public void 대사_본문_수정은_DialogueContent로_알린다()
    {
        var sample = new Sample();
        string line = sample.Line("처음");
        ProjectChangedEventArgs? change = null;
        sample.Editor.Changed += (_, args) => change = args;

        sample.Editor.SetScriptLineText(sample.Script.Id, line, "라루", "수정");

        Assert.Equal(ProjectChangeKind.DialogueContent, change!.Kind);
        Assert.False(change.NeedsInspectorRebuild);
        Assert.False(change.NeedsGraphRebuild);
    }

}
