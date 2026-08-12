using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// 챕터 v2 6단계 — "클릭이 안 먹힌다" 회귀 검증. 헤드리스 창에 진짜 포인터 이벤트를 넣어
/// 카드 선택·더블클릭 열기·간선 라벨 클릭·빈 공간 해제·드래그 커밋을 화면 없이 못 박는다.
/// (OS 전역 입력 주입이 아니다 — 이 창 안의 이벤트일 뿐, 커서는 움직이지 않는다.)
///
/// 원래 결함: 카드의 PointerPressed가 선택하며 캔버스를 통째로 다시 만들어, 방금 누른 카드가
/// 파괴됐다. 그래서 더블클릭(둘째 탭이 다른 인스턴스에 떨어짐)과 드래그(캡처가 죽은 카드에
/// 걸림)가 안 먹혔고, 간선 라벨은 히트 선 위에서 클릭을 삼켰다.
/// </summary>
public sealed class ChapterGraphClickTests
{
    private const double CardWidth = 190;
    private const double CardHeight = 74;

    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    [Fact]
    public void 카드를_클릭하면_캔버스를_다시_만들지_않고_선택된다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (Window window, Canvas canvas, ChapterGraphView view) = Show(project);

        Border before = Card(canvas, "main05.02");
        Click(window, CardCenter(window, canvas, "main05.02"));

        // 선택은 됐고 —
        Assert.True(view.FindControl<StackPanel>("PropertyPanel")!.IsVisible);
        Assert.Equal("조용한 복도", view.FindControl<TextBox>("TitleBox")!.Text);

        // — 카드는 같은 인스턴스 그대로다. 누른 손 밑에서 캔버스를 다시 만들면
        // 드래그·더블클릭이 죽는다(원래 결함의 뿌리).
        Assert.Contains(before, canvas.Children.OfType<Border>());
        Assert.Equal(new Avalonia.Thickness(2.4), before.BorderThickness);
    });

    [Fact]
    public void 카드_더블클릭이_에피소드_워크북을_연다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (Window window, Canvas canvas, ChapterGraphView view) = Show(project);

        var opened = new List<string>();
        view.OpenWorkbookFile = opened.Add;

        Avalonia.Point center = CardCenter(window, canvas, "main05.02");
        Click(window, center);
        Click(window, center);

        Assert.Contains(opened, path => path.Contains("main05.02"));
    });

    [Fact]
    public void 간선_라벨_클릭이_간선을_선택한다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (Window window, Canvas canvas, ChapterGraphView view) = Show(project);

        // 라벨은 간선 한가운데 — 사람이 간선을 누르려고 겨누는 자리다. 원래는 라벨이
        // 히트 선을 덮고 아무것도 하지 않아 "클릭이 안 먹히는" 자리였다.
        Border label = canvas.Children.OfType<Border>().Single(border =>
            border.Tag is null &&
            (border.Child as TextBlock)?.Text?.Contains("라루의 제안") == true);

        Avalonia.Point center = label.TranslatePoint(
            new Avalonia.Point(label.Bounds.Width / 2, label.Bounds.Height / 2), window)!.Value;
        Click(window, center);

        Assert.True(view.FindControl<StackPanel>("EdgePanel")!.IsVisible);
        Assert.Equal("라루의 제안을 듣는다", view.FindControl<TextBox>("EdgeLabelEditBox")!.Text);
    });

    [Fact]
    public void 빈_공간_클릭이_선택을_푼다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (Window window, Canvas canvas, ChapterGraphView view) = Show(project);

        view.SelectEpisode("main05.02");
        Assert.True(view.FindControl<StackPanel>("PropertyPanel")!.IsVisible);

        // 캔버스 왼쪽 위 (5,5)는 배치 여백(60px) 안이라 어떤 카드·간선도 없다.
        Click(window, canvas.TranslatePoint(new Avalonia.Point(5, 5), window)!.Value);

        Assert.False(view.FindControl<StackPanel>("PropertyPanel")!.IsVisible);
        Assert.True(view.FindControl<TextBlock>("NoSelectionText")!.IsVisible);
    });

    [Fact]
    public void 드래그로_놓으면_엑셀_좌표가_바뀐다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (Window window, Canvas canvas, _) = Show(project);

        ChapterEpisode before =
            ChapterWorkbookReader.Read(project.ChapterPath).FindEpisode("main05.01")!;

        Avalonia.Point start = CardCenter(window, canvas, "main05.01");
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Avalonia.Point(start.X + 40, start.Y + 25));
        window.MouseUp(new Avalonia.Point(start.X + 40, start.Y + 25), MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ChapterEpisode after =
            ChapterWorkbookReader.Read(project.ChapterPath).FindEpisode("main05.01")!;

        Assert.Equal(before.X + 40, after.X);
        Assert.Equal(before.Y + 25, after.Y);
    });

    [Fact]
    public void 드래그_중에_간선이_카드를_따라온다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (Window window, Canvas canvas, _) = Show(project);

        Line edge = canvas.Children.OfType<Line>()
            .Single(line => (line.Tag as string) == "main05.01→main05.02");
        Avalonia.Point before = edge.StartPoint;

        // 누르고 끄는 중 — 아직 놓지 않았다. 간선이 카드 중심을 따라와야 그래프가 찢어져
        // 보이지 않는다(엑셀 쓰기는 여전히 놓는 순간 한 번뿐).
        Avalonia.Point start = CardCenter(window, canvas, "main05.01");
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Avalonia.Point(start.X + 50, start.Y + 30));

        Assert.Equal(before.X + 50, edge.StartPoint.X, 3);
        Assert.Equal(before.Y + 30, edge.StartPoint.Y, 3);

        window.MouseUp(new Avalonia.Point(start.X + 50, start.Y + 30), MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static void Click(Window window, Avalonia.Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static Border Card(Canvas canvas, string episodeId) =>
        canvas.Children.OfType<Border>().Single(border => (border.Tag as string) == episodeId);

    private static Avalonia.Point CardCenter(Window window, Canvas canvas, string episodeId) =>
        Card(canvas, episodeId).TranslatePoint(
            new Avalonia.Point(CardWidth / 2, CardHeight / 2), window)!.Value;

    private static (Window Window, Canvas Canvas, ChapterGraphView View) Show(TempProject project)
    {
        var session = new AuthoringSession();
        session.Open(project.ManifestPath);

        var view = new ChapterGraphView();
        var window = new Window { Width = 1400, Height = 800, Content = view };
        window.Show();
        view.Attach(session);

        // 히트 테스트는 배치가 돌아야 진짜다 — 렌더 검증과 같은 두 단계 배치.
        window.Measure(new Avalonia.Size(1400, 800));
        window.Arrange(new Avalonia.Rect(0, 0, 1400, 800));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Canvas canvas = view.FindControl<Canvas>("GraphCanvas")!;
        canvas.Measure(new Avalonia.Size(canvas.Width, canvas.Height));
        canvas.Arrange(new Avalonia.Rect(0, 0, canvas.Width, canvas.Height));

        return (window, canvas, view);
    }

    private sealed class TempProject : IDisposable
    {
        private readonly string _directory;

        public TempProject(string samplePath)
        {
            _directory = Path.Combine(
                Path.GetTempPath(), "vn-chapter-click", Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path.Combine(_directory, ChapterLibrary.FolderName));
            File.Copy(samplePath, ChapterPath);

            ManifestPath = Path.Combine(_directory, "project" + ProjectManifestJson.FileExtension);
            ProjectStore.Save(ManifestPath, new StoryProject { Title = "클릭 검증" });
        }

        public string ManifestPath { get; }

        public string ChapterPath =>
            Path.Combine(_directory, ChapterLibrary.FolderName, "ch05.xlsx");

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
