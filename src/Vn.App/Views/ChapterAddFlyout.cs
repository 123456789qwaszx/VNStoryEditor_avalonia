using Avalonia.Controls;
using Avalonia.Layout;
using Vn.App.Services;

namespace Vn.App.Views;

/// <summary>
/// <b>[＋ 챕터]의 창구</b> — 이름을 받아 <see cref="AuthoringSession.CreateChapter"/>로 넘긴다.
///
/// ⛔ <b>두 화면이 같은 것을 쓴다</b> (2026-09-16): [챕터 그래프]의 툴바·판 한가운데 단추와
/// [대본] 탭 탐색기 머리의 [＋]. 창구가 두 벌이면 <b>어느 쪽으로 만들었느냐에 따라 챕터가
/// 달라진다</b> — 이 저장소가 같은 실수를 자동 길 검사에서 한 번 했다(V1).
///
/// ⚠ 만든 <b>뒤</b>의 일은 화면마다 다르므로 <paramref name="created"/>로 넘긴다
/// (챕터 그래프는 제 목록을 다시 읽고 그 챕터를 고른다).
/// </summary>
internal static class ChapterAddFlyout
{
    public static void ShowAt(Control anchor, AuthoringSession session, Action<string>? created = null)
    {
        var panel = new StackPanel { Spacing = 4, MinWidth = 220 };

        var name = new TextBox
        {
            PlaceholderText = "챕터 Id (예: ch06) — 파일 이름이 됩니다",
            FontSize = 11
        };

        panel.Children.Add(name);

        var flyout = new Flyout { Content = panel };

        var create = new Button
        {
            Content = "만들기",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        void Submit() => UiGuard.Run(session, "새 챕터", () =>
        {
            string chapterId = name.Text?.Trim() ?? string.Empty;

            if (session.CreateChapter(chapterId) is { } failure)
            {
                session.SetStatus(failure);
                return;
            }

            flyout.Hide();
            created?.Invoke(chapterId);
            session.SetStatus($"챕터 '{chapterId}'를 만들었습니다. 장면은 챕터 줄을 우클릭해 더합니다.");
        });

        create.Click += (_, _) => Submit();

        // 이름을 치고 바로 Enter — 이 창구는 한 칸짜리라 마우스로 돌아갈 이유가 없다.
        name.KeyDown += (_, args) =>
        {
            if (args.Key == Avalonia.Input.Key.Enter)
            {
                args.Handled = true;
                Submit();
            }
        };

        panel.Children.Add(create);

        flyout.ShowAt(anchor);
        name.Focus();
    }
}
