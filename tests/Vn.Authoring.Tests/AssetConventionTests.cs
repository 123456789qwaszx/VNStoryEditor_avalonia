using Vn.Authoring.Assets;

namespace Vn.Authoring.Tests;

/// <summary>
/// W-asset-02 — 연결의 권위는 폴더 규약이다. 파일을 규약 이름으로 넣는 것만으로
/// 등록되고(툴·JSON 불필요), 매니페스트는 자유 경로 수입 통로로만 남는다.
/// </summary>
public class AssetConventionTests
{
    private static readonly string FixtureDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "AssetFixtures"));

    private static PreviewAssetLibrary LoadFixtureLibrary() =>
        PreviewAssetLibrary.Load(
            Path.Combine(FixtureDirectory, "backgrounds"),
            Path.Combine(FixtureDirectory, "portraits"));

    private static string TempPortraitsRoot()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"VnTool.Convention.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public void 규약_경로에_넣은_파일은_그것만으로_등록된다()
    {
        // §5-2: portraits/bandi/a/07.png 를 손으로 넣은 상황(픽스처가 그 상태다).
        PreviewAssetLibrary library = LoadFixtureLibrary();

        PortraitResolution resolved = library.ResolvePortrait("bandi", "a", "07");
        Assert.Equal(AssetResolutionKind.Exact, resolved.Kind);
        Assert.EndsWith("07.png", resolved.FilePath, StringComparison.Ordinal);

        PortraitAssetEntry entry = library.PortraitEntries.Single(item =>
            item.Key == new PortraitKey("bandi", "a", "07"));
        Assert.Equal("bandi/a/07.png", entry.SourceFile);

        // 매니페스트는 건드리지 않았다 — bandi는 매니페스트에 없다.
        string manifest = File.ReadAllText(
            Path.Combine(FixtureDirectory, "portraits", PortraitManifest.FileName));
        Assert.DoesNotContain("bandi", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void 매니페스트로만_오는_기존_항목은_여전히_동작한다()
    {
        // §5-3 회귀 — laru는 자유 경로(laru/a_01.png)라 매니페스트가 수입 통로다.
        PreviewAssetLibrary library = LoadFixtureLibrary();

        Assert.Equal(AssetResolutionKind.Exact, library.ResolvePortrait("laru", "a", "01").Kind);
        Assert.Equal(AssetResolutionKind.Exact, library.ResolvePortrait("laru", "b", "3").Kind);
    }

    [Fact]
    public void 같은_키면_규약_경로가_매니페스트를_이긴다()
    {
        string root = TempPortraitsRoot();

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "mina", "a"));
            File.WriteAllBytes(Path.Combine(root, "mina", "a", "01.png"), [1]);
            File.WriteAllBytes(Path.Combine(root, "free_mina.png"), [2]);
            File.WriteAllText(Path.Combine(root, PortraitManifest.FileName), """
                {
                  "formatVersion": 1,
                  "entries": [
                    { "characterId": "mina", "variantKey": "a", "emotionKey": "01", "file": "free_mina.png" }
                  ]
                }
                """);

            PreviewAssetLibrary library = PreviewAssetLibrary.Load(null, root);

            PortraitResolution resolved = library.ResolvePortrait("mina", "a", "01");
            Assert.Equal(AssetResolutionKind.Exact, resolved.Kind);
            Assert.EndsWith(
                Path.Combine("mina", "a", "01.png"),
                resolved.FilePath,
                StringComparison.Ordinal); // 규약이 권위
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 매니페스트가_아예_없어도_규약만으로_전부_동작한다()
    {
        string root = TempPortraitsRoot();

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "mina", "a"));

            for (int emotion = 1; emotion <= 12; emotion++)
            {
                File.WriteAllBytes(Path.Combine(root, "mina", "a", $"{emotion:D2}.png"), [1]);
            }

            PreviewAssetLibrary library = PreviewAssetLibrary.Load(null, root);

            Assert.Empty(library.Problems); // 매니페스트 부재는 문제가 아니다
            Assert.Equal(12, library.PortraitEntries.Count);
            Assert.Equal(AssetResolutionKind.Exact, library.ResolvePortrait("mina", "a", "12").Kind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 규약_경로도_해석의_정규화를_지난다()
    {
        // a/7.png 로 넣어도 요청 "7"(→"07")과 같은 키가 된다 — 해석기와 같은 규칙 하나.
        string root = TempPortraitsRoot();

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "mina", "a"));
            File.WriteAllBytes(Path.Combine(root, "mina", "a", "7.png"), [1]);

            PreviewAssetLibrary library = PreviewAssetLibrary.Load(null, root);

            Assert.Equal(AssetResolutionKind.Exact, library.ResolvePortrait("mina", "a", "7").Kind);
            Assert.Equal(AssetResolutionKind.Exact, library.ResolvePortrait("mina", null, "07").Kind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
