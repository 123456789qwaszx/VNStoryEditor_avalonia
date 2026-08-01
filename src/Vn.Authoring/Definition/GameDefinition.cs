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

    /// <summary>연출 편집기의 드롭다운과 Yarn Preview Formatter가 공유하는 명령 정의.</summary>
    [JsonPropertyName("presentationCommands")]
    public List<PresentationCommandSpec> PresentationCommands { get; init; } = new();

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

            return JsonSerializer.Deserialize<GameDefinition>(
                       File.ReadAllText(path, new UTF8Encoding(false)),
                       new JsonSerializerOptions
                       {
                           PropertyNameCaseInsensitive = true,
                           ReadCommentHandling = JsonCommentHandling.Skip,
                           AllowTrailingCommas = true
                       })
                   ?? Empty;
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


public sealed class PresentationCommandSpec
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("outputCommand")]
    public string OutputCommand { get; init; } = string.Empty;

    [JsonPropertyName("arguments")]
    public Dictionary<string, string> Arguments { get; init; } = new(StringComparer.Ordinal);
}
