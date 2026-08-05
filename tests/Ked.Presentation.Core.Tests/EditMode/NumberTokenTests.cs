using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// 단위도 클램프도 없는 맨 숫자 파서. 문화권 무관(InvariantCulture)이 핵심이다.
    /// </summary>
    public sealed class NumberTokenTests
    {
        private const float Eps = 1e-4f;

        [TestCase("1.5", 1.5f)]
        [TestCase("2", 2f)]
        [TestCase("-3", -3f)]          // 클램프하지 않는다
        [TestCase("-0.25", -0.25f)]
        [TestCase("  2  ", 2f)]        // 앞뒤 공백
        [TestCase("+4", 4f)]
        [TestCase("1e3", 1000f)]
        [TestCase("0", 0f)]
        public void TryParseFloat_읽는다(string token, float expected)
        {
            Assert.IsTrue(NumberToken.TryParseFloat(token, out float value), token);
            Assert.That(value, Is.EqualTo(expected).Within(Eps));
        }

        [TestCase((string)null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("abc")]
        [TestCase("3u")]               // 단위는 이 파서의 일이 아니다
        [TestCase("1s")]
        public void TryParseFloat_못_읽으면_false(string token)
        {
            Assert.IsFalse(NumberToken.TryParseFloat(token, out float value), token);
            Assert.That(value, Is.EqualTo(0f));
        }

        [Test]
        public void 소수점은_항상_점이다()
        {
            // 실행 환경의 문화권이 쉼표를 쓰더라도 규약은 InvariantCulture다.
            // 이것이 깨지면 같은 대본이 기계마다 다르게 읽힌다.
            Assert.IsTrue(NumberToken.TryParseFloat("1.5", out float dot));
            Assert.That(dot, Is.EqualTo(1.5f).Within(Eps));

            Assert.IsFalse(NumberToken.TryParseFloat("1,5", out _));
        }
    }
}
