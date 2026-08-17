using Avalonia.Controls;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// 챕터를 바꿔도 화면이 이전 챕터의 결과를 들고 있지 않다 (2026-08-17 소유자 보고:
/// "Ctrl+S를 눌러야 노드를 감싼 빨간 경고가 사라지면서 스탯이 그제서야 보여" ·
/// "드롭다운으로 챕터를 선택할 때 왼쪽 챕터목록이 실시간으로 반영이 안 된다").
///
/// 뿌리는 하나였다 — 드롭다운이 왼쪽 목록 클릭과 <b>다른 길</b>을 갔다.
/// </summary>
public sealed class ChapterSwitchTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-chapter-switch", Guid.NewGuid().ToString("N"));

    private string ManifestPath => Path.Combine(_directory, "project" + ProjectManifestJson.FileExtension);

    private string ChapterFolder => Path.Combine(_directory, ChapterLibrary.FolderName);

    public ChapterSwitchTests()
    {
        Directory.CreateDirectory(_directory);

        // ch01 — 고아 에피소드가 있어 도달 불가 오류가 난다.
        ChapterWorkbookWriter.EnsureChapterWorkbook(ChapterFolder, "ch01", [("trust", "신뢰")]);
        string first = Path.Combine(ChapterFolder, "ch01.xlsx");
        ChapterWorkbookWriter.AddEpisode(first, "시작", title: "", 0, 0);
        ChapterWorkbookWriter.AddEpisode(first, "고아", title: "", 1, 1);

        // ch02 — 깨끗하고, 간선에 증감이 있어 도착 스탯이 카드에 떠야 한다.
        ChapterWorkbookWriter.EnsureChapterWorkbook(ChapterFolder, "ch02", [("trust", "신뢰")]);
        string second = Path.Combine(ChapterFolder, "ch02.xlsx");
        ChapterWorkbookWriter.AddEpisode(second, "둘시작", title: "", 0, 0);
        ChapterWorkbookWriter.AddEpisode(second, "둘끝", title: "", 1, 0);
        ChapterWorkbookWriter.AddEdge(second, "둘시작", "둘끝");
        ChapterWorkbookWriter.UpdateEdge(second, "둘시작", "둘끝", statChanges: "trust +2");

        var project = new StoryProject { Title = "챕터 전환 검증" };
        ProjectStore.Save(ManifestPath, project);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 드롭다운으로_바꾸면_그_챕터로_검증도_다시_돈다() => HeadlessUi.Run(() =>
    {
        // 전에는 드롭다운이 Draw()만 불렀다 — 이전 챕터(ch01)의 도달성 결과로 ch02를
        // 그려서, ch02의 에피소드가 전부 "도달 불가"로 붉게 섰다.
        (ChapterGraphView view, Canvas canvas) = Show();

        Assert.Equal("ch01", view.FindControl<ComboBox>("ChapterCombo")!.SelectedItem);

        view.FindControl<ComboBox>("ChapterCombo")!.SelectedItem = "ch02";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, Cards(canvas).Count);
        Assert.All(Cards(canvas), card =>
            Assert.NotEqual(2.0, card.BorderThickness.Left));   // 오류 테두리(2px)가 아니다
    });

    [Fact]
    public void 바꾼_챕터의_도착_스탯이_그_자리에서_보인다() => HeadlessUi.Run(() =>
    {
        // "스탯들이 반영된게 그제서야 보여" — 폭도 검증 결과에서 오므로 같이 비어 있었다.
        (ChapterGraphView view, Canvas canvas) = Show();

        view.FindControl<ComboBox>("ChapterCombo")!.SelectedItem = "ch02";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("신뢰 2", StatLine(canvas, "둘끝"));
        Assert.Equal("신뢰 0", StatLine(canvas, "둘시작"));
    });

    [Fact]
    public void 드롭다운_선택이_셸에_알려진다() => HeadlessUi.Run(() =>
    {
        // 왼쪽 목록의 강조는 활성 판을 본다 — 셸이 이 알림을 듣고 판을 옮긴다.
        (ChapterGraphView view, _) = Show();

        var picked = new List<string>();
        view.ChapterSelected += id => picked.Add(id);

        view.FindControl<ComboBox>("ChapterCombo")!.SelectedItem = "ch02";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("ch02", Assert.Single(picked));
    });

    [Fact]
    public void 챕터를_바꾸면_이전_챕터의_선택은_따라오지_않는다() => HeadlessUi.Run(() =>
    {
        // 들고 있으면 없는 간선·에피소드를 가리킨다.
        (ChapterGraphView view, _) = Show();

        view.SelectEpisode("시작");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(view.FindControl<StackPanel>("PropertyPanel")!.IsVisible);

        view.FindControl<ComboBox>("ChapterCombo")!.SelectedItem = "ch02";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(view.FindControl<StackPanel>("PropertyPanel")!.IsVisible);
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static List<Border> Cards(Canvas canvas) =>
        canvas.Children.OfType<Border>().Where(border => border.Tag is string).ToList();

    private static string StatLine(Canvas canvas, string episodeId)
    {
        Border card = Cards(canvas).Single(border => (string)border.Tag! == episodeId);

        return ((StackPanel)card.Child!).Children
            .OfType<TextBlock>()
            .FirstOrDefault(block => block.Tag as string == ChapterGraphView.StatLineTag)
            ?.Text ?? string.Empty;
    }

    private (ChapterGraphView View, Canvas Canvas) Show()
    {
        var session = new AuthoringSession();
        session.Open(ManifestPath);

        var view = new ChapterGraphView();
        var window = new Window { Width = 1400, Height = 800, Content = view };
        window.Show();
        view.Attach(session);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return (view, view.FindControl<Canvas>("GraphCanvas")!);
    }
}
