namespace Vn.Authoring.Assets;

public enum AssetResolutionKind
{
    /// <summary>요청한 키 그대로 찾았다.</summary>
    Exact,

    /// <summary>요청한 키는 없어 기본 초상화 <c>(characterId, "a", "01")</c>로 대신 찾았다.</summary>
    Fallback,

    /// <summary>아무것도 없다. 프리뷰는 키 문자열이 보이는 플레이스홀더를 그린다.</summary>
    Missing
}

/// <summary>어느 키를 요청했고 무엇을 찾았는지가 항상 남는다. 조용히 사라지는 키는 없다.</summary>
public sealed record BackgroundResolution(
    AssetResolutionKind Kind,
    string SpriteKey,
    string? FilePath);

public sealed record PortraitResolution(
    AssetResolutionKind Kind,
    PortraitKey RequestedKey,
    PortraitKey? ResolvedKey,
    string? FilePath);

/// <summary>배경 파일 하나. <see cref="SpriteKey"/> = 루트 기준 상대 경로에서 확장자를 뗀 것.</summary>
public sealed record BackgroundAssetEntry(string SpriteKey, string RelativePath, string FilePath);

/// <summary>
/// 초상화 매니페스트 항목 하나. 파일이 없어도 항목은 남는다 —
/// 탐색기가 "매니페스트에 있는데 파일 없음"을 그 자리에 표시해야 하기 때문이다.
/// </summary>
public sealed record PortraitAssetEntry(PortraitKey Key, string ManifestFile, string? FilePath)
{
    public bool FileExists => FilePath is not null;
}

/// <summary>
/// 프리뷰가 읽는 에셋 인덱스. 파일 경로 수준까지만 다루고 비트맵은 모른다(그건 App 경계).
///
/// <b>파일 변경 감지는 하지 않는다.</b> 새로 고침은 이 객체를 새로 만드는 것 —
/// 명시적 동작 하나뿐이라 "언제 갱신됐지"를 물을 일이 없다.
///
/// 에셋 루트가 미설정이거나 깨져 있어도 예외를 던지지 않는다. 저작은 계속되고
/// 프리뷰만 플레이스홀더가 된다. 대신 무엇이 왜 안 보이는지는 <see cref="Problems"/>와
/// 항목별 상태(<see cref="PortraitAssetEntry.FileExists"/>, <see cref="OrphanPortraitFiles"/>)에
/// 전부 남는다 — 침묵하는 실패를 만들지 않는다.
/// </summary>
public sealed class PreviewAssetLibrary
{
    private readonly Dictionary<string, string> _backgrounds;
    private readonly Dictionary<PortraitKey, string> _portraits;
    private readonly List<string> _problems;

    public static PreviewAssetLibrary Empty { get; } = new(
        backgroundsConfigured: false,
        portraitsConfigured: false,
        new Dictionary<string, string>(StringComparer.Ordinal),
        Array.Empty<BackgroundAssetEntry>(),
        new Dictionary<PortraitKey, string>(),
        Array.Empty<PortraitAssetEntry>(),
        Array.Empty<string>(),
        new List<string>());

    private PreviewAssetLibrary(
        bool backgroundsConfigured,
        bool portraitsConfigured,
        Dictionary<string, string> backgrounds,
        IReadOnlyList<BackgroundAssetEntry> backgroundEntries,
        Dictionary<PortraitKey, string> portraits,
        IReadOnlyList<PortraitAssetEntry> portraitEntries,
        IReadOnlyList<string> orphanPortraitFiles,
        List<string> problems)
    {
        BackgroundsConfigured = backgroundsConfigured;
        PortraitsConfigured = portraitsConfigured;
        _backgrounds = backgrounds;
        BackgroundEntries = backgroundEntries;
        _portraits = portraits;
        PortraitEntries = portraitEntries;
        OrphanPortraitFiles = orphanPortraitFiles;
        _problems = problems;
    }

    /// <summary>배경 루트가 설정되어 있는가. false면 프리뷰가 "미설정"을 표시할 수 있다.</summary>
    public bool BackgroundsConfigured { get; }

    public bool PortraitsConfigured { get; }

    public IReadOnlyCollection<string> BackgroundKeys => _backgrounds.Keys;

    public IReadOnlyCollection<PortraitKey> PortraitKeys => _portraits.Keys;

    /// <summary>배경 파일 전부, 상대 경로 순서. 탐색기 트리와 드래그 소스가 여기서 나온다.</summary>
    public IReadOnlyList<BackgroundAssetEntry> BackgroundEntries { get; }

    /// <summary>매니페스트 항목 전부(파일 없는 항목 포함), 매니페스트 순서.</summary>
    public IReadOnlyList<PortraitAssetEntry> PortraitEntries { get; }

    /// <summary>초상화 루트에 있지만 매니페스트가 모르는 PNG(고아), 상대 경로.</summary>
    public IReadOnlyList<string> OrphanPortraitFiles { get; }

    /// <summary>로드 중 발견한 문제 전부. 프리뷰 화면이 그대로 보여 준다.</summary>
    public IReadOnlyList<string> Problems => _problems;

    public static PreviewAssetLibrary Load(string? backgroundsDirectory, string? portraitsDirectory)
    {
        var problems = new List<string>();
        (Dictionary<string, string> backgrounds, List<BackgroundAssetEntry> backgroundEntries) =
            LoadBackgrounds(backgroundsDirectory, problems);
        (Dictionary<PortraitKey, string> portraits,
            List<PortraitAssetEntry> portraitEntries,
            List<string> orphans) = LoadPortraits(portraitsDirectory, problems);

        return new PreviewAssetLibrary(
            backgroundsConfigured: backgroundsDirectory is not null,
            portraitsConfigured: portraitsDirectory is not null,
            backgrounds,
            backgroundEntries,
            portraits,
            portraitEntries,
            orphans,
            problems);
    }

    /// <summary>
    /// 배경 키는 파일 경로다 — 런타임의 <c>Resources/Backgrounds/{key}</c> 규약 그대로,
    /// 대소문자까지 정확히(Ordinal) 일치해야 한다. 유니티 Resources는 대소문자를 구분하므로
    /// 여기서 느슨하게 찾아 주면 툴에서는 보이고 게임에서는 안 보이는 배경이 생긴다.
    /// 하위 폴더 파일의 키는 <c>폴더/파일명</c>이다.
    /// </summary>
    public BackgroundResolution ResolveBackground(string spriteKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spriteKey);
        string key = spriteKey.Trim();

        return _backgrounds.TryGetValue(key, out string? path)
            ? new BackgroundResolution(AssetResolutionKind.Exact, key, path)
            : new BackgroundResolution(AssetResolutionKind.Missing, key, null);
    }

    /// <summary>
    /// 키 정규화·폴백 규칙은 런타임 PortraitResolver 이식이다(runtime-knowledge-base.md §6):
    /// 정확 일치 → <c>(characterId, "a", "01")</c> 폴백 → 플레이스홀더.
    /// </summary>
    public PortraitResolution ResolvePortrait(string characterId, string? variantKey, string? emotionKey)
    {
        PortraitKey requested = PortraitKey.Normalize(characterId, variantKey, emotionKey);

        if (_portraits.TryGetValue(requested, out string? exact))
        {
            return new PortraitResolution(AssetResolutionKind.Exact, requested, requested, exact);
        }

        PortraitKey fallback = requested.Fallback;

        if (fallback != requested && _portraits.TryGetValue(fallback, out string? fallbackPath))
        {
            return new PortraitResolution(AssetResolutionKind.Fallback, requested, fallback, fallbackPath);
        }

        return new PortraitResolution(AssetResolutionKind.Missing, requested, null, null);
    }

    private static (Dictionary<string, string> Index, List<BackgroundAssetEntry> Entries) LoadBackgrounds(
        string? directory,
        List<string> problems)
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        var entries = new List<BackgroundAssetEntry>();

        if (directory is null)
        {
            return (index, entries);
        }

        if (!Directory.Exists(directory))
        {
            problems.Add($"배경 폴더가 없습니다: {directory}");
            return (index, entries);
        }

        string root = Path.GetFullPath(directory);

        foreach (string file in Directory
                     .EnumerateFiles(root, "*.png", SearchOption.AllDirectories)
                     .Select(Path.GetFullPath)
                     .Order(StringComparer.Ordinal))
        {
            string relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');
            string key = relativePath[..^Path.GetExtension(relativePath).Length];

            entries.Add(new BackgroundAssetEntry(key, relativePath, file));

            if (!index.TryAdd(key, file))
            {
                problems.Add($"배경 키 '{key}'가 중복됩니다: {relativePath}");
            }
        }

        return (index, entries);
    }

    private static (
        Dictionary<PortraitKey, string> Index,
        List<PortraitAssetEntry> Entries,
        List<string> Orphans) LoadPortraits(string? directory, List<string> problems)
    {
        var index = new Dictionary<PortraitKey, string>();
        var entries = new List<PortraitAssetEntry>();
        var orphans = new List<string>();

        if (directory is null)
        {
            return (index, entries, orphans);
        }

        string manifestPath = Path.Combine(directory, PortraitManifest.FileName);

        if (!File.Exists(manifestPath))
        {
            problems.Add(
                Directory.Exists(directory)
                    ? $"초상화 매니페스트가 없습니다: {manifestPath}"
                    : $"초상화 폴더가 없습니다: {directory}");
            return (index, entries, orphans);
        }

        PortraitManifest manifest;

        try
        {
            manifest = PortraitManifest.Parse(File.ReadAllText(manifestPath));
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException)
        {
            problems.Add($"초상화 매니페스트를 읽지 못했습니다: {exception.Message}");
            return (index, entries, orphans);
        }

        string root = Path.GetFullPath(directory);
        HashSet<string> referencedFiles = new(StringComparer.OrdinalIgnoreCase);

        foreach (PortraitManifestEntry entry in manifest.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.CharacterId) || string.IsNullOrWhiteSpace(entry.File))
            {
                problems.Add("초상화 매니페스트에 characterId나 file이 빈 항목이 있습니다.");
                continue;
            }

            PortraitKey key = PortraitKey.Normalize(entry.CharacterId, entry.VariantKey, entry.EmotionKey);
            string manifestFile = entry.File.Replace('\\', '/');
            string fullPath = Path.GetFullPath(Path.Combine(root, manifestFile));
            referencedFiles.Add(fullPath);

            if (!File.Exists(fullPath))
            {
                // 항목은 버리지 않는다 — 탐색기가 그 자리에서 "파일 없음"을 보여야 한다.
                problems.Add($"초상화 파일이 없습니다: {key} → {entry.File}");
                entries.Add(new PortraitAssetEntry(key, manifestFile, null));
                continue;
            }

            entries.Add(new PortraitAssetEntry(key, manifestFile, fullPath));

            if (!index.TryAdd(key, fullPath))
            {
                problems.Add($"초상화 키 '{key}'가 매니페스트에서 중복됩니다.");
            }
        }

        foreach (string file in Directory
                     .EnumerateFiles(root, "*.png", SearchOption.AllDirectories)
                     .Select(Path.GetFullPath)
                     .Order(StringComparer.Ordinal))
        {
            if (!referencedFiles.Contains(file))
            {
                string orphan = Path.GetRelativePath(root, file).Replace('\\', '/');
                orphans.Add(orphan);
                problems.Add($"매니페스트에 없는 초상화 파일(고아): {orphan}");
            }
        }

        return (index, entries, orphans);
    }
}
