using Vn.Core.Analysis;
using Vn.Core.Diagnostics;
using Vn.Core.Schema;
using Vn.Core.Validation;
using Vn.Core.Yarn;

namespace Vn.Core;

public sealed class VnProjectAnalyzer
{
    private readonly YarnCompilerAdapter _compiler = new();

    public AnalysisReport Analyze(
        string yarnProjectPath,
        string schemaPath)
    {
        string fullProjectPath =
            Path.GetFullPath(yarnProjectPath);

        string fullSchemaPath =
            Path.GetFullPath(schemaPath);

        GameSchemaLoadResult schemaResult =
            GameSchemaLoader.Load(fullSchemaPath);

        if (schemaResult.Schema is null)
        {
            return new AnalysisReport(
                fullProjectPath,
                fullSchemaPath,
                Array.Empty<string>(),
                Array.Empty<Story.StoryNode>(),
                SortDiagnostics(schemaResult.Diagnostics));
        }

        YarnCompileOutput yarnOutput =
            _compiler.Compile(
                fullProjectPath,
                schemaResult.Schema);

        IReadOnlyList<VnDiagnostic> customDiagnostics =
            SchemaUsageValidator.Validate(
                schemaResult.Schema,
                yarnOutput.Nodes,
                yarnOutput.ExplicitYarnVariables);

        IReadOnlyList<VnDiagnostic> diagnostics =
            SortDiagnostics(
                schemaResult.Diagnostics
                    .Concat(yarnOutput.Diagnostics)
                    .Concat(customDiagnostics));

        return new AnalysisReport(
            fullProjectPath,
            fullSchemaPath,
            yarnOutput.SourceFiles,
            yarnOutput.Nodes,
            diagnostics);
    }

    private static IReadOnlyList<VnDiagnostic> SortDiagnostics(
        IEnumerable<VnDiagnostic> diagnostics)
    {
        return diagnostics
            .Distinct()
            .OrderBy(diagnostic =>
                diagnostic.FilePath,
                StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Line)
            .ThenBy(diagnostic => diagnostic.Column)
            .ThenByDescending(diagnostic => diagnostic.Severity)
            .ThenBy(diagnostic =>
                diagnostic.Code,
                StringComparer.Ordinal)
            .ThenBy(diagnostic =>
                diagnostic.Message,
                StringComparer.Ordinal)
            .ToArray();
    }
}
