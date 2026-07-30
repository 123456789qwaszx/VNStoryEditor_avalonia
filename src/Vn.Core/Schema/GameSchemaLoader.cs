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
                    "SCHEMA0001",
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
                        "SCHEMA0002",
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
                    "SCHEMA0003",
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
                    "SCHEMA0004",
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
                "SCHEMA0010",
                DiagnosticSeverity.Error,
                "schemaVersion은 1 이상의 정수여야 합니다.",
                path,
                0,
                0));
        }

        AddDuplicateDiagnostics(
            schema.Variables.Select(variable =>
                NormalizeVariableName(variable.Id)),
            "변수",
            "SCHEMA0011",
            path,
            diagnostics);

        AddDuplicateDiagnostics(
            schema.EnumerateCommands().Select(command => command.Id),
            "명령",
            "SCHEMA0012",
            path,
            diagnostics);

        foreach (VariableDefinition variable in schema.Variables)
        {
            if (string.IsNullOrWhiteSpace(variable.Id))
            {
                diagnostics.Add(new VnDiagnostic(
                    "SCHEMA0013",
                    DiagnosticSeverity.Error,
                    "변수 id가 비어 있습니다.",
                    path,
                    0,
                    0));
            }

            if (!SchemaTypeMapper.IsSupported(variable.Type))
            {
                diagnostics.Add(new VnDiagnostic(
                    "SCHEMA0014",
                    DiagnosticSeverity.Error,
                    $"변수 '{variable.Id}'의 타입 '{variable.Type}'은 지원되지 않습니다. string, number, int, float, bool 중 하나를 사용하세요.",
                    path,
                    0,
                    0));
            }
        }

        foreach (CommandDefinition command in schema.EnumerateCommands())
        {
            if (string.IsNullOrWhiteSpace(command.Id))
            {
                diagnostics.Add(new VnDiagnostic(
                    "SCHEMA0015",
                    DiagnosticSeverity.Error,
                    "명령 id가 비어 있습니다.",
                    path,
                    0,
                    0));
            }
        }

        return diagnostics;
    }

    private static void AddDuplicateDiagnostics(
        IEnumerable<string> names,
        string kind,
        string code,
        string path,
        ICollection<VnDiagnostic> diagnostics)
    {
        foreach (string duplicate in names
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .GroupBy(name => name, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key)
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            diagnostics.Add(new VnDiagnostic(
                code,
                DiagnosticSeverity.Error,
                $"{kind} '{duplicate}'이(가) 스키마에 두 번 이상 선언되어 있습니다.",
                path,
                0,
                0));
        }
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
