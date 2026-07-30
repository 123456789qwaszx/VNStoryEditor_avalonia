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
                    "YARN-PROJECT-NOT-FOUND",
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
                        "YARN-NO-SOURCE",
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
                    .OrderBy(DiagnosticSortKey)
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
                    "YARN-UNEXPECTED",
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
        foreach (VariableDefinition variable in schema.Variables)
        {
            if (string.IsNullOrWhiteSpace(variable.Id) ||
                !SchemaTypeMapper.IsSupported(variable.Type))
            {
                continue;
            }

            yield return Declaration.CreateVariable(
                GameSchemaLoader.NormalizeVariableName(variable.Id),
                SchemaTypeMapper.GetYarnType(variable.Type),
                SchemaTypeMapper.GetDefaultValue(variable),
                variable.Description);
        }
    }

    private static IReadOnlyList<StoryNode> ExtractNodes(
        CompilationResult result)
    {
        return result.NodeMetadata
            .OrderBy(metadata => metadata.Title, StringComparer.Ordinal)
            .Select(metadata =>
            {
                string filePath =
                    NormalizeUri(metadata.Uri);

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
                    ToOneBased(metadata.HeaderStartLine),
                    metadata.CommandCalls
                        .OrderBy(name => name, StringComparer.Ordinal)
                        .ToArray(),
                    metadata.VariableReferences
                        .OrderBy(name => name, StringComparer.Ordinal)
                        .ToArray(),
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
