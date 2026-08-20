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
        new(PresentationScriptRowKind.Command, "ln_a", Command("char_rig_staging.move_by"), "<<move_by c1 +2u>>"),
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
    public void 편집_가능할_때만_커맨드_행_우측에_제거_X가_선다() => HeadlessUi.Run(() =>
    {
        var panel = new PresentationScriptPanel();

        panel.Show(Rows(), selectedLineId: "ln_a", editable: true);
        int editableXCount = panel.GetLogicalDescendants().OfType<TextBlock>()
            .Count(text => string.Equals(text.Text, "✕", StringComparison.Ordinal));
        Assert.Equal(2, editableXCount); // 커맨드 행 수만큼 — 대사 행에는 없다.

        panel.Show(Rows(), selectedLineId: "ln_a", editable: false);
        Assert.DoesNotContain(panel.GetLogicalDescendants().OfType<TextBlock>(),
            text => string.Equals(text.Text, "✕", StringComparison.Ordinal));
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
}
