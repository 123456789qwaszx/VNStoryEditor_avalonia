using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// place/size 폴드. 수학은 SettledFocusMath가 이미 성질로 고정했으므로,
    /// 여기서 보는 것은 **배선**이다: 튜닝 조회 → focus 오프셋 합 → 체인 인덱스 → 해.
    ///
    /// 튜닝 값은 실제 덤프(presets/depth.json · focus-tuning.json)의 실측값을 쓴다.
    /// </summary>
    public sealed class FocusStageReductionTests
    {
        private const float Eps = 1e-2f;

        private static Float2Dto F2(float x, float y) => new() { x = x, y = y };
        private static Float3Dto F3(float x, float y, float z) => new() { x = x, y = y, z = z };

        // ── 실측 튜닝 ────────────────────────────────────────────────

        /// <summary>presets/focus-tuning.json의 baseOffsets 실값.</summary>
        private static FocusTuningBodyDto FocusTuning() => new()
        {
            baseOffsets = new FocusOffsetSetDto
            {
                feet = F2(0f, 480f),
                body = F2(0f, 680f),
                bust = F2(0f, 820f),
                face = F2(0f, 950f),
                faceAura = F2(0f, 950f),
                handLeft = F2(-80f, 0f),
                handRight = F2(80f, 0f),
            },
            entries = new List<FocusEntryDto>
            {
                new()
                {
                    key = "bandi",
                    defaultOffset = F2(0f, -20f),
                    offsets = new FocusOffsetSetDto { bust = F2(0f, 10f) },
                },
            },
        };

        /// <summary>presets/depth.json의 실값.</summary>
        private static DepthPresetSetDto DepthPresets() => new()
        {
            far = new DepthPresetDto { depthY = F2(0f, 480f), depthScale = 1.00f, preserveFocusPreset = 0 },
            back = new DepthPresetDto { depthY = F2(0f, 240f), depthScale = 1.14f, preserveFocusPreset = 20 },
            mid = new DepthPresetDto { depthY = F2(0f, 0f), depthScale = 1.00f, preserveFocusPreset = 20 },
            front = new DepthPresetDto { depthY = F2(0f, -320f), depthScale = 1.38f, preserveFocusPreset = 20 },
            close = new DepthPresetDto { depthY = F2(0f, 440f), depthScale = 1.58f, preserveFocusPreset = 30 },
        };

        // ── 합성 리그 (실제 부모 사슬) ───────────────────────────────

        private static RigSchemaNodeDto Node(string id, string parent, bool bottomPivot = false) => new()
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
        };

        private static StageReducerTuning NewTuning()
        {
            RigSchemaRigDto rig = new()
            {
                rigKind = "character",
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
                },
            };

            return new StageReducerTuning
            {
                RigSchemas = new RigSchemasFileDto
                {
                    capturedUnderParentSize = F2(1920f, 1080f),
                    rigs = new List<RigSchemaRigDto> { rig },
                },
                ReferenceStageWidth = 1920f,
                BaseResolution = new Vec2(1920f, 1080f),
                RoleAnchors = new RoleAnchorTuningBodyDto(),
                DepthPresets = DepthPresets(),
                FocusTuning = FocusTuning(),
            };
        }

        private static StageCommand Cmd(string name, params string[] args) => new(name, args, "test.yarn:1");

        private static StageState Fold(StageReducerTuning tuning, params StageCommand[] commands)
            => StageReducer.ApplyAll(StageReducer.CreateInitialState(tuning), commands, tuning);

        private static Vec2 FocusOf(StageState state, string slotKey, string presetName, StageReducerTuning tuning)
        {
            state.TryGetCharacter(slotKey, out string characterKey);

            Vec2 offset = FocusOffsetMath.Resolve(tuning.FocusTuning, characterKey, presetName, Vec2.Zero);

            return SettledFocusMath.FocusPointInRigSpace(
                state.Nodes.BuildChainTo(StageState.NodeKeyOf(slotKey, "CharacterPortrait_VisualOffset")),
                state.Nodes.RootSpace,
                offset);
        }

        // ── 화면 지점 비율 ───────────────────────────────────────────

        [Test]
        public void 화면_지점은_비율표를_따른다()
        {
            Vec2 frame = new(1920f, 1080f);

            Assert.That(ScreenPointRatios.TryResolve(frame, "center", out Vec2 center), Is.True);
            Assert.That(center, Is.EqualTo(Vec2.Zero));

            ScreenPointRatios.TryResolve(frame, "left", out Vec2 left);
            Assert.That(left.X, Is.EqualTo(-460.8f).Within(Eps), "1920 × 0.24");

            ScreenPointRatios.TryResolve(frame, "tl", out Vec2 tl);
            Assert.That(tl.Y, Is.EqualTo(172.8f).Within(Eps), "1080 × 0.16");

            ScreenPointRatios.TryResolve(frame, "inner_br", out Vec2 innerBr);
            Assert.That(innerBr.X, Is.EqualTo(268.8f).Within(Eps), "1920 × 0.14");
            Assert.That(innerBr.Y, Is.EqualTo(-97.2f).Within(Eps), "1080 × 0.09");
        }

        [Test]
        public void 모르는_화면_지점은_거부한다()
        {
            Assert.That(ScreenPointRatios.TryResolve(new Vec2(1920f, 1080f), "그런곳없음", out _), Is.False);
        }

        // ── focus 오프셋 합 ──────────────────────────────────────────

        [Test]
        public void focus_오프셋은_base와_캐릭터_기본과_프리셋을_더한다()
        {
            // 런타임 ResolveOffset과 같은 합: 820(base bust) - 20(bandi 기본) + 10(bandi bust) = 810
            Vec2 offset = FocusOffsetMath.Resolve(FocusTuning(), "bandi", "bust", Vec2.Zero);

            Assert.That(offset.Y, Is.EqualTo(810f).Within(Eps));
        }

        [Test]
        public void 엔트리가_없는_캐릭터는_base만_적용된다()
        {
            Assert.That(
                FocusOffsetMath.Resolve(FocusTuning(), "모르는캐릭터", "bust", Vec2.Zero).Y,
                Is.EqualTo(820f).Within(Eps));

            Assert.That(
                FocusOffsetMath.Resolve(FocusTuning(), null, "face", Vec2.Zero).Y,
                Is.EqualTo(950f).Within(Eps));
        }

        [Test]
        public void 커맨드_오프셋이_마지막에_더해진다()
        {
            Assert.That(
                FocusOffsetMath.Resolve(FocusTuning(), "bandi", "bust", new Vec2(5f, 100f)).Y,
                Is.EqualTo(910f).Within(Eps));
        }

        // ── place: 명중 ──────────────────────────────────────────────

        [Test]
        public void place_center를_접으면_focus가_원점에_온다()
        {
            StageReducerTuning tuning = NewTuning();

            StageState state = Fold(tuning,
                Cmd("slot", "c1"),
                Cmd("place_center", "c1", "bust"));

            Assert.That(state.Unhandled, Is.Empty);

            Vec2 focus = FocusOf(state, "c1", "bust", tuning);

            Assert.That(focus.X, Is.EqualTo(0f).Within(Eps));
            Assert.That(focus.Y, Is.EqualTo(0f).Within(Eps));
        }

        [Test]
        public void place_left는_focus를_화면_왼쪽_지점으로_보낸다()
        {
            StageReducerTuning tuning = NewTuning();

            StageState state = Fold(tuning,
                Cmd("slot", "c1"),
                Cmd("place_left", "c1", "bust"));

            Vec2 focus = FocusOf(state, "c1", "bust", tuning);

            Assert.That(focus.X, Is.EqualTo(-460.8f).Within(Eps), "리포트에 나온 그 값");
        }

        [Test]
        public void place는_배역_오프셋을_반영한다()
        {
            // bandi는 bust 오프셋이 810(= 820-20+10)이라 이동량이 달라진다.
            StageReducerTuning tuning = NewTuning();

            StageState plain = Fold(tuning, Cmd("slot", "c1"), Cmd("place_center", "c1", "bust"));

            StageState cast = Fold(tuning,
                Cmd("slot", "c1"), Cmd("cast", "c1", "bandi"), Cmd("place_center", "c1", "bust"));

            Vec2 plainMove = plain.Nodes.GetState("c1/CharSlot_Track_Focus").AnchoredPosition;
            Vec2 castMove = cast.Nodes.GetState("c1/CharSlot_Track_Focus").AnchoredPosition;

            Assert.That(castMove.Y - plainMove.Y, Is.EqualTo(10f).Within(Eps),
                "820 → 810이면 10만큼 덜 내려간다");

            // 그래도 명중은 각자의 오프셋 기준으로 성립한다.
            Assert.That(FocusOf(cast, "c1", "bust", tuning).Y, Is.EqualTo(0f).Within(Eps));
        }

        [Test]
        public void place는_이동_축에만_클레임을_건다()
        {
            StageState state = Fold(NewTuning(), Cmd("slot", "c1"), Cmd("place_right", "c1", "bust"));

            Assert.That(state.Nodes.GetState("c1/CharSlot_Track").AnchoredPosition, Is.EqualTo(Vec2.Zero));
            Assert.That(state.Nodes.GetState("c1/CharSlot_Track_Focus").AnchoredPosition, Is.Not.EqualTo(Vec2.Zero));
        }

        [Test]
        public void 모르는_focus_토큰은_Unhandled다()
        {
            // 호스트 파서에는 별칭이 있지만 코어는 정규 이름만 안다 — 조용히 넘기지 않는다.
            StageState state = Fold(NewTuning(), Cmd("slot", "c1"), Cmd("place_center", "c1", "torso"));

            Assert.That(state.Unhandled.Count, Is.EqualTo(1));
            Assert.That(state.Unhandled[0].Reason, Does.Contain("focus 프리셋"));
        }

        // ── size: 보존 ───────────────────────────────────────────────

        [TestCase("size_back", 1.14f)]
        [TestCase("size_front", 1.38f)]
        [TestCase("size_close", 1.58f)]
        [TestCase("size_far", 1.00f)]
        public void size는_배율을_적용하고_focus를_보존한다(string command, float expectedScale)
        {
            StageReducerTuning tuning = NewTuning();

            StageState before = Fold(tuning, Cmd("slot", "c1"), Cmd("place_center", "c1", "bust"));
            Vec2 focusBefore = FocusOf(before, "c1", "bust", tuning);

            StageState after = StageReducer.Apply(before, Cmd(command, "c1", "bust"), tuning);

            Assert.That(after.Unhandled.Count, Is.EqualTo(before.Unhandled.Count), "접혀야 한다");

            Assert.That(after.Nodes.GetState("c1/CharSlot_DepthScale").LocalScale.X,
                Is.EqualTo(expectedScale).Within(Eps));

            // 보존: 배율이 바뀌어도 focus는 제자리.
            Vec2 focusAfter = FocusOf(after, "c1", "bust", tuning);

            Assert.That(focusAfter.X, Is.EqualTo(focusBefore.X).Within(Eps));
            Assert.That(focusAfter.Y, Is.EqualTo(focusBefore.Y).Within(Eps));
        }

        [Test]
        public void 보존_focus는_커맨드_인자가_정한다_프리셋_필드가_아니다()
        {
            // ⚠ 런타임 ResolveRawDepth가 프리셋의 preserveFocusPreset을 읽은 직후
            // 커맨드 인자로 덮어쓴다 — 덤프의 그 필드는 사장 데이터다.
            // size_close의 프리셋 값은 30(face)이지만 인자 없이 부르면 브리지 기본 "bust"가 된다.
            StageReducerTuning tuning = NewTuning();

            StageState before = Fold(tuning, Cmd("slot", "c1"));

            StageState byDefault = StageReducer.Apply(before, Cmd("size_close", "c1"), tuning);
            StageState byFace = StageReducer.Apply(before, Cmd("size_close", "c1", "face"), tuning);

            float defaultY = byDefault.Nodes.GetState("c1/CharSlot_DepthY").AnchoredPosition.Y;
            float faceY = byFace.Nodes.GetState("c1/CharSlot_DepthY").AnchoredPosition.Y;

            Assert.That(System.Math.Abs(defaultY - faceY), Is.GreaterThan(1f),
                "보존 대상이 다르면 보정도 달라야 한다");

            // 각자 자기 기준으로 보존이 성립한다.
            Assert.That(FocusOf(byDefault, "c1", "bust", tuning).Y,
                Is.EqualTo(FocusOf(before, "c1", "bust", tuning).Y).Within(Eps),
                "인자 없으면 bust 보존 (프리셋의 face가 아니다)");

            Assert.That(FocusOf(byFace, "c1", "face", tuning).Y,
                Is.EqualTo(FocusOf(before, "c1", "face", tuning).Y).Within(Eps));
        }

        [Test]
        public void size의_숫자_레벨은_커브_미지원으로_Unhandled다()
        {
            // 실제 원문이 쓴다: <<size c1 5>>, <<size @4 14 bust>>, <<size 22 face>>
            StageState state = Fold(NewTuning(), Cmd("slot", "c1"), Cmd("size", "c1", "14", "bust"));

            Assert.That(state.Unhandled.Count, Is.EqualTo(1));
            Assert.That(state.Unhandled[0].Reason, Does.Contain("커브 폴드 미지원"));

            // 실패한 커맨드는 상태를 바꾸지 않는다.
            Assert.That(state.Nodes.GetState("c1/CharSlot_DepthScale").LocalScale.XY, Is.EqualTo(Vec2.One));
        }

        [Test]
        public void depth_튜닝이_없으면_size가_소리를_낸다()
        {
            StageReducerTuning tuning = NewTuning();
            tuning.DepthPresets = null;

            StageState state = Fold(tuning, Cmd("slot", "c1"), Cmd("size_close", "c1"));

            Assert.That(state.Unhandled.Any(u => u.Reason.Contains("depth 프리셋")), Is.True);
        }

        // ── shot_focus_to ────────────────────────────────────────────

        [Test]
        public void shot_focus_to를_접으면_적용측_규약으로_화면_지점에_온다()
        {
            // 명중 검산: 보이는 위치 = 논리 focus × 목표 배율 + pan.
            StageReducerTuning tuning = NewTuning();

            StageState state = Fold(tuning,
                Cmd("slot", "c1"),
                Cmd("place_center", "c1", "bust"),
                Cmd("shot_focus_to", "c1", "face", "center", "1.6"));

            Assert.That(state.Unhandled, Is.Empty);
            Assert.That(state.Shot.Zoom, Is.EqualTo(1.6f).Within(Eps));

            float scale = ShotIntentMath.EvaluateCameraScale(state.Shot.Zoom);

            Vec2 visible = new(
                state.Shot.FocusPointInRigSpace.X * scale + state.Shot.PanInRigSpace.X,
                state.Shot.FocusPointInRigSpace.Y * scale + state.Shot.PanInRigSpace.Y);

            Assert.That(visible.X, Is.EqualTo(0f).Within(Eps), "center에 명중");
            Assert.That(visible.Y, Is.EqualTo(0f).Within(Eps));
        }

        [Test]
        public void shot_focus_to는_현재_카메라를_벗겨_논리_focus를_남긴다()
        {
            StageReducerTuning tuning = NewTuning();

            // 카메라가 이미 움직인 상태에서 접는다.
            StageState state = Fold(tuning,
                Cmd("slot", "c1"),
                Cmd("shot_to", "3", "1u", "0u"),
                Cmd("shot_focus_to", "c1", "bust", "left", "2"));

            Assert.That(state.Unhandled, Is.Empty);

            float scale = ShotIntentMath.EvaluateCameraScale(2f);

            Vec2 visible = new(
                state.Shot.FocusPointInRigSpace.X * scale + state.Shot.PanInRigSpace.X,
                state.Shot.FocusPointInRigSpace.Y * scale + state.Shot.PanInRigSpace.Y);

            ScreenPointRatios.TryResolve(new Vec2(1920f, 1080f), "left", out Vec2 desired);

            Assert.That(visible.X, Is.EqualTo(desired.X).Within(Eps));
            Assert.That(visible.Y, Is.EqualTo(desired.Y).Within(Eps));
        }
    }
}
