using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;
using Path = System.IO.Path;

namespace Vn.App.Tests;

/// <summary>
/// v11 §7 화면 — 엔딩 간선이 판에서 보이고, 아직 안 채운 연출이 보고에 선다.
///
/// 규격: <c>docs/work-orders/edge-presentation-orders.md</c>. 단위 테스트는 배선을 못 보므로
/// 여기서는 앱 경로를 그대로 태운다(동기화 → 되쓰기 → 재읽기 → 그리기).
/// </summary>
public sealed class EdgePresentationScreenTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-edge-screen", Guid.NewGuid().ToString("N"));

    private string ManifestPath => Path.Combine(_directory, "p" + ProjectManifestJson.FileExtension);

    private string ChapterPath =>
        Path.Combine(_directory, ChapterLibrary.FolderName, "ch01.xlsx");

    public EdgePresentationScreenTests()
    {
        Directory.CreateDirectory(_directory);

        string chapters = Path.Combine(_directory, ChapterLibrary.FolderName);
        ChapterWorkbookWriter.EnsureChapterWorkbook(chapters, "ch01", [("trust", "신뢰"), ("anger", "분노")]);

        ChapterWorkbookWriter.AddEpisode(ChapterPath, "ep1", "첫", 0, 0);
        ChapterWorkbookWriter.AddEpisode(ChapterPath, "끝", "마지막", 200, 0);
        ChapterWorkbookWriter.AddEdge(ChapterPath, "ep1", "끝");

        // 기획자가 엔딩키만 적은 상태 — 연출 칸은 비어 있다.
        using (var workbook = new ClosedXML.Excel.XLWorkbook(ChapterPath))
        {
            workbook.Worksheet(ChapterSheetNames.Edges).Cell(2, 10).SetValue("ch_bad");
            workbook.Save();
        }

        ProjectStore.Save(ManifestPath, new StoryProject { Title = "v11 화면" });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 엔딩_간선이_판에서_보인다() => HeadlessUi.Run(() =>
    {
        (ChapterGraphView view, Window window) = Show();

        string[] labels = view.FindControl<Canvas>("GraphCanvas")!.Children
            .OfType<Border>()
            .Select(border => border.Child)
            .OfType<TextBlock>()
            .Select(text => text.Text ?? string.Empty)
            .ToArray();

        // ⏹ = 이 길을 타면 챕터가 끝난다 · 🎬 = 연출이 붙어 있다.
        Assert.Contains(labels, text => text.Contains("⏹ ch_bad", StringComparison.Ordinal));
        Assert.Contains(labels, text => text.Contains("🎬", StringComparison.Ordinal));

        window.Close();
    });

    [Fact]
    public void 아직_안_채운_연출이_보고에_선다() => HeadlessUi.Run(() =>
    {
        // 자동 생성은 자리만 만든다 — 빈 노드가 정상이므로, 어느 간선이 남았는지
        // 한 자리에서 보이지 않으면 엔딩 열 개 중 하나가 빈 채로 출시된다.
        (ChapterGraphView view, Window window) = Show();

        string[] reported = view.FindControl<StackPanel>("DiagnosticsPanel")!
            .GetLogicalDescendants()
            .OfType<TextBlock>()
            .Select(text => text.Text ?? string.Empty)
            .Where(text => text.Contains("아직 비어 있습니다", StringComparison.Ordinal))
            .ToArray();

        string entry = Assert.Single(reported);
        Assert.Contains("엔딩 ch_bad", entry, StringComparison.Ordinal);
        Assert.Contains("ep1→끝", entry, StringComparison.Ordinal);

        window.Close();
    });

    [Fact]
    public void 기획자가_엔딩키만_적어도_연출_이름이_되쓰인다() => HeadlessUi.Run(() =>
    {
        (ChapterGraphView view, Window window) = Show();

        Assert.Equal(
            "엔딩 ch_bad",
            ChapterWorkbookReader.Read(ChapterPath).Edges.Single().PresentationNodeName);

        window.Close();
    });

    [Fact]
    public void 엔딩_간선이_닿는_에피소드에_별표가_선다() => HeadlessUi.Run(() =>
    {
        // v11 회귀 방지 — 엔딩키가 에피소드에서 간선으로 옮겨간 뒤에도 `★`가 계속 서야 한다.
        // 카드는 `episode.IsEnding`을 보고 있었고, 그 값은 v11 이후 언제나 거짓이었다.
        (ChapterGraphView view, Window window) = Show();

        Assert.Equal(["끝"], StarredEpisodes(view));

        window.Close();
    });

    /// <summary>판에서 `★`(엔딩)를 달고 있는 에피소드 Id들.</summary>
    private static string[] StarredEpisodes(ChapterGraphView view) =>
        view.FindControl<Canvas>("GraphCanvas")!.Children
            .OfType<Border>()
            .Where(card => card.Tag is string && card.Child is StackPanel)
            .Where(card => ((StackPanel)card.Child!).Children
                .OfType<StackPanel>()
                .SelectMany(row => row.Children.OfType<TextBlock>())
                .Any(mark => mark.Text == "★"))
            .Select(card => (string)card.Tag!)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

    private (ChapterGraphView View, Window Window) Show()
    {
        var session = new AuthoringSession();
        session.Open(ManifestPath);

        var view = new ChapterGraphView();
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        view.Attach(session);

        window.Measure(new Size(1000, 700));
        window.Arrange(new Rect(0, 0, 1000, 700));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 동기화가 노드를 세우고 이름을 되쓴다 → 다시 읽어야 판이 그 이름을 안다.
        view.SyncEpisodes();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        view.RefreshFromDisk();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return (view, window);
    }
}
