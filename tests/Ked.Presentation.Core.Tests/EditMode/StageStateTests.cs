using System;
using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// 무대 상태의 축들. 리듀서가 이 위에서 돌기 전에 축 자체의 규약을 고정한다.
    /// </summary>
    public sealed class StageStateTests
    {
        private static StageState NewState() => new(RectSpace.Centered(1920f, 1080f));

        private static StageCommand Cmd(string name, params string[] args)
            => new(name, args, "test.yarn:1");

        // ── StageCommand ─────────────────────────────────────────────

        [Test]
        public void Arg는_범위_밖과_빈_값에_fallback을_준다()
        {
            // yarn의 생략 인자가 빈 문자열로 오는 경우가 있다 — 둘을 같이 다뤄야 한다.
            StageCommand cmd = Cmd("show", "c1", "");

            Assert.That(cmd.Arg(0), Is.EqualTo("c1"));
            Assert.That(cmd.Arg(1, "e1"), Is.EqualTo("e1"), "빈 문자열은 fallback");
            Assert.That(cmd.Arg(9, "기본"), Is.EqualTo("기본"), "범위 밖은 fallback");
            Assert.That(cmd.Arg(9), Is.Null);
            Assert.That(cmd.Arg(-1, "기본"), Is.EqualTo("기본"));
        }

        [Test]
        public void 인자가_없어도_만들_수_있다()
        {
            StageCommand cmd = new("shot_reset", null);

            Assert.That(cmd.Args, Is.Empty);
            Assert.That(cmd.Arg(0, "x"), Is.EqualTo("x"));
        }

        [Test]
        public void 이름_없는_커맨드는_거부한다()
        {
            Assert.Throws<ArgumentNullException>(() => new StageCommand(null, new[] { "a" }));
        }

        [Test]
        public void ToString은_출처를_함께_보여준다()
        {
            // Unhandled 목록을 사람이 읽고 고칠 수 있어야 한다.
            Assert.That(Cmd("place_left", "c1", "bust").ToString(),
                Is.EqualTo("<<place_left c1 bust>> @ test.yarn:1"));
        }

        // ── Unhandled ────────────────────────────────────────────────

        [Test]
        public void Unhandled는_커맨드와_이유를_함께_남긴다()
        {
            StageState state = NewState();

            state.AddUnhandled(Cmd("bg_show", "room"), "배경 축이 아직 없다");

            Assert.That(state.Unhandled.Count, Is.EqualTo(1));
            Assert.That(state.Unhandled[0].Reason, Is.EqualTo("배경 축이 아직 없다"));
            Assert.That(state.Unhandled[0].ToString(), Does.Contain("bg_show"));
            Assert.That(state.Unhandled[0].ToString(), Does.Contain("배경 축이 아직 없다"));
        }

        // ── 슬롯 ─────────────────────────────────────────────────────

        [Test]
        public void 노드_키는_슬롯과_스키마_id의_결합이다()
        {
            Assert.That(StageState.NodeKeyOf("c1", "CharSlot_Track"), Is.EqualTo("c1/CharSlot_Track"));
            Assert.That(StageState.NodeKeyOf("c1", RigSchemaLoader.RootKey), Is.EqualTo("c1/__root"));
        }

        [Test]
        public void 스폰된_슬롯만_존재로_친다()
        {
            StageState state = NewState();

            Assert.That(state.HasSlot("c1"), Is.False);
            Assert.That(state.HasSlot(null), Is.False);

            state.RegisterSlot("c1");

            Assert.That(state.HasSlot("c1"), Is.True);
            Assert.That(state.Slots, Is.EquivalentTo(new[] { "c1" }));
        }

        // ── 배역·별칭 해석 ───────────────────────────────────────────

        [Test]
        public void 슬롯_키는_그대로_풀린다()
        {
            StageState state = NewState();
            state.RegisterSlot("c1");

            Assert.That(state.TryResolveSlot("c1", out string slot), Is.True);
            Assert.That(slot, Is.EqualTo("c1"));
        }

        [Test]
        public void 캐릭터_키는_배역_맵으로_풀린다()
        {
            StageState state = NewState();
            state.RegisterSlot("c1");
            state.SetCast("c1", "parkeunseol");

            Assert.That(state.TryResolveSlot("parkeunseol", out string slot), Is.True);
            Assert.That(slot, Is.EqualTo("c1"));

            Assert.That(state.TryGetCharacter("c1", out string character), Is.True);
            Assert.That(character, Is.EqualTo("parkeunseol"));
        }

        [Test]
        public void 별칭은_캐릭터를_거쳐_슬롯까지_풀린다()
        {
            StageState state = NewState();
            state.RegisterSlot("c1");
            state.SetCast("c1", "parkeunseol");
            state.SetAlias("@3", "parkeunseol");

            Assert.That(state.TryResolveSlot("@3", out string slot), Is.True);
            Assert.That(slot, Is.EqualTo("c1"));
        }

        [Test]
        public void 별칭이_슬롯을_직접_가리켜도_풀린다()
        {
            StageState state = NewState();
            state.RegisterSlot("c1");
            state.SetAlias("@3", "c1");

            Assert.That(state.TryResolveSlot("@3", out string slot), Is.True);
            Assert.That(slot, Is.EqualTo("c1"));
        }

        [Test]
        public void 스폰되지_않은_슬롯은_풀리지_않는다()
        {
            // 배역만 있고 리그가 없으면 커맨드를 접을 대상이 없다 — Unhandled로 가야 한다.
            StageState state = NewState();
            state.SetCast("c1", "parkeunseol");

            Assert.That(state.TryResolveSlot("parkeunseol", out _), Is.False);
            Assert.That(state.TryResolveSlot("c1", out _), Is.False);
            Assert.That(state.TryResolveSlot("모르는키", out _), Is.False);
            Assert.That(state.TryResolveSlot(null, out _), Is.False);
        }

        [Test]
        public void 재배역은_이전_관계를_정리한다()
        {
            StageState state = NewState();
            state.RegisterSlot("c1");
            state.RegisterSlot("c2");

            state.SetCast("c1", "albedo");
            state.SetCast("c2", "laru");

            // 같은 캐릭터를 다른 슬롯으로 옮긴다.
            state.SetCast("c2", "albedo");

            Assert.That(state.TryResolveSlot("albedo", out string slot), Is.True);
            Assert.That(slot, Is.EqualTo("c2"), "캐릭터는 새 슬롯을 가리켜야 한다");

            // laru는 c2에서 밀려났다.
            Assert.That(state.TryResolveSlot("laru", out _), Is.False);

            // c1은 배역이 비었다.
            Assert.That(state.TryGetCharacter("c1", out _), Is.False);
        }

        [Test]
        public void 같은_슬롯에_다른_캐릭터를_앉히면_갈아탄다()
        {
            StageState state = NewState();
            state.RegisterSlot("c1");

            state.SetCast("c1", "albedo");
            state.SetCast("c1", "laru");

            Assert.That(state.TryGetCharacter("c1", out string character), Is.True);
            Assert.That(character, Is.EqualTo("laru"));
            Assert.That(state.TryResolveSlot("albedo", out _), Is.False, "이전 배역은 사라진다");
        }

        [Test]
        public void 빈_키는_거부한다()
        {
            StageState state = NewState();

            Assert.Throws<ArgumentException>(() => state.RegisterSlot(""));
            Assert.Throws<ArgumentException>(() => state.SetCast("", "a"));
            Assert.Throws<ArgumentException>(() => state.SetCast("c1", ""));
            Assert.Throws<ArgumentException>(() => state.SetAlias("", "c1"));
            Assert.Throws<ArgumentException>(() => state.SetAlpha("", 1f));
        }

        // ── 가시성 축 ────────────────────────────────────────────────

        [Test]
        public void alpha_기록이_없으면_1이다()
        {
            StageState state = NewState();

            Assert.That(state.GetAlpha("c1/CharacterPortraitSprite_Root"), Is.EqualTo(1f));

            state.SetAlpha("c1/CharacterPortraitSprite_Root", 0f);

            Assert.That(state.GetAlpha("c1/CharacterPortraitSprite_Root"), Is.EqualTo(0f));
        }

        // ── 클레임 라우팅 ────────────────────────────────────────────

        [Test]
        public void 트랜스폼_클레임은_트리로_alpha_클레임은_가시성_축으로_간다()
        {
            StageState state = NewState();
            state.Nodes.Add("c1/CharSlot_Track", null, RectNodeState.StretchFull);

            state.Apply(StageNodeClaim.AnchoredPosition("c1/CharSlot_Track", new Vec2(30f, -10f)));
            state.Apply(FadeOutReduction.Reduce("c1/CharacterPortraitSprite_Root"));

            Assert.That(state.Nodes.GetState("c1/CharSlot_Track").AnchoredPosition,
                Is.EqualTo(new Vec2(30f, -10f)));

            Assert.That(state.GetAlpha("c1/CharacterPortraitSprite_Root"), Is.EqualTo(0f));

            // alpha 노드는 트리에 만들어지지 않는다 — 다른 축이다.
            Assert.That(state.Nodes.Contains("c1/CharacterPortraitSprite_Root"), Is.False);
        }

        // ── 구조 축 ──────────────────────────────────────────────────

        [Test]
        public void 부착은_스테이지와_레이어를_담는다()
        {
            StageState state = NewState();

            Assert.That(state.TryGetAttachment("c1", out _), Is.False);

            state.SetAttachment("c1", new SlotAttachment("stage01", "front"));

            Assert.That(state.TryGetAttachment("c1", out SlotAttachment attachment), Is.True);
            Assert.That(attachment.StageKey, Is.EqualTo("stage01"));
            Assert.That(attachment.LayerKey, Is.EqualTo("front"));
        }

        // ── 복제 (리듀서 순수성의 전제) ──────────────────────────────

        [Test]
        public void Clone은_모든_축에서_원본과_독립이다()
        {
            StageState origin = NewState();
            origin.Nodes.Add("c1/CharSlot_Track", null, RectNodeState.StretchFull);
            origin.RegisterSlot("c1");
            origin.SetCast("c1", "albedo");
            origin.SetAlias("@1", "albedo");
            origin.SetAlpha("c1/Root", 0f);
            origin.SetAttachment("c1", new SlotAttachment("stage00", "mid"));
            origin.Shot = new ShotIntentState(2f, new Vec2(10f, 0f), Vec2.Zero);
            origin.AddUnhandled(Cmd("bg_show"), "이유");

            StageState clone = origin.Clone();

            // 사본을 전 축에서 흔든다.
            clone.Nodes.SetState("c1/CharSlot_Track",
                RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(99f, 99f)));
            clone.RegisterSlot("c2");
            clone.SetCast("c1", "laru");
            clone.SetAlias("@1", "laru");
            clone.SetAlpha("c1/Root", 1f);
            clone.SetAttachment("c1", new SlotAttachment("stage02", "back"));
            clone.Shot = ShotIntentState.Default;
            clone.AddUnhandled(Cmd("other"), "다른 이유");

            // 원본은 그대로여야 한다.
            Assert.That(origin.Nodes.GetState("c1/CharSlot_Track").AnchoredPosition, Is.EqualTo(Vec2.Zero));
            Assert.That(origin.Slots, Is.EquivalentTo(new[] { "c1" }));
            Assert.That(origin.TryGetCharacter("c1", out string character), Is.True);
            Assert.That(character, Is.EqualTo("albedo"));
            Assert.That(origin.TryResolveSlot("@1", out _), Is.True);
            Assert.That(origin.GetAlpha("c1/Root"), Is.EqualTo(0f));
            Assert.That(origin.TryGetAttachment("c1", out SlotAttachment attachment), Is.True);
            Assert.That(attachment.StageKey, Is.EqualTo("stage00"));
            Assert.That(origin.Shot.Zoom, Is.EqualTo(2f));
            Assert.That(origin.Unhandled.Count, Is.EqualTo(1));
        }

        [Test]
        public void Clone은_루트_공간을_이어받는다()
        {
            StageState origin = new(new RectSpace(new Vec2(800f, 600f), Vec2.Half));

            Assert.That(origin.Clone().Nodes.RootSpace.Size, Is.EqualTo(new Vec2(800f, 600f)));
        }
    }
}
