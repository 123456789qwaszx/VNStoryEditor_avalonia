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

        // 작성 규약은 전부 Warning이라 종료 코드를 바꾸지 않는다.
        // 못 지킨 파일도 열려야 하므로 읽어낸 뒤 알리기만 한다.
        IReadOnlyList<VnDiagnostic> conventionDiagnostics =
            WritingConventionValidator.Validate(yarnOutput.Nodes);

        IReadOnlyList<VnDiagnostic> diagnostics =
            SortDiagnostics(
                schemaResult.Diagnostics
                    .Concat(yarnOutput.Diagnostics)
                    .Concat(customDiagnostics)
                    .Concat(conventionDiagnostics));

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
