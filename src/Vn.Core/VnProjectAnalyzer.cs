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

        // 스키마가 없거나 깨져도 원고 자체는 열 수 있어야 한다.
        // 저작 도구에서 스키마 실패가 텍스트·노드·그래프 전체를 함께 죽이면
        // 사용자는 오류를 고칠 수 없다. 빈 스키마로 Yarn 분석을 계속하고,
        // 스키마 문제는 별도 진단으로 남긴다.
        GameSchema schema = schemaResult.Schema ?? GameSchema.Empty;

        YarnCompileOutput yarnOutput =
            _compiler.Compile(
                fullProjectPath,
                schema);

        IReadOnlyList<VnDiagnostic> customDiagnostics =
            schemaResult.Schema is null
                ? Array.Empty<VnDiagnostic>()
                : SchemaUsageValidator.Validate(
                    schema,
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
