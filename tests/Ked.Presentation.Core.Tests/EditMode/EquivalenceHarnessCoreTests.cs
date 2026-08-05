using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>U14-b 코어 부품: yarn 원문 추출기 + 상태 비교기.</summary>
    public sealed class EquivalenceHarnessCoreTests
    {
        // ── YarnCommandTextExtractor ─────────────────────────────────

        private const string SampleYarn = @"title: Pres_sample
---
// 준비 커맨드
<<bg_spawn bg_main class_day>>
<<slot c1>>
<<show c1 e1>>

박은설: 자, 자~ 모두 자리로~ #line:0a1b2c

<<left c1 3u 8fr>>
<<shot_zoom 2.5>>
학생들: 우우~ #line:0d3e4f

<<shot_reset>>
===";

        [Test]
        public void 원문에서_라인_경계_단위로_커맨드를_뽑는다()
        {
            List<string> warnings = new();
            List<YarnLineGroup> groups = YarnCommandTextExtractor.Extract(SampleYarn, "sample.yarn", warnings);

            Assert.That(warnings, Is.Empty);
            Assert.That(groups.Count, Is.EqualTo(3)); // 대사 2 + 꼬리 커맨드 그룹 1

            Assert.That(groups[0].Commands.Select(c => c.Name),
                Is.EqualTo(new[] { "bg_spawn", "slot", "show" }));
            Assert.That(groups[0].LineText, Does.StartWith("박은설:"));
            // 접두사 포함이 규약이다 — Yarn 런타임의 CurrentLineId와 그대로 맞아야 한다.
            Assert.That(groups[0].LineId, Is.EqualTo("line:0a1b2c"));

            Assert.That(groups[1].Commands.Select(c => c.Name),
                Is.EqualTo(new[] { "left", "shot_zoom" }));
            Assert.That(groups[1].Commands[0].Args, Is.EqualTo(new[] { "c1", "3u", "8fr" }));

            Assert.That(groups[2].LineText, Is.Null, "꼬리 그룹");
            Assert.That(groups[2].Commands.Single().Name, Is.EqualTo("shot_reset"));
        }

        [Test]
        public void 출처가_파일과_행_번호로_남는다()
        {
            List<YarnLineGroup> groups = YarnCommandTextExtractor.Extract(SampleYarn, "sample.yarn");

            Assert.That(groups[0].Commands[0].Source, Does.StartWith("sample.yarn:"));
        }

        [Test]
        public void 주석과_헤더는_무시된다()
        {
            List<YarnLineGroup> groups = YarnCommandTextExtractor.Extract(
                "title: T\nposition: 1,2\n---\n<<slot c1>> // 뒤 주석\n대사 한 줄\n===\n",
                "t.yarn");

            Assert.That(groups.Count, Is.EqualTo(1));
            Assert.That(groups[0].Commands.Single().Name, Is.EqualTo("slot"));
        }

        [Test]
        public void 따옴표_인자는_한_덩이다()
        {
            Assert.IsTrue(YarnCommandTextExtractor.TryParseCommand(
                "<<debug_log \"hello world\" x>>", "t:1", out StageCommand cmd));

            Assert.That(cmd.Args, Is.EqualTo(new[] { "hello world", "x" }));
        }

        [Test]
        public void 한_파일의_여러_노드가_따로_뽑힌다()
        {
            // Pres 파일의 실제 모양: Set 노드(커맨드 전용) + Pres 노드(라인별 그룹).
            const string twoNodes = @"title: Set_sample
---
<<bg_spawn bg_main class_day>>
<<slot c1>>
===
title: Pres_sample
---
<<show c1 e1>>
첫 서브 라인 #line:p1
<<left c1 1u>>
둘째 서브 라인 #line:p2
===";

            List<YarnNodeGroups> nodes = YarnCommandTextExtractor.ExtractNodes(twoNodes, "pres.yarn");

            Assert.That(nodes.Count, Is.EqualTo(2));
            Assert.That(nodes[0].NodeName, Is.EqualTo("Set_sample"));
            Assert.That(nodes[0].Groups.Single().LineText, Is.Null, "커맨드 전용 노드는 꼬리 그룹 하나");
            Assert.That(nodes[0].Groups.Single().Commands.Count, Is.EqualTo(2));

            Assert.That(nodes[1].NodeName, Is.EqualTo("Pres_sample"));
            Assert.That(nodes[1].Groups.Count, Is.EqualTo(2));
            Assert.That(nodes[1].Groups[0].LineId, Is.EqualTo("line:p1"));
            Assert.That(nodes[1].Groups[1].Commands.Single().Name, Is.EqualTo("left"));
        }

        [Test]
        public void 분기_커맨드는_경고를_남긴다()
        {
            List<string> warnings = new();

            YarnCommandTextExtractor.Extract(
                "title: T\n---\n<<jump SomeNode>>\n대사\n===\n", "t.yarn", warnings);

            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("jump"));
        }

        // ── StageStateComparer ───────────────────────────────────────

        private static StageState MakeState(float trackX, float alpha, float zoom)
        {
            StageState state = new StageState(RectSpace.Centered(1920f, 1080f));
            state.Nodes.Add("c1/__root", null, RectNodeState.StretchFull);
            state.Nodes.Add("c1/CharSlot_Track", "c1/__root",
                RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(trackX, 0f)));
            state.SetAlpha("c1/__root", alpha);
            state.Shot = new ShotIntentState(zoom, Vec2.Zero, Vec2.Zero);
            return state;
        }

        [Test]
        public void 같은_상태는_등가다()
        {
            StageStateComparer.Result result =
                StageStateComparer.Compare(MakeState(120f, 1f, 2.5f), MakeState(120f, 1f, 2.5f));

            Assert.That(result.IsEquivalent, Is.True, string.Join("\n", result.Mismatches));
            Assert.That(result.ComparedNodes, Is.EqualTo(2));
        }

        [Test]
        public void 위치_어긋남은_노드와_필드가_찍힌다()
        {
            StageStateComparer.Result result =
                StageStateComparer.Compare(MakeState(120f, 1f, 0f), MakeState(121f, 1f, 0f));

            Assert.That(result.IsEquivalent, Is.False);
            Assert.That(result.Mismatches.Single(), Does.Contain("c1/CharSlot_Track"));
            Assert.That(result.Mismatches.Single(), Does.Contain("anchoredPosition"));
        }

        [Test]
        public void 위치_ε는_0_1px다()
        {
            // 잡음 상한 안쪽은 등가, 바깥은 불일치 (b-1 ε 정책).
            Assert.That(StageStateComparer.Compare(
                MakeState(120f, 1f, 0f), MakeState(120.05f, 1f, 0f)).IsEquivalent, Is.True);

            Assert.That(StageStateComparer.Compare(
                MakeState(120f, 1f, 0f), MakeState(120.2f, 1f, 0f)).IsEquivalent, Is.False);
        }

        [Test]
        public void alpha와_shot_어긋남도_잡는다()
        {
            StageStateComparer.Result alpha =
                StageStateComparer.Compare(MakeState(0f, 1f, 0f), MakeState(0f, 0f, 0f));
            Assert.That(alpha.Mismatches.Single(), Does.Contain("alpha"));

            StageStateComparer.Result shot =
                StageStateComparer.Compare(MakeState(0f, 1f, 0f), MakeState(0f, 1f, 2.5f));
            Assert.That(shot.Mismatches.Single(), Does.Contain("shot.zoom"));
        }

        [Test]
        public void 각도는_360_순환으로_비교한다()
        {
            StageState a = MakeState(0f, 1f, 0f);
            StageState b = MakeState(0f, 1f, 0f);

            a.Nodes.SetState("c1/CharSlot_Track",
                a.Nodes.GetState("c1/CharSlot_Track").WithLocalEuler(new Vec3(0f, 0f, 359.999f)));
            b.Nodes.SetState("c1/CharSlot_Track",
                b.Nodes.GetState("c1/CharSlot_Track").WithLocalEuler(new Vec3(0f, 0f, 0f)));

            Assert.That(StageStateComparer.Compare(a, b).IsEquivalent, Is.True);
        }

        [Test]
        public void 캡처에만_있는_노드는_불일치_접힘_전용은_개수다()
        {
            StageState folded = MakeState(0f, 1f, 0f);
            StageState captured = MakeState(0f, 1f, 0f);

            captured.Nodes.Add("c1/GhostNode", "c1/__root", RectNodeState.StretchFull);
            folded.Nodes.Add("c1/FoldOnly", "c1/__root", RectNodeState.StretchFull);

            StageStateComparer.Result result = StageStateComparer.Compare(folded, captured);

            Assert.That(result.Mismatches.Single(), Does.Contain("GhostNode"));
            Assert.That(result.SkippedFoldOnlyNodes, Is.EqualTo(1));
        }

        // ── 폴드 → 비교 왕복 (하네스 축소판) ─────────────────────────

        [Test]
        public void 접은_상태는_같은_열을_접은_상태와_등가다()
        {
            StageReducerTuning tuning = new StageReducerTuning
            {
                RigSchemas = MiniSchemas(),
                ReferenceStageWidth = 1920f,
                BaseResolution = new Vec2(1920f, 1080f),
            };

            List<YarnLineGroup> groups = YarnCommandTextExtractor.Extract(
                "title: T\n---\n<<slot c1>>\n<<left c1 3u>>\n대사 #line:aa\n<<shot_zoom 2>>\n대사2 #line:bb\n===\n",
                "t.yarn");

            StageState a = StageReducer.CreateInitialState(tuning);
            StageState b = StageReducer.CreateInitialState(tuning);

            foreach (YarnLineGroup group in groups)
            {
                a = StageReducer.ApplyAll(a, group.Commands, tuning);
                b = StageReducer.ApplyAll(b, group.Commands, tuning);
            }

            StageStateComparer.Result result = StageStateComparer.Compare(a, b);

            Assert.That(result.IsEquivalent, Is.True, string.Join("\n", result.Mismatches));
            Assert.That(a.Nodes.GetState("c1/CharSlot_Track_X").AnchoredPosition.X,
                Is.EqualTo(-120f).Within(1e-3f));
        }

        private static RigSchemasFileDto MiniSchemas()
        {
            Float2Dto F2(float x, float y) => new Float2Dto { x = x, y = y };
            Float3Dto F3(float x, float y, float z) => new Float3Dto { x = x, y = y, z = z };

            string[] chain = { "__root", "CharSlot_Track", "CharSlot_Track_X", "CharSlot_Track_Y" };
            List<RigSchemaNodeDto> nodes = new();

            for (int i = 0; i < chain.Length; i++)
            {
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
    }
}
