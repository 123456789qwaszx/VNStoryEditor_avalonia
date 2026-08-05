using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// 순수 수학 검증 — 손으로 계산한 기대값. 유니티와의 대조는 UnityParity 하네스가 한다.
    /// </summary>
    public sealed class RectChainMathTests
    {
        // 순수 계산 검증이므로 float 한 줄 연산 수준의 오차만 허용한다.
        private const float Eps = 1e-3f;

        private static readonly RectSpace Stage = RectSpace.Centered(1920f, 1080f);

        // ── RectSize ─────────────────────────────────────────────────

        [Test]
        public void 스트레치_풀_노드는_부모_크기를_그대로_받는다()
        {
            Vec2 size = RectChainMath.RectSize(new Vec2(1920f, 1080f), RectNodeState.StretchFull);

            Assert.That(size.X, Is.EqualTo(1920f).Within(Eps));
            Assert.That(size.Y, Is.EqualTo(1080f).Within(Eps));
        }

        [Test]
        public void 고정_앵커면_sizeDelta가_크기_그_자체다()
        {
            RectNodeState node = RectNodeState.StretchFull
                .WithAnchors(Vec2.Half, Vec2.Half)
                .WithSizeDelta(new Vec2(300f, 600f));

            Vec2 size = RectChainMath.RectSize(new Vec2(1920f, 1080f), node);

            Assert.That(size.X, Is.EqualTo(300f).Within(Eps));
            Assert.That(size.Y, Is.EqualTo(600f).Within(Eps));
        }

        [Test]
        public void 부분_스트레치는_앵커_간격에_sizeDelta를_더한다()
        {
            // 가로 전체 스트레치, 세로는 아래 절반. sizeDelta로 가로 -100 / 세로 +20.
            RectNodeState node = RectNodeState.StretchFull
                .WithAnchors(Vec2.Zero, new Vec2(1f, 0.5f))
                .WithSizeDelta(new Vec2(-100f, 20f));

            Vec2 size = RectChainMath.RectSize(new Vec2(1920f, 1080f), node);

            Assert.That(size.X, Is.EqualTo(1820f).Within(Eps));
            Assert.That(size.Y, Is.EqualTo(560f).Within(Eps));
        }

        // ── LocalPosition ────────────────────────────────────────────

        [Test]
        public void 스트레치_풀_가운데_pivot이면_localPosition은_anchoredPosition이다()
        {
            RectNodeState node = RectNodeState.StretchFull
                .WithAnchoredPosition(new Vec2(100f, 50f));

            Vec2 local = RectChainMath.LocalPosition(new Vec2(1920f, 1080f), Vec2.Half, node);

            Assert.That(local.X, Is.EqualTo(100f).Within(Eps));
            Assert.That(local.Y, Is.EqualTo(50f).Within(Eps));
        }

        [Test]
        public void 바닥_pivot_노드는_부모_바닥에_붙는다()
        {
            // NeedsBottomPivot과 같은 상태: 스트레치 풀 + pivot (0.5, 0).
            // 앵커 기준점의 y = 부모 rect 바닥 = -540.
            RectNodeState node = RectNodeState.StretchFull
                .WithPivot(new Vec2(0.5f, 0f));

            Vec2 local = RectChainMath.LocalPosition(new Vec2(1920f, 1080f), Vec2.Half, node);

            Assert.That(local.X, Is.EqualTo(0f).Within(Eps));
            Assert.That(local.Y, Is.EqualTo(-540f).Within(Eps));
        }

        [Test]
        public void 고정_앵커_노드는_앵커점에서_anchoredPosition만큼_떨어진다()
        {
            RectNodeState node = RectNodeState.StretchFull
                .WithAnchors(Vec2.Half, Vec2.Half)
                .WithSizeDelta(new Vec2(300f, 600f))
                .WithPivot(new Vec2(0.5f, 0f))
                .WithAnchoredPosition(new Vec2(200f, -100f));

            // 고정 앵커면 앵커 사각형이 한 점(부모 가운데 = 원점)으로 접히므로
            // pivot 보간과 무관하게 기준점은 (0,0)이다.
            Vec2 local = RectChainMath.LocalPosition(new Vec2(1920f, 1080f), Vec2.Half, node);

            Assert.That(local.X, Is.EqualTo(200f).Within(Eps));
            Assert.That(local.Y, Is.EqualTo(-100f).Within(Eps));
        }

        [Test]
        public void 부모_pivot이_가운데가_아니면_rect_원점이_이동한다()
        {
            // 부모 pivot (0.5, 0): 부모 로컬에서 rect가 y 0~1080에 걸린다.
            // 자식 스트레치 풀 + 가운데 pivot → 기준점 y = 부모 rect 가운데 = 540.
            RectNodeState node = RectNodeState.StretchFull;

            Vec2 local = RectChainMath.LocalPosition(
                new Vec2(1920f, 1080f), new Vec2(0.5f, 0f), node);

            Assert.That(local.X, Is.EqualTo(0f).Within(Eps));
            Assert.That(local.Y, Is.EqualTo(540f).Within(Eps));
        }

        // ── TransformPoint ───────────────────────────────────────────

        [Test]
        public void 빈_체인은_점을_그대로_돌려준다()
        {
            Vec3 p = new Vec3(12f, 34f, 0f);

            Vec3 result = RectChainMath.TransformPoint(
                System.ReadOnlySpan<RectNodeState>.Empty, Stage, p);

            Assert.That(result, Is.EqualTo(p));
        }

        [Test]
        public void 스트레치_풀_체인은_anchoredPosition의_합이다()
        {
            RectNodeState[] chain =
            {
                RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(100f, 50f)),
                RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(-30f, 10f)),
                RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(5f, 5f)),
            };

            Vec3 result = RectChainMath.TransformPoint(chain, Stage, new Vec3(10f, 20f, 0f));

            Assert.That(result.X, Is.EqualTo(100f - 30f + 5f + 10f).Within(Eps));
            Assert.That(result.Y, Is.EqualTo(50f + 10f + 5f + 20f).Within(Eps));
        }

        [Test]
        public void 스케일은_아래쪽_변위를_증폭한다()
        {
            // A(scale 2) > B(anchored (10, 0)). B 로컬의 (1,0):
            // B에서 (10,0)+(1,0)=(11,0) → A의 스케일 2 → (22,0).
            RectNodeState[] chain =
            {
                RectNodeState.StretchFull.WithLocalScale(new Vec3(2f, 2f, 1f)),
                RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(10f, 0f)),
            };

            Vec3 result = RectChainMath.TransformPoint(chain, Stage, new Vec3(1f, 0f, 0f));

            Assert.That(result.X, Is.EqualTo(22f).Within(Eps));
            Assert.That(result.Y, Is.EqualTo(0f).Within(Eps));
        }

        [Test]
        public void Z_90도_회전은_X축을_Y축으로_보낸다()
        {
            RectNodeState[] chain =
            {
                RectNodeState.StretchFull.WithLocalEuler(new Vec3(0f, 0f, 90f)),
            };

            Vec3 result = RectChainMath.TransformPoint(chain, Stage, new Vec3(1f, 0f, 0f));

            Assert.That(result.X, Is.EqualTo(0f).Within(Eps));
            Assert.That(result.Y, Is.EqualTo(1f).Within(Eps));
        }

        [Test]
        public void 바닥_pivot_체인의_바닥점은_부모_바닥이다()
        {
            // 리그의 DepthScale 모양: 스트레치 풀 + 바닥 pivot + 스케일.
            // 자기 rect의 바닥 가운데(로컬 (0,0)) — 스케일과 무관하게 부모 바닥에 서야 한다.
            RectNodeState[] chain =
            {
                RectNodeState.StretchFull
                    .WithPivot(new Vec2(0.5f, 0f))
                    .WithLocalScale(new Vec3(0.8f, 0.8f, 1f)),
            };

            Vec3 result = RectChainMath.TransformPoint(chain, Stage, Vec3.Zero);

            Assert.That(result.X, Is.EqualTo(0f).Within(Eps));
            Assert.That(result.Y, Is.EqualTo(-540f).Within(Eps));
        }

        // ── InverseTransformPoint ────────────────────────────────────

        [Test]
        public void 역변환은_변환을_되돌린다()
        {
            // 앵커·pivot·sizeDelta·스케일·회전을 전부 섞은 체인.
            RectNodeState[] chain =
            {
                RectNodeState.StretchFull
                    .WithAnchoredPosition(new Vec2(120f, -40f))
                    .WithLocalScale(new Vec3(1.25f, 1.25f, 1f)),
                RectNodeState.StretchFull
                    .WithPivot(new Vec2(0.5f, 0f))
                    .WithLocalEuler(new Vec3(0f, 0f, 15f)),
                RectNodeState.StretchFull
                    .WithAnchors(Vec2.Half, Vec2.Half)
                    .WithSizeDelta(new Vec2(300f, 600f))
                    .WithAnchoredPosition(new Vec2(-80f, 33f))
                    .WithLocalScale(new Vec3(0.8f, 0.8f, 1f)),
                RectNodeState.StretchFull
                    .WithAnchors(Vec2.Zero, new Vec2(1f, 0.5f))
                    .WithSizeDelta(new Vec2(-100f, 20f))
                    .WithAnchoredPosition(new Vec2(7f, -7f)),
            };

            Vec3[] points =
            {
                Vec3.Zero,
                new Vec3(123.4f, -56.7f, 0f),
                new Vec3(-321f, 210f, 0f),
            };

            foreach (Vec3 p in points)
            {
                Vec3 world = RectChainMath.TransformPoint(chain, Stage, p);
                Vec3 back = RectChainMath.InverseTransformPoint(chain, Stage, world);

                Assert.That(back.X, Is.EqualTo(p.X).Within(Eps), p.ToString());
                Assert.That(back.Y, Is.EqualTo(p.Y).Within(Eps), p.ToString());
                Assert.That(back.Z, Is.EqualTo(p.Z).Within(Eps), p.ToString());
            }
        }

        [Test]
        public void 역변환_단독_기대값()
        {
            // A: 스트레치 풀, anchored (100, 50). "월드" (110, 70) → A 로컬 (10, 20).
            RectNodeState[] chain =
            {
                RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(100f, 50f)),
            };

            Vec3 result = RectChainMath.InverseTransformPoint(
                chain, Stage, new Vec3(110f, 70f, 0f));

            Assert.That(result.X, Is.EqualTo(10f).Within(Eps));
            Assert.That(result.Y, Is.EqualTo(20f).Within(Eps));
        }
    }
}
