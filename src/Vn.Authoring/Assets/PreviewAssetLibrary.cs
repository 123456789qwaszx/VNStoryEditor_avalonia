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

/// <summary>
/// 프리뷰가 읽는 에셋 인덱스. 파일 경로 수준까지만 다루고 비트맵은 모른다(그건 App 경계).
///
/// <b>파일 변경 감지는 하지 않는다.</b> 새로 고침은 이 객체를 새로 만드는 것 —
/// 명시적 동작 하나뿐이라 "언제 갱신됐지"를 물을 일이 없다.
///
/// 에셋 루트가 미설정이거나 깨져 있어도 예외를 던지지 않는다. 저작은 계속되고
/// 프리뷰만 플레이스홀더가 된다. 대신 무엇이 왜 안 보이는지는 <see cref="Problems"/>에
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
        new Dictionary<PortraitKey, string>(),
        new List<string>());

    private PreviewAssetLibrary(
        bool backgroundsConfigured,
        bool portraitsConfigured,
        Dictionary<string, string> backgrounds,
        Dictionary<PortraitKey, string> portraits,
        List<string> problems)
    {
        BackgroundsConfigured = backgroundsConfigured;
        PortraitsConfigured = portraitsConfigured;
        _backgrounds = backgrounds;
        _portraits = portraits;
        _problems = problems;
    }

    /// <summary>배경 루트가 설정되어 있는가. false면 프리뷰가 "미설정"을 표시할 수 있다.</summary>
    public bool BackgroundsConfigured { get; }

    public bool PortraitsConfigured { get; }

    public IReadOnlyCollection<string> BackgroundKeys => _backgrounds.Keys;

    public IReadOnlyCollection<PortraitKey> PortraitKeys => _portraits.Keys;

    /// <summary>로드 중 발견한 문제 전부. 프리뷰 화면이 그대로 보여 준다.</summary>
    public IReadOnlyList<string> Problems => _problems;

    public static PreviewAssetLibrary Load(string? backgroundsDirectory, string? portraitsDirectory)
    {
        var problems = new List<string>();
        Dictionary<string, string> backgrounds = LoadBackgrounds(backgroundsDirectory, problems);
        Dictionary<PortraitKey, string> portraits = LoadPortraits(portraitsDirectory, problems);

        return new PreviewAssetLibrary(
            backgroundsConfigured: backgroundsDirectory is not null,
            portraitsConfigured: portraitsDirectory is not null,
            backgrounds,
            portraits,
            problems);
    }

    /// <summary>
    /// 배경 키는 파일명이다 — 런타임의 <c>Resources/Backgrounds/{key}</c> 규약 그대로,
    /// 대소문자까지 정확히(Ordinal) 일치해야 한다. 유니티 Resources는 대소문자를 구분하므로
    /// 여기서 느슨하게 찾아 주면 툴에서는 보이고 게임에서는 안 보이는 배경이 생긴다.
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

    private static Dictionary<string, string> LoadBackgrounds(string? directory, List<string> problems)
    {
        var backgrounds = new Dictionary<string, string>(StringComparer.Ordinal);

        if (directory is null)
        {
            return backgrounds;
        }

        if (!Directory.Exists(directory))
        {
            problems.Add($"배경 폴더가 없습니다: {directory}");
            return backgrounds;
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*.png").Order(StringComparer.Ordinal))
        {
            string key = Path.GetFileNameWithoutExtension(file);

            if (!backgrounds.TryAdd(key, Path.GetFullPath(file)))
            {
                problems.Add($"배경 키 '{key}'가 중복됩니다: {file}");
            }
        }

        return backgrounds;
    }

    private static Dictionary<PortraitKey, string> LoadPortraits(string? directory, List<string> problems)
    {
        var portraits = new Dictionary<PortraitKey, string>();

        if (directory is null)
        {
            return portraits;
        }

        string manifestPath = Path.Combine(directory, PortraitManifest.FileName);

        if (!File.Exists(manifestPath))
        {
            problems.Add(
                Directory.Exists(directory)
                    ? $"초상화 매니페스트가 없습니다: {manifestPath}"
                    : $"초상화 폴더가 없습니다: {directory}");
            return portraits;
        }

        PortraitManifest manifest;

        try
        {
            manifest = PortraitManifest.Parse(File.ReadAllText(manifestPath));
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException)
        {
            problems.Add($"초상화 매니페스트를 읽지 못했습니다: {exception.Message}");
            return portraits;
        }

        foreach (PortraitManifestEntry entry in manifest.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.CharacterId) || string.IsNullOrWhiteSpace(entry.File))
            {
                problems.Add("초상화 매니페스트에 characterId나 file이 빈 항목이 있습니다.");
                continue;
            }

            PortraitKey key = PortraitKey.Normalize(entry.CharacterId, entry.VariantKey, entry.EmotionKey);
            string fullPath = Path.GetFullPath(Path.Combine(directory, entry.File.Replace('\\', '/')));

            if (!File.Exists(fullPath))
            {
                problems.Add($"초상화 파일이 없습니다: {key} → {entry.File}");
                continue;
            }

            if (!portraits.TryAdd(key, fullPath))
            {
                problems.Add($"초상화 키 '{key}'가 매니페스트에서 중복됩니다.");
            }
        }

        return portraits;
    }
}
