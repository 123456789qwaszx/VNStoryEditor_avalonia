using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Model;
using Vn.Authoring.Results;
using Vn.Authoring.Script;

namespace Vn.App.Tests;

/// <summary>
/// 연출 그래프의 카드를 <b>더블클릭하면 무대에 오른다</b> (2026-08-25 소유자: "특정 노드를
/// 더블 클릭했을 때, 무대프리뷰로 연결되도록. 더블클릭한 노드가 선택된채로").
///
/// ⚠ 이 길의 위험은 <b>순서</b>다. 탭 전환이 <c>EnterStageTab</c>을 부르고 그것이 "지금
/// 고른 것"을 보므로, 고르기보다 탭을 먼저 바꾸면 무대는 <b>직전 노드</b>를 연다 —
/// 화면은 멀쩡히 뜨는데 다른 씬이라, 사람이 잘못 눌렀다고 생각하게 되는 종류의 버그다.
/// </summary>
public sealed class PresentationGraphStageJumpTests
{
    private const int GraphTab = 1;
    private const int StageTab = 2;

    [Fact]
    public void 대사_노드를_더블클릭하면_그_씬이_무대에_오른다() => HeadlessUi.Run(() =>
    {
        (MainWindow window, AuthoringSession session, DialogueNode dialogue) = Stage();

        DoubleClick(window, CardCenter(window, dialogue.Id));

        Assert.Equal(StageTab, Tabs(window).SelectedIndex);

        // "더블클릭한 노드가 선택된 채" — 무대가 그리는 것은 <b>그 대사의 씬</b>이다.
        // 대사 노드를 고른 채로 두면 발행본이 그려져 잠긴 화면이 되므로, 선택은
        // 그 대사를 공급받는 연출 노드로 간다(탭에 들어오는 것과 같은 규칙).
        Assert.True(StageIsShowing(session, dialogue.Id));

        window.Close();
    });

    [Fact]
    public void 다른_카드를_더블클릭하면_무대가_그_노드로_바뀐다() => HeadlessUi.Run(() =>
    {
        // ⛔ 한 번 열어 본 뒤가 진짜 시험이다. 탭만 바꾸고 고르기를 빠뜨린 구현도 <b>첫
        //    번째는 통과한다</b> — 그때는 무대가 비어 있어 아무거나 그려도 맞아 보인다.
        //    두 번째부터 <b>직전 씬이 그대로 남는</b> 것으로 드러난다.
        (MainWindow window, AuthoringSession session, DialogueNode first) = Stage();

        DialogueNode second = session.Editor.AddDialogueNode(
            session.ActiveFile!.Id, name: "둘째", scriptId: session.Editor.AddScript("둘째 대본").Id);
        Line(session, second);

        // ⚠ 둘을 떼어 놓는다. 새 노드는 기본 자리에 서므로 겹치는데, 겹치면 <b>진짜 클릭</b>은
        //    위에 있는 카드로 간다 — 그러면 이 시험은 "첫째를 눌렀다"고 믿으면서 둘째를 누른다.
        session.Editor.MoveNode(first.Id, 40, 40);
        session.Editor.MoveNode(second.Id, 40, 420);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        DoubleClick(window, CardCenter(window, first.Id));
        Assert.True(StageIsShowing(session, first.Id));

        // 무대에 있는 채로 그래프로 돌아가 다른 카드를 더블클릭한다.
        Tabs(window).SelectedIndex = GraphTab;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        DoubleClick(window, CardCenter(window, second.Id));

        Assert.Equal(StageTab, Tabs(window).SelectedIndex);
        Assert.True(StageIsShowing(session, second.Id));

        window.Close();
    });

    [Fact]
    public void 설정_노드는_무대로_가지_않고_사유를_말한다() => HeadlessUi.Run(() =>
    {
        // 설정 노드는 무대에 올릴 것이 없다. 조용히 아무 일도 안 하면 더블클릭이 고장 난
        // 것처럼 보이므로, 안 가는 대신 왜 안 가는지를 말한다.
        (MainWindow window, AuthoringSession session, DialogueNode _) = Stage();

        SetNode settings = session.Editor.AddSetNode(session.ActiveFile!.Id, name: "설정");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        DoubleClick(window, CardCenter(window, settings.Id));

        Assert.Equal(GraphTab, Tabs(window).SelectedIndex);
        Assert.Contains("무대에서 볼 것이 없습니다", session.StatusMessage, StringComparison.Ordinal);

        window.Close();
    });

    [Fact]
    public void 무대에서_돌아오면_그_에피소드_노드가_고른_채로_한가운데_선다() => HeadlessUi.Run(() =>
    {
        // 2026-08-25 소유자: "무대프리뷰에서 선택한 에피소드 노드가 화면 중앙에 온 채,
        // 클릭이 된 상태가 되도록."
        //
        // ⚠ 무대의 선택은 <b>연출 노드</b>다. 사람이 "보고 있던 것"이라고 여기는 것은 그
        //    연출이 얹힌 에피소드 노드이므로, 돌아올 때 그쪽으로 돌려놓아야 한다 —
        //    안 그러면 돌아온 화면이 방금 본 씬이 아니라 그 옆의 🎬 카드를 가리킨다.
        (MainWindow window, AuthoringSession session, DialogueNode dialogue) = Stage();

        // 판 밖으로 멀리 보내 둔다 — 가까이 있으면 안 옮겨도 우연히 가운데로 보인다.
        // 그리고 <b>구석에 걸치게</b> 스크롤해 둔다: 누르려면 보여야 하고, 동시에 지금은
        // 가운데가 아니어야 아래 단언이 뜻을 갖는다.
        session.Editor.MoveNode(dialogue.Id, 1200, 900);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        ScrollCardToCorner(window, dialogue.Id);

        Assert.False(IsCentered(window, dialogue.Id), "시작부터 가운데면 이 시험은 아무것도 안 잰다");

        DoubleClick(window, CardCenter(window, dialogue.Id));
        Assert.Equal(StageTab, Tabs(window).SelectedIndex);

        Tabs(window).SelectedIndex = GraphTab;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // ① 고른 것은 <b>에피소드 노드</b>다 — 무대가 들고 있던 연출 노드가 아니다.
        Assert.Equal(dialogue.Id, session.SelectedNodeId);

        // ② 그 카드가 화면 한가운데 있다.
        Assert.True(IsCentered(window, dialogue.Id), "에피소드 노드가 화면 한가운데가 아니다");

        window.Close();
    });

    [Fact]
    public void 챕터_그래프에서_건너오면_화면을_옮기지_않는다() => HeadlessUi.Run(() =>
    {
        // 가운데 맞추기는 <b>무대에서 돌아온</b> 사람에게만 주는 배려다. 다른 길로 들어온
        // 사람의 스크롤 자리를 빼앗으면 그것은 배려가 아니라 방해다.
        (MainWindow window, AuthoringSession session, DialogueNode dialogue) = Stage();

        session.Editor.MoveNode(dialogue.Id, 2400, 1800);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ScrollViewer scroll = window.FindControl<GraphEditorView>("Graph")!
            .FindControl<ScrollViewer>("GraphScroll")!;
        Vector before = scroll.Offset;

        Tabs(window).SelectedIndex = 0; // 챕터 그래프
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Tabs(window).SelectedIndex = GraphTab;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(before, scroll.Offset);

        window.Close();
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <summary>그 카드가 보이는 판의 <b>왼쪽 위 구석</b>에 걸치도록 스크롤한다.</summary>
    private static void ScrollCardToCorner(MainWindow window, string nodeId)
    {
        GraphEditorView graph = window.FindControl<GraphEditorView>("Graph")!;
        Canvas canvas = graph.FindControl<Canvas>("GraphCanvas")!;

        Border card = canvas.Children.OfType<Border>()
            .Single(border => (border.Tag as string) == nodeId);

        graph.FindControl<ScrollViewer>("GraphScroll")!.Offset =
            new Vector(Canvas.GetLeft(card), Canvas.GetTop(card));

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>그 노드의 카드 한가운데가 보이는 판의 한가운데와 겹치는가(카드 반쪽 안).</summary>
    private static bool IsCentered(MainWindow window, string nodeId)
    {
        GraphEditorView graph = window.FindControl<GraphEditorView>("Graph")!;
        ScrollViewer scroll = graph.FindControl<ScrollViewer>("GraphScroll")!;
        Canvas canvas = graph.FindControl<Canvas>("GraphCanvas")!;

        Border card = canvas.Children.OfType<Border>()
            .Single(border => (border.Tag as string) == nodeId);

        var cardCenter = new Avalonia.Point(
            Canvas.GetLeft(card) + (card.Bounds.Width / 2),
            Canvas.GetTop(card) + (card.Bounds.Height / 2));

        var viewCenter = new Avalonia.Point(
            scroll.Offset.X + (scroll.Viewport.Width / 2),
            scroll.Offset.Y + (scroll.Viewport.Height / 2));

        return Math.Abs(cardCenter.X - viewCenter.X) <= card.Bounds.Width / 2 &&
               Math.Abs(cardCenter.Y - viewCenter.Y) <= card.Bounds.Height / 2;
    }

    /// <summary>무대가 이 대사의 씬을 보고 있는가 — 그 대사이거나, 그 대사를 받는 연출 노드다.</summary>
    private static bool StageIsShowing(AuthoringSession session, string dialogueNodeId)
    {
        if (session.SelectedNode is not { } selected)
        {
            return false;
        }

        if (string.Equals(selected.Id, dialogueNodeId, StringComparison.Ordinal))
        {
            return true;
        }

        return NodeExportResolver.SupplyLinkOf(session.Project, dialogueNodeId) is { } supply &&
               string.Equals(supply.SourceNodeId, selected.Id, StringComparison.Ordinal);
    }

    private static TabControl Tabs(MainWindow window) =>
        window.FindControl<TabControl>("MainTabs")!;

    /// <summary>카드는 <c>Tag</c>에 제 노드 Id를 지고 있다 — 그래프가 그것으로 되짚는다.</summary>
    private static Avalonia.Point CardCenter(MainWindow window, string nodeId)
    {
        Canvas canvas = window.FindControl<GraphEditorView>("Graph")!
            .FindControl<Canvas>("GraphCanvas")!;

        Border card = canvas.Children.OfType<Border>()
            .Single(border => (border.Tag as string) == nodeId);

        return card.TranslatePoint(
            new Avalonia.Point(card.Bounds.Width / 2, 12), window)!.Value;
    }

    /// <summary>
    /// 진짜 더블클릭 — 눌림 둘이 제스처로 묶이는지까지 함께 잰다.
    ///
    /// ⚠ 묶임 판정은 두 눌림 사이의 <b>진짜 시계</b>다(헤드리스 입력 Timestamp가 실시간).
    /// 스위트 부하에서 그 간격이 기본 500ms를 넘으면 제스처가 안 묶여 매번 다른 테스트가
    /// 하나씩 떨어졌다(2026-08-26). 그래서 클릭 넷을 보내는 동안만 더블탭 시간 창을
    /// 열어 둔다 — 경로(히트 테스트 → ClickCount → DoubleTapped)는 실물 그대로다.
    /// </summary>
    private static void DoubleClick(MainWindow window, Avalonia.Point point)
    {
        int readsBefore = TestPlatformSettings.Instance.DoubleTapTimeReads;

        using (TestPlatformSettings.Instance.HoldDoubleTapWindowOpen())
        {
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
        }

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 배선 단절을 소리 나게 — Avalonia가 더블탭 시간을 다른 데서 읽게 바뀌면
        // 간헐 실패로 돌아가는 대신 여기서 즉시 무너진다.
        Assert.True(TestPlatformSettings.Instance.DoubleTapTimeReads > readsBefore,
            "더블탭 시간 창이 TestPlatformSettings를 지나지 않았다 — 합성 더블클릭이 다시 진짜 시계에 매였다");
    }

    /// <summary>연출 그래프 탭에 선 창 하나 + 재생할 줄이 있는 대사 노드.</summary>
    private static (MainWindow Window, AuthoringSession Session, DialogueNode Dialogue) Stage()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        AuthoringSession session = window.SessionProbe;

        DialogueNode dialogue = session.Editor.AddDialogueNode(
            session.ActiveFile!.Id, name: "본문", scriptId: session.Editor.AddScript("본문 대본").Id);
        Line(session, dialogue);

        Tabs(window).SelectedIndex = GraphTab;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return (window, session, dialogue);
    }

    /// <summary>재생할 줄 하나 — 빈 노드는 연출 채널이 서지 않는다.</summary>
    private static void Line(AuthoringSession session, DialogueNode dialogue)
    {
        string scriptId = dialogue.ScriptId!;
        ScriptLine line = session.Editor.InsertScriptLine(scriptId);
        session.Editor.SetScriptLineText(scriptId, line.Id, "라루", "첫 줄");
    }
}
