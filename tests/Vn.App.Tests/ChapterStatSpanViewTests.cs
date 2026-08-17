using Avalonia.Controls;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// 노드 카드의 스탯 줄 (2026-08-17 소유자: "각각의 에피소드 노드에서, 간선을 따라 왔을 때
/// 스탯의 변화량이 노드에 표시되도록. 여러 루트가 있을 때는 최소최대량을 표기").
///
/// 증명이 낸 폭이 정말 카드 글자가 되는지를 본다 — 프루버 단위 테스트만으로는 화면에
/// 안 붙는 배선을 못 잡는다.
/// </summary>
public sealed class ChapterStatSpanViewTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-stat-span", Guid.NewGuid().ToString("N"));

    private string ManifestPath => Path.Combine(_directory, "project" + ProjectManifestJson.FileExtension);

    private string ChapterPath => Path.Combine(_directory, ChapterLibrary.FolderName, "ch01.xlsx");

    public ChapterStatSpanViewTests()
    {
        Directory.CreateDirectory(_directory);

        string chapters = Path.Combine(_directory, ChapterLibrary.FolderName);
        ChapterWorkbookWriter.EnsureChapterWorkbook(chapters, "ch01", [("trust", "신뢰"), ("anger", "분노")]);

        // ep1 ─(+1)─ 싼길 ─┐
        //   └─(+3)─ 비싼길 ─┴─ 합류      (분노는 챕터 어디에서도 움직이지 않는다)
        ChapterWorkbookWriter.AddEpisode(ChapterPath, "ep1", title: "", 0, 0);
        ChapterWorkbookWriter.AddEpisode(ChapterPath, "싼길", title: "", 1, 0);
        ChapterWorkbookWriter.AddEpisode(ChapterPath, "비싼길", title: "", 1, 1);
        ChapterWorkbookWriter.AddEpisode(ChapterPath, "합류", title: "", 2, 0);

        ChapterWorkbookWriter.AddEdge(ChapterPath, "ep1", "싼길");
        ChapterWorkbookWriter.AddEdge(ChapterPath, "ep1", "비싼길");
        ChapterWorkbookWriter.AddEdge(ChapterPath, "싼길", "합류");
        ChapterWorkbookWriter.AddEdge(ChapterPath, "비싼길", "합류");

        ChapterWorkbookWriter.UpdateEdge(ChapterPath, "ep1", "싼길", statChanges: "trust +1");
        ChapterWorkbookWriter.UpdateEdge(ChapterPath, "ep1", "비싼길", statChanges: "trust +3");

        ProjectStore.Save(ManifestPath, new StoryProject { Title = "스탯 폭 검증" });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 한_길로만_오는_노드는_값_하나를_단다() => HeadlessUi.Run(() =>
    {
        Canvas canvas = Render();

        Assert.Equal("신뢰 1", StatLine(canvas, "싼길"));
        Assert.Equal("신뢰 3", StatLine(canvas, "비싼길"));
    });

    [Fact]
    public void 여러_길이_들어오는_노드는_최소_최대로_벌어진다() => HeadlessUi.Run(() =>
    {
        Canvas canvas = Render();

        Assert.Equal("신뢰 1~3", StatLine(canvas, "합류"));
    });

    [Fact]
    public void 챕터_어디에서도_안_움직이는_스탯은_카드에_안_뜬다() => HeadlessUi.Run(() =>
    {
        // 분노는 어느 간선도 건드리지 않는다 — 모든 카드에 `분노 0`이 붙으면 정작
        // 움직이는 신뢰가 그 줄에 묻힌다.
        Canvas canvas = Render();

        Assert.DoesNotContain("분노", StatLine(canvas, "합류"));
        Assert.DoesNotContain("분노", StatLine(canvas, "ep1"));
    });

    [Fact]
    public void 시작_노드는_초기값을_그대로_단다() => HeadlessUi.Run(() =>
    {
        Canvas canvas = Render();

        Assert.Equal("신뢰 0", StatLine(canvas, "ep1"));
    });

    [Fact]
    public void 증감이_하나도_없는_챕터는_거르지_않고_초기값을_보여_준다() => HeadlessUi.Run(() =>
    {
        // 2026-08-17 소유자 보고 — 증감을 아직 안 적은 판에서 카드가 비어 있으면 기능이
        // 없는 것처럼 읽힌다. 거를 것이 없을 때는 거르지 않는다.
        ChapterWorkbookWriter.UpdateEdge(ChapterPath, "ep1", "싼길", statChanges: string.Empty);
        ChapterWorkbookWriter.UpdateEdge(ChapterPath, "ep1", "비싼길", statChanges: string.Empty);

        Canvas canvas = Render();

        Assert.Equal("신뢰 0 · 분노 0", StatLine(canvas, "합류"));
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <summary>그 카드에 붙은 스탯 줄 — 없으면 빈 글자.</summary>
    private static string StatLine(Canvas canvas, string episodeId)
    {
        Border card = canvas.Children.OfType<Border>()
            .Single(border => border.Tag as string == episodeId);

        return ((StackPanel)card.Child!).Children
            .OfType<TextBlock>()
            .FirstOrDefault(block => block.Tag as string == ChapterGraphView.StatLineTag)
            ?.Text ?? string.Empty;
    }

    private Canvas Render()
    {
        var session = new AuthoringSession();
        session.Open(ManifestPath);

        var view = new ChapterGraphView();
        var window = new Window { Width = 1280, Height = 800, Content = view };
        window.Show();
        view.Attach(session);

        window.Measure(new Avalonia.Size(1280, 800));
        window.Arrange(new Avalonia.Rect(0, 0, 1280, 800));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return view.FindControl<Canvas>("GraphCanvas")!;
    }
}
