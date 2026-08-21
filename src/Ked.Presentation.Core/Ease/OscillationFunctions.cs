using System;

namespace Ked.Presentation.Core
{
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
        /// <summary>내장 기본 혹 — 0 → 1 → 0 한 번. 곡선을 안 준 gesture가 이걸 탄다.</summary>
        public static float Bump(float t) => (float)Math.Sin(Math.PI * t);

        /// <summary>키가 없으면(null·빈 배열) 기본 혹, 있으면 그 곡선.</summary>
        public static float Evaluate(CurveKey[] keys, float t)
            => keys == null || keys.Length == 0
                ? Bump(t)
                : CurveFunctions.Evaluate(keys, t);
    }
}
