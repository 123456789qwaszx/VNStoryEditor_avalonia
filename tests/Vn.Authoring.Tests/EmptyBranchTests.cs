using Vn.Authoring.Rendering;
using Vn.Authoring.Results;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Script;
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

    // ── 대본의 <b>끝</b>에 있는 빈 블록 ─────────────────────────────────────

    [Fact]
    public void 대본_끝의_빈_블록은_거부되지_않고_꼬리에_실린다()
    {
        // ⛔ 소유자가 겪은 나머지 반쪽. 전환은 늘 <em>다음</em> 대사 줄에 실리는데 대본이
        //    조건 블록으로 끝나면 실을 줄이 없다 — 예전에는 "붙을 곳이 없습니다"로 통째
        //    거부했다. 이제 <b>끝에서 짝이 맞으면</b> 그것은 대사 없는 블록이다.
        var sample = new Sample();
        sample.Line("복도는 조용했다");

        ScenarioPasteOutcome outcome = sample.Editor.ApplyScenarioText(
            sample.Dialogue.Id,
            $"복도는 조용했다\n<<if {sample.ConditionA.Expression}>>\n<<endif>>\n",
            GameDefinition.Empty,
            confirmDeletes: true);

        Assert.True(outcome.Applied, string.Join(" / ", outcome.Problems));
        Assert.Empty(outcome.Problems);

        Assert.Equal(
            [ConditionTransitionKind.BeginIf, ConditionTransitionKind.EndIf],
            sample.Dialogue.TrailingTransitions.Select(transition => transition.Kind));
    }

    [Fact]
    public void 꼬리의_빈_갈래도_흐름에서_갈래로_선다()
    {
        var sample = new Sample();
        sample.Line("복도는 조용했다");

        sample.Editor.ApplyScenarioText(
            sample.Dialogue.Id,
            $"복도는 조용했다\n<<if {sample.ConditionA.Expression}>>\n<<endif>>\n",
            GameDefinition.Empty,
            confirmDeletes: true);

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);

        ConditionBranch branch = Assert.Single(flow.Branches);

        Assert.Equal(sample.ConditionA.Id, branch.ConditionId);

        // 줄이 아니라 <b>꼬리</b>에 산다 — 신원이 그렇게 말한다.
        Assert.True(BranchAnchor.IsTrailing(branch.OpenLineId));

        // 그리고 어느 줄도 그 안에 없다.
        Assert.All(flow.Lines, line => Assert.Null(line.Branch));
    }

    [Fact]
    public void 꼬리가_사라지면_노드에서도_사라진다()
    {
        // ⚠ 대본이 유일한 원천이다. 안 지우면 지운 블록이 산출물에 계속 남는다.
        var sample = new Sample();
        sample.Line("복도는 조용했다");

        sample.Editor.ApplyScenarioText(
            sample.Dialogue.Id,
            $"복도는 조용했다\n<<if {sample.ConditionA.Expression}>>\n<<endif>>\n",
            GameDefinition.Empty,
            confirmDeletes: true);

        Assert.NotEmpty(sample.Dialogue.TrailingTransitions);

        sample.Editor.ApplyScenarioText(
            sample.Dialogue.Id, "복도는 조용했다\n", GameDefinition.Empty, confirmDeletes: true);

        Assert.Empty(sample.Dialogue.TrailingTransitions);
    }

    [Fact]
    public void 꼬리의_빈_블록이_산출물에_그대로_나간다()
    {
        // ⛔ 이것이 이 기능의 <b>수용 기준</b>이다. 저장만 되고 안 나가면 "적었는데 게임에
        //    없다"가 된다 — 조용한 손실 중 가장 나쁘다.
        var sample = new Sample();
        sample.Line("복도는 조용했다");

        sample.Editor.ApplyScenarioText(
            sample.Dialogue.Id,
            $"복도는 조용했다\n<<if {sample.ConditionA.Expression}>>\n<<endif>>\n",
            GameDefinition.Empty,
            confirmDeletes: true);

        DialogueResult result = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;
        RenderedDocument document = ResultDocumentComposer.Compose(result, project: sample.Project);

        // 마지막 대사 줄 <b>뒤에</b> 조건이 열리고 닫힌다.
        List<RenderedSegmentKind> shape = document.Segments
            .Where(segment => segment.Kind is RenderedSegmentKind.DialogueLine
                or RenderedSegmentKind.ConditionBegin
                or RenderedSegmentKind.ConditionEnd)
            .Select(segment => segment.Kind)
            .ToList();

        Assert.Equal(
            [
                RenderedSegmentKind.DialogueLine,
                RenderedSegmentKind.ConditionBegin,
                RenderedSegmentKind.ConditionEnd
            ],
            shape);

        // ⚠ 문서 끝에서 <b>또</b> 닫지 않는다 — 꼬리가 이미 닫았다.
        Assert.Single(document.Segments, segment => segment.Kind == RenderedSegmentKind.ConditionEnd);
    }

    [Fact]
    public void 꼬리의_빈_갈래에_매단_detour가_산출물에_나간다()
    {
        // 소유자가 이 기능을 원한 <b>이유</b> — "연출그래프에서 커스텀 노드로 그 조건에서
        // detour하도록 할 수 있거든". 빈 갈래에 매단 출구가 실제로 나가야 뜻이 있다.
        var sample = new Sample();
        sample.Line("복도는 조용했다");

        sample.Editor.ApplyScenarioText(
            sample.Dialogue.Id,
            $"복도는 조용했다\n<<if {sample.ConditionA.Expression}>>\n<<endif>>\n",
            GameDefinition.Empty,
            confirmDeletes: true);

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);
        ConditionBranch branch = Assert.Single(flow.Branches);

        // 빈 갈래에 커스텀 씬을 매단다 — 신원은 줄이 아니라 꼬리 앵커다.
        sample.Dialogue.BranchExits[branch.OpenLineId] = sample.TargetDefault.Id;

        DialogueResult result = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;
        RenderedDocument document = ResultDocumentComposer.Compose(result, project: sample.Project);

        // 조건 갈래의 출구는 jump가 아니라 detour다 (2026-08-21).
        RenderedSegment detour = Assert.Single(
            document.Segments, segment => segment.Kind == RenderedSegmentKind.BranchDetour);

        Assert.Equal(sample.TargetDefault.Id, detour.TargetNodeId);
    }

    [Fact]
    public void 빈_갈래도_카드에_제_포트를_받는다()
    {
        // ⛔ 소유자가 매다는 <b>실제 자리</b>다 — "잇고 떼는 자리는 연출 그래프 카드의
        //    IF 갈래 포트 하나"(2026-08-23). 포트가 안 서면 매달 방법이 없다.
        var sample = new Sample();
        sample.Line("복도는 조용했다");

        sample.Editor.ApplyScenarioText(
            sample.Dialogue.Id,
            $"복도는 조용했다\n<<if {sample.ConditionA.Expression}>>\n<<endif>>\n",
            GameDefinition.Empty,
            confirmDeletes: true);

        ExitPort port = Assert.Single(
            NodeConnections.PortsOf(sample.Dialogue, sample.Project),
            item => item.Kind == ExitPortKind.Branch);

        Assert.True(BranchAnchor.IsTrailing(port.ExitKey));
        Assert.Null(port.TargetNodeId);

        // 포트로 매단다 — 카드에서 자유 씬을 고르는 것과 같은 길이다.
        sample.Editor.SetExitTarget(port, sample.TargetDefault.Id);

        ExitPort wired = Assert.Single(
            NodeConnections.PortsOf(sample.Dialogue, sample.Project),
            item => item.Kind == ExitPortKind.Branch);

        Assert.Equal(sample.TargetDefault.Id, wired.TargetNodeId);
    }

    [Fact]
    public void 짝이_안_맞는_여는_전환은_여전히_오류다()
    {
        // 끝에서 열고 안 닫으면 그건 대사 없는 블록이 아니라 <b>진짜로 붙을 곳이 없는</b>
        // 전환이다 — 예전 문구 그대로 말한다.
        var sample = new Sample();

        ScenarioParseResult parsed = ScenarioTextParser.Parse(
            $"복도는 조용했다\n<<if {sample.ConditionA.Expression}>>\n", GameDefinition.Empty);

        Assert.Contains(parsed.UnparsedLines, problem => problem.Contains("붙일 곳이 없습니다"));
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
