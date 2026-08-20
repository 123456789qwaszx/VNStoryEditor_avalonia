namespace Ked.Presentation.Core
{
    /// <summary>
    /// show · fade — 등장과 가시성.
    ///
    /// show는 한 커맨드가 아니라 묶음이다: 축 리셋 + 역할 앵커 + 가시성 두 장.
    /// 리셋 목록은 리그 스키마 지식이라 리덕션이 아니라 여기가 갖는다.
    /// </summary>
    public static partial class StageReducer
    {
        // SetAnchorCommandCharR의 리셋 목록(두 플래그 모두 켜진 show 경로).
        private static readonly string[] ShowResetPositionIds =
        {
            "CharSlot_Track", "CharSlot_Track_X", "CharSlot_Track_Y",
            "CharacterPortrait_Track", "CharacterPortrait_Track_Move",
            "CharacterPortrait_Track_Move_X", "CharacterPortrait_Track_Move_Y",
            "CharacterPortrait_SwayPivot", "CharacterPortrait_Shake",
        };

        private static readonly string[] ShowResetEulerIds =
        {
            // CharSlot_SwayPivot은 목록에 없다 — rotate_by의 표적인데 show가 되돌리지
            // 않는 것이 런타임 실동작이다(StagingBundleReductionTests가 고정).
            "CharSlot_Rotation",
            "CharacterPortrait_Rotation", "CharacterPortrait_SwayPivot", "CharacterPortrait_Shake",
        };

        private static readonly string[] ShowResetScaleIds =
        {
            "CharSlot_Scale",
            "CharacterPortrait_SwayPivot", "CharacterPortrait_Shake",
            "CharacterPortrait_ActingScale", "CharacterPortrait_ActingScale_X", "CharacterPortrait_ActingScale_Y",
        };

        private static bool ApplyShow(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            // 역할 앵커: cast된 캐릭터의 튜닝. 배역이 없거나 엔트리가 없으면 Default —
            // 그게 데이터의 의미다. 튜닝 파일 자체가 없으면 침묵하지 않는다.
            SetAnchorReduction.RoleAnchorTuning anchorTuning = SetAnchorReduction.RoleAnchorTuning.Default;

            if (tuning.RoleAnchors == null)
            {
                state.AddUnhandled(cmd, "role-anchor 튜닝이 tuning에 없다 — 앵커를 기본값으로 접었다");
            }
            else if (state.TryGetCharacter(slotKey, out string characterKey))
            {
                tuning.RoleAnchors.TryGet(characterKey, out anchorTuning);
            }

            StageNodeClaim[] claims = SetAnchorReduction.Reduce(
                StageState.NodeKeyOf(slotKey, "CharacterPortrait_VisualOffset"),
                anchorTuning,
                Prefixed(slotKey, ShowResetPositionIds),
                Prefixed(slotKey, ShowResetEulerIds),
                Prefixed(slotKey, ShowResetScaleIds));

            foreach (StageNodeClaim claim in claims)
                state.Apply(claim);

            // 초상 사이징. faceToken의 기본값은 "e1"(생략된 인자)이고,
            // **명시적으로 빈 토큰은 "2"다** — 다른 규칙이라 별칭 파서가 나눠 본다.
            string emotionKey = PortraitKeyNormalizer.ParseShowFaceAlias(cmd.Arg(1, "e1"));

            if (!ApplyPortraitSizing(state, slotKey, emotionKey, tuning, out string sizingReason))
                state.AddUnhandled(cmd, sizingReason);

            // 가시성 두 장: 리그 루트 + 초상 스프라이트 루트.
            state.Apply(FadeInReduction.Reduce(StageState.NodeKeyOf(slotKey, RigSchemaLoader.RootKey)));
            state.Apply(FadeInReduction.Reduce(StageState.NodeKeyOf(slotKey, "CharacterPortraitSprite_Root")));

            return true;
        }

        private static bool ApplyFade(StageState state, in StageCommand cmd, bool visible, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            string nodeKey = StageState.NodeKeyOf(slotKey, "CharacterPortraitSprite_Root");

            state.Apply(visible
                ? FadeInReduction.Reduce(nodeKey)
                : FadeOutReduction.Reduce(nodeKey));

            return true;
        }
    }
}
