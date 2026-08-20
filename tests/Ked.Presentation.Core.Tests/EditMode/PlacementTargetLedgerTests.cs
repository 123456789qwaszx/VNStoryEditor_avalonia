using System;
using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// 예약 장부의 규약. 실제 리그 위에서 종전 알고리즘과 같은 값이 나오는지는
    /// UnityParity의 SettledLedgerUnityParityTests가 판정한다.
    /// </summary>
    public sealed class PlacementTargetLedgerTests
    {
        private static RectNodeState Live()
            => RectNodeState.StretchFull
                .WithAnchoredPosition(new Vec2(10f, 20f))
                .WithLocalScale(new Vec3(2f, 3f, 4f))
                .WithLocalEuler(new Vec3(0f, 0f, 15f));

        // ── 기본 ─────────────────────────────────────────────────────

        [Test]
        public void 예약이_없으면_라이브_그대로다()
        {
            PlacementTargetLedger ledger = new();

            Assert.That(ledger.IsEmpty, Is.True);
            Assert.That(ledger.HasTargets("a"), Is.False);

            RectNodeState live = Live();
            RectNodeState settled = ledger.ApplyTo("a", live);

            Assert.That(settled.AnchoredPosition, Is.EqualTo(live.AnchoredPosition));
            Assert.That(settled.LocalScale, Is.EqualTo(live.LocalScale));
            Assert.That(settled.LocalEulerAngles, Is.EqualTo(live.LocalEulerAngles));
        }

        [Test]
        public void 다른_노드의_예약은_영향을_주지_않는다()
        {
            PlacementTargetLedger ledger = new();
            ledger.PublishAnchoredPosition("other", new Vec2(999f, 999f));

            RectNodeState settled = ledger.ApplyTo("a", Live());

            Assert.That(settled.AnchoredPosition, Is.EqualTo(new Vec2(10f, 20f)));
        }

        [Test]
        public void 각_종류가_해당_성분만_바꾼다()
        {
            PlacementTargetLedger position = new();
            position.PublishAnchoredPosition("a", new Vec2(-5f, 7f));

            RectNodeState afterPosition = position.ApplyTo("a", Live());
            Assert.That(afterPosition.AnchoredPosition, Is.EqualTo(new Vec2(-5f, 7f)));
            Assert.That(afterPosition.LocalScale, Is.EqualTo(new Vec3(2f, 3f, 4f)));
            Assert.That(afterPosition.LocalEulerAngles, Is.EqualTo(new Vec3(0f, 0f, 15f)));

            PlacementTargetLedger euler = new();
            euler.PublishLocalEuler("a", new Vec3(0f, 0f, 90f));

            RectNodeState afterEuler = euler.ApplyTo("a", Live());
            Assert.That(afterEuler.LocalEulerAngles, Is.EqualTo(new Vec3(0f, 0f, 90f)));
            Assert.That(afterEuler.AnchoredPosition, Is.EqualTo(new Vec2(10f, 20f)));
        }

        [Test]
        public void 스케일_예약은_z를_라이브_값으로_보존한다()
        {
            // 종전 ApplyEntry와 같은 규약이다 — 게시는 xy만 하고 z는 건드리지 않는다.
            PlacementTargetLedger ledger = new();
            ledger.PublishLocalScale("a", new Vec2(0.5f, 0.25f));

            RectNodeState settled = ledger.ApplyTo("a", Live());

            Assert.That(settled.LocalScale, Is.EqualTo(new Vec3(0.5f, 0.25f, 4f)));
        }

        // ── 슬롯 분리 (종전 구현의 함정 제거) ────────────────────────

        [Test]
        public void 위치와_스케일을_같은_노드에_겹쳐_예약할_수_있다()
        {
            // 종전 구현은 노드당 종류 하나만 담아서, 나중 게시가 앞의 것을 지웠다.
            // 실사용에서 겹친 적은 없지만(위치=track/depthY, 스케일=scale/depthScale),
            // 함정 자체를 없앤다.
            PlacementTargetLedger ledger = new();

            ledger.PublishAnchoredPosition("a", new Vec2(100f, 200f));
            ledger.PublishLocalScale("a", new Vec2(0.5f, 0.5f));
            ledger.PublishLocalEuler("a", new Vec3(0f, 0f, 45f));

            RectNodeState settled = ledger.ApplyTo("a", Live());

            Assert.That(settled.AnchoredPosition, Is.EqualTo(new Vec2(100f, 200f)), "앞선 위치 예약이 살아 있어야 한다");
            Assert.That(settled.LocalScale, Is.EqualTo(new Vec3(0.5f, 0.5f, 4f)));
            Assert.That(settled.LocalEulerAngles, Is.EqualTo(new Vec3(0f, 0f, 45f)));

            Assert.That(ledger.Count, Is.EqualTo(1), "한 노드는 한 엔트리다");
        }

        [Test]
        public void 같은_종류를_다시_게시하면_덮어쓴다()
        {
            PlacementTargetLedger ledger = new();

            ledger.PublishAnchoredPosition("a", new Vec2(1f, 1f));
            ledger.PublishAnchoredPosition("a", new Vec2(2f, 2f));

            Assert.That(ledger.ApplyTo("a", Live()).AnchoredPosition, Is.EqualTo(new Vec2(2f, 2f)));
        }

        // ── 수명 ─────────────────────────────────────────────────────

        [Test]
        public void Clear는_그_노드의_예약_전부를_지운다()
        {
            PlacementTargetLedger ledger = new();
            ledger.PublishAnchoredPosition("a", new Vec2(100f, 200f));
            ledger.PublishLocalScale("a", new Vec2(0.5f, 0.5f));
            ledger.PublishAnchoredPosition("b", new Vec2(1f, 1f));

            ledger.Clear("a");

            Assert.That(ledger.HasTargets("a"), Is.False);
            Assert.That(ledger.HasTargets("b"), Is.True);

            RectNodeState settled = ledger.ApplyTo("a", Live());
            Assert.That(settled.AnchoredPosition, Is.EqualTo(new Vec2(10f, 20f)));
            Assert.That(settled.LocalScale, Is.EqualTo(new Vec3(2f, 3f, 4f)));
        }

        [Test]
        public void 없는_키_Clear는_조용히_넘어간다()
        {
            PlacementTargetLedger ledger = new();

            // 커맨드 수명 관리가 Clear를 중복 호출할 수 있다 — 여기는 예외를 낼 자리가 아니다.
            Assert.DoesNotThrow(() => ledger.Clear("없음"));
            Assert.DoesNotThrow(() => ledger.Clear(null));
        }

        [Test]
        public void ClearAll은_장부를_비운다()
        {
            PlacementTargetLedger ledger = new();
            ledger.PublishAnchoredPosition("a", Vec2.Zero);
            ledger.PublishLocalScale("b", Vec2.One);

            ledger.ClearAll();

            Assert.That(ledger.IsEmpty, Is.True);
            Assert.That(ledger.Count, Is.Zero);
        }

        [Test]
        public void Count는_노드_수다()
        {
            PlacementTargetLedger ledger = new();

            ledger.PublishAnchoredPosition("a", Vec2.Zero);
            ledger.PublishLocalScale("a", Vec2.One);
            Assert.That(ledger.Count, Is.EqualTo(1));

            ledger.PublishAnchoredPosition("b", Vec2.Zero);
            Assert.That(ledger.Count, Is.EqualTo(2));
        }

        // ── 거부 ─────────────────────────────────────────────────────

        [Test]
        public void 빈_키_게시는_거부한다()
        {
            PlacementTargetLedger ledger = new();

            Assert.Throws<ArgumentException>(() => ledger.PublishAnchoredPosition(null, Vec2.Zero));
            Assert.Throws<ArgumentException>(() => ledger.PublishAnchoredPosition("", Vec2.Zero));
            Assert.Throws<ArgumentException>(() => ledger.PublishLocalScale("", Vec2.One));
            Assert.Throws<ArgumentException>(() => ledger.PublishLocalEuler("", Vec3.Zero));

            Assert.That(ledger.IsEmpty, Is.True, "실패한 게시가 엔트리를 남기면 안 된다");
        }

        [Test]
        public void null_키_조회는_라이브를_돌려준다()
        {
            // 조회는 묻는 쪽이 "없을 수 있다"를 아는 API다 — 여기서는 터지지 않는다.
            PlacementTargetLedger ledger = new();
            ledger.PublishAnchoredPosition("a", new Vec2(1f, 1f));

            Assert.That(ledger.HasTargets(null), Is.False);
            Assert.That(ledger.ApplyTo(null, Live()).AnchoredPosition, Is.EqualTo(new Vec2(10f, 20f)));
        }

        // ── 정착 계산으로 이어지는 자리 ──────────────────────────────

        [Test]
        public void 예약을_입힌_체인이_정착_좌표를_낸다()
        {
            // 이 조합이 "정착 상태 계산"의 전부다: ApplyTo로 상태를 만들고 RectChainMath에 넘긴다.
            // 유니티에 아무것도 쓰지 않으므로 복원이 없다.
            RectSpace space = RectSpace.Centered(1000f, 500f);

            RectNodeState liveTrack = RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(0f, 0f));
            RectNodeState liveScale = RectNodeState.StretchFull.WithLocalScale(Vec3.One);

            PlacementTargetLedger ledger = new();
            ledger.PublishAnchoredPosition("track", new Vec2(120f, -40f));
            ledger.PublishLocalScale("scale", new Vec2(2f, 2f));

            RectNodeState[] live = { liveTrack, liveScale };
            RectNodeState[] settled =
            {
                ledger.ApplyTo("track", liveTrack),
                ledger.ApplyTo("scale", liveScale),
            };

            Vec3 livePoint = RectChainMath.TransformPoint(live, space, new Vec3(50f, 0f, 0f));
            Vec3 settledPoint = RectChainMath.TransformPoint(settled, space, new Vec3(50f, 0f, 0f));

            // 라이브: (50, 0). 정착: track이 (120,-40)으로 가고 scale이 2배 → (120+100, -40)
            Assert.That(livePoint.X, Is.EqualTo(50f).Within(1e-4f));
            Assert.That(settledPoint.X, Is.EqualTo(220f).Within(1e-4f));
            Assert.That(settledPoint.Y, Is.EqualTo(-40f).Within(1e-4f));
        }
    }
}
