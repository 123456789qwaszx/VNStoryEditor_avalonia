using Vn.Authoring.Definition;

namespace Vn.Authoring.Rendering;

/// <summary>Preview 문자열의 표현 목적. 공식 저작 모델에는 저장되지 않는다.</summary>
public enum DocumentOutputFormat
{
    YarnRuntime,
    Scenario,
    Recording,
    Localization,
    Direction
}

/// <summary>도구가 기본 제공하는 읽기 전용 문서 프리셋.</summary>
public enum OutputPresetId
{
    RuntimeFull,
    ScenarioOnly,
    RecordingScript,
    LocalizationScript,
    DirectionSheet
}

/// <summary>
/// Dialogue 문서 합성 시 어떤 의미 레이어를 포함할지 결정한다.
///
/// 이 값은 Preview 요청에만 전달되며 StoryProject, Snapshot, Undo, dirty 상태에 저장되지 않는다.
/// 동일한 StoryProject에서 옵션만 바꾸어 여러 문서를 안전하게 만들 수 있다.
/// </summary>
public sealed class DocumentOutputOptions
{
    private readonly HashSet<PresentationCategory> _presentationCategories;

    public DocumentOutputOptions(
        DocumentOutputFormat format,
        bool includeStructure,
        bool includeSetAssignments,
        bool includeConditions,
        bool includePresentation,
        IEnumerable<PresentationCategory>? presentationCategories,
        bool includeUncategorizedPresentation,
        bool includeDialogueText,
        bool includeLocalizedDialogue,
        bool includeSpeaker,
        bool includeLineId,
        bool includeExecutionJumps,
        bool includeDiagnostics)
    {
        Format = format;
        IncludeStructure = includeStructure;
        IncludeSetAssignments = includeSetAssignments;
        IncludeConditions = includeConditions;
        IncludePresentation = includePresentation;
        _presentationCategories = presentationCategories is null
            ? new HashSet<PresentationCategory>()
            : new HashSet<PresentationCategory>(presentationCategories);
        IncludeUncategorizedPresentation = includeUncategorizedPresentation;
        IncludeDialogueText = includeDialogueText;
        IncludeLocalizedDialogue = includeLocalizedDialogue;
        IncludeSpeaker = includeSpeaker;
        IncludeLineId = includeLineId;
        IncludeExecutionJumps = includeExecutionJumps;
        IncludeDiagnostics = includeDiagnostics;
    }

    public DocumentOutputFormat Format { get; }

    public bool IncludeStructure { get; }

    public bool IncludeSetAssignments { get; }

    public bool IncludeConditions { get; }

    public bool IncludePresentation { get; }

    public IReadOnlySet<PresentationCategory> PresentationCategories => _presentationCategories;

    public bool IncludeUncategorizedPresentation { get; }

    public bool IncludeDialogueText { get; }

    public bool IncludeLocalizedDialogue { get; }

    public bool IncludeSpeaker { get; }

    public bool IncludeLineId { get; }

    public bool IncludeExecutionJumps { get; }

    public bool IncludeDiagnostics { get; }

    public bool IncludesPresentation(PresentationCategory? category)
    {
        if (!IncludePresentation)
        {
            return false;
        }

        return category is null
            ? IncludeUncategorizedPresentation
            : _presentationCategories.Contains(category.Value);
    }
}

/// <summary>사람이 고르는 이름과 합성 옵션을 묶은 불변 프리셋.</summary>
public sealed record OutputPreset(
    OutputPresetId Id,
    string DisplayName,
    string Description,
    DocumentOutputOptions Options);

/// <summary>
/// 기본 출력 프리셋 카탈로그.
///
/// 프리셋 선택은 문서 합성 방법만 바꾸며 StoryProject를 수정하지 않는다.
/// </summary>
public static class OutputPresetCatalog
{
    private static readonly PresentationCategory[] AllPresentationCategories =
    {
        PresentationCategory.Camera,
        PresentationCategory.ScreenEffect,
        PresentationCategory.CharacterActing
    };

    public static OutputPreset RuntimeFull { get; } = new(
        OutputPresetId.RuntimeFull,
        "Runtime Full",
        "Set, 조건, 활성 연출, LineId와 실행 출구를 모두 포함한 Yarn 스타일 Preview",
        new DocumentOutputOptions(
            DocumentOutputFormat.YarnRuntime,
            includeStructure: true,
            includeSetAssignments: true,
            includeConditions: true,
            includePresentation: true,
            presentationCategories: AllPresentationCategories,
            includeUncategorizedPresentation: true,
            includeDialogueText: true,
            includeLocalizedDialogue: false,
            includeSpeaker: true,
            includeLineId: true,
            includeExecutionJumps: true,
            includeDiagnostics: true));

    public static OutputPreset ScenarioOnly { get; } = new(
        OutputPresetId.ScenarioOnly,
        "Scenario Only",
        "조건과 화자·대사만 읽기 쉽게 표시하고 Set, 연출, 실행 출구는 숨김",
        new DocumentOutputOptions(
            DocumentOutputFormat.Scenario,
            includeStructure: true,
            includeSetAssignments: false,
            includeConditions: true,
            includePresentation: false,
            presentationCategories: null,
            includeUncategorizedPresentation: false,
            includeDialogueText: true,
            includeLocalizedDialogue: false,
            includeSpeaker: true,
            includeLineId: false,
            includeExecutionJumps: false,
            includeDiagnostics: true));

    public static OutputPreset RecordingScript { get; } = new(
        OutputPresetId.RecordingScript,
        "Recording Script",
        "LineId, 화자·대사와 Character Acting 지시만 표시",
        new DocumentOutputOptions(
            DocumentOutputFormat.Recording,
            includeStructure: false,
            includeSetAssignments: false,
            includeConditions: false,
            includePresentation: true,
            presentationCategories: new[] { PresentationCategory.CharacterActing },
            includeUncategorizedPresentation: false,
            includeDialogueText: true,
            includeLocalizedDialogue: false,
            includeSpeaker: true,
            includeLineId: true,
            includeExecutionJumps: false,
            includeDiagnostics: false));

    public static OutputPreset LocalizationScript { get; } = new(
        OutputPresetId.LocalizationScript,
        "Localization Script",
        "번역 원문 전달용 LineId, 화자와 대사만 표시",
        new DocumentOutputOptions(
            DocumentOutputFormat.Localization,
            includeStructure: false,
            includeSetAssignments: false,
            includeConditions: false,
            includePresentation: false,
            presentationCategories: null,
            includeUncategorizedPresentation: false,
            includeDialogueText: true,
            includeLocalizedDialogue: true,
            includeSpeaker: true,
            includeLineId: true,
            includeExecutionJumps: false,
            includeDiagnostics: false));

    public static OutputPreset DirectionSheet { get; } = new(
        OutputPresetId.DirectionSheet,
        "Direction Sheet",
        "LineId와 대사 참고문, Camera·ScreenEffect·CharacterActing 지시를 표시",
        new DocumentOutputOptions(
            DocumentOutputFormat.Direction,
            includeStructure: false,
            includeSetAssignments: false,
            includeConditions: false,
            includePresentation: true,
            presentationCategories: AllPresentationCategories,
            includeUncategorizedPresentation: false,
            includeDialogueText: true,
            includeLocalizedDialogue: false,
            includeSpeaker: false,
            includeLineId: true,
            includeExecutionJumps: false,
            includeDiagnostics: false));

    public static IReadOnlyList<OutputPreset> All { get; } = Array.AsReadOnly(
        new[]
        {
            RuntimeFull,
            ScenarioOnly,
            RecordingScript,
            LocalizationScript,
            DirectionSheet
        });

    public static OutputPreset Get(OutputPresetId id)
    {
        return All.First(preset => preset.Id == id);
    }
}
