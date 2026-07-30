using Vn.Core.Diagnostics;
using Vn.Core.Schema;
using Vn.Core.Story;
using Vn.Core.Yarn;

namespace Vn.Core.Validation;

internal static class SchemaUsageValidator
{
    public static IReadOnlyList<VnDiagnostic> Validate(
        GameSchema schema,
        IReadOnlyList<StoryNode> nodes,
        IReadOnlySet<string> explicitYarnVariables)
    {
        var diagnostics = new List<VnDiagnostic>();

        // 타입이 잘못된 변수도 "이름은 존재하는 것"으로 취급한다.
        // 스키마 오류 하나 때문에 그 변수를 쓰는 모든 줄에서 "알 수 없는 변수"가
        // 다시 쏟아지면 작가가 무엇을 고쳐야 할지 알 수 없다.
        HashSet<string> allowedVariables = schema.Variables
            .Where(variable => !string.IsNullOrWhiteSpace(variable.Id))
            .Select(variable =>
                GameSchemaLoader.NormalizeVariableName(variable.Id))
            .ToHashSet(StringComparer.Ordinal);

        allowedVariables.UnionWith(explicitYarnVariables);

        HashSet<string> allowedCommands = schema
            .EnumerateCommands()
            .Select(command => command.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        allowedCommands.UnionWith(YarnBuiltIns.Commands);

        foreach (StoryNode node in nodes)
        {
            AddUnknownNameDiagnostics(
                node.VariableReferences,
                allowedVariables,
                VnDiagnosticCodes.UnknownVariable,
                "변수",
                diagnostics);

            AddUnknownNameDiagnostics(
                node.CommandCalls,
                allowedCommands,
                VnDiagnosticCodes.UnknownCommand,
                "명령",
                diagnostics);
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
                VnDiagnosticCodes.UnknownJumpTarget,
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

    /// <summary>
    /// 이름이 쓰인 지점마다 진단을 하나씩 만든다.
    /// 같은 오타를 한 노드에서 두 번 썼으면 고칠 곳도 두 군데이므로 진단도 두 개여야 한다.
    /// </summary>
    private static void AddUnknownNameDiagnostics(
        IReadOnlyList<StoryReference> references,
        IReadOnlySet<string> allowed,
        string code,
        string kind,
        ICollection<VnDiagnostic> diagnostics)
    {
        foreach (StoryReference reference in references)
        {
            if (allowed.Contains(reference.Name))
            {
                continue;
            }

            string? suggestion =
                NameSuggester.FindClosest(reference.Name, allowed);

            diagnostics.Add(new VnDiagnostic(
                code,
                DiagnosticSeverity.Error,
                BuildUnknownNameMessage(kind, reference.Name, suggestion),
                reference.FilePath,
                reference.Line,
                reference.Column));
        }
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
