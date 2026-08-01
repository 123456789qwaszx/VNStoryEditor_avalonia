using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
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
            _ = DialogueDocumentComposer.ComposePreset(
                sample.Sample.Project,
                sample.Sample.Dialogue.Id,
                preset);
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
        Assert.Contains(document.Segments, segment => segment.Kind == RenderedSegmentKind.BranchJump);
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
            segment.Kind is RenderedSegmentKind.BranchJump or RenderedSegmentKind.DefaultJump);

        string text = DocumentPreviewFormatter.Format(document);
        Assert.Contains("[장면]", text, StringComparison.Ordinal);
        Assert.Contains("[조건:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("#line:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<<set", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Recording_Script는_CharacterActing과_LineId_대사만_남긴다()
    {
        PresetSample sample = BuildPresetSample();

        RenderedDocument document = Compose(sample, OutputPresetCatalog.RecordingScript);
        RenderedSegment[] commands = document.Segments
            .Where(segment => segment.Kind == RenderedSegmentKind.PresentationCommand)
            .ToArray();

        RenderedSegment command = Assert.Single(commands);
        Assert.Equal(PresentationCategory.CharacterActing, command.PresentationCategory);
        Assert.All(document.Segments, segment => Assert.True(
            segment.Kind is RenderedSegmentKind.PresentationCommand or RenderedSegmentKind.DialogueLine));

        string text = DocumentPreviewFormatter.Format(document);
        Assert.Contains("[연기]", text, StringComparison.Ordinal);
        Assert.Contains($"[LINE {sample.Opening.Id}]", text, StringComparison.Ordinal);
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
        Assert.Contains($"{sample.Opening.Id}\t라루\t첫 대사\t", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<<", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Direction_Sheet는_세_연출_범주와_LineId_참고_대사를_남긴다()
    {
        PresetSample sample = BuildPresetSample();

        RenderedDocument document = Compose(sample, OutputPresetCatalog.DirectionSheet);
        PresentationCategory?[] categories = document.Segments
            .Where(segment => segment.Kind == RenderedSegmentKind.PresentationCommand)
            .Select(segment => segment.PresentationCategory)
            .ToArray();

        Assert.Equal(
            new PresentationCategory?[]
            {
                PresentationCategory.Camera,
                PresentationCategory.ScreenEffect,
                PresentationCategory.CharacterActing
            },
            categories);
        Assert.DoesNotContain(document.Segments, segment => segment.Kind == RenderedSegmentKind.SetAssignment);
        Assert.DoesNotContain(document.Segments, segment => segment.Kind == RenderedSegmentKind.ConditionBegin);

        string text = DocumentPreviewFormatter.Format(document);
        Assert.Contains("[Camera]", text, StringComparison.Ordinal);
        Assert.Contains("[ScreenEffect]", text, StringComparison.Ordinal);
        Assert.Contains("[CharacterActing]", text, StringComparison.Ordinal);
        Assert.Contains($"[LINE {sample.Opening.Id}] 첫 대사", text, StringComparison.Ordinal);
        Assert.DoesNotContain("라루:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_Provider의_번역문은_StoryProject가_아닌_Segment에만_들어간다()
    {
        PresetSample sample = BuildPresetSample();
        string before = ProjectSnapshotCodec.Encode(sample.Sample.Project);
        var provider = new DictionaryLocalizedLineProvider(new Dictionary<string, string>
        {
            [sample.Opening.Id] = "First line"
        });

        RenderedDocument document = DialogueDocumentComposer.ComposePreset(
            sample.Sample.Project,
            sample.Sample.Dialogue.Id,
            OutputPresetCatalog.LocalizationScript,
            localization: provider);

        RenderedSegment opening = document.Segments.Single(segment =>
            segment.Source.LineId == sample.Opening.Id);
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
                Assert.False(string.IsNullOrWhiteSpace(segment.Source.StoryFileId));
                Assert.False(string.IsNullOrWhiteSpace(segment.Source.NodeId));
            }
        }
    }

    private static RenderedDocument Compose(PresetSample sample, OutputPreset preset)
    {
        return DialogueDocumentComposer.ComposePreset(
            sample.Sample.Project,
            sample.Sample.Dialogue.Id,
            preset);
    }

    private static PresetSample BuildPresetSample()
    {
        var sample = new Sample();
        sample.SetNode.Assignments.Add(new VariableAssignment { Variable = "favor", Value = "0" });

        LineBox opening = sample.Line(
            "첫 대사",
            LineConditionTransition.BeginIf(sample.ConditionA.Id));
        sample.Editor.SetLineText(sample.Dialogue.Id, opening.Id, "라루", "첫 대사");
        LineBox ending = sample.Line("조건 뒤", LineConditionTransition.EndIf());
        sample.Editor.SetLineText(sample.Dialogue.Id, ending.Id, "윌로", "조건 뒤");
        sample.Editor.SetExitTarget(
            sample.Dialogue.Id,
            ExitPortKind.Branch,
            opening.Id,
            sample.TargetA.Id);
        sample.Editor.SetExitTarget(
            sample.Dialogue.Id,
            ExitPortKind.Default,
            null,
            sample.TargetDefault.Id);

        PresentationNode presentation = sample.Editor.AddPresentationNode(
            sample.File.Id,
            name: "통합 연출");
        sample.Editor.SetPresentationTarget(presentation.Id, sample.Dialogue.Id);
        sample.Editor.AddPresentationCommand(presentation.Id, opening.Id, "camera.closeup");
        sample.Editor.AddPresentationCommand(presentation.Id, opening.Id, "screen.shake");
        sample.Editor.AddPresentationCommand(presentation.Id, opening.Id, "acting.smile");

        return new PresetSample(sample, opening);
    }

    private sealed record PresetSample(Sample Sample, LineBox Opening);
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
