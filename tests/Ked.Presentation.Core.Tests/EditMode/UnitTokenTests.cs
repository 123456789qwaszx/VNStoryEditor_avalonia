using System;
using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    // 1u = 기준 폭 / 48 (D-core-1). 기준 폭이 바뀌면 같은 토큰이 다른 픽셀을 내야 함.
    public sealed class UnitTokenTests
    {
        private const float Fhd = 1920f;   // 지금까지 쓰던 기준 폭. 1u == 40px
        private const float Uhd = 3840f;   // 두 배 기준 폭. 1u == 80px
        private const float Eps = 1e-4f;

        // ── 파싱: 유닛 수 ──────────────────────────────────────────────

        [TestCase("3u", 3f)]
        [TestCase("3", 3f)]
        [TestCase("-3u", -3f)]
        [TestCase("-3", -3f)]
        [TestCase("3.5u", 3.5f)]
        [TestCase("-0.25u", -0.25f)]
        [TestCase("3U", 3f)]          // 대문자 단위
        [TestCase("  3u  ", 3f)]      // 앞뒤 공백
        [TestCase("0u", 0f)]
        public void TryParseUnits_읽는다(string token, float expected)
        {
            Assert.IsTrue(UnitToken.TryParseUnits(token, out float units), token);
            Assert.That(units, Is.EqualTo(expected).Within(Eps));
        }

        [TestCase((string)null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("u")]               // 숫자가 없다
        [TestCase("abc")]
        [TestCase("3px")]             // 이 규약의 단위가 아니다
        [TestCase("3u5")]
        public void TryParseUnits_못_읽으면_false(string token)
        {
            Assert.IsFalse(UnitToken.TryParseUnits(token, out float units), token);
            Assert.That(units, Is.EqualTo(0f));
        }

        // ── 환산: 기준 폭 파생 ────────────────────────────────────────

        [Test]
        public void PixelsPerUnit_은_기준_폭의_48분의_1이다()
        {
            Assert.That(UnitToken.PixelsPerUnit(Fhd), Is.EqualTo(40f).Within(Eps));
            Assert.That(UnitToken.PixelsPerUnit(Uhd), Is.EqualTo(80f).Within(Eps));
            Assert.That(UnitToken.PixelsPerUnit(1280f), Is.EqualTo(1280f / 48f).Within(Eps));
        }

        [Test]
        public void UnitsToPixels_는_기준_폭에_비례한다()
        {
            Assert.That(UnitToken.UnitsToPixels(1f, Fhd), Is.EqualTo(40f).Within(Eps));
            Assert.That(UnitToken.UnitsToPixels(1f, Uhd), Is.EqualTo(80f).Within(Eps));
            Assert.That(UnitToken.UnitsToPixels(-2.5f, Fhd), Is.EqualTo(-100f).Within(Eps));
        }

        /// <summary>수용 기준: 1920에서 지금과 같은 픽셀, 3840에서 정확히 두 배.</summary>
        [TestCase("3u", 120f)]
        [TestCase("1u", 40f)]
        [TestCase("0.5u", 20f)]
        [TestCase("48u", 1920f)]      // 48u가 화면 폭 전체다
        public void TryParsePixels_기준_폭_1920은_종전과_같고_3840은_두_배다(string token, float pixelsAtFhd)
        {
            Assert.IsTrue(UnitToken.TryParsePixels(token, Fhd, out float atFhd));
            Assert.That(atFhd, Is.EqualTo(pixelsAtFhd).Within(Eps));

            Assert.IsTrue(UnitToken.TryParsePixels(token, Uhd, out float atUhd));
            Assert.That(atUhd, Is.EqualTo(pixelsAtFhd * 2f).Within(Eps));
        }

        [TestCase("-3u")]
        [TestCase("-0.5")]
        public void TryParsePixels_는_음수를_0으로_클램프한다(string token)
        {
            Assert.IsTrue(UnitToken.TryParsePixels(token, Fhd, out float pixels));
            Assert.That(pixels, Is.EqualTo(0f).Within(Eps));
        }

        [TestCase("-3u", -120f)]
        [TestCase("-0.5u", -20f)]
        [TestCase("3u", 120f)]
        public void TryParseSignedPixels_는_부호를_보존한다(string token, float pixelsAtFhd)
        {
            Assert.IsTrue(UnitToken.TryParseSignedPixels(token, Fhd, out float atFhd));
            Assert.That(atFhd, Is.EqualTo(pixelsAtFhd).Within(Eps));

            Assert.IsTrue(UnitToken.TryParseSignedPixels(token, Uhd, out float atUhd));
            Assert.That(atUhd, Is.EqualTo(pixelsAtFhd * 2f).Within(Eps));
        }

        [TestCase((string)null)]
        [TestCase("")]
        [TestCase("abc")]
        public void 픽셀_파서도_못_읽으면_false(string token)
        {
            Assert.IsFalse(UnitToken.TryParsePixels(token, Fhd, out float clamped), token);
            Assert.That(clamped, Is.EqualTo(0f));

            Assert.IsFalse(UnitToken.TryParseSignedPixels(token, Fhd, out float signed), token);
            Assert.That(signed, Is.EqualTo(0f));
        }

        // ── 기준 폭이 유효하지 않은 것은 파싱 실패가 아니라 호출자 버그다 ──

        [TestCase(0f)]
        [TestCase(-1920f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void 유효하지_않은_기준_폭은_조용히_넘어가지_않는다(float badWidth)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => { UnitToken.PixelsPerUnit(badWidth); });

            Assert.Throws<ArgumentOutOfRangeException>(
                () => { UnitToken.TryParsePixels("3u", badWidth, out _); });

            Assert.Throws<ArgumentOutOfRangeException>(
                () => { UnitToken.TryParseSignedPixels("3u", badWidth, out _); });
        }

        [Test]
        public void 토큰이_틀렸어도_기준_폭_오류가_먼저_드러난다()
        {
            // 토큰 파싱 실패로 false만 돌려주면 잘못된 기준 폭이 묻힌다.
            Assert.Throws<ArgumentOutOfRangeException>(
                () => { UnitToken.TryParsePixels("abc", 0f, out _); });
        }

        [Test]
        public void 기본_기준_폭은_폴백_값일_뿐이다()
        {
            // 상수 이름이 "1u의 크기"를 뜻하지 않는다는 것을 고정한다.
            Assert.That(UnitToken.DefaultReferenceStageWidth, Is.EqualTo(1920f));
            Assert.That(UnitToken.StageWidthDivisor, Is.EqualTo(48f));
            Assert.That(
                UnitToken.PixelsPerUnit(UnitToken.DefaultReferenceStageWidth),
                Is.EqualTo(40f).Within(Eps));
        }
    }
}