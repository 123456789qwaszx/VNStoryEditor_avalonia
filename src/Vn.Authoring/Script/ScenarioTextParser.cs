using Vn.Authoring.Definition;
using Vn.Authoring.Model;

namespace Vn.Authoring.Script;

/// <summary>다음 대사 줄에 붙을 조건 구조 (X11 문법). 선택 전환은 이 파서의 비범위다.</summary>
public sealed record ScenarioStructureIntent(ConditionTransitionKind Kind, string? Expression);

/// <summary>파싱된 대본 한 줄. <see cref="Transition"/>은 바로 앞의 조건 토큰이다.</summary>
/// <param name="LineId">
/// 줄 끝의 <c>#line:</c> 태그에서 떼어낸 신원 (계약서 C1). 태그가 없으면 null이고,
/// 그때는 동기화가 <b>내용으로</b> 짝을 찾는다. 있으면 <b>ID로</b> 찾는다 — 그쪽이 확실하다.
/// </param>
public sealed record ScenarioLine(
    string Speaker,
    string Text,
    bool SpeakerUnregistered,
    ScenarioStructureIntent? Transition,
    string? LineId = null);

public sealed record ScenarioParseResult(
    IReadOnlyList<ScenarioLine> Lines,
    IReadOnlyList<string> UnparsedLines)
{
    public bool HasUnparsed => UnparsedLines.Count > 0;
}

/// <summary>
/// ScenarioOnly 텍스트를 라인·조건 구조로 읽는다 (X12a). X11이 표기 문법을 Yarn으로
/// 통일했으므로 이 파서가 곧 그 역방향이다 — <b>입력 방법</b>이지 산출물 되읽기가 아니다.
///
/// 규칙(지시서 그대로):
/// <list type="bullet">
/// <item>첫 콜론 기준 분리하되 콜론 앞 접두에 공백이 없을 때만 화자. 미등록 화자는
///   오류가 아니라 표시 대상이다.</item>
/// <item>콜론이 없거나 접두에 공백이 있으면 무화자(지문) 라인.</item>
/// <item><c>&lt;&lt;if&gt;&gt;</c>·<c>&lt;&lt;elseif&gt;&gt;</c>·<c>&lt;&lt;endif&gt;&gt;</c>는 다음 대사 줄의 조건 구조.</item>
/// <item>해석 못 한 줄(선택지 라벨 <c>-&gt;</c>, 기타 <c>&lt;&lt;…&gt;&gt;</c>, 대괄호 장식)은
///   조용히 버리지 않고 <see cref="ScenarioParseResult.UnparsedLines"/>로 보인다(규칙 14).
///   지문으로 삼키면 장식이 대사가 되는 더 나쁜 침묵이 된다.</item>
/// </list>
/// </summary>
public static class ScenarioTextParser
{
    public static ScenarioParseResult Parse(string? text, GameDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var lines = new List<ScenarioLine>();
        var unparsed = new List<string>();
        ScenarioStructureIntent? pending = null;

        foreach (string raw in (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = raw.Trim();

            if (line.Length == 0)
            {
                continue;
            }

            if (TryParseStructure(line, out ScenarioStructureIntent? intent))
            {
                if (pending is not null)
                {
                    // 전환 슬롯은 라인당 하나다(모델 구조). 겹치면 어느 쪽도 추측하지 않는다.
                    unparsed.Add($"{line} — 한 줄에 조건 전환은 하나만 붙습니다. 앞의 '{Describe(pending)}'와 겹칩니다.");
                    continue;
                }

                pending = intent;
                continue;
            }

            if (line.StartsWith("->", StringComparison.Ordinal) ||
                line.StartsWith("<<", StringComparison.Ordinal) ||
                line.StartsWith("[", StringComparison.Ordinal))
            {
                unparsed.Add(line);
                continue;
            }

            (string tagless, string? lineId) = SplitLineTag(line);
            (string speaker, string body) = SplitSpeaker(tagless);
            bool unregistered = speaker.Length > 0 &&
                definition.FindSpeakerCharacterId(speaker) is null;

            lines.Add(new ScenarioLine(speaker, body, unregistered, pending, lineId));
            pending = null;
        }

        if (pending is not null)
        {
            unparsed.Add($"<<{Describe(pending)}>> — 뒤따르는 대사 줄이 없어 붙일 곳이 없습니다.");
        }

        return new ScenarioParseResult(lines, unparsed);
    }

    /// <summary>
    /// 줄 끝의 <c>#line:ln_0001</c>을 떼어낸다 (계약서 C1의 표기 그대로).
    ///
    /// <b>왜 떼어야 하는가</b> — 떼지 않으면 태그가 대사 본문의 일부가 되어, 같은 줄을 다시
    /// 읽을 때마다 내용 비교가 어긋나고 동기화가 "고쳐진 줄"로 착각한다. 엑셀 경로는 시트의
    /// LineId 열로 신원을 이미 알고 있으므로, 그 지식을 텍스트 경계에서 버리면 확실한 ID 매칭이
    /// 내용 추정으로 격하된다.
    ///
    /// 사람이 손으로 붙여넣는 경로도 함께 이득이다 — 내보낸 <c>.yarn</c>에서 복사한 줄에
    /// 태그가 붙어 있어도 신원이 유지된다.
    ///
    /// 마지막 <c>#line:</c>만 본다. 뒤에 공백이 있으면 태그가 아니라 본문의 일부로 둔다.
    /// </summary>
    private static (string Body, string? LineId) SplitLineTag(string line)
    {
        const string Marker = " #line:";
        int at = line.LastIndexOf(Marker, StringComparison.Ordinal);

        if (at < 0)
        {
            return (line, null);
        }

        string id = line[(at + Marker.Length)..];

        if (id.Length == 0 || id.Any(char.IsWhiteSpace))
        {
            return (line, null);
        }

        return (line[..at].TrimEnd(), id);
    }

    /// <summary>첫 콜론 기준, 접두에 공백이 없을 때만 화자다.</summary>
    private static (string Speaker, string Text) SplitSpeaker(string line)
    {
        int colon = line.IndexOf(':');

        if (colon <= 0)
        {
            return (string.Empty, line);
        }

        string prefix = line[..colon];

        if (prefix.Any(char.IsWhiteSpace))
        {
            return (string.Empty, line);
        }

        return (prefix, line[(colon + 1)..].TrimStart());
    }

    private static bool TryParseStructure(string line, out ScenarioStructureIntent? intent)
    {
        intent = null;

        if (!line.StartsWith("<<", StringComparison.Ordinal) ||
            !line.EndsWith(">>", StringComparison.Ordinal))
        {
            return false;
        }

        string inner = line[2..^2].Trim();

        if (string.Equals(inner, "endif", StringComparison.Ordinal))
        {
            intent = new ScenarioStructureIntent(ConditionTransitionKind.EndIf, null);
            return true;
        }

        if (inner.StartsWith("if ", StringComparison.Ordinal))
        {
            intent = new ScenarioStructureIntent(ConditionTransitionKind.BeginIf, inner[3..].Trim());
            return true;
        }

        if (inner.StartsWith("elseif ", StringComparison.Ordinal))
        {
            intent = new ScenarioStructureIntent(ConditionTransitionKind.BeginElseIf, inner[7..].Trim());
            return true;
        }

        return false; // 그 외 커맨드는 이 파서의 어휘가 아니다 — 미해석 목록으로 간다.
    }

    private static string Describe(ScenarioStructureIntent intent) => intent.Kind switch
    {
        ConditionTransitionKind.BeginIf => $"if {intent.Expression}",
        ConditionTransitionKind.BeginElseIf => $"elseif {intent.Expression}",
        ConditionTransitionKind.EndIf => "endif",
        _ => intent.Kind.ToString()
    };
}
