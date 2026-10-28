using Vn.App.Services;
using Vn.Authoring.Assets;

namespace Vn.App.Tests;

/// <summary>
/// 세션의 프리뷰 에셋 인덱스. 루트 설정 변경(편집·되돌리기)은 자동으로 반영하되,
/// 폴더 내용 변경은 명시적 새로 고침만 반영한다.
/// </summary>
public class SessionAssetTests
{
    private static string TempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"VnTool.Assets.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public void 루트_미설정_세션은_빈_라이브러리를_준다()
    {
        var session = new AuthoringSession();

        Assert.False(session.AssetLibrary.BackgroundsConfigured);
        Assert.False(session.AssetLibrary.PortraitsConfigured);
        Assert.Empty(session.AssetLibrary.Problems);
    }

    [Fact]
    public void 루트_설정과_되돌리기가_라이브러리에_반영된다()
    {
        string directory = TempDirectory();

        try
        {
            File.WriteAllBytes(Path.Combine(directory, "office.png"), [0]);

            var session = new AuthoringSession();
            session.Editor.SetAssetRoots(directory, null);

            Assert.True(session.AssetLibrary.BackgroundsConfigured);
            Assert.Equal(
                AssetResolutionKind.Exact,
                session.AssetLibrary.ResolveBackground("office").Kind);

            session.Editor.Undo();
            Assert.False(session.AssetLibrary.BackgroundsConfigured);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 폴더_내용_변경은_명시적_새로_고침만_반영한다()
    {
        string directory = TempDirectory();

        try
        {
            var session = new AuthoringSession();
            session.Editor.SetAssetRoots(directory, null);
            Assert.Equal(AssetResolutionKind.Missing, session.AssetLibrary.ResolveBackground("office").Kind);

            // 파일이 생겨도 인덱스는 그대로다 — 감지가 아니라 새로 고침이다.
            File.WriteAllBytes(Path.Combine(directory, "office.png"), [0]);
            Assert.Equal(AssetResolutionKind.Missing, session.AssetLibrary.ResolveBackground("office").Kind);

            session.RefreshAssets();
            Assert.Equal(AssetResolutionKind.Exact, session.AssetLibrary.ResolveBackground("office").Kind);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
