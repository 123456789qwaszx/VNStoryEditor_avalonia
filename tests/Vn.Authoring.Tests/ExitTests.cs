using Vn.Authoring.Flow;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests;

/// <summary>
/// 출구는 그래프 간선과 노드 내부 설정이 공유하는 하나의 상태다.
/// 어느 쪽에서 바꾸든 같은 곳을 바꾸므로 둘이 어긋날 자리가 없어야 한다.
/// </summary>
public class ExitTests
{
    [Fact]
    public void 조건_갈래마다_서로_다른_출구를_설정할_수_있다()
    {
        var sample = new Sample();
        var (_, l1, _, l3, _, _, _) = sample.BuildSpecExample();

        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, l1.Id, sample.TargetA.Id);
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, l3.Id, sample.TargetB.Id);
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Default, null, sample.TargetDefault.Id);

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);

        Assert.Equal(sample.TargetA.Id, flow.Branches[0].ExitTargetNodeId);
        Assert.Equal(sample.TargetB.Id, flow.Branches[1].ExitTargetNodeId);
        Assert.Equal(sample.TargetDefault.Id, sample.Dialogue.DefaultExitTargetNodeId);
    }

    [Fact]
    public void 기본_출구와_조건_출구는_서로_독립적이다()
    {
        var sample = new Sample();
        var (_, l1, _, _, _, _, _) = sample.BuildSpecExample();

        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, l1.Id, sample.TargetA.Id);
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Default, null, sample.TargetDefault.Id);

        // 기본 출구를 끊어도 조건 출구는 그대로다.
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Default, null, null);

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);
        Assert.Equal(sample.TargetA.Id, flow.Branches[0].ExitTargetNodeId);
        Assert.Null(sample.Dialogue.DefaultExitTargetNodeId);

        // 반대도 마찬가지다.
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Default, null, sample.TargetDefault.Id);
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, l1.Id, null);

        flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);
        Assert.Null(flow.Branches[0].ExitTargetNodeId);
        Assert.Equal(sample.TargetDefault.Id, sample.Dialogue.DefaultExitTargetNodeId);
    }

    [Fact]
    public void 출구가_있는_갈래의_마지막_줄만_출구로_표시된다()
    {
        var sample = new Sample();
        var (_, l1, l2, _, _, _, _) = sample.BuildSpecExample();

        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, l1.Id, sample.TargetA.Id);

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);

        // 갈래는 l1~l2다. 출구 표시는 마지막 줄인 l2에만 붙는다.
        Assert.False(flow.Lines[1].IsBranchExit);
        Assert.True(flow.Lines[2].IsBranchExit);
        Assert.Equal(l2.Id, flow.Lines[2].Line.Id);

        // 두 번째 갈래에는 출구가 없으므로 아무 줄도 출구가 아니다.
        Assert.False(flow.Lines[3].IsBranchExit);
        Assert.False(flow.Lines[4].IsBranchExit);
    }

    [Fact]
    public void 출구를_제거하면_줄이_일반_갈래_상태로_돌아온다()
    {
        var sample = new Sample();
        var (_, l1, _, _, _, _, _) = sample.BuildSpecExample();

        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, l1.Id, sample.TargetA.Id);
        Assert.Contains(
            ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project).Lines,
            line => line.IsBranchExit);

        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, l1.Id, null);

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);
        Assert.DoesNotContain(flow.Lines, line => line.IsBranchExit);
        Assert.All(flow.Lines.Where(line => line.Branch is not null), line => Assert.Equal(1, line.Depth));
    }

    /// <summary>
    /// 출구를 갈래의 마지막 줄이 아니라 <b>여는 줄</b>에 매다는 설계의 핵심 효과다.
    /// 줄을 추가해도 출구는 갈래의 것으로 남고, 표시 위치만 새 마지막 줄로 옮겨 간다.
    /// 출구가 갈래 중간에 파묻히고 그 아래 대사가 실행되는 모순이 생기지 않는다.
    /// </summary>
    [Fact]
    public void 갈래에_줄을_더하면_출구_표시가_새_마지막_줄로_따라간다()
    {
        var sample = new Sample();
        LineBox opener = sample.Line("if", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        LineBox second = sample.Line("둘째");
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, opener.Id, sample.TargetA.Id);

        Assert.True(ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project)
            .Lines.Single(line => line.Line.Id == second.Id).IsBranchExit);

        LineBox third = sample.Line("셋째");

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);
        Assert.False(flow.Lines.Single(line => line.Line.Id == second.Id).IsBranchExit);
        Assert.True(flow.Lines.Single(line => line.Line.Id == third.Id).IsBranchExit);

        // 출구 자체는 바뀌지 않았다. 갈래에 속한 것이지 줄에 속한 것이 아니다.
        Assert.Equal(sample.TargetA.Id, flow.Branches[0].ExitTargetNodeId);
    }

    [Fact]
    public void 갈래를_더_이상_열지_않으면_그_출구는_사라진다()
    {
        var sample = new Sample();
        LineBox opener = sample.Line("if", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, opener.Id, sample.TargetA.Id);

        // 이 줄이 더 이상 갈래를 열지 않게 되면 주인 없는 출구가 남으면 안 된다.
        sample.Editor.SetLineTransition(sample.Dialogue.Id, opener.Id, null);

        Assert.Empty(sample.Dialogue.BranchExits);
        Assert.Empty(ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project).Branches);
    }

    [Fact]
    public void 줄을_지우면_그_줄이_열던_갈래의_출구도_사라진다()
    {
        var sample = new Sample();
        LineBox opener = sample.Line("if", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, opener.Id, sample.TargetA.Id);

        sample.Editor.RemoveLine(sample.Dialogue.Id, opener.Id);

        Assert.Empty(sample.Dialogue.BranchExits);
    }

    [Fact]
    public void 노드를_지우면_그것을_가리키던_출구가_함께_끊긴다()
    {
        var sample = new Sample();
        LineBox opener = sample.Line("if", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, opener.Id, sample.TargetA.Id);
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Default, null, sample.TargetA.Id);

        sample.Editor.RemoveNode(sample.TargetA.Id);

        Assert.Empty(sample.Dialogue.BranchExits);
        Assert.Null(sample.Dialogue.DefaultExitTargetNodeId);
        Assert.DoesNotContain(
            ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project).Problems,
            problem => problem.Kind == FlowProblemKind.MissingExitTarget);
    }
}
