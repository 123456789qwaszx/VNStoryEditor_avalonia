using System;
using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// b-5 depth 묶음의 순수 수학 검증. 골든 수치보다 강한 성질로 고정한다:
    /// - 보존: 푼 depthY를 적용하면 focus가 제자리다 (그게 "보존"의 정의다)
    /// - 명중: 푼 배치를 적용하면 focus가 원하는 지점에 선다
    /// </summary>
    public sealed class SettledFocusMathTests
    {
        private const float Eps = 1e-2f;

        private static readonly RectSpace Stage = RectSpace.Centered(1920f, 1080f);

        /// <summary>리그 축 모양의 체인: TrackFocus > DepthY > DepthScale(바닥 pivot) > Track > VisualOffset.</summary>
        private static RectNodeState[] MakeRigChain(
            Vec2 depthY, Vec2 depthScale, Vec2 trackPos)
        {
            return new[]
            {
                RectNodeState.StretchFull,                                       // 0: TrackFocus
                RectNodeState.StretchFull.WithAnchoredPosition(depthY),          // 1: DepthY
                RectNodeState.StretchFull                                        // 2: DepthScale
                    .WithPivot(new Vec2(0.5f, 0f))
                    .WithLocalScale(new Vec3(depthScale.X, depthScale.Y, 1f)),
                RectNodeState.StretchFull.WithAnchoredPosition(trackPos),        // 3: Track
                RectNodeState.StretchFull.WithPivot(new Vec2(0.5f, 0f)),         // 4: VisualOffset
            };
        }

        private const int DepthYIndex = 1;
        private const int DepthScaleIndex = 2;
        private const int TrackFocusIndex = 0;

        // ── 보존 성질 ────────────────────────────────────────────────

        [TestCase(0f, 240f, 1.14f, 1.14f)]     // back 프리셋 상당
        [TestCase(0f, -320f, 1.18f, 1.18f)]    // front 프리셋 상당
        [TestCase(0f, 440f, 1.38f, 1.38f)]     // close 프리셋 상당
        public void 푼_depthY를_적용하면_focus가_제자리다(
            float rawX, float rawY, float scaleX, float scaleY)
        {
            RectNodeState[] chain = MakeRigChain(
                depthY: new Vec2(0f, 120f),
                depthScale: new Vec2(0.86f, 0.86f),
                trackPos: new Vec2(240f, -60f));

            Vec2 focusOffset = new Vec2(10f, 350f); // Bust 상당의 로컬 오프셋
            Vec2 rawDepthY = new Vec2(rawX, rawY);
            Vec2 targetScale = new Vec2(scaleX, scaleY);

            Vec2 before = SettledFocusMath.FocusPointInRigSpace(chain, Stage, focusOffset);

            Vec2 solved = SettledFocusMath.SolveDepthYPreservingFocus(
                chain, Stage, DepthYIndex, DepthScaleIndex, focusOffset, rawDepthY, targetScale);

            // 푼 값을 실제로 적용한 체인.
            RectNodeState[] applied = (RectNodeState[])chain.Clone();
            applied[DepthYIndex] = applied[DepthYIndex].WithAnchoredPosition(solved);
            applied[DepthScaleIndex] = applied[DepthScaleIndex].WithLocalScale(
                new Vec3(targetScale.X, targetScale.Y, 1f));

            Vec2 after = SettledFocusMath.FocusPointInRigSpace(applied, Stage, focusOffset);

            Assert.That(after.X, Is.EqualTo(before.X).Within(Eps), "focus X 보존");
            Assert.That(after.Y, Is.EqualTo(before.Y).Within(Eps), "focus Y 보존");
        }

        [Test]
        public void 스케일이_같으면_보정은_depth_이동을_상쇄한다()
        {
            // 스케일 변화가 없으면 focus 보존 = rawDepthY 이동분을 그대로 되돌리는 것.
            RectNodeState[] chain = MakeRigChain(
                new Vec2(0f, 100f), new Vec2(1f, 1f), Vec2.Zero);

            Vec2 solved = SettledFocusMath.SolveDepthYPreservingFocus(
                chain, Stage, DepthYIndex, DepthScaleIndex,
                focusLocalOffset: new Vec2(0f, 300f),
                rawDepthY: new Vec2(0f, 400f),
                targetDepthScale: new Vec2(1f, 1f));

            // 보정 결과는 현재 depthY(100)와 같아야 focus가 안 움직인다.
            Assert.That(solved.Y, Is.EqualTo(100f).Within(Eps));
        }

        // ── 명중 성질 ────────────────────────────────────────────────

        [TestCase(-460.8f, 172.8f)]   // Left 상당 (1920*0.24, 1080*0.16)
        [TestCase(0f, 0f)]            // Center
        [TestCase(268.8f, -97.2f)]    // ThirdsLowerRight 상당
        public void 푼_배치를_적용하면_focus가_원하는_지점에_선다(float desiredX, float desiredY)
        {
            RectNodeState[] chain = MakeRigChain(
                depthY: new Vec2(0f, 240f),
                depthScale: new Vec2(1.14f, 1.14f),
                trackPos: new Vec2(-120f, 30f));

            Vec2 focusOffset = new Vec2(0f, 350f);
            Vec2 desired = new Vec2(desiredX, desiredY);

            // TrackFocus(인덱스 0)를 이동시켜 명중시킨다 — place 커맨드의 이동 축.
            Vec2 solved = SettledFocusMath.SolveFocusPlacement(
                chain, Stage, TrackFocusIndex, focusOffset, desired,
                currentMoveAnchoredPosition: chain[TrackFocusIndex].AnchoredPosition);

            RectNodeState[] applied = (RectNodeState[])chain.Clone();
            applied[TrackFocusIndex] = applied[TrackFocusIndex].WithAnchoredPosition(solved);

            Vec2 landed = SettledFocusMath.FocusPointInRigSpace(applied, Stage, focusOffset);

            Assert.That(landed.X, Is.EqualTo(desired.X).Within(Eps), "focus X 명중");
            Assert.That(landed.Y, Is.EqualTo(desired.Y).Within(Eps), "focus Y 명중");
        }

        [Test]
        public void 깊은_노드를_움직여도_명중한다()
        {
            // 스케일된 조상(depthScale 0.8) 아래의 Track(인덱스 3)을 움직이는 경우 —
            // 부모 공간 변환이 스케일을 나눠야 명중한다.
            RectNodeState[] chain = MakeRigChain(
                new Vec2(0f, 120f), new Vec2(0.8f, 0.8f), new Vec2(50f, -20f));

            Vec2 focusOffset = new Vec2(0f, 300f);
            Vec2 desired = new Vec2(200f, 100f);

            Vec2 solved = SettledFocusMath.SolveFocusPlacement(
                chain, Stage, 3, focusOffset, desired,
                currentMoveAnchoredPosition: chain[3].AnchoredPosition);

            RectNodeState[] applied = (RectNodeState[])chain.Clone();
            applied[3] = applied[3].WithAnchoredPosition(solved);

            Vec2 landed = SettledFocusMath.FocusPointInRigSpace(applied, Stage, focusOffset);

            Assert.That(landed.X, Is.EqualTo(desired.X).Within(Eps));
            Assert.That(landed.Y, Is.EqualTo(desired.Y).Within(Eps));
        }

        // ── 방어선 ───────────────────────────────────────────────────

        [Test]
        public void 체인_밖_인덱스는_소리가_난다()
        {
            RectNodeState[] chain = MakeRigChain(Vec2.Zero, Vec2.One, Vec2.Zero);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SettledFocusMath.SolveDepthYPreservingFocus(
                    chain, Stage, 99, DepthScaleIndex, Vec2.Zero, Vec2.Zero, Vec2.One));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SettledFocusMath.SolveFocusPlacement(
                    chain, Stage, -1, Vec2.Zero, Vec2.Zero, Vec2.Zero));
        }
    }
}
