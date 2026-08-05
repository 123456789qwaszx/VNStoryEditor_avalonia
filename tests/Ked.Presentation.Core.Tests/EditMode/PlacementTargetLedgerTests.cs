using System;
using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    public sealed class PlacementTargetLedgerTests
    {
        private const float Eps = 1e-4f;

        [Test]
        public void 예약이_없으면_라이브를_그대로_돌려준다()
        {
            PlacementTargetLedger ledger = new PlacementTargetLedger();
            RectNodeState live = RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(10f, 20f));

            RectNodeState result = ledger.ApplyTo("track", live);

            Assert.That(result.AnchoredPosition, Is.EqualTo(live.AnchoredPosition));
            Assert.That(ledger.IsEmpty, Is.True);
        }

        [Test]
        public void 위치_예약은_위치만_바꾼다()
        {
            PlacementTargetLedger ledger = new PlacementTargetLedger();
            ledger.PublishAnchoredPosition("track", new Vec2(100f, -50f));

            RectNodeState live = RectNodeState.StretchFull
                .WithAnchoredPosition(new Vec2(1f, 2f))
                .WithLocalScale(new Vec3(2f, 2f, 1f));

            RectNodeState result = ledger.ApplyTo("track", live);

            Assert.That(result.AnchoredPosition, Is.EqualTo(new Vec2(100f, -50f)));
            Assert.That(result.LocalScale, Is.EqualTo(live.LocalScale));
        }

        [Test]
        public void 스케일_예약은_z를_보존한다()
        {
            // 종전 규약: rect.localScale = (x, y, 기존 z).
            PlacementTargetLedger ledger = new PlacementTargetLedger();
            ledger.PublishLocalScale("scale", new Vec2(0.8f, 0.9f));

            RectNodeState live = RectNodeState.StretchFull.WithLocalScale(new Vec3(1f, 1f, 7f));

            RectNodeState result = ledger.ApplyTo("scale", live);

            Assert.That(result.LocalScale.X, Is.EqualTo(0.8f).Within(Eps));
            Assert.That(result.LocalScale.Y, Is.EqualTo(0.9f).Within(Eps));
            Assert.That(result.LocalScale.Z, Is.EqualTo(7f).Within(Eps));
        }

        [Test]
        public void 종류별_슬롯은_서로를_지우지_않는다()
        {
            // 종전 구현의 함정(마지막 Publish가 종류째 덮어씀)을 없앤 지점.
            PlacementTargetLedger ledger = new PlacementTargetLedger();
            ledger.PublishAnchoredPosition("node", new Vec2(100f, 0f));
            ledger.PublishLocalScale("node", new Vec2(2f, 2f));

            RectNodeState result = ledger.ApplyTo("node", RectNodeState.StretchFull);

            Assert.That(result.AnchoredPosition, Is.EqualTo(new Vec2(100f, 0f)));
            Assert.That(result.LocalScale.X, Is.EqualTo(2f).Within(Eps));
        }

        [Test]
        public void 같은_종류는_마지막_게시가_이긴다()
        {
            PlacementTargetLedger ledger = new PlacementTargetLedger();
            ledger.PublishAnchoredPosition("node", new Vec2(1f, 1f));
            ledger.PublishAnchoredPosition("node", new Vec2(2f, 2f));

            RectNodeState result = ledger.ApplyTo("node", RectNodeState.StretchFull);

            Assert.That(result.AnchoredPosition, Is.EqualTo(new Vec2(2f, 2f)));
        }

        [Test]
        public void 오일러_예약은_통째로_바꾼다()
        {
            PlacementTargetLedger ledger = new PlacementTargetLedger();
            ledger.PublishLocalEuler("rot", new Vec3(10f, 20f, 30f));

            RectNodeState live = RectNodeState.StretchFull.WithLocalEuler(new Vec3(0f, 0f, 90f));

            Assert.That(ledger.ApplyTo("rot", live).LocalEulerAngles, Is.EqualTo(new Vec3(10f, 20f, 30f)));
        }

        [Test]
        public void Clear는_그_키만_비운다()
        {
            PlacementTargetLedger ledger = new PlacementTargetLedger();
            ledger.PublishAnchoredPosition("a", new Vec2(1f, 0f));
            ledger.PublishAnchoredPosition("b", new Vec2(2f, 0f));

            ledger.Clear("a");

            Assert.That(ledger.HasTargets("a"), Is.False);
            Assert.That(ledger.HasTargets("b"), Is.True);
            Assert.That(ledger.IsEmpty, Is.False);

            ledger.Clear("b");
            Assert.That(ledger.IsEmpty, Is.True);
        }

        [Test]
        public void 다른_키의_예약은_영향이_없다()
        {
            PlacementTargetLedger ledger = new PlacementTargetLedger();
            ledger.PublishAnchoredPosition("a", new Vec2(999f, 999f));

            RectNodeState live = RectNodeState.StretchFull;

            Assert.That(ledger.ApplyTo("b", live).AnchoredPosition, Is.EqualTo(Vec2.Zero));
        }

        [Test]
        public void 빈_키_게시는_거부한다()
        {
            PlacementTargetLedger ledger = new PlacementTargetLedger();

            Assert.Throws<ArgumentException>(() => ledger.PublishAnchoredPosition("", Vec2.Zero));
            Assert.Throws<ArgumentException>(() => ledger.PublishLocalScale(null, Vec2.One));
        }
    }
}
