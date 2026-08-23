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

        return Report(fullProjectPath, fullSchemaPath, schemaResult, schema, yarnOutput);
    }

    /// <summary>
    /// <b><c>.yarnproject</c> 없이</b> 파일 목록을 분석한다.
    ///
    /// 이미터가 방금 쓴 산출물을 그 자리에서 검증하는 길이다 — 검증하려고 산출 폴더에
    /// 프로젝트 파일을 만들어 두면, 유니티가 읽는 폴더가 달라지고 고아 스캔에도 걸린다.
    /// <b>검증 때문에 산출물이 달라지면 그것은 검증이 아니다.</b>
    ///
    /// <paramref name="schemaPath"/>가 <c>null</c>이면 <b>어휘 검사를 하지 않는다</b>(빈
    /// 스키마) — 문법과 전역 라인 ID 유일성만 본다. 저작 도구는 커맨드 어휘를
    /// <c>game.definition.json</c>으로 이미 저작 시점에 검사하므로, 그 둘을 한 어휘로
    /// 합치기 전까지는 여기서 다시 묻지 않는다.
    /// </summary>
    public AnalysisReport AnalyzeFiles(
        IReadOnlyList<string> sourceFiles,
        string? schemaPath = null,
        string? originLabel = null)
    {
        string fullSchemaPath =
            schemaPath is null ? string.Empty : Path.GetFullPath(schemaPath);

        GameSchemaLoadResult schemaResult =
            schemaPath is null
                ? new GameSchemaLoadResult(null, Array.Empty<VnDiagnostic>())
                : GameSchemaLoader.Load(fullSchemaPath);

        GameSchema schema = schemaResult.Schema ?? GameSchema.Empty;

        YarnCompileOutput yarnOutput =
            _compiler.CompileFiles(
                sourceFiles,
                schema,
                originLabel: originLabel);

        return Report(
            originLabel ?? string.Empty, fullSchemaPath, schemaResult, schema, yarnOutput);
    }

    /// <summary>두 입구가 공유하는 뒷부분 — 진단을 모으고 정렬한다.</summary>
    private static AnalysisReport Report(
        string projectPath,
        string schemaPath,
        GameSchemaLoadResult schemaResult,
        GameSchema schema,
        YarnCompileOutput yarnOutput)
    {
        string fullProjectPath = projectPath;
        string fullSchemaPath = schemaPath;

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
