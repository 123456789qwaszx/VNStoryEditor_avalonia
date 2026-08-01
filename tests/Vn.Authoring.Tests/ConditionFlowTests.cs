using Vn.Authoring.Flow;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests;

/// <summary>
/// 조건은 줄마다 반복 저장되지 않고 전환 지점만 저장된다.
/// 그래서 "지금 어떤 갈래인가"는 계산 결과이며, 그 계산이 이 저작 도구의 의미 그 자체다.
/// </summary>
public class ConditionFlowTests
{
    [Fact]
    public void 작업지시서_예시가_같은_구조로_계산된다()
    {
        var sample = new Sample();
        var (l0, l1, l2, l3, l4, l5, l6) = sample.BuildSpecExample();

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);

        // Line 0은 조건 바깥이다.
        Assert.Null(flow.Lines[0].Branch);

        // BeginIf 줄은 자기가 연 갈래 안에 있다.
        Assert.Equal(sample.ConditionA.Id, flow.Lines[1].Branch!.ConditionId);
        Assert.Equal(sample.ConditionA.Id, flow.Lines[2].Branch!.ConditionId);

        // BeginElseIf 줄부터는 두 번째 갈래다.
        Assert.Equal(sample.ConditionB.Id, flow.Lines[3].Branch!.ConditionId);
        Assert.Equal(sample.ConditionB.Id, flow.Lines[4].Branch!.ConditionId);

        // EndIf 줄은 이미 바깥이다.
        Assert.Null(flow.Lines[5].Branch);
        Assert.Null(flow.Lines[6].Branch);

        Assert.Equal(new[] { l0.Id, l1.Id, l2.Id, l3.Id, l4.Id, l5.Id, l6.Id },
            flow.Lines.Select(line => line.Line.Id));
        Assert.Empty(flow.Problems);
    }

    [Fact]
    public void BeginIf_이후_전환이_없는_줄은_같은_갈래를_상속한다()
    {
        var sample = new Sample();
        sample.Line("바깥");
        LineBox opener = sample.Line("여는 줄", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Line("따라오는 줄");
        sample.Line("또 따라오는 줄");

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);

        // 전환을 적지 않은 줄들이 여는 줄과 같은 갈래를 가리킨다.
        Assert.Equal(opener.Id, flow.Lines[1].Branch!.OpenLineId);
        Assert.Equal(opener.Id, flow.Lines[2].Branch!.OpenLineId);
        Assert.Equal(opener.Id, flow.Lines[3].Branch!.OpenLineId);
        Assert.Single(flow.Branches);
    }

    [Fact]
    public void BeginElseIf는_깊이를_증가시키지_않는다()
    {
        var sample = new Sample();
        sample.Line("바깥");
        sample.Line("if", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Line("elseif", LineConditionTransition.BeginElseIf(sample.ConditionB.Id));

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);

        Assert.Equal(0, flow.Lines[0].Depth);
        Assert.Equal(1, flow.Lines[1].Depth);
        Assert.Equal(1, flow.Lines[2].Depth);

        // 중첩이 아니라 형제다. 같은 체인 안에서 순번만 늘어난다.
        Assert.Equal(2, flow.Branches.Count);
        Assert.Equal(flow.Branches[0].ChainIndex, flow.Branches[1].ChainIndex);
        Assert.Equal(0, flow.Branches[0].BranchIndexInChain);
        Assert.Equal(1, flow.Branches[1].BranchIndexInChain);
    }

    [Fact]
    public void EndIf_이후에는_일반_흐름으로_돌아간다()
    {
        var sample = new Sample();
        sample.Line("if", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Line("안쪽");
        sample.Line("endif", LineConditionTransition.EndIf());
        sample.Line("바깥");

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);

        Assert.NotNull(flow.Lines[0].Branch);
        Assert.NotNull(flow.Lines[1].Branch);
        Assert.Null(flow.Lines[2].Branch);
        Assert.Null(flow.Lines[3].Branch);
        Assert.Equal(0, flow.Lines[3].Depth);
    }

    [Fact]
    public void 갈래마다_다른_색_자리를_받는다()
    {
        var sample = new Sample();
        sample.BuildSpecExample();

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);

        Assert.Equal(new[] { 0, 1 }, flow.Branches.Select(branch => branch.PaletteIndex));
    }

    [Fact]
    public void 열린_조건이_없는데_endif가_있으면_알린다()
    {
        var sample = new Sample();
        sample.Line("바깥", LineConditionTransition.EndIf());

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);

        Assert.Equal(FlowProblemKind.EndIfWithoutIf, Assert.Single(flow.Problems).Kind);
        Assert.Null(flow.Lines[0].Branch);
    }

    [Fact]
    public void 중첩_조건은_조용히_바꾸지_않고_알린다()
    {
        var sample = new Sample();
        sample.Line("if", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Line("또 if", LineConditionTransition.BeginIf(sample.ConditionB.Id));

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);

        Assert.Contains(flow.Problems, problem => problem.Kind == FlowProblemKind.NestedCondition);

        // 알리되 화면이 표현할 수 없는 깊이로 가지는 않는다.
        Assert.Equal(1, flow.Lines[1].Depth);
    }

    [Fact]
    public void 사라진_조건을_가리키는_줄은_알린다()
    {
        var sample = new Sample();
        sample.Line("if", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Editor.RemoveCondition(sample.ConditionA.Id);

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);

        Assert.Contains(flow.Problems, problem => problem.Kind == FlowProblemKind.UnknownCondition);

        // 작가가 만든 갈래 구조 자체는 무너지지 않는다.
        Assert.Single(flow.Branches);
    }
}
