using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vn.Core.Schema;

/// <summary>
/// game.schema.json의 메모리 표현.
/// 공개 계약에는 등장하지 않으므로 internal로 둔다. 필요해지면 그때 넓히는 편이 안전하다.
///
/// 모든 문자열 속성은 null을 흡수한다. System.Text.Json은 JSON에 명시적 <c>null</c>이 있으면
/// 초기화 값을 무시하고 null을 그대로 쓰기 때문에, 작가가 손으로 편집한 스키마 때문에
/// 도구가 NullReferenceException으로 죽는 일이 없어야 한다.
/// </summary>
internal sealed class GameSchema
{
    public static GameSchema Empty { get; } = new() { SchemaVersion = 1 };

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("variables")]
    public List<VariableDefinition> Variables
    {
        get => _variables;
        init => _variables = value ?? new List<VariableDefinition>();
    }

    [JsonPropertyName("commands")]
    public List<CommandDefinition> Commands
    {
        get => _commands;
        init => _commands = value ?? new List<CommandDefinition>();
    }

    // 초기 기획에서 eventTypes라는 이름을 사용했다면 그대로 읽을 수 있게 한다.
    [JsonPropertyName("eventTypes")]
    public List<CommandDefinition> EventTypes
    {
        get => _eventTypes;
        init => _eventTypes = value ?? new List<CommandDefinition>();
    }

    private readonly List<VariableDefinition> _variables = new();
    private readonly List<CommandDefinition> _commands = new();
    private readonly List<CommandDefinition> _eventTypes = new();

    /// <summary>
    /// 선언된 순서 그대로, 중복도 제거하지 않고 모두 돌려준다.
    /// 중복·충돌 검사는 이 목록 위에서 해야 한다.
    /// </summary>
    public IEnumerable<CommandDefinition> EnumerateDeclaredCommands()
    {
        return Commands.Concat(EventTypes);
    }

    /// <summary>
    /// 이름 조회용으로 id마다 하나씩만 남긴 목록.
    /// 중복 검사에 쓰면 안 된다. 여기서는 이미 중복이 제거되어 있다.
    /// </summary>
    public IEnumerable<CommandDefinition> EnumerateCommands()
    {
        return EnumerateDeclaredCommands()
            .GroupBy(command => command.Id, StringComparer.Ordinal)
            .Select(group => group.First());
    }
}

internal sealed class VariableDefinition
{
    [JsonPropertyName("id")]
    public string Id
    {
        get => _id;
        init => _id = value ?? string.Empty;
    }

    [JsonPropertyName("type")]
    public string Type
    {
        get => _type;
        init => _type = value ?? string.Empty;
    }

    /// <summary>
    /// 스키마가 지정한 기본값. object?로 두면 실제로는 JsonElement가 들어와
    /// 호출부마다 형변환을 다시 하게 되므로, 들어오는 그대로의 타입을 그대로 드러낸다.
    /// </summary>
    [JsonPropertyName("default")]
    public JsonElement? Default { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    private readonly string _id = string.Empty;
    private readonly string _type = string.Empty;
}

internal sealed class CommandDefinition
{
    [JsonPropertyName("id")]
    public string Id
    {
        get => _id;
        init => _id = value ?? string.Empty;
    }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("params")]
    public List<CommandParameterDefinition> Parameters
    {
        get => _parameters;
        init => _parameters = value ?? new List<CommandParameterDefinition>();
    }

    private readonly string _id = string.Empty;
    private readonly List<CommandParameterDefinition> _parameters = new();
}

internal sealed class CommandParameterDefinition
{
    [JsonPropertyName("name")]
    public string Name
    {
        get => _name;
        init => _name = value ?? string.Empty;
    }

    [JsonPropertyName("type")]
    public string Type
    {
        get => _type;
        init => _type = value ?? string.Empty;
    }

    [JsonPropertyName("required")]
    public bool Required { get; init; } = true;

    private readonly string _name = string.Empty;
    private readonly string _type = string.Empty;
}
