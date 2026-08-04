using Vn.Authoring.Definition;

namespace Vn.Authoring.Assets;

public enum AssetExplorerItemKind
{
    /// <summary>배경 트리의 폴더. 실제 폴더 구조 그대로다.</summary>
    Folder,

    /// <summary>배경 파일 하나. 파일명(하위 폴더면 경로) = spriteKey.</summary>
    Background,

    /// <summary>초상화의 캐릭터 그룹 — 파일 시스템이 아니라 키 구조다(연출가의 머릿속 단위).</summary>
    Character,

    /// <summary>캐릭터 아래 variant 그룹.</summary>
    Variant,

    /// <summary>초상화 하나(emotion 잎). 파일이 없어도 항목은 남는다.</summary>
    Portrait,

    /// <summary>폴더에 있는데 매니페스트가 모르는 파일.</summary>
    Orphan,

    /// <summary>항목이 아니라 문제 자체(예: 초상화 없는 화자).</summary>
    Problem
}

/// <summary>
/// 탐색기 트리 항목 하나. <see cref="Problem"/>이 있으면 화면이 아이콘+문구로 표시한다 —
/// 침묵을 화면으로 바꾸는 자리다(원칙 문서 §2.5).
/// <see cref="BackgroundKey"/>/<see cref="Portrait"/>는 드래그 페이로드다(W20).
/// </summary>
public sealed record AssetExplorerItem(
    AssetExplorerItemKind Kind,
    string Label,
    string? FilePath,
    string? Problem,
    IReadOnlyList<AssetExplorerItem> Children)
{
    public string? BackgroundKey { get; init; }

    public PortraitKey? Portrait { get; init; }

    public bool HasProblem => Problem is not null || Children.Any(child => child.HasProblem);
}

public sealed record AssetExplorerTree(
    bool BackgroundsConfigured,
    bool PortraitsConfigured,
    IReadOnlyList<AssetExplorerItem> BackgroundItems,
    IReadOnlyList<AssetExplorerItem> PortraitItems,
    IReadOnlyList<string> Problems);

/// <summary>
/// 좌측 에셋 탐색기가 그릴 트리를 계산한다. 화면 상태가 아니라 순수 계산이다.
///
/// 배경은 폴더 규약 그대로(폴더 트리 + 파일), 초상화는 매니페스트 기준
/// <b>캐릭터 → variant → emotion</b> 키 구조로 묶는다. 문제 세 종류 —
/// 매니페스트에 있는데 파일 없음, 파일 있는데 매니페스트에 없음(고아),
/// speakers 매핑에 있는데 초상화가 하나도 없는 캐릭터 — 는 전부 항목으로 보인다.
/// </summary>
public static class AssetExplorerModel
{
    public static AssetExplorerTree Build(PreviewAssetLibrary library, GameDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(definition);

        return new AssetExplorerTree(
            library.BackgroundsConfigured,
            library.PortraitsConfigured,
            BuildBackgroundItems(library),
            BuildPortraitItems(library, definition),
            library.Problems);
    }

    private static IReadOnlyList<AssetExplorerItem> BuildBackgroundItems(PreviewAssetLibrary library)
    {
        return BuildBackgroundLevel(library.BackgroundEntries, depth: 0);
    }

    /// <summary>상대 경로의 세그먼트를 따라 폴더 트리를 만든다. 파일은 폴더 뒤에 온다.</summary>
    private static IReadOnlyList<AssetExplorerItem> BuildBackgroundLevel(
        IReadOnlyList<BackgroundAssetEntry> entries,
        int depth)
    {
        var items = new List<AssetExplorerItem>();

        foreach (IGrouping<string, BackgroundAssetEntry> group in entries
                     .Where(entry => Segments(entry).Length > depth + 1)
                     .GroupBy(entry => Segments(entry)[depth], StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            items.Add(new AssetExplorerItem(
                AssetExplorerItemKind.Folder,
                group.Key,
                FilePath: null,
                Problem: null,
                BuildBackgroundLevel(group.ToArray(), depth + 1)));
        }

        foreach (BackgroundAssetEntry entry in entries.Where(entry => Segments(entry).Length == depth + 1))
        {
            items.Add(new AssetExplorerItem(
                AssetExplorerItemKind.Background,
                Segments(entry)[depth],
                entry.FilePath,
                Problem: null,
                Array.Empty<AssetExplorerItem>())
            {
                BackgroundKey = entry.SpriteKey
            });
        }

        return items;

        static string[] Segments(BackgroundAssetEntry entry) => entry.RelativePath.Split('/');
    }

    private static IReadOnlyList<AssetExplorerItem> BuildPortraitItems(
        PreviewAssetLibrary library,
        GameDefinition definition)
    {
        var items = new List<AssetExplorerItem>();

        foreach (IGrouping<string, PortraitAssetEntry> character in library.PortraitEntries
                     .GroupBy(entry => entry.Key.CharacterId, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var variants = new List<AssetExplorerItem>();

            foreach (IGrouping<string, PortraitAssetEntry> variant in character
                         .GroupBy(entry => entry.Key.VariantKey, StringComparer.Ordinal)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var emotions = variant
                    .OrderBy(entry => entry.Key.EmotionKey, StringComparer.Ordinal)
                    .Select(entry => new AssetExplorerItem(
                        AssetExplorerItemKind.Portrait,
                        entry.Key.EmotionKey,
                        entry.FilePath,
                        entry.FileExists ? null : $"파일 없음: {entry.SourceFile}",
                        Array.Empty<AssetExplorerItem>())
                    {
                        Portrait = entry.Key
                    })
                    .ToArray();

                variants.Add(new AssetExplorerItem(
                    AssetExplorerItemKind.Variant,
                    variant.Key,
                    FilePath: null,
                    Problem: null,
                    emotions));
            }

            // 화자 매핑이 있으면 캐릭터 라벨에 표시명을 병기한다.
            string? speakerName = definition.Speakers
                .FirstOrDefault(speaker =>
                    string.Equals(speaker.CharacterId, character.Key, StringComparison.Ordinal))
                ?.Name;

            items.Add(new AssetExplorerItem(
                AssetExplorerItemKind.Character,
                speakerName is null ? character.Key : $"{character.Key} — {speakerName}",
                FilePath: null,
                Problem: null,
                variants));
        }

        foreach (string orphan in library.OrphanPortraitFiles)
        {
            items.Add(new AssetExplorerItem(
                AssetExplorerItemKind.Orphan,
                orphan,
                FilePath: null,
                "매니페스트에 없는 파일",
                Array.Empty<AssetExplorerItem>()));
        }

        // speakers 매핑에 있는데 초상화가 하나도 없는 캐릭터 — 프리뷰에서 화자 강조가
        // 조용히 빠지게 될 캐릭터를 미리 보여 준다.
        HashSet<string> knownCharacters = library.PortraitEntries
            .Select(entry => entry.Key.CharacterId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (SpeakerSpec speaker in definition.Speakers)
        {
            if (!string.IsNullOrWhiteSpace(speaker.CharacterId) &&
                library.PortraitsConfigured &&
                !knownCharacters.Contains(speaker.CharacterId))
            {
                items.Add(new AssetExplorerItem(
                    AssetExplorerItemKind.Problem,
                    $"{speaker.CharacterId} — {speaker.Name}",
                    FilePath: null,
                    "speakers 매핑에 있지만 초상화가 없습니다",
                    Array.Empty<AssetExplorerItem>()));
            }
        }

        return items;
    }
}
