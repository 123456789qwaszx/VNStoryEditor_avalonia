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
/// 초상화 항목 하나. <see cref="SourceFile"/>은 루트 기준 상대 경로 —
/// 규약 경로 항목이면 규약 경로 자신, 매니페스트 항목이면 매니페스트의 file 값이다.
/// 파일이 없어도 항목은 남는다 — 탐색기가 그 자리에 "파일 없음"을 표시해야 한다.
/// </summary>
public sealed record PortraitAssetEntry(PortraitKey Key, string SourceFile, string? FilePath)
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

    /// <summary>
    /// 초상화 해석 순서 (W-asset-02 §3.1) — <b>연결의 권위는 폴더 규약이다.</b>
    ///
    /// 1순위: 규약 경로 스캔 <c>{root}/{characterId}/{variantKey}/{emotionKey}.png</c> —
    ///        파일을 규약 이름으로 넣는 것만으로 등록된다(툴·JSON 불필요).
    /// 2순위: 매니페스트 — 규약 경로에 없는 키만 채운다. 런타임 구버전 덤프·자유 경로의
    ///        <b>수입 통로</b>로 남는 보조이지 권위가 아니다. 없어도 문제가 아니다.
    /// 해석 실패 시 폴백((characterId,"a","01"))→Missing은 ResolvePortrait 그대로.
    /// </summary>
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

        if (!Directory.Exists(directory))
        {
            problems.Add($"초상화 폴더가 없습니다: {directory}");
            return (index, entries, orphans);
        }

        string root = Path.GetFullPath(directory);
        string[] allFiles = Directory
            .EnumerateFiles(root, "*.png", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // 1순위 — 규약 경로. 등록에 다른 어떤 것도 필요 없다.
        var registeredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string file in allFiles)
        {
            string relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');

            if (!TryParseConventionPath(relativePath, out PortraitKey key))
            {
                continue;
            }

            registeredFiles.Add(file);

            if (index.TryAdd(key, file))
            {
                entries.Add(new PortraitAssetEntry(key, relativePath, file));
            }
            else
            {
                // 예: a/7.png와 a/07.png — 정규화 후 같은 키. 조용히 한쪽을 고르지 않는다.
                problems.Add($"규약 경로의 키 '{key}'가 중복됩니다: {relativePath}");
            }
        }

        // 2순위 — 매니페스트(있으면). 규약이 이미 답한 키는 건드리지 않는다.
        string manifestPath = Path.Combine(root, PortraitManifest.FileName);

        if (File.Exists(manifestPath))
        {
            PortraitManifest manifest;

            try
            {
                manifest = PortraitManifest.Parse(File.ReadAllText(manifestPath));
            }
            catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException)
            {
                problems.Add($"초상화 매니페스트를 읽지 못했습니다: {exception.Message}");
                // 참조 집합을 모르면 고아 판정이 과잉 신고가 되므로 여기서 멈춘다.
                return (index, entries, orphans);
            }

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
                registeredFiles.Add(fullPath);

                if (index.ContainsKey(key))
                {
                    continue; // 규약 경로가 이미 답했다 — 규약이 권위다.
                }

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
        }

        // 고아 = 규약 경로로도 해석되지 않고 매니페스트에도 없는 파일 (W-asset-02 §3.2).
        // 규약 위치의 파일은 고아가 아니라 정상 등록이다.
        foreach (string file in allFiles)
        {
            if (!registeredFiles.Contains(file))
            {
                string orphan = Path.GetRelativePath(root, file).Replace('\\', '/');
                orphans.Add(orphan);
                problems.Add(
                    $"어디에도 속하지 않는 초상화 파일(고아): {orphan} — " +
                    "규약 경로(캐릭터/변형/표정.png)도 아니고 매니페스트에도 없습니다.");
            }
        }

        return (index, entries, orphans);
    }

    /// <summary>규약 경로 판정 — {characterId}/{variantKey}/{emotionKey}.png (§3.3에서 PortraitKey로 이동 예정).</summary>
    private static bool TryParseConventionPath(string relativePath, out PortraitKey key)
    {
        key = default;
        string[] segments = relativePath.Split('/');

        if (segments.Length != 3 ||
            !segments[2].EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string emotion = segments[2][..^".png".Length];

        if (string.IsNullOrWhiteSpace(segments[0]) ||
            string.IsNullOrWhiteSpace(segments[1]) ||
            string.IsNullOrWhiteSpace(emotion))
        {
            return false;
        }

        // 해석(ResolvePortrait)과 같은 정규화를 지나야 "7.png"와 요청 "7"이 같은 키가 된다.
        key = PortraitKey.Normalize(segments[0], segments[1], emotion);
        return true;
    }
}
