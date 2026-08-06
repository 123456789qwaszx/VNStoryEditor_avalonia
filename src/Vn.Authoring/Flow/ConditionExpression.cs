using System.Globalization;

namespace Vn.Authoring.Flow;

/// <summary>
/// 조건 식 평가기 (W36-b) — 툴이 판정할 수 있는 <b>지원 문법만</b> 읽는다:
///   비교 하나(<c>$x &gt;= 2</c>, <c>x == true</c>, <c>x != "b"</c>) 또는 단독 피연산자(<c>$flag</c>).
/// 조건 식은 자유 입력("게임이 평가할 식")이므로 그 밖의 문법은 <b>평가 실패</b>로
/// 정직하게 돌려준다 — 추측 보정 금지(원칙 §2.3). 실패한 블록은 자동 판정 없이
/// 기존 근사/수동 선택으로 남는다.
/// 변수 값 표현은 <see cref="StatFold"/>와 같은 원문 문자열이다.
/// </summary>
public static class ConditionExpression
{
    private static readonly string[] Operators = ["==", "!=", ">=", "<=", ">", "<"];

    public static bool TryEvaluate(
        string? expression,
        IReadOnlyDictionary<string, string> values,
        out bool result)
    {
        ArgumentNullException.ThrowIfNull(values);
        result = false;

        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        string text = expression.Trim();

        foreach (string op in Operators)
        {
            int index = text.IndexOf(op, StringComparison.Ordinal);

            if (index <= 0)
            {
                continue;
            }

            // 첫 비교 연산자 하나만 지원한다 — 두 번째가 있으면 지원 밖 문법이다.
            string left = text[..index].Trim();
            string right = text[(index + op.Length)..].Trim();

            if (left.Length == 0 || right.Length == 0 ||
                Operators.Any(other => right.Contains(other, StringComparison.Ordinal)))
            {
                return false;
            }

            return TryCompare(left, right, op, values, out result);
        }

        // 단독 피연산자 — bool 변수/리터럴의 참 거짓.
        return TryTruthiness(text, values, out result);
    }

    private static bool TryCompare(
        string left, string right, string op,
        IReadOnlyDictionary<string, string> values, out bool result)
    {
        result = false;

        if (!TryResolve(left, values, out string leftValue) ||
            !TryResolve(right, values, out string rightValue))
        {
            return false;
        }

        bool leftNumeric = TryNumber(leftValue, out double a);
        bool rightNumeric = TryNumber(rightValue, out double b);

        if (leftNumeric && rightNumeric)
        {
            result = op switch
            {
                "==" => Math.Abs(a - b) < 1e-9,
                "!=" => Math.Abs(a - b) >= 1e-9,
                ">=" => a >= b,
                "<=" => a <= b,
                ">" => a > b,
                "<" => a < b,
                _ => false,
            };
            return true;
        }

        // 숫자가 아니면 ==/!=만 — 원문(Ordinal) 비교. bool도 이 길로 온다(true/false 원문).
        if (op is "==" or "!=")
        {
            bool equal = string.Equals(
                Normalize(leftValue), Normalize(rightValue), StringComparison.Ordinal);
            result = op == "==" ? equal : !equal;
            return true;
        }

        return false; // 숫자 아닌 값의 대소 비교는 지원 밖
    }

    private static bool TryTruthiness(
        string operand, IReadOnlyDictionary<string, string> values, out bool result)
    {
        result = false;

        if (!TryResolve(operand, values, out string value))
        {
            return false;
        }

        string normalized = Normalize(value);

        if (normalized is "true" or "false")
        {
            result = normalized == "true";
            return true;
        }

        if (TryNumber(value, out double number))
        {
            result = Math.Abs(number) >= 1e-9;
            return true;
        }

        return false;
    }

    /// <summary>피연산자 → 값 원문. 리터럴(숫자·bool·따옴표 문자열)이거나 변수($ 유무 모두)다.</summary>
    private static bool TryResolve(
        string operand, IReadOnlyDictionary<string, string> values, out string value)
    {
        value = string.Empty;

        if (operand.Length >= 2 &&
            ((operand[0] == '"' && operand[^1] == '"') || (operand[0] == '\'' && operand[^1] == '\'')))
        {
            value = operand[1..^1];
            return true;
        }

        string normalized = Normalize(operand);

        if (normalized is "true" or "false" || TryNumber(operand, out _))
        {
            value = normalized is "true" or "false" ? normalized : operand;
            return true;
        }

        string name = operand.StartsWith('$') ? operand[1..] : operand;
        return values.TryGetValue(name, out value!) && value is not null;
    }

    private static bool TryNumber(string text, out double number)
        => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);

    private static string Normalize(string text) => text.Trim().ToLowerInvariant();
}
