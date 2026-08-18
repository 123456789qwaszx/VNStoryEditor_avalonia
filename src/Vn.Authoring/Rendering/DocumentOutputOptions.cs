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
///
/// 연출 범주는 게임 정의가 공급하는 문자열 Id다. 어떤 범주가 존재하는지 코드는 모르므로,
/// 기본 프리셋은 범주를 열거하지 않고(null = 전체 허용) 사용자 정의 옵션만 부분집합을 고른다.
/// </summary>
public sealed class DocumentOutputOptions
{
    private readonly HashSet<string>? _presentationCategories;

    public DocumentOutputOptions(
        DocumentOutputFormat format,
        bool includeStructure,
        bool includeSetAssignments,
        bool includeConditions,
        bool includePresentation,
        IEnumerable<string>? presentationCategories,
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
            ? null
            : new HashSet<string>(presentationCategories, StringComparer.Ordinal);
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

    /// <summary>포함할 연출 범주 Id 집합. null이면 모든 범주를 포함한다.</summary>
    public IReadOnlySet<string>? PresentationCategories => _presentationCategories;

    public bool IncludeUncategorizedPresentation { get; }

    public bool IncludeDialogueText { get; }

    public bool IncludeLocalizedDialogue { get; }

    public bool IncludeSpeaker { get; }

    public bool IncludeLineId { get; }

    public bool IncludeExecutionJumps { get; }

    public bool IncludeDiagnostics { get; }

    public bool IncludesPresentation(string? categoryId)
    {
        if (!IncludePresentation)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(categoryId))
        {
            return IncludeUncategorizedPresentation;
        }

        return _presentationCategories is null || _presentationCategories.Contains(categoryId);
    }
}

/// <summary>사람이 고르는 이름과 합성 옵션을 묶은 불변 프리셋.</summary>
public sealed record OutputPreset(
    OutputPresetId Id,
    string DisplayName,
    string Description,
    DocumentOutputOptions Options);

/// <summary>
/// 기본 출력 프리셋 카탈로그. 소유자의 4형식 매핑을 따른다.
///
/// 1) Runtime Full = 유니티 재생용 완본, 2) Recording/Localization Script = LineId+대본,
/// 3) Scenario Only = 대본+조건 검수용, 4) Direction Sheet = LineId+연출 테이블.
///
/// 프리셋 선택은 문서 합성 방법만 바꾸며 StoryProject를 수정하지 않는다.
/// </summary>
public static class OutputPresetCatalog
{
    public static OutputPreset RuntimeFull { get; } = new(
        OutputPresetId.RuntimeFull,
        "Runtime Full",
        "Set, 조건, 모든 연출, LineId와 실행 출구를 모두 포함한 Yarn 스타일 Preview",
        new DocumentOutputOptions(
            DocumentOutputFormat.YarnRuntime,
            includeStructure: true,
            includeSetAssignments: true,
            includeConditions: true,
            includePresentation: true,
            presentationCategories: null,
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
        "녹음 전달용 LineId, 화자와 대사만 표시",
        new DocumentOutputOptions(
            DocumentOutputFormat.Recording,
            includeStructure: false,
            includeSetAssignments: false,
            includeConditions: false,
            includePresentation: false,
            presentationCategories: null,
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

    /// <summary>
    /// 연출 양식 — <b>LineId · 화자 · 대사 · 연출</b>을 한 줄에 놓고 사람이 읽는다.
    ///
    /// 2026-08-18에 <b>화자를 켰다</b>. 이 양식이 자리를 물려받았기 때문이다: 줄에 붙은
    /// 연출은 예전에 `Pres_*.yarn`(서브 레인 사본)으로도 나갔는데, 런타임이 단일 대본만
    /// 읽게 되면서 그 파일이 없어졌다. 연출을 <b>줄 단위로 훑어보는 자리</b>가 여기
    /// 하나만 남았고, 그때 화자가 없으면 누구 대사인지 세어 가며 맞춰야 한다.
    ///
    /// 차후에 별도 엑셀로 빼낼 수 있다(소유자).
    /// </summary>
    public static OutputPreset DirectionSheet { get; } = new(
        OutputPresetId.DirectionSheet,
        "Direction Sheet",
        "LineId · 화자 · 대사 · 모든 범주의 연출 지시를 한눈에",
        new DocumentOutputOptions(
            DocumentOutputFormat.Direction,
            includeStructure: false,
            includeSetAssignments: false,
            includeConditions: false,
            includePresentation: true,
            presentationCategories: null,
            includeUncategorizedPresentation: true,
            includeDialogueText: true,
            includeLocalizedDialogue: false,
            includeSpeaker: true,
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
