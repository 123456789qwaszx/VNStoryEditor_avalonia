using Avalonia.Controls;
using Vn.App.Views;

namespace Vn.App.Tests;

/// <summary>
/// 테스트가 띄운 챕터 화면을 들고 있다가 <b>반드시 닫는다</b> (2026-08-18).
///
/// <b>왜 이게 따로 있어야 하는가</b> — 이 어셈블리는 Avalonia 디스패처 <b>하나</b>를 모든
/// 테스트가 나눠 쓴다(<see cref="HeadlessUi"/>). 그래서 창을 안 닫고 끝낸 테스트의
/// <see cref="ChapterGraphView"/>는 사라지지 않고, 그 뷰가 걸어 둔 파일 감시자도 살아 있다.
///
/// 그 감시자는 이미 지워진 임시 폴더를 보고 있다. 250ms 디바운스가 <b>타이머 스레드에서</b>
/// 깨어나 없어진 디스패처에 <c>Post</c>를 하면 <c>NullReferenceException</c>이 나는데,
/// 그 자리는 잡을 사람이 없어 <b>테스트 호스트가 통째로 죽는다.</b> 죽는 순간에 돌고 있던
/// 테스트가 실패로 찍히므로 — <b>매번 다른 테스트가</b>, 혼자 돌리면 통과하고, 부하가
/// 걸리면 더 자주 깨졌다. 며칠 "흔들리는 테스트"로 보이던 것의 정체가 이것이다.
///
/// 규칙은 하나다: <b>챕터 화면을 띄운 테스트는 닫는다.</b> 잊기 쉬우므로 여기 모아 둔다.
/// </summary>
internal sealed class OpenChapterViews
{
    private readonly List<(ChapterGraphView View, Window Window)> _open = [];

    /// <summary>이 화면의 뒷정리를 맡는다.</summary>
    public void Own(ChapterGraphView view, Window window) => _open.Add((view, window));

    /// <summary>
    /// 전부 닫는다. <b>임시 폴더를 지우기 전에</b> 불러야 한다 — 보고 있는 폴더를 먼저
    /// 지우면 그 삭제 자체가 사건이 되어 감시자를 한 번 더 깨운다.
    ///
    /// ⚠ <b>두 자리에서 불린다.</b> 테스트 클래스의 <c>Dispose</c>는 xUnit 스레드에서 돌고,
    /// <c>using var project</c>의 <c>Dispose</c>는 <see cref="HeadlessUi.Run"/> 람다 <b>안</b>
    /// 이라 이미 UI 스레드다. 창을 만지려면 UI 스레드여야 하지만, UI 스레드에서 다시
    /// <c>Dispatch</c>하면 자기를 기다려 멈춘다 — 그래서 어느 쪽인지 먼저 묻는다.
    /// </summary>
    public void CloseAll()
    {
        if (HeadlessUi.OnUiThread)
        {
            Close();
            return;
        }

        HeadlessUi.Run(Close);
    }

    private void Close()
    {
        foreach ((ChapterGraphView view, Window window) in _open)
        {
            view.DetachSession();
            window.Close();
        }

        _open.Clear();

        // 이미 예약돼 있던 일감은 여기서 소진한다 — 다음 테스트의 RunJobs()로 넘기지 않는다.
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }
}
