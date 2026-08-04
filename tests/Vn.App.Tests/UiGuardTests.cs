using Vn.App.Services;

namespace Vn.App.Tests;

/// <summary>
/// X1 회귀 — 편집기 이벤트 핸들러의 예외는 앱을 끝내는 대신
/// 무동작 + 상태줄 사유가 되어야 한다(공통 불변식 4).
/// </summary>
public class UiGuardTests
{
    [Fact]
    public void 예외는_삼켜지지_않고_상태줄_사유가_된다()
    {
        var session = new AuthoringSession();

        bool ok = UiGuard.Run(session, "이미지 클릭", () =>
            throw new NullReferenceException("테스트 NRE"));

        Assert.False(ok);
        Assert.Contains("이미지 클릭", session.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("테스트 NRE", session.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void 성공한_동작은_상태줄을_건드리지_않는다()
    {
        var session = new AuthoringSession();
        string before = session.StatusMessage;

        Assert.True(UiGuard.Run(session, "정상 동작", () => { }));
        Assert.Equal(before, session.StatusMessage);
    }

    [Fact]
    public async Task async_핸들러의_await_이후_예외도_잡힌다()
    {
        var session = new AuthoringSession();

        await UiGuard.RunAsync(session, "드래그", async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("드래그 실패");
        });

        Assert.Contains("드래그 실패", session.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void 세션이_없어도_포획은_동작한다()
    {
        Assert.False(UiGuard.Run(null, "초기화 전", () => throw new InvalidOperationException()));
    }
}
