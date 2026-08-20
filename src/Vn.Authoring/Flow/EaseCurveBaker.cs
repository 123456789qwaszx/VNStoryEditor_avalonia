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

    public static CurveKey[] Bake(EaseKind kind)
    {
        const float h = 1f / 256f; // 중앙 차분 간격 — 골든 샘플 간격과 같은 크기.
        var keys = new CurveKey[KeyCount];

        for (int i = 0; i < KeyCount; i++)
        {
            float t = i / (float)(KeyCount - 1);
            float value = EaseFunctions.Evaluate(kind, t);

            // 경계에서는 단방향 차분 — 정의역 밖을 밟지 않는다.
            float before = t <= 0f ? value : EaseFunctions.Evaluate(kind, t - h);
            float after = t >= 1f ? value : EaseFunctions.Evaluate(kind, t + h);
            float tangent = (after - before) / ((t <= 0f || t >= 1f) ? h : 2f * h);

            keys[i] = new CurveKey(t, value, tangent, tangent);
        }

        // 끝값을 정확히 0·1로 — 이징에 따라 부동소수 꼬리가 남을 수 있다.
        keys[0] = new CurveKey(0f, EaseFunctions.Evaluate(kind, 0f), keys[0].InTangent, keys[0].OutTangent);
        keys[^1] = new CurveKey(1f, EaseFunctions.Evaluate(kind, 1f), keys[^1].InTangent, keys[^1].OutTangent);

        return keys;
    }
}
