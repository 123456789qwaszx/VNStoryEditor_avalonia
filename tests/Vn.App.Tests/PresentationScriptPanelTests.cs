using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Vn.App.Views;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.App.Tests;

/// <summary>
/// 대본 텍스트 패널 (2026-08-20) — 커맨드 행마다 점(상세조절 입구)이 서고,
/// 현재 라인의 구간이 반투명 박스로 구분되는 것을 사람 눈 없이 닫는다.
/// </summary>
public sealed class PresentationScriptPanelTests
{
    private static PresentationResultCommand Command(string definitionId) =>
        new(Identifier.PresentationCommand(), definitionId,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["slot"] = "c1" });

    private static IReadOnlyList<PresentationScriptRow> Rows() =>
    [
        new(PresentationScriptRowKind.SectionHeader, null, null, "── Setup ──"),
        new(PresentationScriptRowKind.Command, null, Command("char_rig_cast.slot"), "<<slot c1>>"),
        new(PresentationScriptRowKind.Actor, "ln_a", null, "<<actor @1 willow>>"),
        new(PresentationScriptRowKind.Command, "ln_a", Command("char_rig_staging.move_by"), "<<move_by @1 +2u>>"),
        new(PresentationScriptRowKind.Dialogue, "ln_a", null, "라루: 첫 줄"),
        new(PresentationScriptRowKind.Dialogue, "ln_b", null, "윌로: 둘째 줄")
    ];

    [Fact]
    public void 커맨드_행마다_점이_서고_현재_라인_구간이_박스로_구분된다() => HeadlessUi.Run(() =>
    {
        var panel = new PresentationScriptPanel();
        var window = new Window { Content = panel, Width = 400, Height = 500 };
        window.Show();

        panel.Show(Rows(), selectedLineId: "ln_a", editable: true);
        window.Measure(new Avalonia.Size(400, 500));
        window.Arrange(new Avalonia.Rect(0, 0, 400, 500));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 점 = 커맨드 행 수 (Rider 브레이크포인트 감각).
        Assert.Equal(2, panel.GetLogicalDescendants().OfType<Ellipse>().Count());

        // 반투명 하이라이트 박스가 정확히 하나 — 선택 라인(ln_a)의 구간이다.
        Border[] highlighted = panel.GetLogicalDescendants().OfType<Border>()
            .Where(border => border.Background is SolidColorBrush { Color.A: > 0 and < 255 } brush &&
                             brush.Color.A == 60)
            .ToArray();
        Border box = Assert.Single(highlighted);

        // 그 박스 안에 ln_a의 커맨드와 대사가 함께 산다 — 둘째 라인은 밖이다.
        string[] boxTexts = box.GetLogicalDescendants().OfType<TextBlock>()
            .Select(text => text.Text ?? "").ToArray();
        Assert.Contains(boxTexts, text => text.Contains("move_by", StringComparison.Ordinal));
        Assert.Contains(boxTexts, text => text.Contains("첫 줄", StringComparison.Ordinal));
        Assert.DoesNotContain(boxTexts, text => text.Contains("둘째 줄", StringComparison.Ordinal));

        window.Close();
    });

    [Fact]
    public void 행이_없으면_패널이_숨는다() => HeadlessUi.Run(() =>
    {
        var panel = new PresentationScriptPanel();

        panel.Show(null, selectedLineId: null, editable: false);
        Assert.False(panel.IsVisible);

        panel.Show(Rows(), selectedLineId: null, editable: false);
        Assert.True(panel.IsVisible);
    });

    [Fact]
    public void 라인_박스_위쪽에_lineId_헤더가_서고_대사는_그대로_박스_안이다() => HeadlessUi.Run(() =>
    {
        var panel = new PresentationScriptPanel();
        panel.Show(Rows(), selectedLineId: "ln_a", editable: true);

        string[] texts = panel.GetLogicalDescendants().OfType<TextBlock>()
            .Select(text => text.Text ?? "").ToArray();

        // Setup 헤더와 같은 결의 lineId 헤더 — 라인마다 하나 (2026-08-21 소유자 지시).
        Assert.Contains("── ln_a ──", texts);
        Assert.Contains("── ln_b ──", texts);
        Assert.Contains("── Setup ──", texts);

        // 대사 텍스트는 현재 위치 유지 — 헤더가 대사를 대체하지 않는다.
        Assert.Contains(texts, text => text.Contains("첫 줄", StringComparison.Ordinal));
    });

    [Fact]
    public void 제거_X는_선택된_구획의_커맨드_행에서만_보인다() => HeadlessUi.Run(() =>
    {
        var panel = new PresentationScriptPanel();

        TextBlock[] AllX() => panel.GetLogicalDescendants().OfType<TextBlock>()
            .Where(text => string.Equals(text.Text, "✕", StringComparison.Ordinal)).ToArray();

        // 보이는 것 = 불투명 + 클릭 가능. 안 보이는 것도 자리는 잡는다(행 높이 고정).
        int VisibleX() => AllX().Count(text => text.Opacity > 0 && text.IsHitTestVisible);

        // ln_a 선택 — 커맨드 행 둘(Setup 하나·ln_a 하나) 모두 자리는 있고, 보이는 것은
        // ln_a의 하나뿐이다 (2026-08-21 소유자: "특정 라인을 클릭했을 때만 x가 보이도록").
        panel.Show(Rows(), selectedLineId: "ln_a", editable: true);
        Assert.Equal(2, AllX().Length);
        Assert.Equal(1, VisibleX());

        // Setup 선택 — 보이는 것은 Setup 행의 하나.
        panel.Show(Rows(), selectedLineId: null, editable: true, setupSelected: true);
        Assert.Equal(2, AllX().Length);
        Assert.Equal(1, VisibleX());

        // 읽기 전용 — 아예 없다.
        panel.Show(Rows(), selectedLineId: "ln_a", editable: false);
        Assert.Empty(AllX());
    });

    [Fact]
    public void 꺼진_커맨드_행은_빈_점과_흐린_텍스트로_남는다() => HeadlessUi.Run(() =>
    {
        // 2026-08-21 소유자: 점 = 켜고 끄기 — 꺼진 커맨드는 사라지지 않고 흐려진다.
        IReadOnlyList<PresentationScriptRow> rows =
        [
            new(PresentationScriptRowKind.Command, "ln_a",
                Command("char_rig_staging.move_by"), "<<move_by @1 +2u>>"),
            new(PresentationScriptRowKind.Command, "ln_a",
                Command("char_rig_staging.move_by"), "<<move_by @1 -2u>>", IsEnabled: false),
            new(PresentationScriptRowKind.Dialogue, "ln_a", null, "라루: 첫 줄")
        ];

        var panel = new PresentationScriptPanel();
        panel.Show(rows, selectedLineId: "ln_a", editable: true);

        Ellipse[] dots = panel.GetLogicalDescendants().OfType<Ellipse>().ToArray();
        Assert.Equal(2, dots.Length);

        // 켜짐 = 찬 점, 꺼짐 = 빈 점(테두리만).
        Assert.Single(dots, dot => dot.Fill is SolidColorBrush { Color.A: > 0 });
        Ellipse hollow = Assert.Single(dots, dot => Equals(dot.Fill, Brushes.Transparent));
        Assert.NotNull(hollow.Stroke);

        // 꺼진 행의 텍스트는 흐리다.
        Assert.Contains(panel.GetLogicalDescendants().OfType<TextBlock>(),
            text => text.Text == "<<move_by @1 -2u>>" && text.Opacity < 0.6);
    });

    [Fact]
    public void 선택이_바뀌어도_커맨드_행_높이는_그대로다() => HeadlessUi.Run(() =>
    {
        // 2026-08-21 소유자: "간격이 계속 바뀌면서 레이아웃이 이동하는데 … 어지럽고
        // 피로해". 제거 X는 보였다 숨었다 하되 자리는 늘 잡아야 한다.
        var panel = new PresentationScriptPanel();
        var window = new Window { Content = panel, Width = 400, Height = 500 };
        window.Show();

        double MeasureRows(string? selectedLine, bool setupSelected)
        {
            panel.Show(Rows(), selectedLine, editable: true, setupSelected);
            window.Measure(new Avalonia.Size(400, 500));
            window.Arrange(new Avalonia.Rect(0, 0, 400, 500));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            return panel.GetLogicalDescendants().OfType<DockPanel>()
                .Sum(row => row.Bounds.Height);
        }

        double selectedLine = MeasureRows("ln_a", false); // X 보임
        double otherLine = MeasureRows("ln_b", false);    // ln_a의 X 숨음
        double setup = MeasureRows(null, true);           // Setup의 X만 보임

        Assert.Equal(selectedLine, otherLine, 1);
        Assert.Equal(selectedLine, setup, 1);

        window.Close();
    });

    [Fact]
    public void Setup을_고르면_라인_하이라이트는_꺼진다() => HeadlessUi.Run(() =>
    {
        // 2026-08-21 소유자: "라인을 클릭한 상태로 Setup을 클릭하면 하이라이트가 2군데에".
        var panel = new PresentationScriptPanel();

        // 라인 선택이 남아 있어도(선택 라인 정보는 살아 있다) 켜지는 박스는 하나다.
        panel.Show(Rows(), selectedLineId: "ln_a", editable: true, setupSelected: true);

        Border[] highlighted = panel.GetLogicalDescendants().OfType<Border>()
            .Where(border => border.Background is SolidColorBrush { Color.A: 60 })
            .ToArray();
        Border box = Assert.Single(highlighted);
        Assert.Contains(box.GetLogicalDescendants().OfType<TextBlock>(),
            text => (text.Text ?? "").Contains("Setup", StringComparison.Ordinal));

        // Setup에서 벗어나면 그 라인이 다시 켜진다.
        panel.Show(Rows(), selectedLineId: "ln_a", editable: true, setupSelected: false);
        Border lineBox = Assert.Single(
            panel.GetLogicalDescendants().OfType<Border>(),
            border => border.Background is SolidColorBrush { Color.A: 60 });
        Assert.Contains(lineBox.GetLogicalDescendants().OfType<TextBlock>(),
            text => (text.Text ?? "").Contains("ln_a", StringComparison.Ordinal));
    });

    [Fact]
    public void Setup_선택이면_Setup_구획이_선택_박스로_칠해진다() => HeadlessUi.Run(() =>
    {
        var panel = new PresentationScriptPanel();
        panel.Show(Rows(), selectedLineId: null, editable: true, setupSelected: true);

        // 선택 라인이 없고 Setup만 선택 — 반투명 선택 박스는 Setup 구획 하나다.
        Border[] highlighted = panel.GetLogicalDescendants().OfType<Border>()
            .Where(border => border.Background is SolidColorBrush { Color.A: 60 })
            .ToArray();
        Border box = Assert.Single(highlighted);
        Assert.Contains(box.GetLogicalDescendants().OfType<TextBlock>(),
            text => (text.Text ?? "").Contains("Setup", StringComparison.Ordinal));
    });

    // ── 우클릭 메뉴 + 단축키 (2026-08-21 소유자 지시) ────────────────────────

    private static void RightClick(Control target) => target.RaiseEvent(
        new Avalonia.Input.PointerPressedEventArgs(
            target, new Avalonia.Input.Pointer(0, Avalonia.Input.PointerType.Mouse, true),
            target, default, 0,
            new Avalonia.Input.PointerPointProperties(
                Avalonia.Input.RawInputModifiers.RightMouseButton,
                Avalonia.Input.PointerUpdateKind.RightButtonPressed),
            Avalonia.Input.KeyModifiers.None));

    private static void PressCtrl(Control target, Avalonia.Input.Key key) => target.RaiseEvent(
        new Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = Avalonia.Input.KeyModifiers.Control
        });

    private static Border CommandRowOf(PresentationScriptPanel panel, string containsText) =>
        panel.GetLogicalDescendants().OfType<TextBlock>()
            .Where(text => (text.Text ?? "").Contains(containsText, StringComparison.Ordinal))
            .Select(text => text.FindLogicalAncestorOfType<Border>()!)
            .First();

    [Fact]
    public void 우클릭은_그_커맨드를_선택하고_Ctrl_C가_그것을_복사한다() => HeadlessUi.Run(() =>
    {
        var panel = new PresentationScriptPanel();
        var window = new Window { Content = panel, Width = 400, Height = 500 };
        window.Show();

        PresentationResultCommand? selected = null;
        PresentationResultCommand? copied = null;
        panel.CommandSelected += command => selected = command;
        panel.CommandCopyRequested += command => copied = command;

        panel.Show(Rows(), selectedLineId: "ln_a", editable: true);
        window.Measure(new Avalonia.Size(400, 500));
        window.Arrange(new Avalonia.Rect(0, 0, 400, 500));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        RightClick(CommandRowOf(panel, "move_by"));

        // 우클릭 = 선택 — 메뉴·단축키가 보이는 것과 같은 대상을 잡는다.
        Assert.NotNull(selected);
        Assert.Equal("char_rig_staging.move_by", selected!.DefinitionId);
        Assert.Equal(selected.CommandId, panel.SelectedCommandId);

        PressCtrl(panel, Avalonia.Input.Key.C);
        Assert.Equal(selected.CommandId, copied?.CommandId);

        window.Close();
    });

    [Fact]
    public void Ctrl_V는_지금_구획으로_붙여넣기를_청하고_읽기_전용이면_침묵한다() => HeadlessUi.Run(() =>
    {
        var panel = new PresentationScriptPanel();
        (string? LineId, bool Setup)? pasted = null;
        panel.HasClipboardCommand = () => true;
        panel.CommandPasteRequested += (lineId, setup) => pasted = (lineId, setup);

        panel.Show(Rows(), selectedLineId: "ln_a", editable: true);
        PressCtrl(panel, Avalonia.Input.Key.V);
        Assert.Equal(("ln_a", false), pasted);

        // Setup 구획이 선택돼 있으면 Setup으로 간다.
        pasted = null;
        panel.Show(Rows(), selectedLineId: null, editable: true, setupSelected: true);
        PressCtrl(panel, Avalonia.Input.Key.V);
        Assert.Equal(((string?)null, true), pasted);

        // 읽기 전용 — 단축키가 아무것도 청하지 않는다.
        pasted = null;
        panel.Show(Rows(), selectedLineId: "ln_a", editable: false);
        PressCtrl(panel, Avalonia.Input.Key.V);
        Assert.Null(pasted);

        // 클립보드가 비어 있어도 침묵한다.
        panel.HasClipboardCommand = () => false;
        panel.Show(Rows(), selectedLineId: "ln_a", editable: true);
        PressCtrl(panel, Avalonia.Input.Key.V);
        Assert.Null(pasted);
    });
}
