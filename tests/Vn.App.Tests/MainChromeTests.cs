using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Vn.App.Views;

namespace Vn.App.Tests;

/// <summary>
/// 화면과 chrome의 일치 — 챕터 그래프 탭에서는 연출 계층의 우측 열(노드 편집기·무대
/// 프리뷰)과 상단 Yarn/CSV 내보내기가 접힌다. 다른 화면의 [발행]·[내보내기]가 남아 있으면
/// "한 화면에 내보내기가 둘"이 된다(소유자 보고). 돌아오면 전부 복원된다.
///
/// 탭 순서·이름은 2026-08-18 팀장 미팅에서 정해졌다: **챕터 그래프가 첫 탭**이고 그다음이
/// **연출 그래프**(옛 "시나리오 그래프")다. 왼쪽에서 오른쪽으로 밟으면 챕터가 완성된다.
/// </summary>
public sealed class MainChromeTests
{
    [Fact]
    public void 챕터_그래프가_첫_탭이고_그다음이_연출_그래프다() => HeadlessUi.Run(() =>
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var tabs = window.FindControl<TabControl>("MainTabs")!;

        Assert.Equal("챕터 그래프", ((TabItem)tabs.Items[0]!).Header);
        Assert.Equal("연출 그래프", ((TabItem)tabs.Items[1]!).Header);

        window.Close();
    });

    [Fact]
    public void 시작하자마자_챕터_모드다() => HeadlessUi.Run(() =>
    {
        // 첫 탭이 챕터 그래프가 된 뒤로 시작 화면이 곧 접힌 상태다. XAML 기본값은
        // 펼침이고 SelectionChanged는 이미 정해진 선택에 오지 않으므로, 생성자가
        // 한 번 맞춰 주지 않으면 여기서만 어긋난다.
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(window.FindControl<Border>("RightColumn")!.IsVisible);
        Assert.False(window.FindControl<Button>("ExportButton")!.IsVisible);
        Assert.Equal(0, window.FindControl<Grid>("CenterColumns")!.ColumnDefinitions[4].Width.Value);

        window.Close();
    });

    [Fact]
    public void 챕터_탭에서는_우측_열과_연출_내보내기가_접힌다() => HeadlessUi.Run(() =>
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var tabs = window.FindControl<TabControl>("MainTabs")!;
        var right = window.FindControl<Border>("RightColumn")!;
        var export = window.FindControl<Button>("ExportButton")!;
        var columns = window.FindControl<Grid>("CenterColumns")!;

        tabs.SelectedIndex = 1; // 연출 그래프 — 전부 선다
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(right.IsVisible);
        Assert.True(export.IsVisible);
        Assert.Equal(460, columns.ColumnDefinitions[4].Width.Value);

        tabs.SelectedIndex = 0; // 챕터 그래프 — 다시 접힌다
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(right.IsVisible);
        Assert.False(export.IsVisible);
        Assert.Equal(0, columns.ColumnDefinitions[4].Width.Value);

        window.Close();
    });

    [Fact]
    public void 편집_자료와_에셋은_우측_열에서_접힌_채로_시작한다() => HeadlessUi.Run(() =>
    {
        // 2026-08-18 팀장 미팅 — "평소에 굳이 보일 필요가 없지". 둘 다 왼쪽 챕터
        // 목록과 같은 무게로 읽히던 자리에서 우측 맨 아래로 내려왔고, 기본은 접힘이다.
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var resourceScroll = window.FindControl<ScrollViewer>("ResourceScroll")!;
        var toggle = window.FindControl<ToggleButton>("ResourceCollapseToggle")!;

        Assert.False(resourceScroll.IsVisible);
        Assert.Equal("▶", toggle.Content);

        // 왼쪽에는 더 이상 없다 — 우측 열(RightColumn) 안에 산다.
        var right = window.FindControl<Border>("RightColumn")!;
        Assert.Contains(right, resourceScroll.GetVisualAncestors());
        Assert.Contains(right, window.FindControl<AssetExplorerView>("AssetExplorer")!.GetVisualAncestors());

        toggle.IsChecked = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(resourceScroll.IsVisible);
        Assert.Equal("▼", toggle.Content);

        window.Close();
    });
}
