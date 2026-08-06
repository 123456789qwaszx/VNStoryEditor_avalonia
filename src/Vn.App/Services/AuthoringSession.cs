using Avalonia.Media.Imaging;
using Vn.Authoring.Assets;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
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

    /// <summary>
    /// 프리뷰 갈래 선택 (W35) — "지금 어느 갈래를 보고 있는가". 뷰 상태라 저장하지 않고,
    /// 프로젝트를 새로 열면 비워진다. 대사·연출 편집기가 같은 선택 하나를 본다(LineId 불변 덕).
    /// </summary>
    public StageBranchSelection BranchSelection { get; } = new();

    /// <summary>
    /// 조건 값 시뮬의 시작값 오버라이드 (W36-b) — 변수명 → "이 값으로 시작한다고 치자".
    /// 뷰 상태라 저장하지 않는다. 비어 있으면 등록 초기값 그대로다.
    /// </summary>
    public Dictionary<string, string> SimulationValues { get; } = new(StringComparer.Ordinal);

    // ── 런타임 tuning (W23) ────────────────────────────────────────────────

    private RuntimeTuningLibrary? _tuningLibrary;
    private string _tuningLibrarySignature = string.Empty;

    /// <summary>
    /// 런타임 tuning 덤프 인덱스. 경로 설정·정의 해상도가 바뀌면 다시 읽지만, 폴더
    /// <b>내용</b>의 변경은 <see cref="RefreshAssets"/>(명시적 새로 고침)만 반영한다 —
    /// <see cref="AssetLibrary"/>와 같은 규칙이다.
    /// </summary>
    public RuntimeTuningLibrary TuningLibrary
    {
        get
        {
            string? directory = ResolveTuningRoot();
            (double Width, double Height) fallback = Definition.PreviewResolution;
            string signature = $"{directory}{fallback.Width}x{fallback.Height}";

            if (_tuningLibrary is null ||
                !string.Equals(_tuningLibrarySignature, signature, StringComparison.Ordinal))
            {
                _tuningLibrary = RuntimeTuningLibrary.Load(directory, fallback);
                _tuningLibrarySignature = signature;
            }

            return _tuningLibrary;
        }
    }

    /// <summary>
    /// tuning 폴더 해석 — 정의 파일의 재지정이 있으면 그 경로(없으면 경고가 남도록 그대로 전달),
    /// 없으면 프로젝트 폴더의 <c>ExportedTuning</c> 규약 폴더(없으면 미수입 상태로 조용히 null —
    /// 아직 아무것도 약속하지 않은 기본값이므로 경고가 아니라 안내 대상이다).
    /// </summary>
    private string? ResolveTuningRoot()
    {
        string basePath = ProjectPath
            ?? Path.Combine(Environment.CurrentDirectory, "unsaved" + ProjectManifestJson.FileExtension);

        if (!string.IsNullOrWhiteSpace(Definition.RuntimeTuningPath))
        {
            return AssetRootSettings.ResolveFrom(basePath, Definition.RuntimeTuningPath);
        }

        string? conventional = AssetRootSettings.ResolveFrom(basePath, RuntimeTuningLibrary.DefaultFolderName);
        return conventional is not null && Directory.Exists(conventional) ? conventional : null;
    }

    /// <summary>해석된 초상화 루트 절대 경로 — 표정 스프라이트 복제(설정노드)가 쓴다.</summary>
    public string? PortraitsRoot => ResolveAssetRoots().Portraits;

    /// <summary>
    /// 앱에 내장된 기본 튜닝(런타임 실측 덤프의 스냅샷)을 프로젝트 옆 규약 폴더에 만든다 (W46).
    /// 이미 내용이 있는 폴더는 덮어쓰지 않는다 — 교체는 <see cref="ConnectTuningFolder"/>가 한다.
    /// </summary>
    public void CreateDefaultTuning()
    {
        if (TuningTargetRoot() is not { } target)
        {
            return;
        }

        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            SetStatus(
                $"튜닝 폴더가 이미 있습니다: {target} — 덮어쓰지 않습니다. " +
                "교체하려면 '튜닝 폴더 연결…'로 다른 폴더를 복사해 오세요.");
            return;
        }

        var assembly = typeof(AuthoringSession).Assembly;
        const string prefix = "DefaultTuning/";
        int written = 0;

        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            // LogicalName의 경로 구분자는 빌드 OS를 따른다 — 양쪽 다 받아 준다.
            string relative = name[prefix.Length..].Replace('\\', '/');
            string path = Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using Stream source = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"내장 기본 튜닝 리소스를 열 수 없습니다: {name}");
            using FileStream file = File.Create(path);
            source.CopyTo(file);
            written++;
        }

        RefreshAssets();
        SetStatus($"기본 튜닝 {written}개 파일을 만들었습니다: {target} · {TuningLibrary.Summary}");
    }

    /// <summary>
    /// 기존 튜닝 폴더를 골라 프로젝트 옆 규약 폴더로 복사해 연결한다 (W46).
    /// 같은 이름의 파일은 덮어쓴다 — "통째 교체"가 튜닝 관리 규약이다(io-reference §1-3).
    /// </summary>
    public void ConnectTuningFolder(string sourceDirectory)
    {
        if (TuningTargetRoot() is not { } target)
        {
            return;
        }

        string source = Path.GetFullPath(sourceDirectory);

        if (!Directory.Exists(source))
        {
            SetStatus($"폴더를 찾을 수 없습니다: {source}");
            return;
        }

        // 튜닝처럼 보이는지 최소 확인 — 엉뚱한 폴더를 통째로 복사하는 사고를 막는다.
        bool looksLikeTuning =
            File.Exists(Path.Combine(source, "base-resolution.json")) ||
            File.Exists(Path.Combine(source, "rig-schemas.json")) ||
            Directory.Exists(Path.Combine(source, "presets"));

        if (!looksLikeTuning)
        {
            SetStatus(
                "선택한 폴더에서 튜닝 파일을 찾지 못했습니다 " +
                "(base-resolution.json · rig-schemas.json · presets/ 중 하나는 있어야 합니다).");
            return;
        }

        if (string.Equals(source, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            RefreshAssets(); // 이미 규약 자리다 — 다시 읽기만 한다
            return;
        }

        int copied = 0;

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
            copied++;
        }

        RefreshAssets();
        SetStatus($"튜닝 폴더를 연결했습니다({copied}개 파일 복사): {target} · {TuningLibrary.Summary}");
    }

    /// <summary>튜닝이 놓일 규약 자리. 저장 전에는 기준 폴더가 없어 안내만 남기고 null.</summary>
    private string? TuningTargetRoot()
    {
        if (ProjectPath is null)
        {
            SetStatus("튜닝은 프로젝트 폴더 옆에 놓입니다 — 프로젝트를 먼저 저장해 주세요.");
            return null;
        }

        return Path.Combine(
            Path.GetDirectoryName(ProjectPath)!,
            RuntimeTuningLibrary.DefaultFolderName);
    }

    public void RefreshAssets()
    {
        _assetLibrary = null;
        _tuningLibrary = null;
        ImageCache.Clear();
        SetStatus("프리뷰 에셋을 다시 읽었습니다." +
            (TuningLibrary.IsLoaded ? $" · {TuningLibrary.Summary}" : string.Empty));
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
        BranchSelection.Clear();
        SimulationValues.Clear();
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
        BranchSelection.Clear();
        SimulationValues.Clear();
        _savedSnapshot = ProjectSnapshotCodec.Encode(project);
        _tuningLibrary = null;
        StatusMessage =
            $"{Path.GetFileName(ProjectPath)} · 대본 {project.Scripts.Count}개 · " +
            $"노드 {project.EnumerateNodes().Count()}개 · " +
            $"발행 결과 {project.Results.DialogueResults.Count + project.Results.PresentationResults.Count}개" +
            (TuningLibrary.IsLoaded ? $" · {TuningLibrary.Summary}" : string.Empty);

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

        // 판 전환 (GB-1): 활성 파일이 곧 보이는 판이다 — 그 파일만 펼치고 나머지는 접는다.
        // 다른 판을 함께 보고 싶으면 파일 목록의 펼침 체크로 다시 연다(체크는 독립 유지).
        bool expandedChanged = false;

        if (fileId is not null)
        {
            expandedChanged = _expandedFileIds.Count != 1 || !_expandedFileIds.Contains(fileId);
            _expandedFileIds.Clear();
            _expandedFileIds.Add(fileId);
        }

        RaiseFileGraphStateChanged(
            activeFileChanged: true,
            expandedFilesChanged: expandedChanged,
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
