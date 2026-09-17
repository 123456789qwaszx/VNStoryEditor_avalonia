using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// <b>[챕터] 탭의 스탯·조건 표가 편집 가능하다</b> (2026-09-17 소유자: *"이제 엑셀이 아닌
/// 챕터그래프에서 직접 조건과 스탯을 정의"*).
///
/// ⚠ 전에는 읽기 전용이었고 *"편집은 챕터 엑셀의 시트에서"*라고 적혀 있었다. 그 안내는
/// <b>R-F(2026-09-16)가 이미 낡게 만들었다</b> — 그날 워크북이 산출물이 되고 값의 주인이
/// <c>ChapterDocument</c>로 넘어왔는데 글자만 남아 있었다.
/// </summary>
public sealed class ChapterSheetEditTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-sheet-edit", Guid.NewGuid().ToString("N"));

    private string ManifestPath => Path.Combine(_directory, "p" + ProjectManifestJson.FileExtension);

    public ChapterSheetEditTests()
    {
        Directory.CreateDirectory(_directory);
        ProjectStore.Save(ManifestPath, new StoryProject { Title = "챕터 시트 편집" });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 스탯_표에_값_칸이_선다() => HeadlessUi.Run(() =>
    {
        // 전에는 한 줄짜리 글자였다 — 이제 고칠 수 있는 칸이어야 한다.
        (ChapterGraphView view, _) = Show();

        // 표시이름 · 초기 · 최소 · 최대 — 키는 라벨(개명은 [✎]), 타입은 콤보다.
        Assert.Equal(4, Boxes(Row(view, "stat:trust")).Count);
    });

    [Fact]
    public void 표시이름을_고치면_모델에_써진다() => HeadlessUi.Run(() =>
    {
        (ChapterGraphView view, AuthoringSession session) = Show();

        Commit(Boxes(Row(view, "stat:trust"))[0], "신뢰도");

        Assert.Equal(
            "신뢰도",
            session.Editor.FindChapter("ch01")!.Stats.Single(stat => stat.Key == "trust").DisplayName);
    });

    [Fact]
    public void 숫자_칸에_글자를_적으면_조용히_안_버리고_말한다() => HeadlessUi.Run(() =>
    {
        // ⛔ 조용히 버리면 사람은 적은 줄 알고 넘어간다.
        (ChapterGraphView view, AuthoringSession session) = Show();

        Commit(Boxes(Row(view, "stat:trust"))[1], "많이");

        Assert.Contains("정수", session.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(
            0,
            session.Editor.FindChapter("ch01")!.Stats.Single(stat => stat.Key == "trust").Initial);
    });

    [Fact]
    public void 조건_식을_고치면_모델에_써진다() => HeadlessUi.Run(() =>
    {
        (ChapterGraphView view, AuthoringSession session) = Show();

        Commit(Boxes(Row(view, "condition:신뢰높음"))[0], "trust >= 5");

        Assert.Equal(
            "trust >= 5",
            session.Editor.FindChapter("ch01")!.Conditions
                .Single(item => item.Label == "신뢰높음").Expression);
    });

    [Fact]
    public void 쓰는_곳이_있는_스탯을_지우려_하면_어디인지_말한다() => HeadlessUi.Run(() =>
    {
        // ⛔ *"쓰이고 있습니다"*로 끝내면 사람이 챕터를 뒤져야 한다 — 거절이 곧 정리 안내다.
        (ChapterGraphView view, AuthoringSession session) = Show();

        Press(Buttons(Row(view, "stat:trust")).Last());

        // 한 번에 안 지우고 <b>보여 주고 묻는다</b> — 무엇이 바뀔지 보고 나서 누르는 것과
        // 경고만 읽고 누르는 것은 다른 일이다.
        Assert.Contains(session.Editor.FindChapter("ch01")!.Stats, stat => stat.Key == "trust");

        List<string> shown = Confirm(view).GetVisualDescendants().OfType<TextBlock>()
            .Select(block => block.Text ?? string.Empty).ToList();

        Assert.Contains(shown, text => text.Contains("1곳", StringComparison.Ordinal));
        Assert.Contains(shown, text => text.Contains("신뢰높음", StringComparison.Ordinal));
    });

    [Fact]
    public void 한_번_더_누르면_비우고_지운다() => HeadlessUi.Run(() =>
    {
        // ⭐ 다 — 간선은 그 항만 빠지고, 조건은 <b>식만 비고 남는다</b>.
        (ChapterGraphView view, AuthoringSession session) = Show();
        session.Editor.AddEpisode("ch01", "root", title: "root", 0, 0);
        session.Editor.AddEpisode("ch01", "a", title: "a", 0, 0);
        session.Editor.AddEdge("ch01", "root", "a", optionLabel: "믿는다", statChanges: "trust +1");
        Redraw(view);

        Press(Buttons(Row(view, "stat:trust")).Last());
        Press(Confirm(view).GetVisualDescendants().OfType<Button>()
            .Single(button => (button.Content as string) == "모두 비우고 지우기"));

        ChapterDocument chapter = session.Editor.FindChapter("ch01")!;

        Assert.DoesNotContain(chapter.Stats, stat => stat.Key == "trust");
        Assert.Empty(chapter.Edges.Single().StatChanges);

        // 조건은 <b>남는다</b> — 통째로 지우면 그것을 쓰는 간선의 표시/해금이 함께 풀린다.
        Assert.Equal(string.Empty, chapter.Conditions.Single().Expression);
    });

    [Fact]
    public void 아무_데서도_안_쓰는_스탯은_지워진다() => HeadlessUi.Run(() =>
    {
        (ChapterGraphView view, AuthoringSession session) = Show();
        session.Editor.AddChapterStat("ch01", "여분");
        Redraw(view);

        Press(Buttons(Row(view, "stat:여분")).Last());

        Assert.DoesNotContain(session.Editor.FindChapter("ch01")!.Stats, stat => stat.Key == "여분");
    });

    [Fact]
    public void 키_바꾸기는_세이브가_안_따라온다고_먼저_말한다() => HeadlessUi.Run(() =>
    {
        // ⛔ <b>키는 세이브의 열쇠다</b> (런타임 회신 §6.2). 저쪽 `LocalSaveFile.Stats`가 이
        //    글자로 저장하고, 복원은 키가 없으면 <b>초기값</b>으로 떨어진다 — 경고도 없다.
        //    개명이 끌고 가는 다섯 자리는 프로젝트 안이고 <b>디스크의 세이브는 그 다섯에
        //    없다</b>. 표시 이름만 고치려던 사람이 이 문을 열 수 있으므로, 경고가
        //    <b>적기 전에</b> 서야 한다.
        (ChapterGraphView view, _) = Show();

        Press(Buttons(Row(view, "stat:trust")).First());

        List<string> shown = Prompt(view).GetVisualDescendants().OfType<TextBlock>()
            .Select(block => block.Text ?? string.Empty).ToList();

        Assert.Contains(shown, text => text.Contains("세이브의 열쇠", StringComparison.Ordinal));
        Assert.Contains(shown, text => text.Contains("초기값", StringComparison.Ordinal));
        Assert.Contains(shown, text => text.Contains("표시이름", StringComparison.Ordinal));
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 열려 있는 입력 플라이아웃의 내용 — 경고와 칸이 함께 산다.
    ///
    /// ⚠ <b>자리표시 글자</b>로 찾는다. 표의 줄도 `TextBox`+`TextBlock`을 들고 있어서
    /// 그것만으로 고르면 <b>조건 줄이 먼저 잡힌다</b>(한 번 잡혔다).
    /// </summary>
    private static Control Prompt(ChapterGraphView view) =>
        (TopLevel.GetTopLevel(view) as Window)!.GetVisualDescendants()
            .OfType<StackPanel>()
            .First(panel => panel.Children.OfType<TextBox>()
                .Any(box => !string.IsNullOrEmpty(box.PlaceholderText)));

    private static StackPanel Row(ChapterGraphView view, string tag) =>
        view.GetVisualDescendants().OfType<StackPanel>().Single(panel => Equals(panel.Tag, tag));

    private static IReadOnlyList<TextBox> Boxes(StackPanel row) =>
        row.Children.OfType<TextBox>().ToList();

    private static IReadOnlyList<Button> Buttons(StackPanel row) =>
        row.Children.OfType<Button>().ToList();

    /// <summary>Enter로 확정한다 — 초점을 잃는 길과 같은 자리로 간다.</summary>
    private static void Commit(TextBox box, string text)
    {
        box.Text = text;
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static void Press(Button button)
    {
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>열려 있는 확인 플라이아웃의 내용 — 목록과 「모두 비우고 지우기」가 산다.</summary>
    private static Control Confirm(ChapterGraphView view) =>
        (TopLevel.GetTopLevel(view) as Window)!.GetVisualDescendants()
            .OfType<StackPanel>()
            .First(panel => panel.GetVisualDescendants().OfType<Button>()
                .Any(button => (button.Content as string) == "모두 비우고 지우기"));

    private static void Redraw(ChapterGraphView view)
    {
        view.RefreshFromDisk();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// 스탯 `trust`와 그것을 쓰는 조건 `신뢰높음`이 선 챕터.
    ///
    /// ⚠ <b>워크북이 디스크에 있어야 한다</b> — 이 탭의 챕터 목록은 디스크를 읽은
    /// <c>_entries</c>에서 오므로, 모델에만 세운 챕터는 고를 수가 없다. 조건은 그 뒤에
    /// <b>편집기로</b> 더한다(= 사람이 이 탭에서 하는 길) — 표가 살아 있는 모델을 먼저
    /// 보는지까지 함께 재게 된다.
    /// </summary>
    private (ChapterGraphView View, AuthoringSession Session) Show()
    {
        ChapterWorkbookWriter.EnsureChapterWorkbook(
            Path.Combine(_directory, ChapterLibrary.FolderName), "ch01", [("trust", "신뢰")]);

        var session = new AuthoringSession();
        session.Open(ManifestPath);

        var view = new ChapterGraphView();
        var window = new Window { Width = 1400, Height = 800, Content = view };
        window.Show();
        view.Attach(session);
        view.RefreshFromDisk();

        // ⚠ 탭을 열어야 그 안의 컨트롤이 시각 트리에 올라온다 — 안 열면 표는 만들어져
        //    있는데 찾을 수가 없다.
        view.FindControl<TabItem>("ConditionTab")!.IsSelected = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        session.Editor.AddChapterCondition("ch01", "신뢰높음", "trust >= 3");
        Redraw(view);

        return (view, session);
    }
}
