using Vn.Authoring.Definition;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Rendering;

/// <summary>
/// 아직 발행하지 않은 DialogueNode를 정식 출력과 <b>같은 합성기로</b> 펼쳐 보여 준다.
///
/// 미리 보기 전용 합성기를 따로 두면 화면에서 본 것과 발행된 것이 조금씩 다를 수 있고,
/// 그 차이는 아무도 찾지 못한다. 그래서 여기서는 발행 초안을 만들고, 버전 0짜리
/// 작업 중 결과로 감싼 다음, 정식 합성기에 그대로 넘긴다.
///
/// 결과는 <see cref="RenderedDocument.IsPublished"/>가 false다. 정식 결과와 헷갈릴 수 없고,
/// 어떤 PresentationResult와도 짝지어지지 않는다. 연출은 <b>발행된</b> 대사 결과 위에서만
/// 만들어지기 때문이다.
/// </summary>
public static class WorkingDialoguePreview
{
    public static RenderedDocument Compose(
        StoryProject project,
        string dialogueNodeId,
        GameDefinition? definition = null,
        DocumentOutputOptions? options = null,
        OutputPresetId? presetId = null,
        ILocalizedLineProvider? localization = null,
        string? locale = null,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        DialogueDraft draft = DialoguePublisher.Draft(project, dialogueNodeId, definition, locale);
        DialogueResult working = DialoguePublisher.AsWorkingResult(
            draft,
            now ?? DateTimeOffset.UnixEpoch);

        RenderedDocument document = ResultDocumentComposer.Compose(
            working,
            presentation: null,
            project,
            definition,
            options,
            presetId,
            localization);

        options ??= OutputPresetCatalog.RuntimeFull.Options;

        if (!options.IncludeDiagnostics || draft.Problems.Count == 0)
        {
            return document;
        }

        // 진단은 작업 중에만 의미가 있다. 발행된 결과에는 막는 문제가 남아 있을 수 없다.
        return new RenderedDocument(
            document.SourceNodeId,
            document.DialogueResult,
            document.PresentationResult,
            InsertProblems(document, draft),
            document.Options,
            document.PresetId);
    }

    public static RenderedDocument ComposePreset(
        StoryProject project,
        string dialogueNodeId,
        OutputPreset preset,
        GameDefinition? definition = null,
        ILocalizedLineProvider? localization = null,
        string? locale = null)
    {
        ArgumentNullException.ThrowIfNull(preset);

        return Compose(
            project,
            dialogueNodeId,
            definition,
            preset.Options,
            preset.Id,
            localization,
            locale);
    }

    private static List<RenderedSegment> InsertProblems(RenderedDocument document, DialogueDraft draft)
    {
        var segments = new List<RenderedSegment>(document.Segments);
        var warnings = new List<RenderedSegment>();

        for (int index = 0; index < draft.Problems.Count; index++)
        {
            PublishProblem problem = draft.Problems[index];

            warnings.Add(new RenderedSegment(
                Id: $"warning:{draft.SourceNodeId}:{index}",
                Kind: RenderedSegmentKind.Warning,
                Layer: DocumentLayer.Diagnostics,
                Source: new RenderSourceReference(
                    NodeId: draft.SourceNodeId,
                    LineId: problem.LineId),
                Text: problem.Message));
        }

        // 헤더가 있으면 그 바로 뒤에 둔다. 문서를 위에서 읽을 때 먼저 눈에 띄어야 한다.
        int header = segments.FindIndex(
            segment => segment.Kind == RenderedSegmentKind.NodeHeader);
        segments.InsertRange(header + 1, warnings);
        return segments;
    }
}
