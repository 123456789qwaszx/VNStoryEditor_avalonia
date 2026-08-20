using System;

namespace Ked.Presentation.Core
{
    // ─────────────────────────────────────────────────────────────────
    // 이징 순수 함수 — DOTween EaseManager.Evaluate의 코어 포팅.
    //
    // 형태 규약은 reduction-boundary.md 그대로: 순수, UnityEngine 타입 금지,
    // 시간·랜덤·IO·전역 상태 없음. 같은 입력은 언제나 같은 출력.
    //
    // 수식은 DOTween 소스(EaseManager.cs · Bounce.cs · Flash.cs)를 식 단위로
    // 옮긴 것이다 — 모양을 다듬으면 등가가 깨질 수 있어 일부러 원문 형태를
    // 유지한다(compound assignment 포함). 등가의 심판은 EditMode 테스트
    // EaseFunctionsDOTweenParityTests다: DOTween을 참조할 수 있는 유일한
    // 자리에서 전 항목 × 257샘플 오차 < 1e-4를 고정한다.
    //
    // 호스트 트윈 경로(SetEase)는 이 함수를 쓰지 않는다 — DOTween이 그대로
    // 시간의 세계를 진다. 첫 고객은 VnTool 프리뷰(W66b 시간 재현)다.
    // ─────────────────────────────────────────────────────────────────
    public static class EaseFunctions
    {
        /// <summary>DOTween.defaultEaseOvershootOrAmplitude와 같은 값. Back·Elastic·Flash가 쓴다.</summary>
        public const float DefaultOvershootOrAmplitude = 1.70158f;

        /// <summary>DOTween.defaultEasePeriod와 같은 값. Elastic·Flash가 쓴다.
        /// Elastic은 period 0을 "duration의 0.3배"(InOut은 0.45배)로 해석한다 — Evaluate 본문 참조.</summary>
        public const float DefaultPeriod = 0f;

        private const float Pi = (float)Math.PI;
        private const float PiOver2 = Pi * 0.5f;
        private const float TwoPi = Pi * 2f;

        /// <summary>정규화 시간 t ∈ [0,1], DOTween 기본 상수로 평가한다.</summary>
        public static float Evaluate(EaseKind kind, float t)
            => Evaluate(kind, t, 1f, DefaultOvershootOrAmplitude, DefaultPeriod);

        /// <summary>DOTween EaseManager.Evaluate와 같은 시그니처 의미:
        /// 경과 time / 전체 duration, 반환은 0..1 (Back·Elastic은 범위를 벗어날 수 있다).</summary>
        public static float Evaluate(
            EaseKind easeType, float time, float duration, float overshootOrAmplitude, float period)
        {
            switch (easeType) {
            case EaseKind.Linear:
                return time / duration;
            case EaseKind.InSine:
                return -(float)Math.Cos(time / duration * PiOver2) + 1;
            case EaseKind.OutSine:
                return (float)Math.Sin(time / duration * PiOver2);
            case EaseKind.InOutSine:
                return -0.5f * ((float)Math.Cos(Pi * time / duration) - 1);
            case EaseKind.InQuad:
                return (time /= duration) * time;
            case EaseKind.OutQuad:
                return -(time /= duration) * (time - 2);
            case EaseKind.InOutQuad:
                if ((time /= duration * 0.5f) < 1) return 0.5f * time * time;
                return -0.5f * ((--time) * (time - 2) - 1);
            case EaseKind.InCubic:
                return (time /= duration) * time * time;
            case EaseKind.OutCubic:
                return ((time = time / duration - 1) * time * time + 1);
            case EaseKind.InOutCubic:
                if ((time /= duration * 0.5f) < 1) return 0.5f * time * time * time;
                return 0.5f * ((time -= 2) * time * time + 2);
            case EaseKind.InQuart:
                return (time /= duration) * time * time * time;
            case EaseKind.OutQuart:
                return -((time = time / duration - 1) * time * time * time - 1);
            case EaseKind.InOutQuart:
                if ((time /= duration * 0.5f) < 1) return 0.5f * time * time * time * time;
                return -0.5f * ((time -= 2) * time * time * time - 2);
            case EaseKind.InQuint:
                return (time /= duration) * time * time * time * time;
            case EaseKind.OutQuint:
                return ((time = time / duration - 1) * time * time * time * time + 1);
            case EaseKind.InOutQuint:
                if ((time /= duration * 0.5f) < 1) return 0.5f * time * time * time * time * time;
                return 0.5f * ((time -= 2) * time * time * time * time + 2);
            case EaseKind.InExpo:
                return (time == 0) ? 0 : (float)Math.Pow(2, 10 * (time / duration - 1));
            case EaseKind.OutExpo:
                if (time == duration) return 1;
                return (-(float)Math.Pow(2, -10 * time / duration) + 1);
            case EaseKind.InOutExpo:
                if (time == 0) return 0;
                if (time == duration) return 1;
                if ((time /= duration * 0.5f) < 1) return 0.5f * (float)Math.Pow(2, 10 * (time - 1));
                return 0.5f * (-(float)Math.Pow(2, -10 * --time) + 2);
            case EaseKind.InCirc:
                return -((float)Math.Sqrt(1 - (time /= duration) * time) - 1);
            case EaseKind.OutCirc:
                return (float)Math.Sqrt(1 - (time = time / duration - 1) * time);
            case EaseKind.InOutCirc:
                if ((time /= duration * 0.5f) < 1) return -0.5f * ((float)Math.Sqrt(1 - time * time) - 1);
                return 0.5f * ((float)Math.Sqrt(1 - (time -= 2) * time) + 1);
            case EaseKind.InElastic:
                float s0;
                if (time == 0) return 0;
                if ((time /= duration) == 1) return 1;
                if (period == 0) period = duration * 0.3f;
                if (overshootOrAmplitude < 1) {
                    overshootOrAmplitude = 1;
                    s0 = period / 4;
                } else s0 = period / TwoPi * (float)Math.Asin(1 / overshootOrAmplitude);
                return -(overshootOrAmplitude * (float)Math.Pow(2, 10 * (time -= 1)) * (float)Math.Sin((time * duration - s0) * TwoPi / period));
            case EaseKind.OutElastic:
                float s1;
                if (time == 0) return 0;
                if ((time /= duration) == 1) return 1;
                if (period == 0) period = duration * 0.3f;
                if (overshootOrAmplitude < 1) {
                    overshootOrAmplitude = 1;
                    s1 = period / 4;
                } else s1 = period / TwoPi * (float)Math.Asin(1 / overshootOrAmplitude);
                return (overshootOrAmplitude * (float)Math.Pow(2, -10 * time) * (float)Math.Sin((time * duration - s1) * TwoPi / period) + 1);
            case EaseKind.InOutElastic:
                float s;
                if (time == 0) return 0;
                if ((time /= duration * 0.5f) == 2) return 1;
                if (period == 0) period = duration * (0.3f * 1.5f);
                if (overshootOrAmplitude < 1) {
                    overshootOrAmplitude = 1;
                    s = period / 4;
                } else s = period / TwoPi * (float)Math.Asin(1 / overshootOrAmplitude);
                if (time < 1) return -0.5f * (overshootOrAmplitude * (float)Math.Pow(2, 10 * (time -= 1)) * (float)Math.Sin((time * duration - s) * TwoPi / period));
                return overshootOrAmplitude * (float)Math.Pow(2, -10 * (time -= 1)) * (float)Math.Sin((time * duration - s) * TwoPi / period) * 0.5f + 1;
            case EaseKind.InBack:
                return (time /= duration) * time * ((overshootOrAmplitude + 1) * time - overshootOrAmplitude);
            case EaseKind.OutBack:
                return ((time = time / duration - 1) * time * ((overshootOrAmplitude + 1) * time + overshootOrAmplitude) + 1);
            case EaseKind.InOutBack:
                if ((time /= duration * 0.5f) < 1) return 0.5f * (time * time * (((overshootOrAmplitude *= (1.525f)) + 1) * time - overshootOrAmplitude));
                return 0.5f * ((time -= 2) * time * (((overshootOrAmplitude *= (1.525f)) + 1) * time + overshootOrAmplitude) + 2);
            case EaseKind.InBounce:
                return BounceEaseIn(time, duration);
            case EaseKind.OutBounce:
                return BounceEaseOut(time, duration);
            case EaseKind.InOutBounce:
                return BounceEaseInOut(time, duration);
            case EaseKind.Flash:
                return FlashEase(time, duration, overshootOrAmplitude, period);
            case EaseKind.InFlash:
                return FlashEaseIn(time, duration, overshootOrAmplitude, period);
            case EaseKind.OutFlash:
                return FlashEaseOut(time, duration, overshootOrAmplitude, period);
            case EaseKind.InOutFlash:
                return FlashEaseInOut(time, duration, overshootOrAmplitude, period);
            default:
                // DOTween과 같은 폴백: OutQuad.
                return -(time /= duration) * (time - 2);
            }
        }

        // ── Bounce (DG.Tweening.Core.Easing.Bounce 포팅 — Penner) ────────

        private static float BounceEaseIn(float time, float duration)
        {
            return 1 - BounceEaseOut(duration - time, duration);
        }

        private static float BounceEaseOut(float time, float duration)
        {
            if ((time /= duration) < (1 / 2.75f)) {
                return (7.5625f * time * time);
            }
            if (time < (2 / 2.75f)) {
                return (7.5625f * (time -= (1.5f / 2.75f)) * time + 0.75f);
            }
            if (time < (2.5f / 2.75f)) {
                return (7.5625f * (time -= (2.25f / 2.75f)) * time + 0.9375f);
            }
            return (7.5625f * (time -= (2.625f / 2.75f)) * time + 0.984375f);
        }

        private static float BounceEaseInOut(float time, float duration)
        {
            if (time < duration * 0.5f) {
                return BounceEaseIn(time * 2, duration) * 0.5f;
            }
            return BounceEaseOut(time * 2 - duration, duration) * 0.5f + 0.5f;
        }

        // ── Flash (DG.Tweening.Core.Easing.Flash 포팅) ───────────────────
        // overshootOrAmplitude = 깜빡임 스텝 수, period = 가중(0이면 무가중).

        private static float FlashEase(float time, float duration, float overshootOrAmplitude, float period)
        {
            int stepIndex = (int)Math.Ceiling((time / duration) * overshootOrAmplitude); // 1 to overshootOrAmplitude
            float stepDuration = duration / overshootOrAmplitude;
            time -= stepDuration * (stepIndex - 1);
            float dir = (stepIndex % 2 != 0) ? 1 : -1;
            if (dir < 0) time -= stepDuration;
            float res = (time * dir) / stepDuration;
            return FlashWeightedEase(overshootOrAmplitude, period, stepIndex, dir, res);
        }

        private static float FlashEaseIn(float time, float duration, float overshootOrAmplitude, float period)
        {
            int stepIndex = (int)Math.Ceiling((time / duration) * overshootOrAmplitude);
            float stepDuration = duration / overshootOrAmplitude;
            time -= stepDuration * (stepIndex - 1);
            float dir = (stepIndex % 2 != 0) ? 1 : -1;
            if (dir < 0) time -= stepDuration;
            time = time * dir;
            float res = (time /= stepDuration) * time;
            return FlashWeightedEase(overshootOrAmplitude, period, stepIndex, dir, res);
        }

        private static float FlashEaseOut(float time, float duration, float overshootOrAmplitude, float period)
        {
            int stepIndex = (int)Math.Ceiling((time / duration) * overshootOrAmplitude);
            float stepDuration = duration / overshootOrAmplitude;
            time -= stepDuration * (stepIndex - 1);
            float dir = (stepIndex % 2 != 0) ? 1 : -1;
            if (dir < 0) time -= stepDuration;
            time = time * dir;
            float res = -(time /= stepDuration) * (time - 2);
            return FlashWeightedEase(overshootOrAmplitude, period, stepIndex, dir, res);
        }

        private static float FlashEaseInOut(float time, float duration, float overshootOrAmplitude, float period)
        {
            int stepIndex = (int)Math.Ceiling((time / duration) * overshootOrAmplitude);
            float stepDuration = duration / overshootOrAmplitude;
            time -= stepDuration * (stepIndex - 1);
            float dir = (stepIndex % 2 != 0) ? 1 : -1;
            if (dir < 0) time -= stepDuration;
            time = time * dir;
            float res = (time /= stepDuration * 0.5f) < 1
                ? 0.5f * time * time
                : -0.5f * ((--time) * (time - 2) - 1);
            return FlashWeightedEase(overshootOrAmplitude, period, stepIndex, dir, res);
        }

        private static float FlashWeightedEase(
            float overshootOrAmplitude, float period, int stepIndex, float dir, float res)
        {
            float easedRes = 0;
            float finalDecimals = 0;
            // Use previous stepIndex in case of odd ones, so that back ease is not clamped
            if (dir > 0 && (int)overshootOrAmplitude % 2 == 0) stepIndex++;
            else if (dir < 0 && (int)overshootOrAmplitude % 2 != 0) stepIndex++;

            if (period > 0) {
                float finalTruncated = (float)Math.Truncate(overshootOrAmplitude);
                finalDecimals = overshootOrAmplitude - finalTruncated;
                if (finalTruncated % 2 > 0) finalDecimals = 1 - finalDecimals;
                finalDecimals = (finalDecimals * stepIndex) / overshootOrAmplitude;
                easedRes = (res * (overshootOrAmplitude - stepIndex)) / overshootOrAmplitude;
            } else if (period < 0) {
                period = -period;
                easedRes = (res * stepIndex) / overshootOrAmplitude;
            }
            float diff = easedRes - res;
            res += (diff * period) + finalDecimals;
            if (res > 1) res = 1;
            return res;
        }
    }
}