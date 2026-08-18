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
        // 간선 하나가 선분 여럿(포트 꺾임)일 수 있다 — 표식(Tag)의 종류로 센다.
        Assert.Equal(5, canvas.Children.OfType<Line>()
            .Where(line => line.Tag is string)
            .Select(line => (string)line.Tag!)
            .Distinct(StringComparer.Ordinal)
            .Count());
    });

    [Fact]
    public void 그려진_노드는_깊이_열에_선다() => HeadlessUi.Run(() =>
    {
        // v3 — 배치는 깊이 레이아웃이 소유한다. 열 = 시작에서의 가장 긴 경로.
        // 견본의 흐름: 01 → 02 → (A) → 03 → end, A는 02의 분기이자 03으로 합류.
        using var project = new TempProject(SamplePath);
        (Canvas canvas, _) = Render(project);

        IReadOnlyDictionary<string, (double X, double Y)> placed = Placements(canvas);

        Assert.Equal(220, placed["main05.02"].X - placed["main05.01"].X);
        Assert.Equal(440, placed["branch05.02A"].X - placed["main05.01"].X);

        // 합류 노드는 가장 깊은 부모(A, 깊이 2) 다음 열이다 — 간선이 뒤로 꺾이지 않는다.
        Assert.Equal(660, placed["main05.03"].X - placed["main05.01"].X);
        Assert.Equal(880, placed["main05.end"].X - placed["main05.01"].X);

        // 간선 없는 부착 노드는 그래프 아래 줄에 따로 선다.
        Assert.True(
            placed["attach05.02s"].Y > placed["main05.01"].Y,
            $"attach05.02s(Y={placed["attach05.02s"].Y})는 본류(Y={placed["main05.01"].Y}) 아래여야 한다");
    });

    [Fact]
    public void 그려진_간선이_엑셀의_관계_그대로다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (Canvas canvas, _) = Render(project);

        string[] drawn = canvas.Children.OfType<Line>()
            .Where(line => line.Tag is string)   // 히트 선(무표식)은 제외
            .Select(line => (string)line.Tag!)
            .Distinct(StringComparer.Ordinal)    // 포트 꺾임 = 같은 간선의 선분 여럿
            .OrderBy(tag => tag, StringComparer.Ordinal)
            .ToArray();

        // 표식은 라벨까지 담는다 (2026-08-15 — 간선 신원 = 출발·도착·라벨).
        Assert.Equal(
            [
                "branch05.02A→main05.03",
                "main05.01→main05.02",
                "main05.02→branch05.02A [라루의 제안을 듣는다]",
                "main05.02→main05.03 [혼자 문을 연다]",
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
    public void 도달_불가가_원인_조건과_함께_검증_보고에_선다() => HeadlessUi.Run(() =>
    {
        // 규칙 개정 — 도달성 증명(G7)이 뷰에 붙으면서, 견본 챕터의 실제 도달 불가가
        // 화면에 뜬다. 에피소드 워크북이 없으면 스탯이 오르지 않으므로 신뢰높음(trust >= 3)이
        // 영원히 닫히고 branch05.02A에 닿을 수 없다 — 저작 시점에 잡히는 것이 이 레이어의 목적이다.
        using var project = new TempProject(SamplePath);
        (Canvas canvas, ChapterGraphView view) = Render(project);

        var expander = view.FindControl<Expander>("DiagnosticsExpander")!;

        // 오류가 있으면 저절로 펼쳐진다.
        Assert.True(expander.IsExpanded);
        Assert.Contains("오류 1", (string)expander.Header!);

        // 경고 4건 — 스탯 3개가 game.definition.json(기본값은 variables가 비어 있다)에
        // 없다는 것(`스탯` 시트가 "읽기전용 미러"라는 규격 그대로의 보고)과,
        // v11에서 견본의 엔딩 간선에 선 연출 노드가 아직 비어 있다는 것 하나다.
        Assert.Contains("경고 4", (string)expander.Header!);

        // 원인 조건까지 짚는다.
        var panel = view.FindControl<StackPanel>("DiagnosticsPanel")!;
        Assert.Contains(panel.Children.OfType<TextBlock>(), block =>
            block.Text?.Contains("branch05.02A") == true &&
            block.Text.Contains("trust >= 3"));

        // 도달 불가 노드는 그래프에서도 ⚠로 선다.
        Border card = canvas.Children.OfType<Border>()
            .Single(border => (border.Tag as string) == "branch05.02A");

        Assert.Contains(((StackPanel)card.Child!).Children.OfType<StackPanel>().Single()
            .Children.OfType<TextBlock>(), mark => mark.Text == "⚠");
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
            // 카드는 보이는 선택지 칸 수만큼 아래로 자란다 (포트 줄 18px).
            Assert.True(card.Bounds.Height >= CardHeight,
                $"카드 높이 {card.Bounds.Height} < 기본 {CardHeight}");
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

        project.Ui.Own(view, window);

        return (canvas, view);
    }

    /// <summary>견본을 chapters/ 아래 둔 임시 프로젝트. 뷰가 실제로 읽는 자리 그대로다.</summary>
    private sealed class TempProject : IDisposable
    {
        private readonly string _directory;

        /// <summary>이 테스트가 띄운 화면. 폴더를 지우기 <b>전에</b> 닫는다.</summary>
        public OpenChapterViews Ui { get; } = new();

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
            Ui.CloseAll();

            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
