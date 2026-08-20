namespace Ked.Presentation.Core
{
    /// <summary>
    /// 초상 축 — 변형(pose)과 표정(face·face_swap), 그리고 그 결과인 사이징.
    ///
    /// 어느 커맨드가 어느 축을 건드리는지가 이 묶음의 전부다:
    ///
    ///   커맨드                          변형    표정    사이징
    ///   cast (slot, char, var, emo)     갱신    인자    ✅
    ///   show (target, faceToken)        유지    인자    ✅
    ///   face / face_swap (target, emo)  유지    인자    ✅
    ///   pose (target, variant)          갱신    —       ❌
    ///
    /// **pose가 사이징을 다시 계산하지 않는 것은 런타임 실동작이다** —
    /// SetPortraitPoseCommandCharR의 스프라이트 교체가 지금 비활성이라 이 시점엔
    /// 아무것도 바뀌지 않고, 다음 show/face/face_swap이 새 변형으로 resolver를 부를 때 바뀐다.
    ///
    /// 표정은 상태가 아니다 — 런타임 CastBinding도 변형만 들고, 표정은 커맨드 인자로만 온다.
    /// </summary>
    public static partial class StageReducer
    {
        /// <summary>pose (target, variant) — 변형만 갱신한다. 사이징은 다음 표정 커맨드의 몫이다.</summary>
        private static bool ApplyPose(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            state.SetVariant(slotKey, cmd.Arg(1, PortraitDimensionsFileDto.DefaultVariantKey));
            return true;
        }

        /// <summary>face / face_swap (target, emotion) — 표정을 인자로 받아 사이징을 다시 접는다.</summary>
        private static bool ApplyFace(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            return ApplyPortraitSizing(state, slotKey, cmd.Arg(1), tuning, out reason);
        }

        /// <summary>
        /// 표정 하나에 대한 사이징 폴드. cast·show도 같은 자리를 지난다 —
        /// 브리지가 그 둘을 SetPortraitSprite로 팬아웃하기 때문이다.
        /// </summary>
        private static bool ApplyPortraitSizing(
            StageState state, string slotKey, string emotionKey,
            StageReducerTuning tuning, out string reason)
        {
            if (!PortraitSizingStageReduction.TryReduce(
                    state, slotKey, emotionKey, tuning.PortraitDimensions,
                    out StageNodeClaim claim, out reason))
            {
                return false;
            }

            state.Apply(claim);
            return true;
        }
    }
}
