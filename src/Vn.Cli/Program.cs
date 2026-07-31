using System.Text;
using Vn.Core;
using Vn.Core.Analysis;
using Vn.Core.Diagnostics;
using Vn.Core.Story;

return Run(args);

static int Run(string[] args)
{
    ConfigureConsole();

    if (!TryParseArguments(
            args,
            out string projectPath,
            out string schemaPath,
            out OutputFormat format))
    {
        PrintUsage();
        return 64;
    }

    var analyzer = new VnProjectAnalyzer();

    AnalysisReport report =
        analyzer.Analyze(projectPath, schemaPath);

    // 경로 기준은 프로젝트 파일이 있는 폴더다.
    // 현재 작업 디렉터리를 기준으로 삼으면 어디서 실행했느냐에 따라 출력이 달라지고,
    // 골든 픽스처가 "항상 같은 위치에서 돌린다"는 전제에 매달리게 된다.
    string root =
        Path.GetDirectoryName(report.ProjectPath)
        ?? Environment.CurrentDirectory;

    if (format == OutputFormat.List)
    {
        PrintList(report, root);
    }
    else
    {
        PrintText(report, root);
    }

    return report.HasErrors
        ? 1
        : 0;
}

static bool TryParseArguments(
    string[] args,
    out string projectPath,
    out string schemaPath,
    out OutputFormat format)
{
    projectPath = string.Empty;
    schemaPath = string.Empty;
    format = OutputFormat.Text;

    var positional = new List<string>();

    for (int index = 0; index < args.Length; index++)
    {
        string argument = args[index];

        if (!argument.StartsWith("--", StringComparison.Ordinal))
        {
            positional.Add(argument);
            continue;
        }

        string name;
        string? value;

        int separator = argument.IndexOf('=');

        if (separator >= 0)
        {
            name = argument[..separator];
            value = argument[(separator + 1)..];
        }
        else
        {
            name = argument;
            value = index + 1 < args.Length
                ? args[index + 1]
                : null;
            index++;
        }

        if (!string.Equals(name, "--format", StringComparison.Ordinal))
        {
            return false;
        }

        switch (value?.Trim().ToLowerInvariant())
        {
            case "text":
                format = OutputFormat.Text;
                break;

            case "list":
                format = OutputFormat.List;
                break;

            default:
                return false;
        }
    }

    if (positional.Count is < 1 or > 2)
    {
        return false;
    }

    projectPath = Path.GetFullPath(positional[0]);

    schemaPath = positional.Count == 2
        ? Path.GetFullPath(positional[1])
        : Path.Combine(
            Path.GetDirectoryName(projectPath)
                ?? Environment.CurrentDirectory,
            "game.schema.json");

    return true;
}

// 진단 메시지가 한국어라서 콘솔 코드 페이지가 949로 남아 있으면 전부 깨진다.
static void ConfigureConsole()
{
    try
    {
        Console.OutputEncoding = Encoding.UTF8;
    }
    catch (IOException)
    {
        // 콘솔 핸들이 없는 환경. 출력 자체는 그대로 진행한다.
    }
}

static bool UseColor()
{
    return !Console.IsOutputRedirected;
}

static void SetColor(ConsoleColor color)
{
    if (UseColor())
    {
        Console.ForegroundColor = color;
    }
}

static void ResetColor()
{
    if (UseColor())
    {
        Console.ResetColor();
    }
}

static void PrintUsage()
{
    Console.WriteLine(
        "사용법: Vn.Cli <project.yarnproject> [game.schema.json] [--format text|list]");
    Console.WriteLine();
    Console.WriteLine("  --format text  사람이 읽는 출력. 기본값.");
    Console.WriteLine("  --format list  탭으로 구분된 한 줄 한 항목. 회귀 비교용.");
}

/// <summary>
/// 회귀 비교용 출력.
///
/// 골든 픽스처를 사람이 읽는 문장에 걸면, 메시지 문구를 다듬을 때마다 픽스처가 전부 깨진다.
/// 그런데 이 도구에서 메시지 문구는 자주 다듬어야 하는 것이다 — 작가가 읽을 문장이니까.
/// 픽스처가 문구 수정에 저항하게 만들면 결국 문구를 안 고치게 된다.
///
/// 그래서 회귀의 본체는 여기, 문구가 빠진 형태에 둔다.
/// 코드·심각도·파일·줄·열만으로 드리프트는 전부 잡힌다.
/// 메시지 품질은 픽스처가 아니라 사람이 text 출력을 눈으로 보고 판단할 일이다.
/// </summary>
static void PrintList(AnalysisReport report, string root)
{
    foreach (string source in report.SourceFiles)
    {
        Console.WriteLine($"source\t{ToStablePath(source, root)}");
    }

    foreach (StoryNode node in report.Nodes)
    {
        Console.WriteLine(
            $"node\t{node.Title}\t{ToStablePath(node.FilePath, root)}\t{node.HeaderLine}");

        foreach (StoryJump jump in node.Jumps)
        {
            Console.WriteLine(
                $"jump\t{jump.SourceNodeTitle}\t{jump.DestinationNodeTitle}\t" +
                $"{ToStablePath(jump.FilePath, root)}\t{jump.Line}\t{jump.Column}");
        }

        // 텍스트는 넣지 않는다. 픽스처가 대사 문구에 걸리면 문구를 고칠 때마다 깨진다.
        // 개수와 위치만으로 드리프트는 잡힌다.
        foreach (StoryLine line in node.Lines)
        {
            Console.WriteLine(
                $"line\t{node.Title}\t{ToStablePath(line.FilePath, root)}\t{line.Line}\t" +
                $"{line.Depth}\t{(line.IsOption ? "opt" : "-")}\t" +
                $"{(string.IsNullOrEmpty(line.Speaker) ? "-" : line.Speaker)}\t" +
                $"{line.CommandsSincePreviousLine.Count}\t{line.Hashtags.Count}");
        }
    }

    foreach (VnDiagnostic diagnostic in report.Diagnostics)
    {
        Console.WriteLine(
            $"diag\t{diagnostic.Code}\t{diagnostic.Severity}\t" +
            $"{ToStablePath(diagnostic.FilePath, root)}\t{diagnostic.Line}\t{diagnostic.Column}");
    }
}

static void PrintText(AnalysisReport report, string root)
{
    Console.WriteLine("VN Tool - Yarn 검증 결과");
    Console.WriteLine(new string('=', 56));
    Console.WriteLine($"Yarn 프로젝트: {ToStablePath(report.ProjectPath, root)}");
    Console.WriteLine($"게임 스키마:    {ToStablePath(report.SchemaPath, root)}");
    Console.WriteLine($"소스 파일:      {report.SourceFiles.Count}");
    Console.WriteLine($"노드:           {report.Nodes.Count}");
    Console.WriteLine();

    if (report.Nodes.Count > 0)
    {
        Console.WriteLine("[노드]");

        foreach (StoryNode node in report.Nodes)
        {
            Console.WriteLine(
                $"- {node.Title} ({ToStablePath(node.FilePath, root)}:{node.HeaderLine})");

            foreach (StoryJump jump in node.Jumps)
            {
                Console.WriteLine(
                    $"    -> {jump.DestinationNodeTitle} " +
                    $"({ToStablePath(jump.FilePath, root)}:{jump.Line}:{jump.Column})");
            }
        }

        Console.WriteLine();
    }

    Console.WriteLine("[진단]");

    if (report.Diagnostics.Count == 0)
    {
        SetColor(ConsoleColor.Green);
        Console.WriteLine("오류와 경고가 없습니다.");
        ResetColor();
    }
    else
    {
        foreach (VnDiagnostic diagnostic in report.Diagnostics)
        {
            SetColor(diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => ConsoleColor.Red,
                DiagnosticSeverity.Warning => ConsoleColor.Yellow,
                _ => ConsoleColor.Gray
            });

            string path = ToStablePath(diagnostic.FilePath, root);

            string location = diagnostic.Line > 0
                ? $"{path}:{diagnostic.Line}:{diagnostic.Column}"
                : path;

            Console.WriteLine(
                $"{location} [{diagnostic.Severity}] {diagnostic.Code}");

            ResetColor();
            Console.WriteLine($"  {diagnostic.Message}");
        }
    }

    int errors = report.Diagnostics.Count(
        diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);

    int warnings = report.Diagnostics.Count(
        diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Warning);

    Console.WriteLine();
    Console.WriteLine(
        $"결과: 오류 {errors}개, 경고 {warnings}개");
}

/// <summary>
/// 머신·OS에 상관없이 같은 문자열이 나오도록 경로를 다듬는다.
/// 구분자를 <c>/</c>로 통일하므로 픽스처가 Windows와 mac 양쪽에서 그대로 통과한다.
/// </summary>
static string ToStablePath(string path, string root)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return "-";
    }

    // Yarn은 파일에 매이지 않은 진단에 "(External)" 같은 의사 경로를 쓴다.
    // 실제 경로가 아닌 것에 GetRelativePath를 걸면 "../../(External)" 처럼
    // 프로젝트 폴더가 어느 깊이에 있느냐에 따라 달라지는 문자열이 나온다.
    // 픽스처가 폴더 깊이에 매달리게 되므로, 절대 경로가 아니면 그대로 둔다.
    if (!Path.IsPathRooted(path))
    {
        return path.Replace('\\', '/');
    }

    string relative;

    try
    {
        relative = Path.GetRelativePath(root, path);
    }
    catch
    {
        relative = path;
    }

    return relative.Replace('\\', '/');
}

internal enum OutputFormat
{
    Text,
    List
}