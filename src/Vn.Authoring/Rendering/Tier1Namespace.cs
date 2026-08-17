using System.Text;
using Vn.Authoring.Model;

namespace Vn.Authoring.Rendering;

/// <summary>
/// 작가의 아이템·능력(Tier 1)에 <b>챕터 네임스페이스</b>를 붙인다 (2026-08-17 소유자:
/// "아이템, 능력은 오직 챕터단위로만 살리는게 맞아. 이건 짧은 스토리 단위를 상정한거야").
///
/// Yarn 런타임의 변수 저장소는 <b>하나</b>다(계약서 D1 — 세 러너가 공유). 그래서 접두를 안
/// 붙이면 1챕터의 <c>$열쇠</c>와 3챕터의 <c>$열쇠</c>가 <b>같은 변수</b>가 된다 — 저작은
/// 챕터별인데 실행은 전역인, 널브러진 상태다. 접두가 그 틈을 구조적으로 막는다
/// (`chapter-graph-orders.md` §0.5 구멍 A의 해법이 이것이다).
///
/// 접두의 뿌리는 <b>판(StoryFile) Id</b>다 — 챕터 Id가 아니라(소유자 결정 2026-08-17).
/// 챕터 이름을 바꿔도 Id는 그대로라 아이템이 초기화되지 않는다. 읽기는 어렵지만
/// 사람이 Yarn을 직접 볼 일이 없다.
///
/// <b>A계층 스탯에는 붙이지 않는다</b> — 그건 챕터를 넘어 사는 게 맞고, 런타임 브리지가
/// 이름 그대로 왕복한다. 무엇이 스탯인지는 <b>추측하지 않는다</b>: 챕터 조건 공급 노드가
/// 쓰는 이름만 스탯이다(명시 목록, §0.5).
/// </summary>
public static class Tier1Namespace
{
    /// <summary>변수 이름에 쓸 수 있는 글자만 남긴다 — Yarn 식별자를 깨뜨리지 않는다.</summary>
    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (char letter in value)
        {
            builder.Append(char.IsLetterOrDigit(letter) ? letter : '_');
        }

        return builder.ToString();
    }

    /// <summary>그 판의 접두 — <c>__t1_{판Id}_</c>. 판을 못 찾으면 접두가 없다(무접촉).</summary>
    public static string PrefixFor(StoryProject? project, string? nodeId)
    {
        if (project is null || nodeId is null)
        {
            return string.Empty;
        }

        StoryFile? file = project.Files.FirstOrDefault(candidate =>
            candidate.Nodes.Any(node => string.Equals(node.Id, nodeId, StringComparison.Ordinal)));

        return file is null ? string.Empty : $"__t1_{Sanitize(file.Id)}_";
    }

    /// <summary>
    /// 이 판에서 <b>접두를 붙이지 않을</b> 이름들 = A계층 스탯. 챕터 조건 공급 노드의
    /// 조건식에 나오는 변수가 그것이다 — 추측이 아니라 그 노드가 명시한 목록이다.
    /// </summary>
    public static HashSet<string> StatNames(StoryProject? project, string? nodeId)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        if (project is null || nodeId is null)
        {
            return names;
        }

        StoryFile? file = project.Files.FirstOrDefault(candidate =>
            candidate.Nodes.Any(node => string.Equals(node.Id, nodeId, StringComparison.Ordinal)));

        if (file is null)
        {
            return names;
        }

        foreach (SetNode supply in file.Nodes.OfType<SetNode>()
                     .Where(node => Chapters.EpisodeSyncService.IsConditionSupplyNode(node, file)))
        {
            foreach (ConditionDefinition condition in supply.Conditions)
            {
                foreach (string variable in Variables(condition.Expression))
                {
                    names.Add(variable);
                }
            }
        }

        return names;
    }

    /// <summary>변수 하나에 접두를 붙인다. 스탯·합성 추적 변수·빈 이름은 그대로 둔다.</summary>
    public static string Apply(string variable, string prefix, IReadOnlySet<string> statNames)
    {
        string bare = variable.TrimStart('$').Trim();

        if (prefix.Length == 0 || bare.Length == 0 ||
            statNames.Contains(bare) ||
            bare.StartsWith("__", StringComparison.Ordinal)) // 합성 추적(__ch_N)·이미 붙은 것
        {
            return bare;
        }

        return prefix + bare;
    }

    /// <summary>
    /// 조건식 안의 <c>$이름</c>을 전부 훑어 접두를 붙인다. 식은 사람이 쓴 원문이라
    /// 파싱하지 않고 <b>변수 토큰만</b> 갈아 끼운다 — 나머지 글자는 손대지 않는다.
    /// </summary>
    public static string ApplyToExpression(string? expression, string prefix, IReadOnlySet<string> statNames)
    {
        if (string.IsNullOrEmpty(expression) || prefix.Length == 0)
        {
            return expression ?? string.Empty;
        }

        var builder = new StringBuilder(expression.Length + 16);
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

            string name = expression[start..index];
            builder.Append('$').Append(Apply(name, prefix, statNames));
        }

        return builder.ToString();
    }

    private static bool IsNameLetter(char letter) => char.IsLetterOrDigit(letter) || letter == '_';

    /// <summary>식에 나오는 <c>$이름</c>들.</summary>
    private static IEnumerable<string> Variables(string? expression)
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
}
