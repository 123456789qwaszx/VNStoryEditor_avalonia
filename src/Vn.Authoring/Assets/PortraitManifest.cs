using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vn.Authoring.Assets;

/// <summary>
/// 런타임 U12-v1이 내보내는 초상화 덤프 매니페스트(<c>portraits.manifest.json</c>).
///
/// 초상화는 배경과 달리 파일명 규약만으로 찾을 수 없다 — 런타임의 원본이
/// <c>(characterId, variantKey, emotionKey) → Sprite</c> 키 매핑이기 때문이다.
/// 그래서 이 매핑의 덤프가 매니페스트로 함께 온다. 배경은 매니페스트가 없다.
/// 파일명 = spriteKey 규약이라 낡을 매니페스트 자체가 없다.
/// </summary>
public sealed class PortraitManifest
{
    public const string FileName = "portraits.manifest.json";
    public const int CurrentFormatVersion = 1;

    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; init; }

    [JsonPropertyName("entries")]
    public List<PortraitManifestEntry> Entries { get; init; } = new();

    /// <summary>
    /// 버전이 다르면 조용히 읽는 대신 명시적으로 거부한다.
    /// 런타임 쪽 덤프 형식이 바뀌었는데 툴이 옛 규칙으로 읽으면
    /// 양쪽 다 오류 없이 어긋난다 — 그게 이 파이프라인에서 가장 위험한 실패다.
    /// </summary>
    public static PortraitManifest Parse(string json)
    {
        PortraitManifest manifest = JsonSerializer.Deserialize<PortraitManifest>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                })
            ?? throw new InvalidDataException("초상화 매니페스트가 비어 있습니다.");

        if (manifest.FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"초상화 매니페스트 형식 버전 {manifest.FormatVersion}은 지원하지 않습니다. " +
                $"현재 VnTool은 버전 {CurrentFormatVersion}만 읽습니다. " +
                "런타임 덤프(U12-v1)를 다시 내보내거나 VnTool을 갱신하세요.");
        }

        return manifest;
    }
}

/// <summary>
/// 덤프 항목 하나. <see cref="File"/>은 매니페스트 파일 기준 상대 경로(PNG)다.
/// </summary>
public sealed class PortraitManifestEntry
{
    [JsonPropertyName("characterId")]
    public string CharacterId { get; init; } = string.Empty;

    [JsonPropertyName("variantKey")]
    public string VariantKey { get; init; } = string.Empty;

    [JsonPropertyName("emotionKey")]
    public string EmotionKey { get; init; } = string.Empty;

    [JsonPropertyName("file")]
    public string File { get; init; } = string.Empty;
}
