namespace Vn.Core.Yarn;

/// <summary>
/// Yarn Spinner가 스스로 제공해서 game.schema.json에 선언할 필요가 없는 이름들.
///
/// 기준 버전: YarnSpinner.Compiler 3.2.1 (Directory.Packages.props).
/// 컴파일러를 올릴 때 이 목록도 같이 확인해야 한다.
///
/// 주의: <c>set</c>, <c>declare</c>, <c>if</c>, <c>elseif</c>, <c>else</c>, <c>endif</c>,
/// <c>jump</c>, <c>detour</c>, <c>return</c>, <c>call</c>, <c>once</c>, <c>endonce</c>,
/// <c>local</c>, <c>enum</c>, <c>case</c>, <c>endenum</c>은 렉서 키워드라서
/// 애초에 <c>NodeMetadata.CommandCalls</c>에 들어오지 않는다. 여기 넣을 필요가 없다.
/// 실제로 명령 호출로 넘어오는 내장 명령은 아래 둘뿐이다 (3.2.1에서 실측 확인).
/// </summary>
internal static class YarnBuiltIns
{
    public static IReadOnlySet<string> Commands { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            // 대화를 즉시 종료한다. Dialogue 가상 머신이 직접 처리한다.
            "stop",

            // 지정한 초만큼 대기한다. DialogueRunner가 기본 등록한다.
            "wait"
        };
}
