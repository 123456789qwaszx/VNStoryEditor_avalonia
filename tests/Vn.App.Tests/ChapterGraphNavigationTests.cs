using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// 판 다루기 (2026-08-17 소유자: "챕터그래프에서도 컨트롤휠로 확대 축소 / 마우스 중간으로
/// 이동은 할 수 있게") — 연출 그래프와 같은 손놀림이어야 한다.
///
/// 2026-08-18 팀장 미팅에서 Ctrl 요구가 빠졌다: "그냥 휠만으로 확대축소가 가능해야하고".
/// 판 위에서 휠은 곧 배율이고, 세로로 훑는 일은 가운데 단추 끌기가 맡는다.
/// </summary>
public sealed class ChapterGraphNavigationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-chapter-nav", Guid.NewGuid().ToString("N"));

    private string ManifestPath => Path.Combine(_directory, "project" + ProjectManifestJson.FileExtension);

    public ChapterGraphNavigationTests()
    {
        Directory.CreateDirectory(_directory);

        string chapters = Path.Combine(_directory, ChapterLibrary.FolderName);
        ChapterWorkbookWriter.EnsureChapterWorkbook(chapters, "ch01", [("trust", "신뢰")]);
        string path = Path.Combine(chapters, "ch01.xlsx");

        // 뷰포트보다 넓은 판 — 이동이 실제로 일어날 자리가 있어야 한다.
        string previous = string.Empty;

        for (int index = 0; index < 8; index++)
        {
            string id = $"ep{index}";
            ChapterWorkbookWriter.AddEpisode(path, id, title: "", index, 0);

            if (previous.Length > 0)
            {
                ChapterWorkbookWriter.AddEdge(path, previous, id);
            }

            previous = id;
        }

        ProjectStore.Save(ManifestPath, new StoryProject { Title = "판 다루기 검증" });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 그냥_휠로_확대되고_배율이_보인다() => HeadlessUi.Run(() =>
    {
        (ChapterGraphView view, ScrollViewer scroll) = Show();

        Assert.False(view.FindControl<Button>("ZoomResetButton")!.IsVisible);

        Wheel(scroll, delta: 1, KeyModifiers.None);

        Assert.Equal(1.15, Scale(view), 3);
        Assert.True(view.FindControl<Button>("ZoomResetButton")!.IsVisible);
        Assert.Equal("115%", view.FindControl<TextBlock>("ZoomText")!.Text);
    });

    [Fact]
    public void 그냥_휠을_내리면_축소된다() => HeadlessUi.Run(() =>
    {
        // 예전에는 이 손놀림이 세로 스크롤이었다 — 이제는 반대 방향의 배율이다.
        (ChapterGraphView view, ScrollViewer scroll) = Show();

        Wheel(scroll, delta: -1, KeyModifiers.None);

        Assert.Equal(1 / 1.15, Scale(view), 3);
    });

    [Fact]
    public void 컨트롤을_눌러도_같은_배율이다() => HeadlessUi.Run(() =>
    {
        // Ctrl 요구는 빠졌지만 Ctrl+휠이 갑자기 다른 일을 해서는 안 된다 —
        // 손에 익은 사람이 그대로 써도 같은 결과여야 한다.
        (ChapterGraphView view, ScrollViewer scroll) = Show();

        Wheel(scroll, delta: 1, KeyModifiers.Control);

        Assert.Equal(1.15, Scale(view), 3);
    });

    [Fact]
    public void 배율은_경계를_넘지_않는다() => HeadlessUi.Run(() =>
    {
        (ChapterGraphView view, ScrollViewer scroll) = Show();

        for (int index = 0; index < 40; index++)
        {
            Wheel(scroll, delta: -1, KeyModifiers.None);
        }

        Assert.Equal(0.3, Scale(view), 3);

        for (int index = 0; index < 80; index++)
        {
            Wheel(scroll, delta: 1, KeyModifiers.Control);
        }

        Assert.Equal(2.5, Scale(view), 3);
    });

    [Fact]
    public void 배율_단추를_누르면_100퍼센트로_돌아온다() => HeadlessUi.Run(() =>
    {
        (ChapterGraphView view, ScrollViewer scroll) = Show();

        Wheel(scroll, delta: 1, KeyModifiers.Control);
        view.FindControl<Button>("ZoomResetButton")!.RaiseEvent(
            new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(1, Scale(view), 3);
        Assert.False(view.FindControl<Button>("ZoomResetButton")!.IsVisible);
    });

    [Fact]
    public void 가운데_단추를_끌면_판이_따라온다() => HeadlessUi.Run(() =>
    {
        (_, ScrollViewer scroll) = Show();

        Assert.True(scroll.Extent.Width > scroll.Viewport.Width, "판이 뷰포트보다 넓어야 한다");

        MiddlePress(scroll, new Point(300, 200));
        MiddleMove(scroll, new Point(250, 170));

        // 왼쪽으로 끌면 판은 오른쪽으로 — 오프셋이 끈 만큼 는다.
        Assert.Equal(50, scroll.Offset.X, 1);

        // ⚠ 세로는 **끈 만큼(30) 다 가지 못하고 판의 끝에서 멈춘다.** v12 전에는 아예 0에
        // 머물렀다(챕터 판이 한 줄로 뻗어 뷰포트보다 낮았다). 모든 길이 포트를 받으면서
        // 카드가 포트 줄만큼 아래로 자랐고, 그만큼 갈 곳이 생겼다.
        Assert.InRange(scroll.Offset.Y, 0, 30);

        // 단추를 떼면 더 움직여도 따라오지 않는다.
        MiddleRelease(scroll, new Point(250, 170));
        MiddleMove(scroll, new Point(100, 100));

        Assert.Equal(50, scroll.Offset.X, 1);
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static double Scale(ChapterGraphView view) =>
        view.FindControl<LayoutTransformControl>("ZoomHost")!.LayoutTransform is ScaleTransform scale
            ? scale.ScaleX
            : 1;

    private static void Wheel(ScrollViewer scroll, double delta, KeyModifiers modifiers) =>
        scroll.RaiseEvent(new PointerWheelEventArgs(
            scroll,
            new Pointer(0, PointerType.Mouse, true),
            scroll,
            new Point(200, 150),
            0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            modifiers,
            new Vector(0, delta))
        {
            RoutedEvent = InputElement.PointerWheelChangedEvent
        });

    private static void MiddlePress(ScrollViewer scroll, Point at) =>
        scroll.RaiseEvent(new PointerPressedEventArgs(
            scroll,
            new Pointer(0, PointerType.Mouse, true),
            scroll,
            at,
            0,
            new PointerPointProperties(
                RawInputModifiers.MiddleMouseButton, PointerUpdateKind.MiddleButtonPressed),
            KeyModifiers.None)
        {
            RoutedEvent = InputElement.PointerPressedEvent
        });

    private static void MiddleMove(ScrollViewer scroll, Point at) =>
        scroll.RaiseEvent(new PointerEventArgs(
            InputElement.PointerMovedEvent,
            scroll,
            new Pointer(0, PointerType.Mouse, true),
            scroll,
            at,
            0,
            new PointerPointProperties(
                RawInputModifiers.MiddleMouseButton, PointerUpdateKind.Other),
            KeyModifiers.None));

    private static void MiddleRelease(ScrollViewer scroll, Point at) =>
        scroll.RaiseEvent(new PointerReleasedEventArgs(
            scroll,
            new Pointer(0, PointerType.Mouse, true),
            scroll,
            at,
            0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.MiddleButtonReleased),
            KeyModifiers.None,
            MouseButton.Middle)
        {
            RoutedEvent = InputElement.PointerReleasedEvent
        });

    private (ChapterGraphView View, ScrollViewer Scroll) Show()
    {
        var session = new AuthoringSession();
        session.Open(ManifestPath);

        var view = new ChapterGraphView();
        var window = new Window { Width = 700, Height = 500, Content = view };
        window.Show();
        view.Attach(session);

        window.Measure(new Size(700, 500));
        window.Arrange(new Rect(0, 0, 700, 500));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return (view, view.FindControl<ScrollViewer>("GraphScroll")!);
    }
}
