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

        string[] sourceFiles;
        int languageVersion;

        try
        {
            Project project =
                Project.LoadFromFile(fullProjectPath);

            sourceFiles = project.SourceFiles
                .Select(Path.GetFullPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            languageVersion = project.FileVersion;
        }
        catch (Exception exception)
        {
            return Failure(Unexpected(exception, fullProjectPath));
        }

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

        return CompileFiles(sourceFiles, schema, languageVersion, fullProjectPath);
    }

    /// <summary>
    /// <b><c>.yarnproject</c> 없이</b> 파일 목록을 바로 컴파일한다.
    ///
    /// 여는 이유 — 이미터가 <b>방금 쓴 산출물</b>을 그 자리에서 검증하려면 소스 파일이
    /// 이미 손에 있는데, 종전에는 그것을 다시 찾게 하려고 <c>.yarnproject</c>를 하나
    /// 만들어 줘야 했다. 그 파일을 산출 폴더에 두면 유니티가 읽는 폴더가 오염되고
    /// 고아 스캔에도 걸린다. 검증 때문에 산출물이 달라지면 검증이 아니다.
    ///
    /// 프로젝트 파일을 읽는 <see cref="Compile"/>도 파일 목록을 푼 뒤 여기로 들어온다 —
    /// 컴파일 규칙의 사본을 만들지 않는다.
    /// </summary>
    /// <param name="languageVersion">
    /// <c>.yarnproject</c>의 <c>projectFileVersion</c>에 해당한다. 목록만 받을 때는
    /// 그것을 말해 줄 파일이 없으므로 <see cref="DefaultLanguageVersion"/>을 쓴다.
    /// </param>
    /// <param name="originLabel">진단이 가리킬 자리. 없으면 첫 소스 파일의 폴더다.</param>
    public YarnCompileOutput CompileFiles(
        IReadOnlyList<string> sourceFilesIn,
        GameSchema schema,
        int languageVersion = DefaultLanguageVersion,
        string? originLabel = null)
    {
        if (sourceFilesIn is null || sourceFilesIn.Count == 0)
        {
            return Failure(
                new VnDiagnostic(
                    VnDiagnosticCodes.YarnProjectHasNoSource,
                    DiagnosticSeverity.Error,
                    "컴파일할 Yarn 소스 파일이 없습니다.",
                    originLabel ?? string.Empty,
                    0,
                    0));
        }

        string[] sourceFiles = sourceFilesIn
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        string origin = originLabel ?? Path.GetDirectoryName(sourceFiles[0]) ?? string.Empty;

        try
        {
            var compilationJob =
                CompilationJob.CreateFromFiles(sourceFiles);

            compilationJob.CompilationType =
                CompilationJob.Type.FullCompilation;

            compilationJob.LanguageVersion =
                languageVersion;

            compilationJob.Declarations =
                CreateSchemaDeclarations(schema);

            CompilationResult result =
                Compiler.Compile(compilationJob);

            IReadOnlyList<VnDiagnostic> diagnostics =
                MapDiagnostics(result, origin);

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
            return Failure(Unexpected(exception, origin));
        }
    }

    /// <summary>
    /// <c>.yarnproject</c>가 말해 주지 않을 때 쓰는 Yarn 언어 판(= <c>projectFileVersion</c>).
    /// 지금 무는 컴파일러는 <c>YarnSpinner.Compiler 3.2.1</c>이고 그 판이 3이다.
    /// 컴파일러를 올리다 이 값이 낡으면 문법 오류가 아니라 <b>판이 달라 나는 오류</b>가
    /// 나므로, 올릴 때 여기를 같이 본다.
    /// </summary>
    public const int DefaultLanguageVersion = 3;

    private static VnDiagnostic Unexpected(Exception exception, string origin) =>
        new(
            VnDiagnosticCodes.YarnUnexpectedFailure,
            DiagnosticSeverity.Error,
            $"Yarn 프로젝트 처리 중 예상하지 못한 오류가 발생했습니다. " +
            $"[{exception.GetType().Name}] {exception.Message}",
            origin,
            0,
            0);

    /// <summary>
    /// Yarn 진단을 우리 모델로 옮긴다.
    ///
    /// 옮기기 전에 진단 객체의 모양을 한 번 확인한다. 모양이 다르면 그 아래 결과가
    /// 전부 조용히 틀어지기 때문이다 — 심각도를 못 읽으면 오류가 사라진 것처럼 보이고,
    /// 파일 이름을 못 읽으면 위치가 사라진다. 그런 상태를 침묵으로 넘기지 않고 VN2004로 알린다.
    /// </summary>
    private static IReadOnlyList<VnDiagnostic> MapDiagnostics(
        CompilationResult result,
        string projectPath)
    {
        var diagnostics = new List<VnDiagnostic>();

        object? sample = result.Diagnostics.FirstOrDefault();

        if (sample is not null)
        {
            IReadOnlyList<string> missing =
                YarnDiagnosticMapper.FindMissingProperties(sample.GetType());

            if (missing.Count > 0)
            {
                diagnostics.Add(new VnDiagnostic(
                    VnDiagnosticCodes.YarnDiagnosticShapeUnknown,
                    DiagnosticSeverity.Error,
                    $"Yarn 진단 모델이 예상과 다릅니다. 찾지 못한 속성: {string.Join(", ", missing)}. " +
                    "이 상태에서는 Yarn이 낸 진단의 심각도와 위치를 신뢰할 수 없습니다. " +
                    "Directory.Packages.props의 YarnSpinner.Compiler 버전과 YarnDiagnosticMapper를 함께 확인하세요.",
                    projectPath,
                    0,
                    0));
            }
        }

        diagnostics.AddRange(
            result.Diagnostics.Select(YarnDiagnosticMapper.Map));

        return diagnostics
            .OrderBy(DiagnosticSortKey, StringComparer.Ordinal)
            .ToArray();
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
            // 실패해도 out 값은 타입별 영값으로 채워져 있으므로 그대로 쓴다.
            SchemaTypeMapper.TryGetDefaultValue(
                variable,
                out IConvertible defaultValue,
                out _);

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
            YarnSymbolIndex.Build(result, YarnPaths.Normalize);

        YarnLineIndex lines =
            YarnLineIndex.Build(result, YarnPaths.Normalize);

        YarnBlockIndex blocks =
            YarnBlockIndex.Build(result, YarnPaths.Normalize);

        return result.NodeMetadata
            .OrderBy(metadata => metadata.Title, StringComparer.Ordinal)
            .Select(metadata =>
            {
                string filePath =
                    YarnPaths.Normalize(metadata.Uri);

                int headerLine = ToOneBased(metadata.HeaderStartLine);
                int bodyStartLine = ToOneBased(metadata.BodyStartLine);
                int bodyEndLine = ToOneBased(metadata.BodyEndLine);

                // 평평한 목록과 트리가 같은 라인 객체를 보게 한다.
                IReadOnlyList<StoryLine> nodeLines =
                    lines.GetLines(filePath, bodyStartLine, bodyEndLine);

                StoryJump[] jumps = metadata.Jumps
                    .Select(jump => new StoryJump(
                        metadata.Title,
                        jump.DestinationTitle,
                        YarnPaths.Normalize(jump.Uri),
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
                        headerLine,
                        bodyStartLine,
                        bodyEndLine,
                        metadata.CommandCalls),
                    symbols.Resolve(
                        YarnSymbolKind.Variable,
                        filePath,
                        headerLine,
                        bodyStartLine,
                        bodyEndLine,
                        metadata.VariableReferences),
                    jumps,
                    nodeLines,
                    blocks.GetBody(filePath, bodyStartLine, bodyEndLine, nodeLines));
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