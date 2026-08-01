using System.Text;
using Vn.Core;
using Vn.Core.Analysis;
using Vn.Core.Diagnostics;
using Vn.Core.Reporting;
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

    string root = StablePath.RootFor(report.ProjectPath);

    if (format == OutputFormat.List)
    {
        PrintList(report);
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

// 회귀 비교용 출력의 본체는 Vn.Core의 ListReportFormatter에 있다.
// CLI는 줄을 만들지 않고 받아 적기만 하므로, 테스트가 셸을 거치지 않고 같은 줄을 검사할 수 있다.
static void PrintList(AnalysisReport report)
{
    foreach (string line in ListReportFormatter.Format(report))
    {
        Console.WriteLine(line);
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

static string ToStablePath(string path, string root)
{
    return StablePath.ToStable(path, root);
}

internal enum OutputFormat
{
    Text,
    List
}