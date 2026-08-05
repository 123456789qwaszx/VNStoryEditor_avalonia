using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    /// <summary>리듀서에 주입되는 게임별 값 (코어 계약 2: 코드가 아니라 데이터).</summary>
    public sealed class StageReducerTuning
    {
        /// <summary>U12-전체 리그 스키마 덤프. slot 폴드가 캐릭터 리그를 여기서 세운다.</summary>
        public RigSchemasFileDto RigSchemas;

        /// <summary>기준 해상도 가로 폭. 1u 환산의 유일한 입력 (D-core-1).</summary>
        public float ReferenceStageWidth;

        /// <summary>기준 해상도. 무대 루트 공간의 크기다.</summary>
        public Vec2 BaseResolution;

        /// <summary>presets/depth.json — size 계열 폴드의 프리셋 값. 없으면 size는 Unhandled.</summary>
        public DepthPresetSetDto DepthPresets;

        /// <summary>presets/focus-tuning.json — place/size의 focus 오프셋. 없으면 base 0으로 접는다.</summary>
        public FocusTuningBodyDto FocusTuning;

        /// <summary>portrait-dimensions.json — 초상 사이징 폴드. 없으면 사이징은 Unhandled.</summary>
        public PortraitDimensionsFileDto PortraitDimensions;
    }

    /// <summary>
    /// 커맨드 열 → 확정 무대 상태 (U14의 코어 절반).
    ///
    /// Apply는 순수 함수다: 입력 상태를 바꾸지 않고 새 상태를 돌려준다.
    /// 시간·랜덤·IO 없음 — duration 인자는 정지 프레임에 무의미하므로 파싱조차 안 한다.
    /// 인식 못 하거나 아직 못 접는 커맨드는 버리지 않고 Unhandled에 남는다(커맨드명+출처).
    ///
    /// 현재 어휘: slot 계열(리그 스폰) · cast/pose/show/face/face_swap(초상 사이징) ·
    ///   place/size · nudge/move/scale/rotate 계열 · shot 5종 · char_to(구조 축 기록).
    /// 명시적 한계(전부 Unhandled로 남는다): 배경/오버레이/이펙트/오디오/트랜지션과
    ///   아직 디스패치되지 않은 캐릭터 연기 커맨드.
    ///
    /// ⚠ v1 가정 둘 — U14 하네스가 판정한다:
    /// 1. 스테이지/레이어 컨테이너는 항등 트랜스폼(스트레치 풀)이라 리그를 루트
    ///    직속으로 세워도 좌표가 같다. (컨테이너 스키마는 U12 덤프에 없다 — 어긋나면
    ///    U12에 컨테이너 덤프를 추가하는 것이 맞다)
    /// 2. 커맨드 기본값(예: slot의 stage00/mid)은 브리지 시그니처를 따라 여기 박았다.
    ///    카탈로그가 데이터(U10)가 되면 tuning으로 옮긴다.
    /// </summary>
    public static class StageReducer
    {
        public static StageState CreateInitialState(StageReducerTuning tuning)
        {
            Require(tuning);
            return new StageState(new RectSpace(tuning.BaseResolution, Vec2.Half));
        }

        public static StageState Apply(StageState state, in StageCommand command, StageReducerTuning tuning)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            Require(tuning);

            StageState next = state.Clone();

            try
            {
                if (!TryApply(next, command, tuning, out string reason))
                    next.AddUnhandled(command, reason);
            }
            catch (Exception ex)
            {
                // 접다가 터진 커맨드도 침묵 대신 기록이다. 상태는 클론이라 원본은 무사하다.
                next.AddUnhandled(command, $"폴드 중 예외: {ex.Message}");
            }

            return next;
        }

        public static StageState ApplyAll(
            StageState state, IEnumerable<StageCommand> commands, StageReducerTuning tuning)
        {
            foreach (StageCommand command in commands)
                state = Apply(state, command, tuning);

            return state;
        }

        // ── 디스패치 ─────────────────────────────────────────────────

        private static bool TryApply(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            reason = null;

            switch (cmd.Name)
            {
                // 슬롯 스폰 (SetupCharRig)
                case "slot":   return ApplySlot(state, cmd, tuning, cmd.Arg(1, "stage00"), cmd.Arg(2, "mid"), out reason);
                case "slot00": return ApplySlot(state, cmd, tuning, "stage00", cmd.Arg(1, "mid"), out reason);
                case "slot01": return ApplySlot(state, cmd, tuning, "stage01", cmd.Arg(1, "mid"), out reason);
                case "slot02": return ApplySlot(state, cmd, tuning, "stage02", cmd.Arg(1, "mid"), out reason);

                // show — SetAnchor(리셋+앵커) + 가시성 + 표정별 초상 사이징.
                case "show": return ApplyShow(state, cmd, tuning, out reason);

                // 배역·variant·별칭 (커맨드 대상 해석과 portrait resolver의 전제)
                case "cast": return ApplyCast(state, cmd, tuning, out reason);
                case "pose": return ApplyPose(state, cmd, out reason);
                case "actor": return ApplyActor(state, cmd, out reason);

                // fade — 초상 스프라이트 루트의 가시성 (브리지 EnqueueFadeIn/OutDslSpec과 동일 표적)
                case "fade_in": return ApplyFade(state, cmd, visible: true, out reason);
                case "fade_out": return ApplyFade(state, cmd, visible: false, out reason);

                // 표정 전환 — 사이징 재계산 포함 (face_swap의 와이프 연출은 트윈의 일)
                case "face": return ApplyFaceChange(state, cmd, tuning, cmd.Arg(1), out reason);
                case "face_swap": return ApplyFaceChange(state, cmd, tuning, cmd.Arg(1), out reason);

                // place 계열 — focus 지점을 화면 지점으로 (SettledFocusMath.SolveFocusPlacement)
                case "place": return ApplyPlace(state, cmd, tuning, cmd.Arg(2, "center"), out reason);
                case "place_left": return ApplyPlace(state, cmd, tuning, "left", out reason);
                case "place_center": return ApplyPlace(state, cmd, tuning, "center", out reason);
                case "place_right": return ApplyPlace(state, cmd, tuning, "right", out reason);
                case "place_tl": return ApplyPlace(state, cmd, tuning, "tl", out reason);
                case "place_top": return ApplyPlace(state, cmd, tuning, "top", out reason);
                case "place_tr": return ApplyPlace(state, cmd, tuning, "tr", out reason);
                case "place_bl": return ApplyPlace(state, cmd, tuning, "bl", out reason);
                case "place_bottom": return ApplyPlace(state, cmd, tuning, "bottom", out reason);
                case "place_br": return ApplyPlace(state, cmd, tuning, "br", out reason);
                case "place_inner_tl": return ApplyPlace(state, cmd, tuning, "inner_tl", out reason);
                case "place_inner_tr": return ApplyPlace(state, cmd, tuning, "inner_tr", out reason);
                case "place_inner_bl": return ApplyPlace(state, cmd, tuning, "inner_bl", out reason);
                case "place_inner_br": return ApplyPlace(state, cmd, tuning, "inner_br", out reason);

                // size 계열 — depth 프리셋 (focus 보존 보정 포함, SolveDepthYPreservingFocus)
                case "size": return ApplySize(state, cmd, tuning, cmd.Arg(1), cmd.Arg(2), out reason);
                case "size_far": return ApplySize(state, cmd, tuning, "far", cmd.Arg(1), out reason);
                case "size_back": return ApplySize(state, cmd, tuning, "back", cmd.Arg(1), out reason);
                case "size_mid": return ApplySize(state, cmd, tuning, "mid", cmd.Arg(1), out reason);
                case "size_front": return ApplySize(state, cmd, tuning, "front", cmd.Arg(1), out reason);
                case "size_close": return ApplySize(state, cmd, tuning, "close", cmd.Arg(1), out reason);

                // nudge (MoveBy: Track_X / Track_Y, 상대)
                case "left":  return ApplyNudge(state, cmd, tuning, -1f, 0f, "CharSlot_Track_X", out reason);
                case "right": return ApplyNudge(state, cmd, tuning, 1f, 0f, "CharSlot_Track_X", out reason);
                case "up":    return ApplyNudge(state, cmd, tuning, 0f, 1f, "CharSlot_Track_Y", out reason);
                case "down":  return ApplyNudge(state, cmd, tuning, 0f, -1f, "CharSlot_Track_Y", out reason);

                // move-per-frame (거리 = 1u × 프레임 수)
                case "left_per":  return ApplyMovePer(state, cmd, tuning, -1f, 0f, "CharSlot_Track_X", out reason);
                case "right_per": return ApplyMovePer(state, cmd, tuning, 1f, 0f, "CharSlot_Track_X", out reason);
                case "up_per":    return ApplyMovePer(state, cmd, tuning, 0f, 1f, "CharSlot_Track_Y", out reason);
                case "down_per":  return ApplyMovePer(state, cmd, tuning, 0f, -1f, "CharSlot_Track_Y", out reason);

                // staging 이동/스케일/회전
                case "move_by":      return ApplyMoveBy(state, cmd, tuning, out reason);
                case "move_reset":   return ApplyMoveReset(state, cmd, out reason);
                case "scale_by":     return ApplyScaleBy(state, cmd, out reason);
                case "scale_reset":  return ApplyScaleReset(state, cmd, out reason);
                case "rotate_by":    return ApplyRotateBy(state, cmd, out reason);
                case "rotate_reset": return ApplyRotateReset(state, cmd, out reason);

                // shot
                case "shot_focus_to": return ApplyShotFocusTo(state, cmd, tuning, out reason);
                case "shot_zoom":  return ApplyShotZoom(state, cmd, out reason);
                case "shot_to":    return ApplyShotTo(state, cmd, tuning, out reason);
                case "shot_track": return ApplyShotTrack(state, cmd, tuning, out reason);
                case "shot_reset":
                    state.Shot = ShotResetReduction.Reduce();
                    return true;

                // 구조 축 (v1: 부착 기록만 — 컨테이너 항등 가정으로 좌표 영향 없음)
                case "char_to":    return ApplyCharTo(state, cmd, cmd.Arg(1, "stage00"), cmd.Arg(2, "mid"), out reason);
                case "char_to_s0": return ApplyCharTo(state, cmd, "stage00", cmd.Arg(1, "mid"), out reason);
                case "char_to_s1": return ApplyCharTo(state, cmd, "stage01", cmd.Arg(1, "mid"), out reason);
                case "char_to_s2": return ApplyCharTo(state, cmd, "stage02", cmd.Arg(1, "mid"), out reason);

                default:
                    reason = "아직 코어로 이관되지 않은 커맨드";
                    return false;
            }
        }

        // ── 슬롯 ─────────────────────────────────────────────────────

        private static bool ApplySlot(
            StageState state, in StageCommand cmd, StageReducerTuning tuning,
            string stageKey, string layerKey, out string reason)
        {
            if (!TryGetSlotKey(cmd, out string slotKey, out reason))
                return false;

            if (state.HasSlot(slotKey))
            {
                // 같은 슬롯 재선언: 런타임은 기존 리그를 재사용한다. 부착만 갱신한다.
                state.SetAttachment(slotKey, new SlotAttachment(stageKey, layerKey));
                return true;
            }

            if (tuning.RigSchemas == null)
            {
                reason = "tuning에 리그 스키마가 없어 슬롯을 세울 수 없다";
                return false;
            }

            RigSchemaRigDto rig = null;

            for (int i = 0; i < tuning.RigSchemas.rigs.Count; i++)
            {
                if (tuning.RigSchemas.rigs[i]?.rigKind == "character")
                    rig = tuning.RigSchemas.rigs[i];
            }

            if (rig == null)
            {
                reason = "리그 스키마 덤프에 character 리그가 없다";
                return false;
            }

            RigSchemaLoader.AddRigTo(state.Nodes, rig, keyPrefix: slotKey + "/");
            state.RegisterSlot(slotKey);
            state.SetAttachment(slotKey, new SlotAttachment(stageKey, layerKey));

            // 초기 가시성: 덤프의 CanvasGroup 초기 alpha (초상·이모지 루트는 0으로 태어난다).
            for (int i = 0; i < rig.nodes.Count; i++)
            {
                RigSchemaNodeDto node = rig.nodes[i];

                if (node != null && node.hasCanvasGroup)
                    state.SetAlpha(slotKey + "/" + node.id, node.canvasGroupAlpha);
            }

            return true;
        }

        // ── show ─────────────────────────────────────────────────────

        private static readonly string[] ShowResetPositionIds =
        {
            "CharSlot_Track", "CharSlot_Track_X", "CharSlot_Track_Y",
            "CharacterPortrait_Track", "CharacterPortrait_Track_Move",
            "CharacterPortrait_Track_Move_X", "CharacterPortrait_Track_Move_Y",
            "CharacterPortrait_SwayPivot", "CharacterPortrait_Shake",
        };

        private static readonly string[] ShowResetEulerIds =
        {
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

            // SetAnchor 상당: 연기 축 리셋 + 역할 앵커.
            // 역할 앵커 튜닝은 아직 tuning에 배선되지 않아 Default(오프셋 0·배율 1)다 —
            // 현재 덤프의 엔트리들이 실제로 그 값이라 오늘 데이터로는 등가다.
            StageNodeClaim[] claims = SetAnchorReduction.Reduce(
                StageState.NodeKeyOf(slotKey, "CharacterPortrait_VisualOffset"),
                SetAnchorReduction.RoleAnchorTuning.Default,
                Prefixed(slotKey, ShowResetPositionIds),
                Prefixed(slotKey, ShowResetEulerIds),
                Prefixed(slotKey, ShowResetScaleIds));

            foreach (StageNodeClaim claim in claims)
                state.Apply(claim);

            // FadeIn 두 장: 리그 루트 + 초상 스프라이트 루트.
            state.Apply(FadeInReduction.Reduce(StageState.NodeKeyOf(slotKey, RigSchemaLoader.RootKey)));
            state.Apply(FadeInReduction.Reduce(StageState.NodeKeyOf(slotKey, "CharacterPortraitSprite_Root")));

            // 표정: show의 faceToken 별칭("e1", 빈 값은 "2") → 표정 코드 + 사이징 재계산.
            // Yarn 브리지의 선택 인자 기본값은 e1이다. Parser의 빈 값 규칙("2")은
            // 명시적으로 빈 토큰을 넘긴 경우의 규칙이지, 생략된 인자의 기본값이 아니다.
            string faceRaw = PortraitEmotionCode.ParseShowFaceAlias(cmd.Arg(1, "e1"));

            if (PortraitEmotionCode.TryNormalize(faceRaw, out string showEmotion))
            {
                state.TryGetPortrait(slotKey, out char variantSuffix, out _);
                state.SetPortrait(slotKey, variantSuffix, showEmotion);
                ApplyPortraitSizing(state, cmd, tuning, slotKey);
            }
            else
            {
                state.AddUnhandled(cmd, $"show의 표정 토큰 '{cmd.Arg(1)}'을 읽지 못했다");
            }

            return true;
        }

        /// <summary>
        /// pose — CastRegistry의 variant만 바꾼다. 현재 런타임의
        /// SetPortraitPoseCommandCharR는 sprite 교체 코드가 비활성이라 이 시점에는
        /// sizeDelta를 다시 계산하지 않는다. 다음 show/face/face_swap이 새 variant로
        /// resolver를 호출할 때 사이징이 바뀐다.
        /// </summary>
        private static bool ApplyPose(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!state.TryGetCharacter(slotKey, out _))
            {
                reason = $"pose 대상 슬롯 '{slotKey}'에 cast가 없다";
                return false;
            }

            string variant = cmd.Arg(1, "a").Trim().ToLowerInvariant();
            char variantSuffix = variant.Length > 0 ? variant[variant.Length - 1] : 'a';

            state.TryGetPortrait(slotKey, out _, out string emotion);
            state.SetPortrait(slotKey, variantSuffix, emotion ?? PortraitEmotionCode.Default);

            return true;
        }

        // ── 배역·별칭·fade ───────────────────────────────────────────

        private static bool ApplyCast(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            string characterKey = cmd.Arg(1);

            if (string.IsNullOrEmpty(characterKey))
            {
                reason = "cast에 캐릭터 키가 없다";
                return false;
            }

            state.SetCast(slotKey, characterKey);

            // cast (slot, char, [variant=a], [emotion=1]) — 브리지가 pose+face로 팬아웃하는
            // 초기 초상 상태를 그대로 접는다.
            string variantArg = cmd.Arg(2, "a").Trim().ToLowerInvariant();
            char variantSuffix = variantArg.Length > 0 ? variantArg[variantArg.Length - 1] : 'a';

            if (!PortraitEmotionCode.TryNormalize(cmd.Arg(3, "1"), out string emotion))
                emotion = PortraitEmotionCode.Default;

            state.SetPortrait(slotKey, variantSuffix, emotion);
            ApplyPortraitSizing(state, cmd, tuning, slotKey);

            return true;
        }

        /// <summary>
        /// 초상 사이징 (CharRigImageSizingPolicy.HeightFitPreserveAspect 상당):
        /// 폭 = 부모 높이 × 현재 표정 스프라이트의 종횡비, sizeDelta.y = 0.
        /// 치수를 못 찾으면 침묵 대신 Unhandled로 남긴다.
        /// </summary>
        private static void ApplyPortraitSizing(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, string slotKey)
        {
            if (!state.TryGetCharacter(slotKey, out string characterKey))
                return; // 배역이 없으면 그릴 초상도 없다

            state.TryGetPortrait(slotKey, out char variantSuffix, out string emotion);
            emotion ??= PortraitEmotionCode.Default;

            if (tuning.PortraitDimensions == null)
            {
                state.AddUnhandled(cmd, "초상 사이징을 접지 못했다: 치수 덤프(portrait-dimensions.json)가 없다");
                return;
            }

            if (!tuning.PortraitDimensions.TryGetAspect(
                    characterKey, variantSuffix, emotion, out float aspect, out string reason))
            {
                state.AddUnhandled(cmd, "초상 사이징을 접지 못했다: " + reason);
                return;
            }

            string imageKey = StageState.NodeKeyOf(slotKey, "CharacterPortraitSprite_Image");
            string parentKey = StageState.NodeKeyOf(slotKey, "CharacterPortraitSprite_Root");

            float parentHeight = state.Nodes.GetRectSize(parentKey).Y;

            RectNodeState imageState = state.Nodes.GetState(imageKey);
            state.Nodes.SetState(imageKey, imageState.WithSizeDelta(new Vec2(parentHeight * aspect, 0f)));
        }

        /// <summary>face / face_swap — 표정 전환 + 사이징 재계산.</summary>
        private static bool ApplyFaceChange(
            StageState state, in StageCommand cmd, StageReducerTuning tuning,
            string emotionToken, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!PortraitEmotionCode.TryNormalize(emotionToken, out string emotion))
            {
                reason = $"표정 토큰 '{emotionToken}'을 읽지 못했다";
                return false;
            }

            state.TryGetPortrait(slotKey, out char variantSuffix, out _);
            state.SetPortrait(slotKey, variantSuffix, emotion);
            ApplyPortraitSizing(state, cmd, tuning, slotKey);

            return true;
        }

        private static bool ApplyActor(StageState state, in StageCommand cmd, out string reason)
        {
            string aliasSymbol = cmd.Arg(0);
            string targetKey = cmd.Arg(1);

            if (string.IsNullOrEmpty(aliasSymbol) || string.IsNullOrEmpty(targetKey))
            {
                reason = "actor 인자가 모자란다 (별칭, 대상)";
                return false;
            }

            state.SetAlias(aliasSymbol, targetKey);
            reason = null;
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

        // ── place / size ─────────────────────────────────────────────

        private static bool ApplyPlace(
            StageState state, in StageCommand cmd, StageReducerTuning tuning,
            string screenPointName, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            // 인자: (대상, focus 토큰 = "face" 기본, [화면 지점], [시간]).
            if (!FocusPresetName.TryNormalizeToken(cmd.Arg(1, "face"), out string focusName))
            {
                reason = $"focus 프리셋 토큰 '{cmd.Arg(1)}'을 모른다";
                return false;
            }

            state.TryGetCharacter(slotKey, out string characterKey);

            if (!PlaceFocusStageReduction.TryReduce(
                    state, slotKey, characterKey, focusName, screenPointName,
                    tuning.FocusTuning, out StageNodeClaim claim, out reason))
            {
                return false;
            }

            state.Apply(claim);
            return true;
        }

        private static bool ApplySize(
            StageState state, in StageCommand cmd, StageReducerTuning tuning,
            string depthPresetKey, string preserveFocusToken, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            state.TryGetCharacter(slotKey, out string characterKey);

            if (!SetDepthStageReduction.TryReduce(
                    state, slotKey, characterKey, depthPresetKey, preserveFocusToken,
                    tuning.DepthPresets, tuning.FocusTuning,
                    out StageNodeClaim depthYClaim, out StageNodeClaim depthScaleClaim,
                    out reason))
            {
                return false;
            }

            state.Apply(depthYClaim);
            state.Apply(depthScaleClaim);
            return true;
        }

        // ── 이동/스케일/회전 ─────────────────────────────────────────

        private static bool ApplyNudge(
            StageState state, in StageCommand cmd, StageReducerTuning tuning,
            float xSign, float ySign, string targetId, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            string unitToken = cmd.Arg(1);

            if (!UnitToken.TryParsePixels(unitToken, tuning.ReferenceStageWidth, out float pixels))
            {
                reason = $"거리 토큰을 읽지 못했다: '{unitToken}'";
                return false;
            }

            string nodeKey = StageState.NodeKeyOf(slotKey, targetId);

            state.Apply(MoveByReduction.Reduce(
                nodeKey,
                new MoveByReduction.Args(false, new Vec2(pixels * xSign, pixels * ySign)),
                state.Nodes.GetState(nodeKey).AnchoredPosition));

            return true;
        }

        private static bool ApplyMovePer(
            StageState state, in StageCommand cmd, StageReducerTuning tuning,
            float xSign, float ySign, string targetId, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            // 브리지 규약: 거리 = 1u × 프레임 수 (기본 1fr, 파싱 실패 폴백 8).
            if (!DurationToken.TryParseFrames(cmd.Arg(1, "1fr"), out float frames))
                frames = 8f;

            float pixels = UnitToken.UnitsToPixels(1f, tuning.ReferenceStageWidth) * frames;

            string nodeKey = StageState.NodeKeyOf(slotKey, targetId);

            state.Apply(MoveByReduction.Reduce(
                nodeKey,
                new MoveByReduction.Args(false, new Vec2(pixels * xSign, pixels * ySign)),
                state.Nodes.GetState(nodeKey).AnchoredPosition));

            return true;
        }

        private static bool ApplyMoveBy(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!UnitToken.TryParseSignedPixels(cmd.Arg(1, "0u"), tuning.ReferenceStageWidth, out float x) ||
                !UnitToken.TryParseSignedPixels(cmd.Arg(2, "0u"), tuning.ReferenceStageWidth, out float y))
            {
                reason = $"거리 토큰을 읽지 못했다: '{cmd.Arg(1)}', '{cmd.Arg(2)}'";
                return false;
            }

            string nodeKey = StageState.NodeKeyOf(slotKey, "CharSlot_Track");

            state.Apply(MoveByReduction.Reduce(
                nodeKey,
                new MoveByReduction.Args(false, new Vec2(x, y)),
                state.Nodes.GetState(nodeKey).AnchoredPosition));

            return true;
        }

        private static bool ApplyMoveReset(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            foreach (string targetId in new[] { "CharSlot_Track", "CharSlot_Track_Focus" })
            {
                string nodeKey = StageState.NodeKeyOf(slotKey, targetId);

                state.Apply(MoveByReduction.Reduce(
                    nodeKey,
                    new MoveByReduction.Args(true, Vec2.Zero),
                    state.Nodes.GetState(nodeKey).AnchoredPosition));
            }

            return true;
        }

        private static bool ApplyScaleBy(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!NumberToken.TryParseFloat(cmd.Arg(1), out float multiplier))
            {
                reason = $"배율을 읽지 못했다: '{cmd.Arg(1)}'";
                return false;
            }

            string nodeKey = StageState.NodeKeyOf(slotKey, "CharSlot_Scale");

            state.Apply(ScaleToReduction.Reduce(
                nodeKey,
                new ScaleToReduction.Args(true, new Vec2(multiplier, multiplier)),
                state.Nodes.GetState(nodeKey).LocalScale.XY));

            return true;
        }

        private static bool ApplyScaleReset(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            string nodeKey = StageState.NodeKeyOf(slotKey, "CharSlot_Scale");

            state.Apply(ScaleToReduction.Reduce(
                nodeKey,
                new ScaleToReduction.Args(false, Vec2.One),
                state.Nodes.GetState(nodeKey).LocalScale.XY));

            return true;
        }

        private static bool ApplyRotateBy(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!NumberToken.TryParseFloat(cmd.Arg(1), out float degree))
            {
                reason = $"각도를 읽지 못했다: '{cmd.Arg(1)}'";
                return false;
            }

            string nodeKey = StageState.NodeKeyOf(slotKey, "CharSlot_SwayPivot");

            state.Apply(RotateToReduction.Reduce(
                nodeKey,
                new RotateToReduction.Args(true, new Vec3(0f, 0f, degree)),
                state.Nodes.GetState(nodeKey).LocalEulerAngles));

            return true;
        }

        private static bool ApplyRotateReset(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            string nodeKey = StageState.NodeKeyOf(slotKey, "CharSlot_SwayPivot");

            state.Apply(RotateToReduction.Reduce(
                nodeKey,
                new RotateToReduction.Args(false, Vec3.Zero),
                state.Nodes.GetState(nodeKey).LocalEulerAngles));

            return true;
        }

        // ── shot ─────────────────────────────────────────────────────

        /// <summary>
        /// shot_focus_to (roleKey, focus="body", screenPoint="center", zoom=2.5, duration).
        /// 런타임과 같은 경로: "현재 카메라가 적용된" 측정 focus를 만들어
        /// ShotZoomFocusReduction에 넘긴다(내부에서 카메라를 벗겨 논리 좌표 복원).
        /// 폴드의 측정값 = 논리 focus × 현재 배율 + 현재 pan — 적용측 규약 그대로다.
        /// </summary>
        private static bool ApplyShotFocusTo(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!FocusPresetName.TryNormalizeToken(cmd.Arg(1, "body"), out string focusName))
            {
                reason = $"focus 프리셋 토큰 '{cmd.Arg(1)}'을 모른다";
                return false;
            }

            string screenPointName = cmd.Arg(2, "center");

            if (!ScreenPointRatios.TryResolve(state.Nodes.RootSpace.Size, screenPointName, out Vec2 desired))
            {
                reason = $"화면 지점 '{screenPointName}'을 모른다";
                return false;
            }

            if (!NumberToken.TryParseFloat(cmd.Arg(3, "2.5"), out float zoom))
            {
                reason = $"zoom을 읽지 못했다: '{cmd.Arg(3)}'";
                return false;
            }

            state.TryGetCharacter(slotKey, out string characterKey);
            Vec2 focusOffset = FocusOffsetMath.Resolve(tuning.FocusTuning, characterKey, focusName, Vec2.Zero);

            RectNodeState[] chain = state.Nodes.BuildChainTo(
                StageState.NodeKeyOf(slotKey, "CharacterPortrait_VisualOffset"));

            Vec2 logicalFocus = SettledFocusMath.FocusPointInRigSpace(
                chain, state.Nodes.RootSpace, focusOffset);

            float currentScale = ShotIntentMath.EvaluateCameraScale(state.Shot.Zoom);
            Vec2 measuredFocus = logicalFocus * currentScale + state.Shot.PanInRigSpace;

            state.Shot = ShotZoomFocusReduction.Reduce(state.Shot, zoom, measuredFocus, desired);
            reason = null;
            return true;
        }

        private static bool ApplyShotZoom(StageState state, in StageCommand cmd, out string reason)
        {
            if (!NumberToken.TryParseFloat(cmd.Arg(0, "1"), out float zoom))
            {
                reason = $"zoom을 읽지 못했다: '{cmd.Arg(0)}'";
                return false;
            }

            state.Shot = ShotZoomReduction.Reduce(state.Shot, zoom);
            reason = null;
            return true;
        }

        private static bool ApplyShotTo(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            if (!NumberToken.TryParseFloat(cmd.Arg(0, "1"), out float zoom) ||
                !UnitToken.TryParseSignedPixels(cmd.Arg(1, "2.5u"), tuning.ReferenceStageWidth, out float x) ||
                !UnitToken.TryParseSignedPixels(cmd.Arg(2, "0u"), tuning.ReferenceStageWidth, out float y))
            {
                reason = $"shot_to 인자를 읽지 못했다: {cmd}";
                return false;
            }

            state.Shot = ShotToReduction.Reduce(state.Shot, zoom, new Vec2(x, y));
            reason = null;
            return true;
        }

        private static bool ApplyShotTrack(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            if (!UnitToken.TryParseSignedPixels(cmd.Arg(0, "2.5u"), tuning.ReferenceStageWidth, out float x) ||
                !UnitToken.TryParseSignedPixels(cmd.Arg(1, "0u"), tuning.ReferenceStageWidth, out float y))
            {
                reason = $"shot_track 인자를 읽지 못했다: {cmd}";
                return false;
            }

            state.Shot = ShotTrackReduction.Reduce(state.Shot, new Vec2(x, y));
            reason = null;
            return true;
        }

        // ── 구조 ─────────────────────────────────────────────────────

        private static bool ApplyCharTo(
            StageState state, in StageCommand cmd, string stageKey, string layerKey, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            state.SetAttachment(slotKey, new SlotAttachment(stageKey, layerKey));
            return true;
        }

        // ── helper ───────────────────────────────────────────────────

        private static bool TryGetSlotKey(in StageCommand cmd, out string slotKey, out string reason)
        {
            slotKey = cmd.Arg(0);

            if (string.IsNullOrEmpty(slotKey))
            {
                reason = "슬롯 키 인자가 없다";
                return false;
            }

            reason = null;
            return true;
        }

        private static bool TryGetSpawnedSlot(
            StageState state, in StageCommand cmd, out string slotKey, out string reason)
        {
            if (!TryGetSlotKey(cmd, out string targetKey, out reason))
            {
                slotKey = null;
                return false;
            }

            // 별칭("@3")·캐릭터 키("parkeunseol")도 슬롯으로 푼다 (actor/cast 축).
            if (!state.TryResolveSlot(targetKey, out slotKey))
            {
                reason = $"대상 '{targetKey}'를 슬롯으로 풀 수 없다 (slot/cast/actor 선행 필요)";
                return false;
            }

            return true;
        }

        private static string[] Prefixed(string slotKey, string[] ids)
        {
            string[] keys = new string[ids.Length];

            for (int i = 0; i < ids.Length; i++)
                keys[i] = StageState.NodeKeyOf(slotKey, ids[i]);

            return keys;
        }

        private static void Require(StageReducerTuning tuning)
        {
            if (tuning == null)
                throw new ArgumentNullException(nameof(tuning));

            if (!(tuning.ReferenceStageWidth > 0f))
                throw new ArgumentException("tuning.ReferenceStageWidth가 유효하지 않다.", nameof(tuning));

            if (!(tuning.BaseResolution.X > 0f) || !(tuning.BaseResolution.Y > 0f))
                throw new ArgumentException("tuning.BaseResolution이 유효하지 않다.", nameof(tuning));
        }
    }
}