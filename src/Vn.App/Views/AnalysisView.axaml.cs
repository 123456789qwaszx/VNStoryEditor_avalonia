using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Vn.App.Services;
using Vn.Core.Diagnostics;
using Vn.Core.Story;

namespace Vn.App.Views;

/// <summary>
/// 대본 작업 화면. 상태를 소유하지 않고 <see cref="ProjectSession"/>을 표시한다.
/// </summary>
public partial class AnalysisView : UserControl
{
    private ProjectSession? _session;
    private bool _updatingUi;
    private bool _syncingSelection;
    private string? _displayedDocumentPath;
    private IReadOnlyList<NodeListItem> _allNodes = Array.Empty<NodeListItem>();

    public AnalysisView()
    {
        InitializeComponent();

        BoxList.LineSelected += OnBoxLineSelected;
        BoxList.LineEdited += OnBoxLineEdited;
    }

    public event EventHandler<string>? NodeSelected;

    internal void Attach(ProjectSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (_session is not null)
        {
            _session.StateChanged -= OnSessionStateChanged;
            _session.AnalysisChanged -= OnAnalysisChanged;
        }

        _session = session;
        _session.StateChanged += OnSessionStateChanged;
        _session.AnalysisChanged += OnAnalysisChanged;

        RebuildLists();
        RefreshState();
    }

    public bool HasUnsavedWork => _session?.HasUnsavedChanges == true;

    public async Task<bool> SaveAsync()
    {
        if (_session?.Document is null)
        {
            return true;
        }

        DocumentSaveResult result = _session.SaveDocument();

        if (result.Status == DocumentSaveStatus.ExternalConflict)
        {
            bool overwrite = await ConfirmExternalOverwriteAsync();

            if (!overwrite)
            {
                return false;
            }

            result = _session.SaveDocument(overwriteExternalChanges: true);
        }

        if (result.Status == DocumentSaveStatus.NoChanges)
        {
            return true;
        }

        if (result.Status != DocumentSaveStatus.Saved)
        {
            return false;
        }

        await _session.AnalyzeAsync(restoreSelection: true);
        return true;
    }

    public async Task<bool> ReanalyzeAsync()
    {
        if (_session is null)
        {
            return false;
        }

        if (_session.HasUnsavedChanges)
        {
            // SaveAsync가 저장 성공 뒤 재분석까지 수행한다. 여기서 다시 분석하면
            // 같은 프로젝트를 연속 두 번 컴파일하게 된다.
            return await SaveAsync();
        }

        return await _session.AnalyzeAsync(restoreSelection: true);
    }

    /// <summary>
    /// 현재 문서를 교체하는 행동 앞에서 공통으로 사용한다.
    /// 저장·버리기·취소의 의미가 프로젝트 전환, 파일 전환, 종료에서 동일하다.
    /// </summary>
    public async Task<bool> ResolveUnsavedChangesAsync(string action)
    {
        if (_session?.HasUnsavedChanges != true)
        {
            return true;
        }

        UnsavedChoice choice = await ShowUnsavedDialogAsync(action);

        switch (choice)
        {
            case UnsavedChoice.Save:
                return await SaveAsync();

            case UnsavedChoice.Discard:
                _session.DiscardDocumentChanges();
                return true;

            default:
                return false;
        }
    }

    public async Task<bool> SelectNodeByTitleAsync(string title)
    {
        if (_session is null)
        {
            return false;
        }

        if (_session.WouldReplaceDocumentForNode(title) &&
            !await ResolveUnsavedChangesAsync("다른 장면으로 이동"))
        {
            SyncSelections();
            return false;
        }

        bool selected = _session.SelectNode(title);

        if (selected)
        {
            RenderSelectedNode();
            SyncSelections();
            NodeSelected?.Invoke(this, title);
        }

        return selected;
    }

    private void OnSessionStateChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(RefreshState);
    }

    private void OnAnalysisChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(() =>
        {
            RebuildLists();
            RefreshState();
            RenderSelectedNode();
            SyncSelections();
        });
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    private void RefreshState()
    {
        if (_session is null)
        {
            return;
        }

        OpenDocumentSession? document = _session.Document;

        if (document is null)
        {
            _updatingUi = true;

            try
            {
                FileBox.Text = string.Empty;
            }
            finally
            {
                _updatingUi = false;
            }

            _displayedDocumentPath = null;
            DocumentTitleText.Text = "열린 문서 없음";
            SelectionText.Text = string.Empty;
            DocumentStateText.Text = string.Empty;
            BoxList.Clear();
            return;
        }

        DocumentTitleText.Text = Path.GetFileName(document.Path);
        SelectionText.Text = _session.SelectedNode is null
            ? RelativePath(document.Path)
            : $"장면: {_session.SelectedNode.Title} · {RelativePath(document.Path)}";

        DocumentStateText.Text = document.IsDirty
            ? "저장되지 않음"
            : "저장됨";

        if (!string.Equals(
                _displayedDocumentPath,
                document.Path,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(FileBox.Text ?? string.Empty, document.WorkingText, StringComparison.Ordinal))
        {
            int oldCaret = FileBox.CaretIndex;

            _updatingUi = true;

            try
            {
                FileBox.NewLine = NewLineFor(document.LineEndings);
                FileBox.Text = document.WorkingText;
                FileBox.CaretIndex = Math.Min(oldCaret, document.WorkingText.Length);
            }
            finally
            {
                _updatingUi = false;
            }

            _displayedDocumentPath = document.Path;
        }

        UpdateStructureNotice(document);
    }

    private void RebuildLists()
    {
        if (_session is null)
        {
            return;
        }

        _allNodes = _session.Nodes
            .Select(node => new NodeListItem(node, _session.ProjectPath))
            .ToArray();

        ApplyNodeFilter();

        SourceFileList.ItemsSource = _session.SourceFiles
            .Select(path => new SourceFileListItem(path, _session.ProjectPath))
            .ToArray();

        DiagnosticList.ItemsSource = _session.Diagnostics
            .Select(diagnostic => new DiagnosticListItem(diagnostic, _session.ProjectPath))
            .ToArray();

        int errors = _session.Diagnostics.Count(item => item.Severity == DiagnosticSeverity.Error);
        int warnings = _session.Diagnostics.Count(item => item.Severity == DiagnosticSeverity.Warning);
        int schemaUsageProblems = _session.Diagnostics.Count(item =>
            item.Code is VnDiagnosticCodes.UnknownVariable or VnDiagnosticCodes.UnknownCommand);

        DiagnosticSummaryText.Text = schemaUsageProblems >= 10
            ? $"오류 {errors}개 · 주의 {warnings}개\n게임 스키마 정의가 비어 있거나 실제 대본과 맞지 않아 같은 문제가 반복되고 있습니다."
            : $"오류 {errors}개 · 주의 {warnings}개";
    }

    private void ApplyNodeFilter()
    {
        string query = (SearchBox.Text ?? string.Empty).Trim();

        NodeList.ItemsSource = string.IsNullOrEmpty(query)
            ? _allNodes
            : _allNodes
                .Where(item =>
                    item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.Location.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
    }

    private void RenderSelectedNode()
    {
        if (_session?.SelectedNode is not StoryNode node ||
            _session.Document is not OpenDocumentSession document ||
            !string.Equals(node.FilePath, document.Path, StringComparison.OrdinalIgnoreCase))
        {
            BoxList.Clear();
            return;
        }

        // 원문 편집으로 물리적 줄 수가 달라졌다면 분석 당시 줄 번호를 신뢰하지 않는다.
        // 줄 수가 같을 때는 각 카드가 저장 시점 원문과 현재 원문을 대조해 안전한 줄만 편집한다.
        BoxList.Show(
            node.Body,
            document.WorkingText,
            allowEditing: document.CanUseAnalyzedLineMap,
            baselineText: document.SavedText);
        UpdateStructureNotice(document);
        MoveCaretTo(document.WorkingText, _session.SelectedLine, _session.SelectedColumn);
    }

    private void UpdateStructureNotice(OpenDocumentSession document)
    {
        bool stale = document.StructureIsStale;
        StructureNoticeBorder.IsVisible = stale;
        StructureNoticeText.Text = stale
            ? "Yarn 원문이 변경되어 현재 구조 보기는 이전 검사 결과입니다. " +
              "현재 원문과 정확히 일치하는 대사 카드는 계속 수정할 수 있지만, 줄 위치가 달라진 카드는 잠깁니다. " +
              "저장 후 다시 검사하면 구조와 문제 목록이 완전히 갱신됩니다."
            : string.Empty;
    }

    private async void OnNodeSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || NodeList.SelectedItem is not NodeListItem item)
        {
            return;
        }

        await SelectNodeByTitleAsync(item.Node.Title);
    }

    private async void OnSourceFileSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection ||
            _session is null ||
            SourceFileList.SelectedItem is not SourceFileListItem item)
        {
            return;
        }

        if (_session.WouldReplaceDocument(item.Path) &&
            !await ResolveUnsavedChangesAsync("다른 파일로 이동"))
        {
            SyncSelections();
            return;
        }

        _session.SelectSourceFile(item.Path);
        RenderSelectedNode();
        SyncSelections();

        if (_session.SelectedNode is not null)
        {
            NodeSelected?.Invoke(this, _session.SelectedNode.Title);
        }
    }

    private async void OnDiagnosticSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection ||
            _session is null ||
            DiagnosticList.SelectedItem is not DiagnosticListItem item)
        {
            return;
        }

        VnDiagnostic diagnostic = item.Diagnostic;

        if (HasFilePosition(diagnostic) &&
            _session.WouldReplaceDocument(diagnostic.FilePath) &&
            !await ResolveUnsavedChangesAsync("문제가 있는 파일로 이동"))
        {
            SyncSelections();
            return;
        }

        bool moved = _session.SelectDiagnostic(diagnostic);

        if (moved)
        {
            RenderSelectedNode();
            SyncSelections();
            MoveCaretTo(
                _session.Document?.WorkingText ?? string.Empty,
                diagnostic.Line,
                diagnostic.Column);

            if (_session.SelectedNode is not null)
            {
                NodeSelected?.Invoke(this, _session.SelectedNode.Title);
            }
        }
    }

    private void OnFileTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updatingUi || _session?.Document is null)
        {
            return;
        }

        _session.SetWorkingText(FileBox.Text ?? string.Empty);
    }

    private void OnBoxLineSelected(object? sender, StoryLine line)
    {
        if (_session is null)
        {
            return;
        }

        _session.SetSelectionPosition(line.Line, line.Column);
        MoveCaretTo(
            _session.Document?.WorkingText ?? string.Empty,
            line.Line,
            line.Column);
    }

    private void OnBoxLineEdited(object? sender, StoryLineEdit edit)
    {
        if (_session?.Document is null)
        {
            return;
        }

        if (!_session.ApplyLineEdit(edit))
        {
            _session.SetStatus(
                "이 대사의 원문 위치가 변경되어 구조 화면에서 안전하게 수정할 수 없습니다. 저장 후 다시 검사해 주세요.");
            RenderSelectedNode();
            return;
        }

        // 박스 수정은 같은 WorkingText를 변경하므로 원문 탭도 즉시 같은 내용을 보여준다.
        _updatingUi = true;

        try
        {
            FileBox.Text = _session.Document.WorkingText;
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void OnEditorTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (EditorTabs.SelectedIndex == 0)
        {
            RenderSelectedNode();
        }
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyNodeFilter();
        SyncSelections();
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            await SaveAsync();
            return;
        }

        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            SearchBox.Focus();
            SearchBox.SelectAll();
        }
    }

    private void SyncSelections()
    {
        if (_session is null)
        {
            return;
        }

        _syncingSelection = true;

        try
        {
            NodeList.SelectedItem = (NodeList.ItemsSource as IEnumerable<NodeListItem>)?
                .FirstOrDefault(item =>
                    string.Equals(
                        item.Node.Title,
                        _session.SelectedNode?.Title,
                        StringComparison.Ordinal));

            SourceFileList.SelectedItem =
                (SourceFileList.ItemsSource as IEnumerable<SourceFileListItem>)?
                    .FirstOrDefault(item =>
                        string.Equals(
                            item.Path,
                            _session.Document?.Path,
                            StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private async Task<UnsavedChoice> ShowUnsavedDialogAsync(string action)
    {
        Window? owner = TopLevel.GetTopLevel(this) as Window;

        if (owner is null)
        {
            return UnsavedChoice.Cancel;
        }

        var dialog = new Window
        {
            Title = "저장하지 않은 변경",
            Width = 480,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        UnsavedChoice choice = UnsavedChoice.Cancel;

        var save = new Button { Content = "저장하고 계속", IsDefault = true };
        var discard = new Button { Content = "버리고 계속" };
        var cancel = new Button { Content = "취소", IsCancel = true };

        save.Click += (_, _) =>
        {
            choice = UnsavedChoice.Save;
            dialog.Close();
        };

        discard.Click += (_, _) =>
        {
            choice = UnsavedChoice.Discard;
            dialog.Close();
        };

        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = $"저장하지 않은 대본 변경이 있습니다.\n{action} 전에 어떻게 할지 선택해 주세요.",
                    TextWrapping = TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { save, discard, cancel }
                }
            }
        };

        await dialog.ShowDialog(owner);
        return choice;
    }

    private async Task<bool> ConfirmExternalOverwriteAsync()
    {
        Window? owner = TopLevel.GetTopLevel(this) as Window;

        if (owner is null)
        {
            return false;
        }

        var dialog = new Window
        {
            Title = "외부 변경 발견",
            Width = 500,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        bool overwrite = false;
        var overwriteButton = new Button { Content = "내 편집 내용으로 덮어쓰기" };
        var cancelButton = new Button { Content = "취소", IsCancel = true, IsDefault = true };

        overwriteButton.Click += (_, _) =>
        {
            overwrite = true;
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = "이 파일이 VnTool에서 열린 뒤 다른 프로그램에서 변경되었습니다.\n" +
                           "덮어쓰면 외부 변경이 사라집니다. 취소하면 현재 편집 내용은 앱 안에 그대로 남습니다.",
                    TextWrapping = TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { overwriteButton, cancelButton }
                }
            }
        };

        await dialog.ShowDialog(owner);
        return overwrite;
    }

    private void MoveCaretTo(string text, int oneBasedLine, int oneBasedColumn)
    {
        FileBox.CaretIndex = GetCaretIndex(text, oneBasedLine, oneBasedColumn);
        int lineIndex = Math.Max(0, oneBasedLine - 1);

        Dispatcher.UIThread.Post(
            () =>
            {
                try
                {
                    FileBox.ScrollToLine(lineIndex);
                }
                catch (Exception)
                {
                    // 스크롤 실패는 편집 내용이나 선택 상태를 손상시키지 않는다.
                }
            },
            DispatcherPriority.Loaded);
    }

    private static int GetCaretIndex(string text, int oneBasedLine, int oneBasedColumn)
    {
        int lineStart = GetLineStartIndex(text, oneBasedLine);
        int lineEnd = GetLineEndIndex(text, lineStart);
        int offset = Math.Max(0, oneBasedColumn - 1);
        return Math.Min(lineStart + offset, lineEnd);
    }

    private static int GetLineStartIndex(string text, int oneBasedLine)
    {
        int target = Math.Max(1, oneBasedLine);
        int index = 0;

        for (int line = 1; line < target; line++)
        {
            int next = text.IndexOf('\n', index);

            if (next < 0)
            {
                break;
            }

            index = next + 1;
        }

        return Math.Min(index, text.Length);
    }

    private static int GetLineEndIndex(string text, int lineStart)
    {
        for (int index = lineStart; index < text.Length; index++)
        {
            if (text[index] is '\r' or '\n')
            {
                return index;
            }
        }

        return text.Length;
    }

    private string RelativePath(string path)
    {
        if (_session?.ProjectPath is null)
        {
            return Path.GetFileName(path);
        }

        string root = Path.GetDirectoryName(_session.ProjectPath) ?? Environment.CurrentDirectory;

        try
        {
            return Path.GetRelativePath(root, path);
        }
        catch (ArgumentException)
        {
            return Path.GetFileName(path);
        }
    }

    private static bool HasFilePosition(VnDiagnostic diagnostic)
    {
        return diagnostic.Line > 0 &&
            !string.IsNullOrWhiteSpace(diagnostic.FilePath) &&
            Path.IsPathRooted(diagnostic.FilePath);
    }

    private static string NewLineFor(LineEndingStyle style)
    {
        return style switch
        {
            LineEndingStyle.Lf => "\n",
            LineEndingStyle.CrLf => "\r\n",
            LineEndingStyle.Cr => "\r",
            _ => Environment.NewLine
        };
    }

    private enum UnsavedChoice
    {
        Save,
        Discard,
        Cancel
    }
}

public sealed class NodeListItem
{
    public NodeListItem(StoryNode node, string? projectPath)
    {
        Node = node;
        Title = node.Title;
        Location = PathDisplay.Relative(node.FilePath, projectPath) + $" · {node.HeaderLine}행";
    }

    public StoryNode Node { get; }
    public string Title { get; }
    public string Location { get; }
}

public sealed class SourceFileListItem
{
    public SourceFileListItem(string path, string? projectPath)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        RelativePath = PathDisplay.Relative(path, projectPath);
    }

    public string Path { get; }
    public string Name { get; }
    public string RelativePath { get; }
}

public sealed class DiagnosticListItem
{
    public DiagnosticListItem(VnDiagnostic diagnostic, string? projectPath)
    {
        Diagnostic = diagnostic;
        Code = diagnostic.Code;
        Message = diagnostic.Message;
        SeverityText = diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => "오류",
            DiagnosticSeverity.Warning => "주의",
            _ => "정보"
        };

        BadgeBrush = diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => Brushes.IndianRed,
            DiagnosticSeverity.Warning => Brushes.DarkGoldenrod,
            _ => Brushes.SteelBlue
        };

        Location = diagnostic.Line <= 0
            ? "프로젝트 전체"
            : $"{PathDisplay.Relative(diagnostic.FilePath, projectPath)} · {diagnostic.Line}행 {Math.Max(1, diagnostic.Column)}열";
    }

    public VnDiagnostic Diagnostic { get; }
    public string Code { get; }
    public string Message { get; }
    public string SeverityText { get; }
    public IBrush BadgeBrush { get; }
    public string Location { get; }
}

internal static class PathDisplay
{
    public static string Relative(string path, string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "위치 없음";
        }

        if (projectPath is null || !Path.IsPathRooted(path))
        {
            return Path.GetFileName(path);
        }

        string root = Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory;

        try
        {
            return Path.GetRelativePath(root, path);
        }
        catch (ArgumentException)
        {
            return Path.GetFileName(path);
        }
    }
}
