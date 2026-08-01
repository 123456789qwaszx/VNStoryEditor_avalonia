using Vn.Authoring.Flow;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests;

/// <summary>
/// 조건 드롭다운은 "이 줄이 조건에 포함되는가"가 아니라
/// "이 줄에서 조건 흐름을 바꿀 것인가"를 고르는 자리다.
/// 그래서 같은 목록이라도 바로 앞 줄까지의 상태에 따라 의미가 달라진다.
/// </summary>
public class ConditionChoiceTests
{
    [Fact]
    public void 조건_바깥에서는_조건을_고르면_새_갈래가_열린다()
    {
        var sample = new Sample();

        IReadOnlyList<ConditionChoice> choices = ConditionChoices.For(null, sample.Project);

        Assert.Equal(ConditionChoiceKind.Inherit, choices[0].Kind);
        Assert.Equal(ConditionChoices.InheritOutsideLabel, choices[0].Label);

        // 준비된 조건이 전부 보이고, 고르면 BeginIf다.
        Assert.Equal(new[] { "호감 높음", "신뢰 높음" }, choices.Skip(1).Select(choice => choice.Label));
        Assert.All(choices.Skip(1), choice => Assert.Equal(ConditionChoiceKind.BeginIf, choice.Kind));

        // 바깥에는 "조건 종료"가 없다. 닫을 것이 없기 때문이다.
        Assert.DoesNotContain(choices, choice => choice.Kind == ConditionChoiceKind.EndIf);
    }

    [Fact]
    public void 조건_안에서는_유지_다른조건_종료가_보인다()
    {
        var sample = new Sample();
        sample.Line("if", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Line("안쪽");

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);
        IReadOnlyList<ConditionChoice> choices =
            ConditionChoices.For(flow.Lines[1].PrecedingBranch, sample.Project);

        Assert.Equal(ConditionChoices.InheritInsideLabel, choices[0].Label);

        // 지금 적용 중인 조건은 전환 대상으로 다시 보여 주지 않는다.
        Assert.DoesNotContain(choices, choice => choice.ConditionId == sample.ConditionA.Id);

        // 다른 조건은 elseif로 넘어간다. 중첩이 아니다.
        ConditionChoice other = Assert.Single(
            choices,
            choice => choice.ConditionId == sample.ConditionB.Id);
        Assert.Equal(ConditionChoiceKind.BeginElseIf, other.Kind);

        Assert.Equal(ConditionChoiceKind.EndIf, choices[^1].Kind);
        Assert.Equal(ConditionChoices.EndIfLabel, choices[^1].Label);
    }

    [Fact]
    public void 선택지가_그대로_전환으로_바뀐다()
    {
        var sample = new Sample();

        ConditionChoice beginIf = ConditionChoices.For(null, sample.Project)
            .Single(choice => choice.ConditionId == sample.ConditionA.Id);

        LineConditionTransition transition = beginIf.ToTransition()!;
        Assert.Equal(ConditionTransitionKind.BeginIf, transition.Kind);
        Assert.Equal(sample.ConditionA.Id, transition.ConditionId);

        Assert.Null(ConditionChoices.For(null, sample.Project)[0].ToTransition());
    }

    [Fact]
    public void 지금_줄의_전환이_목록에서_선택된_항목이_된다()
    {
        var sample = new Sample();
        sample.Line("if", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        LineBox second = sample.Line("elseif", LineConditionTransition.BeginElseIf(sample.ConditionB.Id));

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);
        IReadOnlyList<ConditionChoice> choices =
            ConditionChoices.For(flow.Lines[1].PrecedingBranch, sample.Project);

        ConditionChoice current = ConditionChoices.Current(choices, second.Transition);

        Assert.Equal(ConditionChoiceKind.BeginElseIf, current.Kind);
        Assert.Equal(sample.ConditionB.Id, current.ConditionId);
    }

    [Fact]
    public void 이름이_없는_조건은_Id로_보여_준다()
    {
        var sample = new Sample();
        sample.Editor.UpdateCondition(sample.ConditionA.Id, string.Empty, "favor >= 5");

        Assert.Contains(
            ConditionChoices.For(null, sample.Project),
            choice => choice.Label == sample.ConditionA.Id);
    }
}
