using Vn.Authoring.Flow;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests;

/// <summary>
/// <b>대사가 없는 조건 갈래</b> (2026-08-24 소유자: "조건을 건 다음에 대사를 붙여야 하는데,
/// 굳이 대사를 붙이지 않아도 되도록 해줄래? 연출그래프에서 커스텀 노드로 그 조건에서
/// detour하도록 할 수 있거든").
///
/// ⚠ 갈래의 신원은 <c>OpenLineId</c> — <b>그 갈래를 여는 줄</b>이다. 그런데 대사가 없는
/// 갈래는 여는 줄이 <b>자기 것이 아니다</b>: 전환은 늘 <em>다음</em> 대사 줄에 실리므로,
/// 빈 블록의 <c>BeginIf</c>와 <c>EndIf</c>가 바깥 줄 하나에 함께 앉는다. 그래서 빈 블록이
/// 둘이면 신원이 겹친다.
/// </summary>
public class EmptyBranchTests
{
    [Fact]
    public void 빈_블록_뒤에_또_블록이_열려도_흐름이_계산된다()
    {
        // 한 줄이 [BeginIf(A), EndIf, BeginIf(B)]를 진다 — 빈 블록 바로 뒤에 블록이 열린다.
        var sample = new Sample();
        sample.Line("바깥");
        string carrier = sample.Line("문이 열렸다");

        sample.Editor.SetLineTransitions(sample.Dialogue.Id, carrier,
        [
            LineConditionTransition.BeginIf(sample.ConditionA.Id),
            LineConditionTransition.EndIf(),
            LineConditionTransition.BeginIf(sample.ConditionB.Id)
        ]);

        // ⛔ 신원이 겹치면 여기서 터진다 — 판이 아예 안 그려진다.
        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);

        Assert.Equal(2, flow.Branches.Count);
        Assert.Equal(2, flow.Branches.Select(branch => branch.OpenLineId).Distinct().Count());
    }

    [Fact]
    public void 빈_블록은_뒤따르는_대사를_삼키지_않는다()
    {
        // ⛔ 이것이 소유자가 겪은 고장이다. 빈 블록의 `EndIf`는 <b>블록 다음 대사 줄</b>에
        //    `BeginIf`와 함께 실리는데, 흐름 해석이 첫 전환만 보아 `EndIf`를 버렸다 —
        //    갈래가 안 닫히고 뒤따르는 대사가 전부 그 조건 안으로 빨려 들어갔다.
        var sample = new Sample();
        string outside = sample.Line("바깥");
        string after = sample.Line("문이 열렸다");

        sample.Editor.SetLineTransitions(sample.Dialogue.Id, after,
        [
            LineConditionTransition.BeginIf(sample.ConditionA.Id),
            LineConditionTransition.EndIf()
        ]);

        string later = sample.Line("그 뒤의 대사");

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);

        // 빈 갈래가 하나 섰다.
        ConditionBranch branch = Assert.Single(flow.Branches);
        Assert.Equal(sample.ConditionA.Id, branch.ConditionId);

        // 그리고 <b>어느 줄도 그 안에 없다</b> — 대사 없는 갈래이기 때문이다.
        Assert.All(flow.Lines, line => Assert.Null(line.Branch));
        Assert.Equal([outside, after, later], flow.Lines.Select(line => line.Line.LineId));
    }

    [Fact]
    public void 한_줄에_실린_전환이_순서대로_전부_적용된다()
    {
        // 위 고장의 뿌리 — 목록의 둘째부터가 조용히 버려지고 있었다.
        // 갈래를 닫고 곧바로 다른 갈래를 여는 흔한 모양으로 못 박는다.
        var sample = new Sample();
        string opener = sample.Line("여는 줄", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        string inside = sample.Line("조건 안");
        string next = sample.Line("다음 갈래");

        sample.Editor.SetLineTransitions(sample.Dialogue.Id, next,
        [
            LineConditionTransition.EndIf(),
            LineConditionTransition.BeginIf(sample.ConditionB.Id)
        ]);

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);

        Assert.Equal(opener, flow.Lines[0].Branch!.OpenLineId);
        Assert.Equal(opener, flow.Lines[1].Branch!.OpenLineId);

        // 닫고 나서 연 갈래는 <b>새 갈래</b>다 — 첫 갈래를 상속하지 않는다.
        Assert.Equal(sample.ConditionB.Id, flow.Lines[2].Branch!.ConditionId);
        Assert.Equal(next, flow.Lines[2].Branch!.OpenLineId);

        _ = inside;
    }
}
