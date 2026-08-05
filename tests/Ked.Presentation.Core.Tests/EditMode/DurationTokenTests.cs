using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// Nfr = N/24초, Ns = N초, 단위 없는 N = N초(하위호환). 결과는 0으로 클램프.
    /// </summary>
    public sealed class DurationTokenTests
    {
        private const float Eps = 1e-4f;

        // ── 초 ────────────────────────────────────────────────────────

        [TestCase("12fr", 0.5f)]
        [TestCase("24fr", 1f)]
        [TestCase("0fr", 0f)]
        [TestCase("1.2s", 1.2f)]
        [TestCase("0.4", 0.4f)]        // 단위 없는 숫자는 초
        [TestCase("2", 2f)]
        [TestCase("12FR", 0.5f)]       // 대소문자
        [TestCase("1S", 1f)]
        [TestCase("  1s  ", 1f)]       // 앞뒤 공백
        public void TryParseSeconds_읽는다(string token, float expected)
        {
            Assert.IsTrue(DurationToken.TryParseSeconds(token, out float seconds), token);
            Assert.That(seconds, Is.EqualTo(expected).Within(Eps));
        }

        [TestCase("-1s")]
        [TestCase("-12fr")]
        [TestCase("-0.4")]
        public void TryParseSeconds_음수는_0으로_클램프한다(string token)
        {
            // 음수는 "즉시 적용"을 뜻하므로 실패가 아니라 0이다.
            Assert.IsTrue(DurationToken.TryParseSeconds(token, out float seconds), token);
            Assert.That(seconds, Is.EqualTo(0f).Within(Eps));
        }

        [TestCase((string)null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("s")]                // 숫자가 없다
        [TestCase("fr")]
        [TestCase("abc")]
        [TestCase("1m")]               // 이 규약의 단위가 아니다
        public void TryParseSeconds_못_읽으면_false(string token)
        {
            Assert.IsFalse(DurationToken.TryParseSeconds(token, out float seconds), token);
            Assert.That(seconds, Is.EqualTo(0f));
        }

        [Test]
        public void TryParseSeconds는_frames_축약을_받지_않는다()
        {
            // 현 규약을 그대로 고정한다: "12frames"는 "s"로 끝나 "12frame"이 남고 파싱에 실패한다.
            // TryParseFrames만 frame/frames를 받는다. 이 비대칭은 b-0 범위 밖이라 손대지 않고 남긴다.
            Assert.IsFalse(DurationToken.TryParseSeconds("12frames", out _));
            Assert.IsFalse(DurationToken.TryParseSeconds("12frame", out _));
        }

        // ── 프레임 ────────────────────────────────────────────────────

        [TestCase("12fr", 12f)]
        [TestCase("12frame", 12f)]
        [TestCase("12frames", 12f)]
        [TestCase("12", 12f)]          // 단위 없는 숫자는 프레임
        [TestCase("1.5fr", 1.5f)]
        [TestCase("12FRAMES", 12f)]
        [TestCase("  12fr  ", 12f)]
        public void TryParseFrames_읽는다(string token, float expected)
        {
            Assert.IsTrue(DurationToken.TryParseFrames(token, out float frames), token);
            Assert.That(frames, Is.EqualTo(expected).Within(Eps));
        }

        [TestCase("-5fr")]
        [TestCase("-5")]
        public void TryParseFrames_음수는_0으로_클램프한다(string token)
        {
            Assert.IsTrue(DurationToken.TryParseFrames(token, out float frames), token);
            Assert.That(frames, Is.EqualTo(0f).Within(Eps));
        }

        [TestCase((string)null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("fr")]
        [TestCase("frames")]
        [TestCase("abc")]
        public void TryParseFrames_못_읽으면_false(string token)
        {
            Assert.IsFalse(DurationToken.TryParseFrames(token, out float frames), token);
            Assert.That(frames, Is.EqualTo(0f));
        }

        // ── 환산 ──────────────────────────────────────────────────────

        [TestCase(24f, 1f)]
        [TestCase(12f, 0.5f)]
        [TestCase(0f, 0f)]
        [TestCase(-12f, 0f)]           // 음수는 0
        public void FramesToSeconds_는_24fps_기준이다(float frames, float expected)
        {
            Assert.That(DurationToken.FramesToSeconds(frames), Is.EqualTo(expected).Within(Eps));
            Assert.That(DurationToken.FramesPerSecond, Is.EqualTo(24f));
        }
    }
}
