using System;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 커스텀 이징 곡선의 키 하나 — Unity Keyframe의 순수 대응
    /// (time · value · inTangent · outTangent, 가중 탄젠트는 비범위).
    /// readonly가 아닌 이유: 커맨드 스펙([Serializable])에 실려 유니티 직렬화를
    /// 타야 한다 — readonly 필드는 유니티가 직렬화하지 못한다.
    /// </summary>
    [Serializable]
    public struct CurveKey
    {
        public float Time;
        public float Value;
        public float InTangent;
        public float OutTangent;

        public CurveKey(float time, float value, float inTangent, float outTangent)
        {
            Time = time;
            Value = value;
            InTangent = inTangent;
            OutTangent = outTangent;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // 커스텀 곡선 평가 — Unity AnimationCurve.Evaluate의 코어 대응.
    //
    // 형태 규약은 EaseFunctions와 같다: 순수, UnityEngine 타입 금지.
    // 구간 양끝 키의 value·tangent로 3차 Hermite 보간, 범위 밖은 끝값 클램프,
    // 무한 탄젠트는 계단(앞 키 값 유지) — 전부 AnimationCurve와 같은 규칙이다.
    //
    // 등가의 심판은 EditMode 테스트 CurveFunctionsAnimationCurveParityTests:
    // 같은 키로 만든 AnimationCurve.Evaluate와 대표 곡선 × 257샘플 < 1e-4.
    // 양쪽이 이 함수 하나를 쓴다: VnTool 프리뷰·스크럽도, 호스트 트윈의
    // 커스텀 이즈 델리게이트도.
    // ─────────────────────────────────────────────────────────────────
    public static class CurveFunctions
    {
        public static float Evaluate(CurveKey[] keys, float t)
        {
            if (keys == null || keys.Length == 0)
                return 0f;

            if (keys.Length == 1)
                return keys[0].Value;

            int last = keys.Length - 1;

            if (t <= keys[0].Time)
                return keys[0].Value;
            if (t >= keys[last].Time)
                return keys[last].Value;

            // 구간 탐색 — 키는 t 오름차순(로더가 검증). 키 수가 작아 선형이 맞다.
            int i = 0;
            while (i < last - 1 && t >= keys[i + 1].Time)
                i++;

            CurveKey k0 = keys[i];
            CurveKey k1 = keys[i + 1];

            float dt = k1.Time - k0.Time;

            // 겹친 키(dt<=0)는 앞 키 값 — 0 나눗셈 가드.
            if (dt <= 0f)
                return k0.Value;

            // 무한 탄젠트 = 계단 구간. AnimationCurve와 같은 처리다.
            if (float.IsInfinity(k0.OutTangent) || float.IsInfinity(k1.InTangent))
                return k0.Value;

            float u = (t - k0.Time) / dt;
            float m0 = k0.OutTangent * dt;
            float m1 = k1.InTangent * dt;

            float u2 = u * u;
            float u3 = u2 * u;

            float h00 = 2f * u3 - 3f * u2 + 1f;
            float h10 = u3 - 2f * u2 + u;
            float h01 = -2f * u3 + 3f * u2;
            float h11 = u3 - u2;

            return h00 * k0.Value + h10 * m0 + h01 * k1.Value + h11 * m1;
        }
    }
}
