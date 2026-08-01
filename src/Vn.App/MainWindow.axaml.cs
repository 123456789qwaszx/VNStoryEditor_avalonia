using System;
using System.ComponentModel;
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
        Analysis.UnsavedChanges += OnUnsavedChanges;
        Graph.NodeSelected += OnGraphNodeSelected;

        Closing += OnClosing;
    }

    private const string BaseTitle = "Vn.App";

    /// <summary>닫기를 한 번 물어본 뒤에는 다시 막지 않는다.</summary>
    private bool _closeConfirmed;

    private void OnUnsavedChanges(object? sender, bool dirty)
    {
        // 박스 탭을 보고 있으면 파일 머리글이 안 보인다. 제목 표시줄에도 같은 표시를 낸다.
        Title = dirty
            ? $"* {BaseTitle}"
            : BaseTitle;
    }

    /// <summary>
    /// 창을 닫을 때도 묻는다. 여기서 안 막으면 저장하지 않은 원고가 조용히 사라진다.
    /// </summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || !Analysis.HasUnsavedWork)
        {
            return;
        }

        // 대화상자를 기다리는 동안 창이 닫히면 안 되므로 일단 막고, 답을 받은 뒤 다시 닫는다.
        e.Cancel = true;

        try
        {
            if (await Analysis.ConfirmDiscardAsync("창을 닫기"))
            {
                _closeConfirmed = true;
                Close();
            }
        }
        catch (Exception)
        {
            // 물어보다 실패하면 닫지 않는다. 원고를 지키는 쪽으로 남는다.
        }
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
