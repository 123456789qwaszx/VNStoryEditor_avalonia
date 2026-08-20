using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// 하네스 부품 둘: 원문 추출기와 상태 비교기.
    ///
    /// 원문 표본은 실제 에피소드 모양 그대로다 — Setup 노드 + Story 노드,
    /// 준비 커맨드 / 대사 / 꼬리 커맨드, 주석, BOM, 태그 없음.
    /// </summary>
    public sealed class EquivalenceHarnessCoreTests
    {
        // 실제 Story_StellaEvent01_01.yarn의 구조를 축소한 것.
        private const string SampleYarn =
            "﻿title: Setup_Sample\n" +
            "---\n" +
            "\n" +
            "<<bg_spawn bg1 fl5>>\n" +
            "<<slot c1>>\n" +
            "\n" +
            "=== \n" +
            "\n" +
            "title: Story_Sample\n" +
            "---\n" +
            "\n" +
            "<<beat Setup_Sample>>\n" +
            "<<cast c1 bandi a 3>>\n" +
            "<<actor @4 c1>>\n" +
            "//<<mirror @4>>\n" +
            "<<place_center @4 bust 24fr>>\n" +
            "\n" +
            "첫 번째 대사다.\n" +
            "\n" +
            "<<left @4 3u>>\n" +
            "<<fade_in @4>>\n" +
            "\n" +
            "두 번째 대사다.\n" +
            "\n" +
            "<<shot_reset>>\n" +
            "===\n";

        private static List<YarnNodeGroups> ExtractSample(List<string> warnings = null)
            => YarnCommandTextExtractor.ExtractNodes(SampleYarn, "sample.yarn", warnings);

        // ── 추출기: 노드 분리 ────────────────────────────────────────

        [Test]
        public void 한_파일의_Setup과_Story_노드를_나눈다()
        {
            List<YarnNodeGroups> nodes = ExtractSample();

            Assert.That(nodes.Count, Is.EqualTo(2));
            Assert.That(nodes[0].NodeName, Is.EqualTo("Setup_Sample"));
            Assert.That(nodes[1].NodeName, Is.EqualTo("Story_Sample"));
        }

        [Test]
        public void BOM이_노드_이름을_더럽히지_않는다()
        {
            // 파일 선두 BOM은 공백이 아니라 Trim에 안 걸린다.
            Assert.That(ExtractSample()[0].NodeName, Is.EqualTo("Setup_Sample"));
        }

        [Test]
        public void 대사가_없는_노드는_꼬리_그룹으로만_남는다()
        {
            YarnNodeGroups setup = ExtractSample()[0];

            Assert.That(setup.Groups.Count, Is.EqualTo(1));
            Assert.That(setup.Groups[0].LineText, Is.Null, "꼬리 그룹은 대사가 없다");
            Assert.That(setup.Groups[0].Commands.Select(c => c.Name),
                Is.EqualTo(new[] { "bg_spawn", "slot" }));
        }

        // ── 추출기: 라인 경계 ────────────────────────────────────────

        [Test]
        public void 커맨드는_다음_대사까지의_그룹에_모인다()
        {
            YarnNodeGroups story = ExtractSample()[1];

            // 대사 2개 + 꼬리 1개.
            Assert.That(story.Groups.Count, Is.EqualTo(3));

            Assert.That(story.Groups[0].LineText, Is.EqualTo("첫 번째 대사다."));
            Assert.That(story.Groups[0].Commands.Select(c => c.Name),
                Is.EqualTo(new[] { "beat", "cast", "actor", "place_center" }));

            Assert.That(story.Groups[1].LineText, Is.EqualTo("두 번째 대사다."));
            Assert.That(story.Groups[1].Commands.Select(c => c.Name),
                Is.EqualTo(new[] { "left", "fade_in" }));

            Assert.That(story.Groups[2].LineText, Is.Null);
            Assert.That(story.Groups[2].Commands.Select(c => c.Name), Is.EqualTo(new[] { "shot_reset" }));
        }

        [Test]
        public void 주석_처리된_커맨드는_빠진다()
        {
            YarnNodeGroups story = ExtractSample()[1];

            Assert.That(story.Groups[0].Commands.Any(c => c.Name == "mirror"), Is.False);
        }

        [Test]
        public void 출처에_파일명과_행_번호가_실린다()
        {
            // Unhandled 목록을 사람이 열어 보고 고칠 수 있어야 한다.
            StageCommand cast = ExtractSample()[1].Groups[0].Commands[1];

            Assert.That(cast.Name, Is.EqualTo("cast"));
            Assert.That(cast.Source, Does.StartWith("sample.yarn:"));
        }

        [Test]
        public void 인자가_순서대로_실린다()
        {
            StageCommand place = ExtractSample()[1].Groups[0].Commands[3];

            Assert.That(place.Name, Is.EqualTo("place_center"));
            Assert.That(place.Args, Is.EqualTo(new[] { "@4", "bust", "24fr" }));
        }

        // ── 추출기: 라인 ID ──────────────────────────────────────────

        [Test]
        public void 이_프로젝트_원문에는_라인_ID가_없다()
        {
            // 실측: 8개 yarn 파일 전부 #line 태그 0건.
            // 하네스는 ID 매칭이 아니라 순서 커서로 가야 한다.
            foreach (YarnLineGroup group in ExtractSample().SelectMany(n => n.Groups))
                Assert.That(group.LineId, Is.Null);
        }

        [Test]
        public void 라인_ID는_접두사를_포함한_전체다()
        {
            // Yarn 런타임의 라인 ID가 "line:316f20c1" 형태다 —
            // 접두사를 벗겨 저장하면 하네스의 시간표 조회가 전 라인에서 빗나간다.
            const string tagged =
                "title: N\n---\n대사입니다. #line:316f20c1\n===\n";

            List<YarnLineGroup> groups = YarnCommandTextExtractor.Extract(tagged, "t.yarn");

            Assert.That(groups[0].LineId, Is.EqualTo("line:316f20c1"));
            Assert.That(groups[0].LineText, Is.EqualTo("대사입니다."), "태그는 대사에서 빠진다");
        }

        // ── 추출기: 커맨드 파싱 ──────────────────────────────────────

        [Test]
        public void 따옴표_인자는_한_덩어리다()
        {
            Assert.That(
                YarnCommandTextExtractor.TryParseCommand("<<say c1 \"안녕 하세요\" 2s>>", "s:1",
                    out StageCommand command),
                Is.True);

            Assert.That(command.Args, Is.EqualTo(new[] { "c1", "안녕 하세요", "2s" }));
        }

        [Test]
        public void 커맨드가_아닌_줄은_거부한다()
        {
            Assert.That(YarnCommandTextExtractor.TryParseCommand("대사입니다", "s:1", out _), Is.False);
            Assert.That(YarnCommandTextExtractor.TryParseCommand("<<>>", "s:1", out _), Is.False);
            Assert.That(YarnCommandTextExtractor.TryParseCommand("<<미완성", "s:1", out _), Is.False);
        }

        [Test]
        public void 인자_없는_커맨드도_읽는다()
        {
            // 실제 원문에 <<36fr>> 같은 것이 있다 — 리듀서가 Unhandled로 처리할 몫이다.
            Assert.That(
                YarnCommandTextExtractor.TryParseCommand("<<36fr>>", "s:1", out StageCommand command),
                Is.True);

            Assert.That(command.Name, Is.EqualTo("36fr"));
            Assert.That(command.Args, Is.Empty);
        }

        // ── 추출기: 선형성 경고 ──────────────────────────────────────

        [Test]
        public void 분기와_옵션은_경고로_소리를_낸다()
        {
            const string branching =
                "title: N\n---\n" +
                "<<if $flag>>\n" +
                "<<jump Other>>\n" +
                "-> 선택지\n" +
                "대사.\n" +
                "===\n";

            List<string> warnings = new();
            YarnCommandTextExtractor.Extract(branching, "b.yarn", warnings);

            // 이 파일은 이 추출기로 접으면 안 된다는 신호다.
            Assert.That(warnings.Count(w => w.Contains("분기/제어")), Is.EqualTo(2));
            Assert.That(warnings.Count(w => w.Contains("옵션")), Is.EqualTo(1));
        }

        [Test]
        public void set은_경고하지_않고_흘려보낸다()
        {
            // 무대 상태와 무관하고 분기하지도 않는다 — 리듀서의 Unhandled로 남으면 된다.
            List<string> warnings = new();

            List<YarnLineGroup> groups = YarnCommandTextExtractor.Extract(
                "title: N\n---\n<<set $a to 1>>\n대사.\n===\n", "s.yarn", warnings);

            Assert.That(warnings, Is.Empty);
            Assert.That(groups[0].Commands[0].Name, Is.EqualTo("set"));
        }

        // ── 비교기 ───────────────────────────────────────────────────

        private static StageState StateWith(Vec2 position, float alpha = 1f)
        {
            StageState state = new(RectSpace.Centered(1920f, 1080f));
            state.Nodes.Add("c1/Track", null, RectNodeState.StretchFull.WithAnchoredPosition(position));
            state.SetAlpha("c1/Track", alpha);
            return state;
        }

        [Test]
        public void 같은_상태는_등가다()
        {
            StageStateComparer.Result result =
                StageStateComparer.Compare(StateWith(new Vec2(10f, 20f)), StateWith(new Vec2(10f, 20f)));

            Assert.That(result.IsEquivalent, Is.True, result.ToString());
            Assert.That(result.ComparedNodes, Is.EqualTo(1));
        }

        [Test]
        public void 위치_ε_경계에서_갈린다()
        {
            // 0.05px는 잡음, 0.2px는 신호. ε = 0.1px가 그 사이에 있다.
            Assert.That(
                StageStateComparer.Compare(
                    StateWith(new Vec2(10f, 20f)), StateWith(new Vec2(10.05f, 20f))).IsEquivalent,
                Is.True, "0.05px는 등가");

            Assert.That(
                StageStateComparer.Compare(
                    StateWith(new Vec2(10f, 20f)), StateWith(new Vec2(10.2f, 20f))).IsEquivalent,
                Is.False, "0.2px는 불일치");
        }

        [Test]
        public void alpha도_비교한다()
        {
            StageStateComparer.Result result = StageStateComparer.Compare(
                StateWith(Vec2.Zero, alpha: 1f), StateWith(Vec2.Zero, alpha: 0f));

            Assert.That(result.IsEquivalent, Is.False);
            Assert.That(result.Mismatches[0], Does.Contain("alpha"));
        }

        [Test]
        public void 각도는_360도_순환으로_본다()
        {
            StageState folded = new(RectSpace.Centered(1920f, 1080f));
            folded.Nodes.Add("n", null, RectNodeState.StretchFull.WithLocalEuler(new Vec3(0f, 0f, 0f)));

            StageState captured = new(RectSpace.Centered(1920f, 1080f));
            captured.Nodes.Add("n", null, RectNodeState.StretchFull.WithLocalEuler(new Vec3(0f, 0f, 360f)));

            Assert.That(StageStateComparer.Compare(folded, captured).IsEquivalent, Is.True,
                "유니티가 오일러를 [0,360)으로 정규화해 돌려주기도 한다");
        }

        [Test]
        public void 캡처에만_있는_노드는_불일치다()
        {
            // 실제 무대에 있는데 접지 못한 노드 — 폴드에 빠진 축이다.
            StageState folded = new(RectSpace.Centered(1920f, 1080f));

            StageStateComparer.Result result = StageStateComparer.Compare(folded, StateWith(Vec2.Zero));

            Assert.That(result.IsEquivalent, Is.False);
            Assert.That(result.Mismatches[0], Does.Contain("캡처에는 있는데"));
        }

        [Test]
        public void 접힘_전용_노드는_개수로_보고한다()
        {
            // 캡처가 안 덮는 축이다. 침묵하지 않되 불일치로 세지도 않는다.
            StageState captured = new(RectSpace.Centered(1920f, 1080f));

            StageStateComparer.Result result = StageStateComparer.Compare(StateWith(Vec2.Zero), captured);

            Assert.That(result.IsEquivalent, Is.True);
            Assert.That(result.FoldOnlyNodes, Is.EqualTo(1));
            Assert.That(result.ComparedNodes, Is.EqualTo(0));
        }

        [Test]
        public void 샷_축도_비교한다()
        {
            StageState folded = new(RectSpace.Centered(1920f, 1080f))
            {
                Shot = new ShotIntentState(3f, new Vec2(80f, 0f), Vec2.Zero),
            };

            StageState captured = new(RectSpace.Centered(1920f, 1080f))
            {
                Shot = new ShotIntentState(3f, new Vec2(90f, 0f), Vec2.Zero),
            };

            StageStateComparer.Result result = StageStateComparer.Compare(folded, captured);

            Assert.That(result.IsEquivalent, Is.False);
            Assert.That(result.Mismatches[0], Does.Contain("shot.panInRigSpace"));
        }

        // ── 하네스 축소판 왕복 ───────────────────────────────────────

        [Test]
        public void 원문을_두_벌_접으면_등가로_판정된다()
        {
            // 하네스의 축소판: 원문 추출 → 두 벌 폴드 → 비교기 등가 판정.
            // 파이프라인이 이어져 있는지 보는 것이 목적이다.
            StageReducerTuning tuning = HarnessTuning();

            List<YarnLineGroup> groups = YarnCommandTextExtractor.Extract(SampleYarn, "sample.yarn");
            List<StageCommand> commands = groups.SelectMany(g => g.Commands).ToList();

            StageState a = StageReducer.ApplyAll(StageReducer.CreateInitialState(tuning), commands, tuning);
            StageState b = StageReducer.ApplyAll(StageReducer.CreateInitialState(tuning), commands, tuning);

            StageStateComparer.Result result = StageStateComparer.Compare(a, b);

            Assert.That(result.IsEquivalent, Is.True, result.ToString());
            Assert.That(result.ComparedNodes, Is.GreaterThan(0), "슬롯이 실제로 세워져야 유의미하다");
        }

        [Test]
        public void 접는_도중_실패해도_판정은_계속된다()
        {
            // 원문에는 아직 못 접는 커맨드가 섞여 있다(bg_spawn·place_center·beat…).
            // 그것들이 Unhandled로 남을 뿐, 접힌 상태 자체는 성립해야 한다.
            StageReducerTuning tuning = HarnessTuning();

            List<StageCommand> commands = YarnCommandTextExtractor
                .Extract(SampleYarn, "sample.yarn")
                .SelectMany(g => g.Commands)
                .ToList();

            StageState state = StageReducer.ApplyAll(
                StageReducer.CreateInitialState(tuning), commands, tuning);

            Assert.That(state.HasSlot("c1"), Is.True, "slot은 접혔다");
            Assert.That(state.Unhandled.Count, Is.GreaterThan(0), "못 접은 것은 기록으로 남았다");
            Assert.That(state.Unhandled.Any(u => u.Command.Name == "bg_spawn"), Is.True);
        }

        private static StageReducerTuning HarnessTuning()
        {
            RigSchemaRigDto rig = new()
            {
                rigKind = "character",
                nodes = new List<RigSchemaNodeDto>
                {
                    NodeDto("__root", ""),
                    NodeDto("CharSlot_Track", "__root"),
                    NodeDto("CharSlot_Track_X", "CharSlot_Track"),
                    NodeDto("CharacterPortraitSprite_Root", "CharSlot_Track_X"),
                },
            };

            return new StageReducerTuning
            {
                RigSchemas = new RigSchemasFileDto
                {
                    capturedUnderParentSize = new Float2Dto { x = 1920f, y = 1080f },
                    rigs = new List<RigSchemaRigDto> { rig },
                },
                ReferenceStageWidth = 1920f,
                BaseResolution = new Vec2(1920f, 1080f),
                RoleAnchors = new RoleAnchorTuningBodyDto(),
            };
        }

        private static RigSchemaNodeDto NodeDto(string id, string parent) => new()
        {
            id = id,
            parent = parent,
            anchoredPosition = new Float2Dto { x = 0f, y = 0f },
            anchorMin = new Float2Dto { x = 0f, y = 0f },
            anchorMax = new Float2Dto { x = 1f, y = 1f },
            pivot = new Float2Dto { x = 0.5f, y = 0.5f },
            sizeDelta = new Float2Dto { x = 0f, y = 0f },
            localScale = new Float3Dto { x = 1f, y = 1f, z = 1f },
            localEulerAngles = new Float3Dto { x = 0f, y = 0f, z = 0f },
            measuredRectSize = new Float2Dto { x = 0f, y = 0f },
        };
    }
}
