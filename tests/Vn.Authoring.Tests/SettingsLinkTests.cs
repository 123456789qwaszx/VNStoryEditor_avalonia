using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Script;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests;

/// <summary>
/// 실행 출구와 조건 공급은 서로 다른 관계이며, DialogueNode의 조건 범위는
/// 활성 Settings link와 게임 전역 정의로만 결정된다는 계약.
/// </summary>
public class SettingsLinkTests
{
    [Fact]
    public void 실행_출구와_Settings_link는_서로_독립적이다()
    {
        var sample = new Sample();
        sample.Editor.SetExitTarget(
            sample.SetNode.Id,
            ExitPortKind.Default,
            branchOpenLineId: null,
            sample.TargetA.Id);

        sample.Editor.RemoveLink(sample.SettingsLink.Id);

        Assert.Equal(sample.TargetA.Id, sample.SetNode.DefaultExitTargetNodeId);
        Assert.Empty(sample.Project.Links);

        ExitPort execution = Assert.Single(NodeConnections.PortsOf(sample.SetNode, sample.Project));
        Assert.Equal(ExitPortKind.Default, execution.Kind);
        Assert.Equal(sample.TargetA.Id, execution.TargetNodeId);
    }

    [Fact]
    public void Dialogue는_연결된_SetNode와_게임_전역_조건만_사용한다()
    {
        var project = new StoryProject();
        var file = new StoryFile("sf_scope", "범위");
        project.Files.Add(file);
        var editor = new ProjectEditor(project);

        SetNode linked = editor.AddSetNode(file.Id, name: "연결됨");
        ConditionDefinition linkedCondition = editor.AddCondition(linked.Id, "연결 조건", "linked == true");
        SetNode unrelated = editor.AddSetNode(file.Id, name: "연결 안 됨");
        ConditionDefinition unrelatedCondition = editor.AddCondition(unrelated.Id, "다른 조건", "other == true");
        DialogueNode dialogue = editor.AddDialogueNode(file.Id, name: "장면");
        editor.AddSettingsLink(linked.Id, dialogue.Id);

        var definition = new GameDefinition
        {
            Conditions = new List<ConditionSpec>
            {
                new()
                {
                    Id = "global_difficulty",
                    Name = "고난도",
                    Expression = "difficulty == hard"
                }
            }
        };

        AvailableConditionCatalog catalog = AvailableConditionResolver.Resolve(
            project,
            dialogue.Id,
            definition);

        Assert.Equal(
            new[] { "global_difficulty", linkedCondition.Id },
            catalog.Conditions.Select(condition => condition.Id));
        Assert.DoesNotContain(
            catalog.Conditions,
            condition => condition.Id == unrelatedCondition.Id);

        IReadOnlyList<ConditionChoice> choices = ConditionChoices.For(
            preceding: null,
            dialogue,
            project,
            definition);

        Assert.Contains(choices, choice => choice.ConditionId == "global_difficulty");
        Assert.Contains(choices, choice => choice.ConditionId == linkedCondition.Id);
        Assert.DoesNotContain(choices, choice => choice.ConditionId == unrelatedCondition.Id);

        ScriptDocument script = editor.AddScript("전역 조건 대본");
        editor.SetDialogueScript(dialogue.Id, script.Id);
        ScriptLine globalLine = editor.InsertScriptLine(script.Id);
        editor.SetLineTransition(
            dialogue.Id,
            globalLine.Id,
            LineConditionTransition.BeginIf("global_difficulty"));

        DialogueFlow flow = ConditionFlowResolver.Resolve(dialogue, project, definition);
        Assert.DoesNotContain(flow.Problems, problem =>
            problem.Kind is FlowProblemKind.UnknownCondition or FlowProblemKind.UnavailableCondition);
    }

    [Fact]
    public void 여러_Settings_link는_Order와_저장_순서대로_조건을_공급한다()
    {
        var project = new StoryProject();
        var file = new StoryFile("sf_order", "순서");
        project.Files.Add(file);
        var editor = new ProjectEditor(project);

        SetNode first = editor.AddSetNode(file.Id, name: "첫 번째");
        ConditionDefinition firstCondition = editor.AddCondition(first.Id, "첫 조건", "a");
        SetNode second = editor.AddSetNode(file.Id, name: "두 번째");
        ConditionDefinition secondCondition = editor.AddCondition(second.Id, "둘째 조건", "b");
        DialogueNode dialogue = editor.AddDialogueNode(file.Id, name: "장면");

        editor.AddSettingsLink(second.Id, dialogue.Id);
        editor.AddSettingsLink(first.Id, dialogue.Id);

        AvailableConditionCatalog catalog = AvailableConditionResolver.Resolve(project, dialogue.Id);

        Assert.Equal(
            new[] { secondCondition.Id, firstCondition.Id },
            catalog.Conditions.Select(condition => condition.Id));
    }

    [Fact]
    public void Settings_link를_삭제해도_기존_Transition은_보존되고_사용불가로_표시된다()
    {
        var sample = new Sample();
        string line = sample.Line(
            "조건 대사",
            LineConditionTransition.BeginIf(sample.ConditionA.Id));

        sample.Editor.RemoveLink(sample.SettingsLink.Id);

        Assert.NotNull(sample.Dialogue.FindExtension(line)!.Transition);
        Assert.Equal(ConditionTransitionKind.BeginIf, sample.Dialogue.FindExtension(line)!.Transition!.Kind);
        Assert.Equal(sample.ConditionA.Id, sample.Dialogue.FindExtension(line)!.Transition!.ConditionId);

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);
        FlowProblem problem = Assert.Single(
            flow.Problems,
            item => item.Kind == FlowProblemKind.UnavailableCondition);
        Assert.Equal(line, problem.LineId);

        IReadOnlyList<ConditionChoice> choices = ConditionChoices.For(
            preceding: null,
            sample.Dialogue,
            sample.Project,
            definition: null,
            currentTransition: sample.Dialogue.FindExtension(line)!.Transition);
        ConditionChoice current = ConditionChoices.Current(choices, sample.Dialogue.FindExtension(line)!.Transition);

        Assert.False(current.IsAvailable);
        Assert.Equal(sample.ConditionA.Id, current.ConditionId);
        Assert.Contains("사용할 수 없음", current.Label, StringComparison.Ordinal);

        ExitPort branch = NodeConnections.PortsOf(sample.Dialogue, sample.Project)
            .Single(port => port.Kind == ExitPortKind.Branch);
        Assert.Contains("사용할 수 없음", branch.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_link를_다시_연결하면_기존_Transition이_다시_유효해진다()
    {
        var sample = new Sample();
        string line = sample.Line(
            "조건 대사",
            LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Editor.RemoveLink(sample.SettingsLink.Id);

        sample.Editor.AddSettingsLink(sample.SetNode.Id, sample.Dialogue.Id);

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);
        Assert.DoesNotContain(flow.Problems, problem =>
            problem.Kind is FlowProblemKind.UnavailableCondition or FlowProblemKind.UnknownCondition);
        Assert.Equal(sample.ConditionA.Id, sample.Dialogue.FindExtension(line)!.Transition!.ConditionId);
    }

    [Fact]
    public void 비활성_Settings_link를_다시_연결하면_활성화된다()
    {
        var sample = new Sample();
        sample.Editor.SetLinkEnabled(sample.SettingsLink.Id, enabled: false);

        NodeLink result = sample.Editor.AddSettingsLink(sample.SetNode.Id, sample.Dialogue.Id);

        Assert.Same(sample.SettingsLink, result);
        Assert.True(result.IsEnabled);
        Assert.Contains(
            AvailableConditionResolver.Resolve(sample.Project, sample.Dialogue.Id).Conditions,
            condition => condition.Id == sample.ConditionA.Id);
    }

    [Fact]
    public void Settings_link는_SetNode에서_DialogueNode로만_만든다()
    {
        var sample = new Sample();

        Assert.Throws<InvalidOperationException>(() =>
            sample.Editor.AddSettingsLink(sample.Dialogue.Id, sample.TargetA.Id));
        Assert.Throws<InvalidOperationException>(() =>
            sample.Editor.AddSettingsLink(sample.SetNode.Id, sample.SetNode.Id));
    }

    [Fact]
    public void 노드를_삭제하면_그_노드의_Settings_link도_정리된다()
    {
        var sample = new Sample();

        sample.Editor.RemoveNode(sample.SetNode.Id);

        Assert.Empty(sample.Project.Links);
    }

    [Fact]
    public void Settings_link_변경은_Connections로_알린다()
    {
        var project = new StoryProject();
        var file = new StoryFile("sf_event", "이벤트");
        project.Files.Add(file);
        var editor = new ProjectEditor(project);
        SetNode setNode = editor.AddSetNode(file.Id);
        DialogueNode dialogue = editor.AddDialogueNode(file.Id);
        ProjectChangeKind? received = null;
        editor.Changed += (_, args) => received = args.Kind;

        NodeLink link = editor.AddSettingsLink(setNode.Id, dialogue.Id);
        Assert.Equal(ProjectChangeKind.Connections, received);

        received = null;
        editor.RemoveLink(link.Id);
        Assert.Equal(ProjectChangeKind.Connections, received);
    }
}
