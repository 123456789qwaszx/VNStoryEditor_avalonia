using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    public sealed class PrimitivesTests
    {
        [Test]
        public void Vec2_연산자()
        {
            Assert.That(new Vec2(1f, 2f) + new Vec2(3f, 4f), Is.EqualTo(new Vec2(4f, 6f)));
            Assert.That(new Vec2(3f, 4f) - new Vec2(1f, 2f), Is.EqualTo(new Vec2(2f, 2f)));
            Assert.That(new Vec2(1f, 2f) * 2f, Is.EqualTo(new Vec2(2f, 4f)));
            Assert.That(Vec2.Scale(new Vec2(2f, 3f), new Vec2(4f, 5f)), Is.EqualTo(new Vec2(8f, 15f)));
            Assert.That(-new Vec2(1f, -2f), Is.EqualTo(new Vec2(-1f, 2f)));
        }

        [Test]
        public void Vec3_연산자와_XY()
        {
            Assert.That(new Vec3(1f, 2f, 3f) + new Vec3(4f, 5f, 6f), Is.EqualTo(new Vec3(5f, 7f, 9f)));
            Assert.That(Vec3.Scale(new Vec3(2f, 3f, 4f), new Vec3(5f, 6f, 7f)), Is.EqualTo(new Vec3(10f, 18f, 28f)));
            Assert.That(new Vec3(1f, 2f, 3f).XY, Is.EqualTo(new Vec2(1f, 2f)));
            Assert.That(new Vec3(new Vec2(1f, 2f)), Is.EqualTo(new Vec3(1f, 2f, 0f)));
        }

        [Test]
        public void Rgba_저장과_알파_교체()
        {
            Rgba c = new Rgba(0.1f, 0.2f, 0.3f, 0.4f);

            Assert.That(c.R, Is.EqualTo(0.1f));
            Assert.That(c.A, Is.EqualTo(0.4f));
            Assert.That(c.WithAlpha(1f), Is.EqualTo(new Rgba(0.1f, 0.2f, 0.3f, 1f)));
            Assert.That(new Rgba(1f, 1f, 1f), Is.EqualTo(Rgba.White));
        }

        [Test]
        public void 값_동등성()
        {
            Assert.That(new Vec2(1f, 2f) == new Vec2(1f, 2f), Is.True);
            Assert.That(new Vec2(1f, 2f) != new Vec2(1f, 2.0001f), Is.True);
            Assert.That(new Vec3(1f, 2f, 3f) == new Vec3(1f, 2f, 3f), Is.True);
            Assert.That(Rgba.Clear == new Rgba(0f, 0f, 0f, 0f), Is.True);
        }

        [Test]
        public void RectNodeState_StretchFull은_빌더_기본값과_같다()
        {
            RectNodeState s = RectNodeState.StretchFull;

            Assert.That(s.AnchorMin, Is.EqualTo(Vec2.Zero));
            Assert.That(s.AnchorMax, Is.EqualTo(Vec2.One));
            Assert.That(s.Pivot, Is.EqualTo(Vec2.Half));
            Assert.That(s.SizeDelta, Is.EqualTo(Vec2.Zero));
            Assert.That(s.AnchoredPosition, Is.EqualTo(Vec2.Zero));
            Assert.That(s.LocalScale, Is.EqualTo(Vec3.One));
            Assert.That(s.LocalEulerAngles, Is.EqualTo(Vec3.Zero));
        }

        [Test]
        public void RectNodeState_With는_해당_필드만_바꾼다()
        {
            RectNodeState s = RectNodeState.StretchFull
                .WithAnchoredPosition(new Vec2(1f, 2f))
                .WithPivot(new Vec2(0.5f, 0f));

            Assert.That(s.AnchoredPosition, Is.EqualTo(new Vec2(1f, 2f)));
            Assert.That(s.Pivot, Is.EqualTo(new Vec2(0.5f, 0f)));
            Assert.That(s.LocalScale, Is.EqualTo(Vec3.One));
            Assert.That(s.AnchorMax, Is.EqualTo(Vec2.One));
        }
    }
}
