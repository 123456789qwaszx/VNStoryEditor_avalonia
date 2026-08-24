using System.Globalization;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 조건 항이 무엇을 묻는가.
///
/// <b>지금은 하나뿐이다.</b> 그래도 열거형으로 남기는 이유는 코어와 같다 — 시나리오
/// 수명의 상태([1] 영구 계층)가 제대로 서면 그때 갈래가 다시 늘어난다.
///
/// ⚠ <c>EpisodeCleared</c>는 2026-08-25에 폐지됐다. 코어가 클리어 이력 추적을 걷으면서
/// <c>ProgressionCondition</c>이 스탯 하나로 줄었고, 두 곳에 다른 어휘를 두면 저작에서
/// 통과한 조건이 게임에서 거부된다.
/// </summary>
public enum ConditionTermKind
{
    /// <summary><c>trust &gt;= 3</c> — Tier 2 스탯과 정수의 비교.</summary>
    StatComparison
}

/// <summary>
/// 규격 §3.1이 정한 비교 연산자 전부. <b>여기 없는 연산자는 받지 않는다</b> —
/// 툴이 런타임에 없는 비교를 만들어 내면 저작 시점에 통과한 조건이 게임에서 깨진다.
/// 넓혀야 하면 이 열거형과 <see cref="ConditionExpressionParser"/> 한 곳만 고치면 된다.
/// </summary>
public enum ConditionComparison
{
    AtLeast,
    AtMost,
    Exactly,

    /// <summary><c>&gt;</c> (2026-08-16 소유자 개방 — &lt;·&gt; 드롭다운).</summary>
    Above,

    /// <summary><c>&lt;</c> (2026-08-16 소유자 개방).</summary>
    Below
}

/// <param name="Key">스탯키.</param>
public sealed record ConditionTerm(
    ConditionTermKind Kind,
    string Key,
    ConditionComparison Comparison,
    int Value);

public enum ConditionProblemKind
{
    Empty,
    TermMalformed,
    OperatorNotSupported,
    ValueNotInteger,
    UnknownStatKey,

    /// <summary>
    /// 폐지된 <c>cleared:</c> 문법 (2026-08-25). 조용히 무시하지 않고 짚어서
    /// <b>고치는 법까지</b> 말한다 — 무시하면 관문이 통째로 사라져 잠긴 길이 열린다.
    /// </summary>
    ClearedRetired
}

/// <param name="Fragment">문제가 된 조각 원문. 사람이 셀에서 눈으로 찾을 수 있어야 한다.</param>
public sealed record ConditionParseProblem(
    ConditionProblemKind Kind,
    string Fragment,
    string Message);

public sealed record ConditionParseResult(
    IReadOnlyList<ConditionTerm> Terms,
    IReadOnlyList<ConditionParseProblem> Problems)
{
    /// <summary>문제가 하나도 없을 때만 참. 부분 성공을 성공으로 세지 않는다.</summary>
    public bool IsValid => Problems.Count == 0;
}

/// <summary>
/// 조건식 파서 — <b>이 레이어에서 조건식을 읽는 유일한 자리다.</b>
///
/// 챕터 워크북(`조건`·`표시조건`·`해금조건`·`간선`의 조건)과 에피소드 워크북(G2의 `조건라벨`이
/// 가리키는 식)이 같은 문법을 쓴다. 규약 사본 금지(지시서 §2 승계 원칙) — 두 번째 구현이
/// 생기면 한쪽만 고쳐지는 날이 오고, 그날 저작 시점 검증과 실행이 갈린다.
///
/// <b>시트·행·열을 모른다.</b> 좌표는 호출자가 붙인다. 그래야 에피소드 워크북(G2)이 자기
/// 좌표계로 같은 파서를 그대로 쓴다.
///
/// 문법 (§3.1):
/// <code>
/// trust &gt;= 3            스탯 비교. 값은 정수만 (G-3)
/// anger &lt;= 0
/// trust == 5
/// A ; B                 AND
/// </code>
/// ⛔ <c>cleared:</c>는 2026-08-25에 폐지됐다 — 진행 코어가 클리어 이력을 기억하지 않는다.
/// 같은 것을 <b>Bool 스탯</b>으로 적는다: 떠나는 간선에서 깃발을 켜고(<c>깃발 = 1</c>)
/// 관문에서 읽는다(<c>깃발 == 1</c>). 문법은 알아보되 <b>오류로 짚고 고치는 법을 말한다</b>.
/// </summary>
public static class ConditionExpressionParser
{
    private const string ClearedPrefix = "cleared:";

    /// <summary>
    /// 소수점 금지의 자리 (G-3). 정수 고정이 경계값 버그(2.5 &gt;= 3)를 막고,
    /// 동시에 G7 도달성 증명의 유한 상태공간을 가능하게 한다.
    /// </summary>
    /// <param name="knownStatKeys">
    /// `스탯` 시트가 선언한 키. 여기 없는 키는 오류다 — <b>오타를 비슷한 이름으로 고쳐 주지 않는다</b>
    /// (승계 원칙: 자동 추측 금지).
    /// </param>
    public static ConditionParseResult Parse(string? text, IReadOnlyCollection<string> knownStatKeys)
    {
        ArgumentNullException.ThrowIfNull(knownStatKeys);

        var terms = new List<ConditionTerm>();
        var problems = new List<ConditionParseProblem>();
        string source = (text ?? string.Empty).Trim();

        if (source.Length == 0)
        {
            problems.Add(new ConditionParseProblem(
                ConditionProblemKind.Empty,
                string.Empty,
                "조건식이 비어 있습니다."));

            return new ConditionParseResult(terms, problems);
        }

        foreach (string rawTerm in source.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string term = rawTerm.Trim();

            if (term.Length == 0)
            {
                continue;
            }

            // ⛔ 폐지된 문법을 <b>이름으로 알아보고</b> 짚는다 (2026-08-25). 그냥 두면
            // "비교 연산자를 찾지 못했습니다"로 떨어져서, 왜 안 되는지도 무엇으로 바꿔야
            // 하는지도 안 보인다. 옛 워크북이 열리는 자리라 안내가 곧 이행 경로다.
            if (term.StartsWith(ClearedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string episodeId = term[ClearedPrefix.Length..].Trim();

                problems.Add(new ConditionParseProblem(
                    ConditionProblemKind.ClearedRetired,
                    term,
                    $"'{term}' — cleared: 는 폐지됐습니다(2026-08-25). 진행 코어가 클리어 " +
                    "이력을 더 이상 기억하지 않습니다. Bool 스탯으로 바꾸세요: `스탯` 시트에 " +
                    $"깃발을 하나 만들고(예: `{Flag(episodeId)}`), " +
                    $"'{(episodeId.Length == 0 ? "그 에피소드" : episodeId)}'에서 나가는 " +
                    $"간선의 `스탯변화`에 `{Flag(episodeId)} = 1`을 적은 뒤, 여기에는 " +
                    $"`{Flag(episodeId)} == 1`을 적습니다."));
                continue;
            }

            if (!TrySplitComparison(term, out string key, out ConditionComparison comparison, out string value))
            {
                problems.Add(new ConditionParseProblem(
                    ConditionProblemKind.OperatorNotSupported,
                    term,
                    $"'{term}' — 비교 연산자를 찾지 못했습니다. 쓸 수 있는 것은 >= · <= · == · > · < 입니다."));
                continue;
            }

            if (key.Length == 0)
            {
                problems.Add(new ConditionParseProblem(
                    ConditionProblemKind.TermMalformed,
                    term,
                    $"'{term}' — 비교 왼쪽에 스탯키가 없습니다."));
                continue;
            }

            // bool 리터럴 (2026-08-16) — bool 스탯의 조건은 `flag == true` 꼴이다.
            // 값 공간은 0/1 하나다: true = 1, false = 0. 등호 비교에서만 의미가 있다.
            if (TryParseBoolLiteral(value, out int boolValue))
            {
                if (comparison != ConditionComparison.Exactly)
                {
                    problems.Add(new ConditionParseProblem(
                        ConditionProblemKind.TermMalformed,
                        term,
                        $"'{term}' — true/false는 == 로만 비교합니다."));
                    continue;
                }

                if (!knownStatKeys.Contains(key))
                {
                    problems.Add(new ConditionParseProblem(
                        ConditionProblemKind.UnknownStatKey,
                        term,
                        $"'{key}'는 `스탯` 시트에 없는 스탯키입니다. " +
                        $"선언된 키: {(knownStatKeys.Count == 0 ? "(없음)" : string.Join(", ", knownStatKeys))}"));
                    continue;
                }

                terms.Add(new ConditionTerm(
                    ConditionTermKind.StatComparison, key, ConditionComparison.Exactly, boolValue));
                continue;
            }

            if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsed))
            {
                problems.Add(new ConditionParseProblem(
                    ConditionProblemKind.ValueNotInteger,
                    term,
                    value.Contains('.', StringComparison.Ordinal)
                        ? $"'{term}' — 스탯 비교 값은 정수만 됩니다. 소수점은 쓸 수 없습니다(G-3)."
                        : $"'{term}' — 스탯 비교 값 '{value}'이 정수가 아닙니다."));
                continue;
            }

            if (!knownStatKeys.Contains(key))
            {
                problems.Add(new ConditionParseProblem(
                    ConditionProblemKind.UnknownStatKey,
                    term,
                    $"'{key}'는 `스탯` 시트에 없는 스탯키입니다. " +
                    $"선언된 키: {(knownStatKeys.Count == 0 ? "(없음)" : string.Join(", ", knownStatKeys))}"));
                continue;
            }

            terms.Add(new ConditionTerm(ConditionTermKind.StatComparison, key, comparison, parsed));
        }

        if (terms.Count == 0 && problems.Count == 0)
        {
            problems.Add(new ConditionParseProblem(
                ConditionProblemKind.Empty,
                source,
                $"'{source}'에서 읽어낼 조건이 없습니다."));
        }

        return new ConditionParseResult(terms, problems);
    }

    /// <summary>
    /// 폐지 안내에 넣을 <b>깃발 이름 제안</b>. 스탯키에 쓸 수 없는 글자(`.`·공백)를 밑줄로
    /// 바꾼다 — 안내문을 그대로 복사해 붙일 수 있어야 이행이 막히지 않는다.
    ///
    /// ⚠ <b>제안일 뿐 툴이 만들지 않는다.</b> 깃발을 켜는 간선을 사람이 골라야 하는데,
    /// 그 자리를 툴이 추측하면 관문이 조용히 다른 뜻이 된다.
    /// </summary>
    private static string Flag(string episodeId) =>
        episodeId.Length == 0
            ? "cleared_그에피소드"
            : "cleared_" + new string(episodeId
                .Select(character => char.IsLetterOrDigit(character) ? character : '_')
                .ToArray());

    /// <summary>두 글자 연산자를 먼저 본다. <c>&gt;=</c>를 <c>&gt;</c>로 읽으면 의미가 달라진다.</summary>
    private static bool TrySplitComparison(
        string term,
        out string key,
        out ConditionComparison comparison,
        out string value)
    {
        (string Token, ConditionComparison Comparison)[] operators =
        [
            (">=", ConditionComparison.AtLeast),
            ("<=", ConditionComparison.AtMost),
            ("==", ConditionComparison.Exactly),
            (">", ConditionComparison.Above),
            ("<", ConditionComparison.Below)
        ];

        foreach ((string token, ConditionComparison mapped) in operators)
        {
            int index = term.IndexOf(token, StringComparison.Ordinal);

            if (index < 0)
            {
                continue;
            }

            key = term[..index].Trim();
            value = term[(index + token.Length)..].Trim();
            comparison = mapped;
            return true;
        }

        key = string.Empty;
        value = string.Empty;
        comparison = ConditionComparison.Exactly;
        return false;
    }

    private static bool TryParseBoolLiteral(string value, out int mapped)
    {
        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            mapped = 1;
            return true;
        }

        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            mapped = 0;
            return true;
        }

        mapped = 0;
        return false;
    }

    /// <summary>
    /// 단일 항 조건식을 (스탯, 연산자, 값) 세 칸으로 분해한다 — `조건` 시트의 구조화 열
    /// (2026-08-16)에 쓰기 위해서다. bool 항(<c>flag == true</c>)은 연산자 칸이 true/false가
    /// 된다. 복합식(;)·cleared:·연산자 없는 원문은 분해하지 않는다(false) — 그런 식은
    /// 스탯 칸에 원문 그대로 남는다(탈출구).
    /// </summary>
    public static bool TryDecomposeSingle(
        string? expression, out string statKey, out string operatorText, out string valueText)
    {
        statKey = string.Empty;
        operatorText = string.Empty;
        valueText = string.Empty;

        string source = (expression ?? string.Empty).Trim();

        if (source.Length == 0 ||
            source.Contains(';', StringComparison.Ordinal) ||
            source.StartsWith(ClearedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TrySplitComparison(source, out string key, out ConditionComparison comparison, out string value) ||
            key.Length == 0 || value.Length == 0)
        {
            return false;
        }

        statKey = key;

        if (TryParseBoolLiteral(value, out int boolValue) && comparison == ConditionComparison.Exactly)
        {
            operatorText = boolValue == 1 ? "true" : "false";
            return true;
        }

        operatorText = comparison switch
        {
            ConditionComparison.AtLeast => ">=",
            ConditionComparison.AtMost => "<=",
            ConditionComparison.Above => ">",
            ConditionComparison.Below => "<",
            _ => "=="
        };
        valueText = value;
        return true;
    }
}
