using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ked.Presentation.Core
{
    /// <summary>리듀서에 주입되는 게임별 값 (게임 데이터는 코드가 아니라 tuning 인자).</summary>
    public sealed class StageReducerTuning
    {
        /// <summary>ExportedTuning/rig-schemas.json. slot 폴드가 캐릭터 리그를 여기서 세운다.</summary>
        public RigSchemasFileDto RigSchemas;

        /// <summary>기준 해상도 가로 폭. 1u 환산의 유일한 입력.</summary>
        public float ReferenceStageWidth;

        /// <summary>기준 해상도. 무대 루트 공간의 크기다.</summary>
        public Vec2 BaseResolution;

        /// <summary>
        /// presets/role-anchor.json — show/set_anchor의 캐릭터별 앵커.
        /// 없으면(null) show가 Default로 접되 Unhandled로 소리를 낸다 —
        /// 실측 덤프에 비기본값 엔트리가 있으므로(tyrant·Amber) 침묵하면 그 장면이 어긋난다.
        /// </summary>
        public RoleAnchorTuningBodyDto RoleAnchors;

        /// <summary>presets/depth.json — size 계열 폴드. 없으면 size는 Unhandled.</summary>
        public DepthPresetSetDto DepthPresets;

        /// <summary>presets/focus-tuning.json — place/size의 focus 오프셋. 없으면 base 0으로 접는다.</summary>
        public FocusTuningBodyDto FocusTuning;

        /// <summary>portrait-dimensions.json — 초상 사이징의 종횡비. 없으면 사이징은 Unhandled.</summary>
        public PortraitDimensionsFileDto PortraitDimensions;
    }

    /// <summary>
    /// 커맨드 열 → 확정 무대 상태.
    ///
    /// Apply(state, command, tuning) → state — 순수 함수다: 입력 상태를 바꾸지 않고
    /// 새 상태를 돌려준다. 시간·랜덤·IO 없음. duration 인자는 정지 프레임에 무의미하므로
    /// 파싱조차 하지 않는다.
    ///
    /// Unhandled 규율 — 네 갈래를 전부 잡는다. 조용히 버리지 않는다:
    ///   ① 모르는 커맨드   ② 잘못된 인자   ③ 미스폰 슬롯   ④ 폴드 중 예외
    /// 이 기록이 "아직 못 접는 것"의 유일한 진실이고, 수렴의 작업 목록이 된다.
    ///
    /// 현재 어휘(v1): slot 계열(리그 스폰) · cast/actor(배역·별칭) ·
    ///   show(리셋+앵커+사이징+가시성) · fade_in/out · place/size 계열 ·
    ///   nudge/move/scale/rotate 계열 · shot 5종 · char_to 계열 ·
    ///   초상 축(pose/face/face_swap) · 시간 커맨드(pause·Nfr — 접을 것이 없다).
    /// 명시적 한계: 배경/오버레이/이펙트/오디오/트랜지션 리그, 대사창,
    ///   절차적 연기 커맨드(목표값이 정의되지 않음).
    ///
    /// ⚠ v1 가정 — 등가성 하네스가 판정한다:
    /// 1. 스테이지/레이어 컨테이너는 항등 트랜스폼(스트레치 풀)이라 리그를 루트 직속으로
    ///    세워도 좌표가 같다. 컨테이너 스키마는 덤프에 없다 — 어긋나면 덤프에 추가한다.
    /// 2. 커맨드 기본값은 브리지 시그니처에서 옮겨 박았다(각 case의 주석 참조).
    ///    카탈로그가 데이터가 되면 tuning으로 옮긴다.
    ///
    /// 파일 구성 — 이 파일은 공개 API·디스패치·공용 helper만 갖는다.
    /// 폴드 본문은 디스패치의 구획을 그대로 따라 partial로 나뉜다:
    ///   StageReducer.Slot.cs      슬롯 스폰 · 배역/별칭 · 구조 축
    ///   StageReducer.Show.cs      show · fade
    ///   StageReducer.Placement.cs place · size
    ///   StageReducer.Staging.cs   nudge · move · scale · rotate
    ///   StageReducer.Shot.cs      shot 5종
    /// </summary>
    public static partial class StageReducer
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
                // 접다가 터진 커맨드도 침묵 대신 기록이다. 예외로 폴드가 멈추면
                // 그 뒤 라인을 하나도 판정할 수 없다. 상태는 클론이라 원본은 무사하다.
                next.AddUnhandled(command, $"폴드 중 예외: {ex.Message}");
            }

            return next;
        }

        public static StageState ApplyAll(
            StageState state, IEnumerable<StageCommand> commands, StageReducerTuning tuning)
        {
            if (commands == null)
                throw new ArgumentNullException(nameof(commands));

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
                // 슬롯 스폰. 브리지: slot(slotKey, stage="stage00", layer="mid") / slotNN(slotKey, layer="mid")
                case "slot":   return ApplySlot(state, cmd, tuning, cmd.Arg(1, "stage00"), cmd.Arg(2, "mid"), out reason);
                case "slot00": return ApplySlot(state, cmd, tuning, "stage00", cmd.Arg(1, "mid"), out reason);
                case "slot01": return ApplySlot(state, cmd, tuning, "stage01", cmd.Arg(1, "mid"), out reason);
                case "slot02": return ApplySlot(state, cmd, tuning, "stage02", cmd.Arg(1, "mid"), out reason);

                // 배역·별칭 — 커맨드 대상 해석(TryResolveSlot)의 전제.
                // 브리지: cast(slot, character, variant="a", emotion="1") — 뒤 둘은 초상 축이다.
                case "cast": return ApplyCast(state, cmd, tuning, out reason);
                case "actor": return ApplyActor(state, cmd, out reason);

                // 초상 축. 브리지: pose(target, variant) / face(target, emotion) /
                //                  face_swap(target, emotion, duration)
                case "pose": return ApplyPose(state, cmd, out reason);
                case "face": return ApplyFace(state, cmd, tuning, out reason);
                case "face_swap": return ApplyFace(state, cmd, tuning, out reason);

                // show — SetAnchor(리셋+역할 앵커) + 초상 사이징 + 가시성.
                case "show": return ApplyShow(state, cmd, tuning, out reason);

                // fade — 브리지와 같은 표적(CharacterPortraitSprite_Root).
                case "fade_in": return ApplyFade(state, cmd, visible: true, out reason);
                case "fade_out": return ApplyFade(state, cmd, visible: false, out reason);

                // place 계열 — focus 지점을 화면 지점으로 (SettledFocusMath.SolveFocusPlacement).
                // 브리지: place(role, focus="bust", screenPoint="center") /
                //         place_*(role, focus="face") — 접미사가 화면 지점을 정한다.
                case "place": return ApplyPlace(state, cmd, tuning, cmd.Arg(2, "center"), "bust", out reason);
                case "place_left": return ApplyPlace(state, cmd, tuning, "left", "face", out reason);
                case "place_center": return ApplyPlace(state, cmd, tuning, "center", "face", out reason);
                case "place_right": return ApplyPlace(state, cmd, tuning, "right", "face", out reason);
                case "place_top": return ApplyPlace(state, cmd, tuning, "top", "face", out reason);
                case "place_bottom": return ApplyPlace(state, cmd, tuning, "bottom", "face", out reason);
                case "place_tl": return ApplyPlace(state, cmd, tuning, "tl", "face", out reason);
                case "place_tr": return ApplyPlace(state, cmd, tuning, "tr", "face", out reason);
                case "place_bl": return ApplyPlace(state, cmd, tuning, "bl", "face", out reason);
                case "place_br": return ApplyPlace(state, cmd, tuning, "br", "face", out reason);
                case "place_inner_tl": return ApplyPlace(state, cmd, tuning, "inner_tl", "face", out reason);
                case "place_inner_tr": return ApplyPlace(state, cmd, tuning, "inner_tr", "face", out reason);
                case "place_inner_bl": return ApplyPlace(state, cmd, tuning, "inner_bl", "face", out reason);
                case "place_inner_br": return ApplyPlace(state, cmd, tuning, "inner_br", "face", out reason);

                // size 계열 — depth 프리셋 (focus 보존 보정 포함).
                // 브리지: size(role, depthArg, preserveFocus="bust") / size_*(role, preserveFocus="bust").
                case "size": return ApplySize(state, cmd, tuning, cmd.Arg(1), cmd.Arg(2, "bust"), out reason);
                case "size_far": return ApplySize(state, cmd, tuning, "far", cmd.Arg(1, "bust"), out reason);
                case "size_back": return ApplySize(state, cmd, tuning, "back", cmd.Arg(1, "bust"), out reason);
                case "size_mid": return ApplySize(state, cmd, tuning, "mid", cmd.Arg(1, "bust"), out reason);
                case "size_front": return ApplySize(state, cmd, tuning, "front", cmd.Arg(1, "bust"), out reason);
                case "size_close": return ApplySize(state, cmd, tuning, "close", cmd.Arg(1, "bust"), out reason);
                case "size_reset": return ApplySize(state, cmd, tuning, "mid", cmd.Arg(1, "bust"), out reason);

                // nudge (unitToken 필수 — 브리지에 기본값이 없다)
                case "left":  return ApplyNudge(state, cmd, tuning, -1f, 0f, "CharSlot_Track_X", out reason);
                case "right": return ApplyNudge(state, cmd, tuning, 1f, 0f, "CharSlot_Track_X", out reason);
                case "up":    return ApplyNudge(state, cmd, tuning, 0f, 1f, "CharSlot_Track_Y", out reason);
                case "down":  return ApplyNudge(state, cmd, tuning, 0f, -1f, "CharSlot_Track_Y", out reason);

                // move-per-frame (거리 = 1u × 프레임 수. 브리지: ParseFrames(token, 폴백 8), 기본 "1fr")
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
                case "shot_zoom":  return ApplyShotZoom(state, cmd, out reason);
                case "shot_to":    return ApplyShotTo(state, cmd, tuning, out reason);
                case "shot_track": return ApplyShotTrack(state, cmd, tuning, out reason);
                case "shot_reset":
                    state.Shot = ShotResetReduction.Reduce();
                    return true;

                case "shot_focus_to": return ApplyShotFocusTo(state, cmd, tuning, out reason);

                // 구조 축 (v1: 부착 기록만 — 컨테이너 항등 가정으로 좌표 영향 없음)
                case "char_to":    return ApplyCharTo(state, cmd, cmd.Arg(1, "stage00"), cmd.Arg(2, "mid"), out reason);
                case "char_to_s0": return ApplyCharTo(state, cmd, "stage00", cmd.Arg(1, "mid"), out reason);
                case "char_to_s1": return ApplyCharTo(state, cmd, "stage01", cmd.Arg(1, "mid"), out reason);
                case "char_to_s2": return ApplyCharTo(state, cmd, "stage02", cmd.Arg(1, "mid"), out reason);

                default:
                    // 시간만 쓰는 커맨드는 못 접는 게 아니라 접을 것이 없다 — 아래 참조.
                    if (IsWaitCommand(cmd.Name))
                        return true;

                    reason = "아직 코어로 이관되지 않은 커맨드";
                    return false;
            }
        }

        // ── helper ───────────────────────────────────────────────────

        /// <summary>브리지가 등록하는 프레임 대기 별칭의 상한. `&lt;&lt;48fr&gt;&gt;` = 2초.</summary>
        private const int MaxFrameWaitAlias = 48;

        /// <summary>
        /// 시간만 쓰는 커맨드인가 — `&lt;&lt;pause 초&gt;&gt;`와 프레임 별칭 `&lt;&lt;1fr&gt;&gt;`~`&lt;&lt;48fr&gt;&gt;`
        /// (브리지 BindFramePauseAliases, 24fps 기준).
        ///
        /// 정지 프레임에 목표가 **없는 게 아니라 접을 것이 없다** — 무대 상태를 하나도
        /// 건드리지 않는다. duration 인자를 파싱조차 하지 않는 것과 같은 규율이라
        /// Unhandled로 남기지 않는다: 남기면 "아직 이관 안 됨" 작업 목록이 거짓말이 된다.
        /// </summary>
        private static bool IsWaitCommand(string name)
        {
            if (name == "pause")
                return true;

            if (name == null || name.Length < 3 || !name.EndsWith("fr", StringComparison.Ordinal))
                return false;

            return int.TryParse(
                       name.Substring(0, name.Length - 2),
                       NumberStyles.None,
                       CultureInfo.InvariantCulture,
                       out int frames)
                   && frames >= 1
                   && frames <= MaxFrameWaitAlias;
        }

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

            reason = null;
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
