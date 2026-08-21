using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Vn.App.Services;
using Vn.App.Views;

namespace Vn.App.Tests;

/// <summary>
/// [새로 고침]이 도는 동안 <b>그 사실이 화면에 보인다</b> (2026-08-21 소유자 보고: 새 튜닝
/// 덤프를 넣고 누르자 창이 30초 넘게 굳어 멈춘 줄 알고 껐다). 굳은 창을 죽이면 저장 안 한
/// 편집이 함께 날아가므로, "일하는 중"과 "죽었다"를 구분할 수 있어야 한다.
/// </summary>
public sealed class AssetRefreshFeedbackTests
{
    [Fact]
    public void 새로_고침은_단추를_잠그고_다시_읽는_중을_먼저_적는다() => HeadlessUi.Run(() =>
    {
        var session = new AuthoringSession();
        var view = new AssetExplorerView();
        var window = new Window { Width = 400, Height = 600, Content = view };
        window.Show();
        view.Attach(session);
        Dispatcher.UIThread.RunJobs();

        var refresh = view.FindControl<Button>("RefreshButton")!;
        Assert.True(refresh.IsEnabled);

        refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        // 무거운 일은 아직 시작 전이다 — 핸들러가 첫 await까지만 왔다.
        // 이 두 줄이 그려지지 않으면 사용자에게는 그냥 멈춘 창이다.
        Assert.False(refresh.IsEnabled);
        Assert.Contains("다시 읽는 중", session.StatusMessage, StringComparison.Ordinal);

        // 일이 끝나면 단추가 돌아오고 상태줄은 결과가 덮는다.
        Dispatcher.UIThread.RunJobs();

        Assert.True(refresh.IsEnabled);
        Assert.DoesNotContain("다시 읽는 중", session.StatusMessage, StringComparison.Ordinal);

        window.Close();
    });
}
