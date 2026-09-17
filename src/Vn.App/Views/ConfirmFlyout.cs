using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Vn.App.Services;

namespace Vn.App.Views;

/// <summary>
/// <b>지우는 일 앞의 한 걸음</b> — 차림표·단추를 누른 것이 첫 걸음이고(이름 끝의 <c>…</c>가
/// 그 뜻이다), 여기가 <b>무엇이 사라지는지 읽고 누르는</b> 두 번째다.
///
/// ⚠ <b>확인 창을 띄우지 않는다.</b> 창은 방금 읽던 것을 덮는다 — 누른 자리 옆에 붙어 뜨는
/// 쪽이 <b>무엇을</b> 지우는 것인지 더 분명하다([챕터 그래프]가 세운 규율).
///
/// ⛔ 2026-09-18에 <see cref="ScriptView"/>에서 꺼냈다. [연출 그래프]에도 같은 걸음이
/// 필요해졌는데, 그때 복사했으면 <b>세 화면이 저마다 다른 색과 다른 손잡이</b>를 갖게 된다 —
/// 이 저장소가 반복해 다친 자리가 그것이다.
/// </summary>
internal static class ConfirmFlyout
{
    /// <param name="anchor">붙어 뜰 자리 — 사람이 방금 누른 것 옆이어야 한다.</param>
    /// <param name="caution">무엇이 사라지는지. <b>되돌릴 수 있는지</b>까지 적는다.</param>
    /// <returns>
    /// 뜬 확인 단추 — <b>테스트의 손잡이</b>다. 플라이아웃의 팝업은 창 밖에 살아
    /// 나무를 타고 내려가 찾을 수 없다.
    /// </returns>
    public static Button Show(
        Control anchor,
        AuthoringSession? session,
        string caution,
        string confirmText,
        Action act,
        Action? closed = null)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(act);

        var panel = new StackPanel { Spacing = 6, MaxWidth = 260 };

        panel.Children.Add(new TextBlock
        {
            Text = caution,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85
        });

        var flyout = new Flyout { Content = panel };

        var confirm = new Button
        {
            Content = confirmText,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,

            // 지우는 단추는 붉다 — 누르기 전에 색이 먼저 말한다.
            Foreground = new SolidColorBrush(Color.FromRgb(190, 60, 60))
        };

        confirm.Click += (_, _) => UiGuard.Run(session, confirmText, () =>
        {
            flyout.Hide();
            closed?.Invoke();
            act();
        });

        panel.Children.Add(confirm);
        flyout.ShowAt(anchor);

        return confirm;
    }
}
