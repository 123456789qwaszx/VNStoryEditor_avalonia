using Vn.Core;
using Vn.Core.Analysis;
using Vn.Core.Diagnostics;
using Vn.Core.Story;

return Run(args);

static int Run(string[] args)
{
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
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("오류와 경고가 없습니다.");
        Console.ResetColor();
    }
    else
    {
        foreach (VnDiagnostic diagnostic in report.Diagnostics)
        {
            Console.ForegroundColor =
                diagnostic.Severity switch
                {
                    DiagnosticSeverity.Error =>
                        ConsoleColor.Red,
                    DiagnosticSeverity.Warning =>
                        ConsoleColor.Yellow,
                    _ => ConsoleColor.Gray
                };

            string location =
                diagnostic.Line > 0
                    ? $"{ToDisplayPath(diagnostic.FilePath)}:{diagnostic.Line}:{diagnostic.Column}"
                    : ToDisplayPath(diagnostic.FilePath);

            Console.WriteLine(
                $"{location} [{diagnostic.Severity}] {diagnostic.Code}");

            Console.ResetColor();
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
