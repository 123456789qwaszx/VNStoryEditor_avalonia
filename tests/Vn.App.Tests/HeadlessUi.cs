using Avalonia;
using Avalonia.Headless;
using Vn.App;

namespace Vn.App.Tests;

/// <summary>
/// 화면 없는 UI 검증의 실행 자리.
///
/// <b>왜 Avalonia.Headless.XUnit을 쓰지 않았는가</b> — 그 어댑터(12.1.1)는 xunit v3를 끌고 오는데
/// 이 저장소는 xunit 2.9.2다. 한 어셈블리에 두 xunit이 들어오면 <c>FactAttribute</c>가 중복돼
/// 테스트 프로젝트 전체가 컴파일되지 않는다. 어댑터 없이 세션을 직접 몰면 <c>[Fact]</c> 그대로
/// 쓸 수 있고 저장소의 테스트 체계를 건드리지 않는다.
///
/// 세션은 프로세스당 하나다. Avalonia는 UI 스레드가 하나뿐이라 테스트마다 새로 만들 수 없다.
/// </summary>
internal static class HeadlessUi
{
    static HeadlessUi()
    {
        // ⛔ 테스트가 개발자의 진짜 설정(%APPDATA%\VnTool)을 읽지 않게 한다 (2026-08-26).
        // MainWindow는 뜨자마자 최근 프로젝트를 복원하므로(OnOpened), 이게 없으면 창을
        // 띄우는 모든 테스트가 <b>개발자가 그날 만지던 실제 프로젝트</b>를 열어 버린다 —
        // 그 프로젝트의 상태에 따라 무관한 테스트들이 무작위로 죽고(실사례: 소유자가
        // 앱에서 실험하던 날 스위트가 계속 흔들렸다), 세션이 실제 원고 폴더에 감시자를
        // 걸고 테스트 노드를 더한다. 임시 경로에는 설정 파일이 없으므로 복원은 조용히
        // 건너뛰고, 테스트가 남기는 설정 저장도 임시 폴더에 떨어진다.
        Vn.App.Services.AppSettingsService.SettingsPathOverride = Path.Combine(
            Path.GetTempPath(), "vn-app-tests-settings", Guid.NewGuid().ToString("N"), "settings.json");
    }

    private static readonly Lazy<HeadlessUnitTestSession> SessionHandle =
        new(() => HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder)));

    /// <summary>
    /// 지금 이 스레드가 <see cref="Run"/> 안인가.
    ///
    /// ⚠ <c>Dispatcher.UIThread.CheckAccess()</c>로는 알 수 없다 — 세션 밖(xUnit 스레드)에서
    /// 물으면 <b>참을 돌려준다</b>(그쪽은 세션의 디스패처가 아니다). 그 대답을 믿고 창을
    /// 만지면 "다른 스레드가 소유한 객체"로 터진다. 그래서 우리가 직접 표시해 둔다.
    /// </summary>
    [ThreadStatic]
    private static bool _inside;

    public static bool OnUiThread => _inside;

    /// <summary>UI 스레드에서 본문을 돌리고 끝날 때까지 기다린다.</summary>
    public static void Run(Action body) =>
        SessionHandle.Value.Dispatch(
            () =>
            {
                _inside = true;

                try
                {
                    body();
                }
                finally
                {
                    _inside = false;
                }
            },
            CancellationToken.None).GetAwaiter().GetResult();
}

/// <summary>
/// 헤드리스 앱 진입점. 실제 <see cref="App"/>을 그대로 쓴다 — 테마·리소스가 진짜와 같아야
/// "그려진다"가 의미를 갖는다. <see cref="App.OnFrameworkInitializationCompleted"/>는 데스크톱
/// 수명주기일 때만 MainWindow를 만들므로 헤드리스에서는 주 창이 뜨지 않는다.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
