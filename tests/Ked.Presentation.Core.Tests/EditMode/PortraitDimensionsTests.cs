using System.Collections.Generic;
using NUnit.Framework;

namespace Ked.Presentation.Core.Tests
{
    /// <summary>
    /// 초상 치수 조회의 골든 — 런타임 PortraitResolver의 규약을 그대로 고정한다.
    /// 여기가 갈라지면 폴드가 다른 스프라이트를 고르고, 그건 곧 폭 불일치다.
    /// </summary>
    public sealed class PortraitDimensionsTests
    {
        private const float Eps = 1e-4f;

        private static PortraitDimensionsFileDto Db(params PortraitDimensionDto[] entries)
            => new() { entries = new List<PortraitDimensionDto>(entries) };

        private static PortraitDimensionDto E(
            string character, string variant, string emotion, float width, float height = 1000f)
            => new() { character = character, variant = variant, emotion = emotion, width = width, height = height };

        [Test]
        public void 종횡비는_가로_나누기_세로다()
        {
            PortraitDimensionsFileDto db = Db(E("tyrant", "a", "01", 800f, 1600f));

            Assert.That(db.TryGetAspect("tyrant", "a", "01", out float aspect, out _), Is.True);
            Assert.That(aspect, Is.EqualTo(0.5f).Within(Eps));
        }

        [Test]
        public void 캐릭터_키는_대소문자를_가리지_않는다()
        {
            PortraitDimensionsFileDto db = Db(E("tyrant", "a", "01", 500f));

            Assert.That(db.TryGetAspect("Tyrant", "a", "01", out _, out _), Is.True);
            Assert.That(db.TryGetAspect(" TYRANT ", "a", "01", out _, out _), Is.True);
        }

        [Test]
        public void 변형은_문자열_전체가_키다()
        {
            // 초상 에셋이 <캐릭터>/<변형>/<표정>.png 폴더 규약을 쓰면서 변형은 폴더 이름 그 자체가 됐다.
            // 마지막 글자만 보던 종전 규칙이라면 school과 casual이 같은 키('l')로 뭉갠다.
            //
            // ⚠ 이 사본이 2026-08-21 ~ 08-23 사이 런타임과 갈려 있던 자리다. 툴만 옛 규칙이라
            // 미리보기가 엉뚱한 치수를 집고 게임은 올바른 것을 집는데 오류는 하나도 안 났다.
            PortraitDimensionsFileDto db = Db(
                E("tyrant", "school", "01", 700f),
                E("tyrant", "casual", "01", 400f));

            Assert.That(db.TryGetAspect("tyrant", "school", "01", out float school, out _), Is.True);
            Assert.That(school, Is.EqualTo(0.7f).Within(Eps));

            Assert.That(db.TryGetAspect("tyrant", "casual", "01", out float casual, out _), Is.True);
            Assert.That(casual, Is.EqualTo(0.4f).Within(Eps),
                "마지막 글자가 같아도 다른 변형이다");

            Assert.That(db.TryGetAspect("tyrant", " School ", "01", out _, out _), Is.True,
                "캐릭터 키와 마찬가지로 트림·소문자화는 한다");
        }

        [Test]
        public void 접두사가_붙은_옛_변형_키는_더는_같은_키가_아니다()
        {
            // 파일 이름에 캐릭터가 눌어붙던 시절의 'body_b'는 이제 'b'와 다른 키다.
            // 폴백 (캐릭터, "a", "01")도 없으므로 그대로 실패한다 — 짐작으로 잇지 않는다.
            PortraitDimensionsFileDto db = Db(E("tyrant", "body_b", "01", 700f));

            Assert.That(db.TryGetAspect("tyrant", "b", "01", out _, out string reason), Is.False);
            Assert.That(reason, Does.Contain("변형='b'"));
        }

        [Test]
        public void 표정은_두_자리로_접힌다()
        {
            PortraitDimensionsFileDto db = Db(E("tyrant", "a", "02", 640f));

            Assert.That(db.TryGetAspect("tyrant", "a", "2", out _, out _), Is.True);
            Assert.That(db.TryGetAspect("tyrant", "a", "02", out _, out _), Is.True);
        }

        [Test]
        public void 빈_인자는_기본_변형과_기본_표정이다()
        {
            PortraitDimensionsFileDto db = Db(E("tyrant", "a", "01", 600f));

            Assert.That(db.TryGetAspect("tyrant", "", "", out float aspect, out _), Is.True);
            Assert.That(aspect, Is.EqualTo(0.6f).Within(Eps));
        }

        [Test]
        public void 못_찾으면_기본_변형_01로_한_번_물러선다()
        {
            PortraitDimensionsFileDto db = Db(
                E("tyrant", "a", "01", 600f),
                E("tyrant", "b", "03", 900f));

            // (b, 09)는 없다 → (a, 01)로.
            Assert.That(db.TryGetAspect("tyrant", "b", "09", out float aspect, out _), Is.True);
            Assert.That(aspect, Is.EqualTo(0.6f).Within(Eps));
        }

        [Test]
        public void 규격_밖_표정도_폴백을_탄다()
        {
            // "abc"는 두 자리 코드로 접히지 않아 빈 코드가 된다 — 런타임도 그 키로 조회하고
            // 실패한 뒤 폴백으로 물러선다. 조용히 기본값으로 잇지 않는다.
            PortraitDimensionsFileDto db = Db(E("tyrant", "a", "01", 600f));

            Assert.That(db.TryGetAspect("tyrant", "a", "abc", out float aspect, out _), Is.True);
            Assert.That(aspect, Is.EqualTo(0.6f).Within(Eps));
        }

        [Test]
        public void 폴백도_없으면_이유와_함께_실패한다()
        {
            PortraitDimensionsFileDto db = Db(E("tyrant", "a", "01", 600f));

            Assert.That(db.TryGetAspect("amber", "a", "01", out _, out string reason), Is.False);
            Assert.That(reason, Does.Contain("amber"));
        }

        [Test]
        public void 캐릭터_키가_없으면_실패한다()
        {
            PortraitDimensionsFileDto db = Db(E("tyrant", "a", "01", 600f));

            Assert.That(db.TryGetAspect("", "a", "01", out _, out string reason), Is.False);
            Assert.That(reason, Does.Contain("캐릭터 키"));
        }

        [Test]
        public void 중복_키는_먼저_들어온_것이_이긴다()
        {
            // PortraitResolver가 TryAdd로 중복을 버리는 방향과 같다.
            PortraitDimensionsFileDto db = Db(
                E("tyrant", "a", "01", 600f),
                E("tyrant", "a", "01", 900f));

            Assert.That(db.TryGetAspect("tyrant", "a", "01", out float aspect, out _), Is.True);
            Assert.That(aspect, Is.EqualTo(0.6f).Within(Eps));
        }

        [Test]
        public void 높이가_0이면_종횡비를_내지_않는다()
        {
            PortraitDimensionsFileDto db = Db(E("tyrant", "a", "01", 600f, 0f));

            Assert.That(db.TryGetAspect("tyrant", "a", "01", out _, out string reason), Is.False);
            Assert.That(reason, Does.Contain("높이"));
        }

        // ── show의 faceToken 별칭 ────────────────────────────────────

        [TestCase("e1", "1")]
        [TestCase("E2", "2")]
        [TestCase("emo3", "3")]
        [TestCase("emotion4", "4")]
        [TestCase("face5", "5")]
        [TestCase("6", "6")]
        public void faceToken은_별칭_접두사를_벗는다(string token, string expected)
        {
            Assert.That(PortraitKeyNormalizer.ParseShowFaceAlias(token), Is.EqualTo(expected));
        }

        [Test]
        public void 빈_faceToken은_2다()
        {
            // 생략된 인자의 기본값("e1")과는 다른 규칙이다 — 런타임 ShowFaceAliasParser 그대로.
            // 다만 원문 추출기가 빈 토큰을 버리므로 폴드에서 이 갈래는 닿지 않는다
            // (reduction-boundary.md의 알려진 한계).
            Assert.That(PortraitKeyNormalizer.ParseShowFaceAlias(""), Is.EqualTo("2"));
            Assert.That(PortraitKeyNormalizer.ParseShowFaceAlias(null), Is.EqualTo("2"));
        }
    }
}
