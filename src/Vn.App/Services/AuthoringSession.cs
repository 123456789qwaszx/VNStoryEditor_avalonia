using Avalonia.Media.Imaging;
using Vn.Authoring.Assets;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Script;
using Vn.Authoring.Serialization;

namespace Vn.App.Services;

/// <summary>
/// 앱이 지금 무엇을 열고 있는지 아는 유일한 객체.
///
/// 편집 자체는 <see cref="Vn.Authoring.Editing.ProjectEditor"/>가 한다. 이 클래스는 그 위에
/// 파일 경로·저장 여부·선택 상태처럼 <em>도구</em>의 관심사만 얹는다.
/// 도메인이 파일 경로를 알 필요는 없고, 알게 되면 콘솔이나 테스트에서 쓰기 어려워진다.
/// </summary>
internal sealed class AuthoringSession
{
    private readonly HashSet<string> _expandedFileIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _knownFileIds = new(StringComparer.Ordinal);
    private string _fileListSignature = string.Empty;
    private string _savedSnapshot;

    public AuthoringSession()
    {
        Editor = new ProjectEditor(NewProjectInstance());
        ResetFileGraphState(Editor.Project);
        _savedSnapshot = ProjectSnapshotCodec.Encode(Editor.Project);
        Editor.Changed += OnEditorChanged;
    }

    public ProjectEditor Editor { get; }

    public StoryProject Project => Editor.Project;

    /// <summary>현재 프로젝트 manifest 경로. 아직 저장하지 않았으면 null이다.</summary>
    public string? ProjectPath { get; private set; }

    public GameDefinition Definition { get; private set; } = GameDefinition.Empty;

    /// <summary>
    /// 새 노드가 추가될 현재 작업 파일.
    ///
    /// 그래프에 펼쳐 보이는 파일 목록과는 독립된 workspace 상태다. 현재 파일을 접어 둔
    /// 상태에서도 그 파일을 계속 작업 대상으로 유지할 수 있다.
    /// </summary>
    public string? ActiveFileId { get; private set; }

    public StoryFile? ActiveFile => Project.FindFile(ActiveFileId);

    /// <summary>
    /// 그래프에 실제 NodeCard로 펼쳐 보일 StoryFile Id 목록.
    ///
    /// 저장 콘텐츠나 Undo 대상이 아니라 앱 workspace 상태다. 파일을 접어도 StoryProject와
    /// 실행·Settings 연결은 전혀 바뀌지 않는다.
    /// </summary>
    public IReadOnlySet<string> ExpandedFileIds => _expandedFileIds;

    public string? SelectedNodeId { get; private set; }

    public StoryNode? SelectedNode => Project.FindNode(SelectedNodeId);

    public string StatusMessage { get; private set; } = "새 프로젝트입니다. 노드를 추가해 시작하세요.";

    // ── 프리뷰 에셋 ────────────────────────────────────────────────────────

    private PreviewAssetLibrary? _assetLibrary;
    private string _assetLibrarySignature = string.Empty;

    /// <summary>
    /// 프리뷰 에셋 인덱스. 에셋 루트 설정이 바뀌면(편집·되돌리기·열기) 자동으로 다시
    /// 읽지만, 폴더 <b>내용</b>의 변경은 감지하지 않는다 — 그건 <see cref="RefreshAssets"/>
    /// (명시적 새로 고침)만 반영한다.
    /// </summary>
    public PreviewAssetLibrary AssetLibrary
    {
        get
        {
            (string? backgrounds, string? portraits) = ResolveAssetRoots();
            string signature = $"{backgrounds}{portraits}";

            if (_assetLibrary is null ||
                !string.Equals(_assetLibrarySignature, signature, StringComparison.Ordinal))
            {
                _assetLibrary = PreviewAssetLibrary.Load(backgrounds, portraits);
                _assetLibrarySignature = signature;
                ImageCache.Clear();
            }

            return _assetLibrary;
        }
    }

    /// <summary>프리뷰 비트맵 캐시. 해석이 끝난 절대 경로가 키다.</summary>
    public PreviewImageCache<Bitmap> ImageCache { get; } = new(path => new Bitmap(path));

    public void RefreshAssets()
    {
        _assetLibrary = null;
        ImageCache.Clear();
        SetStatus("프리뷰 에셋을 다시 읽었습니다.");
    }

    private (string? Backgrounds, string? Portraits) ResolveAssetRoots()
    {
        // 저장 전 프로젝트는 기준 디렉터리가 없어 상대 루트를 해석할 수 없다.
        // 절대 경로 루트는 그대로 동작한다.
        string basePath = ProjectPath
            ?? Path.Combine(Environment.CurrentDirectory, "unsaved" + ProjectManifestJson.FileExtension);

        return (
            AssetRootSettings.ResolveFrom(basePath, Project.AssetRoots.BackgroundsPath),
            AssetRootSettings.ResolveFrom(basePath, Project.AssetRoots.PortraitsPath));
    }

    /// <summary>저장한 뒤로 바뀐 것이 있는지. 실제 내용을 비교하므로 되돌리기로 원상복구하면 깨끗해진다.</summary>
    public bool IsDirty => !string.Equals(_savedSnapshot, ProjectSnapshotCodec.Encode(Project), StringComparison.Ordinal);

    public event EventHandler<ProjectChangedEventArgs>? Changed;

    public event EventHandler? SelectionChanged;

    /// <summary>
    /// 현재 파일 또는 그래프 펼침 상태가 바뀌었을 때.
    /// StoryProject 변경이 아니므로 <see cref="Changed"/>와 분리한다.
    /// </summary>
    public event EventHandler<FileGraphStateChangedEventArgs>? FileGraphStateChanged;

    public void Select(string? nodeId)
    {
        if (string.Equals(SelectedNodeId, nodeId, StringComparison.Ordinal))
        {
            return;
        }

        SelectedNodeId = nodeId;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    // 상태를 모두 맞춘 뒤에 알린다. 알린 다음에 상태를 바꾸면
    // 화면은 방금 지나간 값을 그린 채로 남는다.
    public void NewProject()
    {
        StoryProject project = NewProjectInstance();

        ProjectPath = null;
        Definition = GameDefinition.Empty;
        _savedSnapshot = ProjectSnapshotCodec.Encode(project);
        StatusMessage = "새 프로젝트입니다. 노드를 추가해 시작하세요.";

        ResetFileGraphState(project);
        Editor.Replace(project);
        Select(project.EnumerateNodes().FirstOrDefault()?.Id);
        RaiseFileGraphStateChanged(
            activeFileChanged: true,
            expandedFilesChanged: true,
            fileListChanged: true);
    }

    public void Open(string path)
    {
        ProjectLoadResult loaded = ProjectStore.Load(path);
        StoryProject project = loaded.Project;

        ProjectPath = loaded.ManifestPath;
        Definition = GameDefinition.LoadBeside(loaded.ManifestPath);
        _savedSnapshot = ProjectSnapshotCodec.Encode(project);
        StatusMessage =
            $"{Path.GetFileName(ProjectPath)} · 대본 {project.Scripts.Count}개 · " +
            $"노드 {project.EnumerateNodes().Count()}개 · " +
            $"발행 결과 {project.Results.DialogueResults.Count + project.Results.PresentationResults.Count}개";

        AppSettingsService.SaveRecentProject(loaded.ManifestPath);

        string? activeFileId = project.FindFileContainingNode(project.StartNodeId)?.Id
            ?? project.Files.FirstOrDefault()?.Id;
        ResetFileGraphState(project, activeFileId);

        Editor.Replace(project);
        Select(project.StartNodeId ?? project.EnumerateNodes().FirstOrDefault()?.Id);
        RaiseFileGraphStateChanged(
            activeFileChanged: true,
            expandedFilesChanged: true,
            fileListChanged: true);
    }

    public void Save(string? path = null)
    {
        string target = path ?? ProjectPath
            ?? throw new InvalidOperationException("저장할 경로가 없습니다.");

        ProjectStore.Save(target, Project);

        ProjectPath = Path.GetFullPath(target);
        _savedSnapshot = ProjectSnapshotCodec.Encode(Project);
        Definition = GameDefinition.LoadBeside(ProjectPath);
        AppSettingsService.SaveRecentProject(ProjectPath);

        StatusMessage = $"{Path.GetFileName(ProjectPath)}에 저장했습니다.";
        Changed?.Invoke(this, new ProjectChangedEventArgs(ProjectChangeKind.Content));
    }

    public void SetStatus(string message)
    {
        StatusMessage = message;
        Changed?.Invoke(this, new ProjectChangedEventArgs(ProjectChangeKind.Content));
    }

    /// <summary>
    /// 화자 목록을 <c>game.definition.json</c>에 쓴다 (X5, D-4 — 원천은 파일 하나다).
    /// 설정노드 UI는 이걸 부를 뿐 자체 사본을 갖지 않는다. 쓰고 나면 정의를 다시 읽어
    /// 대사노드 드롭다운·프리뷰 초상화 해석이 같은 원천의 새 값을 본다.
    /// </summary>
    public bool SaveSpeakers(IReadOnlyList<SpeakerSpec> speakers)
    {
        if (ProjectPath is null)
        {
            SetStatus("화자 목록은 game.definition.json에 저장됩니다. 프로젝트를 먼저 저장해 주세요.");
            return false;
        }

        GameDefinitionStore.SaveSpeakers(ProjectPath, speakers);
        Definition = GameDefinition.LoadBeside(ProjectPath);
        SetStatus($"{GameDefinition.FileName}에 화자 {Definition.Speakers.Count}명을 저장했습니다.");
        return true;
    }

    public bool IsFileExpanded(string fileId)
    {
        return _expandedFileIds.Contains(fileId);
    }

    public IEnumerable<StoryNode> EnumerateExpandedNodes()
    {
        return Project.Files
            .Where(file => _expandedFileIds.Contains(file.Id))
            .SelectMany(file => file.Nodes);
    }

    internal void SelectFile(string? fileId)
    {
        if (fileId is not null && Project.FindFile(fileId) is null)
        {
            return;
        }

        if (string.Equals(ActiveFileId, fileId, StringComparison.Ordinal))
        {
            return;
        }

        ActiveFileId = fileId;
        RaiseFileGraphStateChanged(
            activeFileChanged: true,
            expandedFilesChanged: false,
            fileListChanged: false);
    }

    internal void SetFileExpanded(string fileId, bool expanded)
    {
        if (Project.FindFile(fileId) is null)
        {
            return;
        }

        bool changed = expanded
            ? _expandedFileIds.Add(fileId)
            : _expandedFileIds.Remove(fileId);

        if (changed)
        {
            RaiseFileGraphStateChanged(
                activeFileChanged: false,
                expandedFilesChanged: true,
                fileListChanged: false);
        }
    }

    private void OnEditorChanged(object? sender, ProjectChangedEventArgs e)
    {
        bool activeChanged = false;
        bool expandedChanged = false;
        bool fileListChanged = false;

        HashSet<string> validFileIds = Project.Files
            .Select(file => file.Id)
            .ToHashSet(StringComparer.Ordinal);

        if (Project.FindFile(ActiveFileId) is null)
        {
            ActiveFileId = Project.Files.FirstOrDefault()?.Id;
            activeChanged = true;
        }

        expandedChanged |= _expandedFileIds.RemoveWhere(fileId => !validFileIds.Contains(fileId)) > 0;
        _knownFileIds.RemoveWhere(fileId => !validFileIds.Contains(fileId));

        // 새로 생긴 파일만 기본적으로 펼친다. 이미 존재하던 파일을 사용자가 접어 둔 상태는
        // 대사 수정이나 노드 이동 같은 프로젝트 변경 뒤에도 그대로 유지되어야 한다.
        foreach (string fileId in validFileIds)
        {
            if (_knownFileIds.Add(fileId))
            {
                expandedChanged |= _expandedFileIds.Add(fileId);
            }
        }

        string nextFileListSignature = BuildFileListSignature(Project);

        if (!string.Equals(_fileListSignature, nextFileListSignature, StringComparison.Ordinal))
        {
            _fileListSignature = nextFileListSignature;
            fileListChanged = true;
        }

        // 선택하고 있던 노드가 사라졌다면 선택을 놓는다.
        if (SelectedNodeId is not null && Project.FindNode(SelectedNodeId) is null)
        {
            SelectedNodeId = Project.EnumerateNodes().FirstOrDefault()?.Id;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        Changed?.Invoke(this, e);

        if (activeChanged || expandedChanged || fileListChanged)
        {
            RaiseFileGraphStateChanged(activeChanged, expandedChanged, fileListChanged);
        }
    }

    private void ResetFileGraphState(StoryProject project, string? activeFileId = null)
    {
        ActiveFileId = activeFileId ?? project.Files.FirstOrDefault()?.Id;

        _expandedFileIds.Clear();
        _knownFileIds.Clear();

        foreach (StoryFile file in project.Files)
        {
            _expandedFileIds.Add(file.Id);
            _knownFileIds.Add(file.Id);
        }

        _fileListSignature = BuildFileListSignature(project);
    }

    private void RaiseFileGraphStateChanged(
        bool activeFileChanged,
        bool expandedFilesChanged,
        bool fileListChanged)
    {
        FileGraphStateChanged?.Invoke(
            this,
            new FileGraphStateChangedEventArgs(
                activeFileChanged,
                expandedFilesChanged,
                fileListChanged));
    }

    private static string BuildFileListSignature(StoryProject project)
    {
        return string.Join(
            "\n",
            project.Files.Select(file =>
                $"{file.Id}\u001f{file.Name}\u001f{file.RelativePath}\u001f{file.Nodes.Count}"));
    }

    private static StoryProject NewProjectInstance()
    {
        // 대본을 미리 만들지 않는다 (X4, D-3) — 대사 노드가 생성될 때 전용 대본이
        // 함께 태어나므로, 미리 둔 대본은 아무도 안 읽는 빈 파일로 남을 뿐이다.
        var project = new StoryProject { Title = "새 프로젝트" };
        project.Files.Add(new StoryFile(name: "기본 파일"));
        return project;
    }
}

internal sealed class FileGraphStateChangedEventArgs : EventArgs
{
    public FileGraphStateChangedEventArgs(
        bool activeFileChanged,
        bool expandedFilesChanged,
        bool fileListChanged)
    {
        ActiveFileChanged = activeFileChanged;
        ExpandedFilesChanged = expandedFilesChanged;
        FileListChanged = fileListChanged;
    }

    public bool ActiveFileChanged { get; }

    public bool ExpandedFilesChanged { get; }

    /// <summary>파일 이름·경로·순서 또는 소유 노드 개수가 바뀌어 목록 행을 다시 만들어야 하는가.</summary>
    public bool FileListChanged { get; }
}
