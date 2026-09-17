namespace Vn.Authoring.Flow;

/// <summary>
/// 식 안의 <c>$이름</c> 토큰을 훑는 <b>한 자리</b>.
///
/// 식은 사람이 쓴 원문이라 파싱하지 않는다 — <b>토큰만</b> 집어내고 나머지 글자는 한 자도
/// 보지 않는다. 경계를 여기 한 곳에만 적는 이유는 <c>$열쇠</c>와 <c>$열쇠2</c>를 가르는
/// 규칙이 둘로 갈리면 그 어긋남이 Yarn 컴파일에서야 드러나기 때문이다.
///
/// ⛔ <b>더 이상 "변수를 다루는" 자리가 아니다</b> (2026-09-17). 옛 머리글이 적어 둔 두
/// 소비자 <c>Tier1Namespace</c>(챕터 접두)와 <c>ProjectEditor.SetAssignments</c>(개명 전파)는
/// <b>둘 다 작가 변수와 함께 삭제됐다</b>. 갈아 끼우던 <c>Rewrite</c>도 그때 함께 죽었다 —
/// 스탯 키 개명은 이제 문법을 아는 <see cref="Chapters.ConditionExpressionParser.ReplaceStatKey"/>가
/// 한다.
///
/// <b>남은 소비자는 하나다</b> — <c>YarnBundleEmitter.ValidateProgressionStatReferences</c>.
/// 쓰임이 뒤집혔다: 예전에는 <i>변수를 고치려고</i> 찾았고, 지금은 <b>사람이 손으로 적은
/// <c>$</c>를 잡아 산출을 막으려고</b> 찾는다. 대사가 스탯을 읽는 길은 <c>stat("키")</c>
/// 함수뿐이고, 함수에는 왼쪽 변이 없어 쓸 수가 없다.
/// </summary>
public static class VariableTokens
{
    /// <summary>식에 나오는 <c>$이름</c>들. 빈 이름(<c>$</c> 하나)은 세지 않는다.</summary>
    public static IEnumerable<string> Names(string? expression)
    {
        if (string.IsNullOrEmpty(expression))
        {
            yield break;
        }

        for (int index = 0; index < expression.Length; index++)
        {
            if (expression[index] != '$')
            {
                continue;
            }

            int start = index + 1;
            int end = start;

            while (end < expression.Length && IsNameLetter(expression[end]))
            {
                end++;
            }

            if (end > start)
            {
                yield return expression[start..end];
            }

            index = end - 1;
        }
    }

    // ⛔ `Rewrite`는 2026-09-17에 걷혔다. <c>$이름</c>을 갈아 끼우는 일이었고, 그것을 쓰던
    //    두 자리(챕터 접두·개명 전파)가 작가 변수와 함께 사라졌다. 남아 있으면 "대사의
    //    식에서 이름을 갈아 끼우는 길이 있다"고 말하는 셈인데, 이제 그런 길은 없다.

    private static bool IsNameLetter(char letter) => char.IsLetterOrDigit(letter) || letter == '_';
}
