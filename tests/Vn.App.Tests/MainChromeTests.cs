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
        Assert.Equal(0, window.FindControl<Grid>("CenterColumns")!.ColumnDefinitions[2].Width.Value);

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
        Assert.Equal(460, columns.ColumnDefinitions[2].Width.Value);

        tabs.SelectedIndex = 0; // 챕터 그래프 — 다시 접힌다
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(right.IsVisible);
        Assert.False(export.IsVisible);
        Assert.Equal(0, columns.ColumnDefinitions[2].Width.Value);

        window.Close();
    });

    [Fact]
    public void 편집_자료는_사라지고_에셋만_우측_열에_남는다() => HeadlessUi.Run(() =>
    {
        // 2026-08-21 소유자 — "이제 편집자료는 굳이 표시할 필요가 없는 것 같아".
        // 대본·발행 결과·연출 공급을 나열하던 현황판인데, 발행·배선이 자동이 된 뒤로는
        // 볼 이유가 없다. 에셋 탐색기는 그 자리(우측 열 맨 아래)에 그대로 남는다.
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Null(window.FindControl<ScrollViewer>("ResourceScroll"));
        Assert.Null(window.FindControl<ToggleButton>("ResourceCollapseToggle"));

        var right = window.FindControl<Border>("RightColumn")!;
        Assert.Contains(right, window.FindControl<AssetExplorerView>("AssetExplorer")!.GetVisualAncestors());

        window.Close();
    });

    [Fact]
    public void 챕터_목록은_맨_왼쪽_열을_떠나_챕터_그래프_안으로_갔다() => HeadlessUi.Run(() =>
    {
        // 2026-08-22 소유자 — 챕터를 고르는 일과 그 챕터를 편집하는 일이 한 기둥에 선다.
        // 창은 이제 [탭들 | 스플리터 | 우측 열] 셋뿐이고, 판은 그 240px만큼 넓어졌다.
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var columns = window.FindControl<Grid>("CenterColumns")!;
        Assert.Equal(3, columns.ColumnDefinitions.Count);
        Assert.True(columns.ColumnDefinitions[0].Width.IsStar, "첫 열은 탭이 차지한다");

        // 옛 왼쪽 열의 이름들은 창에서 사라졌다 — 남아 있으면 두 자리에 같은 목록이 산다.
        Assert.Null(window.FindControl<StackPanel>("FileListPanel"));
        Assert.Null(window.FindControl<Button>("AddFileButton"));

        // 새 자리는 챕터 그래프 뷰 안이고, 셸이 그 손잡이로 목록을 짓는다.
        var chapterGraph = window.FindControl<ChapterGraphView>("ChapterGraph")!;
        Assert.Contains(chapterGraph, chapterGraph.ChapterListHost.GetVisualAncestors());
        Assert.Contains(chapterGraph, chapterGraph.ChapterAddButton.GetVisualAncestors());

        window.Close();
    });

    /// <summary>
    /// 2026-08-22 소유자 보고 — 챕터 목록 스플리터가 "위로만 움직이고 아래쪽으로는
    /// 동작을 안 하는" 결함. 스플리터 <b>바로 아래가 잠금 배너(Auto)</b>였던 것이 정체다:
    /// 아래로 끌려면 그 행을 줄여야 하는데 Auto는 이미 내용 높이라 줄 것이 없었고,
    /// 위로 끌 때만 그 행이 늘며 움직였다. 배너와 탭을 안쪽 격자로 묶어 스플리터의
    /// 상대를 <b>별(*) 행</b>으로 만들었다.
    /// </summary>
    [Fact]
    public void 챕터_목록_스플리터_아래는_별_행이다() => HeadlessUi.Run(() =>
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var chapterGraph = window.FindControl<ChapterGraphView>("ChapterGraph")!;
        GridSplitter splitter = Assert.Single(
            chapterGraph.GetVisualDescendants().OfType<GridSplitter>(),
            item => item.ResizeDirection == GridResizeDirection.Rows);

        var grid = (Grid)splitter.GetVisualParent()!;
        int row = Grid.GetRow(splitter);

        // 위: 사람이 끄는 절대 높이. 0까지 접히면 다시 잡을 손잡이가 없으므로 하한이 있다.
        Assert.True(grid.RowDefinitions[row - 1].Height.IsAbsolute);
        Assert.True(grid.RowDefinitions[row - 1].MinHeight > 0);

        // 아래: ⚠ 여기가 Auto면 아래로 끌 때 줄일 것이 없어 스플리터가 한쪽으로만 움직인다.
        Assert.True(
            grid.RowDefinitions[row + 1].Height.IsStar,
            "스플리터 아래는 별 행이어야 위아래로 다 끌린다");
        Assert.Equal(row + 2, grid.RowDefinitions.Count);

        window.Close();
    });

    [Fact]
    public void 무대_프리뷰_탭에서는_노드_편집기가_접히고_에셋만_남는다() => HeadlessUi.Run(() =>
    {
        // 2026-08-22 소유자 — "대사편집, 발행, preview를 무대 프리뷰에서는 보이지 않도록".
        // ⚠ 에셋 탐색기는 남는다: 배경·초상을 무대로 끌어다 놓는 유일한 출발지다.
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var tabs = window.FindControl<TabControl>("MainTabs")!;
        var editors = window.FindControl<Panel>("EditorPanel")!;
        var assets = window.FindControl<AssetExplorerView>("AssetExplorer")!;

        tabs.SelectedIndex = 1; // 연출 그래프 — 편집기가 선다
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(editors.IsVisible);

        tabs.SelectedIndex = 2; // 무대 프리뷰 — 편집기만 접힌다
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(editors.IsVisible);
        Assert.True(assets.IsVisible);
        Assert.True(window.FindControl<Border>("RightColumn")!.IsVisible);

        // ⚠ 편집기의 IsVisible 자체는 안 건드린다 — 작업대·연출 추가 콘솔 공급이 그
        // 플래그를 근거로 돌아가므로, 끄면 프리뷰의 편집이 함께 죽는다.
        Assert.Equal(0, window.FindControl<Grid>("RightRows")!.RowDefinitions[0].Height.Value);

        tabs.SelectedIndex = 1; // 돌아오면 복원된다
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(editors.IsVisible);

        window.Close();
    });
}
