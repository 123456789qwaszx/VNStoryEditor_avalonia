using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using Vn.App.Services;

namespace Vn.App.Views;

/// <summary>
/// 재생 컨트롤의 단일 구현 — 붙이는 쪽이 여럿이어도 같은 <see cref="StagePlayback"/>
/// 하나를 본다(사본 금지). 라벨은 StateChanged를 따라가고, 화면에서 떨어지면 구독을
/// 걷는다(창 닫힘 누수 방지).
///
/// <b>자리가 무게를 가른다 (2026-08-21 소유자)</b> — 흐리게 만드는 대신 <b>타임라인을
/// 사이에 두고</b> 양 끝으로 갈랐다("타임라인 가장 오른쪽으로 보내서 위치상 안 겹치게"):
///
/// - <see cref="BuildLeading"/> — 지금 다듬는 라인의 것: <c>[▶ 라인]</c> · 진행 표시
/// - <see cref="BuildTrailing"/> — 가끔 쓰는 것: <c>[▶▶ 전체]</c> · 배속
///
/// [⏮ 처음부터]는 걷혔다. 두 재생 버튼은 <b>각자 제 모드만</b> 비추고 아이콘도 갈린다
/// (라인 ▶ / 전체 ▶▶) — 라인만 재생 중에 전체 재생 버튼이 ⏸로 바뀌던 것이 헷갈림의
/// 정체였고, 같은 ▶ 하나로는 둘이 무엇이 다른지도 보이지 않았다.
/// </summary>
internal static class StagePlaybackControls
{
    /// <summary>재생 줄 왼쪽 — 이 라인만 재생 + 진행 표시.</summary>
    public static Control BuildLeading(StagePlayback playback)
    {
        // 이 라인만 재생 (2026-08-21 소유자) — 연출 하나를 반복해 돌려 보는 버튼.
        var lineButton = new Button { FontSize = 11, Padding = new Thickness(8, 3) };

        var progress = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 34
        };

        void Refresh()
        {
            // 제 모드만 비춘다 — 전체 재생 중에는 여기가 ⏸가 되지 않는다.
            lineButton.Content = playback.IsPlayingLine ? "⏸ 라인" : "▶ 라인";
            ToolTip.SetTip(lineButton, playback.IsPlayingLine
                ? "이 라인 재생 멈추기"
                : "이 라인만 재생 — 끝나면 다음으로 넘어가지 않고 멈춥니다.");

            lineButton.IsEnabled = playback.CanPlay;
            progress.Text = playback.ProgressLabel;
        }

        lineButton.Click += (_, _) => playback.ToggleLinePlay();

        return Row(playback, Refresh, lineButton, progress);
    }

    /// <summary>
    /// 재생 줄 오른쪽 끝 — 노드 전체 재생 + 배속. 타임라인이 왼쪽 컨트롤과 이 둘을
    /// 벌려 놓는다.
    /// </summary>
    public static Control BuildTrailing(StagePlayback playback)
    {
        // 노드 전체 재생 — 아이콘을 겹으로(▶▶) 두어 [▶ 라인]과 한눈에 갈리고, "전체"가
        // 무엇의 전체인지는 툴팁이 마저 말한다 (2026-08-21 소유자: "아이콘을 라인재생과
        // 똑같이 하지말고 … 노드 전체 재생이라는게 드러나도록").
        var playButton = new Button
        {
            FontSize = 11,
            Padding = new Thickness(8, 3),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };

        var speedButton = new Button { FontSize = 11, Padding = new Thickness(8, 3), MinWidth = 44 };
        ToolTip.SetTip(speedButton, "재생 속도 배율 — 타자·전이·여운에 함께 적용됩니다.");

        void Refresh()
        {
            playButton.Content = playback.IsPlayingAll ? "⏸ 전체" : "▶▶ 전체";
            ToolTip.SetTip(playButton, playback.IsPlayingAll
                ? "노드 전체 재생 멈추기"
                : "노드 전체 재생 — 현재 라인부터 이 노드 끝까지 이어서 재생합니다.");

            playButton.IsEnabled = playback.CanPlay;
            speedButton.Content = $"{playback.SpeedMultiplier:0.#}×";
        }

        playButton.Click += (_, _) => playback.TogglePlay();
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

        return Row(playback, Refresh, playButton, speedButton);
    }

    /// <summary>
    /// 가로 줄 하나 + StateChanged 구독의 공통 배선. 붙을 때 구독하고 떨어질 때 걷는다 —
    /// 이 규칙이 두 벌이 되면 한쪽만 새는 날이 온다.
    /// </summary>
    private static Control Row(StagePlayback playback, Action refresh, params Control[] children)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        foreach (Control child in children)
        {
            row.Children.Add(child);
        }

        row.AttachedToVisualTree += (_, _) =>
        {
            playback.StateChanged += refresh;
            refresh();
        };
        row.DetachedFromVisualTree += (_, _) => playback.StateChanged -= refresh;

        refresh();
        return row;
    }
}
