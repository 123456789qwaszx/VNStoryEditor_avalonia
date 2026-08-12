using System.Security.Cryptography;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// G4 수용 기준 중 뷰가 아니라 <b>데이터가 지는 부분</b> — 챕터 폴더를 어떻게 훑는가,
/// 그리고 읽기(+배치 계산)가 정말 읽기뿐인가. 배치 자체의 규칙(깊이 열)은
/// <c>ChapterBranchPlannerTests</c>가 진다.
/// </summary>
public sealed class ChapterGraphViewModelTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "vn-chapter-view-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    // ── 챕터 폴더 ───────────────────────────────────────────────────────────

    [Fact]
    public void 챕터_폴더는_프로젝트_파일_옆이다()
    {
        string folder = ChapterLibrary.FolderFor(Path.Combine(_directory, "story.vnproj.json"))!;

        Assert.Equal(Path.Combine(_directory, "chapters"), folder);
    }

    [Fact]
    public void 폴더가_없으면_빈_목록이고_폴더를_만들지_않는다()
    {
        string folder = Path.Combine(_directory, "chapters");

        Assert.Empty(ChapterLibrary.Load(folder));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void 엑셀이_열어_둔_잠금_파일은_챕터로_세지_않는다()
    {
        string folder = Path.Combine(_directory, "chapters");
        Directory.CreateDirectory(folder);
        File.Copy(SamplePath, Path.Combine(folder, "ch05.xlsx"));
        File.WriteAllText(Path.Combine(folder, "~$ch05.xlsx"), "lock");

        ChapterEntry entry = Assert.Single(ChapterLibrary.Load(folder));

        Assert.Equal("ch05", entry.ChapterId);
        Assert.True(entry.IsReadable);
        Assert.False(entry.HasErrors);
    }

    [Fact]
    public void 못_읽는_워크북이_있어도_나머지는_읽힌다()
    {
        string folder = Path.Combine(_directory, "chapters");
        Directory.CreateDirectory(folder);
        File.Copy(SamplePath, Path.Combine(folder, "ch05.xlsx"));
        File.WriteAllText(Path.Combine(folder, "broken.xlsx"), "이건 xlsx가 아니다");

        IReadOnlyList<ChapterEntry> entries = ChapterLibrary.Load(folder);

        Assert.Equal(2, entries.Count);

        ChapterEntry broken = entries.Single(entry => entry.ChapterId == "broken");
        Assert.False(broken.IsReadable);
        Assert.NotNull(broken.OpenFailure);

        Assert.True(entries.Single(entry => entry.ChapterId == "ch05").IsReadable);
    }

    // ── 읽기전용 (Gate A 4번) ───────────────────────────────────────────────

    [Fact]
    public void 워크북을_읽어도_파일이_바뀌지_않는다()
    {
        string folder = Path.Combine(_directory, "chapters");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "ch05.xlsx");
        File.Copy(SamplePath, path);

        byte[] before = File.ReadAllBytes(path);
        DateTime writtenAt = File.GetLastWriteTimeUtc(path);

        // 읽기 → 배치 계산 → 다시 읽기. 뷰가 한 프레임에 하는 일 전부다.
        for (int pass = 0; pass < 3; pass++)
        {
            ChapterGraphModel model = ChapterWorkbookReader.Read(path);
            _ = ChapterBranchPlanner.Layout(model);
        }

        Assert.Equal(Hash(before), Hash(File.ReadAllBytes(path)));
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void 같은_워크북을_두_번_읽으면_같은_위치가_나온다()
    {
        ChapterGraphModel first = ChapterWorkbookReader.Read(SamplePath);
        ChapterGraphModel second = ChapterWorkbookReader.Read(SamplePath);

        Assert.Equal(
            first.Episodes.Select(episode => (episode.EpisodeId, episode.X, episode.Y)),
            second.Episodes.Select(episode => (episode.EpisodeId, episode.X, episode.Y)));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
