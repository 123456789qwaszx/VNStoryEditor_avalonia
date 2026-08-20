using System;
using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// 정착 focus 수학 — 값이 아니라 **성질**로 고정한다.
    ///
    /// depth 보정과 배치 해는 손으로 기대값을 내기 어렵다(사슬 전체가 개입한다).
    /// 대신 이 둘은 정의가 분명하다:
    ///   보존 = 푼 depthY를 적용하면 focus가 제자리
    ///   명중 = 푼 배치를 적용하면 focus가 원하는 지점
    /// 성질을 고정하면 사슬 모양이 바뀌어도 테스트가 살아 있다.
    ///
    /// 실제 RectTransform·종전 알고리즘과의 대조는 UnityParity 하네스가 한다.
    /// </summary>
    public sealed class SettledFocusMathTests
    {
        private const float Eps = 1e-3f;

        private static readonly RectSpace Space = RectSpace.Centered(1920f, 1080f);

        // 캐릭터 리그 모양의 체인.
        //   0 Track_Focus (place의 이동 축)
        //   1 DepthY      (size의 위치 축)
        //   2 DepthScale  (size의 배율 축, 바닥 pivot)
        //   3 Scale       (바닥 pivot)
        //   4 VisualOffset(focus 측정 노드, 바닥 pivot)
        private const int MoveIndex = 0;
        private const int DepthYIndex = 1;
        private const int DepthScaleIndex = 2;

        private static RectNodeState[] RigChain(
            Vec2 movePosition = default,
            Vec2 depthY = default,
            float depthScale = 1f,
            float slotScale = 1f)
        {
            return new[]
            {
                RectNodeState.StretchFull.WithAnchoredPosition(movePosition),
                RectNodeState.StretchFull.WithAnchoredPosition(depthY),
                RectNodeState.StretchFull
                    .WithPivot(new Vec2(0.5f, 0f))
                    .WithLocalScale(new Vec3(depthScale, depthScale, 1f)),
                RectNodeState.StretchFull
                    .WithPivot(new Vec2(0.5f, 0f))
                    .WithLocalScale(new Vec3(slotScale, slotScale, 1f)),
                RectNodeState.StretchFull.WithPivot(new Vec2(0.5f, 0f)),
            };
        }

        /// <summary>bust 상당의 focus 오프셋 (덤프 실값).</summary>
        private static readonly Vec2 BustOffset = new(0f, 820f);

        private static void AssertVec2(Vec2 actual, Vec2 expected, string what)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(Eps), $"{what} X — actual={actual}");
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(Eps), $"{what} Y — actual={actual}");
        }

        // ── focus 측정 ───────────────────────────────────────────────

        [Test]
        public void focus는_측정_노드_로컬_오프셋을_rig_space로_옮긴_점이다()
        {
            RectNodeState[] chain = RigChain();

            Vec2 focus = SettledFocusMath.FocusPointInRigSpace(chain, Space, BustOffset);

            // 바닥 pivot 사슬이라 측정 노드의 로컬 원점이 무대 바닥(-540).
            // 거기서 오프셋 820 위 = 280.
            AssertVec2(focus, new Vec2(0f, 280f), "기본 자세의 bust focus");
        }

        [Test]
        public void 조상_스케일이_focus_높이를_늘린다()
        {
            Vec2 plain = SettledFocusMath.FocusPointInRigSpace(RigChain(), Space, BustOffset);
            Vec2 scaled = SettledFocusMath.FocusPointInRigSpace(
                RigChain(slotScale: 2f), Space, BustOffset);

            // 바닥에서 재므로 바닥(-540)은 그대로고 높이만 2배: -540 + 820*2 = 1100
            AssertVec2(plain, new Vec2(0f, 280f), "배율 1");
            AssertVec2(scaled, new Vec2(0f, 1100f), "배율 2");
        }

        // ── place: 명중 ──────────────────────────────────────────────

        [Test]
        public void 푼_배치를_적용하면_focus가_원하는_지점에_온다()
        {
            RectNodeState[] chain = RigChain(movePosition: new Vec2(37f, -12f));
            Vec2 desired = new(-460.8f, 120f);   // place_left 상당 (1920 * 0.24)

            Vec2 solved = SettledFocusMath.SolveFocusPlacement(
                chain, Space, MoveIndex, BustOffset, desired,
                chain[MoveIndex].AnchoredPosition);

            // 푼 값을 적용한 뒤 다시 재면 원하는 지점이어야 한다 — 이것이 "명중"의 정의다.
            RectNodeState[] applied = (RectNodeState[])chain.Clone();
            applied[MoveIndex] = applied[MoveIndex].WithAnchoredPosition(solved);

            AssertVec2(
                SettledFocusMath.FocusPointInRigSpace(applied, Space, BustOffset),
                desired,
                "place 명중");
        }

        [Test]
        public void 스케일_조상_아래에서도_명중한다()
        {
            // 이동 축이 스케일 조상 아래 있으면 부모 공간 델타가 배율만큼 줄어야 한다.
            // 그 보정이 빠지면 명중이 어긋난다.
            RectNodeState[] chain =
            {
                RectNodeState.StretchFull.WithLocalScale(new Vec3(1.6f, 1.6f, 1f)),
                RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(20f, 0f)),  // 이동 축
                RectNodeState.StretchFull.WithPivot(new Vec2(0.5f, 0f)),
            };

            Vec2 desired = new(300f, -50f);

            Vec2 solved = SettledFocusMath.SolveFocusPlacement(
                chain, Space, 1, BustOffset, desired, chain[1].AnchoredPosition);

            RectNodeState[] applied = (RectNodeState[])chain.Clone();
            applied[1] = applied[1].WithAnchoredPosition(solved);

            AssertVec2(
                SettledFocusMath.FocusPointInRigSpace(applied, Space, BustOffset),
                desired,
                "스케일 조상 아래 명중");
        }

        [Test]
        public void 회전_조상_아래에서도_명중한다()
        {
            RectNodeState[] chain =
            {
                RectNodeState.StretchFull.WithLocalEuler(new Vec3(0f, 0f, 20f)),
                RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(-15f, 8f)),
                RectNodeState.StretchFull.WithPivot(new Vec2(0.5f, 0f)),
            };

            Vec2 desired = new(-200f, 340f);

            Vec2 solved = SettledFocusMath.SolveFocusPlacement(
                chain, Space, 1, BustOffset, desired, chain[1].AnchoredPosition);

            RectNodeState[] applied = (RectNodeState[])chain.Clone();
            applied[1] = applied[1].WithAnchoredPosition(solved);

            AssertVec2(
                SettledFocusMath.FocusPointInRigSpace(applied, Space, BustOffset),
                desired,
                "회전 조상 아래 명중");
        }

        [Test]
        public void 이미_원하는_지점이면_이동이_없다()
        {
            RectNodeState[] chain = RigChain();
            Vec2 current = SettledFocusMath.FocusPointInRigSpace(chain, Space, BustOffset);

            Vec2 solved = SettledFocusMath.SolveFocusPlacement(
                chain, Space, MoveIndex, BustOffset, current, chain[MoveIndex].AnchoredPosition);

            AssertVec2(solved, chain[MoveIndex].AnchoredPosition, "제자리 배치");
        }

        // ── size: 보존 ───────────────────────────────────────────────

        [TestCase(240f, 1.14f, TestName = "back 상당")]
        [TestCase(-320f, 1.38f, TestName = "front 상당")]
        [TestCase(440f, 1.58f, TestName = "close 상당")]
        [TestCase(480f, 1.00f, TestName = "far 상당")]
        public void 푼_depthY를_적용하면_focus가_제자리다(float rawDepthY, float depthScale)
        {
            // depth 프리셋 실값(ExportedTuning/presets/depth.json).
            RectNodeState[] chain = RigChain(movePosition: new Vec2(60f, 0f));

            Vec2 before = SettledFocusMath.FocusPointInRigSpace(chain, Space, BustOffset);

            Vec2 raw = new(0f, rawDepthY);
            Vec2 targetScale = new(depthScale, depthScale);

            Vec2 solved = SettledFocusMath.SolveDepthYPreservingFocus(
                chain, Space, DepthYIndex, DepthScaleIndex, BustOffset, raw, targetScale);

            // 푼 depthY와 목표 배율을 적용한 뒤 다시 재면 제자리여야 한다 — "보존"의 정의.
            RectNodeState[] applied = (RectNodeState[])chain.Clone();
            applied[DepthYIndex] = applied[DepthYIndex].WithAnchoredPosition(solved);
            applied[DepthScaleIndex] = applied[DepthScaleIndex]
                .WithLocalScale(new Vec3(depthScale, depthScale, 1f));

            AssertVec2(
                SettledFocusMath.FocusPointInRigSpace(applied, Space, BustOffset),
                before,
                "depth 전환 후 focus 보존");
        }

        [Test]
        public void 배율이_1이면_보정이_필요없다()
        {
            // 배율이 안 바뀌고 depthY만 움직이면, 보정은 그 이동을 그대로 상쇄한다.
            RectNodeState[] chain = RigChain(depthY: new Vec2(0f, 100f));

            Vec2 raw = new(0f, 300f);

            Vec2 solved = SettledFocusMath.SolveDepthYPreservingFocus(
                chain, Space, DepthYIndex, DepthScaleIndex, BustOffset, raw, Vec2.One);

            // 원래 자리로 되돌아온다 — focus를 보존하려면 depthY가 움직이면 안 되기 때문.
            AssertVec2(solved, new Vec2(0f, 100f), "배율 불변 시 보정");
        }

        [Test]
        public void 보존은_focus_프리셋에_따라_다른_답을_낸다()
        {
            // feet(0, 480)과 face(0, 950)는 배율 변화에 다르게 반응한다.
            RectNodeState[] chain = RigChain();
            Vec2 raw = new(0f, -320f);
            Vec2 scale = new(1.38f, 1.38f);

            Vec2 feet = SettledFocusMath.SolveDepthYPreservingFocus(
                chain, Space, DepthYIndex, DepthScaleIndex, new Vec2(0f, 480f), raw, scale);

            Vec2 face = SettledFocusMath.SolveDepthYPreservingFocus(
                chain, Space, DepthYIndex, DepthScaleIndex, new Vec2(0f, 950f), raw, scale);

            Assert.That(Math.Abs(feet.Y - face.Y), Is.GreaterThan(1f),
                "보존 대상이 다르면 보정도 달라야 한다");
        }

        [Test]
        public void 계산이_입력_체인을_바꾸지_않는다()
        {
            // 사본에 목표를 입힌다는 것이 이 단계의 핵심이다.
            // 입력을 건드리면 "적용→측정→복원"을 코어에서 되풀이하는 셈이 된다.
            RectNodeState[] chain = RigChain(depthY: new Vec2(0f, 55f), depthScale: 1.2f);

            Vec2 originalDepthY = chain[DepthYIndex].AnchoredPosition;
            Vec3 originalScale = chain[DepthScaleIndex].LocalScale;

            SettledFocusMath.SolveDepthYPreservingFocus(
                chain, Space, DepthYIndex, DepthScaleIndex, BustOffset,
                new Vec2(0f, -320f), new Vec2(1.38f, 1.38f));

            Assert.That(chain[DepthYIndex].AnchoredPosition, Is.EqualTo(originalDepthY));
            Assert.That(chain[DepthScaleIndex].LocalScale, Is.EqualTo(originalScale));
        }

        // ── 거부 ─────────────────────────────────────────────────────

        [Test]
        public void 체인_밖_인덱스는_거부한다()
        {
            RectNodeState[] chain = RigChain();

            // "노드가 측정 체인의 조상이 아니다" — 조용히 어긋나는 대신 터진다.
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SettledFocusMath.SolveFocusPlacement(
                    chain, Space, chain.Length, BustOffset, Vec2.Zero, Vec2.Zero));

            Assert.Throws<ArgumentOutOfRangeException>(
                () => SettledFocusMath.SolveFocusPlacement(
                    chain, Space, -1, BustOffset, Vec2.Zero, Vec2.Zero));

            Assert.Throws<ArgumentOutOfRangeException>(
                () => SettledFocusMath.SolveDepthYPreservingFocus(
                    chain, Space, 99, DepthScaleIndex, BustOffset, Vec2.Zero, Vec2.One));

            Assert.Throws<ArgumentOutOfRangeException>(
                () => SettledFocusMath.SolveDepthYPreservingFocus(
                    chain, Space, DepthYIndex, 99, BustOffset, Vec2.Zero, Vec2.One));
        }
    }
}
