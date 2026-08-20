using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// 트리의 불변식과 좌표 조회.
    ///
    /// 좌표 산수 자체는 RectChainMathTests가 이미 고정했다. 여기서 보는 것은
    /// "트리가 사슬을 옳게 세우는가"와 "침묵하지 않는가"다.
    /// </summary>
    public sealed class RectNodeTreeTests
    {
        private const float Eps = 1e-4f;

        private static RectNodeTree NewTree()
            => new(RectSpace.Centered(1000f, 500f));

        private static void AssertVec3(Vec3 actual, float x, float y, float z)
        {
            Assert.That(actual.X, Is.EqualTo(x).Within(Eps), $"X — actual={actual}");
            Assert.That(actual.Y, Is.EqualTo(y).Within(Eps), $"Y — actual={actual}");
            Assert.That(actual.Z, Is.EqualTo(z).Within(Eps), $"Z — actual={actual}");
        }

        // ── 불변식 ───────────────────────────────────────────────────

        [Test]
        public void 부모가_없으면_자식을_넣을_수_없다()
        {
            RectNodeTree tree = NewTree();

            // 이 제약과 "재부모화 API 없음"이 합쳐져 순환을 구조적으로 불가능하게 만든다.
            Assert.Throws<ArgumentException>(
                () => tree.Add("child", "없는부모", RectNodeState.StretchFull));

            Assert.That(tree.Count, Is.Zero, "실패한 Add가 노드를 남기면 안 된다");
        }

        [Test]
        public void 같은_키를_두_번_넣을_수_없다()
        {
            RectNodeTree tree = NewTree();
            tree.Add("a", null, RectNodeState.StretchFull);

            Assert.Throws<ArgumentException>(
                () => tree.Add("a", null, RectNodeState.StretchFull));
        }

        [Test]
        public void 빈_키는_거부한다()
        {
            RectNodeTree tree = NewTree();

            Assert.Throws<ArgumentException>(() => tree.Add(null, null, RectNodeState.StretchFull));
            Assert.Throws<ArgumentException>(() => tree.Add("", null, RectNodeState.StretchFull));
        }

        [Test]
        public void 없는_키_조회는_0이_아니라_예외다()
        {
            RectNodeTree tree = NewTree();
            tree.Add("a", null, RectNodeState.StretchFull);

            // 침묵은 이 프로젝트가 피하는 실패 모양이다 — 조용한 0은 잘못된 좌표로 흘러간다.
            Assert.Throws<KeyNotFoundException>(() => tree.GetState("b"));
            Assert.Throws<KeyNotFoundException>(() => tree.GetParentKey("b"));
            Assert.Throws<KeyNotFoundException>(() => tree.TransformPoint("b", Vec3.Zero));
            Assert.Throws<KeyNotFoundException>(() => tree.InverseTransformPoint("b", Vec3.Zero));
            Assert.Throws<KeyNotFoundException>(() => tree.GetRectSize("b"));
        }

        [Test]
        public void SetState는_없는_키를_새로_만들지_않는다()
        {
            RectNodeTree tree = NewTree();

            Assert.Throws<KeyNotFoundException>(
                () => tree.SetState("a", RectNodeState.StretchFull));

            Assert.That(tree.Count, Is.Zero);
        }

        [Test]
        public void TryGetState는_없는_키에_false를_준다()
        {
            RectNodeTree tree = NewTree();
            tree.Add("a", null, RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(3f, 4f)));

            Assert.That(tree.TryGetState("a", out RectNodeState found), Is.True);
            Assert.That(found.AnchoredPosition, Is.EqualTo(new Vec2(3f, 4f)));

            Assert.That(tree.TryGetState("b", out _), Is.False);
            Assert.That(tree.TryGetState(null, out _), Is.False);
        }

        [Test]
        public void Contains와_Count와_Keys()
        {
            RectNodeTree tree = NewTree();
            tree.Add("a", null, RectNodeState.StretchFull);
            tree.Add("b", "a", RectNodeState.StretchFull);

            Assert.That(tree.Count, Is.EqualTo(2));
            Assert.That(tree.Contains("a"), Is.True);
            Assert.That(tree.Contains("z"), Is.False);
            Assert.That(tree.Contains(null), Is.False);
            Assert.That(tree.Keys, Is.EquivalentTo(new[] { "a", "b" }));
        }

        [Test]
        public void 루트_직속_노드의_부모는_null이다()
        {
            RectNodeTree tree = NewTree();
            tree.Add("a", null, RectNodeState.StretchFull);
            tree.Add("b", "a", RectNodeState.StretchFull);

            Assert.That(tree.GetParentKey("a"), Is.Null);
            Assert.That(tree.GetParentKey("b"), Is.EqualTo("a"));
        }

        // ── 상태 갱신·복제 ───────────────────────────────────────────

        [Test]
        public void SetState는_구조를_건드리지_않는다()
        {
            RectNodeTree tree = NewTree();
            tree.Add("a", null, RectNodeState.StretchFull);
            tree.Add("b", "a", RectNodeState.StretchFull);

            tree.SetState("a", RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(50f, 0f)));

            Assert.That(tree.GetState("a").AnchoredPosition, Is.EqualTo(new Vec2(50f, 0f)));
            Assert.That(tree.GetParentKey("b"), Is.EqualTo("a"), "부모 관계가 유지돼야 한다");
        }

        [Test]
        public void Clone은_원본과_독립이다()
        {
            RectNodeTree tree = NewTree();
            tree.Add("a", null, RectNodeState.StretchFull);
            tree.Add("b", "a", RectNodeState.StretchFull);

            RectNodeTree clone = tree.Clone();

            clone.SetState("a", RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(99f, 0f)));
            clone.Add("c", "b", RectNodeState.StretchFull);

            // 사본을 접어도 원본은 그대로여야 한다 — 리듀서의 순수성이 이것 위에 선다.
            Assert.That(tree.GetState("a").AnchoredPosition, Is.EqualTo(Vec2.Zero));
            Assert.That(tree.Contains("c"), Is.False);
            Assert.That(tree.Count, Is.EqualTo(2));
            Assert.That(clone.Count, Is.EqualTo(3));
        }

        [Test]
        public void Clone은_루트_공간도_가져간다()
        {
            RectNodeTree tree = new(new RectSpace(new Vec2(800f, 600f), new Vec2(0f, 1f)));

            RectNodeTree clone = tree.Clone();

            Assert.That(clone.RootSpace.Size, Is.EqualTo(new Vec2(800f, 600f)));
            Assert.That(clone.RootSpace.Pivot, Is.EqualTo(new Vec2(0f, 1f)));
        }

        // ── 좌표 조회 ────────────────────────────────────────────────

        [Test]
        public void 사슬을_루트부터_순서대로_세운다()
        {
            // 트리 조회가 RectChainMath에 넘기는 사슬의 순서가 맞는지 본다.
            // 순서가 뒤집히면 이 값이 (35, 5)가 아니라 다른 값이 된다.
            RectNodeTree tree = NewTree();
            tree.Add("a", null, RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(10f, 0f)));
            tree.Add("b", "a", RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(20f, 0f)));
            tree.Add("c", "b", RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(5f, 5f)));

            AssertVec3(tree.TransformPoint("c", Vec3.Zero), 35f, 5f, 0f);
        }

        [Test]
        public void 형제_가지는_서로의_사슬에_끼지_않는다()
        {
            // 같은 부모 아래 둘을 두면 각자 자기 조상만 타야 한다.
            RectNodeTree tree = NewTree();
            tree.Add("root", null, RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(10f, 0f)));
            tree.Add("left", "root", RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(-100f, 0f)));
            tree.Add("right", "root", RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(100f, 0f)));

            AssertVec3(tree.TransformPoint("left", Vec3.Zero), -90f, 0f, 0f);
            AssertVec3(tree.TransformPoint("right", Vec3.Zero), 110f, 0f, 0f);
        }

        [Test]
        public void 트리_조회는_RectChainMath와_같은_값이다()
        {
            // 트리는 사슬을 세울 뿐 새로 산수하지 않는다 — 그걸 못 박는다.
            RectNodeState[] chain =
            {
                RectNodeState.StretchFull
                    .WithAnchoredPosition(new Vec2(12f, -8f))
                    .WithLocalScale(new Vec3(1.4f, 1.4f, 1f)),
                RectNodeState.StretchFull
                    .WithPivot(new Vec2(0.5f, 0f))
                    .WithLocalEuler(new Vec3(0f, 0f, 25f)),
            };

            RectSpace space = RectSpace.Centered(1000f, 500f);

            RectNodeTree tree = new(space);
            tree.Add("a", null, chain[0]);
            tree.Add("b", "a", chain[1]);

            Vec3 point = new(37f, -19f, 0f);

            Vec3 direct = RectChainMath.TransformPoint(chain, space, point);
            Vec3 viaTree = tree.TransformPoint("b", point);

            AssertVec3(viaTree, direct.X, direct.Y, direct.Z);

            Vec3 backDirect = RectChainMath.InverseTransformPoint(chain, space, direct);
            Vec3 backViaTree = tree.InverseTransformPoint("b", direct);

            AssertVec3(backViaTree, backDirect.X, backDirect.Y, backDirect.Z);
        }

        [Test]
        public void 역변환은_변환을_되돌린다()
        {
            RectNodeTree tree = NewTree();
            tree.Add("a", null, RectNodeState.StretchFull.WithLocalScale(new Vec3(1.5f, 1.5f, 1f)));
            tree.Add("b", "a", RectNodeState.StretchFull.WithLocalEuler(new Vec3(0f, 0f, 40f)));

            Vec3 local = new(30f, -12f, 0f);
            Vec3 world = tree.TransformPoint("b", local);

            AssertVec3(tree.InverseTransformPoint("b", world), local.X, local.Y, local.Z);
        }

        // ── rect 크기 ────────────────────────────────────────────────

        [Test]
        public void 스트레치_체인의_크기는_루트_공간_크기다()
        {
            RectNodeTree tree = NewTree();
            tree.Add("a", null, RectNodeState.StretchFull);
            tree.Add("b", "a", RectNodeState.StretchFull);

            Assert.That(tree.GetRectSize("b").X, Is.EqualTo(1000f).Within(Eps));
            Assert.That(tree.GetRectSize("b").Y, Is.EqualTo(500f).Within(Eps));
        }

        [Test]
        public void 고정_앵커_조상이_아래쪽_크기를_바꾼다()
        {
            // 초상 이미지 계열의 모양: 중간에 고정 앵커 노드가 끼면 그 아래는
            // 루트 크기가 아니라 그 노드의 크기를 물려받는다.
            RectNodeTree tree = NewTree();

            tree.Add("a", null, RectNodeState.StretchFull
                .WithAnchors(Vec2.Half, Vec2.Half)
                .WithSizeDelta(new Vec2(400f, 200f)));

            tree.Add("b", "a", RectNodeState.StretchFull);

            Assert.That(tree.GetRectSize("a").X, Is.EqualTo(400f).Within(Eps));
            Assert.That(tree.GetRectSize("b").X, Is.EqualTo(400f).Within(Eps));
            Assert.That(tree.GetRectSize("b").Y, Is.EqualTo(200f).Within(Eps));
        }

        [Test]
        public void 부분_스트레치는_조상_크기에서_파생된다()
        {
            RectNodeTree tree = NewTree();

            tree.Add("a", null, RectNodeState.StretchFull
                .WithAnchors(new Vec2(0.25f, 0f), new Vec2(0.75f, 1f))
                .WithSizeDelta(new Vec2(-40f, 20f)));

            // 0.5 * 1000 - 40 = 460 / 1.0 * 500 + 20 = 520
            Assert.That(tree.GetRectSize("a").X, Is.EqualTo(460f).Within(Eps));
            Assert.That(tree.GetRectSize("a").Y, Is.EqualTo(520f).Within(Eps));
        }
    }
}
