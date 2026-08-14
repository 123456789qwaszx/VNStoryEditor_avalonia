using System.Globalization;

namespace Vn.Authoring.Chapters;

/// <param name="Key">Tier 2 스탯키. `스탯` 시트에 선언된 것만 쓸 수 있다.</param>
/// <param name="Amount">증감량. 정수만 (G-3).</param>
public sealed record StatDelta(string Key, int Amount);

/// <summary>
/// `스탯변화` 문법(`trust +1; anger -1`)을 읽는다 — <b>이 문법을 읽는 유일한 자리다.</b>
///
/// 사는 곳은 챕터 <b>간선</b> 시트다 (2026-08-14 소유자 결정 — 대본 J열 폐지 후 부활).
/// 스탯은 에피소드 <b>사이</b>를 건너는 순간에만 변한다: 간선을 타는 순간 1회 커밋이라
/// 세이브/로드 복귀가 언제나 일관되고, 도달성 증명(G7)이 근사 없이 정확값으로 전이한다.
///
/// <see cref="ConditionExpressionParser"/>와 같은 규칙을 따른다 — 시트·행·열을 모르고,
/// 정수만 받고, 미등록 키를 고쳐 주지 않는다.
/// </summary>
public static class StatDeltaParser
{
    public static StatDeltaParseResult Parse(string? text, IReadOnlyCollection<string> knownStatKeys)
    {
        ArgumentNullException.ThrowIfNull(knownStatKeys);

        var deltas = new List<StatDelta>();
        var problems = new List<ConditionParseProblem>();
        string source = (text ?? string.Empty).Trim();

        if (source.Length == 0)
        {
            return new StatDeltaParseResult(deltas, problems);
        }

        foreach (string raw in source.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string entry = raw.Trim();

            if (entry.Length == 0)
            {
                continue;
            }

            // "trust +1" — 마지막 공백에서 가른다. 스탯키에 공백이 없다는 전제이며,
            // 그 전제가 깨지면 아래에서 미등록 키로 잡힌다(조용히 넘어가지 않는다).
            int split = entry.LastIndexOf(' ');

            if (split <= 0)
            {
                problems.Add(new ConditionParseProblem(
                    ConditionProblemKind.TermMalformed,
                    entry,
                    $"'{entry}' — '스탯키 증감량' 형태여야 합니다(예: trust +1)."));
                continue;
            }

            string key = entry[..split].Trim();
            string amount = entry[(split + 1)..].Trim();

            if (!int.TryParse(amount, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsed))
            {
                problems.Add(new ConditionParseProblem(
                    ConditionProblemKind.ValueNotInteger,
                    entry,
                    amount.Contains('.', StringComparison.Ordinal)
                        ? $"'{entry}' — 스탯 증감량은 정수만 됩니다. 소수점은 쓸 수 없습니다(G-3)."
                        : $"'{entry}' — 증감량 '{amount}'이 정수가 아닙니다."));
                continue;
            }

            if (!knownStatKeys.Contains(key))
            {
                problems.Add(new ConditionParseProblem(
                    ConditionProblemKind.UnknownStatKey,
                    entry,
                    $"'{key}'는 `스탯` 시트에 없는 스탯키입니다. " +
                    $"선언된 키: {(knownStatKeys.Count == 0 ? "(없음)" : string.Join(", ", knownStatKeys))}"));
                continue;
            }

            deltas.Add(new StatDelta(key, parsed));
        }

        return new StatDeltaParseResult(deltas, problems);
    }
}

public sealed record StatDeltaParseResult(
    IReadOnlyList<StatDelta> Deltas,
    IReadOnlyList<ConditionParseProblem> Problems)
{
    public bool IsValid => Problems.Count == 0;
}
