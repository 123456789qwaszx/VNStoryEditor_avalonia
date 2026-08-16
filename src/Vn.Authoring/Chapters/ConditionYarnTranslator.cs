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
/// 시트의 식(`trust >= 3`, AND는 `;`)은 기획자 언어다: 스탯키에 <c>$</c>가 없고, 런타임의
/// <c>EpisodeCondition</c> 평가기가 그래프 해금에서 읽는다. 반면 에피소드 <b>대사 안</b>의
/// <c>&lt;&lt;if&gt;&gt;</c>는 Yarn VM이 평가하므로 변수에 <c>$</c>가 붙어야 한다 — 브리지가
/// 대화 전에 심어 주는 것이 바로 <c>$trust</c>류다(U9). 실컴파일이 이 간극을 잡았다:
/// <c>&lt;&lt;if trust &gt;= 3&gt;&gt;</c>은 Yarn에서 문법 오류다.
///
/// 원문 해석은 <see cref="ConditionExpressionParser"/>가 이미 했다(챕터 리더). 여기서는 그
/// 해석 결과(<see cref="ConditionTerm"/>)를 조립만 한다 — 식을 두 번 해석하는 곳을 만들지 않는다.
///
/// <b><c>cleared:</c>는 번역하지 않는다.</b> 클리어 여부는 런타임이 Yarn 밖에서 추적하고
/// 브리지는 스탯만 왕복한다 — Yarn 안에서 답할 수 없는 질문이라, 대사 조건에 쓰면 오류로
/// 알린다. 그건 챕터 그래프(표시·해금·간선)의 어휘다.
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
            if (term.Kind == ConditionTermKind.EpisodeCleared)
            {
                return new ConditionYarnTranslation(
                    null,
                    $"조건 '{condition.Label}'의 cleared:{term.Key}는 에피소드 대사 조건으로 쓸 수 " +
                    "없습니다. 클리어 여부는 Yarn 밖(런타임)의 것이라 대사가 물을 수 없습니다 — " +
                    "챕터 그래프의 표시·해금·간선 조건으로만 쓰세요.");
            }

            // $ 표기는 조립기의 것 하나를 쓴다 — 여기서 문자열로 덧붙이면 규약 사본이다.
            string variable = YarnSyntax.NormalizeVariable(term.Key);
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
