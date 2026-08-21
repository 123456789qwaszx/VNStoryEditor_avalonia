using System;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// gesture 한 축의 진동 재료. 셋 중 하나로 읽힌다 —
    /// 곡선 키가 있으면 키가 이기고, 없고 이징이 있으면 그 이징의 핑퐁, 둘 다 없으면 기본 혹.
    ///
    /// 유니티 직렬화를 타야 해서(커맨드 스펙에 실린다) readonly가 아니다 — CurveKey와 같은 사정.
    /// </summary>
    [Serializable]
    public struct OscillationSource
    {
        /// <summary>진동 곡선 키. 있으면 이것이 이긴다(@이름으로 온 것).</summary>
        public CurveKey[] CurveKeys;

        /// <summary>곡선이 없을 때 Ease의 핑퐁을 쓸지. false면 기본 혹.</summary>
        public bool UseEase;

        /// <summary>UseEase일 때 핑퐁할 표준 이징.</summary>
        public EaseKind Ease;

        public static OscillationSource FromCurve(CurveKey[] keys)
            => new() { CurveKeys = keys };

        public static OscillationSource FromEase(EaseKind ease)
            => new() { UseEase = true, Ease = ease };

        /// <summary>기본 혹.</summary>
        public static OscillationSource Default => default;
    }

    // ─────────────────────────────────────────────────────────────────
    // 제자리 몸짓(gesture)의 진동 평가 — 정본은 여기다.
    //
    // 변위(t) = 진폭 × 곡선(t)이고, 곡선은 (0,0)에서 시작해 (1,0)으로 끝난다.
    // 그래서 순변위가 0이고, 리듀서는 내용을 안 보고 무변으로 접을 수 있다
    // ("이징은 종점에 관여하지 않는다"는 불변식이 유지된다).
    //
    // EaseFunctions·CurveFunctions와 같은 규칙: 순수 함수, UnityEngine 타입 금지.
    // 툴 프리뷰가 이 함수를 그대로 써야 "보이는 모양 = 재생하는 모양"이 된다.
    // ─────────────────────────────────────────────────────────────────
    public static class OscillationFunctions
    {
        /// <summary>내장 기본 혹 — 0 → 1 → 0 한 번. 곡선도 이징도 안 준 gesture가 이걸 탄다.</summary>
        public static float Bump(float t) => (float)Math.Sin(Math.PI * t);

        /// <summary>
        /// 표준 이징을 **나갔다 돌아오는 왕복의 절반**으로 읽는다.
        /// t=0에서 0 · t=0.5에서 이징의 종점(=1) · t=1에서 다시 0 —
        /// 순변위 0이 수식 자체로 지켜지므로 35종이 그대로 몸짓이 된다.
        /// (OutBack의 오버슈트처럼 중간이 1을 넘는 것은 정상이다.)
        /// </summary>
        public static float PingPong(EaseKind kind, float t)
            => EaseFunctions.Evaluate(kind, t < 0.5f ? t * 2f : 2f - (t * 2f));

        /// <summary>키가 없으면(null·빈 배열) 기본 혹, 있으면 그 곡선.</summary>
        public static float Evaluate(CurveKey[] keys, float t)
            => keys == null || keys.Length == 0
                ? Bump(t)
                : CurveFunctions.Evaluate(keys, t);

        /// <summary>
        /// 축 하나의 진동 값. 우선순위는 곡선 키 → 이징 핑퐁 → 기본 혹이다.
        /// 이 한 함수가 gesture의 모양을 전부 정한다 — 툴 프리뷰도 이걸 부른다.
        /// </summary>
        public static float Evaluate(in OscillationSource source, float t)
        {
            if (source.CurveKeys is { Length: > 0 })
                return CurveFunctions.Evaluate(source.CurveKeys, t);

            return source.UseEase ? PingPong(source.Ease, t) : Bump(t);
        }
    }
}
