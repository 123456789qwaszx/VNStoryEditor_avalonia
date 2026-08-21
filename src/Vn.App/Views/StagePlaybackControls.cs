using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Vn.App.Services;

namespace Vn.App.Views;

/// <summary>
/// 재생 컨트롤 행([▶/⏸]·[▶ 라인]·진행 표시·배속)의 단일 구현 — 도킹 패널과 분리 창이
/// 같은 <see cref="StagePlayback"/> 하나에 이 행을 각자 붙인다(사본 금지). 라벨은
/// StateChanged를 따라가고, 화면에서 떨어지면 구독을 걷는다(창 닫힘 누수 방지).
///
/// <b>자리와 무게 (2026-08-21 소유자)</b>: [⏮ 처음부터]는 걷혔고, 전체 재생이 그 첫
/// 자리로 와서 <b>아이콘 하나</b>가 됐다("거의 쓰지도 않는게 너무 큰 위치를 차지").
/// 배속은 실제 게임에도 있는 기능이라 남기되 <b>끝자리에 흐리게</b> 물러난다.
/// 두 재생 버튼은 <b>각자 제 모드만</b> 비춘다 — 라인만 재생 중에 전체 재생 버튼이
/// ⏸로 바뀌던 것이 헷갈림의 정체였다.
/// </summary>
internal static class StagePlaybackControls
{
    public static Control Build(StagePlayback playback)
    {
        // 전체 재생 = 아이콘 하나. 글자를 뺀 만큼 [▶ 라인]과 무게가 갈린다.
        var playButton = new Button
        {
            FontSize = 12,
            Padding = new Thickness(7, 2),
            MinWidth = 30,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };

        // 이 라인만 재생 (2026-08-21 소유자) — 연출 하나를 반복해 돌려 보는 버튼.
        var lineButton = new Button { FontSize = 11, Padding = new Thickness(8, 3) };

        // 배속은 남기되 물러선다 (2026-08-21 소유자: "눈에 조금 잘 안보이도록 중요성을
        // 낮춰줘") — 단추 테두리를 지우고 흐리게, 행의 끝자리에. 가리키면 또렷해진다.
        var speedButton = new Button
        {
            FontSize = 10,
            Padding = new Thickness(4, 2),
            MinWidth = 30,
            Opacity = 0.4,
            Background = Brushes.Transparent,
            BorderThickness = default,
            Foreground = Brushes.Gainsboro
        };
        ToolTip.SetTip(speedButton, "재생 속도 배율 — 타자·전이·여운에 함께 적용됩니다.");
        speedButton.PointerEntered += (_, _) => speedButton.Opacity = 0.9;
        speedButton.PointerExited += (_, _) => speedButton.Opacity = 0.4;

        var progress = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 34
        };

        void Refresh()
        {
            // 각자 제 모드만 — 라인만 재생 중에는 [라인] 쪽이 ⏸가 된다.
            playButton.Content = playback.IsPlayingAll ? "⏸" : "▶";
            ToolTip.SetTip(playButton, playback.IsPlayingAll
                ? "일시정지"
                : "현재 라인부터 끝까지 재생");

            lineButton.Content = playback.IsPlayingLine ? "⏸ 라인" : "▶ 라인";
            ToolTip.SetTip(lineButton, playback.IsPlayingLine
                ? "이 라인 재생 멈추기"
                : "이 라인만 재생");

            playButton.IsEnabled = playback.CanPlay;
            lineButton.IsEnabled = playback.CanPlay;
            speedButton.Content = $"{playback.SpeedMultiplier:0.#}×";
            progress.Text = playback.ProgressLabel;
        }

        playButton.Click += (_, _) => playback.TogglePlay();
        lineButton.Click += (_, _) => playback.ToggleLinePlay();
        speedButton.Click += (_, _) =>
        {
            // 순환: 0.5 → 1 → 1.5 → 2 → 다시 0.5. 목록 밖 값(설정 파일 수기 편집)은 1로 합류.
            int index = Array.FindIndex(
                StagePlayback.SpeedSteps,
                step => Math.Abs(step - playback.SpeedMultiplier) < 0.0001);
            playback.SpeedMultiplier = index < 0
                ? 1
                : StagePlayback.SpeedSteps[(index + 1) % StagePlayback.SpeedSteps.Length];
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(playButton);
        row.Children.Add(lineButton);
        row.Children.Add(progress);
        row.Children.Add(speedButton); // 끝자리 — 있는 줄은 알되 먼저 눈에 들지는 않게

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
