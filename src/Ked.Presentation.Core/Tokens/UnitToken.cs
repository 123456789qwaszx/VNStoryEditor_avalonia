using System;
using System.Globalization;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 거리 단위 토큰 규약.
    ///   1u = 가로 기준 해상도 / 48
    ///
    /// 기준 해상도가 다른 게임에서도 "화면 폭의 1/48"이라는 같은 비율,
    /// 환산에 쓰는 기준 폭은 코드 상수가 아니라 호출자가 넘기는 값.
    /// 나중에 코어의 tuning 인자로 사용 예정.
    ///
    /// 음수 허용 여부가 커맨드마다 다르다 — 그래서 진입점이 둘.
    ///
    /// 파싱 실패는 로그로 흘리지 않고 false로 돌려준다. 무엇을 할지는 호출자가 정한다.
    /// 반면 기준 폭이 유효하지 않은 것은 파싱 실패가 아니라 호출자의 버그이므로 예외로 던진다.
    /// </summary>
    public static class UnitToken
    {
        /// <summary>규약 상수. 화면 가로를 48칸으로 나눈 것이 1u.</summary>
        public const float StageWidthDivisor = 48f;

        /// <summary>
        /// 기준 폭을 아직 데이터에서 얻지 못한 호출부를 위한 폴백 값일 뿐이다.
        /// "1u의 크기"를 뜻하지 않는다 — 크기는 항상 기준 폭에서 파생된다.
        /// </summary>
        public const float DefaultReferenceStageWidth = 1920f;

        // 기준 폭에서 파생되는 1u의 픽셀 크기. 1920이면 40px, 3840이면 80px.
        public static float PixelsPerUnit(float referenceStageWidth)
            => RequireReferenceStageWidth(referenceStageWidth) / StageWidthDivisor;

        public static float UnitsToPixels(float units, float referenceStageWidth)
            => units * PixelsPerUnit(referenceStageWidth);

        // "3u" / "3" -> 유닛 수. 부호를 그대로 보존.
        public static bool TryParseUnits(string token, out float units)
        {
            units = 0f;

            if (string.IsNullOrWhiteSpace(token))
                return false;

            string normalized = token.Trim().ToLowerInvariant();

            if (normalized.EndsWith("u", StringComparison.Ordinal))
                normalized = normalized.Substring(0, normalized.Length - 1);

            return float.TryParse(
                normalized,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out units);
        }

        // "3u" / "3" -> 픽셀. 음수는 0으로 클램프한다.
        public static bool TryParsePixels(string token, float referenceStageWidth, out float pixels)
        {
            float pixelsPerUnit = PixelsPerUnit(referenceStageWidth);

            if (!TryParseUnits(token, out float units))
            {
                pixels = 0f;
                return false;
            }

            pixels = Math.Max(0f, units) * pixelsPerUnit;
            return true;
        }

        // "-3u" 를 그대로 살린다.
        public static bool TryParseSignedPixels(string token, float referenceStageWidth, out float pixels)
        {
            float pixelsPerUnit = PixelsPerUnit(referenceStageWidth);

            if (!TryParseUnits(token, out float units))
            {
                pixels = 0f;
                return false;
            }

            pixels = units * pixelsPerUnit;
            return true;
        }

        private static float RequireReferenceStageWidth(float referenceStageWidth)
        {
            if (!(referenceStageWidth > 0f) || float.IsInfinity(referenceStageWidth))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(referenceStageWidth),
                    referenceStageWidth,
                    "기준 스테이지 폭은 유한한 양수여야 한다. 게임 데이터에서 오는 값이므로 " +
                    "조용히 폴백하지 않고 알린다.");
            }

            return referenceStageWidth;
        }
    }
}