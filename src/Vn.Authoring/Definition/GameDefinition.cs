using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vn.Authoring.Definition;

/// <summary>
/// 게임마다 달라지는 것들. 프로젝트 옆의 <c>game.definition.json</c>에서 읽는다.
///
/// VnTool은 <c>favor</c>나 <c>route</c> 같은 이름을 하나도 알지 못한다.
/// 알게 되는 순간 이 도구는 그 게임 전용이 되고, 다음 게임에서는 코드를 고쳐야 한다.
/// 그래서 "무엇을 고를 수 있는가"는 전부 이 파일이 공급하고, 도구는 목록을 보여 주기만 한다.
///
/// 파일이 없어도 저작은 계속된다. 그때는 자동완성 후보가 없을 뿐이고,
/// 작가가 직접 입력한 이름을 그대로 쓴다. 편의 기능이 없다고 원고를 못 쓰게 하지 않는다.
/// </summary>
public sealed class GameDefinition
{
    public const string FileName = "game.definition.json";

    public static GameDefinition Empty { get; } = new();

    [JsonPropertyName("variables")]
    public List<VariableSpec> Variables { get; init; } = new();

    [JsonPropertyName("events")]
    public List<EventSpec> Events { get; init; } = new();

    /// <summary>
    /// 프로젝트와 무관하게 게임이 언제나 제공하는 조건. 설정 노드가 만드는 조건에 더해진다.
    /// 여러 게임에서 반복되는 조건(예: 난이도, 언어)을 프로젝트마다 다시 만들지 않아도 된다.
    /// </summary>
    [JsonPropertyName("conditions")]
    public List<ConditionSpec> Conditions { get; init; } = new();

    /// <summary>
    /// 연출 명령의 편집 범주. 목록 순서가 편집기의 표시 순서다.
    /// "캐릭터 연출"이 무엇을 묶는지는 게임마다 다르므로 코드가 아니라 여기서 정의한다.
    /// </summary>
    [JsonPropertyName("presentationCommandCategories")]
    public List<PresentationCategorySpec> PresentationCommandCategories { get; init; } = new();

    /// <summary>연출 편집기의 드롭다운과 Yarn Preview Formatter가 공유하는 명령 정의.</summary>
    [JsonPropertyName("presentationCommands")]
    public List<PresentationCommandSpec> PresentationCommands { get; init; } = new();

    /// <summary>
    /// 대본의 화자명 ↔ 초상화 characterId 매핑. 프리뷰가 화자 강조에 쓴다.
    ///
    /// 런타임의 화자 정책 DBSO는 화자명→표시명/대사창만 알고 캐릭터 키를 모르므로
    /// 이 매핑은 어차피 신규 저작 데이터다. 게임별 어휘는 정의 파일이 공급한다는
    /// 원칙 그대로 여기 둔다. 매핑이 없는 화자는 이름만 표시된다 — 오류가 아니다.
    /// </summary>
    [JsonPropertyName("speakers")]
    public List<SpeakerSpec> Speakers { get; init; } = new();

    /// <summary>프리뷰 표시 설정. 없으면 기본값(1920×1080)이다.</summary>
    [JsonPropertyName("preview")]
    public PreviewSpec? Preview { get; init; }

    /// <summary>
    /// 런타임 tuning 덤프(ExportedTuning) 폴더 재지정. 프로젝트 상대 또는 절대 경로.
    /// 없으면 프로젝트 폴더의 <c>ExportedTuning</c>(런타임 내보내기 폴더명 그대로)을 쓴다.
    /// </summary>
    [JsonPropertyName("runtimeTuningPath")]
    public string? RuntimeTuningPath { get; init; }

    /// <summary>
    /// 프리뷰 기준 해상도. <c>preview.resolution</c>("1920x1080" 형식)에서 읽고,
    /// 없거나 형식이 다르면 기본 1920×1080이다. 게임별 CanvasScaler 해상도가 다르면
    /// 정의 파일이 교체한다 — 코드에 게임을 박지 않는다.
    /// </summary>
    public (double Width, double Height) PreviewResolution
    {
        get
        {
            string[]? parts = Preview?.Resolution?.Split('x', 'X', '×');

            if (parts is { Length: 2 } &&
                double.TryParse(parts[0].Trim(), out double width) && width > 0 &&
                double.TryParse(parts[1].Trim(), out double height) && height > 0)
            {
                return (width, height);
            }

            return (1920, 1080);
        }
    }

    /// <summary>화자명에 매핑된 characterId. 없으면 null — 프리뷰는 이름만 보여 준다.</summary>
    public string? FindSpeakerCharacterId(string? speakerName)
    {
        if (string.IsNullOrWhiteSpace(speakerName))
        {
            return null;
        }

        return Speakers
            .FirstOrDefault(speaker => string.Equals(speaker.Name, speakerName.Trim(), StringComparison.Ordinal))
            ?.CharacterId;
    }

    public static string PathFor(string projectPath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(projectPath))
            ?? Environment.CurrentDirectory;

        return Path.Combine(directory, FileName);
    }

    /// <summary>
    /// 없거나 깨졌으면 빈 정의를 돌려준다. 예외를 밖으로 내보내지 않는다.
    /// 편의를 위한 파일 하나 때문에 프로젝트를 못 열게 되어서는 안 된다.
    /// </summary>
    public static GameDefinition LoadBeside(string projectPath)
    {
        string path = PathFor(projectPath);

        try
        {
            if (!File.Exists(path))
            {
                return Empty;
            }

            return Parse(File.ReadAllText(path, new UTF8Encoding(false))) ?? Empty;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                JsonException or
                NotSupportedException or
                ArgumentException)
        {
            return Empty;
        }
    }

    /// <summary>정의 JSON 파싱 규칙의 단일 구현. 파일과 내장 기본 카탈로그가 같은 길을 지난다.</summary>
    public static GameDefinition? Parse(string json)
    {
        return JsonSerializer.Deserialize<GameDefinition>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
    }
}

public sealed class VariableSpec
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>게임이 해석하는 타입 이름. VnTool은 그대로 보여 주기만 한다.</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

public sealed class EventSpec
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

public sealed class ConditionSpec
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("expression")]
    public string Expression { get; init; } = string.Empty;
}


public sealed class PreviewSpec
{
    /// <summary>"1920x1080" 형식. 프리뷰 창의 기준 좌표계가 된다.</summary>
    [JsonPropertyName("resolution")]
    public string? Resolution { get; init; }
}

public sealed class SpeakerSpec
{
    /// <summary>대본에 적히는 화자명 그대로. Ordinal 정확 일치로 찾는다.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>초상화 매니페스트의 characterId.</summary>
    [JsonPropertyName("characterId")]
    public string CharacterId { get; init; } = string.Empty;
}

public sealed class PresentationCategorySpec
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public sealed class PresentationCommandSpec
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary><see cref="PresentationCategorySpec.Id"/> 참조. 도구는 문자열로만 다룬다.</summary>
    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("outputCommand")]
    public string OutputCommand { get; init; } = string.Empty;

    /// <summary>런타임 실행 방식(immediate/queued). 도구는 해석하지 않고 실어 나른다.</summary>
    [JsonPropertyName("execution")]
    public string Execution { get; init; } = string.Empty;

    /// <summary>메인 레인 전용 커맨드. Pres/Set 노드에 출력하면 런타임이 unknown command로 깨진다.</summary>
    [JsonPropertyName("mainLaneOnly")]
    public bool MainLaneOnly { get; init; }

    /// <summary>
    /// 액팅류의 강도 분류(미세/가벼움/보통/강함 — 레퍼런스 §23.3). 갤러리의 부그룹이 된다.
    /// 어떤 커맨드가 어느 강도인지는 코드가 아니라 이 데이터가 정한다.
    /// </summary>
    [JsonPropertyName("intensity")]
    public string? Intensity { get; init; }

    /// <summary><b>순서가 곧 Yarn 포지셔널 인자 순서다.</b></summary>
    [JsonPropertyName("parameters")]
    public List<PresentationParameterSpec> Parameters { get; init; } = new();

    /// <summary>
    /// 이 커맨드가 무슨 축을 미는가의 선언 (W66). 없으면 null — 모션 편집기가 수치를
    /// 내밀지 않는다. <b>선언만이 근거다</b>: 이름이나 정규식으로 축을 추측하지 않는다.
    /// </summary>
    [JsonPropertyName("motion")]
    public PresentationMotionSpec? Motion { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}

/// <summary>커맨드가 미는 축의 선언. 값의 뜻(단위·상대/절대)과 시간 인자가 여기 명시된다.</summary>
public sealed class PresentationMotionSpec
{
    /// <summary>리그 안 노드 이름. 슬롯 키와 합쳐 <c>{slot}/{node}</c>가 무대 상태의 키다.</summary>
    [JsonPropertyName("node")]
    public string Node { get; init; } = string.Empty;

    /// <summary>true면 값이 현재 위치에 더해지는 증분, false면 목표 좌표 그 자체다.</summary>
    [JsonPropertyName("relative")]
    public bool Relative { get; init; }

    /// <summary>시간을 싣는 파라미터 이름. 없으면 즉시(계단).</summary>
    [JsonPropertyName("durationParam")]
    public string? DurationParam { get; init; }

    /// <summary>이징을 싣는 파라미터 이름 (W67). 없으면 이 커맨드의 이징은 편집 불가다.</summary>
    [JsonPropertyName("easeParam")]
    public string? EaseParam { get; init; }

    /// <summary>런타임 스펙 기본 이징의 기록. 선택기에서 이 값을 고르면 인자를 지워 생략한다.</summary>
    [JsonPropertyName("defaultEase")]
    public string? DefaultEase { get; init; }

    [JsonPropertyName("axes")]
    public List<PresentationMotionAxisSpec> Axes { get; init; } = new();
}

/// <summary>축 하나 — 어느 파라미터가 어느 축을 어떤 단위로 미는가.</summary>
public sealed class PresentationMotionAxisSpec
{
    [JsonPropertyName("param")]
    public string Param { get; init; } = string.Empty;

    /// <summary>x 또는 y.</summary>
    [JsonPropertyName("axis")]
    public string Axis { get; init; } = string.Empty;

    /// <summary>값의 단위(u). 픽셀 환산은 <c>UnitToken</c>이 유일한 자리다.</summary>
    [JsonPropertyName("unit")]
    public string Unit { get; init; } = string.Empty;
}

public sealed class PresentationParameterSpec
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>게임이 해석하는 타입 이름(int, aliasOrSlot 등). VnTool은 그대로 보여 주기만 한다.</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("required")]
    public bool Required { get; init; }

    [JsonPropertyName("default")]
    public string? Default { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}
