using Avalonia.Controls;

namespace Vn.App.Tests;

/// <summary>
/// 화면과 chrome의 일치 — 챕터 그래프 탭에서는 시나리오 계층의 우측 열(노드 편집기·무대
/// 프리뷰)과 상단 Yarn/CSV 내보내기가 접힌다. 다른 화면의 [발행]·[내보내기]가 남아 있으면
/// "한 화면에 내보내기가 둘"이 된다(소유자 보고). 돌아오면 전부 복원된다.
/// </summary>
public sealed class MainChromeTests
{
    [Fact]
    public void 챕터_탭에서는_우측_열과_시나리오_내보내기가_접힌다() => HeadlessUi.Run(() =>
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var tabs = window.FindControl<TabControl>("MainTabs")!;
        var right = window.FindControl<Border>("RightColumn")!;
        var export = window.FindControl<Button>("ExportButton")!;
        var columns = window.FindControl<Grid>("CenterColumns")!;

        tabs.SelectedIndex = 1; // 챕터 그래프
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(right.IsVisible);
        Assert.False(export.IsVisible);
        Assert.Equal(0, columns.ColumnDefinitions[4].Width.Value);

        tabs.SelectedIndex = 0; // 시나리오 그래프 — 전부 복원
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(right.IsVisible);
        Assert.True(export.IsVisible);
        Assert.Equal(460, columns.ColumnDefinitions[4].Width.Value);

        window.Close();
    });
}
