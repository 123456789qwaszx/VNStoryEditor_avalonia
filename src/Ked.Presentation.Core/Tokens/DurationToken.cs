using System;
using System.Globalization;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 시간 토큰 규약.
    ///   Nfr : N 프레임 (24fps 기준)
    ///   Ns  : N 초
    ///   N   : N 초 (단위 없음 — 하위호환)
    /// 결과는 0으로 클램프한다(음수는 즉시 적용을 뜻한다).
    ///
    /// 실패를 로그로 흘리지 않고 false로 돌려준다. 무엇을 할지는 호출자가 정한다.
    /// 런타임은 경고를 찍고 폴백하고, 저작 도구는 화면에 표시한다.
    /// </summary>
    public static class DurationToken
    {
        public const float FramesPerSecond = 24f;

        public static float FramesToSeconds(float frames)
            => Math.Max(0f, frames / FramesPerSecond);

        /// <summary>"12fr" / "1.2s" / "0.4" -> 초. 빈 문자열도 false다.</summary>
        public static bool TryParseSeconds(string token, out float seconds)
        {
            seconds = 0f;

            if (string.IsNullOrWhiteSpace(token))
                return false;

            string normalized = token.Trim().ToLowerInvariant();

            if (normalized.EndsWith("fr", StringComparison.Ordinal))
            {
                if (!TryParseFloat(normalized.Substring(0, normalized.Length - 2), out float frames))
                    return false;

                seconds = Math.Max(0f, frames / FramesPerSecond);
                return true;
            }

            if (normalized.EndsWith("s", StringComparison.Ordinal))
            {
                if (!TryParseFloat(normalized.Substring(0, normalized.Length - 1), out float parsedSeconds))
                    return false;

                seconds = Math.Max(0f, parsedSeconds);
                return true;
            }

            // 단위 없는 숫자는 초로 읽는다(하위호환).
            if (!TryParseFloat(normalized, out float rawSeconds))
                return false;

            seconds = Math.Max(0f, rawSeconds);
            return true;
        }

        /// <summary>"12fr" / "12frame" / "12frames" / "12" -> 프레임 수.</summary>
        public static bool TryParseFrames(string token, out float frames)
        {
            frames = 0f;

            if (string.IsNullOrWhiteSpace(token))
                return false;

            string normalized = token.Trim().ToLowerInvariant();

            if (normalized.EndsWith("frames", StringComparison.Ordinal))
                normalized = normalized.Substring(0, normalized.Length - 6);
            else if (normalized.EndsWith("frame", StringComparison.Ordinal))
                normalized = normalized.Substring(0, normalized.Length - 5);
            else if (normalized.EndsWith("fr", StringComparison.Ordinal))
                normalized = normalized.Substring(0, normalized.Length - 2);

            if (!TryParseFloat(normalized, out float parsed))
                return false;

            frames = Math.Max(0f, parsed);
            return true;
        }

        private static bool TryParseFloat(string text, out float value)
            => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}