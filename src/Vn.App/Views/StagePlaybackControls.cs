using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using Vn.App.Services;

namespace Vn.App.Views;

/// <summary>
/// 재생 컨트롤 행([⏮]·[▶/⏸]·진행 표시)의 단일 구현 — 도킹 패널과 분리 창이 같은
/// <see cref="StagePlayback"/> 하나에 이 행을 각자 붙인다(사본 금지). 라벨은
/// StateChanged를 따라가고, 화면에서 떨어지면 구독을 걷는다(창 닫힘 누수 방지).
/// </summary>
internal static class StagePlaybackControls
{
    public static Control Build(StagePlayback playback)
    {
        var restartButton = new Button { Content = "⏮", FontSize = 11, Padding = new Thickness(8, 3) };
        ToolTip.SetTip(restartButton, "처음부터");

        var playButton = new Button { FontSize = 11, Padding = new Thickness(8, 3) };

        var progress = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 34
        };

        void Refresh()
        {
            playButton.Content = playback.IsPlaying ? "⏸ 일시정지" : "▶ 재생";
            playButton.IsEnabled = playback.CanPlay;
            restartButton.IsEnabled = playback.CanPlay;
            progress.Text = playback.ProgressLabel;
        }

        restartButton.Click += (_, _) => playback.Restart();
        playButton.Click += (_, _) => playback.TogglePlay();

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(restartButton);
        row.Children.Add(playButton);
        row.Children.Add(progress);

        row.AttachedToVisualTree += (_, _) =>
        {
            playback.StateChanged += Refresh;
            Refresh();
        };
        row.DetachedFromVisualTree += (_, _) => playback.StateChanged -= Refresh;

        Refresh();
        return row;
    }
}
