using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    public sealed class RigSchemaLoaderTests
    {
        private const float Eps = 1e-3f;

        private static Float2Dto F2(float x, float y) => new Float2Dto { x = x, y = y };
        private static Float3Dto F3(float x, float y, float z) => new Float3Dto { x = x, y = y, z = z };

        private static RigSchemaNodeDto MakeNode(string id, string parent)
        {
            // 익스포터의 StretchFull 캡처와 같은 모양.
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
                measuredRectSize = F2(1920f, 1080f),
            };
        }

        private static RigSchemasFileDto MakeFile()
        {
            RigSchemaNodeDto bottomPivot = MakeNode("DepthScale", "Track");
            bottomPivot.pivot = F2(0.5f, 0f);
            bottomPivot.localScale = F3(0.8f, 0.8f, 1f);

            return new RigSchemasFileDto
            {
                capturedUnderParentSize = F2(1920f, 1080f),
                rigs = new List<RigSchemaRigDto>
                {
                    new RigSchemaRigDto
                    {
                        rigKind = "character",
                        sourcePrefab = "Assets/CharacterRig.prefab",
                        nodes = new List<RigSchemaNodeDto>
                        {
                            MakeNode("__root", ""),
                            MakeNode("Track", "__root"),
                            bottomPivot,
                        },
                    },
                    new RigSchemaRigDto
                    {
                        rigKind = "background",
                        nodes = new List<RigSchemaNodeDto> { MakeNode("__root", "") },
                    },
                },
            };
        }

        [Test]
        public void 리그를_골라_트리를_세운다()
        {
            RectNodeTree tree = RigSchemaLoader.BuildTree(MakeFile(), "character");

            Assert.That(tree.Count, Is.EqualTo(3));
            Assert.That(tree.GetParentKey("__root"), Is.Null);
            Assert.That(tree.GetParentKey("Track"), Is.EqualTo("__root"));
            Assert.That(tree.GetParentKey("DepthScale"), Is.EqualTo("Track"));
            Assert.That(tree.RootSpace.Size, Is.EqualTo(new Vec2(1920f, 1080f)));
        }

        [Test]
        public void 상태_필드가_그대로_옮겨진다()
        {
            RectNodeTree tree = RigSchemaLoader.BuildTree(MakeFile(), "character");
            RectNodeState state = tree.GetState("DepthScale");

            Assert.That(state.Pivot, Is.EqualTo(new Vec2(0.5f, 0f)));
            Assert.That(state.LocalScale.X, Is.EqualTo(0.8f).Within(Eps));
            Assert.That(state.AnchorMax, Is.EqualTo(Vec2.One));
        }

        [Test]
        public void 바닥_pivot_노드의_좌표가_수학과_맞는다()
        {
            RectNodeTree tree = RigSchemaLoader.BuildTree(MakeFile(), "character");

            // 스트레치 풀 2단 밑 바닥 pivot 노드: 로컬 (0,0) = 부모 rect 바닥 = y -540.
            Vec3 world = tree.TransformPoint("DepthScale", Vec3.Zero);

            Assert.That(world.X, Is.EqualTo(0f).Within(Eps));
            Assert.That(world.Y, Is.EqualTo(-540f).Within(Eps));
        }

        [Test]
        public void 인스턴스_prefix가_전체_키에_적용된다()
        {
            RectNodeTree tree = RigSchemaLoader.BuildTree(MakeFile(), "character", "c1/");

            Assert.That(tree.Contains("c1/__root"), Is.True);
            Assert.That(tree.GetParentKey("c1/DepthScale"), Is.EqualTo("c1/Track"));
            Assert.That(tree.Contains("__root"), Is.False);
        }

        [Test]
        public void 없는_rigKind는_있는_것을_알려주며_거부한다()
        {
            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => RigSchemaLoader.BuildTree(MakeFile(), "charcter")); // 오타 — 비슷해도 잇지 않는다

            Assert.That(ex.Message, Does.Contain("character"));
            Assert.That(ex.Message, Does.Contain("background"));
        }

        [Test]
        public void 손상된_덤프는_조용히_넘어가지_않는다()
        {
            Assert.Throws<ArgumentNullException>(
                () => RigSchemaLoader.BuildTree((RigSchemasFileDto)null, "character"));

            Assert.Throws<ArgumentException>(
                () => RigSchemaLoader.BuildTree(new RigSchemasFileDto(), "character"));

            RigSchemasFileDto noId = MakeFile();
            noId.rigs[0].nodes[1].id = "";
            Assert.Throws<ArgumentException>(
                () => RigSchemaLoader.BuildTree(noId, "character"));

            RigSchemasFileDto nullField = MakeFile();
            nullField.rigs[0].nodes[2].pivot = null;
            Assert.Throws<ArgumentException>(
                () => RigSchemaLoader.BuildTree(nullField, "character"));
        }
    }
}
