using Vn.Authoring.Assets;

namespace Vn.Authoring.Tests;

/// <summary>
/// 표정 스프라이트 복제 — 저작 중 필요한 표정을 이미지로 골라 폴더 규약 경로에
/// 복제해 등록한다. 여기서 고정하는 것: 규약 경로 정규화가 해석기와 같고(PortraitKey
/// 하나), 원본은 건드리지 않고, 기존 파일은 조용히 덮어쓰지 않는다.
/// </summary>
public class PortraitSpriteImporterTests
{
    [Fact]
    public void 이미지를_규약_경로로_복제하고_키를_정규화한다()
    {
        (string root, string source) = MakeFixture();

        try
        {
            // variant 생략 → a, 표정 "7" → "07" — 해석기(ResolvePortrait)와 같은 정규화다.
            PortraitSpriteImporter.Imported imported = PortraitSpriteImporter.Import(
                root, source, "willow", variantKey: null, emotionKey: "7");

            Assert.Equal(new PortraitKey("willow", "a", "07"), imported.Key);
            Assert.Equal(Path.Combine(root, "willow", "a", "07.png"), imported.TargetPath);
            Assert.True(File.Exists(imported.TargetPath));
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(imported.TargetPath));
            Assert.True(File.Exists(source)); // 이동이 아니라 복제 — 원본은 그대로다

            // 복제된 파일은 스캔이 규약 1순위로 곧장 집는다 — 별도 등록 절차가 없다.
            PreviewAssetLibrary library = PreviewAssetLibrary.Load(null, root);
            Assert.Contains(imported.Key, library.PortraitKeys);
        }
        finally
        {
            CleanFixture(root, source);
        }
    }

    [Fact]
    public void 기존_파일은_조용히_덮어쓰지_않는다()
    {
        (string root, string source) = MakeFixture();

        try
        {
            string existing = Path.Combine(root, "willow", "a", "01.png");
            Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
            File.WriteAllBytes(existing, [9, 9, 9]);

            InvalidOperationException rejection = Assert.Throws<InvalidOperationException>(() =>
                PortraitSpriteImporter.Import(root, source, "willow", "a", "1"));

            Assert.Contains("이미 있습니다", rejection.Message, StringComparison.Ordinal);
            Assert.Equal([9, 9, 9], File.ReadAllBytes(existing)); // 기존 파일이 그대로다
        }
        finally
        {
            CleanFixture(root, source);
        }
    }

    [Fact]
    public void 덮어쓰기를_적으면_바꾸고_직전_그림은_bak으로_남는다()
    {
        // 2026-08-17 소유자 요청 — "이미 있다고 그대로 쓰라고 하는 대신 덮어쓸 수 있도록".
        // 그림을 고쳐 다시 넣는 것은 저작 중 흔한 일이고, 막아 두면 탐색기로 파일을 지우고
        // 돌아와야 했다. 대신 조용히 덮지는 않는다 — 부르는 쪽이 명시하고, 직전은 남는다.
        (string root, string source) = MakeFixture();

        try
        {
            string existing = Path.Combine(root, "willow", "a", "01.png");
            Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
            File.WriteAllBytes(existing, [9, 9, 9]);

            PortraitSpriteImporter.Imported imported =
                PortraitSpriteImporter.Import(root, source, "willow", "a", "1", overwrite: true);

            Assert.True(imported.Replaced);
            Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(existing));      // 새 그림이 그 자리에
            Assert.Equal([9, 9, 9], File.ReadAllBytes(existing + ".bak")); // 직전 그림은 남는다
        }
        finally
        {
            CleanFixture(root, source);
        }
    }

    [Fact]
    public void 없던_자리에_넣으면_덮어쓴_것이_아니다()
    {
        (string root, string source) = MakeFixture();

        try
        {
            PortraitSpriteImporter.Imported imported =
                PortraitSpriteImporter.Import(root, source, "willow", "a", "1", overwrite: true);

            Assert.False(imported.Replaced);
            Assert.False(File.Exists(imported.TargetPath + ".bak"));
        }
        finally
        {
            CleanFixture(root, source);
        }
    }

    [Fact]
    public void PNG가_아니면_변환하지_않고_거부한다()
    {
        (string root, string source) = MakeFixture();
        string jpg = Path.ChangeExtension(source, ".jpg");
        File.Move(source, jpg);

        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                PortraitSpriteImporter.Import(root, jpg, "willow", "a", "1"));
            Assert.Empty(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories));
        }
        finally
        {
            CleanFixture(root, jpg);
        }
    }

    [Fact]
    public void 없는_원본은_거부한다()
    {
        (string root, string source) = MakeFixture();
        File.Delete(source);

        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                PortraitSpriteImporter.Import(root, source, "willow", "a", "1"));
        }
        finally
        {
            CleanFixture(root, source);
        }
    }

    [Fact]
    public void 다음_빈_표정_번호는_variant별_숫자_최댓값_더하기_1이다()
    {
        PortraitKey[] existing =
        [
            new("willow", "a", "01"),
            new("willow", "a", "03"),
            new("willow", "b", "07"),
            new("willow", "a", "smile"), // 숫자가 아닌 키는 건너뛴다
            new("laru", "a", "09")       // 다른 캐릭터는 무관하다
        ];

        Assert.Equal("04", PortraitSpriteImporter.NextFreeEmotionKey(existing, "willow", "a"));
        Assert.Equal("08", PortraitSpriteImporter.NextFreeEmotionKey(existing, "willow", "b"));
        Assert.Equal("01", PortraitSpriteImporter.NextFreeEmotionKey(existing, "willow", "c"));
        Assert.Equal("01", PortraitSpriteImporter.NextFreeEmotionKey([], "willow", null));
    }

    private static (string Root, string Source) MakeFixture()
    {
        string stem = Path.Combine(Path.GetTempPath(), $"VnTool.Portrait.{Guid.NewGuid():N}");
        string root = stem + ".root";
        string source = stem + ".source.png";
        Directory.CreateDirectory(root);
        File.WriteAllBytes(source, [1, 2, 3, 4]);
        return (root, source);
    }

    private static void CleanFixture(string root, string source)
    {
        Directory.Delete(root, recursive: true);

        if (File.Exists(source))
        {
            File.Delete(source);
        }
    }
}
