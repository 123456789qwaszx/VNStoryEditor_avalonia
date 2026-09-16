using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// <b>노드 아래 선택지 칸 — 문구를 적어야 살아난다</b> (R7 P-2 · `docs/plans/R7.md` §2 ③).
///
/// 칸은 세 자리를 색으로 가른다: <b>텅 빈 회색</b>(아직 못 끈다) · <b>붉은색</b>(살아났다) ·
/// <b>초록색</b>(이어졌다).
///
/// ⭐ 예외가 하나 — 세 칸이 전부 비어 있으면 <b>첫 칸은 문구 없이도 살아 있다</b>.
/// 그것이 자동 길이고, 묻지 않고 지나가는 길에는 고를 것이 없으니 문구도 없는 것이 맞다.
/// </summary>
public sealed class ChoiceSlotTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-choice-slot", Guid.NewGuid().ToString("N"));

    private string ManifestPath => Path.Combine(_directory, "p" + ProjectManifestJson.FileExtension);

    public ChoiceSlotTests()
    {
        Directory.CreateDirectory(_directory);
        ProjectStore.Save(ManifestPath, new StoryProject { Title = "선택지 칸" });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 아무_문구도_없으면_첫_칸만_살아_있다() => HeadlessUi.Run(() =>
    {
        // ⭐ 그 하나가 자동 길 자리다 — 가장 흔한 "그냥 다음" 잇기에 타이핑이 필요 없다.
        (GraphEditorView graph, AuthoringSession session) = Show();

        IReadOnlyList<IBrush> dots = Dots(graph, session, "root");

        Assert.Equal(3, dots.Count);
        Assert.Equal(GraphEditorView.SlotLive, dots[0]);
        Assert.Equal(GraphEditorView.SlotAsleep, dots[1]);
        Assert.Equal(GraphEditorView.SlotAsleep, dots[2]);
    });

    [Fact]
    public void 문구를_적으면_그_칸이_붉어지고_자동_자리는_닫힌다() => HeadlessUi.Run(() =>
    {
        // ⚠ 자동 길은 그 에피소드의 <b>유일한 간선</b>이어야 한다(`AutoEdgeHasSiblings`) —
        //    선택지가 하나라도 서면 자동일 수가 없으므로 화면이 먼저 그 문을 닫는다.
        (GraphEditorView graph, AuthoringSession session) = Show();

        Write(graph, session, "root", slot: 1, "왼쪽으로");

        IReadOnlyList<IBrush> dots = Dots(graph, session, "root");

        Assert.Equal(GraphEditorView.SlotLive, dots[1]);
        Assert.Equal(GraphEditorView.SlotAsleep, dots[0]);   // 자동 자리가 닫혔다
    });

    [Fact]
    public void 이어진_칸은_초록이다() => HeadlessUi.Run(() =>
    {
        (GraphEditorView graph, AuthoringSession session) = Show();

        session.Editor.AddEdge("ch01", "root", "a", optionLabel: "왼쪽으로");
        graph.Rebuild();

        Assert.Equal(GraphEditorView.SlotJoined, Dots(graph, session, "root")[0]);
    });

    [Fact]
    public void 적은_문구는_챕터의_선택지_사전에_남는다() => HeadlessUi.Run(() =>
    {
        // 아직 이을 데가 없어 간선은 못 만들지만 <b>문구는 잃지 않는다</b> — 문구의 주인은
        // 챕터이고(v9 어휘집), 다음에 그 자리에서 고를 수 있어야 한다.
        (GraphEditorView graph, AuthoringSession session) = Show();

        Write(graph, session, "root", slot: 1, "왼쪽으로");

        Assert.Contains(
            session.Editor.FindChapter("ch01")!.ChoiceOptions,
            option => option.Text == "왼쪽으로");
    });

    [Fact]
    public void 이어진_길의_문구를_고치면_간선이_고쳐진다() => HeadlessUi.Run(() =>
    {
        // ⛔ 여기가 이 조각의 관문이다 — 화면이 고치는 것은 <b>챕터 간선</b>이지 연출 층의
        //    사본이 아니다(§2 ①). 정본이 하나여야 내보내기·검증이 그대로 돈다.
        (GraphEditorView graph, AuthoringSession session) = Show();

        session.Editor.AddEdge("ch01", "root", "a", optionLabel: "왼쪽으로");
        graph.Rebuild();

        Write(graph, session, "root", slot: 0, "오른쪽으로");

        ChapterEdge edge = Assert.Single(session.Editor.FindChapter("ch01")!.Edges);
        Assert.Equal("오른쪽으로", edge.OptionLabel);
    });

    [Fact]
    public void Esc를_누르면_없던_일이_된다() => HeadlessUi.Run(() =>
    {
        (GraphEditorView graph, AuthoringSession session) = Show();

        Press(Labels(graph, session, "root")[1]);
        graph.SlotLabelBox!.Text = "안 쓸 문구";
        Key(graph.SlotLabelBox, Avalonia.Input.Key.Escape);

        Assert.Empty(session.Editor.FindChapter("ch01")!.ChoiceOptions);
        Assert.Equal(GraphEditorView.SlotAsleep, Dots(graph, session, "root")[1]);
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <summary>그 노드의 선택지 칸 머리점들 — 그린 차례대로.</summary>
    private static IReadOnlyList<IBrush> Dots(GraphEditorView graph, AuthoringSession session, string nodeName) =>
        Rows(graph, session, nodeName)
            .Select(row => row.Children.OfType<Ellipse>().Single().Stroke!)
            .ToList();

    private static IReadOnlyList<TextBlock> Labels(GraphEditorView graph, AuthoringSession session, string nodeName) =>
        Rows(graph, session, nodeName)
            .Select(row => row.Children.OfType<TextBlock>().SingleOrDefault())
            .Where(label => label is not null)
            .Select(label => label!)
            .ToList();

    /// <summary>선택지 칸 = 머리점(<see cref="Ellipse"/>)이 있는 줄.</summary>
    private static IReadOnlyList<Grid> Rows(GraphEditorView graph, AuthoringSession session, string nodeName) =>
        graph.FindControl<Canvas>("GraphCanvas")!.Children.OfType<Border>()
            .First(card => card.Tag as string == NodeId(session, nodeName))
            .GetVisualDescendants().OfType<Grid>()
            .Where(row => row.Children.OfType<Ellipse>().Any())
            .ToList();

    /// <summary>카드는 Tag에 NodeId를 진다 — 이름으로 찾으면 헤더 글월에 매인다.</summary>
    private static string NodeId(AuthoringSession session, string nodeName) =>
        session.Project.EnumerateNodes().First(node => node.Name == nodeName).Id;


    /// <summary>그 칸에 문구를 적어 넣는다 — 사람이 하는 길 그대로다.</summary>
    private static void Write(GraphEditorView graph, AuthoringSession session, string nodeName, int slot, string text)
    {
        Press(Labels(graph, session, nodeName)[slot]);

        graph.SlotLabelBox!.Text = text;
        Key(graph.SlotLabelBox, Avalonia.Input.Key.Enter);
    }

    private static void Press(Control control)
    {
        var root = (Visual)TopLevel.GetTopLevel(control)!;

        control.RaiseEvent(new PointerPressedEventArgs(
            control, new Pointer(0, PointerType.Mouse, true), root, default, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None));

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static void Key(Control control, Avalonia.Input.Key key)
    {
        control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>ch01 — root · a, 각각 대사 노드가 선 판.</summary>
    private (GraphEditorView Graph, AuthoringSession Session) Show()
    {
        var session = new AuthoringSession();
        session.Open(ManifestPath);

        session.Editor.EnsureChapter("ch01");
        string fileId = session.EnsureChapterBoard("ch01");

        foreach (string episodeId in (string[])["root", "a"])
        {
            session.Editor.AddEpisode("ch01", episodeId, title: episodeId, 0, 0);
            session.Editor.AddDialogueNode(fileId, name: episodeId).ExcelEpisodeId = episodeId;
        }

        // ⚠ 판을 펴야 카드가 선다 — 접힌 판은 표 프록시 하나로 온다.
        session.SetFileExpanded(fileId, expanded: true);

        var graph = new GraphEditorView();
        var window = new Window { Width = 1400, Height = 900, Content = graph };
        window.Show();
        graph.Attach(session);
        graph.Rebuild();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return (graph, session);
    }
}
