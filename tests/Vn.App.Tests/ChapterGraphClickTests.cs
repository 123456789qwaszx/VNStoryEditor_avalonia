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
using Vn.Authoring.Editing;
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
        (Window window, Canvas canvas, ChapterGraphView view, _) = Show(project);

        Border before = Card(canvas, "main05.02");
        Click(window, CardCenter(window, canvas, "main05.02"));

        // 선택은 됐고 —
        Assert.True(view.FindControl<StackPanel>("PropertyPanel")!.IsVisible);
        Assert.Equal("main05.02", view.FindControl<TextBox>("IdBox")!.Text);

        // — 카드는 같은 인스턴스 그대로다. 누른 손 밑에서 캔버스를 다시 만들면
        // 드래그·더블클릭이 죽는다(원래 결함의 뿌리).
        Assert.Contains(before, canvas.Children.OfType<Border>());
        Assert.Equal(new Avalonia.Thickness(2.4), before.BorderThickness);
    });

    [Fact]
    public void 카드_더블클릭이_에피소드_워크북을_연다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (Window window, Canvas canvas, ChapterGraphView view, _) = Show(project);

        var opened = new List<string>();
        view.OpenWorkbookFile = opened.Add;
        view.WorkbookHandlerProbe = () => @"C:\Program Files\Microsoft Office\EXCEL.EXE";

        Avalonia.Point center = CardCenter(window, canvas, "main05.02");
        Click(window, center);
        Click(window, center);

        Assert.Contains(opened, path => path.Contains("main05.02"));
    });

    [Fact]
    public void 기본_앱이_스프레드시트가_아니면_열지_않고_폴더를_보여준다() => HeadlessUi.Run(() =>
    {
        // 실사례 — .xlsx가 챗지피티에 연결된 기계에서 더블클릭이 챗지피티를 열었다.
        // 편집할 수 없는 앱에 워크북을 던지지 않는다: 폴더에서 보여 주고 사유를 말한다.
        using var project = new TempProject(SamplePath);
        (Window window, Canvas canvas, ChapterGraphView view, _) = Show(project);

        var opened = new List<string>();
        var revealed = new List<string>();
        view.OpenWorkbookFile = opened.Add;
        view.RevealInFolder = revealed.Add;
        view.WorkbookHandlerProbe = () => @"C:\Users\me\AppData\Local\ChatGPT\ChatGPT.exe";

        Avalonia.Point center = CardCenter(window, canvas, "main05.02");
        Click(window, center);
        Click(window, center);

        Assert.Empty(opened);
        Assert.Contains(revealed, path => path.Contains("main05.02"));
    });

    [Fact]
    public void 간선_라벨_클릭이_간선을_선택한다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (Window window, Canvas canvas, ChapterGraphView view, _) = Show(project);

        // 문구는 이제 카드 오른변의 포트 문구다(선택지 시트의 보이는 칸, 2026-08-16) —
        // 사람이 간선을 누르려고 겨누는 자리이고, 누르면 그 간선이 선택된다.
        TextBlock label = canvas.Children.OfType<TextBlock>().Single(block =>
            block.Text?.Contains("라루의 제안") == true);

        Avalonia.Point center = label.TranslatePoint(
            new Avalonia.Point(label.Bounds.Width / 2, label.Bounds.Height / 2), window)!.Value;
        Click(window, center);

        Assert.True(view.FindControl<StackPanel>("EdgePanel")!.IsVisible);
        Assert.Contains("라루의 제안을 듣는다",
            (string)view.FindControl<ComboBox>("EdgeLabelEditBox")!.SelectedItem!); // 짝 칸 표시
    });

    [Fact]
    public void 빈_공간_클릭이_선택을_푼다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (Window window, Canvas canvas, ChapterGraphView view, _) = Show(project);

        view.SelectEpisode("main05.02");
        Assert.True(view.FindControl<StackPanel>("PropertyPanel")!.IsVisible);

        // 캔버스 왼쪽 위 (5,5)는 배치 여백(60px) 안이라 어떤 카드·간선도 없다.
        Click(window, canvas.TranslatePoint(new Avalonia.Point(5, 5), window)!.Value);

        Assert.False(view.FindControl<StackPanel>("PropertyPanel")!.IsVisible);
        Assert.True(view.FindControl<TextBlock>("NoSelectionText")!.IsVisible);
    });

    [Fact]
    public void 카드를_끌면_그_자리가_적히고_되돌리기_한_번으로_돌아온다() => HeadlessUi.Run(() =>
    {
        // v4 (2026-09-18 소유자) — 자리의 주인이 깊이 배치에서 사람으로 넘어왔다.
        using var project = new TempProject(SamplePath);
        (Window window, Canvas canvas, ChapterGraphView view, AuthoringSession session) =
            Show(project);

        ChapterEpisode Episode(string id) =>
            session.Editor.FindChapter("ch05")!.Episodes.Single(e => e.EpisodeId == id);

        // ⚠ 절대값이 아니라 <b>카드 사이의 거리</b>로 잰다. 견본에는 음수 자리가 있어
        //   (branch05.02A의 Y=-120) 처음 옮길 때 판 전체가 안쪽으로 밀리기 때문이다 —
        //   모양은 그대로이고 원점만 바뀐다.
        (double X, double Y) Gap() =>
            (Episode("main05.02").X - Episode("main05.01").X,
             Episode("main05.02").Y - Episode("main05.01").Y);

        (double X, double Y) before = Gap();

        Drag(window, CardCenter(window, canvas, "main05.02"), new Vector(120, 60));

        Assert.Equal((before.X + 120, before.Y + 60), Gap());

        // 드래그 한 번 = 되돌리기 한 번. 포인터가 움직일 때마다 적었다면 여기서 한 번
        // 물러도 끌던 도중의 자리로 돌아올 뿐 제자리로는 못 온다.
        session.Editor.Undo();

        Assert.Equal(before, Gap());
    });

    [Fact]
    public void 누르기만_한_카드는_자리를_안_적는다() => HeadlessUi.Run(() =>
    {
        // 고르기만 해도 되돌리기가 쌓이면 Ctrl+Z가 아무 일도 안 하는 것처럼 보인다.
        using var project = new TempProject(SamplePath);
        (Window window, Canvas canvas, ChapterGraphView view, AuthoringSession session) =
            Show(project);

        var kinds = new List<ProjectChangeKind>();
        session.Editor.Changed += (_, args) => kinds.Add(args.Kind);

        Click(window, CardCenter(window, canvas, "main05.02"));

        Assert.True(view.FindControl<StackPanel>("PropertyPanel")!.IsVisible); // 골라지긴 했다
        Assert.DoesNotContain(ProjectChangeKind.NodeMetadata, kinds);
    });

    [Fact]
    public void 한_점에_포개진_챕터는_빌린_자리로_그리고_첫_옮기기에_적는다() => HeadlessUi.Run(() =>
    {
        // v3까지 자리는 그릴 때마다 계산됐으므로 X·Y를 아무도 안 채웠다 — 엑셀에서 들여온
        // 챕터가 정확히 이 모양이다. 그대로 그리면 판이 카드 한 장처럼 보인다.
        using var project = new TempProject(SamplePath);
        (Window window, Canvas canvas, ChapterGraphView view, AuthoringSession session) =
            Show(project);

        List<string> ids = session.Editor.FindChapter("ch05")!.Episodes
            .Select(episode => episode.EpisodeId).ToList();

        session.Editor.MoveEpisodes("ch05", ids.ToDictionary(id => id, _ => (0.0, 0.0)));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // ① 판을 열었다고 프로젝트에 적지는 않는다 — 값은 아직 전부 (0,0)이다.
        Assert.All(session.Editor.FindChapter("ch05")!.Episodes,
            episode => Assert.Equal((0.0, 0.0), (episode.X, episode.Y)));

        // ② 그래도 카드는 흩어져 보인다 — 빌린 배치다.
        List<double> lefts = ids.Select(id => Canvas.GetLeft(Card(canvas, id))).ToList();
        Assert.True(lefts.Distinct().Count() > 1);

        // ③ 사람이 처음 옮기는 그 손과 함께 판 전체가 적힌다. 하나만 적으면 나머지는
        //    다음 그리기에서 도로 한 점에 포개진다.
        Drag(window, CardCenter(window, canvas, "main05.02"), new Vector(40, 40));

        Assert.False(ChapterBranchPlanner.NeedsSeeding(
            session.Editor.FindChapter("ch05")!.ToGraphModel("ch05.xlsx", session.Definition)));

        // ④ 한 번의 변경이라 한 번에 물러난다.
        session.Editor.Undo();

        Assert.All(session.Editor.FindChapter("ch05")!.Episodes,
            episode => Assert.Equal((0.0, 0.0), (episode.X, episode.Y)));
    });

    [Fact]
    public void 자동_정렬은_한_번의_되돌리기다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (Window window, Canvas canvas, ChapterGraphView view, AuthoringSession session) =
            Show(project);

        ChapterEpisode Episode() =>
            session.Editor.FindChapter("ch05")!.Episodes.Single(e => e.EpisodeId == "main05.02");

        Drag(window, CardCenter(window, canvas, "main05.02"), new Vector(300, 200));

        (double X, double Y) dragged = (Episode().X, Episode().Y);

        view.FindControl<Button>("AutoArrangeButton")!.RaiseEvent(
            new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotEqual(dragged, (Episode().X, Episode().Y));

        // 자리의 주인이 사람이 되면 돌아올 길이 있어야 한다 — 그 길도 한 번에 물러야 한다.
        session.Editor.Undo();

        Assert.Equal(dragged, (Episode().X, Episode().Y));
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 카드 끌기 — 누르고, 왼쪽 단추를 누른 채 한 번 움직이고, 놓는다 (v4).
    ///
    /// ⚠ <b>움직임에 단추 상태를 실어야 한다.</b> 안 실으면 화면은 "놓았다"로 읽고 첫
    /// 움직임에서 드래그를 끝낸다 — 실제 마우스가 보내는 것과 같은 모양이어야 한다.
    /// </summary>
    private static void Drag(Window window, Avalonia.Point from, Vector delta)
    {
        Avalonia.Point to = from + delta;

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

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

    private static (Window Window, Canvas Canvas, ChapterGraphView View, AuthoringSession Session) Show(
        TempProject project)
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

        project.Ui.Own(view, window);

        return (window, canvas, view, session);
    }

    private sealed class TempProject : IDisposable
    {
        private readonly string _directory;

        /// <summary>이 테스트가 띄운 화면. 폴더를 지우기 <b>전에</b> 닫는다.</summary>
        public OpenChapterViews Ui { get; } = new();

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
            Ui.CloseAll();

            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
