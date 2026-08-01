using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Vn.App.Services;

namespace Vn.App;

/// <summary>
/// 앱 셸. 프로젝트 세션을 한 번 만들고 대본 화면과 그래프 화면에 연결한다.
/// </summary>
public partial class MainWindow : Window
{
    private const string BaseTitle = "VnTool";

    private readonly ProjectSession _session = new();
    private bool _closeConfirmed;
    private bool _openedRecentProject;

    public MainWindow()
    {
        InitializeComponent();

        Analysis.Attach(_session);

        _session.StateChanged += OnSessionStateChanged;
        _session.AnalysisChanged += OnAnalysisChanged;
        Analysis.NodeSelected += OnAnalysisNodeSelected;
        Graph.NodeSelected += OnGraphNodeSelected;

        Opened += OnOpened;
        Closing += OnClosing;

        RefreshShell();
    }

    /// <summary>
    /// 최근 프로젝트 복원은 편의 기능이다. 실패해도 앱을 닫지 않는다.
    ///
    /// 이 핸들러는 <c>async void</c>라 여기서 새는 예외는 잡아 줄 곳이 없고 곧장 프로세스를 죽인다.
    /// 그러면 마지막에 열었던 프로젝트가 깨졌다는 이유만으로 새 프로젝트를 열 기회조차 사라진다.
    /// 그래서 실패는 상태 줄로 알리고 빈 창은 그대로 쓸 수 있게 둔다.
    /// </summary>
    private async void OnOpened(object? sender, EventArgs e)
    {
        if (_openedRecentProject)
        {
            return;
        }

        _openedRecentProject = true;

        try
        {
            string? recent = AppSettingsService.LoadRecentProject();

            if (recent is not null)
            {
                await OpenProjectPathAsync(recent, askToDiscard: false);
            }
        }
        catch (Exception exception)
        {
            StartupLog.TryWrite("최근 프로젝트 복원", exception);
            _session.SetStatus(
                "마지막에 열었던 프로젝트를 다시 열지 못했습니다. " +
                $"'프로젝트 열기…'로 새로 열어 주세요. ({exception.Message})");
        }
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!await Analysis.ResolveUnsavedChangesAsync("다른 프로젝트를 열기"))
            {
                return;
            }

            IStorageProvider? storage = TopLevel.GetTopLevel(this)?.StorageProvider;

            if (storage is null || !storage.CanOpen)
            {
                _session.SetStatus("이 환경에서는 파일 선택 창을 열 수 없습니다.");
                return;
            }

            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Yarn 프로젝트 열기",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Yarn 프로젝트")
                        {
                            Patterns = new[] { "*.yarnproject" }
                        }
                    }
                });

            if (files.Count == 0)
            {
                return;
            }

            await OpenProjectPathAsync(files[0].Path.LocalPath, askToDiscard: false);
        }
        catch (Exception exception)
        {
            _session.SetStatus($"프로젝트를 열지 못했습니다. {exception.Message}");
        }
    }

    private async Task OpenProjectPathAsync(string path, bool askToDiscard)
    {
        if (askToDiscard &&
            !await Analysis.ResolveUnsavedChangesAsync("다른 프로젝트를 열기"))
        {
            return;
        }

        await _session.OpenProjectAsync(path);

        if (_session.SelectedNode is not null)
        {
            Graph.SelectByTitle(_session.SelectedNode.Title);
        }
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await Analysis.SaveAsync();
        }
        catch (Exception exception)
        {
            ReportRecoverable("저장", exception);
        }
    }

    private async void OnAnalyzeClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await Analysis.ReanalyzeAsync();
        }
        catch (Exception exception)
        {
            ReportRecoverable("다시 검사", exception);
        }
    }

    /// <summary>
    /// <c>async void</c> 핸들러에서 샌 예외는 앱을 죽인다. 한 번의 조작이 실패했다고
    /// 편집 중인 원고까지 잃게 할 이유는 없다. 상세 내용은 로그에, 작가에게는 한 문장만 보인다.
    /// </summary>
    private void ReportRecoverable(string action, Exception exception)
    {
        StartupLog.TryWrite(action, exception);
        _session.SetStatus($"{action}하지 못했습니다. {exception.Message}");
    }

    private void OnSessionStateChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshShell();
        }
        else
        {
            Dispatcher.UIThread.Post(RefreshShell);
        }
    }

    private void OnAnalysisChanged(object? sender, EventArgs e)
    {
        void RefreshGraph()
        {
            Graph.Show(_session.Nodes, _session.ProjectPath);

            if (_session.SelectedNode is not null)
            {
                Graph.SelectByTitle(_session.SelectedNode.Title);
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshGraph();
        }
        else
        {
            Dispatcher.UIThread.Post(RefreshGraph);
        }
    }

    private void OnAnalysisNodeSelected(object? sender, string title)
    {
        Graph.SelectByTitle(title);
    }

    private async void OnGraphNodeSelected(object? sender, string title)
    {
        try
        {
            bool selected = await Analysis.SelectNodeByTitleAsync(title);

            if (selected)
            {
                Graph.SelectByTitle(title);
            }
            else if (_session.SelectedNode is not null)
            {
                // 다른 파일로 이동하기 직전 미저장 확인을 취소했다면
                // 그래프도 실제 선택으로 되돌려 세 화면이 다른 장면을 가리키지 않게 한다.
                Graph.SelectByTitle(_session.SelectedNode.Title);
            }
        }
        catch (Exception exception)
        {
            ReportRecoverable("장면 이동", exception);
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || !_session.HasUnsavedChanges)
        {
            return;
        }

        e.Cancel = true;

        if (await Analysis.ResolveUnsavedChangesAsync("앱을 닫기"))
        {
            _closeConfirmed = true;
            Close();
        }
    }

    private void RefreshShell()
    {
        string? projectPath = _session.ProjectPath;
        string projectName = projectPath is null
            ? "열린 프로젝트 없음"
            : Path.GetFileNameWithoutExtension(projectPath);

        ProjectNameText.Text = projectName;
        ProjectPathText.Text = projectPath ?? string.Empty;
        StatusText.Text = _session.StatusMessage;
        DirtyText.Text = _session.HasUnsavedChanges
            ? "● 저장되지 않은 변경"
            : string.Empty;

        AnalysisProgress.IsVisible = _session.IsAnalyzing;
        OpenButton.IsEnabled = !_session.IsAnalyzing;
        SaveButton.IsEnabled = !_session.IsAnalyzing && _session.Document is not null;
        AnalyzeButton.IsEnabled = !_session.IsAnalyzing && _session.HasProject;

        Title = _session.HasUnsavedChanges
            ? $"* {BaseTitle} — {projectName}"
            : projectPath is null
                ? BaseTitle
                : $"{BaseTitle} — {projectName}";
    }
}
