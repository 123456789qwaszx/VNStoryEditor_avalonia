using System;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 곡선의 쓰임새. 끝값이 정체를 가른다 — 선언이 아니라 키에서 파생된다.
    /// (그래서 curves.json 스키마는 그대로다.)
    /// </summary>
    public enum CurveKind
    {
        /// <summary>이동 곡선: (0,0) → (1,1). move_by·place·size·shot·scale·rotate가 쓴다.
        /// 종점이 명목 목표값이라 끝이 1이어야 한다.</summary>
        Motion,

        /// <summary>진동 곡선: (0,0) → (1,0). gesture가 쓴다.
        /// 순변위 0이 정체라 끝이 0이어야 한다 — 중간은 자유(음수 = 반대 방향).</summary>
        Oscillation,
    }

    // ─────────────────────────────────────────────────────────────────
    // 곡선 종류 판정 — 런타임 로더와 툴 저작 검증이 같은 규칙을 써야 한다.
    // 한쪽만 알면 "툴에서는 되는데 재생에서 사라지는" 곡선이 생긴다.
    // ─────────────────────────────────────────────────────────────────
    public static class CurveKindRules
    {
        /// <summary>끝값 판정 허용 오차. JSON 왕복의 부동소수 잡음만 흡수한다.</summary>
        public const float EndpointTolerance = 1e-4f;

        public static bool TryClassify(CurveKey[] keys, out CurveKind kind, out string reason)
        {
            kind = CurveKind.Motion;

            if (keys == null || keys.Length < 2)
            {
                reason = "키가 2개 이상이어야 한다";
                return false;
            }

            float first = keys[0].Value;
            float last = keys[keys.Length - 1].Value;

            // 시작은 두 종류 모두 0이다 — 0이 아닌 데서 시작하면 첫 프레임이 튄다.
            if (Math.Abs(first) > EndpointTolerance)
            {
                reason = $"첫 키 v가 0이 아니다 ({first})";
                return false;
            }

            if (Math.Abs(last - 1f) <= EndpointTolerance)
            {
                kind = CurveKind.Motion;
                reason = null;
                return true;
            }

            if (Math.Abs(last) <= EndpointTolerance)
            {
                kind = CurveKind.Oscillation;
                reason = null;
                return true;
            }

            reason =
                $"마지막 키 v가 1도 0도 아니다 ({last}) — " +
                "이동 곡선은 1로, 진동 곡선은 0으로 끝나야 한다";

            return false;
        }
    }
}
