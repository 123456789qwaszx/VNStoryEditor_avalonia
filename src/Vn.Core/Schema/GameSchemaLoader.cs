using System.Text.Json;
using Vn.Core.Diagnostics;

namespace Vn.Core.Schema;

internal static class GameSchemaLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static GameSchemaLoadResult Load(string path)
    {
        string fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            return GameSchemaLoadResult.Failure(
                new VnDiagnostic(
                    VnDiagnosticCodes.SchemaFileNotFound,
                    DiagnosticSeverity.Error,
                    "게임 스키마 파일을 찾을 수 없습니다.",
                    fullPath,
                    0,
                    0));
        }

        try
        {
            string json = File.ReadAllText(fullPath);
            GameSchema? schema =
                JsonSerializer.Deserialize<GameSchema>(json, Options);

            if (schema is null)
            {
                return GameSchemaLoadResult.Failure(
                    new VnDiagnostic(
                        VnDiagnosticCodes.SchemaEmpty,
                        DiagnosticSeverity.Error,
                        "게임 스키마의 내용이 비어 있습니다.",
                        fullPath,
                        0,
                        0));
            }

            var diagnostics = ValidateShape(schema, fullPath);

            return new GameSchemaLoadResult(
                schema,
                diagnostics);
        }
        catch (JsonException exception)
        {
            return GameSchemaLoadResult.Failure(
                new VnDiagnostic(
                    VnDiagnosticCodes.SchemaJsonInvalid,
                    DiagnosticSeverity.Error,
                    $"게임 스키마 JSON을 읽을 수 없습니다. {exception.Message}",
                    fullPath,
                    exception.LineNumber is null
                        ? 0
                        : checked((int)exception.LineNumber.Value + 1),
                    exception.BytePositionInLine is null
                        ? 0
                        : checked((int)exception.BytePositionInLine.Value + 1)));
        }
        catch (IOException exception)
        {
            return GameSchemaLoadResult.Failure(
                new VnDiagnostic(
                    VnDiagnosticCodes.SchemaFileUnreadable,
                    DiagnosticSeverity.Error,
                    $"게임 스키마 파일을 읽지 못했습니다. {exception.Message}",
                    fullPath,
                    0,
                    0));
        }
    }

    private static IReadOnlyList<VnDiagnostic> ValidateShape(
        GameSchema schema,
        string path)
    {
        var diagnostics = new List<VnDiagnostic>();

        if (schema.SchemaVersion <= 0)
        {
            diagnostics.Add(new VnDiagnostic(
                VnDiagnosticCodes.SchemaVersionInvalid,
                DiagnosticSeverity.Error,
                "schemaVersion은 1 이상의 정수여야 합니다.",
                path,
                0,
                0));
        }

        AddDuplicateVariableDiagnostics(schema, path, diagnostics);
        AddDuplicateCommandDiagnostics(schema, path, diagnostics);

        foreach (VariableDefinition variable in schema.Variables)
        {
            if (string.IsNullOrWhiteSpace(variable.Id))
            {
                diagnostics.Add(new VnDiagnostic(
                    VnDiagnosticCodes.SchemaVariableIdEmpty,
                    DiagnosticSeverity.Error,
                    "변수 id가 비어 있습니다.",
                    path,
                    0,
                    0));
            }

            if (!SchemaTypeMapper.IsSupported(variable.Type))
            {
                diagnostics.Add(new VnDiagnostic(
                    VnDiagnosticCodes.SchemaVariableTypeUnsupported,
                    DiagnosticSeverity.Error,
                    $"변수 '{variable.Id}'의 타입 '{variable.Type}'은 지원되지 않습니다. string, number, int, float, bool 중 하나를 사용하세요.",
                    path,
                    0,
                    0));
            }
            else if (!SchemaTypeMapper.TryGetDefaultValue(variable, out _))
            {
                diagnostics.Add(new VnDiagnostic(
                    VnDiagnosticCodes.SchemaDefaultValueInvalid,
                    DiagnosticSeverity.Error,
                    $"변수 '{variable.Id}'의 default 값이 타입 '{variable.Type}'과 맞지 않습니다.",
                    path,
                    0,
                    0));
            }
        }

        foreach (CommandDefinition command in schema.EnumerateDeclaredCommands())
        {
            if (string.IsNullOrWhiteSpace(command.Id))
            {
                diagnostics.Add(new VnDiagnostic(
                    VnDiagnosticCodes.SchemaCommandIdEmpty,
                    DiagnosticSeverity.Error,
                    "명령 id가 비어 있습니다.",
                    path,
                    0,
                    0));
            }
        }

        return diagnostics;
    }

    private static void AddDuplicateVariableDiagnostics(
        GameSchema schema,
        string path,
        ICollection<VnDiagnostic> diagnostics)
    {
        // 빈 id는 여기서 걸러낸다. 정규화하면 전부 "$"가 되어
        // "변수 '$'이(가) 두 번 선언되었다"는 엉뚱한 진단이 나온다. 빈 id는 VN1013이 따로 잡는다.
        IEnumerable<string> names = schema.Variables
            .Where(variable => !string.IsNullOrWhiteSpace(variable.Id))
            .Select(variable => NormalizeVariableName(variable.Id));

        foreach (string duplicate in FindDuplicates(names))
        {
            diagnostics.Add(new VnDiagnostic(
                VnDiagnosticCodes.SchemaDuplicateVariable,
                DiagnosticSeverity.Error,
                $"변수 '{duplicate}'이(가) 스키마에 두 번 이상 선언되어 있습니다.",
                path,
                0,
                0));
        }
    }

    private static void AddDuplicateCommandDiagnostics(
        GameSchema schema,
        string path,
        ICollection<VnDiagnostic> diagnostics)
    {
        // EnumerateCommands()는 id마다 하나만 남기므로 중복 검사에 쓸 수 없다.
        // 선언된 그대로의 목록을 봐야 한다.
        HashSet<string> commandIds = schema.Commands
            .Select(command => command.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        HashSet<string> eventTypeIds = schema.EventTypes
            .Select(command => command.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        IEnumerable<string> declaredIds = schema.EnumerateDeclaredCommands()
            .Select(command => command.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id));

        foreach (string duplicate in FindDuplicates(declaredIds))
        {
            bool crossesLists =
                commandIds.Contains(duplicate) &&
                eventTypeIds.Contains(duplicate);

            diagnostics.Add(crossesLists
                ? new VnDiagnostic(
                    VnDiagnosticCodes.SchemaCommandIdConflict,
                    DiagnosticSeverity.Error,
                    $"명령 '{duplicate}'이(가) commands와 eventTypes 양쪽에 선언되어 있습니다. 한쪽만 남기세요. 지금은 commands 쪽 정의만 사용됩니다.",
                    path,
                    0,
                    0)
                : new VnDiagnostic(
                    VnDiagnosticCodes.SchemaDuplicateCommand,
                    DiagnosticSeverity.Error,
                    $"명령 '{duplicate}'이(가) 스키마에 두 번 이상 선언되어 있습니다.",
                    path,
                    0,
                    0));
        }
    }

    private static IEnumerable<string> FindDuplicates(IEnumerable<string> names)
    {
        return names
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.Ordinal);
    }

    internal static string NormalizeVariableName(string name)
    {
        string trimmed = name.Trim();
        return trimmed.StartsWith('$')
            ? trimmed
            : $"${trimmed}";
    }
}

internal sealed record GameSchemaLoadResult(
    GameSchema? Schema,
    IReadOnlyList<VnDiagnostic> Diagnostics)
{
    public static GameSchemaLoadResult Failure(VnDiagnostic diagnostic)
    {
        return new GameSchemaLoadResult(
            null,
            new[] { diagnostic });
    }
}
