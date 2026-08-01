using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Vn.App.Services;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App;

/// <summary>
/// 앱 셸. 세션을 하나 만들고 그래프 화면과 노드 화면에 같은 것을 물려준다.
///
/// 두 화면은 서로를 모른다. 둘 다 <see cref="AuthoringSession"/>만 보고 있고,
/// 편집은 전부 <see cref="ProjectEditor"/>를 지난다. 그래서 여기에는 화면을 잇는 코드가 없다.
/// </summary>
public partial class MainWindow : Window
{
    private const string BaseTitle = "VnTool";

    private readonly AuthoringSession _session = new();
    private bool _restoredRecent;
    private bool _rebuildingFileList;

    public MainWindow()
    {
        InitializeComponent();

        Graph.Attach(_session);
        DialogueEditor.Attach(_session);
        SetEditor.Attach(_session);

        _session.Changed += OnSessionChanged;
        _session.SelectionChanged += OnSelectionChanged;
        _session.FileGraphStateChanged += OnFileGraphStateChanged;

        NewButton.Click += OnNewClick;
        OpenButton.Click += OnOpenClick;
        SaveButton.Click += OnSaveClick;
        SaveAsButton.Click += OnSaveAsClick;
        UndoButton.Click += (_, _) => _session.Editor.Undo();
        RedoButton.Click += (_, _) => _session.Editor.Redo();

        Opened += OnOpened;

        RebuildFileList();
        Graph.Rebuild();
        ShowSelectedNode();
        RefreshShell();
    }

    /// <summary>
    /// 최근 프로젝트 복원은 편의 기능이다. 실패해도 빈 창은 그대로 쓸 수 있어야 한다.
    /// 이 핸들러는 async void라 예외가 새면 잡아 줄 곳이 없고 곧장 프로세스를 죽인다.
    /// </summary>
    private void OnOpened(object? sender, EventArgs e)
    {
        if (_restoredRecent)
        {
            return;
        }

        _restoredRecent = true;

        try
        {
            if (AppSettingsService.LoadRecentProject() is { } recent)
            {
                _session.Open(recent);
            }
        }
        catch (Exception exception)
        {
            StartupLog.TryWrite("최근 프로젝트 복원", exception);
            _session.SetStatus(
                $"마지막에 열었던 프로젝트를 열지 못했습니다. '열기…'로 다시 시도해 주세요. ({exception.Message})");
        }
    }

    private void OnSessionChanged(object? sender, ProjectChangedEventArgs e)
    {
        void Apply()
        {
            ProjectRefreshPlan plan = ProjectRefreshPlanner.Plan(e.Kind, _session.SelectedNode);

            if (plan.RebuildGraph)
            {
                Graph.Rebuild();
            }
            else if (plan.RefreshGraphPositions)
            {
                // 좌표만 바뀌었다. 카드를 다시 만들면 드래그가 끊긴다.
                Graph.RefreshPositions();
            }

            if (plan.RebuildInspector)
            {
                RebuildInspector();
            }
            else if (plan.RefreshPreview)
            {
                // 화자·대사 내용 변경은 편집 컨트롤을 유지해야 하지만 Script Preview는
                // 현재 모델을 곧바로 반영해야 한다. Preview만 다시 합성한다.
                DialogueEditor.RefreshPreview();
            }

            RefreshShell();
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        ShowSelectedNode();
        Graph.HighlightSelection();
        RefreshShell();
    }

    private void OnFileGraphStateChanged(object? sender, FileGraphStateChangedEventArgs e)
    {
        void Apply()
        {
            RebuildFileList();

            if (e.ExpandedFilesChanged)
            {
                Graph.Rebuild();
            }

            RefreshShell();
        }

        // 체크박스나 라디오 버튼의 RoutedEvent가 끝나기 전에 파일 목록을 통째로
        // 교체하면 클릭한 컨트롤을 자기 이벤트 안에서 제거하게 된다. 다음 UI turn에서 갱신한다.
        Dispatcher.UIThread.Post(Apply);
    }

    // ── 파일 ────────────────────────────────────────────────────────────────

    private void OnNewClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            _session.NewProject();
        }
        catch (Exception exception)
        {
            Report("새 프로젝트", exception);
        }
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            IStorageProvider? storage = GetTopLevel(this)?.StorageProvider;

            if (storage is null || !storage.CanOpen)
            {
                _session.SetStatus("이 환경에서는 파일 선택 창을 열 수 없습니다.");
                return;
            }

            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "VnTool 프로젝트 열기",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { ProjectFileType() }
                });

            if (files.Count > 0)
            {
                _session.Open(files[0].Path.LocalPath);
            }
        }
        catch (Exception exception)
        {
            Report("프로젝트 열기", exception);
        }
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_session.ProjectPath is null)
            {
                await SaveAsAsync();
                return;
            }

            _session.Save();
            RefreshShell();
        }
        catch (Exception exception)
        {
            Report("저장", exception);
        }
    }

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await SaveAsAsync();
        }
        catch (Exception exception)
        {
            Report("저장", exception);
        }
    }

    private async Task SaveAsAsync()
    {
        IStorageProvider? storage = GetTopLevel(this)?.StorageProvider;

        if (storage is null || !storage.CanSave)
        {
            _session.SetStatus("이 환경에서는 저장 창을 열 수 없습니다.");
            return;
        }

        IStorageFile? file = await storage.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "VnTool 프로젝트 저장",
                SuggestedFileName = "project" + ProjectManifestJson.FileExtension,
                FileTypeChoices = new[] { ProjectFileType() }
            });

        if (file is not null)
        {
            _session.Save(file.Path.LocalPath);
            RefreshShell();
        }
    }

    private static FilePickerFileType ProjectFileType()
    {
        return new FilePickerFileType("VnTool 프로젝트")
        {
            Patterns = new[] { "*.vnproject.json", "*.vnstory.json", "*.json" }
        };
    }

    // ── 화면 ────────────────────────────────────────────────────────────────

    private void RebuildFileList()
    {
        _rebuildingFileList = true;

        try
        {
            FileListPanel.Children.Clear();

            foreach (StoryFile file in _session.Project.Files)
            {
                string fileId = file.Id;

                var expanded = new CheckBox
                {
                    IsChecked = _session.IsFileExpanded(fileId),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                ToolTip.SetTip(expanded, "이 파일의 노드를 그래프에 펼쳐 표시");

                expanded.IsCheckedChanged += (_, _) =>
                {
                    if (!_rebuildingFileList)
                    {
                        _session.SetFileExpanded(fileId, expanded: expanded.IsChecked == true);
                    }
                };

                var active = new RadioButton
                {
                    GroupName = "ActiveStoryFile",
                    IsChecked = string.Equals(_session.ActiveFileId, fileId, StringComparison.Ordinal),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Content = new StackPanel
                    {
                        Spacing = 1,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = file.Name,
                                FontWeight = FontWeight.SemiBold,
                                TextTrimming = TextTrimming.CharacterEllipsis
                            },
                            new TextBlock
                            {
                                Text = $"{file.Nodes.Count}개 노드 · {file.RelativePath}",
                                FontSize = 10,
                                Opacity = 0.55,
                                TextTrimming = TextTrimming.CharacterEllipsis
                            }
                        }
                    }
                };

                active.IsCheckedChanged += (_, _) =>
                {
                    if (!_rebuildingFileList && active.IsChecked == true)
                    {
                        _session.SelectFile(fileId);
                    }
                };

                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("54,*"),
                    MinHeight = 42
                };

                Grid.SetColumn(expanded, 0);
                Grid.SetColumn(active, 1);
                row.Children.Add(expanded);
                row.Children.Add(active);

                FileListPanel.Children.Add(new Border
                {
                    Padding = new Thickness(2),
                    CornerRadius = new CornerRadius(5),
                    Background = string.Equals(_session.ActiveFileId, fileId, StringComparison.Ordinal)
                        ? new SolidColorBrush(Color.FromArgb(24, 37, 99, 235))
                        : Brushes.Transparent,
                    Child = row
                });
            }

            if (_session.Project.Files.Count == 0)
            {
                FileListPanel.Children.Add(new TextBlock
                {
                    Text = "StoryFile이 없습니다.",
                    Margin = new Thickness(6, 10),
                    Opacity = 0.55,
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }
        finally
        {
            _rebuildingFileList = false;
        }
    }

    private void ShowSelectedNode()
    {
        StoryNode? node = _session.SelectedNode;

        DialogueEditor.IsVisible = node is DialogueNode;
        SetEditor.IsVisible = node is SetNode;
        EmptyText.IsVisible = node is null or PresentationNode;
        EmptyText.Text = node is PresentationNode presentation
            ? $"{presentation.Name}\n\n{presentation.Bindings.Count}개 LineId binding\n연출 명령 편집 UI는 다음 단계에서 추가합니다."
            : "노드를 선택하면 여기서 편집합니다.";

        if (node is DialogueNode)
        {
            DialogueEditor.Show(node.Id);
        }
        else if (node is SetNode)
        {
            SetEditor.Show(node.Id);
        }
    }

    /// <summary>
    /// 구조가 바뀌었을 때 오른쪽 화면을 다시 만든다.
    /// 선택된 노드가 그대로면 같은 노드를 다시 그리고, 바뀌었으면 그쪽으로 옮긴다.
    /// </summary>
    private void RebuildInspector()
    {
        StoryNode? node = _session.SelectedNode;

        if (node is DialogueNode && DialogueEditor.NodeId == node.Id)
        {
            DialogueEditor.Rebuild();
            return;
        }

        if (node is SetNode && SetEditor.NodeId == node.Id)
        {
            SetEditor.Rebuild();
            return;
        }

        if (node is PresentationNode)
        {
            ShowSelectedNode();
            return;
        }

        ShowSelectedNode();
    }

    private void RefreshShell()
    {
        string name = _session.ProjectPath is null
            ? _session.Project.Title
            : ProjectDisplayName(_session.ProjectPath);

        ProjectNameText.Text = name;
        ProjectPathText.Text = _session.ProjectPath ?? "저장되지 않음";
        ActiveFileSummaryText.Text = _session.ActiveFile is { } activeFile
            ? $"현재: {activeFile.Name}"
            : "현재 작업 파일 없음";
        StatusText.Text = _session.StatusMessage;

        bool dirty = _session.IsDirty;
        DirtyText.Text = dirty ? "● 저장되지 않은 변경" : string.Empty;

        UndoButton.IsEnabled = _session.Editor.CanUndo;
        RedoButton.IsEnabled = _session.Editor.CanRedo;

        Title = dirty ? $"* {BaseTitle} — {name}" : $"{BaseTitle} — {name}";
    }

    private static string ProjectDisplayName(string path)
    {
        string fileName = Path.GetFileName(path) ?? string.Empty;
        return fileName.EndsWith(ProjectManifestJson.FileExtension, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^ProjectManifestJson.FileExtension.Length]
            : Path.GetFileNameWithoutExtension(fileName) ?? fileName;
    }

    private void Report(string action, Exception exception)
    {
        StartupLog.TryWrite(action, exception);
        _session.SetStatus($"{action}하지 못했습니다. {exception.Message}");
        RefreshShell();
    }
}
