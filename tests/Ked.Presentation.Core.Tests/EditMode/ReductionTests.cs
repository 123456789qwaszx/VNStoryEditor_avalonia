using System;
using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// b-4 경계 검증: 본보기 리덕션 둘(MoveBy·ScaleTo)과
    /// StageNodeClaim이 흐르는 세 갈래(상태 적용 · 트리 적용 · 장부 게시).
    /// 기대값은 호스트 커맨드가 종전에 하던 계산 그대로다 — 동작 불변이 규약이다.
    /// </summary>
    public sealed class ReductionTests
    {
        private const float Eps = 1e-4f;

        // ── MoveByReduction — MoveByCommandCharR.CalculateDestinationPosition와 동일 ──

        [Test]
        public void MoveBy_상대는_현재에_delta를_더한다()
        {
            StageNodeClaim claim = MoveByReduction.Reduce(
                "CharSlot_Track",
                new MoveByReduction.Args(useAbsolutePosition: false, delta: new Vec2(30f, -20f)),
                currentAnchoredPosition: new Vec2(100f, 50f));

            Assert.That(claim.NodeKey, Is.EqualTo("CharSlot_Track"));
            Assert.That(claim.Kind, Is.EqualTo(StageNodeClaimKind.AnchoredPosition));
            Assert.That(claim.Value.XY, Is.EqualTo(new Vec2(130f, 30f)));
        }

        [Test]
        public void MoveBy_절대는_delta가_곧_목표다()
        {
            StageNodeClaim claim = MoveByReduction.Reduce(
                "CharSlot_Track",
                new MoveByReduction.Args(useAbsolutePosition: true, delta: new Vec2(30f, -20f)),
                currentAnchoredPosition: new Vec2(100f, 50f));

            Assert.That(claim.Value.XY, Is.EqualTo(new Vec2(30f, -20f)));
        }

        // ── ScaleToReduction — ScaleToCommandCharR.ResolveTargetScale와 동일 ──

        [Test]
        public void ScaleTo_절대는_toScale이_곧_목표다()
        {
            StageNodeClaim claim = ScaleToReduction.Reduce(
                "CharSlot_Scale",
                new ScaleToReduction.Args(relativeToCurrent: false, toScale: new Vec2(1.38f, 1.38f)),
                currentLocalScaleXY: new Vec2(0.8f, 0.8f));

            Assert.That(claim.Kind, Is.EqualTo(StageNodeClaimKind.LocalScaleXY));
            Assert.That(claim.Value.X, Is.EqualTo(1.38f).Within(Eps));
        }

        [Test]
        public void ScaleTo_상대는_현재에_곱한다()
        {
            StageNodeClaim claim = ScaleToReduction.Reduce(
                "CharSlot_Scale",
                new ScaleToReduction.Args(relativeToCurrent: true, toScale: new Vec2(2f, 3f)),
                currentLocalScaleXY: new Vec2(0.5f, 0.5f));

            Assert.That(claim.Value.X, Is.EqualTo(1f).Within(Eps));
            Assert.That(claim.Value.Y, Is.EqualTo(1.5f).Within(Eps));
        }

        [Test]
        public void 리덕션은_순수하다_같은_입력_같은_출력()
        {
            MoveByReduction.Args args = new MoveByReduction.Args(false, new Vec2(1f, 2f));

            StageNodeClaim first = MoveByReduction.Reduce("k", args, new Vec2(10f, 10f));
            StageNodeClaim second = MoveByReduction.Reduce("k", args, new Vec2(10f, 10f));

            Assert.That(first.Value, Is.EqualTo(second.Value));
        }

        // ── StageNodeClaim: 세 갈래 ──────────────────────────────────

        [Test]
        public void 클레임을_상태에_적용하면_그_성분만_바뀐다()
        {
            RectNodeState live = RectNodeState.StretchFull
                .WithAnchoredPosition(new Vec2(1f, 2f))
                .WithLocalScale(new Vec3(1f, 1f, 5f));

            RectNodeState moved = StageNodeClaim
                .AnchoredPosition("k", new Vec2(100f, 200f))
                .ApplyTo(live);

            Assert.That(moved.AnchoredPosition, Is.EqualTo(new Vec2(100f, 200f)));
            Assert.That(moved.LocalScale, Is.EqualTo(live.LocalScale));

            // 스케일 클레임의 z 보존 규약 — Ledger와 동일.
            RectNodeState scaled = StageNodeClaim
                .LocalScaleXY("k", new Vec2(2f, 3f))
                .ApplyTo(live);

            Assert.That(scaled.LocalScale, Is.EqualTo(new Vec3(2f, 3f, 5f)));
        }

        [Test]
        public void 클레임을_트리에_적용하면_좌표가_따라온다()
        {
            RectNodeTree tree = new RectNodeTree(RectSpace.Centered(1920f, 1080f));
            tree.Add("track", null, RectNodeState.StretchFull);
            tree.Add("leaf", "track", RectNodeState.StretchFull);

            MoveByReduction.Reduce(
                    "track",
                    new MoveByReduction.Args(false, new Vec2(120f, -40f)),
                    tree.GetState("track").AnchoredPosition)
                .ApplyTo(tree);

            Vec3 world = tree.TransformPoint("leaf", Vec3.Zero);

            Assert.That(world.X, Is.EqualTo(120f).Within(Eps));
            Assert.That(world.Y, Is.EqualTo(-40f).Within(Eps));
        }

        [Test]
        public void 클레임을_없는_노드에_적용하면_소리가_난다()
        {
            RectNodeTree tree = new RectNodeTree(RectSpace.Centered(1920f, 1080f));

            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                () => StageNodeClaim.AnchoredPosition("ghost", Vec2.Zero).ApplyTo(tree));
        }

        [Test]
        public void 클레임을_장부에_게시하면_기존_게시와_같다()
        {
            PlacementTargetLedger viaClaim = new PlacementTargetLedger();
            viaClaim.Publish(StageNodeClaim.AnchoredPosition("a", new Vec2(10f, 20f)));
            viaClaim.Publish(StageNodeClaim.LocalScaleXY("b", new Vec2(2f, 2f)));
            viaClaim.Publish(StageNodeClaim.LocalEuler("c", new Vec3(0f, 0f, 45f)));

            PlacementTargetLedger direct = new PlacementTargetLedger();
            direct.PublishAnchoredPosition("a", new Vec2(10f, 20f));
            direct.PublishLocalScale("b", new Vec2(2f, 2f));
            direct.PublishLocalEuler("c", new Vec3(0f, 0f, 45f));

            RectNodeState live = RectNodeState.StretchFull;

            foreach (string key in new[] { "a", "b", "c" })
            {
                RectNodeState fromClaim = viaClaim.ApplyTo(key, live);
                RectNodeState fromDirect = direct.ApplyTo(key, live);

                Assert.That(fromClaim.AnchoredPosition, Is.EqualTo(fromDirect.AnchoredPosition), key);
                Assert.That(fromClaim.LocalScale, Is.EqualTo(fromDirect.LocalScale), key);
                Assert.That(fromClaim.LocalEulerAngles, Is.EqualTo(fromDirect.LocalEulerAngles), key);
            }
        }

        [Test]
        public void 빈_노드_키의_클레임은_거부한다()
        {
            Assert.Throws<ArgumentException>(
                () => StageNodeClaim.AnchoredPosition("", Vec2.Zero));

            Assert.Throws<ArgumentException>(
                () => StageNodeClaim.LocalScaleXY(null, Vec2.One));
        }
    }
}
