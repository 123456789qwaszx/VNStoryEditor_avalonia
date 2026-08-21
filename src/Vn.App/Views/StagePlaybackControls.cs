using Avalonia;
using Avalonia.Controls;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Media;

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
/// - <see cref="BuildLeading"/> — 지금 다듬는 라인의 것: <c>[▶ 라인]</c> 하나
/// - <see cref="BuildTrailing"/> — 진행 표시 · 노드 전체 재생 · 배속
///
/// [⏮ 처음부터]는 걷혔다. 두 재생 버튼은 <b>각자 제 모드만</b> 비추고 아이콘도 갈린다
/// (라인 = ▶ 글리프 / 전체 = <b>꼬리 긴 화살표</b>) — 라인만 재생 중에 전체 재생 버튼이
/// ⏸로 바뀌던 것이 헷갈림의 정체였고, 같은 ▶ 하나로는 둘이 무엇이 다른지도 안 보였다.
/// </summary>
internal static class StagePlaybackControls
{
    /// <summary>
    /// 재생 줄 왼쪽 — 이 라인만 재생 하나뿐. 진행 표시는 오른쪽 묶음으로 갔다
    /// (2026-08-21 소유자: "1/3 역시 전체실행쪽으로").
    /// </summary>
    public static Control BuildLeading(StagePlayback playback)
    {
        // 이 라인만 재생 (2026-08-21 소유자) — 연출 하나를 반복해 돌려 보는 버튼.
        // 왼쪽 벽에 딱 붙지 않게 한 칸 띄운다(같은 날 소유자: "너무 딱 붙어 있는데").
        var lineButton = new Button
        {
            FontSize = 11,
            Padding = new Thickness(8, 3),
            Margin = new Thickness(10, 0, 0, 0)
        };

        void Refresh()
        {
            // 제 모드만 비춘다 — 전체 재생 중에는 여기가 ⏸가 되지 않는다.
            lineButton.Content = playback.IsPlayingLine ? "⏸ 라인" : "▶ 라인";
            ToolTip.SetTip(lineButton, playback.IsPlayingLine
                ? "이 라인 재생 멈추기"
                : "이 라인만 재생 — 끝나면 다음으로 넘어가지 않고 멈춥니다.");

            lineButton.IsEnabled = playback.CanPlay;
        }

        lineButton.Click += (_, _) => playback.ToggleLinePlay();

        return Row(playback, Refresh, lineButton);
    }

    /// <summary>
    /// 재생 줄 오른쪽 끝 — 진행 표시 · 노드 전체 재생 · 배속. 타임라인이 왼쪽 컨트롤과
    /// 이 묶음을 벌려 놓는다.
    /// </summary>
    public static Control BuildTrailing(StagePlayback playback)
    {
        var progress = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 34,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 0, 4, 0)
        };

        // 노드 전체 재생 — [▶ 라인]보다 <b>작게</b>, 아이콘은 <b>꼬리 긴 화살표</b>
        // (2026-08-21 소유자: "꼬리가 있는 화살표로 쭉 길게 간다는 느낌" · "조금 크기를
        // 줄이고"). 글리프가 아니라 도형인 이유 둘: 긴 화살표 글리프(⟶ 등)는 폰트에
        // 따라 두부가 되고, 꼬리 길이를 우리가 정할 수 있어야 "쭉 간다"가 보인다.
        var playButton = new Button
        {
            FontSize = 10,
            Padding = new Thickness(6, 2),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };

        ShapePath playIcon = Icon(
            // 꼬리(가는 막대)에서 머리(삼각형)로 한 획. 꼬리가 길어 "쭉 간다"가 읽힌다.
            "M 0,3.6 L 11.5,3.6 L 11.5,0.6 L 18,4.5 L 11.5,8.4 L 11.5,5.4 L 0,5.4 Z");

        ShapePath pauseIcon = Icon(
            // 같은 폭(18)에 가운데 정렬 — 상태가 바뀌어도 단추 너비가 흔들리지 않는다.
            "M 4.75,0.6 L 7.75,0.6 L 7.75,8.4 L 4.75,8.4 Z " +
            "M 10.25,0.6 L 13.25,0.6 L 13.25,8.4 L 10.25,8.4 Z");

        Control playContent = IconRow(playIcon, "전체");
        Control pauseContent = IconRow(pauseIcon, "전체");

        var speedButton = new Button { FontSize = 11, Padding = new Thickness(8, 3), MinWidth = 44 };
        ToolTip.SetTip(speedButton, "재생 속도 배율 — 타자·전이·여운에 함께 적용됩니다.");

        void Refresh()
        {
            playButton.Content = playback.IsPlayingAll ? pauseContent : playContent;
            ToolTip.SetTip(playButton, playback.IsPlayingAll
                ? "노드 전체 재생 멈추기"
                : "노드 전체 재생 — 현재 라인부터 이 노드 끝까지 이어서 재생합니다.");

            playButton.IsEnabled = playback.CanPlay;

            // 아이콘 색은 단추의 글자색을 따라간다(테마를 따라가려고). Shape의 Fill은
            // 상속되지 않으므로 비활성일 때의 흐림도 여기서 함께 준다.
            IBrush foreground = playButton.Foreground ?? Brushes.Gainsboro;
            playIcon.Fill = foreground;
            pauseIcon.Fill = foreground;
            playIcon.Opacity = pauseIcon.Opacity = playback.CanPlay ? 1 : 0.45;

            speedButton.Content = $"{playback.SpeedMultiplier:0.#}×";
            progress.Text = playback.ProgressLabel;
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

        return Row(playback, Refresh, progress, playButton, speedButton);
    }

    /// <summary>고정 폭 18의 도형 아이콘 — 상태가 바뀌어도 자리가 흔들리지 않는다.</summary>
    private static ShapePath Icon(string geometry) => new()
    {
        Data = Geometry.Parse(geometry),
        Width = 18,
        Height = 9,
        Stretch = Stretch.None,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Control IconRow(ShapePath icon, string label)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(icon);
        row.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center
        });
        return row;
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
