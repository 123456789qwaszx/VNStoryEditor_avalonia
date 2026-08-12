using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// Gate A 1번 — "견본 워크북을 프로젝트에 넣으면 그래프가 엑셀에 적힌 X·Y 위치와 간선 관계
/// 그대로 그려진다"를 <b>사람 눈 없이</b> 닫는다.
///
/// 창을 띄우고 좌표를 찍어 클릭하는 방식은 쓰지 않는다 — 창이 앞으로 올라왔는지 확인할 수 없어
/// 남의 창을 누르게 된다. 헤드리스로 진짜 시각 트리를 만들고 배치를 돌린 뒤, 그려진 것을
/// 이름으로 확인한다.
/// </summary>
public sealed class ChapterGraphViewRenderTests
{
    private const double CardWidth = 190;
    private const double CardHeight = 74;

    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    [Fact]
    public void 견본_챕터가_6노드_5간선으로_그려진다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (Canvas canvas, _) = Render(project);

        Assert.Equal(6, NodeCards(canvas).Count);
        Assert.Equal(5, canvas.Children.OfType<Line>().Count());
    });

    [Fact]
    public void 그려진_노드의_상대_위치가_엑셀의_X_Y와_같다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (Canvas canvas, _) = Render(project);

        IReadOnlyDictionary<string, (double X, double Y)> placed = Placements(canvas);

        // 지시서가 콕 집은 것 — 신뢰 분기가 문 너머보다 위에 있어야 한다 (Y가 작다).
        Assert.True(
            placed["branch05.02A"].Y < placed["main05.03"].Y,
            $"branch05.02A(Y={placed["branch05.02A"].Y})가 " +
            $"main05.03(Y={placed["main05.03"].Y})보다 위여야 한다");

        // 엑셀의 간격이 화면에서도 그대로다. 평행이동 하나뿐이므로 차이가 보존된다.
        Assert.Equal(220, placed["main05.02"].X - placed["main05.01"].X);
        Assert.Equal(440, placed["main05.03"].X - placed["main05.01"].X);
        Assert.Equal(680, placed["main05.end"].X - placed["main05.01"].X);
        Assert.Equal(240, placed["main05.03"].Y - placed["branch05.02A"].Y);
        Assert.Equal(170, placed["attach05.02s"].Y - placed["main05.01"].Y);

        // 같은 X를 가진 둘은 화면에서도 같은 X다 (자동 레이아웃이 끼어들지 않았다).
        Assert.Equal(placed["branch05.02A"].X, placed["main05.03"].X);
    });

    [Fact]
    public void 그려진_간선이_엑셀의_관계_그대로다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (Canvas canvas, _) = Render(project);

        string[] drawn = canvas.Children.OfType<Line>()
            .Select(line => (string)line.Tag!)
            .OrderBy(tag => tag, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "branch05.02A→main05.03",
                "main05.01→main05.02",
                "main05.02→branch05.02A",
                "main05.02→main05.03",
                "main05.03→main05.end"
            ],
            drawn);
    });

    [Fact]
    public void 간선은_두_노드의_중심을_잇는다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (Canvas canvas, _) = Render(project);

        IReadOnlyDictionary<string, (double X, double Y)> placed = Placements(canvas);
        Line edge = canvas.Children.OfType<Line>()
            .Single(line => (string)line.Tag! == "main05.01→main05.02");

        Assert.Equal(placed["main05.01"].X + (CardWidth / 2), edge.StartPoint.X, 3);
        Assert.Equal(placed["main05.01"].Y + (CardHeight / 2), edge.StartPoint.Y, 3);
        Assert.Equal(placed["main05.02"].X + (CardWidth / 2), edge.EndPoint.X, 3);
        Assert.Equal(placed["main05.02"].Y + (CardHeight / 2), edge.EndPoint.Y, 3);
    });

    [Fact]
    public void 오류가_없으면_검증_보고가_접힌_채로_건수를_말한다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (_, ChapterGraphView view) = Render(project);

        var expander = view.FindControl<Expander>("DiagnosticsExpander")!;

        // 오류가 없으면 저절로 펼치지 않는다 — 경고·알림 때문에 매번 펼쳐지면 배너가 된다.
        Assert.False(expander.IsExpanded);

        // 견본의 경고 3건은 스탯 3개가 game.definition.json(기본값은 variables가 비어 있다)에
        // 없다는 것뿐이다. `스탯` 시트가 "읽기전용 미러"라는 규격 그대로의 보고다.
        Assert.Equal("검증 보고 — 오류 0 · 경고 3", (string)expander.Header!);
    });

    [Fact]
    public void 노드_카드는_배치_뒤_실제_크기를_갖는다() => HeadlessUi.Run(() =>
    {
        // "그려졌다"의 최소 조건 — 배치가 돌아 카드가 0×0이 아니어야 한다.
        using var project = new TempProject(SamplePath);
        (Canvas canvas, _) = Render(project);

        Assert.All(NodeCards(canvas), card =>
        {
            Assert.Equal(CardWidth, card.Bounds.Width);
            Assert.Equal(CardHeight, card.Bounds.Height);
        });
    });

    [Fact]
    public void 챕터_워크북이_없으면_어디에_넣으라고_알려_준다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(samplePath: null);
        (Canvas canvas, ChapterGraphView view) = Render(project);

        var empty = view.FindControl<TextBlock>("EmptyText")!;

        Assert.Empty(canvas.Children);
        Assert.True(empty.IsVisible);
        Assert.Contains(ChapterLibrary.FolderName, empty.Text);
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static List<Border> NodeCards(Canvas canvas) =>
        canvas.Children.OfType<Border>().Where(border => border.Tag is string).ToList();

    private static IReadOnlyDictionary<string, (double X, double Y)> Placements(Canvas canvas) =>
        NodeCards(canvas).ToDictionary(
            card => (string)card.Tag!,
            card => (Canvas.GetLeft(card), Canvas.GetTop(card)),
            StringComparer.Ordinal);

    private static (Canvas Canvas, ChapterGraphView View) Render(TempProject project)
    {
        var session = new AuthoringSession();
        session.Open(project.ManifestPath);

        var view = new ChapterGraphView();
        var window = new Window { Width = 1280, Height = 800, Content = view };
        window.Show();

        view.Attach(session);

        // 배치를 실제로 돌린다 — 이걸 하지 않으면 Bounds가 전부 0이고 "그려졌다"가 거짓이 된다.
        window.Measure(new Avalonia.Size(1280, 800));
        window.Arrange(new Avalonia.Rect(0, 0, 1280, 800));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 캔버스는 스크롤 안이라 창 배치만으로는 자기 크기까지 펴지지 않는다. 그린 판 전체를
        // 대상으로 한 번 더 돌려야 카드가 실제 크기를 갖는다.
        Canvas canvas = view.FindControl<Canvas>("GraphCanvas")!;
        canvas.Measure(new Avalonia.Size(canvas.Width, canvas.Height));
        canvas.Arrange(new Avalonia.Rect(0, 0, canvas.Width, canvas.Height));

        return (canvas, view);
    }

    /// <summary>견본을 chapters/ 아래 둔 임시 프로젝트. 뷰가 실제로 읽는 자리 그대로다.</summary>
    private sealed class TempProject : IDisposable
    {
        private readonly string _directory;

        public TempProject(string? samplePath)
        {
            _directory = Path.Combine(
                Path.GetTempPath(), "vn-chapter-render", Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(_directory);

            if (samplePath is not null)
            {
                Directory.CreateDirectory(Path.Combine(_directory, ChapterLibrary.FolderName));
                File.Copy(samplePath, Path.Combine(_directory, ChapterLibrary.FolderName, "ch05.xlsx"));
            }

            ManifestPath = Path.Combine(_directory, "project" + ProjectManifestJson.FileExtension);
            ProjectStore.Save(ManifestPath, new StoryProject { Title = "렌더 검증" });
        }

        public string ManifestPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
