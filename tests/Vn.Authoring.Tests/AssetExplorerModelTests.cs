using Vn.Authoring.Assets;
using Vn.Authoring.Definition;

namespace Vn.Authoring.Tests;

/// <summary>
/// W17 에셋 탐색기 뷰모델. 배경은 폴더 구조 그대로, 초상화는 캐릭터→variant→emotion
/// 키 구조로 묶이고, 세 종류의 문제(파일 없음·고아·초상화 없는 화자)가 항목으로 보인다.
/// </summary>
public class AssetExplorerModelTests
{
    private static readonly string FixtureDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "AssetFixtures"));

    private static readonly GameDefinition Definition = GameDefinition.Parse("""
        {
          "speakers": [
            { "name": "라루", "characterId": "laru" },
            { "name": "윌로", "characterId": "willo" },
            { "name": "모브", "characterId": "nobody" }
          ]
        }
        """)!;

    private static AssetExplorerTree BuildFixtureTree()
    {
        PreviewAssetLibrary library = PreviewAssetLibrary.Load(
            Path.Combine(FixtureDirectory, "backgrounds"),
            Path.Combine(FixtureDirectory, "portraits"));

        return AssetExplorerModel.Build(library, Definition);
    }

    [Fact]
    public void 배경_트리는_폴더_구조_그대로다()
    {
        AssetExplorerTree tree = BuildFixtureTree();

        // 폴더가 먼저, 파일은 상대 경로 순서.
        Assert.Equal(
            [("night", AssetExplorerItemKind.Folder),
             ("office.png", AssetExplorerItemKind.Background),
             ("street_night.png", AssetExplorerItemKind.Background)],
            tree.BackgroundItems.Select(item => (item.Label, item.Kind)));

        // 하위 폴더 파일의 spriteKey는 "폴더/파일명"이고 해석도 그 키로 된다.
        AssetExplorerItem alley = Assert.Single(tree.BackgroundItems[0].Children);
        Assert.Equal("night/alley", alley.BackgroundKey);

        PreviewAssetLibrary library = PreviewAssetLibrary.Load(
            Path.Combine(FixtureDirectory, "backgrounds"), null);
        Assert.Equal(AssetResolutionKind.Exact, library.ResolveBackground("night/alley").Kind);
        Assert.Equal(AssetResolutionKind.Exact, library.ResolveBackground("office").Kind);
    }

    [Fact]
    public void 초상화는_캐릭터_variant_emotion_키_구조로_묶인다()
    {
        AssetExplorerTree tree = BuildFixtureTree();

        AssetExplorerItem laru = tree.PortraitItems.Single(item =>
            item.Kind == AssetExplorerItemKind.Character && item.Label.StartsWith("laru", StringComparison.Ordinal));

        // 화자 매핑이 있으면 표시명이 병기된다.
        Assert.Equal("laru — 라루", laru.Label);

        Assert.Equal(["a", "b"], laru.Children.Select(variant => variant.Label));
        Assert.Equal(["01", "02"], laru.Children[0].Children.Select(emotion => emotion.Label));

        AssetExplorerItem emotion01 = laru.Children[0].Children[0];
        Assert.Equal(AssetExplorerItemKind.Portrait, emotion01.Kind);
        Assert.Equal(new PortraitKey("laru", "a", "01"), emotion01.Portrait);
        Assert.NotNull(emotion01.FilePath); // 썸네일·드래그 소스
    }

    [Fact]
    public void 문제_세_종류가_전부_항목으로_보인다()
    {
        AssetExplorerTree tree = BuildFixtureTree();

        // 1. 매니페스트에 있는데 파일 없음 — ghost 캐릭터의 잎에 문구가 붙는다.
        AssetExplorerItem ghost = tree.PortraitItems.Single(item =>
            item.Kind == AssetExplorerItemKind.Character && item.Label.StartsWith("ghost", StringComparison.Ordinal));
        AssetExplorerItem ghostLeaf = ghost.Children.Single().Children.Single();
        Assert.Contains("파일 없음", ghostLeaf.Problem, StringComparison.Ordinal);
        Assert.True(ghost.HasProblem); // 접힌 상태에서도 위로 전파된다

        // 2. 파일 있는데 매니페스트에 없음(고아).
        AssetExplorerItem orphan = tree.PortraitItems.Single(item => item.Kind == AssetExplorerItemKind.Orphan);
        Assert.Equal("willo/stray.png", orphan.Label);

        // 3. speakers 매핑에 있는데 초상화 없는 캐릭터.
        AssetExplorerItem missingSpeaker = tree.PortraitItems.Single(item =>
            item.Kind == AssetExplorerItemKind.Problem);
        Assert.StartsWith("nobody", missingSpeaker.Label, StringComparison.Ordinal);
        Assert.Contains("초상화가 없습니다", missingSpeaker.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void 루트_미설정이면_빈_트리이고_문제도_없다()
    {
        AssetExplorerTree tree = AssetExplorerModel.Build(PreviewAssetLibrary.Empty, Definition);

        Assert.False(tree.BackgroundsConfigured);
        Assert.False(tree.PortraitsConfigured);
        Assert.Empty(tree.BackgroundItems);
        Assert.Empty(tree.PortraitItems); // 미설정 상태에서 "초상화 없는 화자"를 문제로 만들지 않는다
        Assert.Empty(tree.Problems);
    }
}
