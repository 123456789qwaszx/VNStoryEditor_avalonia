using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
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

    // 분석은 Yarn 전체를 컴파일하므로 프로젝트가 커지면 눈에 띄게 오래 걸린다.
    // UI 스레드에서 돌리면 그동안 창이 통째로 멈춘다.
    //
    // Vn.Core에는 비동기 API를 두지 않는다. Core는 UI를 모르는 동기 라이브러리로 두고,
    // 어느 스레드에서 부를지는 부르는 쪽이 정한다. 그래서 감싸는 일은 여기서만 한다.
    private async void OnAnalyzeClick(object? sender, RoutedEventArgs e)
    {
        string projectPath = ProjectPathBox.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            OutputBox.Text = "경로를 입력하세요.";
            return;
        }

        // 버튼을 끄는 것은 안내이기도 하고 재진입 방지이기도 하다.
        // 분석 중에 또 누르면 두 번째 분석이 첫 번째 결과를 덮어쓴다.
        AnalyzeButton.IsEnabled = false;
        OutputBox.Text = "분석 중...";

        try
        {
            string fullPath = Path.GetFullPath(projectPath.Trim('"'));

            string schemaPath = Path.Combine(
                Path.GetDirectoryName(fullPath) ?? ".",
                "game.schema.json");

            AnalysisReport report = await Task.Run(
                () => new VnProjectAnalyzer().Analyze(fullPath, schemaPath));

            OutputBox.Text = Format(report);
        }
        catch (Exception exception)
        {
            // async void에서 예외가 새어나가면 앱이 그대로 죽는다.
            // 잡지 못한 예외가 없도록 여기서 전부 받는다.
            OutputBox.Text = $"[{exception.GetType().Name}] {exception.Message}";
        }
        finally
        {
            // 성공이든 실패든 버튼은 반드시 돌아와야 한다.
            // 여기가 비면 한 번 실패한 앱은 다시 분석할 수 없는 상태로 남는다.
            AnalyzeButton.IsEnabled = true;
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