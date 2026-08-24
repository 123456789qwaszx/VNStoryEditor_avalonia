using System.Text;

namespace Vn.Authoring.Rendering;

/// <summary>
/// Yarn 표기 조립 규칙의 단일 구현. Preview(YarnPreviewFormatter)와
/// 파일 이미터(YarnBundleEmitter)가 같은 코드를 지나야 화면에서 본 것과
/// 파일에 쓴 것이 다른 이야기를 하지 않는다.
/// </summary>
internal static class YarnSyntax
{
    public const string Indent = "    ";

    public static string IndentOf(int level) =>
        string.Concat(Enumerable.Repeat(Indent, Math.Max(0, level)));

    /// <summary>
    /// <c>&lt;&lt;name v1 v2&gt;&gt;</c> — 런타임 커맨드는 인자를 순서로 읽는다.
    /// Segment의 인자 목록이 이미 카탈로그 파라미터 순서이므로 값만 그 순서대로 잇는다.
    /// </summary>
    public static void AppendCommand(StringBuilder builder, RenderedSegment segment)
    {
        builder.Append("<<")
            .Append(string.IsNullOrWhiteSpace(segment.CommandName)
                ? segment.DefinitionId ?? "presentation"
                : segment.CommandName);

        foreach (RenderedArgument argument in segment.Arguments ?? Array.Empty<RenderedArgument>())
        {
            builder.Append(' ').Append(argument.Value);
        }

        builder.Append(">>");
    }

    /// <summary><c>&lt;&lt;set $x += 1&gt;&gt;</c></summary>
    public static void AppendSet(StringBuilder builder, RenderedSegment segment)
    {
        builder.Append("<<set ")
            .Append(NormalizeVariable(segment.Variable))
            .Append(' ')
            .Append(segment.Operator ?? "=")
            .Append(' ')
            .Append(segment.Value ?? string.Empty)
            .Append(">>");
    }

    public static void AppendCondition(StringBuilder builder, string keyword, string? expression)
    {
        builder.Append("<<")
            .Append(keyword)
            .Append(' ')
            .Append(string.IsNullOrWhiteSpace(expression) ? "false" : expression)
            .Append(">>");
    }

    public static void AppendJump(StringBuilder builder, string target)
    {
        builder.Append("<<jump ").Append(target).Append(">>");
    }

    /// <summary>
    /// <c>&lt;&lt;detour 노드&gt;&gt;</c> — 대상 노드를 재생하고 이 자리로 돌아온다.
    /// 조건 갈래의 커스텀 씬 출구가 쓴다 (YarnSpinner 3.x).
    /// </summary>
    public static void AppendDetour(StringBuilder builder, string target)
    {
        builder.Append("<<detour ").Append(target).Append(">>");
    }

    /// <summary>화자가 있으면 <c>화자: 대사</c>, 없으면 대사만.</summary>
    public static void AppendDialogue(StringBuilder builder, RenderedSegment segment)
    {
        if (!string.IsNullOrWhiteSpace(segment.Speaker))
        {
            builder.Append(segment.Speaker).Append(": ");
        }

        builder.Append(segment.Text ?? string.Empty);
    }

    public static string NormalizeVariable(string? variable)
    {
        string value = variable?.Trim() ?? string.Empty;

        if (value.StartsWith('$'))
        {
            return value;
        }

        return "$" + value;
    }

    /// <summary>
    /// 변수 이름에 쓸 수 있는 글자만 남긴다 — 문자·숫자·밑줄 (2026-08-25).
    ///
    /// <b>Yarn 식별자에는 공백이 못 들어간다.</b> 그대로 내면
    /// <c>&lt;&lt;set $능력이 바뀌 += 4&gt;&gt;</c>가 되어 파서가 거기서 끊기고,
    /// 그 노드만이 아니라 <b>번들 전체가</b> 컴파일에 실패한다.
    ///
    /// ⚠ 추측이 아니라 <b>정규화</b>다. 공백이 든 이름을 Yarn이 읽을 방법은 하나도 없으므로
    /// 고를 것이 없다 — <see cref="SanitizeNodeName"/>·<c>Tier1Namespace</c>의 접두가
    /// 이미 같은 규칙을 쓰고, 이 함수가 그 규칙의 <b>유일한 주인</b>이다.
    ///
    /// <b>입력과 출력 양쪽에 건다.</b> 입력(설정 노드의 변수 칸)에서 막으면 새로 생기지
    /// 않고, 출력(접두 적용)에서 걸면 <b>이미 저장된 프로젝트</b>도 그날로 컴파일된다.
    /// </summary>
    public static string SanitizeVariableName(string? name)
    {
        string source = (name ?? string.Empty).TrimStart('$').Trim();

        if (source.Length == 0)
        {
            return string.Empty;   // 빈 이름은 발행 검증이 따로 막는다 — 여기서 지어내지 않는다.
        }

        var builder = new StringBuilder(source.Length);

        foreach (char c in source)
        {
            builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        return builder.ToString();
    }

    /// <summary>
    /// 노드 타이틀에 쓸 수 있는 이름으로 다듬는다. 문자·숫자·밑줄만 남긴다.
    /// 타이틀은 세이브 키다(계약서 C2) — 같은 입력은 언제나 같은 이름이 되어야 한다.
    /// </summary>
    public static string SanitizeNodeName(string? name)
    {
        string source = string.IsNullOrWhiteSpace(name) ? "unnamed" : name.Trim();
        var builder = new StringBuilder(source.Length);

        foreach (char c in source)
        {
            builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        return builder.ToString();
    }
}
