using ClosedXML.Excel;
using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>옮기면 옛 자리의 대본을 민다</b> (2026-09-16 소유자: <i>"옮길 때 옛 파일을 밀어줘"</i>).
///
/// ⛔ 대본 워크북은 <c>episodes/{챕터}/{에피소드}.xlsx</c>에 살고 산출물이다. 챕터가 바뀌면
/// 다음 저장이 새 자리에 내는데 옛 자리의 파일은 아무도 안 지운다 — 그대로 두면 <b>같은
/// 에피소드의 원고가 두 폴더에</b> 남는다.
///
/// ⚠ 지우지 않고 민다 — <c>.bak</c>은 이 저장소의 공통 규약이다.
/// </summary>
public sealed class EpisodeMoverTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "vn-episode-move", Guid.NewGuid().ToString("N"));

    public EpisodeMoverTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void 옛_자리의_워크북이_bak으로_밀린다()
    {
        World world = Build();

        EpisodeMover.Result result =
            EpisodeMover.Move(world.Editor, world.ProjectPath, "ch01", ["a"], "ch02");

        Assert.True(result.Moved, result.Failure);

        Assert.False(File.Exists(Path.Combine(world.Episodes, "a.xlsx")));
        Assert.True(File.Exists(Path.Combine(world.Episodes, "a.xlsx" + ChapterDeleter.BackupSuffix)));

        // 이름을 말해 준다 — 안 말하면 "원고가 어디 갔지"가 되고 폴더를 열어 봐야 풀린다.
        Assert.Equal(["a.xlsx.bak"], result.Backups);
    }

    [Fact]
    public void 엑셀이_붙들고_있으면_모델도_안_옮긴다()
    {
        // ⛔ 파일이 먼저인 이유다. 모델만 옮겨 놓고 파일에서 막히면 원고는 옛 챕터 폴더에
        //    남은 채 에피소드는 새 챕터에 앉는다.
        World world = Build();

        using (new FileStream(
                   Path.Combine(world.Episodes, "a.xlsx"),
                   FileMode.Open, FileAccess.Read, FileShare.None))
        {
            EpisodeMover.Result result =
                EpisodeMover.Move(world.Editor, world.ProjectPath, "ch01", ["a"], "ch02");

            Assert.False(result.Moved);
            Assert.Contains("엑셀이 열고 있을 수 있습니다", result.Failure!, StringComparison.Ordinal);
        }

        Assert.Contains(world.Editor.FindChapter("ch01")!.Episodes, episode => episode.EpisodeId == "a");
        Assert.Empty(world.Editor.FindChapter("ch02")!.Episodes);
    }

    [Fact]
    public void 모델이_거절하면_민_파일을_되돌린다()
    {
        // 도착에 같은 Id가 있어 거절되는 경우다 — 원고가 `.bak`에 남아 있으면 사람은
        // 아무 일도 안 일어난 줄 아는데 파일 이름만 바뀌어 있다.
        World world = Build();
        world.Editor.AddEpisode("ch02", "a", title: "남의 a", 0, 0);

        EpisodeMover.Result result =
            EpisodeMover.Move(world.Editor, world.ProjectPath, "ch01", ["a"], "ch02");

        Assert.False(result.Moved);
        Assert.True(File.Exists(Path.Combine(world.Episodes, "a.xlsx")));
        Assert.False(File.Exists(Path.Combine(world.Episodes, "a.xlsx" + ChapterDeleter.BackupSuffix)));
    }

    [Fact]
    public void 같은_챕터_안이면_파일을_안_건드린다()
    {
        // 에피소드를 같은 챕터의 다른 장면에 놓은 경우다 — 파일이 있던 자리 그대로다.
        World world = Build();

        EpisodeMover.Result result = EpisodeMover.Move(
            world.Editor, world.ProjectPath, "ch01", ["a"], "ch01", sceneId: "opening");

        Assert.True(result.Moved);
        Assert.Empty(result.Backups);
        Assert.True(File.Exists(Path.Combine(world.Episodes, "a.xlsx")));
        Assert.Equal("opening", world.Editor.FindChapter("ch01")!.Episodes.Single(e => e.EpisodeId == "a").SceneId);
    }

    [Fact]
    public void 아직_아무도_안_쓴_에피소드는_밀_것이_없다()
    {
        // 파일이 없는 것은 실패가 아니다 — 대본은 글을 쓴 뒤에야 생긴다.
        World world = Build();

        EpisodeMover.Result result =
            EpisodeMover.Move(world.Editor, world.ProjectPath, "ch01", ["b"], "ch02");

        Assert.True(result.Moved, result.Failure);
        Assert.Empty(result.Backups);
        Assert.Contains(world.Editor.FindChapter("ch02")!.Episodes, episode => episode.EpisodeId == "b");
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private sealed record World(ProjectEditor Editor, string ProjectPath, string Episodes);

    /// <summary>ch01: a(대본 있음) · b(없음) · ch02: 빈 챕터.</summary>
    private World Build()
    {
        string projectPath = Path.Combine(_root, "예제.vnproject.json");

        var project = new StoryProject { Title = "옮기기" };
        ProjectStore.Save(projectPath, project);

        var editor = new ProjectEditor(project);

        editor.EnsureChapter("ch01");
        editor.EnsureChapter("ch02");
        editor.EnsureChapterBoard("ch01");
        editor.EnsureChapterBoard("ch02");

        editor.AddEpisode("ch01", "a", title: "가", 0, 0, sceneId: "shared");
        editor.AddEpisode("ch01", "b", title: "나", 260, 0, sceneId: "shared");

        string episodes = EpisodeLibrary.FolderFor(projectPath, "ch01")!;
        Directory.CreateDirectory(episodes);
        WriteScript(Path.Combine(episodes, "a.xlsx"));

        return new World(editor, projectPath, episodes);
    }

    private static void WriteScript(string path)
    {
        using var book = new XLWorkbook();
        IXLWorksheet sheet = book.Worksheets.Add("대본");

        sheet.Cell(1, 1).Value = "순번";
        sheet.Cell(1, 2).Value = "화자";
        sheet.Cell(1, 3).Value = "대사";
        sheet.Cell(2, 1).Value = 10;
        sheet.Cell(2, 2).Value = "윌로";
        sheet.Cell(2, 3).Value = "복도는 조용했다.";

        book.SaveAs(path);
    }
}
