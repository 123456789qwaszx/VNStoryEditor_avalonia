using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>챕터 조건이 대사 안의 <c>&lt;&lt;if&gt;&gt;</c>로 번역되는 모양</b>
/// (2026-09-17 소유자 · 계약서 §D1).
///
/// ⛔ <b>읽기 전용 함수다.</b> <c>$trust</c>(변수)로 심으면 읽기와 함께
/// <c>&lt;&lt;set $trust = 5&gt;&gt;</c>가 문법적으로 유효해지는데, 대사 중의 스탯 쓰기는
/// 세이브/로드 복귀와 <b>도달성 증명이 못 보는 뒷길</b>이다(2026-08-14에 J열을 폐지한 이유).
/// 함수에는 왼쪽 변이 없으므로 <b>금지가 아니라 불가능</b>이다.
///
/// ⚠ 이 표기는 <b>런타임과의 접점</b>이다 — 저쪽이 같은 이름의 Yarn 함수를 등록해야 한다.
/// 이름을 바꾸는 것은 양쪽 합의 사항이고, 그래서 여기서 <b>글자 그대로</b> 붙든다.
/// </summary>
public sealed class ConditionYarnTranslationTests
{
    [Fact]
    public void 스탯은_변수가_아니라_읽기_전용_함수로_나간다()
    {
        Assert.Equal("stat(\"trust\") >= 3", Yarn("trust >= 3"));
    }

    [Fact]
    public void 달러_표기는_한_글자도_안_나간다()
    {
        // ⛔ 하나라도 새면 그 자리에서 쓰기가 가능해진다 — 막는 지점이 여기 하나다.
        Assert.DoesNotContain("$", Yarn("trust >= 3; fatigue <= 2")!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("trust >= 3", "stat(\"trust\") >= 3")]
    [InlineData("trust <= 3", "stat(\"trust\") <= 3")]
    [InlineData("trust > 3", "stat(\"trust\") > 3")]
    [InlineData("trust < 3", "stat(\"trust\") < 3")]
    [InlineData("trust == 3", "stat(\"trust\") == 3")]
    public void 비교_다섯이_그대로_옮겨진다(string sheet, string expected)
    {
        Assert.Equal(expected, Yarn(sheet));
    }

    [Fact]
    public void 여러_항은_AND로_이어진다()
    {
        // 시트의 `;`가 AND다 — 기획자 언어와 Yarn의 유일한 차이 중 하나.
        Assert.Equal(
            "stat(\"trust\") >= 3 && stat(\"fatigue\") <= 2",
            Yarn("trust >= 3; fatigue <= 2"));
    }

    [Fact]
    public void 깃발도_같은_표기로_나간다()
    {
        // 깃발은 0/1 정수로 산다(2026-08-19) — 따로 만든 표기가 없다. 시트에서는
        // `깃발 == true`로 적고 파서가 1로 옮긴다.
        Assert.Equal("stat(\"met_willow\") == 1", Yarn("met_willow == true"));
    }

    [Fact]
    public void 공백이_든_키는_Yarn_식별자로_정규화된다()
    {
        // ⚠ Yarn 식별자에 공백이 못 들어간다 — 그대로 내면 <b>번들 전체가</b> 컴파일에
        //    실패한다. 규칙의 주인은 `YarnSyntax.SanitizeVariableName` 하나다.
        Assert.Equal("stat(\"호감 도\") >= 1", Yarn("호감 도 >= 1"));
    }

    [Fact]
    public void 식이_깨졌으면_번역하지_않고_이유를_든다()
    {
        // ⛔ 못 옮기는 것을 반쯤 옮겨 내보내면 런타임에서 깨진다 — 여기서 멈추고 말한다.
        ConditionYarnTranslation translated = Translate("trust >=");

        Assert.False(translated.IsTranslatable);
        Assert.Null(translated.Yarn);
        Assert.NotNull(translated.Problem);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static string? Yarn(string expression) => Translate(expression).Yarn;

    /// <summary>
    /// 시트에 적힌 그대로 해석해 번역한다 — 해석은 챕터 리더와 <b>같은 파서</b>를 지난다.
    /// 여기서 항을 손으로 지어 넣으면 파서가 못 만드는 모양까지 시험하게 된다.
    /// </summary>
    private static ConditionYarnTranslation Translate(string expression)
    {
        string[] keys = ["trust", "fatigue", "met_willow", "호감 도"];
        ConditionParseResult parsed = ConditionExpressionParser.Parse(expression, keys);

        return ConditionYarnTranslator.Translate(new ChapterCondition(
            Label: "조건",
            Expression: expression,
            Description: null,
            Parsed: parsed.Terms,
            IsValid: parsed.IsValid,
            SourceRow: 0));
    }
}
