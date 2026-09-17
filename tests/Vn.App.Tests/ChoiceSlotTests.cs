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

    [Fact]
    public void 꺼진_점을_눌러도_켜지지_않고_문구_칸만_열린다() => HeadlessUi.Run(() =>
    {
        // ⛔ <b>살리는 것은 적는 행위다</b> (2026-09-17 소유자). 점을 눌러 켜는 길을 잠깐
        //    뒀다가 물렸다 — 켜기와 적기가 갈라지면 손짓이 둘이 되고, 켜 두고 안 적은 칸이
        //    뜻 없이 붉게 남는다. 점은 <b>적을 자리로 데려가기만</b> 한다.
        (GraphEditorView graph, AuthoringSession session) = Show();

        Press(Rows(graph, session, "root")[1].Children.OfType<Ellipse>().Single());

        Assert.NotNull(graph.SlotLabelBox);
        Assert.Equal(GraphEditorView.SlotAsleep, Dots(graph, session, "root")[1]);
    });

    [Fact]
    public void 문구를_비우면_도로_꺼진다() => HeadlessUi.Run(() =>
    {
        (GraphEditorView graph, AuthoringSession session) = Show();

        Write(graph, session, "root", slot: 1, "왼쪽으로");
        Assert.Equal(GraphEditorView.SlotLive, Dots(graph, session, "root")[1]);

        Write(graph, session, "root", slot: 1, string.Empty);
        Assert.Equal(GraphEditorView.SlotAsleep, Dots(graph, session, "root")[1]);
    });

    [Fact]
    public void 길을_끊어도_문구는_칸에_남는다() => HeadlessUi.Run(() =>
    {
        // 길을 끊는 것과 <b>무엇을 물을지 정한 것</b>은 다른 일이다 — 다시 이을 때 안 친다.
        (GraphEditorView graph, AuthoringSession session) = Show();

        session.Editor.AddEdge("ch01", "root", "a", optionLabel: "왼쪽으로");
        graph.Rebuild();

        Drag(graph, session, "root", slot: 0, onto: null);

        Assert.Empty(session.Editor.FindChapter("ch01")!.Edges);
        Assert.Equal("왼쪽으로", Labels(graph, session, "root")[0].Text);
        Assert.Equal(GraphEditorView.SlotLive, Dots(graph, session, "root")[0]);
    });

    [Fact]
    public void 사람이_안_만든_점은_안_선다() => HeadlessUi.Run(() =>
    {
        // ⛔ 대본에서 파생되던 "진행"·구판 OPTION 스텁을 걷었다 (결정 ②) — 선택지를 긋는
        //    자리가 이 화면이 된 지금은 사람이 만들지 않은 점이 섞여 혼란스럽다.
        (GraphEditorView graph, AuthoringSession session) = Show();

        Assert.Equal(3, Rows(graph, session, "root").Count);
        Assert.DoesNotContain(Texts(graph), text => text.Contains("진행", StringComparison.Ordinal));
    });

    [Fact]
    public void 안_이은_칸_옆에는_종료가_안_붙는다() => HeadlessUi.Run(() =>
    {
        // 갈 곳이 없는 것과 거기서 끝나는 것은 다르다 — 빈 칸은 아직 아무 말도 안 한다.
        (GraphEditorView graph, AuthoringSession session) = Show();

        Assert.DoesNotContain(Texts(graph), text => text.Contains("종료", StringComparison.Ordinal));
    });

    // ── 끌어 잇기 (R7 P-3) ─────────────────────────────────────────────────

    [Fact]
    public void 문구를_적고_끌어다_놓으면_챕터_간선이_생긴다() => HeadlessUi.Run(() =>
    {
        // ⛔ 이 조각의 관문 — 화면이 만드는 것은 <b>챕터 간선</b>이다(§2 ①). 그래야
        //    [챕터 그래프]에도 같은 길이 보이고 내보내기가 그대로 돈다.
        (GraphEditorView graph, AuthoringSession session) = Show();

        Write(graph, session, "root", slot: 1, "왼쪽으로");
        Drag(graph, session, "root", slot: 1, onto: "a");

        ChapterEdge edge = Assert.Single(session.Editor.FindChapter("ch01")!.Edges);

        Assert.Equal("root", edge.FromEpisodeId);
        Assert.Equal("a", edge.ToEpisodeId);
        Assert.Equal("왼쪽으로", edge.OptionLabel);
        Assert.False(edge.Auto);
    });

    [Fact]
    public void 문구_없는_첫_칸을_끌면_자동_길이_된다() => HeadlessUi.Run(() =>
    {
        // §2 ③-a — 묻지 않고 지나가는 길에는 고를 것이 없으니 문구도 없다.
        (GraphEditorView graph, AuthoringSession session) = Show();

        // ⚠ 자동 길은 장면을 못 넘으므로 둘을 같은 장면에 둔다 — 안 그러면 규격 위반이고,
        //    화면이 그것을 먼저 막는다(아래 테스트).
        session.Editor.UpdateEpisodeScenes("ch01", ["root", "a"], "opening");
        graph.Rebuild();

        Drag(graph, session, "root", slot: 0, onto: "a");

        ChapterEdge edge = Assert.Single(session.Editor.FindChapter("ch01")!.Edges);

        Assert.True(edge.Auto);
        Assert.True(string.IsNullOrEmpty(edge.OptionLabel));

        // 그리고 그 길은 규격을 지킨다 — 저작 시점 진단이 조용해야 한다.
        Assert.DoesNotContain(
            session.Editor.FindChapter("ch01")!.ToGraphModel("chapters/ch01.xlsx").Errors,
            item => item.Code.ToString().StartsWith("AutoEdge", StringComparison.Ordinal));
    });

    [Fact]
    public void 장면을_넘는_자동_길은_먼저_막고_대안을_말한다() => HeadlessUi.Run(() =>
    {
        // ⚠ 장면 경계는 실제로 `Commit → Exit → Enter`다 — 묻지 않고 넘어가면 안 된다
        //    (`AutoEdgeCrossesScene`). 관문에서야 알면 되돌려야 하니 여기서 먼저 말한다.
        //
        // ⚠ 장면ID를 아무 데도 안 적은 챕터가 바로 이 경우다 — 에피소드마다 제 장면이라
        //    (`__scene_*`) 자동 길이 아예 설 수 없다. 숨기지 않고 말한다.
        (GraphEditorView graph, AuthoringSession session) = Show();

        Drag(graph, session, "root", slot: 0, onto: "a");

        Assert.Empty(session.Editor.FindChapter("ch01")!.Edges);
        Assert.Contains("같은 장면 안에서만", session.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("문구를 적어", session.StatusMessage, StringComparison.Ordinal);
    });

    [Fact]
    public void 빈_곳에_놓으면_길이_걷힌다() => HeadlessUi.Run(() =>
    {
        (GraphEditorView graph, AuthoringSession session) = Show();

        session.Editor.AddEdge("ch01", "root", "a", optionLabel: "왼쪽으로");
        graph.Rebuild();

        Drag(graph, session, "root", slot: 0, onto: null);

        Assert.Empty(session.Editor.FindChapter("ch01")!.Edges);
    });

    [Fact]
    public void 다른_챕터의_노드에는_못_놓고_이유를_말한다() => HeadlessUi.Run(() =>
    {
        // ⚠ 간선은 챕터 안의 길이다. 조용히 무시하면 사람은 손이 미끄러진 줄 안다.
        (GraphEditorView graph, AuthoringSession session) = Show(secondChapter: true);

        Write(graph, session, "root", slot: 1, "저쪽으로");
        Drag(graph, session, "root", slot: 1, onto: "far");

        Assert.Empty(session.Editor.FindChapter("ch01")!.Edges);
        Assert.Contains("다른 챕터", session.StatusMessage, StringComparison.Ordinal);
    });

    [Fact]
    public void 같은_문구로_같은_곳에_두_번은_안_된다() => HeadlessUi.Run(() =>
    {
        // 간선의 신원은 (출발, 도착, 문구)다 (v9) — 겹치면 같은 버튼이 둘로 보인다.
        (GraphEditorView graph, AuthoringSession session) = Show();

        session.Editor.AddEdge("ch01", "root", "a", optionLabel: "왼쪽으로");
        graph.Rebuild();

        Write(graph, session, "root", slot: 1, "왼쪽으로");
        Drag(graph, session, "root", slot: 1, onto: "a");

        Assert.Single(session.Editor.FindChapter("ch01")!.Edges);
        Assert.Contains("이미 있습니다", session.StatusMessage, StringComparison.Ordinal);
    });

    [Fact]
    public void 이미_사전에_있는_문구를_다시_써도_된다() => HeadlessUi.Run(() =>
    {
        // ⛔ 사전은 어휘집이다 — 같은 말을 다른 자리에서 다시 쓰는 것이 그 존재 이유다.
        //    [선택지 사전] 화면의 중복 거절을 그대로 부르면 <b>흔한 문구를 두 번째 노드에서
        //    못 쓴다</b>(2026-09-16에 이 테스트가 잡았다).
        (GraphEditorView graph, AuthoringSession session) = Show();

        Write(graph, session, "root", slot: 1, "돌아간다");
        Write(graph, session, "a", slot: 1, "돌아간다");

        Assert.DoesNotContain("오류", session.StatusMessage, StringComparison.Ordinal);
        Assert.Single(session.Editor.FindChapter("ch01")!.ChoiceOptions,
            option => option.Text == "돌아간다");
    });

    [Fact]
    public void 진행이_착지하지_않는_카드에도_칸_셋이_선다() => HeadlessUi.Run(() =>
    {
        // ⭐ R7 P-6 · 결정 ⑤. 전에는 척추 카드(진행이 착지하는 에피소드)만 레일 가지와
        //    칸을 받았고, 그 갈래가 곧 엑셀노드/자유노드의 구분이었다.
        //
        // 「분기 추가」가 세우는 카드가 정확히 이 부류다 — 들어오는 간선이 없고
        // `<<detour>>`로만 불려 간다. 거기서 <b>진짜 분기를 뚫는</b> 것이 그 기능의 요점이라,
        // 칸이 안 서면 기능 자체가 성립하지 않는다.
        (GraphEditorView graph, AuthoringSession session) = Show();

        DialogueNode aside = session.Editor.AddDialogueNode(
            session.Project.Files[0].Id, 0, 400, "곁가지");
        graph.Rebuild();

        Assert.Equal("곁가지", aside.MarkedEpisodeId);
        Assert.DoesNotContain(
            session.Editor.FindChapter("ch01")!.Edges,
            edge => string.Equals(edge.ToEpisodeId, "곁가지", StringComparison.Ordinal));

        Assert.Equal(3, Rows(graph, session, "곁가지").Count);
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 그 칸을 끌어 저 노드에 놓는다 — 사람이 하는 길 그대로다.
    /// <paramref name="onto"/>가 null이면 빈 곳에 놓는다(= 끊기).
    /// </summary>
    private static void Drag(
        GraphEditorView graph, AuthoringSession session, string nodeName, int slot, string? onto)
    {
        Press(Rows(graph, session, nodeName)[slot].Children.OfType<Ellipse>().Single());


        var canvas = graph.FindControl<Canvas>("GraphCanvas")!;

        // 빈 곳은 카드가 하나도 없는 먼 자리다.
        Point on = onto is null
            ? new Point(5000, 5000)
            : Card(graph, session, onto) is { } card
                ? new Point(Canvas.GetLeft(card) + 20, Canvas.GetTop(card) + 10)
                : default;

        // ⚠ 포인터 이벤트의 자리는 <b>최상위 기준</b>이다 — 넘긴 rootVisual과 무관하게 그렇게
        //   풀린다(2026-09-16에 탐색기에서 한 번, 여기서 또 한 번 밟았다). 판 좌표를 그대로
        //   주면 엉뚱한 데에 놓여 <b>조용히 아무 일도 안 일어난다</b>.
        var root = (Visual)TopLevel.GetTopLevel(canvas)!;
        Point at = canvas.TranslatePoint(on, root)!.Value;

        canvas.RaiseEvent(new PointerReleasedEventArgs(
            canvas, new Pointer(0, PointerType.Mouse, true), root, at, 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None, MouseButton.Left));

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static Border? Card(GraphEditorView graph, AuthoringSession session, string nodeName) =>
        graph.FindControl<Canvas>("GraphCanvas")!.Children.OfType<Border>()
            .FirstOrDefault(card => card.Tag as string == NodeId(session, nodeName));

    /// <summary>그 에피소드의 선택지 칸 머리점들 — 칸 번호 차례대로.</summary>
    private static IReadOnlyList<IBrush> Dots(GraphEditorView graph, AuthoringSession session, string episodeId) =>
        Rows(graph, session, episodeId)
            .Select(row => row.Children.OfType<Ellipse>().Single().Stroke!)
            .ToList();

    private static IReadOnlyList<TextBlock> Labels(
        GraphEditorView graph, AuthoringSession session, string episodeId) =>
        Rows(graph, session, episodeId)
            .Select(row => row.Children.OfType<TextBlock>().SingleOrDefault())
            .Where(label => label is not null)
            .Select(label => label!)
            .ToList();

    /// <summary>
    /// 선택지 칸들 — <b>카드 아래 철도 위</b>에 선다 (2026-09-16 소유자: "아래쪽으로 3개").
    /// 줄은 Tag에 <c>{에피소드}#{칸}</c>을 진다.
    /// </summary>
    private static IReadOnlyList<StackPanel> Rows(
        GraphEditorView graph, AuthoringSession session, string episodeId) =>
        graph.FindControl<Canvas>("GraphCanvas")!.Children.OfType<StackPanel>()
            .Where(row => (row.Tag as string)?.StartsWith(episodeId + "#", StringComparison.Ordinal) == true)
            .OrderBy(row => (row.Tag as string)![(episodeId.Length + 1)..], StringComparer.Ordinal)
            .ToList();

    /// <summary>판 위의 모든 글월 — 서면 안 되는 것이 섰는지 재는 자리.</summary>
    private static IReadOnlyList<string> Texts(GraphEditorView graph) =>
        graph.FindControl<Canvas>("GraphCanvas")!
            .GetVisualDescendants().OfType<TextBlock>()
            .Select(text => text.Text ?? string.Empty)
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

    /// <summary>ch01 — root · a, 각각 대사 노드가 선 판. 자리는 겹치지 않게 벌려 둔다.</summary>
    private (GraphEditorView Graph, AuthoringSession Session) Show(bool secondChapter = false)
    {
        var session = new AuthoringSession();
        session.Open(ManifestPath);

        session.Editor.EnsureChapter("ch01");
        string fileId = session.EnsureChapterBoard("ch01");

        Seed(session, "ch01", fileId, 0, "root", "a");

        if (secondChapter)
        {
            session.Editor.EnsureChapter("ch02");
            string second = session.EnsureChapterBoard("ch02");
            Seed(session, "ch02", second, 1200, "far");
            session.SetFileExpanded(second, expanded: true);
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

    private static void Seed(
        AuthoringSession session, string chapterId, string fileId, double x, params string[] episodeIds)
    {
        _ = fileId;

        for (int index = 0; index < episodeIds.Length; index++)
        {
            // ⚠ <b>카드를 따로 세우지 않는다</b> (2026-09-18) — `AddEpisode`가 함께 만든다.
            //    전에는 여기서 한 장 더 만들었고, 그러면 이름이 같은 카드가 둘 서서 끌어
            //    놓기가 <b>엉뚱한 카드</b>를 맞힌다(이 테스트가 `root → root` 간선으로 깨졌다).
            session.Editor.AddEpisode(chapterId, episodeIds[index], title: episodeIds[index], 0, 0);

            DialogueNode card = session.Project.EnumerateNodes().OfType<DialogueNode>()
                .Single(node => EpisodeNaming.EpisodeIdOf(node) == episodeIds[index]);

            // 자리는 이 테스트가 정한다 — 카드를 맞히려면 좌표를 알아야 한다.
            session.Editor.MoveNode(card.Id, x + (index * 320), 0);
        }
    }
}
