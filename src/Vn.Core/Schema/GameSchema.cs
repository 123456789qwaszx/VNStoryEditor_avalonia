using System.Text.Json.Serialization;

namespace Vn.Core.Schema;

public sealed class GameSchema
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("variables")]
    public List<VariableDefinition> Variables { get; init; } = new();

    [JsonPropertyName("commands")]
    public List<CommandDefinition> Commands { get; init; } = new();

    // 초기 기획에서 eventTypes라는 이름을 사용했다면 그대로 읽을 수 있게 한다.
    [JsonPropertyName("eventTypes")]
    public List<CommandDefinition> EventTypes { get; init; } = new();

    public IEnumerable<CommandDefinition> EnumerateCommands()
    {
        return Commands
            .Concat(EventTypes)
            .GroupBy(command => command.Id, StringComparer.Ordinal)
            .Select(group => group.First());
    }
}

public sealed class VariableDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("default")]
    public object? Default { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

public sealed class CommandDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("params")]
    public List<CommandParameterDefinition> Parameters { get; init; } = new();
}

public sealed class CommandParameterDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("required")]
    public bool Required { get; init; } = true;
}
