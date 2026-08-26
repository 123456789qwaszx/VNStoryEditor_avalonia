using System.Reflection;
using Avalonia;
using Avalonia.Platform;

namespace Vn.App.Tests;

/// <summary>
/// 헤드리스 테스트의 플랫폼 설정 — 실물(<see cref="DefaultPlatformSettings"/>) 그대로에
/// <b>더블탭 시간 창 하나만</b> 잠시 열어 둘 수 있게 덮었다.
///
/// <b>왜 있는가</b> — 합성 더블클릭(MouseDown/Up 둘)은 두 눌림 사이가 플랫폼 더블탭 시간
/// (기본 500ms 고정) 안이어야 제스처로 묶이는데, 그 간격은 <b>진짜 시계</b>로 잰다
/// (헤드리스 입력의 Timestamp가 Stopwatch 실시간이다). 전체 스위트 부하에서 GC·스케줄링이
/// 끼면 500ms가 그냥 넘어가고, 그날그날 다른 테스트가 하나씩 떨어진다 — 2026-08-26에
/// PresentationGraphStageJumpTests 셋이 각각 한 번씩 그렇게 죽었다.
///
/// 시간 창을 <b>프로세스 전체에서</b> 늘려 두면 반대 사고가 난다: 같은 자리를 두 번 따로
/// 누르는 다른 테스트의 클릭 둘이 더블클릭으로 묶인다. 그래서 창은
/// <see cref="HoldDoubleTapWindowOpen"/> 스코프 안에서만 열린다.
///
/// <b>왜 DispatchProxy인가</b> — Avalonia 12의 참조 어셈블리는 <see cref="IPlatformSettings"/>
/// 구현도 <see cref="DefaultPlatformSettings"/> 상속도 컴파일 수준에서 막는다(PrivateApi 봉인).
/// 런타임 어셈블리에는 그 봉인이 없어서, 런타임에 인터페이스를 대신 구현해 주는
/// <see cref="DispatchProxy"/>로 감싸면 된다. MouseDevice는 클릭마다
/// <c>AvaloniaLocator</c>에서 설정을 새로 꺼내므로 로케이터를 되묶는 것으로 충분하다.
/// </summary>
public class TestPlatformSettings : DispatchProxy
{
    /// <summary>설치된 프록시 하나 — <see cref="Install"/>이 채운다.</summary>
    public static TestPlatformSettings Instance { get; private set; } = null!;

    private IPlatformSettings _inner = null!;

    /// <summary>열려 있는 동안 더블탭 시간 창이 사실상 무한대다.</summary>
    private bool _doubleTapWindowHeldOpen;

    /// <summary>
    /// 입력 배선이 이 설정을 실제로 지나갔는지 세는 눈금.
    /// <see cref="PresentationGraphStageJumpTests"/>의 DoubleClick이 이 값으로 배선 단절을
    /// <b>소리 나게</b> 만든다 — Avalonia가 설정을 다른 데서 읽게 바뀌면 간헐 실패가 아니라
    /// 즉시 실패로 드러난다.
    /// </summary>
    public int DoubleTapTimeReads { get; private set; }

    /// <summary>
    /// 헤드리스 플랫폼이 등록해 둔 실물 설정을 감싸 로케이터에 되묶는다.
    /// AvaloniaLocator도 참조 어셈블리에서 봉인이라(위 PrivateApi) 리플렉션으로 만진다.
    /// </summary>
    public static void Install()
    {
        Type locator = typeof(AvaloniaObject).Assembly.GetType("Avalonia.AvaloniaLocator")
            ?? throw new InvalidOperationException("Avalonia.AvaloniaLocator가 사라졌다");

        object current = locator.GetProperty("Current")!.GetValue(null)!;
        IPlatformSettings inner = (IPlatformSettings?)current.GetType()
                .GetMethod("GetService")!.Invoke(current, [typeof(IPlatformSettings)])
            ?? throw new InvalidOperationException("헤드리스 플랫폼이 IPlatformSettings를 등록하기 전이다");

        IPlatformSettings proxy = DispatchProxy.Create<IPlatformSettings, TestPlatformSettings>();
        Instance = (TestPlatformSettings)(object)proxy;
        Instance._inner = inner;

        object currentMutable = locator.GetProperty("CurrentMutable")!.GetValue(null)!;
        locator.GetMethod("BindToSelf")!
            .MakeGenericMethod(typeof(IPlatformSettings))
            .Invoke(currentMutable, [proxy]);
    }

    public IDisposable HoldDoubleTapWindowOpen()
    {
        _doubleTapWindowHeldOpen = true;
        return new Closer(this);
    }

    private sealed class Closer(TestPlatformSettings owner) : IDisposable
    {
        public void Dispose() => owner._doubleTapWindowHeldOpen = false;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (string.Equals(targetMethod!.Name, nameof(IPlatformSettings.GetDoubleTapTime), StringComparison.Ordinal))
        {
            DoubleTapTimeReads++;

            if (_doubleTapWindowHeldOpen)
            {
                return TimeSpan.FromDays(1);
            }
        }

        return targetMethod.Invoke(_inner, args);
    }
}
