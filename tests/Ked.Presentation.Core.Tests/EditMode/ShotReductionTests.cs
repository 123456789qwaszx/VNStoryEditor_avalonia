using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// 샷 리덕션. 기대값은 종전 BuildTargetState 본문에서 온다.
    ///
    /// zoom_focus는 값보다 **명중**이 정의다: 적용측 규약("보이는 위치 = 논리 × 배율 + pan")으로
    /// 역검산해서 원하는 화면 지점에 오는지 본다.
    /// </summary>
    public sealed class ShotReductionTests
    {
        private const float Eps = 1e-4f;

        private static readonly ShotIntentState From =
            new(2f, new Vec2(30f, -10f), new Vec2(100f, 200f));

        private static void AssertVec2(Vec2 actual, Vec2 expected, string what)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(Eps), $"{what} X — actual={actual}");
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(Eps), $"{what} Y — actual={actual}");
        }

        // ── 규약 상수 ────────────────────────────────────────────────

        [Test]
        public void zoom은_1당_배율_0_05다()
        {
            // 커맨드(목표 계산)와 적용측(카메라 루트·response)이 같이 보는 규약이다.
            Assert.That(ShotIntentMath.ZoomToScaleFactor, Is.EqualTo(0.05f));

            Assert.That(ShotIntentMath.EvaluateCameraScale(0f), Is.EqualTo(1f).Within(Eps));
            Assert.That(ShotIntentMath.EvaluateCameraScale(2f), Is.EqualTo(1.1f).Within(Eps));
            Assert.That(ShotIntentMath.EvaluateCameraScale(-4f), Is.EqualTo(0.8f).Within(Eps));
        }

        [Test]
        public void 카메라_제거와_역산은_서로_역이다()
        {
            Vec2 logical = new(120f, -45f);
            Vec2 pan = new(15f, 8f);
            float scale = ShotIntentMath.EvaluateCameraScale(3f);

            // 적용측 규약대로 보이는 위치를 만든 뒤, 다시 벗기면 논리 좌표로 돌아온다.
            Vec2 visible = new(logical.X * scale + pan.X, logical.Y * scale + pan.Y);

            AssertVec2(
                ShotIntentMath.RemoveCurrentCameraTransformFromFocusPoint(visible, pan, scale),
                logical,
                "카메라 제거");
        }

        // ── 단순 리덕션 4종 ──────────────────────────────────────────

        [Test]
        public void shot_zoom은_zoom만_바꾼다()
        {
            ShotIntentState to = ShotZoomReduction.Reduce(From, 5f);

            Assert.That(to.Zoom, Is.EqualTo(5f));
            AssertVec2(to.PanInRigSpace, From.PanInRigSpace, "pan 유지");
            AssertVec2(to.FocusPointInRigSpace, From.FocusPointInRigSpace, "focus 유지");
        }

        [Test]
        public void shot_track은_현재_pan에_더한다()
        {
            ShotIntentState to = ShotTrackReduction.Reduce(From, new Vec2(20f, 5f));

            Assert.That(to.Zoom, Is.EqualTo(From.Zoom));
            AssertVec2(to.PanInRigSpace, new Vec2(50f, -5f), "pan 가산");
            AssertVec2(to.FocusPointInRigSpace, From.FocusPointInRigSpace, "focus 유지");
        }

        [Test]
        public void shot_to는_zoom과_pan을_절대값으로_바꾼다()
        {
            ShotIntentState to = ShotToReduction.Reduce(From, 4f, new Vec2(-80f, 60f));

            Assert.That(to.Zoom, Is.EqualTo(4f));
            AssertVec2(to.PanInRigSpace, new Vec2(-80f, 60f), "pan 절대");

            // focus는 건드리지 않는다 — 논리 focus는 zoom_focus만 바꾼다.
            AssertVec2(to.FocusPointInRigSpace, From.FocusPointInRigSpace, "focus 유지");
        }

        [Test]
        public void shot_reset은_기본_샷이다()
        {
            ShotIntentState to = ShotResetReduction.Reduce();

            Assert.That(to.Zoom, Is.EqualTo(0f));
            AssertVec2(to.PanInRigSpace, Vec2.Zero, "pan 0");
            AssertVec2(to.FocusPointInRigSpace, Vec2.Zero, "focus 0");
        }

        // ── shot_focus_to: 명중 ──────────────────────────────────────

        [Test]
        public void 접은_뒤_적용측_규약으로_계산하면_원하는_지점에_온다()
        {
            // 이것이 zoom_focus의 정의다.
            Vec2 measured = new(250f, 380f);      // 현재 카메라가 적용된 채로 측정된 focus
            Vec2 desired = new(-460.8f, 0f);      // place_left 상당 화면 지점
            float targetZoom = 6f;

            ShotIntentState to = ShotZoomFocusReduction.Reduce(From, targetZoom, measured, desired);

            float targetScale = ShotIntentMath.EvaluateCameraScale(targetZoom);

            Vec2 visible = new(
                to.FocusPointInRigSpace.X * targetScale + to.PanInRigSpace.X,
                to.FocusPointInRigSpace.Y * targetScale + to.PanInRigSpace.Y);

            AssertVec2(visible, desired, "zoom_focus 명중");
            Assert.That(to.Zoom, Is.EqualTo(targetZoom));
        }

        [Test]
        public void 논리_focus는_현재_카메라를_벗긴_값이다()
        {
            Vec2 measured = new(250f, 380f);

            ShotIntentState to = ShotZoomFocusReduction.Reduce(From, 6f, measured, Vec2.Zero);

            Vec2 expected = ShotIntentMath.RemoveCurrentCameraTransformFromFocusPoint(
                measured, From.PanInRigSpace, ShotIntentMath.EvaluateCameraScale(From.Zoom));

            AssertVec2(to.FocusPointInRigSpace, expected, "논리 focus 복원");
        }

        [Test]
        public void 현재_카메라가_기본이면_측정값이_곧_논리_focus다()
        {
            // zoom 0, pan 0이면 배율 1 · 이동 0이라 벗길 것이 없다.
            Vec2 measured = new(77f, -33f);

            ShotIntentState to = ShotZoomFocusReduction.Reduce(
                ShotIntentState.Default, 0f, measured, Vec2.Zero);

            AssertVec2(to.FocusPointInRigSpace, measured, "기본 카메라");
            AssertVec2(to.PanInRigSpace, new Vec2(-77f, 33f), "원점으로 보내는 pan");
        }

        [Test]
        public void 같은_지점을_다시_요청하면_같은_결과다()
        {
            // 순수 함수 — 같은 입력은 언제나 같은 출력.
            Vec2 measured = new(120f, 90f);
            Vec2 desired = new(50f, -20f);

            ShotIntentState a = ShotZoomFocusReduction.Reduce(From, 3f, measured, desired);
            ShotIntentState b = ShotZoomFocusReduction.Reduce(From, 3f, measured, desired);

            Assert.That(b.Zoom, Is.EqualTo(a.Zoom));
            AssertVec2(b.PanInRigSpace, a.PanInRigSpace, "pan");
            AssertVec2(b.FocusPointInRigSpace, a.FocusPointInRigSpace, "focus");
        }
    }
}
