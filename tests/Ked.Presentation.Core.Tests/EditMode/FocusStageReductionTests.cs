using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// U14-c2 골든: place/size 폴드. 성질로 고정한다 —
    /// place: 접은 뒤 focus가 화면 지점에 선다. size: 접은 뒤 focus가 제자리다.
    /// 값은 실제 덤프 모양(depth mid=1.0/close=1.38, focus bust=820 등)을 쓴다.
    /// </summary>
    public sealed class FocusStageReductionTests
    {
        private const float Eps = 1e-2f;

        private static Float2Dto F2(float x, float y) => new Float2Dto { x = x, y = y };

        private static DepthPresetSetDto MakeDepthPresets() => new DepthPresetSetDto
        {
            mid = new DepthPresetDto
            {
                depthY = F2(0f, 0f), depthScale = 1f,
                preserveFocusPreset = 20, preserveFocusOffset = F2(0f, 0f),
            },
            close = new DepthPresetDto
            {
                depthY = F2(0f, 440f), depthScale = 1.38f,
                preserveFocusPreset = 30, preserveFocusOffset = F2(0f, 0f),
            },
            back = new DepthPresetDto
            {
                depthY = F2(0f, 240f), depthScale = 1.14f,
                preserveFocusPreset = 20, preserveFocusOffset = F2(0f, 0f),
            },
        };

        private static FocusTuningBodyDto MakeFocusTuning() => new FocusTuningBodyDto
        {
            baseOffsets = new FocusOffsetSetDto
            {
                feet = F2(0f, 480f), body = F2(0f, 680f),
                bust = F2(0f, 820f), face = F2(0f, 950f),
            },
            entries =
            {
                new FocusEntryDto
                {
                    key = "parkeunseol",
                    defaultOffset = F2(0f, -20f),
                    offsets = new FocusOffsetSetDto { bust = F2(0f, 10f) },
                },
            },
        };

        private static StageReducerTuning MakeTuning()
        {
            // StageReducerTests의 미니 스키마를 재사용하기 위해 같은 모양으로 만든다.
            Float3Dto F3(float x, float y, float z) => new Float3Dto { x = x, y = y, z = z };

            string[] chain =
            {
                "__root", "CharSlot_Track_Focus", "CharSlot_DepthY", "CharSlot_DepthScale",
                "CharSlot_Track", "CharSlot_Track_X", "CharSlot_Track_Y",
                "CharSlot_Rotation", "CharSlot_SwayPivot", "CharSlot_Scale",
                "CharacterPortrait_VisualOffset",
            };

            List<RigSchemaNodeDto> nodes = new();

            for (int i = 0; i < chain.Length; i++)
            {
                bool bottomPivot = chain[i] is "CharSlot_DepthScale" or "CharSlot_SwayPivot"
                    or "CharSlot_Scale" or "CharacterPortrait_VisualOffset";

                nodes.Add(new RigSchemaNodeDto
                {
                    id = chain[i],
                    parent = i == 0 ? "" : chain[i - 1],
                    anchoredPosition = F2(0f, 0f),
                    anchorMin = F2(0f, 0f),
                    anchorMax = F2(1f, 1f),
                    pivot = bottomPivot ? F2(0.5f, 0f) : F2(0.5f, 0.5f),
                    sizeDelta = F2(0f, 0f),
                    localScale = F3(1f, 1f, 1f),
                    localEulerAngles = F3(0f, 0f, 0f),
                });
            }

            return new StageReducerTuning
            {
                RigSchemas = new RigSchemasFileDto
                {
                    capturedUnderParentSize = F2(1920f, 1080f),
                    rigs = new List<RigSchemaRigDto>
                    {
                        new RigSchemaRigDto { rigKind = "character", nodes = nodes },
                    },
                },
                ReferenceStageWidth = 1920f,
                BaseResolution = new Vec2(1920f, 1080f),
                DepthPresets = MakeDepthPresets(),
                FocusTuning = MakeFocusTuning(),
            };
        }

        private static void AddPortraitNodes(StageReducerTuning tuning, float fixedRootHeight = 0f)
        {
            List<RigSchemaNodeDto> nodes = tuning.RigSchemas.rigs[0].nodes;
            bool fixedHeight = fixedRootHeight > 0f;

            string[] chain =
            {
                "CharacterPortrait_Track", "CharacterPortrait_Rotation",
                "CharacterPortrait_Track_Move", "CharacterPortrait_Track_Move_X", "CharacterPortrait_Track_Move_Y",
                "CharacterPortrait_SwayPivot", "CharacterPortrait_Shake",
                "CharacterPortrait_ActingScale", "CharacterPortrait_ActingScale_X", "CharacterPortrait_ActingScale_Y",
                "CharacterPortraitSprite_Root", "CharacterPortraitSprite_Image",
            };

            for (int i = 0; i < chain.Length; i++)
            {
                bool isSpriteRoot = chain[i] == "CharacterPortraitSprite_Root";

                nodes.Add(new RigSchemaNodeDto
                {
                    id = chain[i],
                    parent = i == 0 ? "CharacterPortrait_VisualOffset" : chain[i - 1],
                    anchoredPosition = F2(0f, 0f),
                    anchorMin = isSpriteRoot && fixedHeight ? F2(0.5f, 0.5f) : F2(0f, 0f),
                    anchorMax = isSpriteRoot && fixedHeight ? F2(0.5f, 0.5f) : F2(1f, 1f),
                    pivot = F2(0.5f, 0.5f),
                    sizeDelta = isSpriteRoot && fixedHeight ? F2(0f, fixedRootHeight) : F2(0f, 0f),
                    localScale = new Float3Dto { x = 1f, y = 1f, z = 1f },
                    localEulerAngles = new Float3Dto(),
                });
            }
        }

        private static StageCommand Cmd(string name, params string[] args)
            => new StageCommand(name, args, "t.yarn:1");

        private static Vec2 FocusPointOf(StageState state, string slotKey, string presetName, StageReducerTuning tuning)
        {
            state.TryGetCharacter(slotKey, out string character);
            Vec2 offset = FocusOffsetMath.Resolve(tuning.FocusTuning, character, presetName, Vec2.Zero);

            RectNodeState[] chain = state.Nodes.BuildChainTo(
                StageState.NodeKeyOf(slotKey, "CharacterPortrait_VisualOffset"));

            return SettledFocusMath.FocusPointInRigSpace(chain, state.Nodes.RootSpace, offset);
        }

        [Test]
        public void place_center를_접으면_focus가_화면_가운데에_선다()
        {
            StageReducerTuning tuning = MakeTuning();
            StageState state = StageReducer.CreateInitialState(tuning);

            state = StageReducer.ApplyAll(state, new[]
            {
                Cmd("slot", "c3"),
                Cmd("cast", "c3", "parkeunseol"),
                Cmd("actor", "@3", "parkeunseol"),
                Cmd("place_center", "@3", "bust", "0fr"),
            }, tuning);

            Assert.That(state.Unhandled.Count(u => u.Command.Name == "place_center"), Is.EqualTo(0),
                string.Join("\n", state.Unhandled));

            Vec2 landed = FocusPointOf(state, "c3", "bust", tuning);

            Assert.That(landed.X, Is.EqualTo(0f).Within(Eps));
            Assert.That(landed.Y, Is.EqualTo(0f).Within(Eps));
        }

        [Test]
        public void place_left는_바깥_비율_지점이다()
        {
            StageReducerTuning tuning = MakeTuning();
            StageState state = StageReducer.CreateInitialState(tuning);

            state = StageReducer.ApplyAll(state, new[]
            {
                Cmd("slot", "c1"),
                Cmd("place_left", "c1", "face"),
            }, tuning);

            Vec2 landed = FocusPointOf(state, "c1", "face", tuning);

            Assert.That(landed.X, Is.EqualTo(-1920f * 0.24f).Within(Eps)); // -460.8 — 리포트의 그 값
            Assert.That(landed.Y, Is.EqualTo(0f).Within(Eps));
        }

        [Test]
        public void size를_접으면_focus는_제자리고_배율은_프리셋이다()
        {
            StageReducerTuning tuning = MakeTuning();
            StageState state = StageReducer.CreateInitialState(tuning);

            state = StageReducer.ApplyAll(state, new[]
            {
                Cmd("slot", "c3"),
                Cmd("cast", "c3", "parkeunseol"),
                Cmd("place_center", "c3", "bust"),
            }, tuning);

            Vec2 before = FocusPointOf(state, "c3", "bust", tuning);

            state = StageReducer.Apply(state, Cmd("size", "c3", "close", "bust", "0fr"), tuning);

            Assert.That(state.Unhandled.Count(u => u.Command.Name == "size"), Is.EqualTo(0),
                string.Join("\n", state.Unhandled));

            Vec2 after = FocusPointOf(state, "c3", "bust", tuning);

            Assert.That(after.X, Is.EqualTo(before.X).Within(Eps), "focus X 보존");
            Assert.That(after.Y, Is.EqualTo(before.Y).Within(Eps), "focus Y 보존");
            Assert.That(state.Nodes.GetState("c3/CharSlot_DepthScale").LocalScale.X,
                Is.EqualTo(1.38f).Within(1e-4f), "close 프리셋 배율");
        }

        [Test]
        public void shot_focus_to를_접으면_focus가_목표_배율로_화면_지점에_보인다()
        {
            StageReducerTuning tuning = MakeTuning();
            StageState state = StageReducer.CreateInitialState(tuning);

            state = StageReducer.ApplyAll(state, new[]
            {
                Cmd("slot", "c3"),
                Cmd("cast", "c3", "parkeunseol"),
                Cmd("place_center", "c3", "bust"),
                Cmd("shot_focus_to", "c3", "face", "center", "2.5", "1.2s"),
            }, tuning);

            Assert.That(state.Unhandled.Count(u => u.Command.Name == "shot_focus_to"), Is.EqualTo(0),
                string.Join("\n", state.Unhandled));

            Assert.That(state.Shot.Zoom, Is.EqualTo(2.5f).Within(1e-4f));

            // 명중 검산: 논리 focus × 목표 배율 + pan = 화면 지점(center = 0,0).
            Vec2 logical = FocusPointOf(state, "c3", "face", tuning);
            float scale = ShotIntentMath.EvaluateCameraScale(state.Shot.Zoom);
            Vec2 visible = logical * scale + state.Shot.PanInRigSpace;

            Assert.That(visible.X, Is.EqualTo(0f).Within(Eps));
            Assert.That(visible.Y, Is.EqualTo(0f).Within(Eps));
        }

        [Test]
        public void 모르는_프리셋과_레벨_수치는_Unhandled다()
        {
            StageReducerTuning tuning = MakeTuning();
            StageState state = StageReducer.CreateInitialState(tuning);

            state = StageReducer.ApplyAll(state, new[]
            {
                Cmd("slot", "c1"),
                Cmd("size", "c1", "7", "bust"),        // 레벨 수치 — 커브 폴드 미지원
                Cmd("place_center", "c1", "elbow"),    // 모르는 focus 토큰
            }, tuning);

            Assert.That(state.Unhandled.Count, Is.EqualTo(2));
            Assert.That(state.Unhandled[0].Reason, Does.Contain("7"));
            Assert.That(state.Unhandled[1].Reason, Does.Contain("elbow"));
        }

        [Test]
        public void cast를_접으면_초상_사이징이_치수_덤프에서_온다()
        {
            StageReducerTuning tuning = MakeTuning();

            tuning.PortraitDimensions = new PortraitDimensionsFileDto
            {
                entries =
                {
                    new PortraitDimensionDto { character = "parkeunseol", variant = "a", emotion = "01", width = 574.5f, height = 1000f },
                    new PortraitDimensionDto { character = "parkeunseol", variant = "a", emotion = "02", width = 574.5f, height = 1000f },
                },
            };

            AddPortraitNodes(tuning);

            StageState state = StageReducer.CreateInitialState(tuning);

            state = StageReducer.ApplyAll(state, new[]
            {
                Cmd("slot", "c3"),
                Cmd("cast", "c3", "parkeunseol"),
            }, tuning);

            // 폭 = 부모 높이(1080) × 종횡비(0.5745) = 620.46 — 리포트의 그 계열 값.
            Assert.That(state.Nodes.GetState("c3/CharacterPortraitSprite_Image").SizeDelta.X,
                Is.EqualTo(1080f * 0.5745f).Within(0.1f));
            Assert.That(state.Nodes.GetState("c3/CharacterPortraitSprite_Image").SizeDelta.Y,
                Is.EqualTo(0f));

            // 사이징까지 접혔으니 cast는 기록 없이 통과다.
            Assert.That(state.Unhandled.Count(u => u.Command.Name == "cast"), Is.EqualTo(0));
        }

        [Test]
        public void 초상_변경_커맨드가_variant와_표정별_치수로_사이징을_다시_접는다()
        {
            StageReducerTuning tuning = MakeTuning();

            tuning.PortraitDimensions = new PortraitDimensionsFileDto
            {
                entries =
                {
                    new PortraitDimensionDto { character = "x", variant = "x_a", emotion = "01", width = 500f, height = 1000f },
                    new PortraitDimensionDto { character = "x", variant = "x_a", emotion = "2", width = 700f, height = 1000f },
                    new PortraitDimensionDto { character = "x", variant = "x_b", emotion = "01", width = 600f, height = 1000f },
                    new PortraitDimensionDto { character = "x", variant = "x_b", emotion = "02", width = 800f, height = 1000f },
                },
            };

            AddPortraitNodes(tuning);

            StageState state = StageReducer.CreateInitialState(tuning);

            state = StageReducer.ApplyAll(state, new[]
            {
                Cmd("slot", "c1"),
                Cmd("cast", "c1", "x"),
            }, tuning);

            string imageKey = "c1/CharacterPortraitSprite_Image";
            Assert.That(state.Nodes.GetState(imageKey).SizeDelta.X,
                Is.EqualTo(540f).Within(Eps), "cast 기본 a/01");

            state = StageReducer.Apply(state, Cmd("show", "c1", "e2"), tuning);
            Assert.That(state.Nodes.GetState(imageKey).SizeDelta.X,
                Is.EqualTo(756f).Within(Eps), "show e2 → a/02");

            state = StageReducer.Apply(state, Cmd("face", "c1", "1"), tuning);
            Assert.That(state.Nodes.GetState(imageKey).SizeDelta.X,
                Is.EqualTo(540f).Within(Eps), "face 1 → a/01");

            state = StageReducer.Apply(state, Cmd("face_swap", "c1", "2", "5fr"), tuning);
            Assert.That(state.Nodes.GetState(imageKey).SizeDelta.X,
                Is.EqualTo(756f).Within(Eps), "face_swap 2 → a/02");

            state = StageReducer.Apply(state, Cmd("show", "c1"), tuning);
            Assert.That(state.Nodes.GetState(imageKey).SizeDelta.X,
                Is.EqualTo(540f).Within(Eps), "인자 없는 show의 런타임 기본값은 e1");

            state = StageReducer.Apply(state, Cmd("pose", "c1", "b"), tuning);
            Assert.That(state.Nodes.GetState(imageKey).SizeDelta.X,
                Is.EqualTo(540f).Within(Eps), "pose 자체는 현재 런타임에서 sprite를 바꾸지 않는다");

            state = StageReducer.Apply(state, Cmd("face", "c1", "2"), tuning);
            Assert.That(state.Nodes.GetState(imageKey).SizeDelta.X,
                Is.EqualTo(864f).Within(Eps), "pose b 뒤 face 2 → b/02");

            Assert.That(state.Unhandled, Is.Empty, string.Join("\n", state.Unhandled));
        }

        [Test]
        public void 초상_상태를_바꾼_clone은_원본의_portrait_딕셔너리를_공유하지_않는다()
        {
            StageReducerTuning tuning = MakeTuning();
            tuning.PortraitDimensions = new PortraitDimensionsFileDto
            {
                entries =
                {
                    new PortraitDimensionDto { character = "x", variant = "x_a", emotion = "01", width = 500f, height = 1000f },
                    new PortraitDimensionDto { character = "x", variant = "x_a", emotion = "02", width = 700f, height = 1000f },
                },
            };
            AddPortraitNodes(tuning);

            StageState original = StageReducer.ApplyAll(
                StageReducer.CreateInitialState(tuning),
                new[] { Cmd("slot", "c1"), Cmd("cast", "c1", "x") },
                tuning);

            StageState changed = StageReducer.Apply(original, Cmd("face", "c1", "2"), tuning);

            Assert.That(original.TryGetPortrait("c1", out char originalVariant, out string originalEmotion), Is.True);
            Assert.That(changed.TryGetPortrait("c1", out char changedVariant, out string changedEmotion), Is.True);
            Assert.That(originalVariant, Is.EqualTo('a'));
            Assert.That(changedVariant, Is.EqualTo('a'));
            Assert.That(originalEmotion, Is.EqualTo("01"));
            Assert.That(changedEmotion, Is.EqualTo("02"));
            Assert.That(original.Nodes.GetState("c1/CharacterPortraitSprite_Image").SizeDelta.X,
                Is.EqualTo(540f).Within(Eps));
            Assert.That(changed.Nodes.GetState("c1/CharacterPortraitSprite_Image").SizeDelta.X,
                Is.EqualTo(756f).Within(Eps));
        }

        [Test]
        public void 초상_폭은_1080_상수가_아니라_현재_부모_높이에서_계산된다()
        {
            StageReducerTuning tuning = MakeTuning();
            tuning.PortraitDimensions = new PortraitDimensionsFileDto
            {
                entries =
                {
                    new PortraitDimensionDto { character = "x", variant = "x_a", emotion = "01", width = 500f, height = 1000f },
                },
            };
            AddPortraitNodes(tuning, fixedRootHeight: 720f);

            StageState state = StageReducer.ApplyAll(
                StageReducer.CreateInitialState(tuning),
                new[] { Cmd("slot", "c1"), Cmd("cast", "c1", "x") },
                tuning);

            Assert.That(state.Nodes.GetState("c1/CharacterPortraitSprite_Image").SizeDelta.X,
                Is.EqualTo(360f).Within(Eps));
            Assert.That(state.Nodes.GetState("c1/CharacterPortraitSprite_Image").SizeDelta.Y,
                Is.EqualTo(0f));
        }

        [Test]
        public void 초상_치수_항목이_없으면_정확한_identity가_Unhandled_이유에_남는다()
        {
            StageReducerTuning tuning = MakeTuning();
            tuning.PortraitDimensions = new PortraitDimensionsFileDto();
            AddPortraitNodes(tuning);

            StageState state = StageReducer.ApplyAll(
                StageReducer.CreateInitialState(tuning),
                new[] { Cmd("slot", "c1"), Cmd("cast", "c1", "missing") },
                tuning);

            Assert.That(state.Unhandled.Count, Is.EqualTo(1));
            Assert.That(state.Unhandled[0].Command.Name, Is.EqualTo("cast"));
            Assert.That(state.Unhandled[0].Reason, Does.Contain("missing/a/01"));
        }

        [Test]
        public void 치수_조회는_리졸버의_폴백_규약을_따른다()
        {
            PortraitDimensionsFileDto dims = new PortraitDimensionsFileDto
            {
                entries =
                {
                    new PortraitDimensionDto { character = "x", variant = "x_a", emotion = "01", width = 500f, height = 1000f },
                    new PortraitDimensionDto { character = "x", variant = "x_a", emotion = "02", width = 700f, height = 1000f },
                },
            };

            // 정확 일치.
            Assert.That(dims.TryGetAspect("x", 'A', "2", out float exact, out _), Is.True);
            Assert.That(exact, Is.EqualTo(0.7f).Within(1e-4f));

            // 없는 표정 → 기본('a', "01") 폴백 — 런타임 리졸버와 동일.
            Assert.That(dims.TryGetAspect("x", 'a', "07", out float fallback, out _), Is.True);
            Assert.That(fallback, Is.EqualTo(0.5f).Within(1e-4f));

            // 없는 캐릭터 → 이유와 함께 실패.
            Assert.That(dims.TryGetAspect("없는캐릭터", 'a', "01", out _, out string missing), Is.False);
            Assert.That(missing, Does.Contain("없다"));

            // 표정 코드 정규화 규약.
            Assert.That(PortraitEmotionCode.TryNormalize("1", out string one), Is.True);
            Assert.That(one, Is.EqualTo("01"));
            Assert.That(PortraitEmotionCode.TryNormalize(
                PortraitEmotionCode.ParseShowFaceAlias("e1"), out string aliasOne), Is.True);
            Assert.That(aliasOne, Is.EqualTo("01"));
            Assert.That(PortraitEmotionCode.TryNormalize("abc", out _), Is.False);
            Assert.That(PortraitEmotionCode.ParseShowFaceAlias("e1"), Is.EqualTo("1"));
            Assert.That(PortraitEmotionCode.ParseShowFaceAlias(""), Is.EqualTo("2"));
        }

        [Test]
        public void focus_오프셋은_base와_캐릭터_보정의_합이다()
        {
            Vec2 offset = FocusOffsetMath.Resolve(
                MakeFocusTuning(), "parkeunseol", "bust", Vec2.Zero);

            // base 820 + default -20 + bust 보정 10 = 810.
            Assert.That(offset.Y, Is.EqualTo(810f).Within(Eps));

            Vec2 noEntry = FocusOffsetMath.Resolve(MakeFocusTuning(), "unknown", "bust", Vec2.Zero);
            Assert.That(noEntry.Y, Is.EqualTo(820f).Within(Eps));
        }
    }
}