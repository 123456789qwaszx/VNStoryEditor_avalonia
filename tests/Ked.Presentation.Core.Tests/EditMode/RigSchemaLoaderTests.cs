using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// 덤프 → 트리 변환의 규율. 여기서는 합성 덤프를 쓴다 —
    /// 실제 덤프와 실제 리그의 대조는 UnityParity 하네스의 일이다.
    /// </summary>
    public sealed class RigSchemaLoaderTests
    {
        private const float Eps = 1e-4f;

        // ── 합성 덤프 ────────────────────────────────────────────────

        private static Float2Dto F2(float x, float y) => new() { x = x, y = y };
        private static Float3Dto F3(float x, float y, float z) => new() { x = x, y = y, z = z };

        /// <summary>스트레치 풀 기본값 노드. 필요한 필드만 덮어 쓴다.</summary>
        private static RigSchemaNodeDto Node(string id, string parent)
        {
            return new RigSchemaNodeDto
            {
                id = id,
                parent = parent,
                anchoredPosition = F2(0f, 0f),
                anchorMin = F2(0f, 0f),
                anchorMax = F2(1f, 1f),
                pivot = F2(0.5f, 0.5f),
                sizeDelta = F2(0f, 0f),
                localScale = F3(1f, 1f, 1f),
                localEulerAngles = F3(0f, 0f, 0f),
                measuredRectSize = F2(0f, 0f),
            };
        }

        /// <summary>
        /// 루트 + 자식 둘의 최소 리그. parent 표기는 실제 덤프 규약 그대로다:
        /// __root만 빈 문자열이고, 그 자식들은 "__root"를 명시한다.
        /// </summary>
        private static RigSchemaRigDto SampleRig(string rigKind = "character")
        {
            RigSchemaNodeDto track = Node("Track", RigSchemaLoader.RootKey);
            track.anchoredPosition = F2(10f, 0f);

            RigSchemaNodeDto scale = Node("Scale", "Track");
            scale.pivot = F2(0.5f, 0f);
            scale.localScale = F3(2f, 2f, 1f);

            return new RigSchemaRigDto
            {
                rigKind = rigKind,
                sourcePrefab = "Assets/Sample.prefab",
                nodes = new List<RigSchemaNodeDto>
                {
                    Node(RigSchemaLoader.RootKey, ""),
                    track,
                    scale,
                },
            };
        }

        private static RigSchemasFileDto SampleFile(params RigSchemaRigDto[] rigs)
        {
            return new RigSchemasFileDto
            {
                capturedUnderParentSize = F2(1920f, 1080f),
                rigs = new List<RigSchemaRigDto>(rigs),
            };
        }

        // ── 정상 경로 ────────────────────────────────────────────────

        [Test]
        public void 덤프_순서대로_부모_자식_트리가_선다()
        {
            RectNodeTree tree = RigSchemaLoader.BuildTree(SampleFile(SampleRig()), "character");

            Assert.That(tree.Count, Is.EqualTo(3));
            Assert.That(tree.GetParentKey(RigSchemaLoader.RootKey), Is.Null, "리그 루트는 루트 공간 직속");
            Assert.That(tree.GetParentKey("Track"), Is.EqualTo(RigSchemaLoader.RootKey));
            Assert.That(tree.GetParentKey("Scale"), Is.EqualTo("Track"));
        }

        [Test]
        public void 빈_parent만_루트_공간_직속이다()
        {
            // 덤프 규약 두 겹을 구분해야 한다:
            //   parent ""       → 루트 공간 직속 (리그 루트 자신)
            //   parent "__root" → 리그 루트의 자식
            // 둘을 같이 취급하면 리그 루트가 사슬에서 빠져 좌표가 조용히 어긋난다.
            RectNodeTree tree = RigSchemaLoader.BuildTree(SampleFile(SampleRig()), "character");

            Assert.That(tree.GetParentKey(RigSchemaLoader.RootKey), Is.Null);
            Assert.That(tree.GetParentKey("Track"), Is.EqualTo(RigSchemaLoader.RootKey));
        }

        [Test]
        public void capturedUnderParentSize가_루트_공간이_된다()
        {
            RectNodeTree tree = RigSchemaLoader.BuildTree(SampleFile(SampleRig()), "character");

            // 이게 어긋나면 스트레치 노드의 rect 크기가 전부 어긋난다.
            Assert.That(tree.RootSpace.Size, Is.EqualTo(new Vec2(1920f, 1080f)));
            Assert.That(tree.RootSpace.Pivot, Is.EqualTo(Vec2.Half), "익스포터가 가운데 pivot 밑에서 캡처했다");
        }

        [Test]
        public void 세운_트리의_좌표가_기대값이다()
        {
            RectNodeTree tree = RigSchemaLoader.BuildTree(SampleFile(SampleRig()), "character");

            // __root(스트레치 풀, 변위 0) → Track(+10, 0) → Scale(바닥 pivot)
            // 바닥 pivot이므로 Scale의 로컬 원점은 부모 바닥 = -540.
            Vec3 p = tree.TransformPoint("Scale", Vec3.Zero);

            Assert.That(p.X, Is.EqualTo(10f).Within(Eps));
            Assert.That(p.Y, Is.EqualTo(-540f).Within(Eps));
        }

        [Test]
        public void keyPrefix가_노드_키와_부모_참조_양쪽에_붙는다()
        {
            RectNodeTree tree = RigSchemaLoader.BuildTree(SampleFile(SampleRig()), "character", "c1/");

            Assert.That(tree.Contains("c1/" + RigSchemaLoader.RootKey), Is.True);
            Assert.That(tree.Contains("c1/Track"), Is.True);
            Assert.That(tree.Contains("Track"), Is.False, "접두사 없는 키가 남으면 안 된다");

            // 부모 참조에도 붙어야 사슬이 이어진다.
            Assert.That(tree.GetParentKey("c1/Scale"), Is.EqualTo("c1/Track"));
            Assert.That(tree.GetParentKey("c1/Track"), Is.EqualTo("c1/" + RigSchemaLoader.RootKey));
        }

        [Test]
        public void ToState는_필드를_그대로_옮긴다()
        {
            RigSchemaNodeDto node = Node("n", "");
            node.anchoredPosition = F2(1f, 2f);
            node.anchorMin = F2(0.25f, 0.1f);
            node.anchorMax = F2(0.75f, 0.9f);
            node.pivot = F2(0.5f, 0f);
            node.sizeDelta = F2(-40f, 20f);
            node.localScale = F3(1.4f, 0.8f, 1f);
            node.localEulerAngles = F3(0f, 0f, 25f);

            RectNodeState state = RigSchemaLoader.ToState(node);

            Assert.That(state.AnchoredPosition, Is.EqualTo(new Vec2(1f, 2f)));
            Assert.That(state.AnchorMin, Is.EqualTo(new Vec2(0.25f, 0.1f)));
            Assert.That(state.AnchorMax, Is.EqualTo(new Vec2(0.75f, 0.9f)));
            Assert.That(state.Pivot, Is.EqualTo(new Vec2(0.5f, 0f)));
            Assert.That(state.SizeDelta, Is.EqualTo(new Vec2(-40f, 20f)));
            Assert.That(state.LocalScale, Is.EqualTo(new Vec3(1.4f, 0.8f, 1f)));
            Assert.That(state.LocalEulerAngles, Is.EqualTo(new Vec3(0f, 0f, 25f)));
        }

        [Test]
        public void 여러_리그_중_고른_것만_세운다()
        {
            RigSchemasFileDto file = SampleFile(SampleRig("character"), SampleRig("background"));

            RectNodeTree tree = RigSchemaLoader.BuildTree(file, "background");

            Assert.That(tree.Count, Is.EqualTo(3));
        }

        // ── 거부 규율 ────────────────────────────────────────────────

        [Test]
        public void 모르는_rigKind는_있는_것을_나열하며_거부한다()
        {
            RigSchemasFileDto file = SampleFile(SampleRig("character"), SampleRig("overlay"));

            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => RigSchemaLoader.BuildTree(file, "charater")); // 오타

            // "비슷한 이름"으로 잇지 않는다. 대신 무엇이 있었는지 말한다.
            Assert.That(ex.Message, Does.Contain("character"));
            Assert.That(ex.Message, Does.Contain("overlay"));
        }

        [Test]
        public void 빈_덤프는_거부한다()
        {
            Assert.Throws<ArgumentNullException>(
                () => RigSchemaLoader.BuildTree(null, "character"));

            Assert.Throws<ArgumentException>(
                () => RigSchemaLoader.BuildTree(new RigSchemasFileDto(), "character"));

            RigSchemasFileDto noSize = SampleFile(SampleRig());
            noSize.capturedUnderParentSize = null;

            Assert.Throws<ArgumentException>(
                () => RigSchemaLoader.BuildTree(noSize, "character"));
        }

        [Test]
        public void 노드가_없는_리그는_거부한다()
        {
            RigSchemaRigDto empty = new() { rigKind = "character", nodes = new List<RigSchemaNodeDto>() };

            Assert.Throws<ArgumentException>(
                () => RigSchemaLoader.BuildTree(SampleFile(empty), "character"));
        }

        [Test]
        public void id가_없는_노드는_거부한다()
        {
            RigSchemaRigDto rig = SampleRig();
            rig.nodes[1].id = "";

            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => RigSchemaLoader.BuildTree(SampleFile(rig), "character"));

            Assert.That(ex.Message, Does.Contain("[1]"), "몇 번째 노드인지 알려야 한다");
        }

        [Test]
        public void 빈_트랜스폼_필드는_0으로_떨어지지_않고_예외다()
        {
            // System.Text.Json의 IncludeFields 누락이 이 모양으로 나타난다.
            // 조용히 0으로 채우면 좌표가 전부 원점으로 접힌 채 통과한다.
            RigSchemaRigDto rig = SampleRig();
            rig.nodes[2].pivot = null;

            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => RigSchemaLoader.BuildTree(SampleFile(rig), "character"));

            Assert.That(ex.Message, Does.Contain("Scale"));
            Assert.That(ex.Message, Does.Contain("character"));
        }

        [Test]
        public void 부모가_자식보다_뒤에_오면_거부한다()
        {
            // 덤프 규약은 "부모 먼저"다. 어긋나면 트리가 조용히 반쪽으로 서는 대신 터진다.
            RigSchemaRigDto rig = SampleRig();
            (rig.nodes[1], rig.nodes[2]) = (rig.nodes[2], rig.nodes[1]);

            Assert.Throws<ArgumentException>(
                () => RigSchemaLoader.BuildTree(SampleFile(rig), "character"));
        }
    }
}
