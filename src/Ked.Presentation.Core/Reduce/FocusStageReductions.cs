using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 화면 지점 프리셋의 rig-space 좌표 (호스트 ScreenFocusPointResolver의 비율 규약).
    ///
    /// ⚠ 이 비율은 게임별 값이므로 원칙적으로는 tuning 데이터가 맞다
    /// (reduction-boundary.md 백로그). 지금은 호스트에도 같은 상수가 있어 두 곳이다 —
    /// U12 덤프에 screen-focus-points.json을 추가할 때 여기를 지운다.
    /// </summary>
    public static class ScreenPointRatios
    {
        public const float OuterXRatio = 0.24f;
        public const float OuterYRatio = 0.16f;
        public const float InnerXRatio = 0.14f;
        public const float InnerYRatio = 0.09f;

        /// <summary>지점 이름 → rig-space 좌표. 모르면 false.</summary>
        public static bool TryResolve(Vec2 frameSize, string pointName, out Vec2 point)
        {
            float ox = frameSize.X * OuterXRatio;
            float oy = frameSize.Y * OuterYRatio;
            float ix = frameSize.X * InnerXRatio;
            float iy = frameSize.Y * InnerYRatio;

            switch ((pointName ?? "").Trim().ToLowerInvariant())
            {
                case "center": point = Vec2.Zero; return true;

                case "left": point = new Vec2(-ox, 0f); return true;
                case "right": point = new Vec2(ox, 0f); return true;
                case "top": point = new Vec2(0f, oy); return true;
                case "bottom": point = new Vec2(0f, -oy); return true;

                case "tl": point = new Vec2(-ox, oy); return true;
                case "tr": point = new Vec2(ox, oy); return true;
                case "bl": point = new Vec2(-ox, -oy); return true;
                case "br": point = new Vec2(ox, -oy); return true;

                case "inner_tl": point = new Vec2(-ix, iy); return true;
                case "inner_tr": point = new Vec2(ix, iy); return true;
                case "inner_bl": point = new Vec2(-ix, -iy); return true;
                case "inner_br": point = new Vec2(ix, -iy); return true;

                default: point = Vec2.Zero; return false;
            }
        }
    }

    /// <summary>
    /// focus 로컬 오프셋 해석 — 런타임 CharacterFocusTuningDBSO.ResolveOffset과 같은 합:
    ///   base[preset] + entry(character).defaultOffset + entry.offsets[preset] + 커맨드 오프셋
    ///
    /// ⚠ facing 미러 축은 아직 폴드에 없다(기본 Right = 항등).
    /// mirror 커맨드를 쓰는 장면은 불일치로 드러난다 — 그때 facing 축을 폴드에 올린다.
    /// </summary>
    public static class FocusOffsetMath
    {
        public static Vec2 Resolve(
            FocusTuningBodyDto tuning,
            string characterKey,
            string presetName,
            Vec2 commandOffset)
        {
            Vec2 offset = Vec2.Zero;

            if (tuning?.baseOffsets != null && tuning.baseOffsets.TryGet(presetName, out Vec2 baseOffset))
                offset += baseOffset;

            if (tuning != null && characterKey != null && tuning.TryGetEntry(characterKey, out FocusEntryDto entry))
            {
                if (entry.defaultOffset != null)
                    offset += entry.defaultOffset.ToVec2();

                if (entry.offsets != null && entry.offsets.TryGet(presetName, out Vec2 presetOffset))
                    offset += presetOffset;
            }

            return offset + commandOffset;
        }
    }

    /// <summary>place 계열 — focus 지점을 화면 지점으로 보내는 이동 축(Track_Focus) 목표.</summary>
    public static class PlaceFocusStageReduction
    {
        /// <summary>스펙 기본 moveTarget (브리지: CharacterRigTarget.CharSlot_Track_Focus).</summary>
        public const string MoveNodeId = "CharSlot_Track_Focus";

        public const string MeasureNodeId = "CharacterPortrait_VisualOffset";

        public static bool TryReduce(
            StageState state,
            string slotKey,
            string characterKey,
            string focusPresetName,
            string screenPointName,
            FocusTuningBodyDto focusTuning,
            out StageNodeClaim claim,
            out string reason)
        {
            claim = default;

            if (!ScreenPointRatios.TryResolve(state.Nodes.RootSpace.Size, screenPointName, out Vec2 desired))
            {
                reason = $"화면 지점 '{screenPointName}'을 모른다";
                return false;
            }

            Vec2 focusOffset = FocusOffsetMath.Resolve(focusTuning, characterKey, focusPresetName, Vec2.Zero);

            string measureKey = StageState.NodeKeyOf(slotKey, MeasureNodeId);
            string moveKey = StageState.NodeKeyOf(slotKey, MoveNodeId);

            List<string> chainKeys = new();
            RectNodeState[] chain = state.Nodes.BuildChainTo(measureKey, chainKeys);

            int moveIndex = chainKeys.IndexOf(moveKey);

            if (moveIndex < 0)
            {
                reason = $"이동 축 '{moveKey}'가 측정 체인에 없다";
                return false;
            }

            Vec2 solved = SettledFocusMath.SolveFocusPlacement(
                chain,
                state.Nodes.RootSpace,
                moveIndex,
                focusOffset,
                desired,
                state.Nodes.GetState(moveKey).AnchoredPosition);

            claim = StageNodeClaim.AnchoredPosition(moveKey, solved);
            reason = null;
            return true;
        }
    }

    /// <summary>size 계열 — depth 프리셋 적용(focus 보존 보정 포함). DepthY 위치 + DepthScale 배율 두 장.</summary>
    public static class SetDepthStageReduction
    {
        public const string DepthYNodeId = "CharSlot_DepthY";
        public const string DepthScaleNodeId = "CharSlot_DepthScale";
        public const string MeasureNodeId = "CharacterPortrait_VisualOffset";

        /// <param name="preserveFocusToken">
        /// 보존할 focus 프리셋 토큰. **항상 커맨드 인자에서 온다**(브리지 기본값 "bust").
        ///
        /// ⚠ 덤프의 preset.preserveFocusPreset / preserveFocusOffset은 쓰지 않는다 —
        /// 런타임 CharacterDepthResolver.ResolveRawDepth가 프리셋 값을 읽은 직후
        /// 커맨드 인자로 무조건 덮어쓰기 때문이다(그 두 필드는 런타임에서 사장 데이터다).
        /// 프리셋 값을 폴백으로 쓰면 재생과 갈라진다.
        /// </param>
        public static bool TryReduce(
            StageState state,
            string slotKey,
            string characterKey,
            string depthPresetKey,
            string preserveFocusToken,
            DepthPresetSetDto depthPresets,
            FocusTuningBodyDto focusTuning,
            out StageNodeClaim depthYClaim,
            out StageNodeClaim depthScaleClaim,
            out string reason)
        {
            depthYClaim = default;
            depthScaleClaim = default;

            if (depthPresets == null)
            {
                reason = "tuning에 depth 프리셋이 없다";
                return false;
            }

            if (!depthPresets.TryGet(depthPresetKey, out DepthPresetDto preset))
            {
                // 숫자 레벨(size c1 5)이 여기로 온다 — 커브 폴드는 미지원이다.
                reason = $"depth 프리셋 '{depthPresetKey}'를 모른다 (레벨 수치는 커브 폴드 미지원)";
                return false;
            }

            if (!FocusPresetName.TryNormalizeToken(preserveFocusToken, out string preserveName))
            {
                reason = $"focus 프리셋 토큰 '{preserveFocusToken}'을 모른다";
                return false;
            }

            // 커맨드 오프셋은 0이다 — yarn 경로의 spec.focusOffset이 기본값이고,
            // 런타임이 preserveFocusOffset을 그 값으로 덮어쓴다.
            Vec2 focusOffset = FocusOffsetMath.Resolve(focusTuning, characterKey, preserveName, Vec2.Zero);

            string measureKey = StageState.NodeKeyOf(slotKey, MeasureNodeId);
            string depthYKey = StageState.NodeKeyOf(slotKey, DepthYNodeId);
            string depthScaleKey = StageState.NodeKeyOf(slotKey, DepthScaleNodeId);

            List<string> chainKeys = new();
            RectNodeState[] chain = state.Nodes.BuildChainTo(measureKey, chainKeys);

            int depthYIndex = chainKeys.IndexOf(depthYKey);
            int depthScaleIndex = chainKeys.IndexOf(depthScaleKey);

            if (depthYIndex < 0 || depthScaleIndex < 0)
            {
                reason = "depth 축이 측정 체인에 없다";
                return false;
            }

            Vec2 rawDepthY = preset.depthY?.ToVec2() ?? Vec2.Zero;
            Vec2 targetScale = new(preset.depthScale, preset.depthScale);

            Vec2 solvedDepthY = SettledFocusMath.SolveDepthYPreservingFocus(
                chain,
                state.Nodes.RootSpace,
                depthYIndex,
                depthScaleIndex,
                focusOffset,
                rawDepthY,
                targetScale);

            depthYClaim = StageNodeClaim.AnchoredPosition(depthYKey, solvedDepthY);
            depthScaleClaim = StageNodeClaim.LocalScaleXY(depthScaleKey, targetScale);
            reason = null;
            return true;
        }
    }
}
