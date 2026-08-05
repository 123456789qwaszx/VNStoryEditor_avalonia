using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// b-5 staging 묶음 골든. yarn staging 커맨드가 실제로 쓰는 스펙 조합 그대로 고정한다:
    /// rotate_by(SwayPivot, 상대 z) · rotate_reset(절대 0) ·
    /// scale_by(상대 배율) · scale_reset(절대 1) · move_reset(절대 0).
    /// </summary>
    public sealed class StagingBundleReductionTests
    {
        private const float Eps = 1e-4f;

        [Test]
        public void rotate_by는_현재_z에_각도를_더한다()
        {
            // EnqueueRotateBySpec: relativeToCurrent=true, toEuler=(0,0,degree).
            StageNodeClaim claim = RotateToReduction.Reduce(
                "CharSlot_SwayPivot",
                new RotateToReduction.Args(relativeToCurrent: true, toEuler: new Vec3(0f, 0f, 15f)),
                currentLocalEuler: new Vec3(0f, 0f, -10f));

            Assert.That(claim.Kind, Is.EqualTo(StageNodeClaimKind.LocalEulerAngles));
            Assert.That(claim.Value.Z, Is.EqualTo(5f).Within(Eps));
        }

        [Test]
        public void rotate_reset은_절대_0이다()
        {
            // EnqueueRotateResetSpec: relativeToCurrent=false(기본), toEuler=(0,0,0).
            StageNodeClaim claim = RotateToReduction.Reduce(
                "CharSlot_SwayPivot",
                new RotateToReduction.Args(relativeToCurrent: false, toEuler: Vec3.Zero),
                currentLocalEuler: new Vec3(0f, 0f, 37f));

            Assert.That(claim.Value, Is.EqualTo(Vec3.Zero));
        }

        [Test]
        public void rotate_to_절대는_toEuler_그_자체다()
        {
            StageNodeClaim claim = RotateToReduction.Reduce(
                "node",
                new RotateToReduction.Args(false, new Vec3(10f, 20f, 30f)),
                new Vec3(1f, 2f, 3f));

            Assert.That(claim.Value, Is.EqualTo(new Vec3(10f, 20f, 30f)));
        }

        [Test]
        public void scale_by는_현재에_배율을_곱한다()
        {
            // EnqueueSizeBySpec: relativeToCurrent=true, toScale=(m,m).
            StageNodeClaim claim = ScaleToReduction.Reduce(
                "CharSlot_Scale",
                new ScaleToReduction.Args(relativeToCurrent: true, toScale: new Vec2(1.2f, 1.2f)),
                currentLocalScaleXY: new Vec2(0.9f, 0.9f));

            Assert.That(claim.Value.X, Is.EqualTo(1.08f).Within(Eps));
        }

        [Test]
        public void scale_reset은_절대_1이다()
        {
            StageNodeClaim claim = ScaleToReduction.Reduce(
                "CharSlot_Scale",
                new ScaleToReduction.Args(false, Vec2.One),
                new Vec2(1.4f, 1.4f));

            Assert.That(claim.Value.XY, Is.EqualTo(Vec2.One));
        }

        [Test]
        public void move_reset은_절대_0이다()
        {
            // EnqueueSetPlaceResetSpecs: useAbsolutePosition=true, delta=(0,0) — Track과 Track_Focus 두 장.
            StageNodeClaim track = MoveByReduction.Reduce(
                "CharSlot_Track",
                new MoveByReduction.Args(useAbsolutePosition: true, delta: Vec2.Zero),
                currentAnchoredPosition: new Vec2(240f, -60f));

            StageNodeClaim focus = MoveByReduction.Reduce(
                "CharSlot_Track_Focus",
                new MoveByReduction.Args(useAbsolutePosition: true, delta: Vec2.Zero),
                currentAnchoredPosition: new Vec2(-80f, 40f));

            Assert.That(track.Value.XY, Is.EqualTo(Vec2.Zero));
            Assert.That(focus.Value.XY, Is.EqualTo(Vec2.Zero));
        }

        [Test]
        public void 회전_클레임은_트리에_접힌다()
        {
            RectNodeTree tree = new RectNodeTree(RectSpace.Centered(1920f, 1080f));
            tree.Add("sway", null, RectNodeState.StretchFull.WithLocalEuler(new Vec3(0f, 0f, -10f)));
            tree.Add("leaf", "sway", RectNodeState.StretchFull);

            RotateToReduction.Reduce(
                    "sway",
                    new RotateToReduction.Args(true, new Vec3(0f, 0f, 100f)),
                    tree.GetState("sway").LocalEulerAngles)
                .ApplyTo(tree);

            // z 90° 회전: leaf 로컬 (1,0) → 루트 (0,1).
            Vec3 world = tree.TransformPoint("leaf", new Vec3(1f, 0f, 0f));

            Assert.That(world.X, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(world.Y, Is.EqualTo(1f).Within(1e-3f));
        }
    }
}
