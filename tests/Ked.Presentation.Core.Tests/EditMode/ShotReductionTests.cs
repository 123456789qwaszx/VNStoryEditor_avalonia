using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// b-5 shot 묶음 골든. 기대값은 호스트 BuildTargetState들이 종전에 내던 값 그대로다.
    /// zoom_focus는 적용측 규약("보이는 위치 = 논리 × 배율 + pan")으로 명중을 검산한다.
    /// </summary>
    public sealed class ShotReductionTests
    {
        private const float Eps = 1e-4f;

        private static readonly ShotIntentState From = new ShotIntentState(
            zoom: 2f,
            panInRigSpace: new Vec2(100f, -50f),
            focusPointInRigSpace: new Vec2(300f, 200f));

        [Test]
        public void 규약_상수_zoom당_배율_증가는_0_05다()
        {
            Assert.That(ShotIntentMath.ZoomToScaleFactor, Is.EqualTo(0.05f));
            Assert.That(ShotIntentMath.EvaluateCameraScale(0f), Is.EqualTo(1f).Within(Eps));
            Assert.That(ShotIntentMath.EvaluateCameraScale(2.5f), Is.EqualTo(1.125f).Within(Eps));
            Assert.That(ShotIntentMath.EvaluateCameraScale(-10f), Is.EqualTo(0.5f).Within(Eps));
        }

        [Test]
        public void shot_zoom은_zoom만_바꾼다()
        {
            ShotIntentState result = ShotZoomReduction.Reduce(From, 5f);

            Assert.That(result.Zoom, Is.EqualTo(5f));
            Assert.That(result.PanInRigSpace, Is.EqualTo(From.PanInRigSpace));
            Assert.That(result.FocusPointInRigSpace, Is.EqualTo(From.FocusPointInRigSpace));
        }

        [Test]
        public void shot_track은_pan에_더한다()
        {
            ShotIntentState result = ShotTrackReduction.Reduce(From, new Vec2(-30f, 20f));

            Assert.That(result.PanInRigSpace, Is.EqualTo(new Vec2(70f, -30f)));
            Assert.That(result.Zoom, Is.EqualTo(From.Zoom));
        }

        [Test]
        public void shot_to는_절대값이다()
        {
            ShotIntentState result = ShotToReduction.Reduce(From, 1f, new Vec2(48f, 0f));

            Assert.That(result.Zoom, Is.EqualTo(1f));
            Assert.That(result.PanInRigSpace, Is.EqualTo(new Vec2(48f, 0f)));
            Assert.That(result.FocusPointInRigSpace, Is.EqualTo(From.FocusPointInRigSpace));
        }

        [Test]
        public void shot_reset은_기본_샷이다()
        {
            ShotIntentState result = ShotResetReduction.Reduce();

            Assert.That(result.Zoom, Is.EqualTo(0f));
            Assert.That(result.PanInRigSpace, Is.EqualTo(Vec2.Zero));
            Assert.That(result.FocusPointInRigSpace, Is.EqualTo(Vec2.Zero));
        }

        // ── zoom_focus ───────────────────────────────────────────────

        [Test]
        public void 카메라_제거와_pan_역산은_서로_역이다()
        {
            float scale = ShotIntentMath.EvaluateCameraScale(3f);
            Vec2 pan = new Vec2(80f, -40f);
            Vec2 visible = new Vec2(250f, 130f);

            Vec2 logical = ShotIntentMath.RemoveCurrentCameraTransformFromFocusPoint(visible, pan, scale);

            // 적용 규약: 보이는 위치 = 논리 × 배율 + pan.
            Assert.That(logical.X * scale + pan.X, Is.EqualTo(visible.X).Within(Eps));
            Assert.That(logical.Y * scale + pan.Y, Is.EqualTo(visible.Y).Within(Eps));
        }

        [TestCase(2.5f, 0f, 0f)]           // Center로 줌인
        [TestCase(0f, -460.8f, 172.8f)]    // TopLeft 상당으로
        [TestCase(-4f, 268.8f, -97.2f)]    // 줌아웃하며 ThirdsLowerRight 상당으로
        public void zoom_focus를_적용하면_focus가_원하는_화면_지점에_보인다(
            float targetZoom, float desiredX, float desiredY)
        {
            Vec2 measuredFocus = new Vec2(320f, 180f); // 현재 카메라가 적용된 채 측정된 값
            Vec2 desired = new Vec2(desiredX, desiredY);

            ShotIntentState result = ShotZoomFocusReduction.Reduce(
                From, targetZoom, measuredFocus, desired);

            // 명중 검산: 목표 상태의 논리 focus를 목표 카메라로 다시 보이게 하면 desired다.
            float targetScale = ShotIntentMath.EvaluateCameraScale(result.Zoom);
            Vec2 visibleAfter = result.FocusPointInRigSpace * targetScale + result.PanInRigSpace;

            Assert.That(result.Zoom, Is.EqualTo(targetZoom));
            Assert.That(visibleAfter.X, Is.EqualTo(desired.X).Within(1e-2f));
            Assert.That(visibleAfter.Y, Is.EqualTo(desired.Y).Within(1e-2f));
        }

        [Test]
        public void zoom_focus의_논리_focus는_현재_카메라를_벗긴_값이다()
        {
            // 종전 1~7단계의 4단계 그대로: (측정값 - 현재pan) / 현재배율.
            Vec2 measured = new Vec2(320f, 180f);

            ShotIntentState result = ShotZoomFocusReduction.Reduce(From, 1f, measured, Vec2.Zero);

            float fromScale = ShotIntentMath.EvaluateCameraScale(From.Zoom);
            Vec2 expected = new Vec2(
                (measured.X - From.PanInRigSpace.X) / fromScale,
                (measured.Y - From.PanInRigSpace.Y) / fromScale);

            Assert.That(result.FocusPointInRigSpace.X, Is.EqualTo(expected.X).Within(Eps));
            Assert.That(result.FocusPointInRigSpace.Y, Is.EqualTo(expected.Y).Within(Eps));
        }
    }
}
