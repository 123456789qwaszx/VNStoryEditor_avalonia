using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

/// <summary>
/// 선택지는 조건 전환과 동형의 "선택 전환 체인"이다.
/// 순서 안정성은 세이브 계약이고(계약서 C3), 라벨은 접두 없이 출력되며(D6),
/// 미리보기 태그는 표시 전용이고 실제 효과는 본문의 set이다(D5).
/// </summary>
public class ChoiceTests
{
    // ── 흐름 해석 ───────────────────────────────────────────────────────────

    [Fact]
    public void 선택_체인은_조건과_같은_기계로_갈래를_만든다()
    {
        var sample = new Sample();
        string label1 = sample.Line("안전한 길", LineConditionTransition.BeginChoice());
        string body1 = sample.Line("본문 1");
        string label2 = sample.Line("깊은 숲", LineConditionTransition.BeginNextOption());
        string body2 = sample.Line("본문 2");
        string after = sample.Line("블록 뒤", LineConditionTransition.EndChoice());

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);

        Assert.Equal(2, flow.Branches.Count);
        Assert.All(flow.Branches, branch => Assert.True(branch.IsChoice));
        Assert.Equal(new[] { label1, label2 }, flow.Branches.Select(branch => branch.OpenLineId));
        Assert.Equal(new[] { 0, 1 }, flow.Branches.Select(branch => branch.BranchIndexInChain));
        Assert.All(flow.Branches, branch => Assert.StartsWith("op_", branch.OptionId!, StringComparison.Ordinal));

        // 본문 줄은 자기 옵션 갈래 안에 있고, 블록 뒤 줄은 바깥이다.
        Assert.Equal(label1, flow.Lines.Single(line => line.Line.LineId == body1).Branch!.OpenLineId);
        Assert.Equal(label2, flow.Lines.Single(line => line.Line.LineId == body2).Branch!.OpenLineId);
        Assert.Null(flow.Lines.Single(line => line.Line.LineId == after).Branch);
        Assert.Empty(flow.Problems);
    }

    [Fact]
    public void 조건_안_선택지는_정식_구성이다()
    {
        // W54 (소유자 결정): 조건 갈래 본문 안에서 닫히는 선택 블록은 지원한다.
        var sample = new Sample();
        string opener = sample.Line("조건 시작", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        string label1 = sample.Line("사과", LineConditionTransition.BeginChoice());
        string body1 = sample.Line("사과 본문");
        string label2 = sample.Line("포도", LineConditionTransition.BeginNextOption());
        string body2 = sample.Line("포도 본문");
        string joined = sample.Line("합류(조건 안)", LineConditionTransition.EndChoice());
        sample.Line("끝", LineConditionTransition.EndIf());

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);

        Assert.Empty(flow.Problems);
        Assert.Equal(3, flow.Branches.Count); // 조건 1 + 옵션 2

        // 깊이: 옵션 라벨·본문은 2(조건+선택), 선택이 닫힌 합류 줄은 다시 조건 안 1.
        Assert.Equal(2, flow.Lines.Single(line => line.Line.LineId == body1).Depth);
        Assert.Equal(2, flow.Lines.Single(line => line.Line.LineId == label2).Depth);
        Assert.Equal(1, flow.Lines.Single(line => line.Line.LineId == joined).Depth);

        // 안쪽 갈래 표시는 옵션이고, 바깥 조건 갈래는 선택지 줄까지 덮는다.
        Assert.Equal(label1, flow.Lines.Single(line => line.Line.LineId == body1).Branch!.OpenLineId);
        ConditionBranch condition = flow.Branches.Single(branch => !branch.IsChoice);
        Assert.Equal(opener, condition.OpenLineId);
        Assert.True(condition.LastLineIndex >=
            flow.Lines.Single(line => line.Line.LineId == body2).Index);

        // 발행도 막히지 않는다.
        sample.Editor.PublishDialogue(sample.Dialogue.Id);
    }

    [Fact]
    public void 선택이_닫히기_전의_조건_전환은_여전히_MixedChain이다()
    {
        // 조건 종료(EndIf)는 W55로 선택지를 함께 닫지만, elseif 같은 갈래 전환은 여전히 위반이다.
        var sample = new Sample();
        sample.Line("조건 시작", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Line("선택 시작", LineConditionTransition.BeginChoice());
        sample.Line("끝", LineConditionTransition.BeginElseIf(sample.ConditionB.Id)); // 선택지 끝 없이 갈래 전환

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);
        Assert.Contains(flow.Problems, problem => problem.Kind == FlowProblemKind.MixedChain);

        Assert.Throws<Vn.Authoring.Editing.PublishRejectedException>(
            () => sample.Editor.PublishDialogue(sample.Dialogue.Id));
    }

    [Fact]
    public void 조건_종료_한_줄이_선택지도_함께_닫는다()
    {
        // W55 (소유자 지시): 선택지 끝과 조건 끝을 한 줄로 겹칠 수 있다.
        var sample = new Sample();
        sample.Line("조건 시작", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Line("사과", LineConditionTransition.BeginChoice());
        string body = sample.Line("사과 본문");
        string combined = sample.Line("모두 끝", LineConditionTransition.EndIf());
        sample.Line("바깥");

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);

        Assert.Empty(flow.Problems);
        Assert.Equal(0, flow.Lines.Single(line => line.Line.LineId == combined).Depth); // 둘 다 닫혔다

        // 중첩 선택지 안의 드롭다운에는 결합 종료 항목이 제시된다.
        ResolvedLine inside = flow.Lines.Single(line => line.Line.LineId == combined);
        Assert.True(inside.PrecedingBranch is { IsChoice: true } && inside.PrecedingDepth == 2);
        IReadOnlyList<ConditionChoice> choices = ConditionChoices.For(
            inside.PrecedingBranch,
            sample.Dialogue,
            sample.Project,
            choiceInsideCondition: true);
        Assert.Contains(choices, choice =>
            choice.Kind == ConditionChoiceKind.EndIf &&
            choice.Label == ConditionChoices.EndChoiceAndIfLabel);

        sample.Editor.PublishDialogue(sample.Dialogue.Id); // 발행도 막히지 않는다
        _ = body;
    }

    [Fact]
    public void 끝까지_열린_선택_블록은_발행되고_알림만_남는다()
    {
        // 규칙 개정 (2026-08-14 소유자 승인, 2단계 포트 규칙) — 선택지로 끝나는 노드는
        // 정상이다. 옵션들이 곧 노드의 끝이고, 이어진 옵션은 출구로 점프하며 안 이은
        // 옵션은 에피소드 종료다. 예전에는 발행을 막았다(Gate B 반칸의 정체).
        var sample = new Sample();
        sample.Line("라벨", LineConditionTransition.BeginChoice());
        sample.Line("본문");

        Vn.Authoring.Results.DialogueDraft draft =
            sample.Editor.InspectDialoguePublish(sample.Dialogue.Id);

        Assert.True(draft.CanPublish);
        Assert.Contains(draft.Problems, problem =>
            !problem.IsBlocking && problem.Message.Contains("문서 끝까지 열려"));

        sample.Editor.PublishDialogue(sample.Dialogue.Id); // 막히지 않는다
    }

    // ── OptionId 정체성 ─────────────────────────────────────────────────────

    [Fact]
    public void 편집기는_OptionId를_발급하고_전환을_다시_지정해도_잇는다()
    {
        var sample = new Sample();
        string label = sample.Line("라벨", LineConditionTransition.BeginChoice());

        string firstId = sample.Dialogue.FindExtension(label)!.Transition!.OptionId!;
        Assert.StartsWith("op_", firstId, StringComparison.Ordinal);

        // 같은 줄의 전환 종류를 바꿔도 옵션의 정체성은 그대로다.
        sample.Editor.SetLineTransition(
            sample.Dialogue.Id,
            label,
            LineConditionTransition.BeginNextOption());

        Assert.Equal(firstId, sample.Dialogue.FindExtension(label)!.Transition!.OptionId);
    }

    [Fact]
    public void 선택_전환은_OptionId까지_저장_왕복된다()
    {
        var sample = new Sample();
        string label = sample.Line("라벨", LineConditionTransition.BeginChoice());
        sample.Line("끝", LineConditionTransition.EndChoice());
        string optionId = sample.Dialogue.FindExtension(label)!.Transition!.OptionId!;

        StoryProject reloaded = ProjectSnapshotCodec.Decode(ProjectSnapshotCodec.Encode(sample.Project));
        LineConditionTransition transition =
            reloaded.FindDialogue(sample.Dialogue.Id)!.FindExtension(label)!.Transition!;

        Assert.Equal(ConditionTransitionKind.BeginChoice, transition.Kind);
        Assert.Equal(optionId, transition.OptionId);
    }

    // ── 발행 ────────────────────────────────────────────────────────────────

    [Fact]
    public void 발행_결과에_OptionId가_얼어붙고_스키마는_지금_판이다()
    {
        var sample = new Sample();
        string label = sample.Line("라벨", LineConditionTransition.BeginChoice());
        sample.Line("본문");
        sample.Line("끝", LineConditionTransition.EndChoice());
        string optionId = sample.Dialogue.FindExtension(label)!.Transition!.OptionId!;

        DialogueResult result = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        // ⚠ 숫자를 손으로 적지 않는다 — 규격이 오를 때마다 이 줄이 걸리는데, 여기서
        //    보려는 것은 <b>발행이 지금 판으로 찍히는가</b>이지 그 숫자가 몇인가가 아니다.
        //    (v3: 선택 전환 · v4: 꼬리 전환 — 이력은 `DialogueResult`에 적혀 있다.)
        Assert.Equal(DialogueResult.CurrentSchemaVersion, result.Identity.SchemaVersion);
        Assert.Equal(optionId, result.FindLine(label)!.Transition!.OptionId);

        // 결과 저장 왕복에서도 유지된다.
        ResultRepository reloaded = ResultStoreJson.Read(ResultStoreJson.Write(sample.Project.Results));
        Assert.Equal(
            optionId,
            reloaded.DialogueResults.Single().FindLine(label)!.Transition!.OptionId);
    }

    [Fact]
    public void 옵션_순서를_바꾸면_발행_검증이_세이브_경고를_낸다()
    {
        var sample = new Sample();
        string label1 = sample.Line("옵션 A", LineConditionTransition.BeginChoice());
        sample.Line("본문 A");
        string label2 = sample.Line("옵션 B", LineConditionTransition.BeginNextOption());
        sample.Line("본문 B");
        sample.Line("끝", LineConditionTransition.EndChoice());

        string idA = sample.Dialogue.FindExtension(label1)!.Transition!.OptionId!;
        string idB = sample.Dialogue.FindExtension(label2)!.Transition!.OptionId!;
        sample.Editor.PublishDialogue(sample.Dialogue.Id);

        // 두 옵션의 정체성을 맞바꾼다 — 리플레이 인덱스가 다른 선택지를 가리키게 된다.
        sample.Editor.SetLineTransition(sample.Dialogue.Id, label1, LineConditionTransition.BeginChoice(idB));
        sample.Editor.SetLineTransition(sample.Dialogue.Id, label2, LineConditionTransition.BeginNextOption(idA));

        DialogueDraft draft = DialoguePublisher.Draft(sample.Project, sample.Dialogue.Id);

        PublishProblem warning = Assert.Single(
            draft.Problems,
            problem => problem.Kind == PublishProblemKind.ChoiceOrderChanged);
        Assert.False(warning.IsBlocking);
        Assert.Contains("다른 선택지를 리플레이", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 라벨_문구만_고치면_순서_경고가_없다()
    {
        var sample = new Sample();
        string label = sample.Line("원래 라벨", LineConditionTransition.BeginChoice());
        sample.Line("본문");
        sample.Line("끝", LineConditionTransition.EndChoice());
        sample.Editor.PublishDialogue(sample.Dialogue.Id);

        sample.Editor.SetScriptLineText(sample.Script.Id, label, string.Empty, "고친 라벨");

        DialogueDraft draft = DialoguePublisher.Draft(sample.Project, sample.Dialogue.Id);
        Assert.DoesNotContain(
            draft.Problems,
            problem => problem.Kind == PublishProblemKind.ChoiceOrderChanged);
    }

    // ── 합성(Segment) ───────────────────────────────────────────────────────

    [Fact]
    public void 라벨은_ChoiceOption으로_본문은_들여쓴_일반_라인으로_펼쳐진다()
    {
        var sample = new Sample();
        string label = sample.Line("안전한 길", LineConditionTransition.BeginChoice());
        sample.Editor.SetLineSetOperations(sample.Dialogue.Id, label, new[]
        {
            new SetOperation { Variable = "Fatigue", Operator = SetOperatorKind.Add, Value = "10" },
            new SetOperation { Variable = "route", Operator = SetOperatorKind.Assign, Value = "\"a\"" },
            new SetOperation { Variable = "risk", Operator = SetOperatorKind.Subtract, Value = "1" }
        });
        string body = sample.Line("본문");
        sample.Line("끝", LineConditionTransition.EndChoice());

        DialogueResult result = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;
        RenderedDocument document = ResultDocumentComposer.Compose(result, project: sample.Project);

        RenderedSegment option = document.Segments.Single(segment =>
            segment.Kind == RenderedSegmentKind.ChoiceOption);
        Assert.Equal("안전한 길", option.Text);
        Assert.Equal(0, option.ChoiceBlockOrdinal);
        Assert.Equal(0, option.ChoiceOptionIndex);

        // D5 — 정수 증감만 태그가 되고 키는 소문자다. 대입과 비정수는 태그를 만들지 않는다.
        Assert.Equal(new[] { "#fatigue:+10", "#risk:-1" }, option.Tags);

        // 라벨은 일반 대사 라인으로 나오지 않는다.
        Assert.DoesNotContain(document.Segments, segment =>
            segment.Kind == RenderedSegmentKind.DialogueLine && segment.Source.LineId == label);

        RenderedSegment bodyLine = document.Segments.Single(segment =>
            segment.Kind == RenderedSegmentKind.DialogueLine && segment.Source.LineId == body);
        Assert.Equal(1, bodyLine.IndentLevel);

        // 발행 검증이 대문자 변수 소문자화를 알렸다.
        DialogueDraft draft = DialoguePublisher.Draft(sample.Project, sample.Dialogue.Id);
        Assert.Contains(draft.Problems, problem =>
            problem.Kind == PublishProblemKind.ChoicePreviewNotice && !problem.IsBlocking);
    }

    // ── 이미터 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Story는_라벨과_효과_태그와_동기_변수를_내고_Pres는_합성_조건으로_같은_갈래를_탄다()
    {
        ChoiceWorld world = BuildChoiceWorld();

        YarnBundle bundle = YarnBundleEmitter.Emit(
            world.Dialogue,
            world.Presentation,
            world.Sample.Project,
            Sample.Definition,
            bundleName: "choices_ep");

        Assert.False(bundle.HasBlockingProblems);

        // Story: 라벨은 접두 없이(D6), 태그는 표시 전용(D5), 본문 첫 문장은 동기 변수 set.
        Assert.Contains(
            $"-> 안전한 길을 따라간다 #fatigue:+10 #common_ingredient:+15 #line:{world.Label1}",
            bundle.StoryText,
            StringComparison.Ordinal);
        // 옵션 본문의 실제 효과는 작가 변수 set이다 — 챕터 접두를 받는다 (2026-08-17).
        Assert.Contains(
            "    <<set $__t1_sf_test_fatigue += 10>>",
            bundle.StoryText,
            StringComparison.Ordinal);

        // 옵션 출구는 본문 끝의 jump다. 2026-08-18까지는 그 앞에 <<pres_end>>가 붙었는데
        // 서브 레인이 없어져 함께 사라졌다.
        Assert.Contains("    <<jump A로_간다>>", bundle.StoryText, StringComparison.Ordinal);
        Assert.DoesNotContain("pres_end", bundle.StoryText, StringComparison.Ordinal);

        // 합성 추적 변수(`$__ch_N`)도 사라졌다 — 서브 레인 사본이 같은 갈래를 타게 하려고
        // 두었던 것이라, 레인이 없어지자 쓸 곳이 없다. 선언 파일에도 나오지 않는다.
        Assert.DoesNotContain("__ch_", bundle.StoryText, StringComparison.Ordinal);
        Assert.DoesNotContain("__ch_", YarnBundleEmitter.ComposeDeclarationsText(new[] { bundle }) ?? string.Empty,
            StringComparison.Ordinal);
    }

    private static int CountPlainLines(string yarn)
    {
        return yarn.Split('\n')
            .Select(line => line.Trim())
            .Count(line =>
                line.Length > 0 &&
                !line.StartsWith("<<", StringComparison.Ordinal) &&
                !line.StartsWith("->", StringComparison.Ordinal) &&
                !line.StartsWith("title:", StringComparison.Ordinal) &&
                line != "---" &&
                line != "===");
    }

    /// <summary>
    /// options.yarn 상당: 선택 블록 2개, 본문 라인·set·정수 효과·옵션 출구·라인 연출 포함.
    /// </summary>
    internal static ChoiceWorld BuildChoiceWorld()
    {
        var sample = new Sample();

        sample.Line("숲 입구에 도착했다.");

        string label1 = sample.Line("안전한 길을 따라간다", LineConditionTransition.BeginChoice());
        sample.Editor.SetLineSetOperations(sample.Dialogue.Id, label1, new[]
        {
            new SetOperation { Variable = "fatigue", Operator = SetOperatorKind.Add, Value = "10" },
            new SetOperation { Variable = "common_ingredient", Operator = SetOperatorKind.Add, Value = "15" }
        });
        string body1 = sample.Line("안전한 길에서 평범한 재료를 얻었다.");

        string label2 = sample.Line("깊은 숲으로 들어간다", LineConditionTransition.BeginNextOption());
        sample.Editor.SetLineSetOperations(sample.Dialogue.Id, label2, new[]
        {
            new SetOperation { Variable = "fatigue", Operator = SetOperatorKind.Add, Value = "30" },
            new SetOperation { Variable = "risk", Operator = SetOperatorKind.Add, Value = "1" }
        });
        sample.Line("숲 깊은 곳에서 희귀한 향신료를 발견했다.");

        sample.Line("좋아. 가보자.", LineConditionTransition.EndChoice());

        string label3 = sample.Line("바로 돌아간다", LineConditionTransition.BeginChoice());
        sample.Line("돌아가는 길.");
        sample.Line("조금 더 머문다", LineConditionTransition.BeginNextOption());
        sample.Line("조금 더 머물렀다.");
        sample.Line("끝.", LineConditionTransition.EndChoice());

        // 첫 블록의 옵션이 아니라 두 번째 블록의 첫 옵션에 출구를 단다.
        sample.Editor.SetExitTarget(
            sample.Dialogue.Id,
            ExitPortKind.Branch,
            label3,
            sample.TargetA.Id);
        sample.Editor.SetExitTarget(
            sample.Dialogue.Id,
            ExitPortKind.Default,
            null,
            sample.TargetDefault.Id);

        DialogueResult dialogue = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        PresentationNode node = sample.Editor.AddPresentationNode(sample.File.Id, name: "선택 연출");
        sample.Editor.SetPresentationSource(node.Id, dialogue.Identity.ResultId, dialogue.Identity.Version);
        sample.Editor.AddPresentationCommand(node.Id, body1, "camera.closeup");
        PresentationResult presentation = sample.Editor.PublishPresentation(node.Id).Result;

        return new ChoiceWorld(sample, label1, label2, dialogue, node, presentation);
    }

    // ── 조건 안 선택지 (W54) ────────────────────────────────────────────────

    /// <summary>W54: 조건 갈래 안에서 닫히는 선택 블록 + 그 조건 갈래의 출구.</summary>
    internal static ChoiceWorld BuildNestedChoiceWorld()
    {
        var sample = new Sample();

        // 실컴파일까지 가는 월드다 — 조건 식은 Yarn 문법($ 접두)이어야 한다 (골든 월드와 동일).
        sample.SetNode.Assignments.Add(new VariableAssignment { Variable = "favor", Value = "0" });
        sample.Editor.UpdateCondition(sample.ConditionA.Id, "호감 높음", "$favor >= 5");

        sample.Line("가게 앞이다.");
        string opener = sample.Line("주인이 있다", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        string label1 = sample.Line("치킨을 산다", LineConditionTransition.BeginChoice());
        string body1 = sample.Line("치킨을 샀다.");
        string label2 = sample.Line("그냥 나온다", LineConditionTransition.BeginNextOption());
        sample.Line("빈손으로 나왔다.");
        sample.Line("주인이 인사했다.", LineConditionTransition.EndChoice());
        sample.Line("가게를 나섰다.", LineConditionTransition.EndIf());
        sample.Line("집으로 간다.");

        // 조건 갈래의 출구 — 안의 선택지를 다 지나고 나서 점프해야 한다.
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, opener, sample.TargetA.Id);
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Default, null, sample.TargetDefault.Id);

        DialogueResult dialogue = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        PresentationNode node = sample.Editor.AddPresentationNode(sample.File.Id, name: "중첩 연출");
        sample.Editor.SetPresentationSource(node.Id, dialogue.Identity.ResultId, dialogue.Identity.Version);
        sample.Editor.AddPresentationCommand(node.Id, body1, "camera.closeup");
        PresentationResult presentation = sample.Editor.PublishPresentation(node.Id).Result;

        return new ChoiceWorld(sample, label1, label2, dialogue, node, presentation);
    }

    [Fact]
    public void 조건_안_선택지는_들여쓴_옵션으로_나오고_조건_출구는_선택_뒤에_detour한다()
    {
        ChoiceWorld world = BuildNestedChoiceWorld();

        YarnBundle bundle = YarnBundleEmitter.Emit(
            world.Dialogue,
            world.Presentation,
            world.Sample.Project,
            Sample.Definition,
            bundleName: "nested_ep");
        string story = bundle.Files.Single(file => file.FileName == "nested_ep.yarn").Text;

        Assert.Contains("\n    -> 치킨을 산다", story);   // 라벨이 조건 깊이만큼 들여쓰였다
        Assert.Contains("\n        치킨을 샀다.", story);  // 옵션 본문은 라벨보다 한 단 더

        // 동기 변수(`$__ch_N`)는 2026-08-18에 사라졌다 — 서브 레인 사본이 같은 갈래를
        // 타게 하려던 것이라, 레인이 없어지자 쓸 곳이 없다.
        Assert.DoesNotContain("__ch_", story, StringComparison.Ordinal);

        // 조건 갈래 출구(detour)는 선택 블록의 마지막 본문 뒤, endif 앞에서 나온다 —
        // 선택 전환에 새어 나오면 선택지가 제시되기 전에 떠나 버린다.
        int lastOptionBody = story.IndexOf("빈손으로 나왔다.", StringComparison.Ordinal);
        int detour = story.IndexOf("<<detour ", StringComparison.Ordinal);
        int endif = story.IndexOf("<<endif>>", StringComparison.Ordinal);
        Assert.True(lastOptionBody >= 0 && lastOptionBody < detour && detour < endif, story);
    }

    internal sealed record ChoiceWorld(
        Sample Sample,
        string Label1,
        string Label2,
        DialogueResult Dialogue,
        PresentationNode PresentationNode,
        PresentationResult Presentation);
}
