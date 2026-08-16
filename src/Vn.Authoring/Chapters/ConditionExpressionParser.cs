using System.Globalization;

namespace Vn.Authoring.Chapters;

public enum ConditionTermKind
{
    /// <summary><c>trust &gt;= 3</c> — Tier 2 스탯과 정수의 비교.</summary>
    StatComparison,

    /// <summary><c>cleared:main05.02</c> — 그 에피소드를 클리어했는가.</summary>
    EpisodeCleared
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

/// <param name="Key">스탯키. <see cref="ConditionTermKind.EpisodeCleared"/>면 EpisodeId다.</param>
public sealed record ConditionTerm(
    ConditionTermKind Kind,
    string Key,
    ConditionComparison Comparison,
    int Value)
{
    public static ConditionTerm Cleared(string episodeId) =>
        new(ConditionTermKind.EpisodeCleared, episodeId, ConditionComparison.Exactly, 1);
}

public enum ConditionProblemKind
{
    Empty,
    TermMalformed,
    OperatorNotSupported,
    ValueNotInteger,
    UnknownStatKey,
    EpisodeIdBlank
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
/// cleared:main05.02     에피소드 클리어 여부
/// A ; B                 AND
/// </code>
/// <c>Flag</c>·<c>Token</c>·<c>ChapterCleared</c>는 런타임이 지원하지만 v1 비범위다 —
/// <see cref="ConditionTermKind"/>에 항목을 더하면 열리도록 두었다.
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

            if (term.StartsWith(ClearedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string episodeId = term[ClearedPrefix.Length..].Trim();

                if (episodeId.Length == 0)
                {
                    problems.Add(new ConditionParseProblem(
                        ConditionProblemKind.EpisodeIdBlank,
                        term,
                        $"'{term}' — cleared: 뒤에 EpisodeId가 없습니다."));
                    continue;
                }

                terms.Add(ConditionTerm.Cleared(episodeId));
                continue;
            }

            if (!TrySplitComparison(term, out string key, out ConditionComparison comparison, out string value))
            {
                problems.Add(new ConditionParseProblem(
                    ConditionProblemKind.OperatorNotSupported,
                    term,
                    $"'{term}' — 비교 연산자를 찾지 못했습니다. 쓸 수 있는 것은 >= · <= · == · > · < 이고, " +
                    $"에피소드 클리어는 cleared:EpisodeId 입니다."));
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
