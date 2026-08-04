namespace Vn.Authoring.Assets;

/// <summary>
/// 외부 이미지 하나를 초상화 폴더 규약 경로로 <b>복제</b>해 등록한다.
///
/// 저작 중 "이 표정이 필요하다"가 생기면, 파일을 손으로 옮겨 이름을 맞추는 대신
/// 여기서 원하는 이미지를 골라 <c>{root}/{char}/{variant}/{emotion}.png</c>로 복제한다 —
/// 연결의 권위는 폴더 규약이므로(W-asset-02) 복제된 순간 등록도 끝난 것이다.
/// 초상화 폴더는 참고용이 아니라 <b>복제본을 모으는 자리</b>다. 이미 있으면 그냥 쓴다.
///
/// 원본은 건드리지 않고, 기존 파일은 조용히 덮어쓰지 않으며, PNG가 아니면
/// 변환하는 척하지 않고 거부한다(원칙 §2.3 추측 보정 금지 — 규약 확장자는 .png다).
/// 경로 조립은 <see cref="PortraitKey.ToRelativePath"/> 한 곳이다(규약 사본 금지).
/// </summary>
public static class PortraitSpriteImporter
{
    public sealed record Imported(PortraitKey Key, string TargetPath);

    /// <summary>이 키의 복제본이 놓일 절대 경로. 존재 판정과 복제가 같은 계산을 쓴다.</summary>
    public static string TargetPathFor(string portraitsRoot, PortraitKey key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portraitsRoot);
        return Path.Combine(
            portraitsRoot,
            key.ToRelativePath().Replace('/', Path.DirectorySeparatorChar));
    }

    public static Imported Import(
        string portraitsRoot,
        string sourcePath,
        string characterId,
        string? variantKey,
        string? emotionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portraitsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        if (!File.Exists(sourcePath))
        {
            throw new InvalidOperationException($"원본 이미지가 없습니다: {sourcePath}");
        }

        if (!sourcePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "초상화 규약 확장자는 .png입니다. 다른 형식은 변환하지 않고 거부합니다 — PNG로 저장한 뒤 다시 고르세요.");
        }

        PortraitKey key = PortraitKey.Normalize(characterId, variantKey, emotionKey);
        string targetPath = TargetPathFor(portraitsRoot, key);

        if (File.Exists(targetPath))
        {
            throw new InvalidOperationException(
                $"'{key}'는 이미 있습니다({targetPath}). 기존 초상화는 덮어쓰지 않습니다 — 바꾸려면 파일을 직접 정리하세요.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(sourcePath, targetPath, overwrite: false);
        return new Imported(key, targetPath);
    }

    /// <summary>
    /// 이 캐릭터·variant에서 비어 있는 다음 표정 번호(두 자리). 숫자가 아닌 표정 키는
    /// 건너뛰고 숫자 최댓값 + 1이다 — 하나도 없으면 "01".
    /// </summary>
    public static string NextFreeEmotionKey(
        IEnumerable<PortraitKey> existing,
        string characterId,
        string? variantKey)
    {
        ArgumentNullException.ThrowIfNull(existing);
        PortraitKey reference = PortraitKey.Normalize(characterId, variantKey, emotionKey: null);

        int max = 0;

        foreach (PortraitKey key in existing)
        {
            if (string.Equals(key.CharacterId, reference.CharacterId, StringComparison.Ordinal) &&
                string.Equals(key.VariantKey, reference.VariantKey, StringComparison.Ordinal) &&
                int.TryParse(key.EmotionKey, out int number) &&
                number > max)
            {
                max = number;
            }
        }

        return (max + 1).ToString("D2");
    }
}
