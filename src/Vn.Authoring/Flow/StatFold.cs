using System.Globalization;
using Vn.Authoring.Model;

namespace Vn.Authoring.Flow;

/// <summary>
/// 스탯 HUD(X3)의 값 계산 — 등록된 변수의 초기값에서 시작해 선택 라인까지의
/// <c>&lt;&lt;set&gt;&gt;</c>을 문서 순서로 누적한다.
///
/// MiniStageFold와 같은 갈래 근사다: 조건·선택 갈래 안의 set도 전부 적용한다.
/// 정확한 분기 시뮬이 아니므로 화면은 이것이 근사임을 숨기지 않는다(규칙 14).
/// 순수 함수이고 결과를 저장하지 않는다.
/// </summary>
public static class StatFold
{
    public sealed record StatValue(string Variable, string Display);

    /// <param name="initial">등록된 변수와 초기값 — HUD에 보일 목록이자 순서다.</param>
    /// <param name="operations">선택 라인까지의 set, 문서 순서.</param>
    public static IReadOnlyList<StatValue> Fold(
        IEnumerable<(string Variable, string Value)> initial,
        IEnumerable<(string Variable, SetOperatorKind Operator, string Value)> operations)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(operations);

        var order = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach ((string variable, string value) in initial)
        {
            if (variable.Length > 0 && values.TryAdd(variable, value))
            {
                order.Add(variable);
            }
        }

        foreach ((string variable, SetOperatorKind op, string value) in operations)
        {
            Apply(values, variable, op, value);
        }

        return order.Select(variable => new StatValue(variable, Format(values[variable]))).ToList();
    }

    /// <summary>
    /// set 하나를 값 사전에 적용한다 — HUD 누적과 조건 값 시뮬(W36-b)이 같은 연산 하나를
    /// 쓴다(사본 금지). 등록되지 않은 변수는 스탯이 아니다 — HUD 범위 밖(set 행에서는 보인다).
    /// </summary>
    public static void Apply(
        Dictionary<string, string> values, string variable, SetOperatorKind op, string value)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (!values.TryGetValue(variable, out string? current))
        {
            return;
        }

        values[variable] = op switch
        {
            SetOperatorKind.Assign => value,
            SetOperatorKind.Add => ApplyNumeric(current, value, static (a, b) => a + b),
            SetOperatorKind.Subtract => ApplyNumeric(current, value, static (a, b) => a - b),
            _ => current
        };
    }

    /// <summary>수치가 아니면(누적 불가) 마지막 원문을 그대로 둔다 — 조용히 0으로 만들지 않는다.</summary>
    private static string ApplyNumeric(string current, string delta, Func<double, double, double> apply)
    {
        if (double.TryParse(current, NumberStyles.Float, CultureInfo.InvariantCulture, out double a) &&
            double.TryParse(delta, NumberStyles.Float, CultureInfo.InvariantCulture, out double b))
        {
            return apply(a, b).ToString("0.###", CultureInfo.InvariantCulture);
        }

        return current;
    }

    private static string Format(string value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double numeric)
            ? numeric.ToString("0.###", CultureInfo.InvariantCulture)
            : value;
    }
}
