using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// U14 코어 절반 — 폴드 골든. 스키마 모양의 미니 캐릭터 리그로
    /// slot → show → 이동/스케일/회전/샷 커맨드 열을 접고,
    /// Unhandled 규율(버리지 않는다)과 Apply의 순수성(원본 불변)을 고정한다.
    /// </summary>
    public sealed class StageReducerTests
    {
        private const float Eps = 1e-3f;

        private static Float2Dto F2(float x, float y) => new Float2Dto { x = x, y = y };
        private static Float3Dto F3(float x, float y, float z) => new Float3Dto { x = x, y = y, z = z };

        /// <summary>실제 CharacterRigSchema의 사슬 순서를 축약한 캐릭터 리그 덤프.</summary>
        private static RigSchemasFileDto MakeTuningSchemas()
        {
            string[] chain =
            {
                "__root",
                "CharSlot_Track_Focus", "CharSlot_DepthY", "CharSlot_DepthScale",
                "CharSlot_Track", "CharSlot_Track_X", "CharSlot_Track_Y",
                "CharSlot_Rotation", "CharSlot_SwayPivot", "CharSlot_Scale",
                "CharacterPortrait_VisualOffset",
                "CharacterPortrait_Track", "CharacterPortrait_Rotation",
                "CharacterPortrait_Track_Move", "CharacterPortrait_Track_Move_X", "CharacterPortrait_Track_Move_Y",
                "CharacterPortrait_SwayPivot", "CharacterPortrait_Shake",
                "CharacterPortrait_ActingScale", "CharacterPortrait_ActingScale_X", "CharacterPortrait_ActingScale_Y",
                "CharacterPortraitSprite_Root",
            };

            List<RigSchemaNodeDto> nodes = new();

            for (int i = 0; i < chain.Length; i++)
            {
                // 실물 스키마처럼: 초상 스프라이트 루트는 CanvasGroup alpha 0으로 태어난다.
                bool isPortraitSpriteRoot = chain[i] == "CharacterPortraitSprite_Root";

                nodes.Add(new RigSchemaNodeDto
                {
                    id = chain[i],
                    parent = i == 0 ? "" : chain[i - 1],
                    anchoredPosition = F2(0f, 0f),
                    anchorMin = F2(0f, 0f),
                    anchorMax = F2(1f, 1f),
                    pivot = F2(0.5f, 0.5f),
                    sizeDelta = F2(0f, 0f),
                    localScale = F3(1f, 1f, 1f),
                    localEulerAngles = F3(0f, 0f, 0f),
                    measuredRectSize = F2(1920f, 1080f),
                    hasCanvasGroup = isPortraitSpriteRoot,
                    canvasGroupAlpha = 0f,
                });
            }

            return new RigSchemasFileDto
            {
                capturedUnderParentSize = F2(1920f, 1080f),
                rigs = new List<RigSchemaRigDto>
                {
                    new RigSchemaRigDto { rigKind = "character", nodes = nodes },
                },
            };
        }

        private static StageReducerTuning MakeTuning() => new StageReducerTuning
        {
            RigSchemas = MakeTuningSchemas(),
            ReferenceStageWidth = 1920f,
            BaseResolution = new Vec2(1920f, 1080f),
        };

        private static StageCommand Cmd(string name, params string[] args)
            => new StageCommand(name, args, source: "test.yarn:1");

        // ── 슬롯과 폴드 기본 ─────────────────────────────────────────

        [Test]
        public void slot은_리그를_세운다()
        {
            StageReducerTuning tuning = MakeTuning();
            StageState state = StageReducer.CreateInitialState(tuning);

            state = StageReducer.Apply(state, Cmd("slot", "c1"), tuning);

            Assert.That(state.HasSlot("c1"), Is.True);
            Assert.That(state.Nodes.Contains("c1/CharSlot_Track"), Is.True);
            Assert.That(state.Unhandled, Is.Empty);
            Assert.That(state.TryGetAttachment("c1", out SlotAttachment att), Is.True);
            Assert.That(att.StageKey, Is.EqualTo("stage00"));
            Assert.That(att.LayerKey, Is.EqualTo("mid"));
        }

        [Test]
        public void Apply는_원본_상태를_바꾸지_않는다()
        {
            StageReducerTuning tuning = MakeTuning();
            StageState before = StageReducer.CreateInitialState(tuning);

            StageState after = StageReducer.Apply(before, Cmd("slot", "c1"), tuning);

            Assert.That(before.HasSlot("c1"), Is.False);
            Assert.That(after.HasSlot("c1"), Is.True);
            Assert.That(before.Nodes.Count, Is.EqualTo(0));
        }

        [Test]
        public void 모르는_커맨드는_출처와_함께_Unhandled에_남는다()
        {
            StageReducerTuning tuning = MakeTuning();
            StageState state = StageReducer.CreateInitialState(tuning);

            state = StageReducer.Apply(state, Cmd("bg_spawn", "bg_main", "class_day"), tuning);

            Assert.That(state.Unhandled.Count, Is.EqualTo(1));
            Assert.That(state.Unhandled[0].Command.Name, Is.EqualTo("bg_spawn"));
            Assert.That(state.Unhandled[0].Command.Source, Is.EqualTo("test.yarn:1"));
        }

        [Test]
        public void 슬롯_없이_온_이동_커맨드도_Unhandled다()
        {
            StageReducerTuning tuning = MakeTuning();
            StageState state = StageReducer.CreateInitialState(tuning);

            state = StageReducer.Apply(state, Cmd("left", "c1", "3u"), tuning);

            Assert.That(state.Unhandled.Count, Is.EqualTo(1));
            Assert.That(state.Unhandled[0].Reason, Does.Contain("c1"));
        }

        // ── 이동/스케일/회전 폴드 ────────────────────────────────────

        [Test]
        public void nudge는_1u가_기준_폭에서_파생된_픽셀만큼_움직인다()
        {
            StageReducerTuning tuning = MakeTuning();
            StageState state = StageReducer.CreateInitialState(tuning);

            state = StageReducer.ApplyAll(state, new[]
            {
                Cmd("slot", "c1"),
                Cmd("left", "c1", "3u"),     // -120px @1920
                Cmd("up", "c1", "0.5u"),     // +20px
            }, tuning);

            Assert.That(state.Nodes.GetState("c1/CharSlot_Track_X").AnchoredPosition.X,
                Is.EqualTo(-120f).Within(Eps));
            Assert.That(state.Nodes.GetState("c1/CharSlot_Track_Y").AnchoredPosition.Y,
                Is.EqualTo(20f).Within(Eps));

            // 정지 프레임 좌표까지: 사슬 전체가 스트레치 항등이라 합산 그대로다.
            Vec3 world = state.Nodes.TransformPoint("c1/CharSlot_Track_Y", Vec3.Zero);
            Assert.That(world.X, Is.EqualTo(-120f).Within(Eps));
            Assert.That(world.Y, Is.EqualTo(20f).Within(Eps));
        }

        [Test]
        public void move_scale_rotate_reset_계열이_접힌다()
        {
            StageReducerTuning tuning = MakeTuning();
            StageState state = StageReducer.CreateInitialState(tuning);

            state = StageReducer.ApplyAll(state, new[]
            {
                Cmd("slot", "c1"),
                Cmd("move_by", "c1", "2u", "-1u"),
                Cmd("scale_by", "c1", "1.2"),
                Cmd("scale_by", "c1", "1.2"),
                Cmd("rotate_by", "c1", "15"),
                Cmd("move_reset", "c1"),
                Cmd("rotate_reset", "c1"),
            }, tuning);

            Assert.That(state.Nodes.GetState("c1/CharSlot_Track").AnchoredPosition,
                Is.EqualTo(Vec2.Zero), "move_reset");
            Assert.That(state.Nodes.GetState("c1/CharSlot_SwayPivot").LocalEulerAngles,
                Is.EqualTo(Vec3.Zero), "rotate_reset");
            Assert.That(state.Nodes.GetState("c1/CharSlot_Scale").LocalScale.X,
                Is.EqualTo(1.44f).Within(Eps), "scale_by 두 번 누적");
            Assert.That(state.Unhandled, Is.Empty);
        }

        [Test]
        public void left_per은_프레임_수에_비례한_거리다()
        {
            StageReducerTuning tuning = MakeTuning();
            StageState state = StageReducer.CreateInitialState(tuning);

            state = StageReducer.ApplyAll(state, new[]
            {
                Cmd("slot", "c1"),
                Cmd("right_per", "c1", "6fr"),   // 1u × 6 = 240px
            }, tuning);

            Assert.That(state.Nodes.GetState("c1/CharSlot_Track_X").AnchoredPosition.X,
                Is.EqualTo(240f).Within(Eps));
        }

        // ── show ─────────────────────────────────────────────────────

        [Test]
        public void show는_축을_리셋하고_가시성을_켜고_표정_상태를_접는다()
        {
            StageReducerTuning tuning = MakeTuning();
            StageState state = StageReducer.CreateInitialState(tuning);

            state = StageReducer.ApplyAll(state, new[]
            {
                Cmd("slot", "c1"),
                Cmd("move_by", "c1", "2u", "0u"),   // 어지럽힌 뒤
                Cmd("rotate_by", "c1", "30"),
                Cmd("show", "c1", "e2"),
            }, tuning);

            Assert.That(state.Nodes.GetState("c1/CharSlot_Track").AnchoredPosition,
                Is.EqualTo(Vec2.Zero), "show가 Track을 리셋");

            // 주의: show(SetAnchor)의 리셋 목록은 CharSlot_Rotation이지 SwayPivot이 아니다.
            // rotate_by(SwayPivot)는 show로 되돌아가지 않는다 — 런타임 실동작 그대로다.
            Assert.That(state.Nodes.GetState("c1/CharSlot_SwayPivot").LocalEulerAngles.Z,
                Is.EqualTo(30f).Within(Eps), "rotate_by는 show에 리셋되지 않는다");
            Assert.That(state.Nodes.GetState("c1/CharSlot_Rotation").LocalEulerAngles,
                Is.EqualTo(Vec3.Zero), "show가 Rotation 축을 리셋");
            Assert.That(state.GetAlpha("c1/__root"), Is.EqualTo(1f));
            Assert.That(state.GetAlpha("c1/CharacterPortraitSprite_Root"), Is.EqualTo(1f));

            // 표정 축이 폴드에 들어왔다(U14-c5). 이 시나리오는 cast가 없어 사이징 대상이
            // 없고, show 자체는 기록 없이 접힌다.
            Assert.That(state.Unhandled.Count(u => u.Command.Name == "show"), Is.EqualTo(0));
        }

        [Test]
        public void 스폰_시_초기_alpha는_덤프의_CanvasGroup_값이다()
        {
            StageReducerTuning tuning = MakeTuning();
            StageState state = StageReducer.CreateInitialState(tuning);

            state = StageReducer.Apply(state, Cmd("slot", "c1"), tuning);

            // 초상 스프라이트 루트는 0으로 태어나고, CanvasGroup 없는 노드는 1(기본)이다.
            Assert.That(state.GetAlpha("c1/CharacterPortraitSprite_Root"), Is.EqualTo(0f));
            Assert.That(state.GetAlpha("c1/CharSlot_Track"), Is.EqualTo(1f));
        }

        // ── 배역·별칭·fade ───────────────────────────────────────────

        [Test]
        public void cast와_actor를_거쳐_별칭으로_커맨드가_풀린다()
        {
            StageReducerTuning tuning = MakeTuning();
            StageState state = StageReducer.CreateInitialState(tuning);

            state = StageReducer.ApplyAll(state, new[]
            {
                Cmd("slot", "c3"),
                Cmd("cast", "c3", "parkeunseol"),
                Cmd("actor", "@3", "parkeunseol"),
                Cmd("left", "@3", "1u"),           // 별칭 → 캐릭터 → 슬롯
                Cmd("right", "parkeunseol", "3u"), // 캐릭터 키 직접
            }, tuning);

            // cast의 기록 1건(치수 덤프 없는 tuning이라 사이징 미폴드) 외에는 전부 접혀야 한다.
            Assert.That(state.Unhandled.Count(u => u.Command.Name == "cast"), Is.EqualTo(1));
            Assert.That(state.Unhandled.Count(u => u.Command.Name != "cast"), Is.EqualTo(0));
            Assert.That(state.Unhandled.Single(u => u.Command.Name == "cast").Reason,
                Does.Contain("portrait-dimensions.json"));

            // -40 + 120 = +80px.
            Assert.That(state.Nodes.GetState("c3/CharSlot_Track_X").AnchoredPosition.X,
                Is.EqualTo(80f).Within(Eps));
        }

        [Test]
        public void 재배역하면_캐릭터가_새_슬롯으로_풀린다()
        {
            StageReducerTuning tuning = MakeTuning();
            StageState state = StageReducer.CreateInitialState(tuning);

            state = StageReducer.ApplyAll(state, new[]
            {
                Cmd("slot", "c1"),
                Cmd("slot", "c2"),
                Cmd("cast", "c1", "yoonsaea"),
                Cmd("cast", "c2", "yoonsaea"),      // 같은 캐릭터를 c2로
                Cmd("left", "yoonsaea", "1u"),
            }, tuning);

            Assert.That(state.Nodes.GetState("c2/CharSlot_Track_X").AnchoredPosition.X,
                Is.EqualTo(-40f).Within(Eps));
            Assert.That(state.Nodes.GetState("c1/CharSlot_Track_X").AnchoredPosition.X,
                Is.EqualTo(0f).Within(Eps));
        }

        [Test]
        public void fade는_초상_스프라이트_루트의_가시성이다()
        {
            StageReducerTuning tuning = MakeTuning();
            StageState state = StageReducer.CreateInitialState(tuning);

            state = StageReducer.ApplyAll(state, new[]
            {
                Cmd("slot", "c1"),
                Cmd("cast", "c1", "yoonsaea"),
                Cmd("actor", "@1", "yoonsaea"),
                Cmd("fade_in", "@1"),
            }, tuning);

            Assert.That(state.GetAlpha("c1/CharacterPortraitSprite_Root"), Is.EqualTo(1f));

            state = StageReducer.Apply(state, Cmd("fade_out", "@1"), tuning);

            Assert.That(state.GetAlpha("c1/CharacterPortraitSprite_Root"), Is.EqualTo(0f));
        }

        // ── shot ─────────────────────────────────────────────────────

        [Test]
        public void shot_계열이_접힌다()
        {
            StageReducerTuning tuning = MakeTuning();
            StageState state = StageReducer.CreateInitialState(tuning);

            state = StageReducer.ApplyAll(state, new[]
            {
                Cmd("shot_zoom", "2.5"),
                Cmd("shot_track", "1u", "0u"),      // pan += (40, 0)
                Cmd("shot_track", "-0.5u", "1u"),   // pan += (-20, 40)
            }, tuning);

            Assert.That(state.Shot.Zoom, Is.EqualTo(2.5f).Within(Eps));
            Assert.That(state.Shot.PanInRigSpace.X, Is.EqualTo(20f).Within(Eps));
            Assert.That(state.Shot.PanInRigSpace.Y, Is.EqualTo(40f).Within(Eps));

            state = StageReducer.Apply(state, Cmd("shot_reset"), tuning);

            Assert.That(state.Shot.Zoom, Is.EqualTo(0f));
            Assert.That(state.Shot.PanInRigSpace, Is.EqualTo(Vec2.Zero));
        }

        // ── 구조 축 ──────────────────────────────────────────────────

        [Test]
        public void char_to는_부착을_갱신한다()
        {
            StageReducerTuning tuning = MakeTuning();
            StageState state = StageReducer.CreateInitialState(tuning);

            state = StageReducer.ApplyAll(state, new[]
            {
                Cmd("slot01", "c1", "far"),
                Cmd("char_to_s2", "c1", "front"),
            }, tuning);

            Assert.That(state.TryGetAttachment("c1", out SlotAttachment att), Is.True);
            Assert.That(att.StageKey, Is.EqualTo("stage02"));
            Assert.That(att.LayerKey, Is.EqualTo("front"));
        }

        // ── 방어선 ───────────────────────────────────────────────────

        [Test]
        public void 잘못된_인자는_이유와_함께_Unhandled다()
        {
            StageReducerTuning tuning = MakeTuning();
            StageState state = StageReducer.CreateInitialState(tuning);

            state = StageReducer.ApplyAll(state, new[]
            {
                Cmd("slot", "c1"),
                Cmd("left", "c1", "abc"),
                Cmd("scale_by", "c1", "not-a-number"),
            }, tuning);

            Assert.That(state.Unhandled.Count, Is.EqualTo(2));
            Assert.That(state.Unhandled[0].Reason, Does.Contain("abc"));
        }

        [Test]
        public void 스키마_없는_tuning으로_slot을_접으면_Unhandled다()
        {
            StageReducerTuning tuning = new StageReducerTuning
            {
                RigSchemas = null,
                ReferenceStageWidth = 1920f,
                BaseResolution = new Vec2(1920f, 1080f),
            };

            StageState state = StageReducer.CreateInitialState(tuning);
            state = StageReducer.Apply(state, Cmd("slot", "c1"), tuning);

            Assert.That(state.HasSlot("c1"), Is.False);
            Assert.That(state.Unhandled.Count, Is.EqualTo(1));
        }
    }
}