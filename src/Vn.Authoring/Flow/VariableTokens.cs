namespace Vn.Authoring.Flow;

/// <summary>
/// 조건식 안의 <c>$이름</c> 토큰을 훑는 <b>한 자리</b>.
///
/// 식은 사람이 쓴 원문이라 파싱하지 않는다 — <b>변수 토큰만</b> 갈아 끼우고 나머지 글자는
/// 한 자도 손대지 않는다. 이 규칙을 두 곳에 적으면 한쪽이 <c>$열쇠</c>와 <c>$열쇠2</c>를
/// 가르지 못하는 날이 오고, 그 어긋남은 Yarn 컴파일에서야 드러난다.
///
/// 지금 이것을 쓰는 두 자리:
/// <list type="bullet">
///   <item><see cref="Rendering.Tier1Namespace"/> — 챕터 네임스페이스 접두 붙이기</item>
///   <item><c>ProjectEditor.SetAssignments</c> — 아이템·능력 개명 전파</item>
/// </list>
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

    /// <summary>
    /// <c>$이름</c>을 <paramref name="map"/>이 정한 이름으로 갈아 끼운다. <c>$</c>는 남고,
    /// 이름이 아닌 글자는 통과한다. 한 토큰은 <b>한 번만</b> 매핑되므로 맞바꾸기(A↔B)도
    /// 그대로 성립한다.
    /// </summary>
    public static string Rewrite(string? expression, Func<string, string> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        if (string.IsNullOrEmpty(expression))
        {
            return expression ?? string.Empty;
        }

        var builder = new System.Text.StringBuilder(expression.Length + 16);
        int index = 0;

        while (index < expression.Length)
        {
            if (expression[index] != '$')
            {
                builder.Append(expression[index]);
                index++;
                continue;
            }

            int start = ++index;

            while (index < expression.Length && IsNameLetter(expression[index]))
            {
                index++;
            }

            builder.Append('$').Append(map(expression[start..index]));
        }

        return builder.ToString();
    }

    private static bool IsNameLetter(char letter) => char.IsLetterOrDigit(letter) || letter == '_';
}
