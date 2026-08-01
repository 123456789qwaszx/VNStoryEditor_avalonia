using Avalonia.Controls;
using Vn.Core.Analysis;

namespace Vn.App;

/// <summary>
/// 두 뷰를 잇는 자리.
///
/// 분석 탭과 그래프 탭은 서로를 모른다. 한쪽이 다른 쪽을 직접 잡으면
/// 뷰가 늘어날 때마다 서로를 아는 관계도 같이 늘어난다.
/// 배선은 여기 한 곳에만 둔다.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Analysis.Analyzed += OnAnalyzed;
        Graph.NodeSelected += OnGraphNodeSelected;
    }

    private void OnAnalyzed(object? sender, AnalysisReport report)
    {
        // 좌표는 프로젝트 폴더 옆 vn.workspace.json에 남는다.
        Graph.Show(report.Nodes, report.ProjectPath);
    }

    private void OnGraphNodeSelected(object? sender, string title)
    {
        Analysis.SelectNodeByTitle(title);
    }
}
