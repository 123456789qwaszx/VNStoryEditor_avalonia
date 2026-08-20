using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// staging 묶음. 기대값은 종전 RotateToCommandCharR.ResolveTargetEuler에서 온다:
    ///   relativeToCurrent ? startEuler + toEuler : toEuler
    ///
    /// yarn이 실제로 쓰는 조합 둘(rotate_by · rotate_reset)을 골든으로 고정한다.
    /// </summary>
    public sealed class StagingBundleReductionTests
    {
        private const float Eps = 1e-4f;

        // 브리지가 만드는 스펙과 같은 표적.
        private const string SwayPivot = "CharSlot_SwayPivot";

        [Test]
        public void rotate_by는_현재_각에_z를_더한다()
        {
            // 브리지: toEuler = (0, 0, degree), relativeToCurrent = true
            StageNodeClaim claim = RotateToReduction.Reduce(
                SwayPivot,
                new RotateToReduction.Args(true, new Vec3(0f, 0f, 15f)),
                new Vec3(0f, 0f, 20f));

            Assert.That(claim.NodeKey, Is.EqualTo(SwayPivot));
            Assert.That(claim.Kind, Is.EqualTo(StageNodeClaimKind.LocalEulerAngles));
            Assert.That(claim.Value, Is.EqualTo(new Vec3(0f, 0f, 35f)));
        }

        [Test]
        public void rotate_reset은_절대_0이다()
        {
            // 브리지: toEuler = (0, 0, 0), relativeToCurrent 기본값(false)
            StageNodeClaim claim = RotateToReduction.Reduce(
                SwayPivot,
                new RotateToReduction.Args(false, Vec3.Zero),
                new Vec3(0f, 0f, 137f));

            Assert.That(claim.Value, Is.EqualTo(Vec3.Zero));
        }

        [Test]
        public void 절대_모드는_현재_각을_무시한다()
        {
            StageNodeClaim claim = RotateToReduction.Reduce(
                SwayPivot,
                new RotateToReduction.Args(false, new Vec3(10f, 20f, 30f)),
                new Vec3(90f, 90f, 90f));

            Assert.That(claim.Value, Is.EqualTo(new Vec3(10f, 20f, 30f)));
        }

        [Test]
        public void 상대_모드는_3축_전부_더한다()
        {
            // Ledger가 Vector3를 게시하므로 리덕션도 3축을 다룬다.
            StageNodeClaim claim = RotateToReduction.Reduce(
                SwayPivot,
                new RotateToReduction.Args(true, new Vec3(5f, -10f, 15f)),
                new Vec3(1f, 2f, 3f));

            Assert.That(claim.Value, Is.EqualTo(new Vec3(6f, -8f, 18f)));
        }

        [Test]
        public void 회전_클레임을_트리에_접으면_좌표가_돈다()
        {
            // 값만 보면 "각을 넣었다"까지다. 접은 뒤 좌표까지 봐야 클레임이 실제로 먹는지 안다.
            RectNodeTree tree = new(RectSpace.Centered(1000f, 500f));
            tree.Add(SwayPivot, null, RectNodeState.StretchFull);

            StageNodeClaim claim = RotateToReduction.Reduce(
                SwayPivot,
                new RotateToReduction.Args(true, new Vec3(0f, 0f, 90f)),
                tree.GetState(SwayPivot).LocalEulerAngles);

            claim.ApplyTo(tree);

            // Z 90°는 X축을 Y축으로 보낸다.
            Vec3 p = tree.TransformPoint(SwayPivot, new Vec3(100f, 0f, 0f));

            Assert.That(p.X, Is.EqualTo(0f).Within(Eps));
            Assert.That(p.Y, Is.EqualTo(100f).Within(Eps));
        }

        [Test]
        public void show가_되돌리는_축과_rotate_by의_축이_다르다()
        {
            // 런타임 실동작이다. set_anchor/show의 오일러 리셋 목록은 CharSlot_Rotation이고
            // rotate_by의 표적은 CharSlot_SwayPivot이라, show는 rotate_by를 되돌리지 않는다.
            // 리듀서도 이 동작을 그대로 따라야 하므로 여기 못 박아 둔다.
            RectNodeTree tree = new(RectSpace.Centered(1000f, 500f));
            tree.Add("CharSlot_Rotation", null, RectNodeState.StretchFull);
            tree.Add("CharSlot_SwayPivot", "CharSlot_Rotation", RectNodeState.StretchFull);

            // rotate_by
            RotateToReduction
                .Reduce("CharSlot_SwayPivot", new RotateToReduction.Args(true, new Vec3(0f, 0f, 30f)), Vec3.Zero)
                .ApplyTo(tree);

            // show 상당: 오일러 리셋 목록에 SwayPivot이 없다.
            StageNodeClaim[] showClaims = SetAnchorReduction.Reduce(
                "CharSlot_SwayPivot",
                SetAnchorReduction.RoleAnchorTuning.Default,
                resetPositionKeys: null,
                resetEulerKeys: new[] { "CharSlot_Rotation" },
                resetScaleKeys: null);

            foreach (StageNodeClaim claim in showClaims)
                claim.ApplyTo(tree);

            Assert.That(
                tree.GetState("CharSlot_SwayPivot").LocalEulerAngles.Z,
                Is.EqualTo(30f).Within(Eps),
                "show는 SwayPivot의 회전을 되돌리지 않는다");
        }
    }
}
