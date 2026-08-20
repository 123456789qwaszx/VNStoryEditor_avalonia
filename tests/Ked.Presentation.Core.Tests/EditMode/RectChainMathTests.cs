using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// 순수 좌표 수학의 골든. 기대값은 전부 손으로 계산한 것이다 —
    /// 구현을 돌려 나온 값을 기대값으로 박으면 틀린 구현을 고정하게 된다.
    ///
    /// 실제 RectTransform과의 대조는 UnityParity 하네스가 따로 한다.
    /// 여기가 통과하고 거기가 실패하면 "유니티 규약을 잘못 안 것"이고,
    /// 여기가 실패하면 "내 산수가 틀린 것"이다 — 두 실패를 갈라 보려고 나눠 둔다.
    /// </summary>
    public sealed class RectChainMathTests
    {
        private const float Eps = 1e-4f;

        // 부동소수 계산 결과는 정확 일치로 비교하지 않는다.
        // (특히 -0f — Vec2.Equals는 -0f와 +0f를 다르게 본다)
        private static void AssertVec2(Vec2 actual, float x, float y)
        {
            Assert.That(actual.X, Is.EqualTo(x).Within(Eps), $"X 불일치. actual={actual}");
            Assert.That(actual.Y, Is.EqualTo(y).Within(Eps), $"Y 불일치. actual={actual}");
        }

        private static void AssertVec3(Vec3 actual, float x, float y, float z)
        {
            Assert.That(actual.X, Is.EqualTo(x).Within(Eps), $"X 불일치. actual={actual}");
            Assert.That(actual.Y, Is.EqualTo(y).Within(Eps), $"Y 불일치. actual={actual}");
            Assert.That(actual.Z, Is.EqualTo(z).Within(Eps), $"Z 불일치. actual={actual}");
        }

        // ── RectSize ─────────────────────────────────────────────────

        [Test]
        public void 스트레치_풀_노드는_부모_크기를_그대로_받는다()
        {
            Vec2 size = RectChainMath.RectSize(new Vec2(1920f, 1080f), RectNodeState.StretchFull);

            AssertVec2(size, 1920f, 1080f);
        }

        [Test]
        public void 고정_앵커면_sizeDelta가_크기_그_자체다()
        {
            // 초상 이미지 모양: 앵커가 한 점이라 부모 크기가 개입하지 않는다.
            RectNodeState node = RectNodeState.StretchFull
                .WithAnchors(Vec2.Half, Vec2.Half)
                .WithSizeDelta(new Vec2(300f, 600f));

            Vec2 size = RectChainMath.RectSize(new Vec2(1920f, 1080f), node);

            AssertVec2(size, 300f, 600f);
        }

        [Test]
        public void 부분_스트레치는_앵커_간격에_sizeDelta를_더한다()
        {
            // 오버레이 모양: x는 절반만 스트레치, y는 풀 스트레치.
            RectNodeState node = RectNodeState.StretchFull
                .WithAnchors(new Vec2(0.25f, 0f), new Vec2(0.75f, 1f))
                .WithSizeDelta(new Vec2(-40f, 20f));

            Vec2 size = RectChainMath.RectSize(new Vec2(1000f, 500f), node);

            // x: 0.5 * 1000 - 40 = 460 / y: 1.0 * 500 + 20 = 520
            AssertVec2(size, 460f, 520f);
        }

        // ── LocalPosition ────────────────────────────────────────────

        [Test]
        public void 스트레치_풀_가운데_pivot이면_localPosition은_anchoredPosition이다()
        {
            RectNodeState node = RectNodeState.StretchFull
                .WithAnchoredPosition(new Vec2(30f, -20f));

            Vec2 local = RectChainMath.LocalPosition(new Vec2(1000f, 500f), Vec2.Half, node);

            // 앵커 기준점이 부모 가운데(0,0)라 anchoredPosition이 그대로 나온다.
            AssertVec2(local, 30f, -20f);
        }

        [Test]
        public void 바닥_pivot_노드는_부모_바닥에_붙는다()
        {
            // 리그의 NeedsBottomPivot 노드가 이 모양이다.
            RectNodeState node = RectNodeState.StretchFull.WithPivot(new Vec2(0.5f, 0f));

            Vec2 local = RectChainMath.LocalPosition(new Vec2(1000f, 500f), Vec2.Half, node);

            // y: 앵커 사각형 [-250, 250]을 자기 pivot.y=0으로 보간 → -250 (부모 바닥).
            // 여기서 0.5를 쓰면 0이 나온다 — LocalPosition 주석의 그 실수.
            AssertVec2(local, 0f, -250f);
        }

        [Test]
        public void 고정_앵커_노드는_앵커점에서_anchoredPosition만큼_떨어진다()
        {
            // 우상단 앵커에 고정.
            RectNodeState node = RectNodeState.StretchFull
                .WithAnchors(Vec2.One, Vec2.One)
                .WithAnchoredPosition(new Vec2(-50f, -30f));

            Vec2 local = RectChainMath.LocalPosition(new Vec2(1000f, 500f), Vec2.Half, node);

            // 앵커점 = 부모 우상단 (500, 250). 거기서 (-50, -30).
            AssertVec2(local, 450f, 220f);
        }

        [Test]
        public void 부모_pivot이_가운데가_아니면_rect_원점이_이동한다()
        {
            // 부모 pivot이 좌하단이면 부모 rect가 1사분면에 놓인다.
            Vec2 local = RectChainMath.LocalPosition(
                new Vec2(1000f, 500f), Vec2.Zero, RectNodeState.StretchFull);

            // 가운데 pivot 부모였다면 (0,0)이었을 자리.
            AssertVec2(local, 500f, 250f);
        }

        // ── TransformPoint ───────────────────────────────────────────

        [Test]
        public void 빈_체인은_점을_그대로_돌려준다()
        {
            Vec3 p = RectChainMath.TransformPoint(
                System.ReadOnlySpan<RectNodeState>.Empty,
                RectSpace.Centered(1000f, 500f),
                new Vec3(7f, -3f, 2f));

            AssertVec3(p, 7f, -3f, 2f);
        }

        [Test]
        public void 스트레치_풀_체인은_anchoredPosition의_합이다()
        {
            // 스트레치 풀이라 부모 크기가 그대로 내려가고, 회전·스케일이 없어
            // 변위가 단순 누적된다. 체인 조립이 맞는지 보는 가장 싼 검사다.
            RectNodeState[] chain =
            {
                RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(10f, 0f)),
                RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(20f, 0f)),
                RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(5f, 5f)),
            };

            Vec3 p = RectChainMath.TransformPoint(chain, RectSpace.Centered(1000f, 500f), Vec3.Zero);

            AssertVec3(p, 35f, 5f, 0f);
        }

        [Test]
        public void 스케일은_아래쪽_변위를_증폭한다()
        {
            // 조상의 스케일이 자손의 변위에 곱해진다 — depth 축이 이걸로 산다.
            RectNodeState[] chain =
            {
                RectNodeState.StretchFull.WithLocalScale(new Vec3(2f, 2f, 1f)),
                RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(10f, 0f)),
            };

            Vec3 p = RectChainMath.TransformPoint(chain, RectSpace.Centered(1000f, 500f), Vec3.Zero);

            AssertVec3(p, 20f, 0f, 0f);
        }

        [Test]
        public void Z_90도_회전은_X축을_Y축으로_보낸다()
        {
            RectNodeState[] chain =
            {
                RectNodeState.StretchFull.WithLocalEuler(new Vec3(0f, 0f, 90f)),
            };

            Vec3 p = RectChainMath.TransformPoint(
                chain, RectSpace.Centered(1000f, 500f), new Vec3(100f, 0f, 0f));

            AssertVec3(p, 0f, 100f, 0f);
        }

        [Test]
        public void 바닥_pivot_체인의_바닥점은_부모_바닥이다()
        {
            // pivot.y = 0이면 노드 로컬 원점이 곧 자기 바닥이다.
            RectNodeState[] chain =
            {
                RectNodeState.StretchFull.WithPivot(new Vec2(0.5f, 0f)),
            };

            Vec3 p = RectChainMath.TransformPoint(chain, RectSpace.Centered(1000f, 500f), Vec3.Zero);

            AssertVec3(p, 0f, -250f, 0f);
        }

        [Test]
        public void 부모_크기가_줄면_자손_앵커점도_따라_줄어든다()
        {
            // 고정 앵커 + sizeDelta로 부모 크기를 바꾸면 그 아래 스트레치 자식의
            // 앵커 계산이 달라진다 — 1패스가 필요한 이유가 이것이다.
            RectNodeState[] chain =
            {
                RectNodeState.StretchFull
                    .WithAnchors(Vec2.Half, Vec2.Half)
                    .WithSizeDelta(new Vec2(400f, 200f)),
                RectNodeState.StretchFull.WithPivot(new Vec2(0.5f, 0f)),
            };

            Vec3 p = RectChainMath.TransformPoint(chain, RectSpace.Centered(1000f, 500f), Vec3.Zero);

            // 부모(400×200)의 바닥 = -100. 1000×500을 그대로 썼다면 -250이 나온다.
            AssertVec3(p, 0f, -100f, 0f);
        }

        // ── InverseTransformPoint ────────────────────────────────────

        [Test]
        public void 역변환은_변환을_되돌린다()
        {
            // 전 요소가 섞인 체인에서의 왕복. 개별 기대값을 손으로 못 내는 조합이라
            // 성질(역함수)로 고정한다.
            RectNodeState[] chain =
            {
                RectNodeState.StretchFull
                    .WithAnchoredPosition(new Vec2(12f, -8f))
                    .WithLocalScale(new Vec3(1.4f, 1.4f, 1f)),
                RectNodeState.StretchFull
                    .WithPivot(new Vec2(0.5f, 0f))
                    .WithLocalEuler(new Vec3(0f, 0f, 25f)),
                RectNodeState.StretchFull
                    .WithAnchors(Vec2.Half, Vec2.Half)
                    .WithSizeDelta(new Vec2(300f, 600f))
                    .WithAnchoredPosition(new Vec2(-5f, 40f))
                    .WithLocalScale(new Vec3(0.8f, 1.2f, 1f)),
            };

            RectSpace space = RectSpace.Centered(1920f, 1080f);
            Vec3 local = new(37f, -19f, 0f);

            Vec3 world = RectChainMath.TransformPoint(chain, space, local);
            Vec3 back = RectChainMath.InverseTransformPoint(chain, space, world);

            AssertVec3(back, local.X, local.Y, local.Z);
        }

        [Test]
        public void 역변환_단독_기대값()
        {
            // 왕복만으로는 두 함수가 같이 틀린 경우를 못 잡는다. 한 방향을 못 박는다.
            RectNodeState[] chain =
            {
                RectNodeState.StretchFull
                    .WithAnchoredPosition(new Vec2(100f, 50f))
                    .WithLocalScale(new Vec3(2f, 2f, 1f)),
            };

            Vec3 local = RectChainMath.InverseTransformPoint(
                chain, RectSpace.Centered(1000f, 500f), new Vec3(140f, 50f, 0f));

            // (140,50) - t(100,50) = (40,0) → 스케일 2로 나누면 (20,0).
            AssertVec3(local, 20f, 0f, 0f);
        }

        [Test]
        public void 오일러_3축은_Z_X_Y_순서로_합성된다()
        {
            // 순서는 손 계산으로 못 박기 나쁘다(3축 삼각함수 곱). 대신 성질로 고정한다:
            // 한 노드의 (x,y,z) 오일러 = 단축 회전 세 노드를 Z→X→Y로 쌓은 것과 같다.
            //
            // 스트레치 풀 + 가운데 pivot 체인은 모든 노드의 localPosition이 0이라
            // 순수 회전 합성만 남는다. 체인은 끝 노드부터 적용되므로
            // [Y, X, Z] 순으로 쌓으면 Z가 먼저 걸린다.
            //
            // (유니티 규약과 같은지는 UnityParity의 오일러_3축_순서가 판정한다.
            //  여기서 잡는 것은 "합성 순서가 구현 안에서 일관한가"다.)
            RectSpace space = RectSpace.Centered(1000f, 500f);
            Vec3 point = new(100f, -40f, 25f);

            RectNodeState[] combined =
            {
                RectNodeState.StretchFull.WithLocalEuler(new Vec3(30f, 40f, 50f)),
            };

            RectNodeState[] composed =
            {
                RectNodeState.StretchFull.WithLocalEuler(new Vec3(0f, 40f, 0f)),
                RectNodeState.StretchFull.WithLocalEuler(new Vec3(30f, 0f, 0f)),
                RectNodeState.StretchFull.WithLocalEuler(new Vec3(0f, 0f, 50f)),
            };

            Vec3 a = RectChainMath.TransformPoint(combined, space, point);
            Vec3 b = RectChainMath.TransformPoint(composed, space, point);

            AssertVec3(a, b.X, b.Y, b.Z);
        }

        [Test]
        public void 스트레치_풀_체인은_회전만_남긴다()
        {
            // 위 테스트가 딛고 선 전제 — 이게 깨지면 저기서 회전이 아니라
            // 변위 차이를 비교하게 된다.
            RectNodeState[] chain =
            {
                RectNodeState.StretchFull,
                RectNodeState.StretchFull,
            };

            Vec3 p = RectChainMath.TransformPoint(
                chain, RectSpace.Centered(1000f, 500f), new Vec3(7f, -3f, 2f));

            AssertVec3(p, 7f, -3f, 2f);
        }
    }
}
