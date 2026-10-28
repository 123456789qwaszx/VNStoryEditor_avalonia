namespace Vn.Authoring.Assets;

/// <summary>
/// 초상화 하나를 가리키는 정규화된 키.
///
/// 정규화 규칙은 런타임 PortraitResolver의 것을 그대로 이식했다
/// (<c>runtime-knowledge-base.md</c> §6). 규칙이 어긋나면 런타임은 찾는 초상화를
/// 툴이 못 찾거나 그 반대가 되고, 둘 다 오류 없이 어긋난다.
/// <list type="bullet">
/// <item>variantKey를 비우면 <c>"a"</c>다.</item>
/// <item>emotionKey는 두 자리로 정규화한다 — <c>"2"</c>는 <c>"02"</c>다.</item>
/// </list>
/// </summary>
public readonly record struct PortraitKey(string CharacterId, string VariantKey, string EmotionKey)
{
    public const string DefaultVariantKey = "a";
    public const string DefaultEmotionKey = "01";

    public static PortraitKey Normalize(string characterId, string? variantKey, string? emotionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);

        return new PortraitKey(
            characterId.Trim(),
            string.IsNullOrWhiteSpace(variantKey) ? DefaultVariantKey : variantKey.Trim(),
            NormalizeEmotion(emotionKey));
    }

    /// <summary>키가 실패했을 때 시도하는 폴백 — 같은 캐릭터의 기본 초상화.</summary>
    public PortraitKey Fallback => new(CharacterId, DefaultVariantKey, DefaultEmotionKey);

    /// <summary>
    /// 경로 조립의 유일한 자리 (W-asset-02 §3.3 · 원칙 §2.4 규약 사본 금지).
    /// 키 표현과 폴더 규약은 원래 같은 모양이다 — <c>bandi/a/01</c>.
    /// </summary>
    public override string ToString() => $"{CharacterId}/{VariantKey}/{EmotionKey}";

    /// <summary>폴더 규약 상대 경로 — 스캔·안내문·덤프 출력이 전부 이 함수를 지난다.</summary>
    public string ToRelativePath(string extension = ".png") => $"{ToString()}{extension}";

    /// <summary>
    /// 규약 상대 경로를 키로 해석한다: 정확히 세 구획 + .png.
    /// 해석기(<c>ResolvePortrait</c>)와 같은 정규화를 지나므로 <c>a/7.png</c>와
    /// 요청 <c>"7"</c>이 같은 키가 된다.
    /// </summary>
    public static bool TryParseRelativePath(string? relativePath, out PortraitKey key)
    {
        key = default;

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        string[] segments = relativePath.Replace('\\', '/').Split('/');

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

        key = Normalize(segments[0], segments[1], emotion);
        return true;
    }

    private static string NormalizeEmotion(string? emotionKey)
    {
        string trimmed = emotionKey?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return DefaultEmotionKey;
        }

        // "2" → "02". 이미 두 자리 이상이거나 숫자가 아니면 그대로 둔다.
        return trimmed.Length == 1 && char.IsAsciiDigit(trimmed[0]) ? "0" + trimmed : trimmed;
    }
}
