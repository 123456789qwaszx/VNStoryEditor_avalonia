using System.Text;
using Vn.Core;
using Vn.Core.Analysis;
using Vn.Core.Diagnostics;
using Vn.Core.Story;

return Run(args);

static int Run(string[] args)
{
    ConfigureConsole();

    if (args.Length is < 1 or > 2)
    {
        PrintUsage();
        return 64;
    }

    string projectPath = Path.GetFullPath(args[0]);

    string schemaPath = args.Length == 2
        ? Path.GetFullPath(args[1])
        : Path.Combine(
            Path.GetDirectoryName(projectPath)
                ?? Environment.CurrentDirectory,
            "game.schema.json");

    var analyzer = new VnProjectAnalyzer();

    AnalysisReport report =
        analyzer.Analyze(projectPath, schemaPath);

    PrintReport(report);

    return report.HasErrors
        ? 1
        : 0;
}

// 진단 메시지가 한국어라서 콘솔 코드 페이지가 949로 남아 있으면 전부 깨진다.
// 리다이렉트된 출력에는 색을 넣지 않는다. 파일이나 파이프에 제어 문자가 섞이면
// 골든 픽스처 비교가 무너진다.
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
        "사용법: Vn.Cli <project.yarnproject> [game.schema.json]");
}

static void PrintReport(AnalysisReport report)
{
    Console.WriteLine("VN Tool - Yarn 검증 결과");
    Console.WriteLine(new string('=', 56));
    Console.WriteLine($"Yarn 프로젝트: {report.ProjectPath}");
    Console.WriteLine($"게임 스키마:    {report.SchemaPath}");
    Console.WriteLine($"소스 파일:      {report.SourceFiles.Count}");
    Console.WriteLine($"노드:           {report.Nodes.Count}");
    Console.WriteLine();

    if (report.Nodes.Count > 0)
    {
        Console.WriteLine("[노드]");

        foreach (StoryNode node in report.Nodes)
        {
            Console.WriteLine(
                $"- {node.Title} ({ToDisplayPath(node.FilePath)}:{node.HeaderLine})");

            foreach (StoryJump jump in node.Jumps)
            {
                Console.WriteLine(
                    $"    -> {jump.DestinationNodeTitle} ({ToDisplayPath(jump.FilePath)}:{jump.Line}:{jump.Column})");
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

            string location =
                diagnostic.Line > 0
                    ? $"{ToDisplayPath(diagnostic.FilePath)}:{diagnostic.Line}:{diagnostic.Column}"
                    : ToDisplayPath(diagnostic.FilePath);

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

static string ToDisplayPath(string path)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return "(위치 없음)";
    }

    try
    {
        return Path.GetRelativePath(
            Environment.CurrentDirectory,
            path);
    }
    catch
    {
        return path;
    }
}
