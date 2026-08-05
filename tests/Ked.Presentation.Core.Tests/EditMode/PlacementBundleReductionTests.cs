using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// b-5 placement 묶음 골든: SetAnchor · FadeIn/FadeOut 리덕션과
    /// "show가 정지 프레임에 남기는 것"의 트리 폴드.
    /// 기대값은 SetAnchorCommandCharR가 종전에 쓰던 값 그대로다.
    /// </summary>
    public sealed class PlacementBundleReductionTests
    {
        private const float Eps = 1e-4f;

        private static readonly string[] ResetPos = { "CharSlot_Track", "CharSlot_Track_X", "CharSlot_Track_Y" };
        private static readonly string[] ResetEuler = { "CharSlot_Rotation" };
        private static readonly string[] ResetScale = { "CharSlot_Scale" };

        // ── SetAnchorReduction ───────────────────────────────────────

        [Test]
        public void 튜닝_엔트리가_있으면_오프셋과_배율이_앵커에_실린다()
        {
            StageNodeClaim[] claims = SetAnchorReduction.Reduce(
                "CharacterPortrait_VisualOffset",
                new SetAnchorReduction.RoleAnchorTuning(new Vec2(12f, -34f), 1.2f),
                ResetPos, ResetEuler, ResetScale);

            // 순서 규약: pos 리셋 3 → euler 리셋 1 → scale 리셋 1 → 앵커 위치 → 앵커 스케일.
            Assert.That(claims.Length, Is.EqualTo(7));

            StageNodeClaim anchorPos = claims[5];
            StageNodeClaim anchorScale = claims[6];

            Assert.That(anchorPos.NodeKey, Is.EqualTo("CharacterPortrait_VisualOffset"));
            Assert.That(anchorPos.Value.XY, Is.EqualTo(new Vec2(12f, -34f)));
            Assert.That(anchorScale.Value.X, Is.EqualTo(1.2f).Within(Eps));
            Assert.That(anchorScale.Value.Y, Is.EqualTo(1.2f).Within(Eps));
        }

        [Test]
        public void 튜닝_엔트리가_없으면_기본값이다_오프셋0_배율1()
        {
            StageNodeClaim[] claims = SetAnchorReduction.Reduce(
                "anchor",
                SetAnchorReduction.RoleAnchorTuning.Default,
                ResetPos, ResetEuler, ResetScale);

            Assert.That(claims[5].Value.XY, Is.EqualTo(Vec2.Zero));
            Assert.That(claims[6].Value.X, Is.EqualTo(1f).Within(Eps));
        }

        [Test]
        public void 배율_하한은_종전_클램프와_같다()
        {
            // 종전: Mathf.Max(0.0001f, entry.visualScale).
            StageNodeClaim[] claims = SetAnchorReduction.Reduce(
                "anchor",
                new SetAnchorReduction.RoleAnchorTuning(Vec2.Zero, 0f),
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

            Assert.That(claims[1].Value.X, Is.EqualTo(SetAnchorReduction.MinVisualScale).Within(1e-6f));
        }

        [Test]
        public void 리셋_클레임은_pos0_euler0_scale1이다()
        {
            StageNodeClaim[] claims = SetAnchorReduction.Reduce(
                "anchor",
                SetAnchorReduction.RoleAnchorTuning.Default,
                ResetPos, ResetEuler, ResetScale);

            for (int i = 0; i < 3; i++)
            {
                Assert.That(claims[i].Kind, Is.EqualTo(StageNodeClaimKind.AnchoredPosition));
                Assert.That(claims[i].NodeKey, Is.EqualTo(ResetPos[i]));
                Assert.That(claims[i].Value.XY, Is.EqualTo(Vec2.Zero));
            }

            Assert.That(claims[3].Kind, Is.EqualTo(StageNodeClaimKind.LocalEulerAngles));
            Assert.That(claims[3].Value, Is.EqualTo(Vec3.Zero));

            Assert.That(claims[4].Kind, Is.EqualTo(StageNodeClaimKind.LocalScaleXY));
            Assert.That(claims[4].Value.XY, Is.EqualTo(Vec2.One));
        }

        // ── 골든: show의 정지 프레임 폴드 ────────────────────────────

        [Test]
        public void 어질러진_리그에_set_anchor를_접으면_기본_자세로_돌아온다()
        {
            // 리그 축이 어질러진 상태를 트리로 만든다.
            RectNodeTree tree = new RectNodeTree(RectSpace.Centered(1920f, 1080f));
            tree.Add("CharSlot_Track", null,
                RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(240f, 0f)));
            tree.Add("CharSlot_Track_X", "CharSlot_Track",
                RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(-80f, 33f)));
            tree.Add("CharSlot_Track_Y", "CharSlot_Track_X",
                RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(0f, -120f)));
            tree.Add("CharSlot_Rotation", "CharSlot_Track_Y",
                RectNodeState.StretchFull.WithLocalEuler(new Vec3(0f, 0f, 25f)));
            tree.Add("CharSlot_Scale", "CharSlot_Rotation",
                RectNodeState.StretchFull.WithPivot(new Vec2(0.5f, 0f)).WithLocalScale(new Vec3(1.4f, 1.4f, 1f)));
            tree.Add("CharacterPortrait_VisualOffset", "CharSlot_Scale",
                RectNodeState.StretchFull.WithPivot(new Vec2(0.5f, 0f)));

            StageNodeClaim[] claims = SetAnchorReduction.Reduce(
                "CharacterPortrait_VisualOffset",
                new SetAnchorReduction.RoleAnchorTuning(new Vec2(0f, 12f), 1.1f),
                ResetPos, ResetEuler, ResetScale);

            foreach (StageNodeClaim claim in claims)
                claim.ApplyTo(tree);

            // 축이 전부 초기화되고 앵커만 튜닝 값이다.
            Assert.That(tree.GetState("CharSlot_Track").AnchoredPosition, Is.EqualTo(Vec2.Zero));
            Assert.That(tree.GetState("CharSlot_Rotation").LocalEulerAngles, Is.EqualTo(Vec3.Zero));
            Assert.That(tree.GetState("CharSlot_Scale").LocalScale.X, Is.EqualTo(1f).Within(Eps));
            Assert.That(tree.GetState("CharacterPortrait_VisualOffset").AnchoredPosition,
                Is.EqualTo(new Vec2(0f, 12f)));

            // 정지 프레임 좌표: 바닥 pivot 사슬이라 앵커의 (0,0)은
            // 부모 바닥(-540) + 앵커 오프셋 y(12) = -528에 선다.
            Vec3 world = tree.TransformPoint("CharacterPortrait_VisualOffset", Vec3.Zero);

            Assert.That(world.X, Is.EqualTo(0f).Within(Eps));
            Assert.That(world.Y, Is.EqualTo(-528f).Within(Eps));
        }

        // ── Fade 리덕션 ──────────────────────────────────────────────

        [Test]
        public void 페이드_리덕션은_가시성_축_클레임이다()
        {
            StageNodeClaim fadeIn = FadeInReduction.Reduce("RigRoot");
            StageNodeClaim fadeOut = FadeOutReduction.Reduce("RigRoot");

            Assert.That(fadeIn.Kind, Is.EqualTo(StageNodeClaimKind.CanvasAlpha));
            Assert.That(fadeIn.Value.X, Is.EqualTo(1f));
            Assert.That(fadeOut.Value.X, Is.EqualTo(0f));
        }

        [Test]
        public void 가시성_클레임은_트랜스폼_상태에_적용될_수_없다()
        {
            // alpha는 RectNodeState에 살지 않는다 — 조용히 무시하는 대신 예외.
            Assert.Throws<InvalidOperationException>(
                () => FadeInReduction.Reduce("RigRoot").ApplyTo(RectNodeState.StretchFull));
        }

        [Test]
        public void 빈_리셋_목록도_앵커_클레임은_나온다()
        {
            StageNodeClaim[] claims = SetAnchorReduction.Reduce(
                "anchor",
                SetAnchorReduction.RoleAnchorTuning.Default,
                null, null, null);

            Assert.That(claims.Length, Is.EqualTo(2));
            Assert.That(claims[0].Kind, Is.EqualTo(StageNodeClaimKind.AnchoredPosition));
            Assert.That(claims[1].Kind, Is.EqualTo(StageNodeClaimKind.LocalScaleXY));
        }
    }
}
