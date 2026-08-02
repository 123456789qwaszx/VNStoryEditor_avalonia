using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// 정식 출력의 입력은 정확히 호환되는 두 결과뿐이다.
///
/// 어긋난 조합으로 만든 Runtime Full은 겉보기에 멀쩡하다. 대사와 연출이 어긋나 있다는
/// 사실을 아무도 알아채지 못한다는 것이 정확히 위험한 지점이다.
/// </summary>
public class RuntimeCompositionTests
{
    [Fact]
    public void 정확히_호환되는_두_결과는_합성된다()
    {
        var fixture = new SliceFixture();
        ResolvedComposition resolved = fixture.Resolve();

        Assert.True(resolved.IsCompatible);
        Assert.Empty(resolved.BlockingProblems);

        RenderedDocument document = ResultDocumentComposer.Compose(resolved, fixture.Project);
        Assert.True(document.IsPublished);
        Assert.Equal(fixture.Dialogue.Identity, document.DialogueResult);
        Assert.Equal(fixture.Presentation.Identity, document.PresentationResult);
    }

    [Fact]
    public void 다른_대사_결과_위에서_만들어진_연출은_합성되지_않는다()
    {
        var fixture = new SliceFixture();

        // 대본을 고쳐 v2를 만든다. 연출은 여전히 v1 위에서 만들어진 것이다.
        fixture.Sample.Editor.SetScriptLineText(
            fixture.Sample.Script.Id,
            fixture.LineId,
            "라루",
            "다른 대사");
        DialogueResult v2 = fixture.Sample.Editor.PublishDialogue(fixture.Sample.Dialogue.Id).Result;

        var mismatched = new RuntimeComposition(name: "어긋난 조합")
        {
            DialogueResultId = v2.Identity.ResultId,
            DialogueResultVersion = v2.Identity.Version,
            PresentationResultId = fixture.Presentation.Identity.ResultId,
            PresentationResultVersion = fixture.Presentation.Identity.Version
        };

        ResolvedComposition resolved = RuntimeCompositionResolver.Resolve(
            fixture.Project.Results,
            mismatched);

        Assert.False(resolved.IsCompatible);
        Assert.Contains(
            resolved.Problems,
            problem => problem.Kind == CompositionProblemKind.SourceMismatch);

        // 정식 문서로 만들어 주지 않는다.
        Assert.Throws<IncompatibleCompositionException>(
            () => ResultDocumentComposer.Compose(resolved, fixture.Project));
    }

    [Fact]
    public void 호환되지_않는_조합은_프로젝트에_저장할_수도_없다()
    {
        var fixture = new SliceFixture();
        fixture.Sample.Editor.SetScriptLineText(
            fixture.Sample.Script.Id,
            fixture.LineId,
            "라루",
            "다른 대사");
        DialogueResult v2 = fixture.Sample.Editor.PublishDialogue(fixture.Sample.Dialogue.Id).Result;

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            fixture.Sample.Editor.AddComposition(
                v2.Identity.ResultId,
                v2.Identity.Version,
                fixture.Presentation.Identity.ResultId,
                fixture.Presentation.Identity.Version));

        Assert.Contains("호환되지 않는", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 연출_없이도_대사만으로_정상_합성된다()
    {
        var fixture = new SliceFixture();
        RuntimeComposition dialogueOnly = fixture.Sample.Editor.AddComposition(
            fixture.Dialogue.Identity.ResultId,
            fixture.Dialogue.Identity.Version,
            name: "대사만");

        ResolvedComposition resolved = RuntimeCompositionResolver.Resolve(
            fixture.Project.Results,
            dialogueOnly);

        Assert.True(resolved.IsCompatible);
        RenderedDocument document = ResultDocumentComposer.Compose(resolved, fixture.Project);

        Assert.Null(document.PresentationResult);
        Assert.DoesNotContain(document.Segments, segment =>
            segment.Kind == RenderedSegmentKind.PresentationCommand);
        Assert.Contains(document.Segments, segment =>
            segment.Kind == RenderedSegmentKind.DialogueLine);
    }

    [Fact]
    public void 연출이_없는_LineId도_순서를_지키며_출력된다()
    {
        var fixture = new SliceFixture();
        RenderedDocument document = ResultDocumentComposer.Compose(
            fixture.Resolve(),
            fixture.Project,
            options: OutputPresetCatalog.DirectionSheet.Options,
            presetId: OutputPresetId.DirectionSheet);

        string[] lineOrder = document.Segments
            .Where(segment => segment.Kind == RenderedSegmentKind.DialogueLine)
            .Select(segment => segment.Source.LineId!)
            .ToArray();

        // 연출은 첫 줄에만 붙였지만 두 줄 모두 자리를 지킨다.
        Assert.Equal(new[] { fixture.LineId, fixture.SecondLineId }, lineOrder);
    }

    [Fact]
    public void 내용_해시가_어긋나면_합성하지_않는다()
    {
        var fixture = new SliceFixture();

        // Id와 버전은 같지만 해시가 다른 연출 결과. 파일을 도구 밖에서 고친 상황이다.
        var tampered = new PresentationResult(
            fixture.Presentation.Identity,
            fixture.Presentation.SourceNodeId,
            fixture.Presentation.SourceNodeName,
            new DialogueResultReference(
                fixture.Dialogue.Identity.ResultId,
                fixture.Dialogue.Identity.Version,
                "sha256:손으로바꾼값"),
            fixture.Presentation.Bindings,
            fixture.Presentation.PublishedAt);

        IReadOnlyList<CompositionProblem> problems =
            RuntimeCompositionResolver.CheckCompatibility(fixture.Dialogue, tampered);

        Assert.Contains(problems, problem =>
            problem.Kind == CompositionProblemKind.ContentHashMismatch && problem.IsBlocking);
    }

    [Fact]
    public void 대상_결과에_없는_LineId의_연출은_합성을_막지_않고_진단으로_남는다()
    {
        var fixture = new SliceFixture(withOrphan: true);
        ResolvedComposition resolved = fixture.Resolve();

        Assert.True(resolved.IsCompatible);
        Assert.Contains(
            resolved.Problems,
            problem => problem.Kind == CompositionProblemKind.OrphanBinding && !problem.IsBlocking);

        RenderedDocument document = ResultDocumentComposer.Compose(resolved, fixture.Project);
        Assert.Contains(document.Segments, segment =>
            segment.Kind == RenderedSegmentKind.Warning &&
            (segment.Text ?? string.Empty).Contains("ln_없는줄", StringComparison.Ordinal));
    }

    [Fact]
    public void 작업_중_미리보기는_발행_결과와_섞이지_않는다()
    {
        var fixture = new SliceFixture();

        RenderedDocument working = WorkingDialoguePreview.Compose(
            fixture.Project,
            fixture.Sample.Dialogue.Id);

        Assert.False(working.IsPublished);
        Assert.Equal(0, working.DialogueResult.Version);
        Assert.Null(working.PresentationResult);

        // 작업 중 결과는 어떤 연출과도 호환되지 않는다.
        Assert.False(fixture.Presentation.Source.Matches(working.DialogueResult));
    }
}

/// <summary>발행된 대사 결과와 연출 결과, 그리고 그 둘을 묶은 조합 하나.</summary>
internal sealed class SliceFixture
{
    public SliceFixture(bool withOrphan = false)
    {
        Sample = new Sample();
        LineId = Sample.Line("첫 대사");
        SecondLineId = Sample.Line("둘째 대사");
        Sample.Editor.SetExitTarget(
            Sample.Dialogue.Id,
            Vn.Authoring.Flow.ExitPortKind.Default,
            null,
            Sample.TargetDefault.Id);

        Dialogue = Sample.Editor.PublishDialogue(Sample.Dialogue.Id).Result;

        PresentationNode node = Sample.Editor.AddPresentationNode(Sample.File.Id, name: "연출");
        Sample.Editor.SetPresentationSource(
            node.Id,
            Dialogue.Identity.ResultId,
            Dialogue.Identity.Version);
        Sample.Editor.AddPresentationCommand(node.Id, LineId, "camera.closeup");

        if (withOrphan)
        {
            Sample.Editor.AddPresentationCommand(node.Id, "ln_없는줄", "screen.shake");
        }

        Presentation = Sample.Editor.PublishPresentation(node.Id).Result;
        Composition = Sample.Editor.AddComposition(
            Dialogue.Identity.ResultId,
            Dialogue.Identity.Version,
            Presentation.Identity.ResultId,
            Presentation.Identity.Version,
            name: "1장 합성");
    }

    public Sample Sample { get; }

    public StoryProject Project => Sample.Project;

    public string LineId { get; }

    public string SecondLineId { get; }

    public DialogueResult Dialogue { get; }

    public PresentationResult Presentation { get; }

    public RuntimeComposition Composition { get; }

    public ResolvedComposition Resolve() =>
        RuntimeCompositionResolver.Resolve(Project.Results, Composition);
}
