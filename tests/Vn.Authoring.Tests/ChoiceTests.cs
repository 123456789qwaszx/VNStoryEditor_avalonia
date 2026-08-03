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
    public void 조건과_선택이_겹치면_MixedChain으로_알리고_발행을_막는다()
    {
        var sample = new Sample();
        sample.Line("if 안", LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Line("선택 시작", LineConditionTransition.BeginChoice());
        sample.Line("끝", LineConditionTransition.EndChoice());

        DialogueFlow flow = ConditionFlowResolver.Resolve(sample.Dialogue, sample.Project);
        Assert.Contains(flow.Problems, problem => problem.Kind == FlowProblemKind.MixedChain);

        Assert.Throws<Vn.Authoring.Editing.PublishRejectedException>(
            () => sample.Editor.PublishDialogue(sample.Dialogue.Id));
    }

    [Fact]
    public void 닫히지_않은_선택_블록은_발행을_막는다()
    {
        var sample = new Sample();
        sample.Line("라벨", LineConditionTransition.BeginChoice());
        sample.Line("본문");

        Assert.Throws<Vn.Authoring.Editing.PublishRejectedException>(
            () => sample.Editor.PublishDialogue(sample.Dialogue.Id));
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
    public void 발행_결과에_OptionId가_얼어붙고_스키마는_v3이다()
    {
        var sample = new Sample();
        string label = sample.Line("라벨", LineConditionTransition.BeginChoice());
        sample.Line("본문");
        sample.Line("끝", LineConditionTransition.EndChoice());
        string optionId = sample.Dialogue.FindExtension(label)!.Transition!.OptionId!;

        DialogueResult result = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        Assert.Equal(3, result.Identity.SchemaVersion);
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
        Assert.Contains("    <<set $__ch_0 = 0>>\n    <<set $fatigue += 10>>", bundle.StoryText, StringComparison.Ordinal);
        Assert.Contains("    <<set $__ch_0 = 1>>", bundle.StoryText, StringComparison.Ordinal);
        Assert.Contains("    <<set $__ch_1 = 0>>", bundle.StoryText, StringComparison.Ordinal);

        // 옵션 출구는 본문 끝에서 pres_end 뒤 jump다 (A5).
        Assert.Contains("    <<pres_end>>\n    <<jump Story_A로_간다>>", bundle.StoryText, StringComparison.Ordinal);

        // Pres: 라벨 사본 없이 합성 조건으로 갈라진다. set은 복제되지 않는다 (D2).
        Assert.Contains("<<if $__ch_0 == 0>>", bundle.PresText!, StringComparison.Ordinal);
        Assert.Contains("<<elseif $__ch_0 == 1>>", bundle.PresText!, StringComparison.Ordinal);
        Assert.Contains("<<endif>>", bundle.PresText!, StringComparison.Ordinal);
        Assert.DoesNotContain("->", bundle.PresText!, StringComparison.Ordinal);
        Assert.DoesNotContain("<<set", bundle.PresText!, StringComparison.Ordinal);
        Assert.DoesNotContain("#line:", bundle.PresText!, StringComparison.Ordinal);

        // 동기 변수는 선언 파일에 들어간다.
        string declarations = YarnBundleEmitter.ComposeDeclarationsText(new[] { bundle })!;
        Assert.Contains("<<declare $__ch_0 = 0>>", declarations, StringComparison.Ordinal);
        Assert.Contains("<<declare $__ch_1 = 0>>", declarations, StringComparison.Ordinal);
    }

    [Fact]
    public void Pres_사본의_라인_수는_Story_본문_라인_수와_같다()
    {
        ChoiceWorld world = BuildChoiceWorld();

        YarnBundle bundle = YarnBundleEmitter.Emit(
            world.Dialogue,
            world.Presentation,
            world.Sample.Project,
            Sample.Definition,
            bundleName: "choices_ep");

        // 계약서 B — 라벨(-> 라인)은 advance 0이므로 사본에서 빠지고,
        // 본문 라인 수는 양쪽이 정확히 같아야 락스텝이 유지된다.
        Assert.Equal(
            CountPlainLines(bundle.StoryText),
            CountPlainLines(bundle.PresText!));
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

    internal sealed record ChoiceWorld(
        Sample Sample,
        string Label1,
        string Label2,
        DialogueResult Dialogue,
        PresentationNode PresentationNode,
        PresentationResult Presentation);
}
