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

    public override string ToString() => $"{CharacterId}/{VariantKey}/{EmotionKey}";

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
