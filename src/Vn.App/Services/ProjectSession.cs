using System.Text.Json;
using Vn.Core;
using Vn.Core.Analysis;
using Vn.Core.Diagnostics;
using Vn.Core.Story;

namespace Vn.App.Services;

/// <summary>
/// 앱 전체가 공유하는 현재 작업 상태의 소유자.
/// 뷰는 상태를 복제하지 않고 사용자 의도를 이 객체에 전달한다.
/// </summary>
internal sealed class ProjectSession
{
    private readonly Func<string, string, AnalysisReport> _analyze;
    private int _analysisGeneration;
    private IReadOnlyList<string> _discoveredSourceFiles = Array.Empty<string>();

    public ProjectSession(Func<string, string, AnalysisReport>? analyze = null)
    {
        _analyze = analyze ?? new VnProjectAnalyzer().Analyze;
    }

    public event EventHandler? StateChanged;

    public event EventHandler? AnalysisChanged;

    public string? ProjectPath { get; private set; }

    public string? SchemaPath { get; private set; }

    public AnalysisReport? Report { get; private set; }

    public OpenDocumentSession? Document { get; private set; }

    public StoryNode? SelectedNode { get; private set; }

    public int SelectedLine { get; private set; } = 1;

    public int SelectedColumn { get; private set; } = 1;

    public bool IsAnalyzing { get; private set; }

    public string StatusMessage { get; private set; } = "Yarn 프로젝트를 열어 주세요.";

    public bool HasProject => ProjectPath is not null;

    public bool HasUnsavedChanges => Document?.IsDirty == true;

    public IReadOnlyList<string> SourceFiles =>
        Report is not null && Report.SourceFiles.Count > 0
            ? Report.SourceFiles
            : _discoveredSourceFiles;

    public IReadOnlyList<StoryNode> Nodes =>
        Report?.Nodes ?? Array.Empty<StoryNode>();

    public IReadOnlyList<VnDiagnostic> Diagnostics =>
        Report?.Diagnostics ?? Array.Empty<VnDiagnostic>();

    public async Task OpenProjectAsync(string projectPath)
    {
        string fullPath = Path.GetFullPath(projectPath.Trim('"'));

        ProjectPath = fullPath;
        SchemaPath = Path.Combine(
            Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory,
            "game.schema.json");

        Report = null;
        _discoveredSourceFiles = DiscoverSourceFiles(fullPath);
        Document = null;
        SelectedNode = null;
        SelectedLine = 1;
        SelectedColumn = 1;
        StatusMessage = "프로젝트를 읽는 중입니다…";
        OnStateChanged();

        AppSettingsService.SaveRecentProject(fullPath);
        await AnalyzeAsync(restoreSelection: true);
    }

    /// <summary>
    /// 전체 분석은 오래 걸릴 수 있다. 요청 세대를 비교해 늦게 끝난 이전 결과가
    /// 더 최신 프로젝트나 편집 상태를 덮지 못하게 한다.
    /// </summary>
    public async Task<bool> AnalyzeAsync(bool restoreSelection = true)
    {
        if (ProjectPath is null || SchemaPath is null)
        {
            return false;
        }

        int generation = ++_analysisGeneration;
        string projectPath = ProjectPath;
        string schemaPath = SchemaPath;
        string? selectedTitle = restoreSelection
            ? SelectedNode?.Title ?? AppSettingsService.LoadRecentNode(projectPath)
            : null;
        IsAnalyzing = true;
        StatusMessage = "대본을 검사하는 중입니다…";
        OnStateChanged();

        AnalysisReport? report = null;
        Exception? failure = null;

        try
        {
            report = await Task.Run(() => _analyze(projectPath, schemaPath));
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (generation != _analysisGeneration ||
            !string.Equals(ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        IsAnalyzing = false;

        if (failure is not null)
        {
            string? fallback = _discoveredSourceFiles.FirstOrDefault();

            if (fallback is not null)
            {
                TryOpenDocument(fallback);
            }

            StatusMessage = $"프로젝트를 검사하지 못했지만 원문은 계속 수정할 수 있습니다. {failure.Message}";
            OnStateChanged();
            return false;
        }

        if (report is null)
        {
            StatusMessage = "분석 결과를 받지 못했지만 원문은 계속 수정할 수 있습니다.";
            OnStateChanged();
            return false;
        }

        Report = report;

        if (report.SourceFiles.Count == 0)
        {
            _discoveredSourceFiles = DiscoverSourceFiles(projectPath);
        }
        else
        {
            _discoveredSourceFiles = report.SourceFiles;
        }

        StoryNode? next = null;

        if (!string.IsNullOrWhiteSpace(selectedTitle))
        {
            next = report.Nodes.FirstOrDefault(node =>
                string.Equals(node.Title, selectedTitle, StringComparison.Ordinal));
        }

        next ??= report.Nodes.FirstOrDefault();
        SelectedNode = next;

        if (next is not null)
        {
            SelectedLine = next.HeaderLine;
            SelectedColumn = 1;
            AppSettingsService.SaveRecentNode(projectPath, next.Title);

            if (Document is null ||
                !string.Equals(Document.Path, next.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                TryOpenDocument(next.FilePath);
            }
            else if (!Document.IsDirty)
            {
                Document.ReloadFromDisk();
            }
        }
        else if (Document is null)
        {
            string? fallback = SourceFiles.FirstOrDefault();

            if (fallback is not null)
            {
                TryOpenDocument(fallback);
            }
        }

        int errors = report.Diagnostics.Count(item => item.Severity == DiagnosticSeverity.Error);
        int warnings = report.Diagnostics.Count(item => item.Severity == DiagnosticSeverity.Warning);

        StatusMessage = report.Nodes.Count == 0
            ? $"읽을 수 있는 노드는 없지만 원문은 계속 수정할 수 있습니다. 오류 {errors}개, 주의 {warnings}개"
            : $"노드 {report.Nodes.Count}개 · 오류 {errors}개 · 주의 {warnings}개";

        AnalysisChanged?.Invoke(this, EventArgs.Empty);
        OnStateChanged();
        return true;
    }

    public bool WouldReplaceDocumentForNode(string title)
    {
        StoryNode? node = Nodes.FirstOrDefault(item =>
            string.Equals(item.Title, title, StringComparison.Ordinal));

        return node is not null && WouldReplaceDocument(node.FilePath);
    }

    public bool WouldReplaceDocument(string filePath)
    {
        return Document is not null &&
            !string.Equals(Document.Path, filePath, StringComparison.OrdinalIgnoreCase);
    }

    public bool SelectNode(string title)
    {
        StoryNode? node = Nodes.FirstOrDefault(item =>
            string.Equals(item.Title, title, StringComparison.Ordinal));

        if (node is null)
        {
            return false;
        }

        SelectedNode = node;
        SelectedLine = node.HeaderLine;
        SelectedColumn = 1;

        if (Document is null ||
            !string.Equals(Document.Path, node.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryOpenDocument(node.FilePath))
            {
                return false;
            }
        }

        if (ProjectPath is not null)
        {
            AppSettingsService.SaveRecentNode(ProjectPath, node.Title);
        }

        OnStateChanged();
        return true;
    }

    public bool SelectSourceFile(string filePath, int line = 1, int column = 1)
    {
        if (!TryOpenDocument(filePath))
        {
            return false;
        }

        SelectedNode = FindNode(filePath, line);
        SelectedLine = Math.Max(1, line);
        SelectedColumn = Math.Max(1, column);
        OnStateChanged();
        return true;
    }

    public bool SelectDiagnostic(VnDiagnostic diagnostic)
    {
        if (!HasFilePosition(diagnostic) || !File.Exists(diagnostic.FilePath))
        {
            StatusMessage = diagnostic.Message;
            OnStateChanged();
            return false;
        }

        return SelectSourceFile(diagnostic.FilePath, diagnostic.Line, diagnostic.Column);
    }


    public void SetSelectionPosition(int line, int column)
    {
        SelectedLine = Math.Max(1, line);
        SelectedColumn = Math.Max(1, column);
        OnStateChanged();
    }

    public void SetWorkingText(string text)
    {
        if (Document is null)
        {
            return;
        }

        Document.ReplaceText(text);
        OnStateChanged();
    }

    public bool ApplyLineEdit(StoryLineEdit edit)
    {
        if (Document is null || !Document.TryApplyLineEdit(edit))
        {
            return false;
        }

        SelectedLine = edit.Line;
        SelectedColumn = 1;
        OnStateChanged();
        return true;
    }

    public DocumentSaveResult SaveDocument(bool overwriteExternalChanges = false)
    {
        if (Document is null)
        {
            return DocumentSaveResult.NoChanges();
        }

        DocumentSaveResult result = Document.Save(overwriteExternalChanges);

        StatusMessage = result.Status switch
        {
            DocumentSaveStatus.Saved => "저장했습니다. 다시 검사합니다…",
            DocumentSaveStatus.NoChanges => "저장할 변경이 없습니다.",
            DocumentSaveStatus.ExternalConflict =>
                "파일이 다른 프로그램에서 변경되었습니다. 덮어쓸지 선택해 주세요.",
            DocumentSaveStatus.FileMissing =>
                "원본 파일이 사라졌습니다. 편집 내용은 앱 안에 그대로 남아 있습니다.",
            _ => $"저장하지 못했습니다. {result.Exception?.Message}"
        };

        OnStateChanged();
        return result;
    }

    public void DiscardDocumentChanges()
    {
        Document?.DiscardChanges();
        OnStateChanged();
    }

    public void SetStatus(string message)
    {
        StatusMessage = message;
        OnStateChanged();
    }

    private bool TryOpenDocument(string path)
    {
        try
        {
            Document = OpenDocumentSession.Open(path);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                ArgumentException or
                NotSupportedException)
        {
            StatusMessage =
                $"'{Path.GetFileName(path)}' 파일을 열지 못했습니다. {exception.Message}";
            return false;
        }
    }

    private StoryNode? FindNode(string filePath, int line)
    {
        return Nodes.FirstOrDefault(node =>
            string.Equals(node.FilePath, filePath, StringComparison.OrdinalIgnoreCase) &&
            line >= node.HeaderLine &&
            line <= Math.Max(node.BodyEndLine, node.HeaderLine));
    }

    private static bool HasFilePosition(VnDiagnostic diagnostic)
    {
        return diagnostic.Line > 0 &&
            !string.IsNullOrWhiteSpace(diagnostic.FilePath) &&
            Path.IsPathRooted(diagnostic.FilePath);
    }

    /// <summary>
    /// 컴파일러가 문법 오류로 노드를 하나도 돌려주지 못해도 실제 .yarn 파일은 열 수 있게 한다.
    /// Yarn 프로젝트의 일반적인 glob을 우선 지원하고, 읽지 못하면 프로젝트 폴더의 .yarn을 찾는다.
    /// </summary>
    private static IReadOnlyList<string> DiscoverSourceFiles(string projectPath)
    {
        string directory = Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory;

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(projectPath));

            if (document.RootElement.TryGetProperty("sourceFiles", out JsonElement sources) &&
                sources.ValueKind == JsonValueKind.Array)
            {
                bool recursive = sources.EnumerateArray()
                    .Select(item => item.GetString())
                    .Any(pattern => pattern?.Contains("**", StringComparison.Ordinal) == true);

                SearchOption option = recursive
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                return Directory.EnumerateFiles(directory, "*.yarn", option)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray();
            }
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                JsonException or
                ArgumentException)
        {
            // 아래의 보수적인 검색으로 계속한다.
        }

        try
        {
            return Directory.EnumerateFiles(directory, "*.yarn", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private void OnStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
