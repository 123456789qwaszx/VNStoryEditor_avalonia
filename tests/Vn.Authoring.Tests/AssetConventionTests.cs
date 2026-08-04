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
    public void 고아는_규약도_매니페스트도_아닌_파일뿐이다()
    {
        // §3.2 — 규약 위치의 파일(bandi/a/07.png)은 고아가 아니라 정상 등록이다.
        PreviewAssetLibrary library = LoadFixtureLibrary();

        Assert.DoesNotContain("bandi/a/07.png", library.OrphanPortraitFiles);
        Assert.DoesNotContain(
            library.Problems,
            problem => problem.Contains("bandi", StringComparison.Ordinal));

        // 2세그먼트 자유 경로 + 매니페스트 미참조(willo/stray.png)는 여전히 고아다.
        Assert.Contains("willo/stray.png", library.OrphanPortraitFiles);
    }

    [Fact]
    public void 세_구획이_아닌_경로는_규약이_아니다()
    {
        string root = TempPortraitsRoot();

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "mina", "a", "extra"));
            File.WriteAllBytes(Path.Combine(root, "mina", "a", "extra", "01.png"), [1]); // 4구획
            File.WriteAllBytes(Path.Combine(root, "loose.png"), [1]); // 1구획

            PreviewAssetLibrary library = PreviewAssetLibrary.Load(null, root);

            Assert.Equal(["loose.png", "mina/a/extra/01.png"], library.OrphanPortraitFiles.Order());
            Assert.Empty(library.PortraitEntries);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 안내문은_규약_경로를_말하고_JSON을_말하지_않는다()
    {
        // §3.4 — 빈칸의 안내문이 사용자 접점 전부다. 경로 모양은 PortraitKey에서 나온다.
        Assert.Contains(
            new PortraitKey("bandi", "a", "07").ToRelativePath(),
            AssetExplorerModel.PortraitPlacementGuide,
            StringComparison.Ordinal);
        Assert.Contains("night/alley", AssetExplorerModel.BackgroundPlacementGuide, StringComparison.Ordinal);

        // 비전공자 기준(§2): 안내 어디에도 JSON·매니페스트 편집이 없다.
        foreach (string guide in (string[])[
            AssetExplorerModel.PortraitPlacementGuide,
            AssetExplorerModel.BackgroundPlacementGuide])
        {
            Assert.DoesNotContain("JSON", guide, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("매니페스트", guide, StringComparison.Ordinal);
        }

        // 파일 없음 항목에도 "어디에 넣으면 되는지"가 그 자리에 보인다.
        AssetExplorerTree tree = AssetExplorerModel.Build(
            LoadFixtureLibrary(),
            Vn.Authoring.Definition.GameDefinition.Empty);
        AssetExplorerItem ghostLeaf = tree.PortraitItems
            .Single(item => item.Label.StartsWith("ghost", StringComparison.Ordinal))
            .Children.Single().Children.Single();
        Assert.Contains("ghost/a/01.png 위치에 넣어도 됩니다", ghostLeaf.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void 경로와_키는_한_함수로_왕복한다()
    {
        // §3.3 — 조립은 ToRelativePath, 해석은 TryParseRelativePath. 사본 없음.
        var key = new PortraitKey("bandi", "a", "07");
        Assert.Equal("bandi/a/07.png", key.ToRelativePath());
        Assert.Equal("bandi/a/07", key.ToRelativePath(""));

        Assert.True(PortraitKey.TryParseRelativePath(key.ToRelativePath(), out PortraitKey parsed));
        Assert.Equal(key, parsed);

        // 역슬래시·정규화도 해석기와 같은 규칙 하나를 지난다.
        Assert.True(PortraitKey.TryParseRelativePath(@"mina\b\7.PNG", out PortraitKey windows));
        Assert.Equal(new PortraitKey("mina", "b", "07"), windows);

        Assert.False(PortraitKey.TryParseRelativePath("mina/01.png", out _));      // 2구획
        Assert.False(PortraitKey.TryParseRelativePath("a/b/c/01.png", out _));     // 4구획
        Assert.False(PortraitKey.TryParseRelativePath("mina/a/01.jpg", out _));    // 확장자
        Assert.False(PortraitKey.TryParseRelativePath(null, out _));
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
