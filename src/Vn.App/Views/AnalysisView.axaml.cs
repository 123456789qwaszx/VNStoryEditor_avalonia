using System;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Vn.Core;
using Vn.Core.Analysis;
using Vn.Core.Diagnostics;

namespace Vn.App.Views;

public partial class AnalysisView : UserControl
{
    public AnalysisView()
    {
        InitializeComponent();
    }

    private void OnAnalyzeClick(object? sender, RoutedEventArgs e)
    {
        string projectPath = ProjectPathBox.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            OutputBox.Text = "경로를 입력하세요.";
            return;
        }

        try
        {
            string fullPath = Path.GetFullPath(projectPath.Trim('"'));

            string schemaPath = Path.Combine(
                Path.GetDirectoryName(fullPath) ?? ".",
                "game.schema.json");

            AnalysisReport report =
                new VnProjectAnalyzer().Analyze(fullPath, schemaPath);

            OutputBox.Text = Format(report);
        }
        catch (Exception exception)
        {
            OutputBox.Text = $"[{exception.GetType().Name}] {exception.Message}";
        }
    }

    private static string Format(AnalysisReport report)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"소스 파일 {report.SourceFiles.Count}개, " +
                           $"노드 {report.Nodes.Count}개, " +
                           $"진단 {report.Diagnostics.Count}개");
        builder.AppendLine();

        foreach (VnDiagnostic diagnostic in report.Diagnostics)
        {
            builder.AppendLine(
                $"[{diagnostic.Severity}] {diagnostic.Code}  " +
                $"{diagnostic.FilePath}:{diagnostic.Line}:{diagnostic.Column}");
            builder.AppendLine($"    {diagnostic.Message}");
        }

        return builder.ToString();
    }
}