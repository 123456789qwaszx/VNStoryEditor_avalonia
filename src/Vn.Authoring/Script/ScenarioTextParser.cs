using Vn.Authoring.Definition;
using Vn.Authoring.Model;

namespace Vn.Authoring.Script;

/// <summary>다음 대사 줄에 붙을 조건 구조 (X11 문법). 선택 전환은 이 파서의 비범위다.</summary>
public sealed record ScenarioStructureIntent(ConditionTransitionKind Kind, string? Expression);

/// <summary>파싱된 대본 한 줄. <see cref="Transitions"/>는 바로 앞의 조건 토큰들이다(순서대로).</summary>
/// <param name="LineId">
/// 줄 끝의 <c>#line:</c> 태그에서 떼어낸 신원 (계약서 C1). 태그가 없으면 null이고,
/// 그때는 동기화가 <b>내용으로</b> 짝을 찾는다. 있으면 <b>ID로</b> 찾는다 — 그쪽이 확실하다.
/// </param>
public sealed record ScenarioLine(
    string Speaker,
    string Text,
    bool SpeakerUnregistered,
    IReadOnlyList<ScenarioStructureIntent> Transitions,
    string? LineId = null)
{
    /// <summary>
    /// 첫 전환 — 슬롯이 하나뿐이던 시절의 이름이다. 한 줄에 전환이 하나인 흔한 경우에
    /// 부르는 쪽이 목록을 풀 이유가 없어 남겨 둔다. 상태를 재생하는 쪽은
    /// <see cref="Transitions"/>를 봐야 한다.
    /// </summary>
    public ScenarioStructureIntent? Transition =>
        Transitions.Count > 0 ? Transitions[0] : null;
}

/// <param name="TrailingTransitions">
/// <b>마지막 대사 줄 뒤에 남은 전환들</b> (2026-08-24).
///
/// 전환은 늘 <em>다음</em> 대사 줄에 실리는데, 대본이 조건 블록으로 끝나면 실을 줄이 없다.
/// 예전에는 <b>닫는</b> 전환만 눈감아 주고(산출 쪽이 문서 끝에서 닫으므로) <b>여는</b>
/// 전환은 오류로 세웠다. 그래서 <b>대사 없는 조건 블록이 대본의 마지막</b>이면 통째로
/// 거부됐다 — 소유자가 겪은 그 자리다.
///
/// 이제 <b>끝에서 짝이 맞는 열고-닫기</b>는 여기 담아 나른다. 짝이 안 맞는 여는 전환은
/// 여전히 오류다(열 대상도 닫을 자리도 없다).
/// </param>
public sealed record ScenarioParseResult(
    IReadOnlyList<ScenarioLine> Lines,
    IReadOnlyList<string> UnparsedLines,
    IReadOnlyList<ScenarioStructureIntent>? TrailingTransitions = null)
{
    public bool HasUnparsed => UnparsedLines.Count > 0;

    /// <summary>마지막 줄 뒤의 전환들. 없으면 빈 목록이다.</summary>
    public IReadOnlyList<ScenarioStructureIntent> Trailing =>
        TrailingTransitions ?? Array.Empty<ScenarioStructureIntent>();
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
        var pending = new List<ScenarioStructureIntent>();
        bool inChoice = false;

        foreach (string raw in (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = raw.Trim();

            if (line.Length == 0)
            {
                continue;
            }

            if (TryParseStructure(line, out ScenarioStructureIntent? intent))
            {
                // 한 줄에 여러 전환이 몰릴 수 있다 (2026-08-17) — Yarn에는 전환만 있는
                // 줄이 없어서, 블록이 겹쳐 닫히거나 닫히자마자 다음이 열리면 그것들이
                // 전부 다음 대사 줄 앞에 쌓인다. 순서 그대로 실어 나른다.
                pending.Add(intent!);
                continue;
            }

            if (line.StartsWith(OptionMarker, StringComparison.Ordinal))
            {
                if (pending.Count > 0)
                {
                    unparsed.Add($"{line} — 옵션 줄에는 조건 전환을 함께 붙일 수 없습니다. " +
                        $"앞의 '{Describe(pending[0])}'가 붙을 곳이 없습니다.");
                    pending.Clear();
                    continue;
                }

                (string optionText, string? optionId) =
                    SplitLineTag(line[OptionMarker.Length..].Trim());

                // 첫 옵션이 블록을 열고, 뒤따르는 옵션은 같은 블록의 다음 갈래다(깊이는 늘지 않는다).
                lines.Add(new ScenarioLine(
                    string.Empty,
                    optionText,
                    SpeakerUnregistered: false,
                    [new ScenarioStructureIntent(
                        inChoice ? ConditionTransitionKind.BeginNextOption : ConditionTransitionKind.BeginChoice,
                        null)],
                    optionId));

                inChoice = true;
                continue;
            }

            if (line.StartsWith("<<", StringComparison.Ordinal) ||
                line.StartsWith("[", StringComparison.Ordinal))
            {
                unparsed.Add(line);
                continue;
            }

            // 선택 블록 안에서는 들여쓰기가 소속을 정한다 — 들여쓴 줄은 옵션 본문이고,
            // 들여쓰기가 풀린 줄은 블록을 닫는다. Yarn이 옵션 소속을 표기하는 방식 그대로다.
            if (inChoice && !IsIndented(raw))
            {
                inChoice = false;

                // 선택 블록의 닫힘은 맨 앞에 선다 — 조건 전환보다 먼저 일어난 일이다.
                pending.Insert(0, new ScenarioStructureIntent(ConditionTransitionKind.EndChoice, null));
            }

            (string tagless, string? lineId) = SplitLineTag(line);
            (string speaker, string body) = SplitSpeaker(tagless, definition);
            bool unregistered = speaker.Length > 0 &&
                definition.FindSpeakerCharacterId(speaker) is null;

            lines.Add(new ScenarioLine(speaker, body, unregistered, [.. pending], lineId));
            pending.Clear();
        }

        // 끝에 남은 <b>닫는</b> 전환은 잘못이 아니다 (2026-08-17 소유자 보고) — 조건
        // 블록이 에피소드의 마지막이면 닫힘을 실어 나를 다음 줄이 없는 것이 정상이고,
        // 산출 쪽(ResultDocumentComposer)이 문서 끝에서 그 블록을 닫는다.
        //
        // 2026-08-24에 <b>여는</b> 전환도 조건부로 받는다: 끝에서 <b>짝이 맞으면</b>
        // 그것은 <b>대사 없는 조건 블록</b>이고(소유자: "굳이 대사를 붙이지 않아도 되도록"),
        // 열 대상이 없는 것이 아니라 <b>담을 것이 없는</b> 것이다. 짝이 안 맞는 여는
        // 전환만 예전대로 말한다 — 그건 진짜로 붙을 곳이 없다.
        foreach (ScenarioStructureIntent left in Unbalanced(pending))
        {
            unparsed.Add($"<<{Describe(left)}>> — 뒤따르는 대사 줄이 없어 붙일 곳이 없습니다.");
        }

        return new ScenarioParseResult(lines, unparsed, pending.Count > 0 ? [.. pending] : null);
    }

    /// <summary>
    /// 끝에 남은 전환 중 <b>짝이 없는 여는 전환</b>들. 열고 닫기가 맞아떨어지면 그것은
    /// 대사 없는 블록이므로 잘못이 아니다.
    ///
    /// 깊이를 세는 것으로 충분하다 — 이 목록은 <b>한 자리</b>에 몰린 전환들이라 순서가 곧
    /// 일어나는 순서이고, 조건과 선택이 섞여도 각자 제 짝만 본다.
    /// </summary>
    private static List<ScenarioStructureIntent> Unbalanced(List<ScenarioStructureIntent> pending)
    {
        var open = new List<ScenarioStructureIntent>();

        foreach (ScenarioStructureIntent intent in pending)
        {
            switch (intent.Kind)
            {
                case ConditionTransitionKind.BeginIf:
                case ConditionTransitionKind.BeginChoice:
                    open.Add(intent);
                    break;

                // elseif·다음 옵션은 열려 있는 것의 갈래를 바꿀 뿐 깊이를 안 바꾼다.
                case ConditionTransitionKind.BeginElseIf:
                case ConditionTransitionKind.BeginNextOption:
                    if (open.Count == 0)
                    {
                        open.Add(intent);
                    }

                    break;

                case ConditionTransitionKind.EndIf:
                case ConditionTransitionKind.EndChoice:
                    if (open.Count > 0)
                    {
                        open.RemoveAt(open.Count - 1);
                    }

                    break;
            }
        }

        return open;
    }

    private const string OptionMarker = "->";

    /// <summary>
    /// 선택 블록 안에서 옵션 본문임을 나타내는 들여쓰기가 있는가.
    ///
    /// <b>이 파서에서 들여쓰기가 의미를 갖는 유일한 자리다.</b> 블록 밖에서는 지금까지대로
    /// 모두 다듬어 읽는다 — 사람이 붙여넣는 텍스트의 들여쓰기는 대개 뜻이 없기 때문이다.
    /// 선택 블록 안에서만은 뜻이 있다: Yarn이 옵션 소속을 들여쓰기로 표기하므로,
    /// 그것을 버리면 "옵션 본문"과 "블록 다음 줄"을 구별할 방법이 사라진다.
    /// </summary>
    private static bool IsIndented(string raw) =>
        raw.Length > 0 && char.IsWhiteSpace(raw[0]);

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
    private static (string Speaker, string Text) SplitSpeaker(string line, GameDefinition definition)
    {
        int colon = line.IndexOf(':');

        if (colon <= 0)
        {
            return (string.Empty, line);
        }

        string prefix = line[..colon];

        // 접두에 공백이 있으면 산문("그는 말했다: …")일 수 있어 화자로 삼지 않는다 — 단,
        // 등록된 화자명과 <b>다듬기 없이 정확히</b> 같으면 그 이름이다("늙은 상인"처럼 공백
        // 있는 이름도 이름이다). 정확 일치만 받아야 "윌로 : …"(이름 뒤 공백)가 지문이라는
        // 기존 규칙이 그대로 산다. 등록부는 명시적 어휘라 산문과 헷갈릴 일이 없다.
        if (prefix.Any(char.IsWhiteSpace) &&
            !definition.Speakers.Any(speaker =>
                string.Equals(speaker.Name, prefix, StringComparison.Ordinal)))
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
