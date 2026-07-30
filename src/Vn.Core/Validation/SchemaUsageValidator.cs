using Vn.Core.Diagnostics;
using Vn.Core.Schema;
using Vn.Core.Story;

namespace Vn.Core.Validation;

internal static class SchemaUsageValidator
{
    private static readonly HashSet<string> BuiltInCommands =
        new(StringComparer.Ordinal)
        {
            "stop"
        };

    public static IReadOnlyList<VnDiagnostic> Validate(
        GameSchema schema,
        IReadOnlyList<StoryNode> nodes,
        IReadOnlySet<string> explicitYarnVariables)
    {
        var diagnostics = new List<VnDiagnostic>();

        HashSet<string> schemaVariables = schema.Variables
            .Select(variable =>
                GameSchemaLoader.NormalizeVariableName(variable.Id))
            .ToHashSet(StringComparer.Ordinal);

        HashSet<string> allowedVariables =
            new(schemaVariables, StringComparer.Ordinal);

        allowedVariables.UnionWith(explicitYarnVariables);

        HashSet<string> allowedCommands = schema
            .EnumerateCommands()
            .Select(command => command.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        allowedCommands.UnionWith(BuiltInCommands);

        foreach (StoryNode node in nodes)
        {
            foreach (string variable in node.VariableReferences)
            {
                if (allowedVariables.Contains(variable))
                {
                    continue;
                }

                string? suggestion =
                    NameSuggester.FindClosest(
                        variable,
                        allowedVariables);

                diagnostics.Add(new VnDiagnostic(
                    "VAR0001",
                    DiagnosticSeverity.Error,
                    BuildUnknownNameMessage(
                        "변수",
                        variable,
                        suggestion),
                    node.FilePath,
                    node.HeaderLine,
                    1));
            }

            foreach (string command in node.CommandCalls)
            {
                if (allowedCommands.Contains(command))
                {
                    continue;
                }

                string? suggestion =
                    NameSuggester.FindClosest(
                        command,
                        allowedCommands);

                diagnostics.Add(new VnDiagnostic(
                    "CMD0001",
                    DiagnosticSeverity.Error,
                    BuildUnknownNameMessage(
                        "명령",
                        command,
                        suggestion),
                    node.FilePath,
                    node.HeaderLine,
                    1));
            }
        }

        HashSet<string> knownNodeTitles = nodes
            .Select(node => node.Title)
            .ToHashSet(StringComparer.Ordinal);

        foreach (StoryJump jump in nodes.SelectMany(node => node.Jumps))
        {
            if (knownNodeTitles.Contains(jump.DestinationNodeTitle))
            {
                continue;
            }

            string? suggestion =
                NameSuggester.FindClosest(
                    jump.DestinationNodeTitle,
                    knownNodeTitles);

            diagnostics.Add(new VnDiagnostic(
                "GRAPH0001",
                DiagnosticSeverity.Error,
                BuildUnknownNameMessage(
                    "이동 대상 노드",
                    jump.DestinationNodeTitle,
                    suggestion),
                jump.FilePath,
                jump.Line,
                jump.Column));
        }

        return diagnostics
            .OrderBy(diagnostic => diagnostic.FilePath, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Line)
            .ThenBy(diagnostic => diagnostic.Column)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToArray();
    }

    private static string BuildUnknownNameMessage(
        string kind,
        string unknown,
        string? suggestion)
    {
        string message =
            $"알 수 없는 {kind} '{unknown}'입니다.";

        if (!string.IsNullOrWhiteSpace(suggestion))
        {
            message +=
                $" '{suggestion}'을(를) 입력하려던 것인지 확인해 주세요.";
        }

        return message;
    }
}
