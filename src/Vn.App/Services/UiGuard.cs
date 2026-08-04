namespace Vn.App.Services;

/// <summary>
/// 편집기 이벤트 핸들러의 예외 포획 단일 구현 (공통 불변식 4 — 크래시 금지).
///
/// UI 이벤트 핸들러에서 새어 나간 예외는 Avalonia 디스패처를 지나 프로세스를 죽인다 —
/// 8/1의 AnalysisView NRE 크래시 3건이 그 실증이다. 클릭 하나가 앱과 미저장 원고를
/// 같이 끝내서는 안 된다. 실패는 <b>무동작 + 상태줄 사유</b>가 된다(조용히 삼키지 않는다).
/// </summary>
internal static class UiGuard
{
    /// <summary>동작 하나를 포획 아래서 실행한다. 성공이면 true, 예외였으면 false.</summary>
    public static bool Run(AuthoringSession? session, string context, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception exception)
        {
            Report(session, context, exception);
            return false;
        }
    }

    /// <summary>async void 핸들러용. await 이후의 예외도 프로세스를 죽이기 전에 잡는다.</summary>
    public static async Task RunAsync(AuthoringSession? session, string context, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            Report(session, context, exception);
        }
    }

    public static void Report(AuthoringSession? session, string context, Exception exception)
    {
        StartupLog.TryWrite(context, exception);
        session?.SetStatus($"{context} 중 오류가 발생해 동작을 취소했습니다: {exception.Message}");
    }
}
