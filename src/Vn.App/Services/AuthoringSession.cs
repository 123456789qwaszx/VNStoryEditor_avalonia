using Avalonia.Media.Imaging;
using Vn.Authoring.Assets;
using Vn.Authoring.Chapters;
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

    /// <summary>
    /// `game.definition.json`의 내용 — 기획자가 소유하는 어휘(스탯·조건·화자·연출 카탈로그).
    ///
    /// ⚠ <b>바뀌면 스스로 알린다</b> (2026-08-26 소유자 보고: 챕터 그래프에서 화자를 지웠는데
    /// 연출 그래프가 옛 목록을 계속 보였다). 예전에는 쓰는 자리마다 파일을 다시 읽고
    /// <b>조용히</b> 이 칸만 갈아 끼웠고, 이미 그려진 화면은 그 사실을 알 길이 없어 제 노드를
    /// 다시 고를 때까지 낡은 목록을 들고 있었다.
    ///
    /// <b>알림을 세터에 둔 이유</b>: 다시 읽는 자리가 다섯이라(열기·저장·화자 저장·변수 등록·
    /// 새 프로젝트) 부르는 쪽마다 알림을 적으면 언젠가 한 곳이 빠진다. 값이 바뀌는 길이
    /// 하나면 알림도 하나다.
    /// </summary>
    public GameDefinition Definition
    {
        get;

        private set
        {
            field = value;
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    } = GameDefinition.Empty;

    /// <summary>
    /// <b>A계층(기획자) 스탯 키</b> — 챕터 `스탯` 시트가 등록한 것들 (2026-08-17 소유자:
    /// "둘은 서로 완전히 다른 계층이야").
    ///
    /// 정의 파일의 `variables`는 두 계층의 변수를 <b>한 목록에</b> 담고 있어서, 작가의 변수
    /// 후보를 그대로 쓰면 <c>trust</c>에 <c>&lt;&lt;set&gt;&gt;</c>을 걸 수 있게 된다 —
    /// <b>스탯이 변하는 자리는 간선뿐</b>이라는 규칙이 화면에서 깨진다. 그래서 작가 화면은
    /// 이 집합을 빼고 후보를 세운다.
    ///
    /// 원천은 챕터 그래프가 읽은 목록 하나다(두 곳이 따로 xlsx를 읽지 않는다).
    /// </summary>
    public IReadOnlySet<string> ChapterStatKeys { get; private set; } =
        new HashSet<string>(StringComparer.Ordinal);

    // ⚠ `ChapterSpeakerNames`·`ChapterSpeakerCharacterIds`는 2026-08-23에 사라졌다
    // (소유자: "엑셀 내 어떤 것에서도 화자를 사용하지 않는다 … 화자는 툴 내부에서 직접
    // 정의해서 쓰는 게 맞는 것이였다"). 챕터 `화자` 시트가 폐지되면서 챕터에서 오는 화자
    // 어휘라는 것 자체가 없어졌다 — 화자의 유일한 원천은 <see cref="Definition"/>이다.

    /// <summary>챕터 목록을 다시 읽을 때마다 A계층 어휘를 따라잡는다.</summary>
    public void SupplyChapterVocabulary(IReadOnlyList<ChapterEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        ChapterStatKeys = entries
            .Where(entry => entry.Model is not null)
            .SelectMany(entry => entry.Model!.Stats.Select(stat => stat.Key))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.Ordinal);
    }

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

    /// <summary>해석된 배경 루트 절대 경로 — 가져오기·폴더 열기(W48)가 쓴다.</summary>
    public string? BackgroundsRoot => ResolveAssetRoots().Backgrounds;

    /// <summary>해석된 BGM 루트 절대 경로 (W59). 파일명이 곧 clipKey다.</summary>
    public string? BgmRoot => ResolvePath(Project.AssetRoots.BgmPath);

    /// <summary>해석된 효과음 루트 절대 경로 (W59). 파일명이 곧 clipKey다.</summary>
    public string? SfxRoot => ResolvePath(Project.AssetRoots.SfxPath);

    /// <summary>오디오 파일 확장자 규약 (W59) — 이 셋만 clipKey 후보가 된다.</summary>
    public static readonly string[] AudioExtensions = [".mp3", ".wav", ".ogg"];

    /// <summary>루트 폴더의 오디오 clipKey 목록 (W59) — 파일명(확장자 제외)이 곧 키다.</summary>
    public IReadOnlyList<string> AudioClipKeys(string? root)
    {
        if (root is null || !Directory.Exists(root))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(root)
            .Where(file => AudioExtensions.Contains(
                Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(key => !string.IsNullOrEmpty(key))
            .Select(key => key!)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// clipKey를 실제 파일 경로로 되찾는다 (W62 미리 듣기) — 파일명(확장자 제외)=키
    /// 규약의 역방향. 없으면 null — 호출자가 사유를 상태줄로 알린다.
    /// </summary>
    public string? ResolveAudioClipPath(string? root, string clipKey)
    {
        if (root is null || string.IsNullOrWhiteSpace(clipKey) || !Directory.Exists(root))
        {
            return null;
        }

        return Directory.EnumerateFiles(root)
            .Where(file => AudioExtensions.Contains(
                Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .FirstOrDefault(file => string.Equals(
                Path.GetFileNameWithoutExtension(file), clipKey, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 아무 곳의 오디오 파일을 BGM/효과음 루트로 복제해 들여온다 (W59) — 배경 가져오기와
    /// 같은 규약: 파일명이 곧 키, 같은 이름은 건너뛴다.
    /// </summary>
    public int ImportAudio(bool bgm, IEnumerable<string> files)
    {
        string kind = bgm ? "BGM" : "효과음";

        if ((bgm ? BgmRoot : SfxRoot) is not { } root)
        {
            SetStatus($"{kind} 폴더가 없습니다 — 프로젝트를 저장하면 assets 아래 준비됩니다.");
            return 0;
        }

        Directory.CreateDirectory(root);
        int copied = 0;
        var skipped = new List<string>();

        foreach (string file in files.Where(item => AudioExtensions.Contains(
            Path.GetExtension(item), StringComparer.OrdinalIgnoreCase)))
        {
            string destination = Path.Combine(root, Path.GetFileName(file));

            if (File.Exists(destination))
            {
                skipped.Add(Path.GetFileName(file));
                continue;
            }

            File.Copy(file, destination);
            copied++;
        }

        RefreshAssets();
        SetStatus($"{kind} {copied}개를 가져왔습니다." +
            (skipped.Count > 0 ? $" 같은 이름이 있어 건너뜀: {string.Join(", ", skipped)}" : string.Empty));
        return copied;
    }

    private string? ResolvePath(string? configured)
    {
        string basePath = ProjectPath
            ?? Path.Combine(Environment.CurrentDirectory, "unsaved" + ProjectManifestJson.FileExtension);

        return AssetRootSettings.ResolveFrom(basePath, configured);
    }

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

    /// <summary>
    /// 아무 곳의 배경 PNG를 배경 루트로 복제해 들여온다 (W48) — "폴더에 채워 넣는" 느낌의
    /// 짧은 길. 파일명이 곧 키이므로 이름 그대로 복사하고, 같은 이름이 있으면 건너뛴다
    /// (덮어쓰면 기존 배경이 소리 없이 바뀐다).
    /// </summary>
    public int ImportBackgrounds(IEnumerable<string> files)
    {
        if (ResolveAssetRoots().Backgrounds is not { } root)
        {
            SetStatus("배경 폴더가 없습니다 — 프로젝트를 저장하면 assets/backgrounds가 준비됩니다.");
            return 0;
        }

        Directory.CreateDirectory(root);
        int copied = 0;
        var skipped = new List<string>();

        foreach (string file in files.Where(item =>
            string.Equals(Path.GetExtension(item), ".png", StringComparison.OrdinalIgnoreCase)))
        {
            string destination = Path.Combine(root, Path.GetFileName(file));

            if (File.Exists(destination))
            {
                skipped.Add(Path.GetFileName(file));
                continue;
            }

            File.Copy(file, destination);
            copied++;
        }

        RefreshAssets();
        SetStatus($"배경 {copied}개를 가져왔습니다." +
            (skipped.Count > 0 ? $" 같은 이름이 있어 건너뜀: {string.Join(", ", skipped)}" : string.Empty));
        return copied;
    }

    /// <summary>
    /// 아무 곳의 PNG를 초상화 규약 자리({캐릭터}/{변형}/{표정 2자리}.png)로 복제해 들여온다
    /// (W48). 표정 번호는 그 폴더의 빈 번호부터 차례로 붙는다 — 이름 규약을 몰라도
    /// 채워 넣는 순서대로 01, 02…가 된다.
    /// </summary>
    public int ImportPortraits(string characterKey, char variant, IEnumerable<string> files)
    {
        if (ResolveAssetRoots().Portraits is not { } root)
        {
            SetStatus("초상화 폴더가 없습니다 — 프로젝트를 저장하면 assets/portraits가 준비됩니다.");
            return 0;
        }

        string key = characterKey.Trim();

        if (key.Length == 0)
        {
            SetStatus("캐릭터 키를 입력해야 초상화를 들여올 수 있습니다.");
            return 0;
        }

        string folder = Path.Combine(root, key, char.ToLowerInvariant(variant).ToString());
        Directory.CreateDirectory(folder);

        // 다음 빈 표정 번호부터 — 이미 01·02가 있으면 03부터 붙는다.
        int next = Directory.EnumerateFiles(folder, "*.png")
            .Select(file => int.TryParse(Path.GetFileNameWithoutExtension(file), out int parsed) ? parsed : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        int copied = 0;

        foreach (string file in files.Where(item =>
            string.Equals(Path.GetExtension(item), ".png", StringComparison.OrdinalIgnoreCase)))
        {
            File.Copy(file, Path.Combine(folder, $"{next + copied:00}.png"));
            copied++;
        }

        RefreshAssets();
        SetStatus(copied > 0
            ? $"{key}/{char.ToLowerInvariant(variant)}에 초상화 {copied}개를 {next:00}번부터 넣었습니다."
            : "가져올 PNG가 없습니다.");
        return copied;
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

    /// <summary>
    /// 상태줄 글자가 바뀌었을 때. <b><see cref="Changed"/>와 갈라 둔 이유가 성능이다</b>
    /// (2026-08-18): 예전에는 <see cref="SetStatus"/>가 프로젝트 변경으로 방송돼서,
    /// "에피소드 3개를 반영했습니다" 한 줄이 챕터 워크북 전체 재읽기 + 판 재구성을
    /// 불렀다. 동기화가 상태를 여러 번 적으면 그만큼 되풀이됐고, 그 되풀이가 다시
    /// 상태를 적어 스스로를 먹였다 — 노드 60개에서 첫 화면이 58초 걸리던 정체다.
    ///
    /// 상태줄은 프로젝트가 아니다. 듣는 쪽은 글자만 갈아 끼운다.
    /// </summary>
    public event EventHandler? StatusChanged;

    public event EventHandler? SelectionChanged;

    /// <summary>
    /// <see cref="Definition"/>이 갈렸을 때 — 기획자의 어휘가 바뀌었다는 알림이다.
    ///
    /// ⚠ <b>프로젝트 변경(<see cref="Changed"/>)과 갈라 둔다.</b> 정의 파일은 프로젝트
    /// 바깥에 살고, 저것으로 방송하면 워크북 재읽기·판 재구성이 딸려 온다(2026-08-18
    /// 성능 항목의 그 고리다). 듣는 쪽은 <b>제가 그리는 목록만</b> 다시 세운다.
    /// </summary>
    public event EventHandler? DefinitionChanged;

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

        ForgetWorkbookMemos();
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

    /// <summary>
    /// 워크북에 대한 기억을 비운다 (2026-08-24 성능) — 파싱 결과와 이행 판정.
    ///
    /// 둘 다 <b>내용 해시가 열쇠라 틀릴 일은 없지만</b>, 프로젝트를 바꾸면 그 기억은
    /// 영영 쓸모가 없다. 들고 있을 이유가 없는 것을 들고 있지 않는다 — 그래야 기억의
    /// 크기가 <b>지금 연 프로젝트 하나</b>로 묶인다.
    /// </summary>
    private static void ForgetWorkbookMemos()
    {
        WorkbookParseCache.Clear();
        WorkbookMigrationGate.Clear();
    }

    public void Open(string path)
    {
        ProjectLoadResult loaded = ProjectStore.Load(path);
        StoryProject project = loaded.Project;

        ForgetWorkbookMemos();
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
        bool firstSave = ProjectPath is null;
        string? previousManifestPath = ProjectPath;

        // 새 프로젝트의 첫 저장 (W48): 에셋 폴더 규약을 프로젝트에 미리 심는다 —
        // 루트가 저장본에 들어가야 하므로 디스크에 쓰기 전에 지정한다. 오디오도 같은 구조 (W59).
        if (firstSave && Project.AssetRoots.IsEmpty)
        {
            Editor.SetAssetRoots("assets/backgrounds", "assets/portraits", "assets/bgm", "assets/sfx");
        }

        ProjectStore.Save(target, Project);

        ProjectPath = Path.GetFullPath(target);
        // 규약 데이터 이사는 정의 읽기보다 먼저다 — 이사해 온 game.definition.json을
        // 바로 아래 LoadBeside가 읽어야 하고, EnsureProjectFolders가 빈 뼈대를 먼저
        // 깔면 "있는 파일은 불가침"에 걸려 진짜 정의가 영영 못 들어온다.
        int carried = CarryProjectData(previousManifestPath, ProjectPath);
        _savedSnapshot = ProjectSnapshotCodec.Encode(Project);
        Definition = GameDefinition.LoadBeside(ProjectPath);
        AppSettingsService.SaveRecentProject(ProjectPath);

        bool provisioned = EnsureProjectFolders();

        StatusMessage = $"{Path.GetFileName(ProjectPath)}에 저장했습니다." +
            (carried > 0 ? $" 이전 폴더의 챕터·대본·정의·에셋 {carried}개 파일을 함께 복사했습니다." : string.Empty) +
            (provisioned ? " 없던 에셋 폴더·기본 튜닝을 준비했습니다." : string.Empty);
        Changed?.Invoke(this, new ProjectChangedEventArgs(ProjectChangeKind.Content));
    }

    /// <summary>
    /// 다른 폴더로 저장 = 프로젝트 이사 (2026-08-21 소유자 보고). 매니페스트만 옮기면
    /// 프로젝트가 찢어진다: 판(StoryFile)·대본·작가 화자는 매니페스트에 실려 따라오는데,
    /// 챕터 워크북·에피소드 대본·정의 파일·에셋은 <b>폴더 규약</b>이라 옛 집에 남는다.
    /// 그 반쪽 상태가 "새 프로젝트인데 연출 그래프에 이전 프로젝트의 챕터가 보인다"의
    /// 정체였다 — 챕터 목록은 새(빈) chapters/를 읽어 비어 있는데, 옛 챕터의 판은 설정
    /// 노드(조건·아이템·화자)까지 달고 그대로 서 있었다.
    ///
    /// 복사이지 이동이 아니다(옛 매니페스트는 계속 그 폴더에서 산다). 새 자리에 이미 있는
    /// 파일은 불가침이고, 엑셀 잠금 임시 파일(~$)은 데이터가 아니라 거른다. `exported/`는
    /// 산출물이라 안 옮긴다 — 다음 재읽기의 자동 내보내기가 새로 만든다.
    /// </summary>
    /// <returns>실제로 복사한 파일 수. 같은 폴더 재저장·첫 저장은 0이다.</returns>
    private int CarryProjectData(string? previousManifestPath, string targetManifestPath)
    {
        if (previousManifestPath is null)
        {
            return 0;
        }

        string? fromRoot = Path.GetDirectoryName(Path.GetFullPath(previousManifestPath));
        string? toRoot = Path.GetDirectoryName(targetManifestPath);

        if (fromRoot is null || toRoot is null ||
            string.Equals(fromRoot, toRoot, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        int copied = 0;

        copied += CopyMissing(
            ChapterLibrary.FolderFor(previousManifestPath), ChapterLibrary.FolderFor(targetManifestPath));
        copied += CopyMissing(
            EpisodeLibrary.FolderFor(previousManifestPath), EpisodeLibrary.FolderFor(targetManifestPath));
        copied += CopyMissingFile(
            GameDefinition.PathFor(previousManifestPath), GameDefinition.PathFor(targetManifestPath));

        foreach (string? root in new[]
        {
            Project.AssetRoots.BackgroundsPath,
            Project.AssetRoots.PortraitsPath,
            Project.AssetRoots.BgmPath,
            Project.AssetRoots.SfxPath,
            string.IsNullOrWhiteSpace(Definition.RuntimeTuningPath)
                ? RuntimeTuningLibrary.DefaultFolderName
                : Definition.RuntimeTuningPath
        })
        {
            // 절대 경로 루트는 이사와 무관하게 그대로 유효하다 — 옮길 것이 없다.
            if (string.IsNullOrWhiteSpace(root) || Path.IsPathRooted(root))
            {
                continue;
            }

            copied += CopyMissing(
                AssetRootSettings.ResolveFrom(previousManifestPath, root),
                AssetRootSettings.ResolveFrom(targetManifestPath, root));
        }

        return copied;
    }

    private static int CopyMissing(string? sourceFolder, string? destinationFolder)
    {
        if (sourceFolder is null || destinationFolder is null || !Directory.Exists(sourceFolder))
        {
            return 0;
        }

        int copied = 0;

        foreach (string file in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file).StartsWith("~$", StringComparison.Ordinal))
            {
                continue;
            }

            copied += CopyMissingFile(
                file, Path.Combine(destinationFolder, Path.GetRelativePath(sourceFolder, file)));
        }

        return copied;
    }

    private static int CopyMissingFile(string source, string destination)
    {
        if (!File.Exists(source) || File.Exists(destination))
        {
            return 0;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination);
        return 1;
    }

    /// <summary>
    /// 프로젝트 폴더의 기본 살림 (W48·W60) — <b>매 저장마다</b> 없는 에셋 폴더를 만들고,
    /// 튜닝 폴더가 통째로 없으면 기본 튜닝을 깐다. 첫 저장뿐 아니라 다른 이름으로 저장한
    /// 새 위치, 실수로 폴더를 지운 경우까지 복구한다. 내용이 있는 폴더는 불가침이다.
    /// 무언가 실제로 만들었을 때만 true — 조용한 저장이 시끄러워지지 않는다.
    /// </summary>
    private bool EnsureProjectFolders()
    {
        if (ProjectPath is null)
        {
            return false;
        }

        bool created = false;

        foreach (string? root in new[] { BackgroundsRoot, PortraitsRoot, BgmRoot, SfxRoot })
        {
            if (root is not null && !Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
                created = true;
            }
        }

        // 정의 파일 — 없으면 뼈대를 깐다. 손으로 만들어야 하는 파일이면 새 프로젝트마다
        // 스탯 경고부터 맞는다(실사례). 있는 파일은 불가침.
        if (GameDefinitionStore.EnsureBeside(ProjectPath))
        {
            created = true;
        }

        // 기본 튜닝 — 재지정(runtimeTuningPath)이 없고 규약 폴더가 비었을 때만.
        if (string.IsNullOrWhiteSpace(Definition.RuntimeTuningPath))
        {
            string tuningRoot = Path.Combine(
                Path.GetDirectoryName(ProjectPath)!, RuntimeTuningLibrary.DefaultFolderName);

            if (!Directory.Exists(tuningRoot) || !Directory.EnumerateFileSystemEntries(tuningRoot).Any())
            {
                CreateDefaultTuning(); // 상태 메시지는 저장 요약이 덮는다
                created = true;
            }
        }

        if (created)
        {
            RefreshAssets();
        }

        return created;
    }

    public void SetStatus(string message)
    {
        StatusMessage = message;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 엑셀 동기화가 대본을 바꿨다 — 열려 있는 편집 화면(줄 목록·그래프)을 다시 만든다.
    ///
    /// 툴 안 타이핑은 편집 컨트롤을 보존해야 하지만(포커스가 날아간다), 바깥에서 온 변경은
    /// 반대다: 보존하면 화면이 파일과 다른 거짓말을 한다 — "시트에서 고쳤는데 툴에
    /// 반영이 안 된다"(실사례). 그래서 구조 변경으로 격상해 알린다.
    /// </summary>
    public void NotifyExternalScriptChange() =>
        Changed?.Invoke(this, new ProjectChangedEventArgs(ProjectChangeKind.Structure));

    // 작가 화면에서 `game.definition.json`의 speakers를 갈아 끼우던 길은 폐지됐다
    // (2026-08-17 소유자 — "이게 기획자가 쓰는 전용으로 되어야 하는데, 현재는 시나리오
    // 작가가 한것까지 저기에 들어간다"). 작가가 더한 화자는 프로젝트에 산다
    // (`ProjectEditor.SetWriterSpeakers`). 정의 파일에 쓰는 창구는 아래 [등록] 하나뿐이고,
    // 그것도 기획자 화면(챕터 그래프의 검증 보고)에서만 불린다.

    /// <summary>
    /// 화자 목록을 <c>game.definition.json</c>에 통째로 쓴다 — 챕터 그래프 [화자] 탭 전용
    /// (2026-08-23 소유자: "이 화자탭에서 화자 추가 삭제를 하는 것이 구조적으로 옳다.
    /// 여기서 추가한 화자는 게임 project에 저장되며, 모든 챕터가 공유해서 쓴다").
    ///
    /// <b>통째로 쓰는 것이 규격이다</b> — 더하기만 하는 <see cref="RegisterVariables"/>와
    /// 다르다. 탭에서 지운 이름이 파일에 남으면 목록이 화면과 갈리고, 그 순간부터 어느 쪽이
    /// 진짜인지 아무도 모른다. 쓰고 나면 정의를 다시 읽어 드롭다운이 같은 목록을 본다.
    ///
    /// 초상화 매핑도 같은 배열이 진다(<c>characterId</c>) — 화자의 집이 하나라는 뜻이다.
    /// </summary>
    public bool SaveSpeakers(IReadOnlyList<SpeakerSpec> speakers)
    {
        ArgumentNullException.ThrowIfNull(speakers);

        if (ProjectPath is null)
        {
            SetStatus("화자는 game.definition.json에 저장됩니다. 프로젝트를 먼저 저장해 주세요.");
            return false;
        }

        GameDefinitionStore.SaveSpeakers(ProjectPath, speakers);
        Definition = GameDefinition.LoadBeside(ProjectPath);
        return true;
    }

    /// <summary>
    /// 스탯 키를 <c>game.definition.json</c>의 variables에 더한다 — 검증 보고의 [등록] 단추 전용.
    /// 존재의 원천은 정의 파일이므로(§3.1) 자동 쓰기가 아니라 사람이 누른 순간에만 쓰고,
    /// 없는 이름만 더한다. 쓰고 나면 정의를 다시 읽어 챕터 검증이 새 원천을 본다.
    /// </summary>
    public bool RegisterVariables(IReadOnlyList<VariableSpec> variables)
    {
        if (ProjectPath is null)
        {
            SetStatus("스탯은 game.definition.json에 등록됩니다. 프로젝트를 먼저 저장해 주세요.");
            return false;
        }

        int added = GameDefinitionStore.AddVariables(ProjectPath, variables);
        Definition = GameDefinition.LoadBeside(ProjectPath);

        SetStatus(added > 0
            ? $"{GameDefinition.FileName}에 변수 {added}개를 등록했습니다."
            : "새로 등록할 변수가 없습니다 — 이미 정의 파일에 있습니다.");

        return added > 0;
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

    /// <summary>
    /// 챕터의 판 (챕터 v2, G-1 v2 — 챕터 = 판 1:1). 이름이 챕터 Id와 같은 StoryFile이 그
    /// 챕터의 판이고, 없으면 만든다. 왼쪽 챕터 목록 클릭과 에피소드 동기화가 같은 이 규칙
    /// 하나를 쓴다 — 두 곳이 갈리면 노드가 엉뚱한 판에 생긴다.
    /// </summary>
    /// <summary>
    /// 그 챕터의 판을 보장한다. <b>규칙은 <see cref="ProjectEditor.EnsureChapterBoard"/>가
    /// 갖는다</b> (2026-08-23에 내려갔다 — 세션 상태를 하나도 안 보는 순수 편집 작업이었다).
    /// 여기 남은 것은 부르는 자리 스물여덟을 안 건드리기 위한 이름 하나뿐이다.
    /// </summary>
    internal string EnsureChapterBoard(string chapterId) => Editor.EnsureChapterBoard(chapterId);

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

        // 선택과 펼침은 독립이다 — W43의 "선택=그 파일만 펼침" 자동 접힘은 실사용에서
        // "여러 파일을 오가며 함께 본다"를 막아 헷갈린다는 소유자 판단으로 철회했다 (W49).
        // 활성 파일이 접혀 있으면 펴 주기만 한다(고른 파일이 안 보이는 일은 없어야 한다).
        bool expandedChanged = fileId is not null && _expandedFileIds.Add(fileId);

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
