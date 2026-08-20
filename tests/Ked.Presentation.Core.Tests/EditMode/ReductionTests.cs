using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// 리덕션 규약과 본보기 둘.
    ///
    /// 기대값은 전부 "종전 식"에서 온다 — 새 기대값을 만들면 동작 불변을 주장할 수 없다.
    ///   MoveBy   : useAbsolutePosition ? delta : startPos + delta
    ///   ScaleTo  : relativeToCurrent ? current * toScale : toScale
    /// </summary>
    public sealed class ReductionTests
    {
        // ── MoveByReduction ──────────────────────────────────────────

        [Test]
        public void MoveBy_상대는_현재_위치에_더한다()
        {
            StageNodeClaim claim = MoveByReduction.Reduce(
                "track",
                new MoveByReduction.Args(false, new Vec2(30f, -10f)),
                new Vec2(100f, 50f));

            Assert.That(claim.NodeKey, Is.EqualTo("track"));
            Assert.That(claim.Kind, Is.EqualTo(StageNodeClaimKind.AnchoredPosition));
            Assert.That(claim.Value.XY, Is.EqualTo(new Vec2(130f, 40f)));
        }

        [Test]
        public void MoveBy_절대는_현재_위치를_무시한다()
        {
            StageNodeClaim claim = MoveByReduction.Reduce(
                "track",
                new MoveByReduction.Args(true, new Vec2(30f, -10f)),
                new Vec2(100f, 50f));

            Assert.That(claim.Value.XY, Is.EqualTo(new Vec2(30f, -10f)));
        }

        [Test]
        public void MoveBy는_순수하다()
        {
            // 같은 입력은 언제나 같은 출력. 호출 순서·횟수에 의존하지 않는다.
            MoveByReduction.Args args = new(false, new Vec2(5f, 5f));

            Vec2 first = MoveByReduction.Reduce("n", args, new Vec2(1f, 1f)).Value.XY;
            Vec2 second = MoveByReduction.Reduce("n", args, new Vec2(1f, 1f)).Value.XY;

            Assert.That(second, Is.EqualTo(first));
            Assert.That(first, Is.EqualTo(new Vec2(6f, 6f)));
        }

        // ── ScaleToReduction ─────────────────────────────────────────

        [Test]
        public void ScaleTo_절대는_스펙_값_그대로다()
        {
            StageNodeClaim claim = ScaleToReduction.Reduce(
                "scale",
                new ScaleToReduction.Args(false, new Vec2(1.5f, 2f)),
                new Vec2(3f, 3f));

            Assert.That(claim.Kind, Is.EqualTo(StageNodeClaimKind.LocalScaleXY));
            Assert.That(claim.Value.XY, Is.EqualTo(new Vec2(1.5f, 2f)));
        }

        [Test]
        public void ScaleTo_상대는_현재에_곱한다()
        {
            // 성분별 곱이다 — 종전 ResolveTargetScale과 같은 식.
            StageNodeClaim claim = ScaleToReduction.Reduce(
                "scale",
                new ScaleToReduction.Args(true, new Vec2(2f, 0.5f)),
                new Vec2(3f, 4f));

            Assert.That(claim.Value.XY, Is.EqualTo(new Vec2(6f, 2f)));
        }

        // ── 클레임 적용 ──────────────────────────────────────────────

        [Test]
        public void 스케일_클레임은_z를_보존한다()
        {
            // Ledger와 같은 규약. 게시·트윈·폴드가 같은 규약을 봐야 한다.
            RectNodeState state = RectNodeState.StretchFull.WithLocalScale(new Vec3(1f, 1f, 7f));

            StageNodeClaim claim = StageNodeClaim.LocalScaleXY("n", new Vec2(2f, 3f));

            Assert.That(claim.ApplyTo(state).LocalScale, Is.EqualTo(new Vec3(2f, 3f, 7f)));
        }

        [Test]
        public void 각_클레임은_해당_성분만_바꾼다()
        {
            RectNodeState state = RectNodeState.StretchFull
                .WithAnchoredPosition(new Vec2(1f, 2f))
                .WithLocalScale(new Vec3(3f, 4f, 5f))
                .WithLocalEuler(new Vec3(0f, 0f, 30f));

            RectNodeState moved = StageNodeClaim.AnchoredPosition("n", new Vec2(9f, 9f)).ApplyTo(state);
            Assert.That(moved.AnchoredPosition, Is.EqualTo(new Vec2(9f, 9f)));
            Assert.That(moved.LocalScale, Is.EqualTo(new Vec3(3f, 4f, 5f)));
            Assert.That(moved.LocalEulerAngles, Is.EqualTo(new Vec3(0f, 0f, 30f)));

            RectNodeState turned = StageNodeClaim.LocalEuler("n", new Vec3(0f, 0f, 90f)).ApplyTo(state);
            Assert.That(turned.LocalEulerAngles, Is.EqualTo(new Vec3(0f, 0f, 90f)));
            Assert.That(turned.AnchoredPosition, Is.EqualTo(new Vec2(1f, 2f)));
        }

        [Test]
        public void 빈_노드_키는_거부한다()
        {
            Assert.Throws<ArgumentException>(() => StageNodeClaim.AnchoredPosition(null, Vec2.Zero));
            Assert.Throws<ArgumentException>(() => StageNodeClaim.LocalScaleXY("", Vec2.One));
            Assert.Throws<ArgumentException>(() => StageNodeClaim.LocalEuler("", Vec3.Zero));
        }

        // ── 클레임이 흐르는 세 갈래 ──────────────────────────────────

        [Test]
        public void 트리_폴드는_없는_노드에_침묵하지_않는다()
        {
            RectNodeTree tree = new(RectSpace.Centered(1000f, 500f));
            tree.Add("a", null, RectNodeState.StretchFull);

            StageNodeClaim.AnchoredPosition("a", new Vec2(7f, 8f)).ApplyTo(tree);
            Assert.That(tree.GetState("a").AnchoredPosition, Is.EqualTo(new Vec2(7f, 8f)));

            Assert.Throws<KeyNotFoundException>(
                () => StageNodeClaim.AnchoredPosition("없음", Vec2.Zero).ApplyTo(tree));

            Assert.Throws<ArgumentNullException>(
                () => StageNodeClaim.AnchoredPosition("a", Vec2.Zero).ApplyTo((RectNodeTree)null));
        }

        [Test]
        public void 장부_게시는_종류별_슬롯으로_들어간다()
        {
            PlacementTargetLedger ledger = new();

            ledger.Publish(StageNodeClaim.AnchoredPosition("n", new Vec2(10f, 20f)));
            ledger.Publish(StageNodeClaim.LocalScaleXY("n", new Vec2(2f, 2f)));
            ledger.Publish(StageNodeClaim.LocalEuler("n", new Vec3(0f, 0f, 45f)));

            RectNodeState settled = ledger.ApplyTo("n", RectNodeState.StretchFull.WithLocalScale(new Vec3(1f, 1f, 9f)));

            Assert.That(settled.AnchoredPosition, Is.EqualTo(new Vec2(10f, 20f)));
            Assert.That(settled.LocalScale, Is.EqualTo(new Vec3(2f, 2f, 9f)));
            Assert.That(settled.LocalEulerAngles, Is.EqualTo(new Vec3(0f, 0f, 45f)));
        }

        [Test]
        public void 세_갈래가_같은_값을_본다()
        {
            // 이 단계의 존재 이유. 리덕션 하나의 출력이
            //   장부 게시 · 트윈 종점 · 트리 폴드
            // 셋 모두에서 같은 값이어야 "재생 = 정착 예약 = 정지 프레임"이 성립한다.
            StageNodeClaim claim = MoveByReduction.Reduce(
                "track",
                new MoveByReduction.Args(false, new Vec2(40f, 0f)),
                new Vec2(60f, 10f));

            Vec2 expected = new(100f, 10f);

            // ① 트윈 종점 — 호스트가 그대로 쓰는 값
            Assert.That(claim.Value.XY, Is.EqualTo(expected));

            // ② 장부 게시
            PlacementTargetLedger ledger = new();
            ledger.Publish(claim);
            RectNodeState live = RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(60f, 10f));
            Assert.That(ledger.ApplyTo("track", live).AnchoredPosition, Is.EqualTo(expected));

            // ③ 트리 폴드
            RectNodeTree tree = new(RectSpace.Centered(1000f, 500f));
            tree.Add("track", null, live);
            claim.ApplyTo(tree);
            Assert.That(tree.GetState("track").AnchoredPosition, Is.EqualTo(expected));
        }
    }
}
