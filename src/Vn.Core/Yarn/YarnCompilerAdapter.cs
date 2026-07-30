using Yarn;
using Yarn.Compiler;
using Vn.Core.Diagnostics;
using Vn.Core.Schema;
using Vn.Core.Story;

namespace Vn.Core.Yarn;

internal sealed class YarnCompilerAdapter
{
    public YarnCompileOutput Compile(
        string yarnProjectPath,
        GameSchema schema)
    {
        string fullProjectPath =
            Path.GetFullPath(yarnProjectPath);

        if (!File.Exists(fullProjectPath))
        {
            return Failure(
                new VnDiagnostic(
                    VnDiagnosticCodes.YarnProjectNotFound,
                    DiagnosticSeverity.Error,
                    "Yarn 프로젝트 파일을 찾을 수 없습니다.",
                    fullProjectPath,
                    0,
                    0));
        }

        try
        {
            Project project =
                Project.LoadFromFile(fullProjectPath);

            string[] sourceFiles = project.SourceFiles
                .Select(Path.GetFullPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            if (sourceFiles.Length == 0)
            {
                return Failure(
                    new VnDiagnostic(
                        VnDiagnosticCodes.YarnProjectHasNoSource,
                        DiagnosticSeverity.Error,
                        ".yarnproject에 포함된 Yarn 소스 파일이 없습니다.",
                        fullProjectPath,
                        0,
                        0));
            }

            var compilationJob =
                CompilationJob.CreateFromFiles(sourceFiles);

            compilationJob.CompilationType =
                CompilationJob.Type.FullCompilation;

            compilationJob.LanguageVersion =
                project.FileVersion;

            compilationJob.Declarations =
                CreateSchemaDeclarations(schema);

            CompilationResult result =
                Compiler.Compile(compilationJob);

            IReadOnlyList<VnDiagnostic> diagnostics =
                result.Diagnostics
                    .Select(YarnDiagnosticMapper.Map)
                    .OrderBy(DiagnosticSortKey, StringComparer.Ordinal)
                    .ToArray();

            IReadOnlyList<StoryNode> nodes =
                ExtractNodes(result);

            IReadOnlySet<string> explicitVariables =
                result.Declarations
                    .Where(declaration =>
                        declaration.IsVariable &&
                        !declaration.IsImplicit)
                    .Select(declaration => declaration.Name)
                    .ToHashSet(StringComparer.Ordinal);

            return new YarnCompileOutput(
                sourceFiles,
                nodes,
                explicitVariables,
                diagnostics);
        }
        catch (Exception exception)
        {
            return Failure(
                new VnDiagnostic(
                    VnDiagnosticCodes.YarnUnexpectedFailure,
                    DiagnosticSeverity.Error,
                    $"Yarn 프로젝트 처리 중 예상하지 못한 오류가 발생했습니다. {exception.Message}",
                    fullProjectPath,
                    0,
                    0));
        }
    }

    private static IEnumerable<Declaration> CreateSchemaDeclarations(
        GameSchema schema)
    {
        // Yarn은 같은 이름을 두 번 선언하면 예외를 던지고, 그러면 컴파일 전체가 날아가
        // 노드도 점프도 하나 못 얻는다. 스키마 오타 하나가 나머지 분석을 전부 삼키면 안 된다.
        // 중복 자체는 VN1011이 이미 알렸으므로 여기서는 첫 선언만 쓴다.
        var declared = new HashSet<string>(StringComparer.Ordinal);

        foreach (VariableDefinition variable in schema.Variables)
        {
            if (string.IsNullOrWhiteSpace(variable.Id) ||
                !SchemaTypeMapper.IsSupported(variable.Type))
            {
                continue;
            }

            if (!declared.Add(
                    GameSchemaLoader.NormalizeVariableName(variable.Id)))
            {
                continue;
            }

            // default가 타입과 안 맞으면 VN1017이 이미 알렸다.
            // 여기서는 영값으로 계속 진행해서 오류 하나가 컴파일 전체를 막지 않게 한다.
            if (!SchemaTypeMapper.TryGetDefaultValue(
                    variable,
                    out IConvertible defaultValue))
            {
                defaultValue = SchemaTypeMapper.GetFallbackValue(variable.Type);
            }

            yield return Declaration.CreateVariable(
                GameSchemaLoader.NormalizeVariableName(variable.Id),
                SchemaTypeMapper.GetYarnType(variable.Type),
                defaultValue,
                variable.Description);
        }
    }

    private static IReadOnlyList<StoryNode> ExtractNodes(
        CompilationResult result)
    {
        YarnSymbolIndex symbols =
            YarnSymbolIndex.Build(result, NormalizeUri);

        return result.NodeMetadata
            .OrderBy(metadata => metadata.Title, StringComparer.Ordinal)
            .Select(metadata =>
            {
                string filePath =
                    NormalizeUri(metadata.Uri);

                int headerLine = ToOneBased(metadata.HeaderStartLine);
                int bodyStartLine = ToOneBased(metadata.BodyStartLine);
                int bodyEndLine = ToOneBased(metadata.BodyEndLine);

                StoryJump[] jumps = metadata.Jumps
                    .Select(jump => new StoryJump(
                        metadata.Title,
                        jump.DestinationTitle,
                        NormalizeUri(jump.Uri),
                        ToOneBased(jump.Range.Start.Line),
                        ToOneBased(jump.Range.Start.Character)))
                    .OrderBy(jump => jump.FilePath, StringComparer.Ordinal)
                    .ThenBy(jump => jump.Line)
                    .ThenBy(jump => jump.Column)
                    .ThenBy(
                        jump => jump.DestinationNodeTitle,
                        StringComparer.Ordinal)
                    .ToArray();

                return new StoryNode(
                    metadata.Title,
                    filePath,
                    headerLine,
                    bodyStartLine,
                    bodyEndLine,
                    symbols.Resolve(
                        YarnSymbolKind.Command,
                        filePath,
                        bodyStartLine,
                        bodyEndLine,
                        metadata.CommandCalls,
                        headerLine),
                    symbols.Resolve(
                        YarnSymbolKind.Variable,
                        filePath,
                        bodyStartLine,
                        bodyEndLine,
                        metadata.VariableReferences,
                        headerLine),
                    jumps);
            })
            .ToArray();
    }

    private static YarnCompileOutput Failure(
        VnDiagnostic diagnostic)
    {
        return new YarnCompileOutput(
            Array.Empty<string>(),
            Array.Empty<StoryNode>(),
            new HashSet<string>(StringComparer.Ordinal),
            new[] { diagnostic });
    }

    private static string NormalizeUri(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
            uri.IsFile)
        {
            return uri.LocalPath;
        }

        try
        {
            return Path.GetFullPath(value);
        }
        catch
        {
            return value;
        }
    }

    private static int ToOneBased(int zeroBased)
    {
        return zeroBased < 0
            ? 0
            : zeroBased + 1;
    }

    private static string DiagnosticSortKey(
        VnDiagnostic diagnostic)
    {
        return string.Join(
            '\u001f',
            diagnostic.FilePath,
            diagnostic.Line.ToString("D8"),
            diagnostic.Column.ToString("D8"),
            diagnostic.Code,
            diagnostic.Message);
    }
}
