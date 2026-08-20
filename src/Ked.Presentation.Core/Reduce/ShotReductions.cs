namespace Ked.Presentation.Core
{
    // 샷 축의 목표는 노드 클레임이 아니라 ShotIntentState 그 자체.
    // 의도를 실제 트랜스폼으로 푸는 것(카메라 루트·response 바인딩)은 적용측의 일.

    // shot_zoom - zoom만 바꾼다.
    public static class ShotZoomReduction
    {
        public static ShotIntentState Reduce(in ShotIntentState from, float zoom)
            => new(zoom, from.PanInRigSpace, from.FocusPointInRigSpace);
    }

    // shot_track - 현재 pan에 더한다.
    public static class ShotTrackReduction
    {
        public static ShotIntentState Reduce(in ShotIntentState from, Vec2 panDelta)
            => new(from.Zoom, from.PanInRigSpace + panDelta, from.FocusPointInRigSpace);
    }

    // shot_to - zoom과 pan을 절대값으로.
    public static class ShotToReduction
    {
        public static ShotIntentState Reduce(in ShotIntentState from, float zoom, Vec2 pan)
            => new(zoom, pan, from.FocusPointInRigSpace);
    }

    // shot_reset - 기본 샷으로.
    public static class ShotResetReduction
    {
        public static ShotIntentState Reduce()
            => ShotIntentState.Default;
    }

    // shot_focus_to - 캐릭터 focus 지점이 화면의 지정 지점에 오는 zoom/pan.
    //
    // 입력의 measuredFocusInRigSpace는 "현재 카메라가 적용된 채로 측정된" 값.
    // (정착 focus 측정 결과). 카메라를 영향을 제거해, 논리 좌표로 되돌리고,
    // 목표 배율에서 원하는 지점에 오도록 pan을 역산
    public static class ShotZoomFocusReduction
    {
        public static ShotIntentState Reduce(
            in ShotIntentState from,
            float targetZoom,
            Vec2 measuredFocusInRigSpace,
            Vec2 desiredPointInRigSpace)
        {
            float fromScale = ShotIntentMath.EvaluateCameraScale(from.Zoom);
            float targetScale = ShotIntentMath.EvaluateCameraScale(targetZoom);

            Vec2 logicalFocusPoint = ShotIntentMath.RemoveCurrentCameraTransformFromFocusPoint(
                measuredFocusInRigSpace, from.PanInRigSpace, fromScale);

            Vec2 targetPan = ShotIntentMath.CalculatePanToPlaceFocusAtScreenPoint(
                logicalFocusPoint, desiredPointInRigSpace, targetScale);

            return new ShotIntentState(targetZoom, targetPan, logicalFocusPoint);
        }
    }
}