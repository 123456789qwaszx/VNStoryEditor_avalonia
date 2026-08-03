using Vn.Authoring.Assets;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

/// <summary>
/// W13 에셋 연결. 프리뷰가 배경·초상화를 어떻게 찾는지가 런타임 규약과 같아야 하고,
/// 못 찾는 경우가 조용히 사라지지 않아야 한다.
/// </summary>
public class PreviewAssetTests
{
    private static readonly string FixtureDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "AssetFixtures"));

    private static string BackgroundsDirectory => Path.Combine(FixtureDirectory, "backgrounds");

    private static string PortraitsDirectory => Path.Combine(FixtureDirectory, "portraits");

    private static PreviewAssetLibrary LoadFixtureLibrary() =>
        PreviewAssetLibrary.Load(BackgroundsDirectory, PortraitsDirectory);

    // ── 배경 ───────────────────────────────────────────────────────────────

    [Fact]
    public void 배경_키는_파일명이고_대소문자까지_정확히_일치해야_한다()
    {
        PreviewAssetLibrary library = LoadFixtureLibrary();

        BackgroundResolution exact = library.ResolveBackground("office");
        Assert.Equal(AssetResolutionKind.Exact, exact.Kind);
        Assert.EndsWith("office.png", exact.FilePath, StringComparison.Ordinal);

        // 유니티 Resources는 대소문자를 구분한다. 툴이 느슨하게 찾아 주면
        // 툴에서는 보이고 게임에서는 안 보이는 배경이 생긴다.
        Assert.Equal(AssetResolutionKind.Missing, library.ResolveBackground("Office").Kind);

        BackgroundResolution missing = library.ResolveBackground("harbor");
        Assert.Equal(AssetResolutionKind.Missing, missing.Kind);
        Assert.Equal("harbor", missing.SpriteKey); // 어느 키가 없는지 문자로 남는다
        Assert.Null(missing.FilePath);
    }

    // ── 초상화 ─────────────────────────────────────────────────────────────

    [Fact]
    public void 초상화_키를_정규화해서_찾는다()
    {
        PreviewAssetLibrary library = LoadFixtureLibrary();

        // variant 비우면 "a", emotion "2"는 "02" — 런타임 PortraitResolver 규칙.
        PortraitResolution resolved = library.ResolvePortrait("laru", null, "2");

        Assert.Equal(AssetResolutionKind.Exact, resolved.Kind);
        Assert.Equal(new PortraitKey("laru", "a", "02"), resolved.ResolvedKey);
        Assert.EndsWith("a_02.png", resolved.FilePath, StringComparison.Ordinal);

        Assert.Equal(
            AssetResolutionKind.Exact,
            library.ResolvePortrait("laru", "b", "3").Kind);
    }

    [Fact]
    public void 없는_표정은_기본_초상화로_폴백한다()
    {
        PreviewAssetLibrary library = LoadFixtureLibrary();

        PortraitResolution fallback = library.ResolvePortrait("willo", "b", "05");

        Assert.Equal(AssetResolutionKind.Fallback, fallback.Kind);
        Assert.Equal(new PortraitKey("willo", "b", "05"), fallback.RequestedKey); // 요청은 보존
        Assert.Equal(new PortraitKey("willo", "a", "01"), fallback.ResolvedKey);
        Assert.EndsWith("a_01.png", fallback.FilePath, StringComparison.Ordinal);
    }

    [Fact]
    public void 폴백도_없으면_요청_키가_남은_채_Missing이다()
    {
        PreviewAssetLibrary library = LoadFixtureLibrary();

        PortraitResolution missing = library.ResolvePortrait("nobody", null, null);

        Assert.Equal(AssetResolutionKind.Missing, missing.Kind);
        Assert.Equal(new PortraitKey("nobody", "a", "01"), missing.RequestedKey);
        Assert.Null(missing.ResolvedKey);
        Assert.Null(missing.FilePath);
    }

    // ── 매니페스트 ─────────────────────────────────────────────────────────

    [Fact]
    public void 매니페스트_형식_버전이_다르면_명시적으로_거부한다()
    {
        var exception = Assert.Throws<InvalidDataException>(
            () => PortraitManifest.Parse("""{ "formatVersion": 2, "entries": [] }"""));

        Assert.Contains("형식 버전 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 매니페스트가_가리키는_파일이_없으면_Problems에_남는다()
    {
        PreviewAssetLibrary library = LoadFixtureLibrary();

        // 픽스처의 ghost 항목은 PNG가 없다. 조용히 사라지는 대신 문제로 보인다.
        Assert.Contains(library.Problems, problem => problem.Contains("ghost", StringComparison.Ordinal));
        Assert.Equal(AssetResolutionKind.Missing, library.ResolvePortrait("ghost", null, null).Kind);
    }

    [Fact]
    public void 루트_미설정이면_전부_플레이스홀더지만_오류는_아니다()
    {
        PreviewAssetLibrary library = PreviewAssetLibrary.Load(null, null);

        Assert.False(library.BackgroundsConfigured);
        Assert.False(library.PortraitsConfigured);
        Assert.Empty(library.Problems);
        Assert.Equal(AssetResolutionKind.Missing, library.ResolveBackground("office").Kind);
        Assert.Equal(AssetResolutionKind.Missing, library.ResolvePortrait("laru", null, null).Kind);
    }

    [Fact]
    public void 없는_폴더와_없는_매니페스트는_Problems로_보인다()
    {
        PreviewAssetLibrary library = PreviewAssetLibrary.Load(
            Path.Combine(FixtureDirectory, "no_such_backgrounds"),
            BackgroundsDirectory); // 배경 폴더에는 매니페스트가 없다

        Assert.Equal(2, library.Problems.Count);
        Assert.Contains(library.Problems, problem => problem.Contains("배경 폴더", StringComparison.Ordinal));
        Assert.Contains(library.Problems, problem => problem.Contains("매니페스트", StringComparison.Ordinal));
    }

    // ── 프로젝트 설정 저장 ─────────────────────────────────────────────────

    [Fact]
    public void 에셋_루트가_manifest와_스냅샷을_왕복한다()
    {
        var project = new StoryProject();
        project.AssetRoots.BackgroundsPath = "../Runtime/Backgrounds";
        project.AssetRoots.PortraitsPath = "../Runtime/Portraits";

        ProjectManifest manifest = ProjectManifestJson.Read(ProjectManifestJson.Write(project));
        Assert.Equal("../Runtime/Backgrounds", manifest.AssetRoots.BackgroundsPath);
        Assert.Equal("../Runtime/Portraits", manifest.AssetRoots.PortraitsPath);

        StoryProject decoded = ProjectSnapshotCodec.Decode(ProjectSnapshotCodec.Encode(project));
        Assert.Equal("../Runtime/Backgrounds", decoded.AssetRoots.BackgroundsPath);
        Assert.Equal("../Runtime/Portraits", decoded.AssetRoots.PortraitsPath);

        // 미설정 프로젝트의 manifest에는 assetRoots 키 자체가 없다.
        Assert.DoesNotContain("assetRoots", ProjectManifestJson.Write(new StoryProject()), StringComparison.Ordinal);
    }

    [Fact]
    public void 에셋_루트_변경은_편집_통로를_지나고_되돌릴_수_있다()
    {
        var editor = new ProjectEditor(new StoryProject());

        editor.SetAssetRoots("assets\\backgrounds\\", "assets/portraits");

        Assert.Equal("assets/backgrounds", editor.Project.AssetRoots.BackgroundsPath); // 슬래시 정규화
        Assert.Equal("assets/portraits", editor.Project.AssetRoots.PortraitsPath);

        editor.Undo();
        Assert.True(editor.Project.AssetRoots.IsEmpty);

        editor.Redo();
        Assert.Equal("assets/backgrounds", editor.Project.AssetRoots.BackgroundsPath);
    }

    [Fact]
    public void 상대_에셋_루트는_프로젝트_기준으로_절대_경로가_된다()
    {
        string projectPath = Path.Combine(FixtureDirectory, "project.vnproject.json");

        string? resolved = AssetRootSettings.ResolveFrom(projectPath, "backgrounds");

        Assert.Equal(BackgroundsDirectory, resolved);
        Assert.Null(AssetRootSettings.ResolveFrom(projectPath, null));
    }

    // ── 화자 매핑 ──────────────────────────────────────────────────────────

    [Fact]
    public void 게임_정의가_화자와_캐릭터_키를_잇는다()
    {
        GameDefinition definition = GameDefinition.Parse("""
            {
              "speakers": [
                { "name": "라루", "characterId": "laru" },
                { "name": "윌로", "characterId": "willo" }
              ]
            }
            """)!;

        Assert.Equal("laru", definition.FindSpeakerCharacterId("라루"));
        Assert.Equal("laru", definition.FindSpeakerCharacterId(" 라루 ")); // 공백은 다듬는다

        // 매핑 없는 화자는 오류가 아니다 — 프리뷰가 이름만 보여 준다.
        Assert.Null(definition.FindSpeakerCharacterId("모브"));
        Assert.Null(definition.FindSpeakerCharacterId(null));
    }
}
