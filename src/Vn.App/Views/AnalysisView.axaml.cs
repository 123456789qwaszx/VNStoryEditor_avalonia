using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Vn.Core;
using Vn.Core.Analysis;
using Vn.Core.Diagnostics;
using Vn.Core.Story;

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
            StatusText.Text = "경로를 입력하세요.";
            return;
        }

        // 버튼을 끄는 것은 안내이기도 하고 재진입 방지이기도 하다.
        // 분석 중에 또 누르면 두 번째 분석이 첫 번째 결과를 덮어쓴다.
        AnalyzeButton.IsEnabled = false;
        StatusText.Text = "분석 중...";
        ClearResults();

        try
        {
            string fullPath = Path.GetFullPath(projectPath.Trim('"'));

            string schemaPath = Path.Combine(
                Path.GetDirectoryName(fullPath) ?? ".",
                "game.schema.json");

            AnalysisReport report = await Task.Run(
                () => new VnProjectAnalyzer().Analyze(fullPath, schemaPath));

            ShowResults(report);
        }
        catch (Exception exception)
        {
            // async void에서 예외가 새어나가면 앱이 그대로 죽는다.
            // 잡지 못한 예외가 없도록 여기서 전부 받는다.
            StatusText.Text = $"[{exception.GetType().Name}] {exception.Message}";
        }
        finally
        {
            // 성공이든 실패든 버튼은 반드시 돌아와야 한다.
            // 여기가 비면 한 번 실패한 앱은 다시 분석할 수 없는 상태로 남는다.
            AnalyzeButton.IsEnabled = true;
        }
    }

    // 목록에 담는 것은 Core가 준 순서 그대로다. 여기서 다시 정렬하거나 거르지 않는다.
    // AnalysisReport.Diagnostics는 이미 정렬되어 있고, 그 순서가 CLI 출력·골든 픽스처와
    // 같은 순서다. 뷰가 순서를 바꾸면 화면과 픽스처가 서로 다른 것을 말하게 된다.
    private void ShowResults(AnalysisReport report)
    {
        StatusText.Text =
            $"소스 파일 {report.SourceFiles.Count}개, " +
            $"노드 {report.Nodes.Count}개, " +
            $"진단 {report.Diagnostics.Count}개";

        SourceFileList.ItemsSource = report.SourceFiles;

        NodeList.ItemsSource = report.Nodes
            .Select(FormatNode)
            .ToList();

        DiagnosticList.ItemsSource = report.Diagnostics
            .Select(FormatDiagnostic)
            .ToList();
    }

    private void ClearResults()
    {
        SourceFileList.ItemsSource = null;
        NodeList.ItemsSource = null;
        DiagnosticList.ItemsSource = null;
    }

    private static string FormatNode(StoryNode node)
    {
        return $"{node.Title}  ({node.FilePath}:{node.HeaderLine})";
    }

    private static string FormatDiagnostic(VnDiagnostic diagnostic)
    {
        return
            $"[{diagnostic.Severity}] {diagnostic.Code}  " +
            $"{diagnostic.FilePath}:{diagnostic.Line}:{diagnostic.Column}  " +
            $"{diagnostic.Message}";
    }
}
