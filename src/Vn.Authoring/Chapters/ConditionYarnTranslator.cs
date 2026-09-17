using Vn.Authoring.Rendering;

namespace Vn.Authoring.Chapters;

/// <param name="Yarn">번역된 Yarn 식. 번역할 수 없으면 null이고 <see cref="Problem"/>이 이유다.</param>
public sealed record ConditionYarnTranslation(string? Yarn, string? Problem)
{
    public bool IsTranslatable => Yarn is not null;
}

/// <summary>
/// 챕터 `조건` 시트의 식을 Yarn 식으로 번역한다 — <b>두 언어를 잇는 유일한 자리다.</b>
///
/// 시트의 식(`trust >= 3`, AND는 `;`)은 기획자 언어다: 런타임의 <c>EpisodeCondition</c>
/// 평가기가 그래프 해금에서 읽는다. 반면 에피소드 <b>대사 안</b>의 <c>&lt;&lt;if&gt;&gt;</c>는
/// Yarn VM이 평가하므로 Yarn이 아는 표기여야 한다 — <c>&lt;&lt;if trust &gt;= 3&gt;&gt;</c>은
/// Yarn에서 문법 오류다(실컴파일이 이 간극을 잡았다).
///
/// ⭐ <b>2026-09-17에 표기가 바뀌었다</b>: <c>$trust</c>(변수) → <c>stat("trust")</c>
/// (<b>읽기 전용 함수</b> · <see cref="YarnSyntax.StatFunction"/>).
///
/// <list type="bullet">
/// <item>계층을 하나로 합치기로 정해지면서(소유자) <b>작가 전용 스탯이라는 개념이
/// 없어졌다</b> — 대사가 물을 낱말은 챕터 스탯 하나뿐이다.</item>
/// <item>변수로 심으면 <c>&lt;&lt;set $trust = 5&gt;&gt;</c>가 문법적으로 유효해지고, 그
/// 쓰기는 세이브/로드 복귀와 도달성 증명이 <b>못 보는 뒷길</b>이다. 함수에는 왼쪽 변이 없다.</item>
/// <item>⚠ 2026-09-01(G0)에 저쪽의 <c>PublishStats</c> 다리가 걷혔는데 이 번역기는 <b>계속
/// <c>$trust</c>를 냈다</b> — 즉 그날부터 여기서 나간 대사 조건은 <b>런타임에서 조용히
/// 빗나가고 있었다</b>. 이 변경이 그 구멍을 닫는다.</item>
/// </list>
///
/// 원문 해석은 <see cref="ConditionExpressionParser"/>가 이미 했다(챕터 리더). 여기서는 그
/// 해석 결과(<see cref="ConditionTerm"/>)를 조립만 한다 — 식을 두 번 해석하는 곳을 만들지 않는다.
///
/// <b>남은 어휘는 스탯 비교 하나뿐이다</b> — 그래서 여기서 못 옮기는 항이 없다.
/// <c>cleared:</c>는 2026-08-25에 폐지됐고(파서가 오류로 막는다), 그 자리는 Bool 스탯이
/// 대신한다. 깃발도 결국 스탯이라 이 번역기가 그대로 옮긴다 — 예전에는 대사가 물을 수
/// 없는 질문이었던 것이 이제 물을 수 있는 질문이 됐다.
/// </summary>
public static class ConditionYarnTranslator
{
    public static ConditionYarnTranslation Translate(ChapterCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);

        if (!condition.IsValid || condition.Parsed.Count == 0)
        {
            return new ConditionYarnTranslation(
                null, $"조건 '{condition.Label}'의 식이 유효하지 않아 번역할 수 없습니다.");
        }

        var parts = new List<string>();

        foreach (ConditionTerm term in condition.Parsed)
        {
            // ⭐ <b>읽기 전용 함수</b>로 나간다 (2026-09-17 소유자). 표기는 조립기의 것
            //    하나를 쓴다 — 여기서 문자열로 덧붙이면 규약 사본이다.
            string variable = YarnSyntax.StatRead(term.Key);
            string comparison = term.Comparison switch
            {
                ConditionComparison.AtLeast => ">=",
                ConditionComparison.AtMost => "<=",
                ConditionComparison.Above => ">",
                ConditionComparison.Below => "<",
                _ => "=="
            };

            parts.Add($"{variable} {comparison} {term.Value}");
        }

        return new ConditionYarnTranslation(string.Join(" && ", parts), null);
    }
}
