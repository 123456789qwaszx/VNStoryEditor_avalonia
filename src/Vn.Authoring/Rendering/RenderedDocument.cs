using Vn.Authoring.Results;

namespace Vn.Authoring.Rendering;

/// <summary>평면 문서를 이루는 의미 단위.</summary>
public enum RenderedSegmentKind
{
    NodeHeader,
    SetAssignment,
    ConditionBegin,
    ConditionElseIf,
    ConditionEnd,
    PresentationCommand,
    DialogueLine,
    BranchJump,
    DefaultJump,
    Warning,
    NodeFooter
}

/// <summary>
/// 같은 결과를 런타임용, 녹음용, 번역용, 연출 지시서용으로
/// 합성할 때 사용하는 의미 레이어.
/// </summary>
public enum DocumentLayer
{
    Structure,
    SetAssignments,
    Conditions,
    Presentation,
    Dialogue,
    ExecutionJumps,
    Diagnostics
}

/// <summary>
/// 합성된 한 Segment가 어느 결과의 어디에서 왔는지.
///
/// 화면에는 평평한 문자열만 보여도 이 참조를 잃지 않는다. Preview 줄 클릭, 로컬라이징,
/// 녹음 대본, 연출 명령 오류 표시가 모두 이 연결을 사용한다.
///
/// 프리셋 필터로 다른 Segment가 빠져도 남은 Segment의 참조는 그대로다.
/// </summary>
public sealed record RenderSourceReference(
    string? NodeId = null,
    string? LineId = null,
    string? ConditionId = null,
    string? DialogueResultId = null,
    string? PresentationResultId = null,
    string? PresentationNodeId = null,
    string? PresentationCommandId = null);

/// <summary>
/// Formatter에 종속되지 않은 평면 문서 조각.
/// Text는 대사·표시 이름·경고처럼 사람이 읽는 내용이고, Expression/Variable/TargetNodeId는
/// Yarn뿐 아니라 다른 출력 형식도 다시 사용할 수 있는 의미 값이다.
/// </summary>
public sealed record RenderedSegment(
    string Id,
    RenderedSegmentKind Kind,
    DocumentLayer Layer,
    RenderSourceReference Source,
    int IndentLevel = 0,
    string? Text = null,
    string? LocalizedText = null,
    string? Speaker = null,
    string? Expression = null,
    string? Variable = null,
    string? Value = null,
    string? TargetNodeId = null,
    string? TargetNodeName = null,
    string? DefinitionId = null,
    string? CommandName = null,
    Vn.Authoring.Definition.PresentationCategory? PresentationCategory = null,
    IReadOnlyDictionary<string, string>? Arguments = null);

/// <summary>
/// 결과 하나(또는 결과 조합 하나)를 평평하게 합성한 결과.
///
/// 파일 저장용 포맷이 아니다. 정식 원본은 계속 발행된 Result이고, 이 문서는 그것을
/// 사람이나 엔진이 읽는 표현으로 펼친 것이다. 방향은 언제나 결과 → 문서이며
/// 되파싱하지 않는다.
/// </summary>
public sealed class RenderedDocument
{
    public RenderedDocument(
        string sourceNodeId,
        ResultIdentity dialogueResult,
        ResultIdentity? presentationResult,
        IReadOnlyList<RenderedSegment> segments,
        DocumentOutputOptions? options = null,
        OutputPresetId? presetId = null)
    {
        ArgumentNullException.ThrowIfNull(segments);

        SourceNodeId = sourceNodeId;
        DialogueResult = dialogueResult;
        PresentationResult = presentationResult;
        Segments = segments.ToArray();
        Options = options ?? OutputPresetCatalog.RuntimeFull.Options;
        PresetId = presetId;
    }

    /// <summary>이 문서를 낳은 결과의 원본 DialogueNode.</summary>
    public string SourceNodeId { get; }

    /// <summary>합성에 사용한 대사 결과. Version 0이면 발행하지 않은 작업 중 미리 보기다.</summary>
    public ResultIdentity DialogueResult { get; }

    /// <summary>합성에 사용한 연출 결과. 연출 없이 합성했으면 null이다.</summary>
    public ResultIdentity? PresentationResult { get; }

    public IReadOnlyList<RenderedSegment> Segments { get; }

    /// <summary>이 문서 결과를 만들 때 사용한 합성 옵션. 프로젝트에는 저장되지 않는다.</summary>
    public DocumentOutputOptions Options { get; }

    /// <summary>기본 프리셋으로 합성한 경우의 식별자. 사용자 정의 옵션이면 null일 수 있다.</summary>
    public OutputPresetId? PresetId { get; }

    /// <summary>발행된 결과에서 나온 정식 문서인지.</summary>
    public bool IsPublished => DialogueResult.IsPublished;
}
