using Avalonia.Controls;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// 에피소드를 만들면 곧바로 쓸 수 있어야 한다 (2026-08-17 소유자 보고) — 대본 엑셀이
/// 그 자리에서 생기고, 대사가 한 줄도 없어도 시나리오 그래프에 노드가 선다.
/// </summary>
public sealed class EpisodeCreationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-episode-create", Guid.NewGuid().ToString("N"));

    private string ManifestPath => Path.Combine(_directory, "project" + ProjectManifestJson.FileExtension);

    private string ChapterPath =>
        Path.Combine(_directory, ChapterLibrary.FolderName, "ch01.xlsx");

    private string EpisodesFolder => Path.Combine(_directory, "episodes", "ch01");

    public EpisodeCreationTests()
    {
        Directory.CreateDirectory(_directory);
        ChapterWorkbookWriter.EnsureChapterWorkbook(
            Path.Combine(_directory, ChapterLibrary.FolderName), "ch01", [("trust", "신뢰"), ("anger", "분노")]);
        ProjectStore.Save(ManifestPath, new StoryProject { Title = "에피소드 생성 검증" });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 에피소드를_만들면_대본_엑셀이_그_자리에서_생긴다() => HeadlessUi.Run(() =>
    {
        var session = new AuthoringSession();
        session.Open(ManifestPath);

        var view = new ChapterGraphView();
        var window = new Window { Width = 1400, Height = 800, Content = view };
        window.Show();
        view.Attach(session);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        view.AddEpisodeFromToolbar();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 노드를 더블클릭하지 않아도 파일이 있어야 한다.
        Assert.NotNull(EpisodeLibrary.FindExisting(EpisodesFolder, "new01"));

        window.Close();
    });

    [Fact]
    public void 엑셀에서_직접_더한_행에도_대본이_생긴다() => HeadlessUi.Run(() =>
    {
        // 엑셀에서 직접 더한 행도 [대본 가져오기]로 들어온다.
        ChapterWorkbookWriter.AddEpisode(ChapterPath, "손으로적은화", title: string.Empty, 0, 0);

        var session = new AuthoringSession();
        session.Open(ManifestPath);

        var view = new ChapterGraphView();
        var window = new Window { Width = 1400, Height = 800, Content = view };
        window.Show();
        view.Attach(session);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        view.ImportEpisodes();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 대사가 한 줄도 없어도 작가의 판에 노드가 선다 (2026-08-17).
        Assert.Contains(
            session.Project.EnumerateNodes().OfType<DialogueNode>(),
            node => node.MarkedEpisodeId == "손으로적은화");

        // ⛔ <b>대본 파일이 생기는지는 더 묻지 않는다</b> (R-D · 2026-09-16). 예전에는
        //    동기화가 빈 워크북을 만들어 주었지만, 워크북은 이제 <b>산출물</b>이라 만드는
        //    자리는 이미터다(§4). 들여오기는 읽기만 한다 — 읽는 길이 쓰는 길을 겸하면
        //    "다시 읽지 않는다"가 성립하지 않는다.

        window.Close();
    });
}
