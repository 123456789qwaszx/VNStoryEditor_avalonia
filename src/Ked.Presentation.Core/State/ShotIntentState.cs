namespace Ked.Presentation.Core
{
    /// <summary>
    /// 샷 의도 상태 — StageState의 샷 축 (U13-b-5 shot 묶음).
    ///
    /// 런타임 PresentationIntentState의 코어 대응. "저작된 의도"이지 최종 트랜스폼이
    /// 아니다 — 카메라 루트와 대상별 response로 푸는 것은 적용측(호스트 / 2b 렌더러)의 일이다.
    /// 정지 프레임이 필요로 하는 것은 이 세 값뿐이다.
    /// </summary>
    public readonly struct ShotIntentState
    {
        /// <summary>저작된 zoom 의도. 실제 카메라 배율은 ShotIntentMath.EvaluateCameraScale.</summary>
        public readonly float Zoom;

        /// <summary>rig-space 카메라 pan 오프셋.</summary>
        public readonly Vec2 PanInRigSpace;

        /// <summary>rig-space 논리 focus 지점. 프레이밍·대상 산개가 이 점 기준으로 풀린다.</summary>
        public readonly Vec2 FocusPointInRigSpace;

        public ShotIntentState(float zoom, Vec2 panInRigSpace, Vec2 focusPointInRigSpace)
        {
            Zoom = zoom;
            PanInRigSpace = panInRigSpace;
            FocusPointInRigSpace = focusPointInRigSpace;
        }

        public static readonly ShotIntentState Default =
            new ShotIntentState(0f, Vec2.Zero, Vec2.Zero);
    }

    /// <summary>
    /// 샷 의도 수학. zoom→배율 환산과 pan 역산의 유일한 자리다 —
    /// 커맨드(목표 계산)와 적용측(카메라 루트·response)이 같은 규약을 봐야 한다.
    /// 트윈 보간(Interpolate)은 시간의 세계라 호스트에 남는다.
    /// </summary>
    public static class ShotIntentMath
    {
        /// <summary>규약 상수: zoom 1당 배율 +0.05.</summary>
        public const float ZoomToScaleFactor = 0.05f;

        public static float EvaluateCameraScale(float zoom)
            => 1f + zoom * ZoomToScaleFactor;

        /// <summary>현재 보이는 위치에서 저작된 pan/배율을 제거해 논리 좌표를 복원한다.</summary>
        public static Vec2 RemoveCurrentCameraTransformFromFocusPoint(
            Vec2 focusPointInRigSpace,
            Vec2 currentPan,
            float currentScale)
        {
            return new Vec2(
                (focusPointInRigSpace.X - currentPan.X) / currentScale,
                (focusPointInRigSpace.Y - currentPan.Y) / currentScale);
        }

        /// <summary>
        /// 논리 focus가 원하는 화면 지점에 보이도록 pan을 역산한다.
        /// 적용측 규약: 보이는 위치 = 논리 위치 × 배율 + pan.
        /// </summary>
        public static Vec2 CalculatePanToPlaceFocusAtScreenPoint(
            Vec2 logicalFocusPointInRigSpace,
            Vec2 desiredPointInRigSpace,
            float targetScale)
        {
            return desiredPointInRigSpace - logicalFocusPointInRigSpace * targetScale;
        }
    }
}
