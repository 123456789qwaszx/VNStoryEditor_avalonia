using Ked.Presentation.Core;

namespace Vn.Authoring.Flow;

/// <summary>
/// 표준 이징을 커스텀 곡선의 시작점으로 굽는다 (W67 후속 — 소유자: "선택한 이징에서
/// 출발해 키를 줘서 조절").
///
/// 키 5개(t = 0·¼·½·¾·1), 값은 <see cref="EaseFunctions"/> 그대로, 탄젠트는 중앙 차분 —
/// 완벽 재현이 아니라 <b>편집하기 좋은 출발점</b>이 목표다(키가 적어야 만질 만하다).
/// 얼마나 가까운지는 테스트가 수치로 고정한다.
/// </summary>
public static class EaseCurveBaker
{
    public const int KeyCount = 5;

    /// <summary>
    /// 진동은 키가 더 필요하다 (2026-08-21 `gesture`) — 핑퐁은 <b>가운데가 봉우리</b>라
    /// 5키(간격 ¼)로는 그 꼭대기와 양쪽 어깨를 한 번에 못 잡는다. 9키(간격 ⅛)면
    /// t=0.5가 키로 서고 어깨도 남는다.
    /// </summary>
    public const int OscillationKeyCount = 9;

    public static CurveKey[] Bake(EaseKind kind) =>
        Bake(kind, CurveKind.Motion);

    /// <summary>
    /// 표준 이징을 곡선 키로 굽는다. <paramref name="curveKind"/>가 진동이면
    /// <see cref="OscillationFunctions.PingPong"/>을 굽는다 — 그러면 끝점이 (0,0)·(1,0)이라
    /// <b>구운 결과가 그대로 유효한 진동 곡선</b>이고, 표준 이징에서 손으로 그리는 곡선으로
    /// 넘어가는 길이 이어진다(증보 지시서의 그 이음매).
    /// </summary>
    public static CurveKey[] Bake(EaseKind kind, CurveKind curveKind)
    {
        bool oscillation = curveKind == CurveKind.Oscillation;
        Func<float, float> shape = oscillation
            ? t => OscillationFunctions.PingPong(kind, t)
            : t => EaseFunctions.Evaluate(kind, t);

        const float h = 1f / 256f; // 중앙 차분 간격 — 골든 샘플 간격과 같은 크기.
        int count = oscillation ? OscillationKeyCount : KeyCount;
        var keys = new CurveKey[count];

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float value = shape(t);

            // 경계에서는 단방향 차분 — 정의역 밖을 밟지 않는다.
            float before = t <= 0f ? value : shape(t - h);
            float after = t >= 1f ? value : shape(t + h);
            float tangent = (after - before) / ((t <= 0f || t >= 1f) ? h : 2f * h);

            keys[i] = new CurveKey(t, value, tangent, tangent);
        }

        // 끝값을 계약 자리로 못 박는다 — 이징에 따라 부동소수 꼬리가 남고, 진동은
        // 그 꼬리 하나로 CurveKindRules가 종류를 못 읽어 통째로 버려질 수 있다.
        float first = oscillation ? 0f : shape(0f);
        float last = oscillation ? 0f : shape(1f);
        keys[0] = new CurveKey(0f, first, keys[0].InTangent, keys[0].OutTangent);
        keys[^1] = new CurveKey(1f, last, keys[^1].InTangent, keys[^1].OutTangent);

        return keys;
    }
}
