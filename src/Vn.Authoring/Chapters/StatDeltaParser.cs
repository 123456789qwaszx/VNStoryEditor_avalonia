using System.Globalization;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 간선이 스탯에 하는 일이 <b>더하기인가 정하기인가</b> (2026-08-19).
///
/// 깃발에는 증감이 맞는 낱말이 아니다. `met_willow +1`은 "만난 횟수를 하나 늘린다"로 읽히고,
/// `+2`를 적으면 뜻이 없는데도 조용히 같은 결과가 된다. 켜고 끄는 것은 <b>지정</b>이다.
/// </summary>
public enum StatChangeKind
{
    /// <summary>`trust +2` — 지금 값에 더한다. 정수 스탯의 기본이자 유일한 방식이다.</summary>
    Add,

    /// <summary>`met_willow true` — 값을 그것으로 만든다. <b>bool 스탯만</b> 쓸 수 있다.</summary>
    Set
}

/// <param name="Key">Tier 2 스탯키. `스탯` 시트에 선언된 것만 쓸 수 있다.</param>
/// <param name="Amount">
/// <see cref="StatChangeKind.Add"/>이면 증감량, <see cref="StatChangeKind.Set"/>이면
/// <b>정할 값</b>이다(bool이므로 0 또는 1). 어느 쪽이든 정수만 (G-3).
/// </param>
public sealed record StatDelta(string Key, int Amount, StatChangeKind Kind = StatChangeKind.Add)
{
    /// <summary>이 변화가 깃발을 켜는가 — 화면·내보내기가 이 이름으로 묻는다.</summary>
    public bool IsSet => Kind == StatChangeKind.Set;
}

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

            // `met_willow = true`도 받는다 — 사람이 등호를 붙이는 쪽이 자연스러워 실제로
            // 그렇게 적는다. 정본 표기는 등호 없는 `met_willow true`다(안내서·툴이 그것을 쓴다).
            string key = entry[..split].TrimEnd().TrimEnd('=').Trim();
            string amount = entry[(split + 1)..].Trim();

            // 깃발을 켜고 끈다 (2026-08-19). `조건` 시트에서 bool을 true/false 낱말로
            // 쓰는 것과 같은 표기라, 기획자가 한 낱말만 알면 두 자리에서 통한다.
            if (Flag(amount) is { } flag)
            {
                if (!knownStatKeys.Contains(key))
                {
                    problems.Add(UnknownKey(entry, key, knownStatKeys));
                    continue;
                }

                deltas.Add(new StatDelta(key, flag ? 1 : 0, StatChangeKind.Set));
                continue;
            }

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
                problems.Add(UnknownKey(entry, key, knownStatKeys));
                continue;
            }

            deltas.Add(new StatDelta(key, parsed));
        }

        return new StatDeltaParseResult(deltas, problems);
    }

    /// <summary>`true`/`false`면 그 값, 아니면 null. 대소문자는 가리지 않는다.</summary>
    private static bool? Flag(string token) =>
        string.Equals(token, "true", StringComparison.OrdinalIgnoreCase) ? true
        : string.Equals(token, "false", StringComparison.OrdinalIgnoreCase) ? false
        : null;

    private static ConditionParseProblem UnknownKey(
        string entry, string key, IReadOnlyCollection<string> knownStatKeys) =>
        new(ConditionProblemKind.UnknownStatKey,
            entry,
            $"'{key}'는 `스탯` 시트에 없는 스탯키입니다. " +
            $"선언된 키: {(knownStatKeys.Count == 0 ? "(없음)" : string.Join(", ", knownStatKeys))}");
}

public sealed record StatDeltaParseResult(
    IReadOnlyList<StatDelta> Deltas,
    IReadOnlyList<ConditionParseProblem> Problems)
{
    public bool IsValid => Problems.Count == 0;
}
