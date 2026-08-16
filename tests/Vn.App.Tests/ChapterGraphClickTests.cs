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
        (Window window, Canvas canvas, ChapterGraphView view) = Show(project);

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
        (Window window, Canvas canvas, ChapterGraphView view) = Show(project);

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
        (Window window, Canvas canvas, ChapterGraphView view) = Show(project);

        // 문구는 이제 카드 오른변의 포트 문구다(선택지 시트의 보이는 칸, 2026-08-16) —
        // 사람이 간선을 누르려고 겨누는 자리이고, 누르면 그 간선이 선택된다.
        TextBlock label = canvas.Children.OfType<TextBlock>().Single(block =>
            block.Text?.Contains("라루의 제안") == true);

        Avalonia.Point center = label.TranslatePoint(
            new Avalonia.Point(label.Bounds.Width / 2, label.Bounds.Height / 2), window)!.Value;
        Click(window, center);

        Assert.True(view.FindControl<StackPanel>("EdgePanel")!.IsVisible);
        Assert.Equal("1", view.FindControl<ComboBox>("EdgeLabelEditBox")!.SelectedItem); // 선택지수
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
