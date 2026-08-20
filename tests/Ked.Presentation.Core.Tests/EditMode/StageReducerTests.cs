using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// 리듀서 폴드 골든.
    ///
    /// 리그는 실제 캐릭터 스키마와 같은 부모 사슬의 합성 24노드다 — 실제 덤프와의
    /// 대조는 UnityParity·등가성 하네스의 일이고, 여기서는 폴드 규율을 고정한다.
    /// 기본값·표적은 전부 브리지 실측에서 왔다(각 테스트 주석).
    /// </summary>
    public sealed class StageReducerTests
    {
        private const float Eps = 1e-3f;

        // 1920 / 48 = 40px. 1u 환산 검증의 기준.
        private const float PixelsPerUnit = 40f;

        // ── 합성 리그 (실제 스키마의 부모 사슬 그대로) ───────────────

        private static Float2Dto F2(float x, float y) => new() { x = x, y = y };
        private static Float3Dto F3(float x, float y, float z) => new() { x = x, y = y, z = z };

        private static RigSchemaNodeDto Node(
            string id, string parent, bool bottomPivot = false, float? canvasGroupAlpha = null)
        {
            return new RigSchemaNodeDto
            {
                id = id,
                parent = parent,
                anchoredPosition = F2(0f, 0f),
                anchorMin = F2(0f, 0f),
                anchorMax = F2(1f, 1f),
                pivot = bottomPivot ? F2(0.5f, 0f) : F2(0.5f, 0.5f),
                sizeDelta = F2(0f, 0f),
                localScale = F3(1f, 1f, 1f),
                localEulerAngles = F3(0f, 0f, 0f),
                measuredRectSize = F2(0f, 0f),
                hasCanvasGroup = canvasGroupAlpha.HasValue,
                canvasGroupAlpha = canvasGroupAlpha ?? 1f,
            };
        }

        private static RigSchemaRigDto CharacterRig()
        {
            return new RigSchemaRigDto
            {
                rigKind = "character",
                sourcePrefab = "",
                nodes = new List<RigSchemaNodeDto>
                {
                    Node("__root", ""),
                    Node("CharSlot_Track_Focus", "__root"),
                    Node("CharSlot_DepthY", "CharSlot_Track_Focus"),
                    Node("CharSlot_DepthScale", "CharSlot_DepthY", bottomPivot: true),
                    Node("CharSlot_Track_Idle", "CharSlot_DepthScale"),
                    Node("CharSlot_Track", "CharSlot_Track_Idle"),
                    Node("CharSlot_Track_X", "CharSlot_Track"),
                    Node("CharSlot_Track_Y", "CharSlot_Track_X"),
                    Node("CharSlot_Rotation", "CharSlot_Track_Y"),
                    Node("CharSlot_SwayPivot", "CharSlot_Rotation", bottomPivot: true),
                    Node("CharSlot_Scale", "CharSlot_SwayPivot", bottomPivot: true),
                    Node("CharacterPortrait_VisualOffset", "CharSlot_Scale", bottomPivot: true),
                    Node("CharacterPortrait_Track", "CharacterPortrait_VisualOffset"),
                    Node("CharacterPortrait_Rotation", "CharacterPortrait_Track"),
                    Node("CharacterPortrait_Track_Move", "CharacterPortrait_Rotation"),
                    Node("CharacterPortrait_Track_Move_X", "CharacterPortrait_Track_Move"),
                    Node("CharacterPortrait_Track_Move_Y", "CharacterPortrait_Track_Move_X"),
                    Node("CharacterPortrait_SwayPivot", "CharacterPortrait_Track_Move_Y", bottomPivot: true),
                    Node("CharacterPortrait_Shake", "CharacterPortrait_SwayPivot"),
                    Node("CharacterPortrait_ActingScale", "CharacterPortrait_Shake"),
                    Node("CharacterPortrait_ActingScale_X", "CharacterPortrait_ActingScale"),
                    Node("CharacterPortrait_ActingScale_Y", "CharacterPortrait_ActingScale_X"),
                    // 실제 스키마: NeedsCanvasGroup + InitialCanvasGroupAlpha = 0.
                    Node("CharacterPortraitSprite_Root", "CharacterPortrait_ActingScale_Y", canvasGroupAlpha: 0f),
                    Node("CharacterPortraitSprite_Image", "CharacterPortraitSprite_Root"),
                    Node("EmojiSlot00_Root", "__root", canvasGroupAlpha: 0f),
                },
            };
        }

        private const int RigNodeCount = 25;

        private static StageReducerTuning NewTuning()
        {
            return new StageReducerTuning
            {
                RigSchemas = new RigSchemasFileDto
                {
                    capturedUnderParentSize = F2(1920f, 1080f),
                    rigs = new List<RigSchemaRigDto> { CharacterRig() },
                },
                ReferenceStageWidth = 1920f,
                BaseResolution = new Vec2(1920f, 1080f),
                RoleAnchors = new RoleAnchorTuningBodyDto
                {
                    entries = new List<RoleAnchorEntryDto>
                    {
                        // 실제 덤프의 비기본값 엔트리.
                        new() { key = "tyrant", offset = F2(-30f, -800f), visualScale = 5f },
                    },
                },
                PortraitDimensions = NewPortraitDimensions(),
            };
        }

        /// <summary>
        /// 높이를 전부 1080(= 부모 rect 높이)으로 둔다 — 그러면 접힌 폭이 곧 여기 적은 width다.
        /// tyrant는 변형·표정마다 폭이 다르다: **종횡비가 캐릭터당 하나라는 가정을 깨는 자리**다.
        /// </summary>
        private static PortraitDimensionsFileDto NewPortraitDimensions()
        {
            return new PortraitDimensionsFileDto
            {
                entries = new List<PortraitDimensionDto>
                {
                    new() { character = "tyrant", variant = "a", emotion = "01", width = 620f, height = 1080f },
                    new() { character = "tyrant", variant = "a", emotion = "02", width = 540f, height = 1080f },
                    new() { character = "tyrant", variant = "b", emotion = "01", width = 810f, height = 1080f },
                    new() { character = "parkeunseol", variant = "a", emotion = "01", width = 432f, height = 1080f },
                },
            };
        }

        private static float PortraitWidthOf(StageState state, string slotKey)
            => state.Nodes.GetState(StageState.NodeKeyOf(slotKey, "CharacterPortraitSprite_Image")).SizeDelta.X;

        private static StageCommand Cmd(string name, params string[] args)
            => new(name, args, "test.yarn:1");

        private static StageState Fold(StageReducerTuning tuning, params StageCommand[] commands)
            => StageReducer.ApplyAll(StageReducer.CreateInitialState(tuning), commands, tuning);

        private static StageState Fold(params StageCommand[] commands)
            => Fold(NewTuning(), commands);

        // ── 스폰 ─────────────────────────────────────────────────────

        [Test]
        public void slot은_리그를_prefix_키로_세우고_부착을_기록한다()
        {
            StageState state = Fold(Cmd("slot", "c1"));

            Assert.That(state.HasSlot("c1"), Is.True);
            Assert.That(state.Nodes.Count, Is.EqualTo(RigNodeCount));
            Assert.That(state.Nodes.Contains("c1/__root"), Is.True);
            Assert.That(state.Nodes.GetParentKey("c1/CharSlot_Track"), Is.EqualTo("c1/CharSlot_Track_Idle"));

            // 브리지 기본값: stage00 / mid.
            Assert.That(state.TryGetAttachment("c1", out SlotAttachment attachment), Is.True);
            Assert.That(attachment.StageKey, Is.EqualTo("stage00"));
            Assert.That(attachment.LayerKey, Is.EqualTo("mid"));

            Assert.That(state.Unhandled, Is.Empty);
        }

        [Test]
        public void slot01은_stage01에_붙는다()
        {
            StageState state = Fold(Cmd("slot01", "c1", "front"));

            state.TryGetAttachment("c1", out SlotAttachment attachment);
            Assert.That(attachment.StageKey, Is.EqualTo("stage01"));
            Assert.That(attachment.LayerKey, Is.EqualTo("front"));
        }

        [Test]
        public void 같은_슬롯_재선언은_부착만_갱신한다()
        {
            // 런타임은 기존 리그를 재사용한다 — 노드가 늘면 안 된다.
            StageState state = Fold(
                Cmd("slot", "c1"),
                Cmd("left", "c1", "2u"),
                Cmd("slot", "c1", "stage02", "back"));

            Assert.That(state.Nodes.Count, Is.EqualTo(RigNodeCount));

            state.TryGetAttachment("c1", out SlotAttachment attachment);
            Assert.That(attachment.StageKey, Is.EqualTo("stage02"));

            // 좌표도 유지 — 재선언이 리셋이 아니다.
            Assert.That(state.Nodes.GetState("c1/CharSlot_Track_X").AnchoredPosition.X,
                Is.EqualTo(-2f * PixelsPerUnit).Within(Eps));
        }

        [Test]
        public void 슬롯_둘은_키가_섞이지_않는다()
        {
            StageState state = Fold(
                Cmd("slot", "c1"),
                Cmd("slot", "c2"),
                Cmd("left", "c1", "1u"));

            Assert.That(state.Nodes.Count, Is.EqualTo(RigNodeCount * 2));
            Assert.That(state.Nodes.GetState("c1/CharSlot_Track_X").AnchoredPosition.X,
                Is.EqualTo(-PixelsPerUnit).Within(Eps));
            Assert.That(state.Nodes.GetState("c2/CharSlot_Track_X").AnchoredPosition.X,
                Is.EqualTo(0f).Within(Eps));
        }

        // ── 순수성 ───────────────────────────────────────────────────

        [Test]
        public void Apply는_입력_상태를_바꾸지_않는다()
        {
            StageReducerTuning tuning = NewTuning();
            StageState before = Fold(tuning, Cmd("slot", "c1"));

            Vec2 positionBefore = before.Nodes.GetState("c1/CharSlot_Track").AnchoredPosition;
            int unhandledBefore = before.Unhandled.Count;

            StageState after = StageReducer.Apply(before, Cmd("move_by", "c1", "3u", "0u"), tuning);

            Assert.That(before.Nodes.GetState("c1/CharSlot_Track").AnchoredPosition, Is.EqualTo(positionBefore));
            Assert.That(before.Unhandled.Count, Is.EqualTo(unhandledBefore));
            Assert.That(after.Nodes.GetState("c1/CharSlot_Track").AnchoredPosition.X,
                Is.EqualTo(3f * PixelsPerUnit).Within(Eps));
        }

        // ── 배역·별칭 ────────────────────────────────────────────────

        [Test]
        public void cast와_actor를_거치면_별칭으로_커맨드가_풀린다()
        {
            // 브리지: cast(slot, characterKey, variant="a", emotion="1") / actor(alias, target).
            StageState state = Fold(
                Cmd("slot", "c1"),
                Cmd("cast", "c1", "parkeunseol"),
                Cmd("actor", "@3", "parkeunseol"),
                Cmd("left", "@3", "2u"));

            Assert.That(state.Nodes.GetState("c1/CharSlot_Track_X").AnchoredPosition.X,
                Is.EqualTo(-2f * PixelsPerUnit).Within(Eps));

            // 캐릭터 키로도 풀린다.
            StageState byCharacter = StageReducer.Apply(
                state, Cmd("right", "parkeunseol", "1u"), NewTuning());

            Assert.That(byCharacter.Nodes.GetState("c1/CharSlot_Track_X").AnchoredPosition.X,
                Is.EqualTo(-PixelsPerUnit).Within(Eps));
        }

        [Test]
        public void cast는_배역과_변형을_앉히고_초상_폭까지_접는다()
        {
            // 브리지 팬아웃: 배역 → pose(variant="a") → face(emotion="1" → "01").
            StageState state = Fold(Cmd("slot", "c1"), Cmd("cast", "c1", "tyrant"));

            Assert.That(state.TryGetCharacter("c1", out string character), Is.True);
            Assert.That(character, Is.EqualTo("tyrant"));
            Assert.That(state.GetVariant("c1"), Is.EqualTo("a"));

            Assert.That(PortraitWidthOf(state, "c1"), Is.EqualTo(620f).Within(Eps));
            Assert.That(state.Unhandled, Is.Empty);
        }

        [Test]
        public void cast의_변형_표정_인자가_초상_폭을_고른다()
        {
            StageState state = Fold(
                Cmd("slot", "c1"),
                Cmd("cast", "c1", "tyrant", "b", "1"));

            Assert.That(state.GetVariant("c1"), Is.EqualTo("b"));
            Assert.That(PortraitWidthOf(state, "c1"), Is.EqualTo(810f).Within(Eps));
        }

        [Test]
        public void 초상_치수가_없으면_배역은_접히고_사이징만_기록된다()
        {
            StageReducerTuning tuning = NewTuning();
            tuning.PortraitDimensions = null;

            StageState state = Fold(tuning, Cmd("slot", "c1"), Cmd("cast", "c1", "tyrant"));

            Assert.That(state.TryGetCharacter("c1", out _), Is.True, "배역은 접혔다");
            Assert.That(state.Unhandled.Count, Is.EqualTo(1));
            Assert.That(state.Unhandled[0].Reason, Does.Contain("초상 치수"));
        }

        // ── 초상 축 (변형 · 표정 · 사이징) ───────────────────────────

        [Test]
        public void face는_표정마다_폭을_다시_접는다()
        {
            // 브리지: face(target, emotion) — 표정은 상태가 아니라 인자다.
            StageState state = Fold(
                Cmd("slot", "c1"),
                Cmd("cast", "c1", "tyrant"),
                Cmd("face", "c1", "2"));

            Assert.That(PortraitWidthOf(state, "c1"), Is.EqualTo(540f).Within(Eps));

            StageState back = StageReducer.Apply(state, Cmd("face", "c1", "1"), NewTuning());
            Assert.That(PortraitWidthOf(back, "c1"), Is.EqualTo(620f).Within(Eps));
        }

        [Test]
        public void face_swap도_같은_사이징을_탄다()
        {
            StageState state = Fold(
                Cmd("slot", "c1"),
                Cmd("cast", "c1", "tyrant"),
                Cmd("face_swap", "c1", "2", "8fr"));

            Assert.That(PortraitWidthOf(state, "c1"), Is.EqualTo(540f).Within(Eps));
            Assert.That(state.Unhandled, Is.Empty);
        }

        [Test]
        public void pose는_변형만_갱신하고_폭은_다음_표정_커맨드가_바꾼다()
        {
            // 런타임 실동작: SetPortraitPoseCommandCharR의 스프라이트 교체가 비활성이라
            // pose 시점에는 sizeDelta가 그대로다.
            StageState posed = Fold(
                Cmd("slot", "c1"),
                Cmd("cast", "c1", "tyrant"),
                Cmd("pose", "c1", "b"));

            Assert.That(posed.GetVariant("c1"), Is.EqualTo("b"));
            Assert.That(PortraitWidthOf(posed, "c1"), Is.EqualTo(620f).Within(Eps),
                "pose는 폭을 건드리지 않는다");

            StageState faced = StageReducer.Apply(posed, Cmd("face", "c1", "1"), NewTuning());
            Assert.That(PortraitWidthOf(faced, "c1"), Is.EqualTo(810f).Within(Eps),
                "다음 표정 커맨드가 새 변형으로 다시 고른다");
        }

        [Test]
        public void 재배역은_변형을_비운다()
        {
            // 런타임 CastRegistry.CastCharRig이 새 바인딩을 빈 변형으로 만든다.
            StageState state = Fold(
                Cmd("slot", "c1"),
                Cmd("cast", "c1", "tyrant", "b", "1"),
                Cmd("cast", "c1", "parkeunseol"));

            Assert.That(state.GetVariant("c1"), Is.EqualTo("a"));
            Assert.That(PortraitWidthOf(state, "c1"), Is.EqualTo(432f).Within(Eps));
        }

        [Test]
        public void 없는_표정은_기본_변형_01로_물러선다()
        {
            // PortraitResolver의 폴백 규약: (캐릭터, 'a', "01").
            StageState state = Fold(
                Cmd("slot", "c1"),
                Cmd("cast", "c1", "tyrant", "b", "9"));

            Assert.That(PortraitWidthOf(state, "c1"), Is.EqualTo(620f).Within(Eps));
            Assert.That(state.Unhandled, Is.Empty);
        }

        [Test]
        public void 치수에_없는_캐릭터는_사이징이_Unhandled다()
        {
            StageState state = Fold(Cmd("slot", "c1"), Cmd("cast", "c1", "amber"));

            Assert.That(state.Unhandled.Count, Is.EqualTo(1));
            Assert.That(state.Unhandled[0].Reason, Does.Contain("초상 치수가 없다"));
        }

        [Test]
        public void 배역이_없으면_face는_Unhandled다()
        {
            StageState state = Fold(Cmd("slot", "c1"), Cmd("face", "c1", "2"));

            Assert.That(state.Unhandled.Count, Is.EqualTo(1));
            Assert.That(state.Unhandled[0].Reason, Does.Contain("배역이 없다"));
        }

        // ── 이동 계열 (1u = 기준 폭 / 48) ────────────────────────────

        [Test]
        public void nudge는_1u를_기준_폭에서_파생한다()
        {
            // 브리지: left/right → Track_X, up/down → Track_Y. unitToken은 필수.
            StageState state = Fold(
                Cmd("slot", "c1"),
                Cmd("left", "c1", "2u"),
                Cmd("up", "c1", "1.5u"));

            Assert.That(state.Nodes.GetState("c1/CharSlot_Track_X").AnchoredPosition.X,
                Is.EqualTo(-80f).Within(Eps));
            Assert.That(state.Nodes.GetState("c1/CharSlot_Track_Y").AnchoredPosition.Y,
                Is.EqualTo(60f).Within(Eps));
        }

        [Test]
        public void move_per는_프레임_수에_1u를_곱하고_실패하면_8프레임이다()
        {
            // 브리지: ParseFrames(frameToken, 폴백 8f), 기본 "1fr".
            StageState threeFrames = Fold(
                Cmd("slot", "c1"), Cmd("right_per", "c1", "3fr"));

            Assert.That(threeFrames.Nodes.GetState("c1/CharSlot_Track_X").AnchoredPosition.X,
                Is.EqualTo(3f * PixelsPerUnit).Within(Eps));

            StageState fallback = Fold(
                Cmd("slot", "c1"), Cmd("down_per", "c1", "이상한토큰"));

            Assert.That(fallback.Nodes.GetState("c1/CharSlot_Track_Y").AnchoredPosition.Y,
                Is.EqualTo(-8f * PixelsPerUnit).Within(Eps));
            Assert.That(fallback.Unhandled, Is.Empty, "폴백은 브리지 규약이지 실패가 아니다");
        }

        [Test]
        public void move_by는_상대_move_reset은_두_노드_절대_0이다()
        {
            // 브리지: move_by(slot, x="0u", y="0u") 상대 / move_reset은 Track과 Track_Focus에 절대 0.
            StageState moved = Fold(
                Cmd("slot", "c1"),
                Cmd("move_by", "c1", "1u", "-2u"),
                Cmd("move_by", "c1", "1u", "0u"));

            Assert.That(moved.Nodes.GetState("c1/CharSlot_Track").AnchoredPosition,
                Is.EqualTo(new Vec2(80f, -80f)));

            StageState reset = StageReducer.Apply(moved, Cmd("move_reset", "c1"), NewTuning());

            Assert.That(reset.Nodes.GetState("c1/CharSlot_Track").AnchoredPosition, Is.EqualTo(Vec2.Zero));
            Assert.That(reset.Nodes.GetState("c1/CharSlot_Track_Focus").AnchoredPosition, Is.EqualTo(Vec2.Zero));
        }

        [Test]
        public void scale_by는_상대_곱_rotate_by는_상대_z_가산이다()
        {
            StageState state = Fold(
                Cmd("slot", "c1"),
                Cmd("scale_by", "c1", "1.5"),
                Cmd("scale_by", "c1", "2"),
                Cmd("rotate_by", "c1", "15"),
                Cmd("rotate_by", "c1", "-40"));

            Assert.That(state.Nodes.GetState("c1/CharSlot_Scale").LocalScale.XY,
                Is.EqualTo(new Vec2(3f, 3f)));
            Assert.That(state.Nodes.GetState("c1/CharSlot_SwayPivot").LocalEulerAngles.Z,
                Is.EqualTo(-25f).Within(Eps));

            StageState reset = StageReducer.ApplyAll(
                state,
                new[] { Cmd("scale_reset", "c1"), Cmd("rotate_reset", "c1") },
                NewTuning());

            Assert.That(reset.Nodes.GetState("c1/CharSlot_Scale").LocalScale.XY, Is.EqualTo(Vec2.One));
            Assert.That(reset.Nodes.GetState("c1/CharSlot_SwayPivot").LocalEulerAngles, Is.EqualTo(Vec3.Zero));
        }

        // ── show ─────────────────────────────────────────────────────

        [Test]
        public void show는_연기_축을_리셋하고_배역_앵커를_입힌다()
        {
            StageState state = Fold(
                Cmd("slot", "c1"),
                Cmd("cast", "c1", "tyrant"),
                Cmd("move_by", "c1", "3u", "0u"),
                Cmd("rotate_by", "c1", "20"),   // SwayPivot — show가 되돌리지 않는 축
                Cmd("scale_by", "c1", "2"),
                Cmd("show", "c1"));

            // 리셋 축.
            Assert.That(state.Nodes.GetState("c1/CharSlot_Track").AnchoredPosition, Is.EqualTo(Vec2.Zero));
            Assert.That(state.Nodes.GetState("c1/CharSlot_Scale").LocalScale.XY, Is.EqualTo(Vec2.One));
            Assert.That(state.Nodes.GetState("c1/CharSlot_Rotation").LocalEulerAngles, Is.EqualTo(Vec3.Zero));

            // 런타임 실동작: rotate_by의 표적(CharSlot_SwayPivot)은 show가 되돌리지 않는다.
            Assert.That(state.Nodes.GetState("c1/CharSlot_SwayPivot").LocalEulerAngles.Z,
                Is.EqualTo(20f).Within(Eps));

            // 배역 앵커: tyrant = offset (-30, -800), scale 5 (실제 덤프의 비기본값 엔트리).
            RectNodeState anchor = state.Nodes.GetState("c1/CharacterPortrait_VisualOffset");
            Assert.That(anchor.AnchoredPosition, Is.EqualTo(new Vec2(-30f, -800f)));
            Assert.That(anchor.LocalScale.XY, Is.EqualTo(new Vec2(5f, 5f)));

            // 가시성 두 장.
            Assert.That(state.GetAlpha("c1/__root"), Is.EqualTo(1f));
            Assert.That(state.GetAlpha("c1/CharacterPortraitSprite_Root"), Is.EqualTo(1f));

            // 초상 폭: faceToken 생략 → "e1" → 표정 "1" → "01".
            Assert.That(PortraitWidthOf(state, "c1"), Is.EqualTo(620f).Within(Eps));
            Assert.That(state.Unhandled, Is.Empty);
        }

        [Test]
        public void show의_faceToken은_별칭을_벗는다()
        {
            StageState alias = Fold(
                Cmd("slot", "c1"), Cmd("cast", "c1", "tyrant"), Cmd("show", "c1", "emo2"));

            Assert.That(PortraitWidthOf(alias, "c1"), Is.EqualTo(540f).Within(Eps));

            StageState shortForm = Fold(
                Cmd("slot", "c1"), Cmd("cast", "c1", "tyrant"), Cmd("show", "c1", "e2"));

            Assert.That(PortraitWidthOf(shortForm, "c1"), Is.EqualTo(540f).Within(Eps));
        }

        [Test]
        public void 배역이_없으면_show_앵커는_기본값이고_사이징만_기록된다()
        {
            StageState state = Fold(Cmd("slot", "c1"), Cmd("show", "c1"));

            RectNodeState anchor = state.Nodes.GetState("c1/CharacterPortrait_VisualOffset");
            Assert.That(anchor.AnchoredPosition, Is.EqualTo(Vec2.Zero));
            Assert.That(anchor.LocalScale.XY, Is.EqualTo(Vec2.One));

            Assert.That(state.GetAlpha("c1/CharacterPortraitSprite_Root"), Is.EqualTo(1f),
                "사이징이 빠져도 가시성은 접힌다");
            Assert.That(state.Unhandled.Count, Is.EqualTo(1));
            Assert.That(state.Unhandled[0].Reason, Does.Contain("배역이 없다"));
        }

        [Test]
        public void role_anchor_튜닝이_없으면_show가_소리를_낸다()
        {
            StageReducerTuning tuning = NewTuning();
            tuning.RoleAnchors = null;

            StageState state = Fold(tuning, Cmd("slot", "c1"), Cmd("show", "c1"));

            Assert.That(state.Unhandled.Any(u => u.Reason.Contains("role-anchor")), Is.True,
                "실측 덤프에 비기본값 엔트리(tyrant·Amber)가 있다 — 침묵하면 그 장면이 어긋난다");
        }

        // ── 초기 가시성 (첫 리포트의 최대 불일치 클래스) ────────────

        [Test]
        public void 스폰은_덤프의_초기_alpha를_반영한다()
        {
            // 실제 리그에서 초상·오버레이·이모지 루트는 alpha 0으로 태어난다.
            // 기본값 1로 두면 show 전 구간이 전부 어긋난다.
            StageState state = Fold(Cmd("slot", "c1"));

            Assert.That(state.GetAlpha("c1/CharacterPortraitSprite_Root"), Is.EqualTo(0f));
            Assert.That(state.GetAlpha("c1/EmojiSlot00_Root"), Is.EqualTo(0f));

            // CanvasGroup이 없는 노드는 기록하지 않는다 — 기본값 1이 곧 답이다.
            Assert.That(state.GetAlpha("c1/CharSlot_Track"), Is.EqualTo(1f));
            Assert.That(state.GetAlpha("c1/__root"), Is.EqualTo(1f));
        }

        [Test]
        public void hasCanvasGroup이_false면_alpha를_건드리지_않는다()
        {
            // 구덤프 호환: JsonUtility가 없는 필드를 0으로 채우므로 bool 게이트가 필요하다.
            // 게이트 없이 canvasGroupAlpha만 보면 전 노드가 alpha 0이 된다.
            StageReducerTuning tuning = NewTuning();

            foreach (RigSchemaNodeDto node in tuning.RigSchemas.rigs[0].nodes)
            {
                node.hasCanvasGroup = false;
                node.canvasGroupAlpha = 0f;   // 구덤프가 읽히는 모양
            }

            StageState state = Fold(tuning, Cmd("slot", "c1"));

            Assert.That(state.GetAlpha("c1/CharacterPortraitSprite_Root"), Is.EqualTo(1f),
                "게이트가 false면 기본값이 답이다");
        }

        [Test]
        public void show는_초기_alpha_0을_1로_올린다()
        {
            // 스폰 직후 0 → show가 1. 첫 리포트에서 Sprite_Root만 419건이었던 이유다
            // (show 이후로는 폴드도 캡처도 1이라 맞았다).
            StageState spawned = Fold(Cmd("slot", "c1"));
            Assert.That(spawned.GetAlpha("c1/CharacterPortraitSprite_Root"), Is.EqualTo(0f));

            StageState shown = StageReducer.Apply(spawned, Cmd("show", "c1"), NewTuning());
            Assert.That(shown.GetAlpha("c1/CharacterPortraitSprite_Root"), Is.EqualTo(1f));

            // show가 안 건드리는 이모지 루트는 0 그대로 — 캡처도 0이다.
            Assert.That(shown.GetAlpha("c1/EmojiSlot00_Root"), Is.EqualTo(0f));
        }

        [Test]
        public void fade는_초상_스프라이트_루트의_가시성이다()
        {
            // 브리지 표적: CharacterPortraitSprite_Root.
            StageState state = Fold(
                Cmd("slot", "c1"),
                Cmd("show", "c1"),
                Cmd("fade_out", "c1"));

            Assert.That(state.GetAlpha("c1/CharacterPortraitSprite_Root"), Is.EqualTo(0f));
            Assert.That(state.GetAlpha("c1/__root"), Is.EqualTo(1f), "리그 루트는 그대로");

            StageState back = StageReducer.Apply(state, Cmd("fade_in", "c1"), NewTuning());
            Assert.That(back.GetAlpha("c1/CharacterPortraitSprite_Root"), Is.EqualTo(1f));
        }

        // ── shot ─────────────────────────────────────────────────────

        [Test]
        public void shot_계열은_샷_축에_접힌다()
        {
            // 브리지 기본값: shot_zoom(zoom=1) / shot_to(zoom=1, x=2.5u, y=0u) / shot_track(x=2.5u, y=0u).
            StageState state = Fold(
                Cmd("shot_zoom", "3"),
                Cmd("shot_track", "1u", "-0.5u"),
                Cmd("shot_track", "1u", "0u"));

            Assert.That(state.Shot.Zoom, Is.EqualTo(3f));
            Assert.That(state.Shot.PanInRigSpace, Is.EqualTo(new Vec2(80f, -20f)));

            StageState to = StageReducer.Apply(state, Cmd("shot_to", "5", "-2u", "1u"), NewTuning());
            Assert.That(to.Shot.Zoom, Is.EqualTo(5f));
            Assert.That(to.Shot.PanInRigSpace, Is.EqualTo(new Vec2(-80f, 40f)));

            StageState reset = StageReducer.Apply(to, Cmd("shot_reset"), NewTuning());
            Assert.That(reset.Shot.Zoom, Is.EqualTo(0f));
            Assert.That(reset.Shot.PanInRigSpace, Is.EqualTo(Vec2.Zero));
        }

        [Test]
        public void shot_focus_to는_focus_튜닝_없이도_접힌다()
        {
            // 튜닝이 없으면 오프셋이 0일 뿐 폴드 자체는 성립한다.
            // (오프셋 합의 정확성은 FocusStageReductionTests가 본다.)
            StageState state = Fold(Cmd("slot", "c1"), Cmd("shot_focus_to", "c1", "body", "center", "2.5"));

            Assert.That(state.Unhandled, Is.Empty);
            Assert.That(state.Shot.Zoom, Is.EqualTo(2.5f));
        }

        // ── 시간 커맨드 (접을 것이 없다) ─────────────────────────────

        [TestCase("1fr")]
        [TestCase("12fr")]
        [TestCase("24fr")]
        [TestCase("48fr")]
        [TestCase("pause")]
        public void 시간_커맨드는_무해하게_접힌다(string name)
        {
            // 브리지 BindFramePauseAliases: <<1fr>>~<<48fr>>은 대기 별칭이다(24fps 기준).
            // 무대 상태를 하나도 건드리지 않으므로 Unhandled 작업 목록에 넣지 않는다.
            StageState state = Fold(Cmd("slot", "c1"), Cmd(name));

            Assert.That(state.Unhandled, Is.Empty);
        }

        [TestCase("0fr")]
        [TestCase("49fr")]
        [TestCase("fr")]
        [TestCase("-2fr")]
        [TestCase("2.5fr")]
        [TestCase("frame")]
        public void 대기_별칭이_아닌_이름은_그대로_Unhandled다(string name)
        {
            // 브리지가 등록하지 않는 이름이다 — 비슷하게 생겼다고 삼키지 않는다.
            StageState state = Fold(Cmd(name));

            Assert.That(state.Unhandled.Count, Is.EqualTo(1));
            Assert.That(state.Unhandled[0].Reason, Does.Contain("이관되지 않은"));
        }

        // ── Unhandled 규율 네 갈래 ───────────────────────────────────

        [Test]
        public void 모르는_커맨드는_출처와_함께_남는다()
        {
            StageState state = Fold(Cmd("bg_show", "room01"));

            Assert.That(state.Unhandled.Count, Is.EqualTo(1));
            Assert.That(state.Unhandled[0].Command.Name, Is.EqualTo("bg_show"));
            Assert.That(state.Unhandled[0].Command.Source, Is.EqualTo("test.yarn:1"));
            Assert.That(state.Unhandled[0].Reason, Does.Contain("이관되지 않은"));
        }

        [Test]
        public void 잘못된_인자는_이유와_함께_남는다()
        {
            StageState state = Fold(
                Cmd("slot", "c1"),
                Cmd("scale_by", "c1", "숫자아님"),
                Cmd("left", "c1"));   // unitToken 필수 (브리지에 기본값 없음)

            Assert.That(state.Unhandled.Count, Is.EqualTo(2));
            Assert.That(state.Unhandled[0].Reason, Does.Contain("배율"));
            Assert.That(state.Unhandled[1].Reason, Does.Contain("거리 토큰"));

            // 실패한 커맨드는 상태를 바꾸지 않는다.
            Assert.That(state.Nodes.GetState("c1/CharSlot_Scale").LocalScale.XY, Is.EqualTo(Vec2.One));
        }

        [Test]
        public void 미스폰_슬롯은_남는다()
        {
            StageState state = Fold(Cmd("left", "c9", "2u"));

            Assert.That(state.Unhandled.Count, Is.EqualTo(1));
            Assert.That(state.Unhandled[0].Reason, Does.Contain("c9"));
        }

        [Test]
        public void 스키마_없는_tuning으로는_슬롯을_세울_수_없다()
        {
            StageReducerTuning tuning = NewTuning();
            tuning.RigSchemas = null;

            StageState state = Fold(tuning, Cmd("slot", "c1"));

            Assert.That(state.HasSlot("c1"), Is.False);
            Assert.That(state.Unhandled.Count, Is.EqualTo(1));
            Assert.That(state.Unhandled[0].Reason, Does.Contain("리그 스키마"));
        }

        [Test]
        public void 폴드_중_예외는_기록이_되고_이전_상태는_무사하다()
        {
            // VisualOffset이 없는 손상 리그: show의 앵커 클레임이 트리에서 터진다.
            StageReducerTuning tuning = NewTuning();
            tuning.RigSchemas.rigs[0].nodes.RemoveAll(n => n.id == "CharacterPortrait_VisualOffset");
            tuning.RigSchemas.rigs[0].nodes.RemoveAll(n => n.parent == "CharacterPortrait_VisualOffset");
            tuning.RigSchemas.rigs[0].nodes.RemoveAll(n => n.id.StartsWith("CharacterPortrait_"));
            tuning.RigSchemas.rigs[0].nodes.RemoveAll(n => n.id.StartsWith("CharacterPortraitSprite_"));

            StageState before = Fold(tuning, Cmd("slot", "c1"));
            int unhandledBefore = before.Unhandled.Count;

            StageState after = StageReducer.Apply(before, Cmd("show", "c1"), tuning);

            Assert.That(after.Unhandled.Count, Is.GreaterThan(unhandledBefore));
            Assert.That(after.Unhandled.Any(u => u.Reason.Contains("폴드 중 예외")), Is.True);

            // 원본(이전 상태)은 무사하다 — Apply가 클론 위에서 돌았다.
            Assert.That(before.Unhandled.Count, Is.EqualTo(unhandledBefore));
        }

        [Test]
        public void 실패한_커맨드도_폴드를_멈추지_않는다()
        {
            // 예외로 폴드가 멈추면 그 뒤 라인을 하나도 판정할 수 없다.
            StageState state = Fold(
                Cmd("slot", "c1"),
                Cmd("모르는커맨드"),
                Cmd("left", "c1", "1u"));

            Assert.That(state.Unhandled.Count, Is.EqualTo(1));
            Assert.That(state.Nodes.GetState("c1/CharSlot_Track_X").AnchoredPosition.X,
                Is.EqualTo(-PixelsPerUnit).Within(Eps));
        }
    }
}
