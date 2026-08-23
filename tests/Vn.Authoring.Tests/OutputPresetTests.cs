using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

/// <summary>
/// 출력 프리셋은 같은 StoryProject를 서로 다른 읽기 문서로 투영할 뿐,
/// 공식 원본과 Snapshot을 수정하지 않아야 한다.
/// </summary>
public class OutputPresetTests
{
    [Fact]
    public void 기본_프리셋_다섯_개를_결정적인_순서로_제공한다()
    {
        Assert.Equal(
            new[]
            {
                OutputPresetId.RuntimeFull,
                OutputPresetId.ScenarioOnly,
                OutputPresetId.RecordingScript,
                OutputPresetId.LocalizationScript,
                OutputPresetId.DirectionSheet
            },
            OutputPresetCatalog.All.Select(preset => preset.Id));

        Assert.Equal("Runtime Full", OutputPresetCatalog.RuntimeFull.DisplayName);
        Assert.Same(
            OutputPresetCatalog.DirectionSheet,
            OutputPresetCatalog.Get(OutputPresetId.DirectionSheet));
    }

    [Fact]
    public void 프리셋_합성은_StoryProject와_Snapshot을_수정하지_않는다()
    {
        PresetSample sample = BuildPresetSample();
        string before = ProjectSnapshotCodec.Encode(sample.Sample.Project);

        foreach (OutputPreset preset in OutputPresetCatalog.All)
        {
            _ = Compose(sample, preset);
        }

        string after = ProjectSnapshotCodec.Encode(sample.Sample.Project);
        Assert.Equal(before, after);
    }

    [Fact]
    public void Runtime_Full은_모든_저작_레이어를_포함한다()
    {
        PresetSample sample = BuildPresetSample();

        RenderedDocument document = Compose(sample, OutputPresetCatalog.RuntimeFull);

        Assert.Contains(document.Segments, segment => segment.Kind == RenderedSegmentKind.NodeHeader);
        Assert.Contains(document.Segments, segment => segment.Kind == RenderedSegmentKind.SetAssignment);
        Assert.Contains(document.Segments, segment => segment.Kind == RenderedSegmentKind.ConditionBegin);
        Assert.Contains(document.Segments, segment => segment.Kind == RenderedSegmentKind.PresentationCommand);
        Assert.Contains(document.Segments, segment => segment.Kind == RenderedSegmentKind.DialogueLine);
        Assert.Contains(document.Segments, segment => segment.Kind == RenderedSegmentKind.BranchDetour);
        Assert.Contains(document.Segments, segment => segment.Kind == RenderedSegmentKind.DefaultJump);
        Assert.Equal(OutputPresetId.RuntimeFull, document.PresetId);
    }

    [Fact]
    public void Scenario_Only는_조건과_대사만_남긴다()
    {
        PresetSample sample = BuildPresetSample();

        RenderedDocument document = Compose(sample, OutputPresetCatalog.ScenarioOnly);

        Assert.Contains(document.Segments, segment => segment.Kind == RenderedSegmentKind.NodeHeader);
        Assert.Contains(document.Segments, segment => segment.Kind == RenderedSegmentKind.ConditionBegin);
        Assert.Contains(document.Segments, segment => segment.Kind == RenderedSegmentKind.DialogueLine);
        Assert.DoesNotContain(document.Segments, segment => segment.Kind == RenderedSegmentKind.SetAssignment);
        Assert.DoesNotContain(document.Segments, segment => segment.Kind == RenderedSegmentKind.PresentationCommand);
        Assert.DoesNotContain(document.Segments, segment =>
            segment.Kind is RenderedSegmentKind.BranchJump
                or RenderedSegmentKind.BranchDetour
                or RenderedSegmentKind.DefaultJump);

        string text = DocumentPreviewFormatter.Format(document);
        Assert.Contains("[장면]", text, StringComparison.Ordinal);

        // X11 — 조건은 대괄호 표기가 아니라 실제 Yarn 문법이다(표기=파싱 문법, X12 왕복 전제).
        Assert.Contains("<<if ", text, StringComparison.Ordinal);
        Assert.Contains("<<endif>>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[조건", text, StringComparison.Ordinal);
        Assert.DoesNotContain("#line:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<<set", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Recording_Script는_LineId_화자_대사만_남긴다()
    {
        // 소유자 4형식 매핑의 2번(LineId+대본). 연출은 Direction Sheet의 몫이다.
        PresetSample sample = BuildPresetSample();

        RenderedDocument document = Compose(sample, OutputPresetCatalog.RecordingScript);

        Assert.All(document.Segments, segment =>
            Assert.Equal(RenderedSegmentKind.DialogueLine, segment.Kind));

        string text = DocumentPreviewFormatter.Format(document);
        Assert.Contains($"[LINE {sample.Opening}]", text, StringComparison.Ordinal);
        Assert.Contains("라루:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Camera", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ScreenEffect", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_Script는_LineId_화자_원문_표를_만든다()
    {
        PresetSample sample = BuildPresetSample();

        RenderedDocument document = Compose(sample, OutputPresetCatalog.LocalizationScript);

        Assert.All(document.Segments, segment =>
            Assert.Equal(RenderedSegmentKind.DialogueLine, segment.Kind));

        string text = DocumentPreviewFormatter.Format(document);
        Assert.StartsWith("LineId\tSpeaker\tSourceText\tLocalizedText\n", text, StringComparison.Ordinal);
        Assert.Contains($"{sample.Opening}\t라루\t첫 대사\t", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<<", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Direction_Sheet는_모든_연출_범주와_LineId_참고_대사를_남긴다()
    {
        PresetSample sample = BuildPresetSample();

        RenderedDocument document = Compose(sample, OutputPresetCatalog.DirectionSheet);
        string?[] categories = document.Segments
            .Where(segment => segment.Kind == RenderedSegmentKind.PresentationCommand)
            .Select(segment => segment.PresentationCategoryId)
            .ToArray();

        Assert.Equal(
            new[] { "camera", "screen_effect", "character_acting" },
            categories);
        Assert.DoesNotContain(document.Segments, segment => segment.Kind == RenderedSegmentKind.SetAssignment);
        Assert.DoesNotContain(document.Segments, segment => segment.Kind == RenderedSegmentKind.ConditionBegin);

        string text = DocumentPreviewFormatter.Format(document);
        Assert.Contains("[Camera]", text, StringComparison.Ordinal);
        Assert.Contains("[ScreenEffect]", text, StringComparison.Ordinal);
        Assert.Contains("[CharacterActing]", text, StringComparison.Ordinal);
        Assert.Contains($"[LINE {sample.Opening}]", text, StringComparison.Ordinal);
        Assert.Contains("첫 대사", text, StringComparison.Ordinal);

        // 2026-08-18 — 화자를 켰다. 이 양식이 **줄 단위로 연출을 훑는 유일한 자리**가
        // 됐기 때문이다: 런타임이 단일 대본만 읽게 되면서 `Pres_*.yarn`(서브 레인 사본)이
        // 없어졌고, 화자가 없으면 누구 대사인지 세어 가며 맞춰야 한다.
        Assert.Contains("라루", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_Provider의_번역문은_StoryProject가_아닌_Segment에만_들어간다()
    {
        PresetSample sample = BuildPresetSample();
        string before = ProjectSnapshotCodec.Encode(sample.Sample.Project);
        var provider = new DictionaryLocalizedLineProvider(new Dictionary<string, string>
        {
            [sample.Opening] = "First line"
        });

        RenderedDocument document = ResultDocumentComposer.Compose(
            sample.Dialogue,
            sample.Presentation,
            sample.Sample.Project,
            Sample.Definition,
            OutputPresetCatalog.LocalizationScript.Options,
            OutputPresetId.LocalizationScript,
            provider);

        RenderedSegment opening = document.Segments.Single(segment =>
            segment.Source.LineId == sample.Opening);
        Assert.Equal("First line", opening.LocalizedText);
        Assert.Equal(before, ProjectSnapshotCodec.Encode(sample.Sample.Project));
        Assert.Contains("First line", DocumentPreviewFormatter.Format(document), StringComparison.Ordinal);
    }

    [Fact]
    public void 모든_프리셋은_남아_있는_Segment의_source_mapping을_그대로_유지한다()
    {
        PresetSample sample = BuildPresetSample();
        RenderedDocument full = Compose(sample, OutputPresetCatalog.RuntimeFull);
        Dictionary<string, RenderSourceReference> fullSources = full.Segments.ToDictionary(
            segment => segment.Id,
            segment => segment.Source,
            StringComparer.Ordinal);

        foreach (OutputPreset preset in OutputPresetCatalog.All.Skip(1))
        {
            RenderedDocument document = Compose(sample, preset);

            foreach (RenderedSegment segment in document.Segments)
            {
                Assert.True(fullSources.TryGetValue(segment.Id, out RenderSourceReference? source));
                Assert.Equal(source, segment.Source);
                Assert.False(string.IsNullOrWhiteSpace(segment.Source.DialogueResultId));
                Assert.False(string.IsNullOrWhiteSpace(segment.Source.NodeId));
            }
        }
    }

    private static RenderedDocument Compose(PresetSample sample, OutputPreset preset)
    {
        return ResultDocumentComposer.Compose(
            sample.Dialogue,
            sample.Presentation,
            sample.Sample.Project,
            Sample.Definition,
            preset.Options,
            preset.Id);
    }

    private static PresetSample BuildPresetSample()
    {
        var sample = new Sample();
        sample.SetNode.Assignments.Add(new VariableAssignment { Variable = "favor", Value = "0" });

        string opening = sample.Line(
            "첫 대사",
            LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Editor.SetScriptLineText(sample.Script.Id, opening, "라루", "첫 대사");
        string ending = sample.Line("조건 뒤", LineConditionTransition.EndIf());
        sample.Editor.SetScriptLineText(sample.Script.Id, ending, "윌로", "조건 뒤");

        // ⚠ 작가가 <b>줄에</b> 단 set — 2026-08-24부터 문서에 남는 set은 이것뿐이다.
        // 설정노드의 초기값은 선언으로만 나간다(작업지시 §4).
        sample.Editor.SetLineSetOperations(sample.Dialogue.Id, opening, new[]
        {
            new SetOperation { Variable = "favor", Operator = SetOperatorKind.Add, Value = "1" }
        });

        sample.Editor.SetExitTarget(
            sample.Dialogue.Id,
            ExitPortKind.Branch,
            opening,
            sample.TargetA.Id);
        sample.Editor.SetExitTarget(
            sample.Dialogue.Id,
            ExitPortKind.Default,
            null,
            sample.TargetDefault.Id);

        DialogueResult dialogue = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        PresentationNode presentation = sample.Editor.AddPresentationNode(
            sample.File.Id,
            name: "통합 연출");
        sample.Editor.SetPresentationSource(
            presentation.Id,
            dialogue.Identity.ResultId,
            dialogue.Identity.Version);
        sample.Editor.AddPresentationCommand(presentation.Id, opening, "camera.closeup");
        sample.Editor.AddPresentationCommand(presentation.Id, opening, "screen.shake");
        sample.Editor.AddPresentationCommand(presentation.Id, opening, "acting.smile");

        PresentationResult published = sample.Editor.PublishPresentation(presentation.Id).Result;
        sample.Editor.AddComposition(
            dialogue.Identity.ResultId,
            dialogue.Identity.Version,
            published.Identity.ResultId,
            published.Identity.Version,
            name: "1장 합성");

        return new PresetSample(sample, opening, dialogue, published);
    }

    private sealed record PresetSample(
        Sample Sample,
        string Opening,
        DialogueResult Dialogue,
        PresentationResult Presentation);
    private sealed class DictionaryLocalizedLineProvider : ILocalizedLineProvider
    {
        private readonly IReadOnlyDictionary<string, string> _lines;

        public DictionaryLocalizedLineProvider(IReadOnlyDictionary<string, string> lines)
        {
            _lines = lines;
        }

        public string? GetLocalizedText(string lineId)
        {
            return _lines.TryGetValue(lineId, out string? text) ? text : null;
        }
    }

}
