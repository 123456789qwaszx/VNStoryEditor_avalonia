using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    public sealed class RectNodeTreeTests
    {
        private const float Eps = 1e-3f;

        private static readonly RectSpace Stage = RectSpace.Centered(1920f, 1080f);

        // ── 구조 불변식 ──────────────────────────────────────────────

        [Test]
        public void 부모가_먼저_있어야_넣을_수_있다()
        {
            RectNodeTree tree = new RectNodeTree(Stage);

            Assert.Throws<ArgumentException>(
                () => tree.Add("child", "missing-parent", RectNodeState.StretchFull));
        }

        [Test]
        public void 중복_키는_거부한다()
        {
            RectNodeTree tree = new RectNodeTree(Stage);
            tree.Add("a", null, RectNodeState.StretchFull);

            Assert.Throws<ArgumentException>(
                () => tree.Add("a", null, RectNodeState.StretchFull));
        }

        [Test]
        public void 빈_키는_거부한다()
        {
            RectNodeTree tree = new RectNodeTree(Stage);

            Assert.Throws<ArgumentException>(() => tree.Add("", null, RectNodeState.StretchFull));
            Assert.Throws<ArgumentException>(() => tree.Add(null, null, RectNodeState.StretchFull));
        }

        [Test]
        public void 없는_키의_조회와_갱신은_조용히_넘어가지_않는다()
        {
            RectNodeTree tree = new RectNodeTree(Stage);

            Assert.Throws<KeyNotFoundException>(() => tree.GetState("ghost"));
            Assert.Throws<KeyNotFoundException>(() => tree.GetParentKey("ghost"));
            Assert.Throws<KeyNotFoundException>(() => tree.SetState("ghost", RectNodeState.StretchFull));
            Assert.Throws<KeyNotFoundException>(() => tree.TransformPoint("ghost", Vec3.Zero));
            Assert.Throws<KeyNotFoundException>(() => tree.GetRectSize("ghost"));
        }

        [Test]
        public void 부모_관계를_보존한다()
        {
            RectNodeTree tree = new RectNodeTree(Stage);
            tree.Add("root", null, RectNodeState.StretchFull);
            tree.Add("mid", "root", RectNodeState.StretchFull);
            tree.Add("leaf", "mid", RectNodeState.StretchFull);

            Assert.That(tree.GetParentKey("root"), Is.Null);
            Assert.That(tree.GetParentKey("mid"), Is.EqualTo("root"));
            Assert.That(tree.GetParentKey("leaf"), Is.EqualTo("mid"));
            Assert.That(tree.Count, Is.EqualTo(3));
        }

        // ── 조회가 b-1 수학과 같다 ───────────────────────────────────

        [Test]
        public void TransformPoint는_같은_사슬의_RectChainMath와_같다()
        {
            RectNodeState a = RectNodeState.StretchFull
                .WithAnchoredPosition(new Vec2(120f, -40f))
                .WithLocalScale(new Vec3(1.25f, 1.25f, 1f));
            RectNodeState b = RectNodeState.StretchFull
                .WithPivot(new Vec2(0.5f, 0f))
                .WithLocalEuler(new Vec3(0f, 0f, 15f));
            RectNodeState c = RectNodeState.StretchFull
                .WithAnchors(Vec2.Half, Vec2.Half)
                .WithSizeDelta(new Vec2(300f, 600f))
                .WithAnchoredPosition(new Vec2(-80f, 33f));

            RectNodeTree tree = new RectNodeTree(Stage);
            tree.Add("a", null, a);
            tree.Add("b", "a", b);
            tree.Add("c", "b", c);

            RectNodeState[] chain = { a, b, c };
            Vec3 p = new Vec3(12.3f, -45.6f, 0f);

            Vec3 expectedWorld = RectChainMath.TransformPoint(chain, Stage, p);
            Vec3 world = tree.TransformPoint("c", p);

            Assert.That(world.X, Is.EqualTo(expectedWorld.X).Within(Eps));
            Assert.That(world.Y, Is.EqualTo(expectedWorld.Y).Within(Eps));

            Vec3 expectedLocal = RectChainMath.InverseTransformPoint(chain, Stage, expectedWorld);
            Vec3 local = tree.InverseTransformPoint("c", world);

            Assert.That(local.X, Is.EqualTo(expectedLocal.X).Within(Eps));
            Assert.That(local.Y, Is.EqualTo(expectedLocal.Y).Within(Eps));
        }

        [Test]
        public void 형제_노드는_서로의_사슬에_끼지_않는다()
        {
            RectNodeTree tree = new RectNodeTree(Stage);
            tree.Add("root", null, RectNodeState.StretchFull);
            tree.Add("left", "root", RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(100f, 0f)));
            tree.Add("right", "root", RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(-100f, 0f)));

            Vec3 leftWorld = tree.TransformPoint("left", Vec3.Zero);
            Vec3 rightWorld = tree.TransformPoint("right", Vec3.Zero);

            Assert.That(leftWorld.X, Is.EqualTo(100f).Within(Eps));
            Assert.That(rightWorld.X, Is.EqualTo(-100f).Within(Eps));
        }

        [Test]
        public void GetRectSize는_스트레치와_고정_앵커를_가른다()
        {
            RectNodeTree tree = new RectNodeTree(Stage);
            tree.Add("stretch", null, RectNodeState.StretchFull);
            tree.Add("fixed", "stretch", RectNodeState.StretchFull
                .WithAnchors(Vec2.Half, Vec2.Half)
                .WithSizeDelta(new Vec2(300f, 600f)));
            tree.Add("inner", "fixed", RectNodeState.StretchFull);

            Assert.That(tree.GetRectSize("stretch").X, Is.EqualTo(1920f).Within(Eps));
            Assert.That(tree.GetRectSize("fixed").X, Is.EqualTo(300f).Within(Eps));
            // 고정 앵커 밑의 스트레치는 그 부모 크기를 따른다.
            Assert.That(tree.GetRectSize("inner").Y, Is.EqualTo(600f).Within(Eps));
        }

        // ── SetState / Clone ────────────────────────────────────────

        [Test]
        public void SetState는_그_노드만_바꾼다()
        {
            RectNodeTree tree = new RectNodeTree(Stage);
            tree.Add("a", null, RectNodeState.StretchFull);
            tree.Add("b", "a", RectNodeState.StretchFull);

            tree.SetState("a", RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(50f, 0f)));

            Assert.That(tree.TransformPoint("b", Vec3.Zero).X, Is.EqualTo(50f).Within(Eps));
            Assert.That(tree.GetState("b").AnchoredPosition, Is.EqualTo(Vec2.Zero));
        }

        [Test]
        public void Clone은_독립이다()
        {
            RectNodeTree tree = new RectNodeTree(Stage);
            tree.Add("a", null, RectNodeState.StretchFull);

            RectNodeTree clone = tree.Clone();
            clone.SetState("a", RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(77f, 0f)));

            Assert.That(tree.GetState("a").AnchoredPosition, Is.EqualTo(Vec2.Zero));
            Assert.That(clone.GetState("a").AnchoredPosition.X, Is.EqualTo(77f).Within(Eps));
            Assert.That(clone.RootSpace.Size, Is.EqualTo(tree.RootSpace.Size));
        }
    }
}
