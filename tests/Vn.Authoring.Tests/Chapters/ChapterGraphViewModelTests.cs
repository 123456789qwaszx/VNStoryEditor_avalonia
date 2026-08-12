using System.Security.Cryptography;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// G4 수용 기준 중 뷰가 아니라 <b>데이터가 지는 부분</b> — 위치가 엑셀 그대로인가,
/// 자동 레이아웃이 끼어들지 않는가, 챕터 폴더를 어떻게 훑는가, 그리고 읽기가 정말 읽기뿐인가.
/// 화면 그리기는 <c>ChapterGraphView</c>가 이 값들을 Canvas 좌표로 옮기는 일만 한다.
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

    // ── 배치 ────────────────────────────────────────────────────────────────

    [Fact]
    public void 배치는_음수_좌표를_여백_안으로_밀되_상대_위치를_보존한다()
    {
        ChapterGraphModel model = ChapterWorkbookReader.Read(SamplePath);
        var layout = ChapterGraphLayout.For(model.Episodes, nodeWidth: 190, nodeHeight: 74, margin: 60);

        (double topX, double topY) = layout.Place(model.FindEpisode("branch05.02A")!);
        (double bottomX, double bottomY) = layout.Place(model.FindEpisode("main05.03")!);
        (double startX, double startY) = layout.Place(model.FindEpisode("main05.01")!);

        // 가장 작은 좌표(X=0, Y=-120)가 여백에 딱 붙는다.
        Assert.Equal(60, startX);
        Assert.Equal(60, topY);

        // 엑셀에서의 간격이 그대로다: X는 440-0=440, Y는 120-(-120)=240.
        Assert.Equal(440, topX - startX);
        Assert.Equal(240, bottomY - topY);
        Assert.Equal(topX, bottomX);
    }

    [Fact]
    public void 배치는_겹친_노드를_떼어놓지_않는다()
    {
        // 자동 레이아웃 금지(§6) — 두 노드가 같은 자리에 있으면 같은 자리에 그린다.
        // 툴이 "예쁘게" 떼어놓는 순간 "엑셀에 적은 자리에 그려진다"가 거짓이 된다.
        ChapterEpisode[] episodes =
        [
            Episode("a", 100, 100),
            Episode("b", 100, 100)
        ];

        var layout = ChapterGraphLayout.For(episodes, nodeWidth: 190, nodeHeight: 74, margin: 60);

        Assert.Equal(layout.Place(episodes[0]), layout.Place(episodes[1]));
    }

    [Fact]
    public void 캔버스_크기는_가장_바깥_노드와_여백으로_정해진다()
    {
        ChapterEpisode[] episodes = [Episode("a", 0, 0), Episode("b", 400, 200)];
        var layout = ChapterGraphLayout.For(episodes, nodeWidth: 190, nodeHeight: 74, margin: 60);

        Assert.Equal(400 + 190 + 120, layout.Width);
        Assert.Equal(200 + 74 + 120, layout.Height);
    }

    [Fact]
    public void 에피소드가_없어도_배치가_터지지_않는다()
    {
        var layout = ChapterGraphLayout.For([], nodeWidth: 190, nodeHeight: 74, margin: 60);

        Assert.Equal(120, layout.Width);
        Assert.Equal(120, layout.Height);
    }

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

        // 읽기 → 배치 → 다시 읽기. 뷰가 한 프레임에 하는 일 전부다.
        for (int pass = 0; pass < 3; pass++)
        {
            ChapterGraphModel model = ChapterWorkbookReader.Read(path);
            ChapterGraphLayout layout =
                ChapterGraphLayout.For(model.Episodes, nodeWidth: 190, nodeHeight: 74, margin: 60);

            foreach (ChapterEpisode episode in model.Episodes)
            {
                layout.Place(episode);
            }
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

    private static ChapterEpisode Episode(string id, double x, double y) =>
        new(id, id, "01", "Main", $"Story_{id}", x, y, null, null, null, null, SourceRow: 2);
}
