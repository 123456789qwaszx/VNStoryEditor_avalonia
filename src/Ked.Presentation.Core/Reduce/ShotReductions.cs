namespace Ked.Presentation.Core
{
    // ─────────────────────────────────────────────────────────────────
    // shot 계열의 "스펙 → 목표 상태" 변환 (U13-b-5 shot 묶음).
    //
    // 호스트의 ShotIntentCommandBase.BuildTargetState(from, scope)가 유일하게
    // 선언된 클레임 경계였다 — 이미 리듀서 모양이라 그대로 옮겨진다.
    // 샷 축의 목표는 노드 클레임이 아니라 ShotIntentState 그 자체다.
    // 의도→실제 트랜스폼 풀이(response 바인딩)는 적용측의 일이다.
    // ─────────────────────────────────────────────────────────────────

    /// <summary>shot_zoom — zoom만 바꾼다.</summary>
    public static class ShotZoomReduction
    {
        public static ShotIntentState Reduce(in ShotIntentState from, float zoom)
            => new ShotIntentState(zoom, from.PanInRigSpace, from.FocusPointInRigSpace);
    }

    /// <summary>shot_track — 현재 pan에 더한다.</summary>
    public static class ShotTrackReduction
    {
        public static ShotIntentState Reduce(in ShotIntentState from, Vec2 panDelta)
            => new ShotIntentState(from.Zoom, from.PanInRigSpace + panDelta, from.FocusPointInRigSpace);
    }

    /// <summary>shot_to — zoom과 pan을 절대값으로.</summary>
    public static class ShotToReduction
    {
        public static ShotIntentState Reduce(in ShotIntentState from, float zoom, Vec2 pan)
            => new ShotIntentState(zoom, pan, from.FocusPointInRigSpace);
    }

    /// <summary>shot_reset — 기본 샷으로.</summary>
    public static class ShotResetReduction
    {
        public static ShotIntentState Reduce()
            => ShotIntentState.Default;
    }

    /// <summary>
    /// shot_focus_to — 캐릭터 focus 지점이 화면의 지정 지점에 오는 zoom/pan.
    ///
    /// 입력의 measuredFocusInRigSpace는 "현재 카메라가 적용된 채로 측정된" 값이다
    /// (정착 focus 측정 — SettledFocusMath / 호스트 resolver가 낸다).
    /// 여기서 현재 카메라를 벗겨 논리 좌표로 되돌리고, 목표 배율에서
    /// 원하는 지점에 오도록 pan을 역산한다 — 종전 BuildTargetState의 1~7단계 그대로.
    /// </summary>
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
