using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// 값 계층의 골든. 여기 있는 성질이 깨지면 그 위 좌표 계산은 볼 필요도 없다.
    /// </summary>
    public sealed class PrimitivesTests
    {
        [Test]
        public void Vec2_연산자()
        {
            Assert.That(new Vec2(1f, 2f) + new Vec2(3f, 4f), Is.EqualTo(new Vec2(4f, 6f)));
            Assert.That(new Vec2(3f, 4f) - new Vec2(1f, 2f), Is.EqualTo(new Vec2(2f, 2f)));
            Assert.That(-new Vec2(1f, -2f), Is.EqualTo(new Vec2(-1f, 2f)));
            Assert.That(new Vec2(1f, 2f) * 2f, Is.EqualTo(new Vec2(2f, 4f)));
            Assert.That(2f * new Vec2(1f, 2f), Is.EqualTo(new Vec2(2f, 4f)));

            // Scale은 성분별 곱이다 — 내적이나 스칼라 곱이 아니다.
            Assert.That(Vec2.Scale(new Vec2(2f, 3f), new Vec2(4f, 5f)), Is.EqualTo(new Vec2(8f, 15f)));
        }

        [Test]
        public void Vec2_상수()
        {
            Assert.That(Vec2.Zero, Is.EqualTo(new Vec2(0f, 0f)));
            Assert.That(Vec2.One, Is.EqualTo(new Vec2(1f, 1f)));
            Assert.That(Vec2.Half, Is.EqualTo(new Vec2(0.5f, 0.5f)));
        }

        [Test]
        public void Vec3_연산자와_XY()
        {
            Assert.That(new Vec3(1f, 2f, 3f) + new Vec3(4f, 5f, 6f), Is.EqualTo(new Vec3(5f, 7f, 9f)));
            Assert.That(new Vec3(4f, 5f, 6f) - new Vec3(1f, 2f, 3f), Is.EqualTo(new Vec3(3f, 3f, 3f)));
            Assert.That(-new Vec3(1f, -2f, 3f), Is.EqualTo(new Vec3(-1f, 2f, -3f)));
            Assert.That(new Vec3(1f, 2f, 3f) * 2f, Is.EqualTo(new Vec3(2f, 4f, 6f)));

            Assert.That(
                Vec3.Scale(new Vec3(2f, 3f, 4f), new Vec3(5f, 6f, 7f)),
                Is.EqualTo(new Vec3(10f, 18f, 28f)));

            Assert.That(new Vec3(1f, 2f, 3f).XY, Is.EqualTo(new Vec2(1f, 2f)));
        }

        [Test]
        public void Vec3_Vec2_승격은_z가_0이다()
        {
            // 좌표 계산의 입출력이 대개 이 모양이다 — 기본 z가 1이면 전부 어긋난다.
            Assert.That(new Vec3(new Vec2(1f, 2f)), Is.EqualTo(new Vec3(1f, 2f, 0f)));
            Assert.That(new Vec3(new Vec2(1f, 2f), 5f), Is.EqualTo(new Vec3(1f, 2f, 5f)));
        }

        [Test]
        public void Rgba_저장과_알파_교체()
        {
            Rgba c = new Rgba(0.1f, 0.2f, 0.3f, 0.4f);

            Assert.That(c.R, Is.EqualTo(0.1f));
            Assert.That(c.G, Is.EqualTo(0.2f));
            Assert.That(c.B, Is.EqualTo(0.3f));
            Assert.That(c.A, Is.EqualTo(0.4f));

            Assert.That(c.WithAlpha(1f), Is.EqualTo(new Rgba(0.1f, 0.2f, 0.3f, 1f)));

            // 알파 기본값은 1 — 불투명이 기본이다.
            Assert.That(new Rgba(1f, 1f, 1f), Is.EqualTo(Rgba.White));
            Assert.That(Rgba.Black, Is.EqualTo(new Rgba(0f, 0f, 0f, 1f)));
            Assert.That(Rgba.Clear, Is.EqualTo(new Rgba(0f, 0f, 0f, 0f)));
        }

        [Test]
        public void 값_동등성은_정확_일치다()
        {
            // ε가 들어 있지 않다는 것이 규약이다. 근사 비교는 StageStateComparer의 일이다.
            Assert.That(new Vec2(1f, 2f) == new Vec2(1f, 2f), Is.True);
            Assert.That(new Vec2(1f, 2f) != new Vec2(1f, 2.0001f), Is.True);

            Assert.That(new Vec3(1f, 2f, 3f) == new Vec3(1f, 2f, 3f), Is.True);
            Assert.That(new Vec3(1f, 2f, 3f) != new Vec3(1f, 2f, 3.0001f), Is.True);

            Assert.That(Rgba.Clear == new Rgba(0f, 0f, 0f, 0f), Is.True);
            Assert.That(Rgba.White != Rgba.Black, Is.True);
        }

        [Test]
        public void 같은_값은_같은_해시다()
        {
            // Dictionary 키로 쓰이는 자리가 있다(트리 조회는 문자열이지만 값 캐시는 이쪽).
            Assert.That(new Vec2(1.5f, -2.5f).GetHashCode(), Is.EqualTo(new Vec2(1.5f, -2.5f).GetHashCode()));
            Assert.That(new Vec3(1.5f, -2.5f, 3f).GetHashCode(), Is.EqualTo(new Vec3(1.5f, -2.5f, 3f).GetHashCode()));
        }

        [Test]
        public void ToString은_로캘에_흔들리지_않는다()
        {
            // 테스트 실패 메시지와 리포트 JSON이 이 표기를 쓴다 —
            // 로캘에 따라 소수점이 쉼표가 되면 리포트를 기계로 읽을 수 없다.
            Assert.That(new Vec2(1.5f, 2.5f).ToString(), Is.EqualTo("(1.5, 2.5)"));
            Assert.That(new Vec3(1.5f, 2.5f, 3.5f).ToString(), Is.EqualTo("(1.5, 2.5, 3.5)"));
        }

        [Test]
        public void RectNodeState_StretchFull은_빌더_기본값과_같다()
        {
            RectNodeState s = RectNodeState.StretchFull;

            Assert.That(s.AnchoredPosition, Is.EqualTo(Vec2.Zero));
            Assert.That(s.AnchorMin, Is.EqualTo(Vec2.Zero));
            Assert.That(s.AnchorMax, Is.EqualTo(Vec2.One));
            Assert.That(s.Pivot, Is.EqualTo(Vec2.Half));
            Assert.That(s.SizeDelta, Is.EqualTo(Vec2.Zero));
            Assert.That(s.LocalScale, Is.EqualTo(Vec3.One));
            Assert.That(s.LocalEulerAngles, Is.EqualTo(Vec3.Zero));
        }

        [Test]
        public void RectNodeState_default는_초기_상태가_아니다()
        {
            // 이 차이를 모르고 default를 쓰면 좌표가 전부 원점으로 접힌다.
            // 실패하는 테스트가 아니라 규약을 못 박는 테스트다.
            Assert.That(default(RectNodeState).LocalScale, Is.EqualTo(Vec3.Zero));
            Assert.That(RectNodeState.StretchFull.LocalScale, Is.EqualTo(Vec3.One));
        }

        [Test]
        public void RectNodeState_With는_해당_필드만_바꾼다()
        {
            RectNodeState s = RectNodeState.StretchFull
                .WithAnchoredPosition(new Vec2(1f, 2f))
                .WithPivot(new Vec2(0.5f, 0f));

            Assert.That(s.AnchoredPosition, Is.EqualTo(new Vec2(1f, 2f)));
            Assert.That(s.Pivot, Is.EqualTo(new Vec2(0.5f, 0f)));

            // 건드리지 않은 필드는 그대로다.
            Assert.That(s.AnchorMin, Is.EqualTo(Vec2.Zero));
            Assert.That(s.AnchorMax, Is.EqualTo(Vec2.One));
            Assert.That(s.SizeDelta, Is.EqualTo(Vec2.Zero));
            Assert.That(s.LocalScale, Is.EqualTo(Vec3.One));
            Assert.That(s.LocalEulerAngles, Is.EqualTo(Vec3.Zero));
        }

        [Test]
        public void RectNodeState_With는_원본을_바꾸지_않는다()
        {
            RectNodeState original = RectNodeState.StretchFull;
            RectNodeState moved = original.WithAnchoredPosition(new Vec2(10f, 20f));

            Assert.That(original.AnchoredPosition, Is.EqualTo(Vec2.Zero));
            Assert.That(moved.AnchoredPosition, Is.EqualTo(new Vec2(10f, 20f)));
        }

        [Test]
        public void RectSpace_Centered는_가운데_pivot이다()
        {
            RectSpace space = RectSpace.Centered(1920f, 1080f);

            Assert.That(space.Size, Is.EqualTo(new Vec2(1920f, 1080f)));
            Assert.That(space.Pivot, Is.EqualTo(Vec2.Half));
        }
    }
}
