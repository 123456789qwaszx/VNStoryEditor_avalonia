using System.Security.Cryptography;
using System.Text;

namespace Vn.Authoring.Script;

public enum ScriptParseProblemKind
{
    /// <summary>구분자가 없어 화자를 알 수 없다. 화자 없는 줄로 다룬다.</summary>
    MissingSpeaker,

    /// <summary>구분자는 있지만 앞이 비어 있다. 화자 없는 줄로 다룬다.</summary>
    EmptySpeaker,

    /// <summary>화자는 있는데 대사가 비어 있다. 줄은 그대로 남긴다.</summary>
    EmptyText,

    /// <summary>
    /// 구분자 앞부분이 화자로 보이지 않는다(너무 길거나 문장 부호를 포함).
    /// 임의로 자르지 않고 줄 전체를 대사로 다룬다.
    /// </summary>
    AmbiguousSpeaker
}

/// <param name="SourceLineNumber">작가 파일에서의 1부터 시작하는 물리 줄 번호.</param>
public sealed record ScriptParseProblem(
    ScriptParseProblemKind Kind,
    int SourceLineNumber,
    string RawText,
    string Message);

/// <param name="SourceLineNumber">작가 파일에서의 1부터 시작하는 물리 줄 번호.</param>
public sealed record ParsedScriptLine(int SourceLineNumber, string Speaker, string Text);

public sealed class ParsedScript
{
    public ParsedScript(
        IReadOnlyList<ParsedScriptLine> lines,
        IReadOnlyList<ScriptParseProblem> problems,
        string contentHash)
    {
        Lines = lines;
        Problems = problems;
        ContentHash = contentHash;
    }

    public IReadOnlyList<ParsedScriptLine> Lines { get; }

    /// <summary>버린 줄은 없다. 애매하게 다룬 줄을 여기에 모두 알린다.</summary>
    public IReadOnlyList<ScriptParseProblem> Problems { get; }

    /// <summary>정규화한 원본 텍스트의 해시. 같은 파일을 다시 읽었는지 판별한다.</summary>
    public string ContentHash { get; }
}

/// <summary>
/// 작가의 평평한 대본을 줄 단위로 읽는다.
///
/// <b>문법은 하나뿐이다.</b>
/// <code>
/// 캐릭터이름: 대사
/// </code>
///
/// 규칙:
/// <list type="bullet">
///   <item>BOM은 버리고 CRLF·CR은 LF로 정규화한다.</item>
///   <item>공백뿐인 줄은 건너뛴다. 문제로 알리지 않는다.</item>
///   <item><c>//</c>로 시작하는 줄은 작가 메모로 보고 건너뛴다.</item>
///   <item><b>첫 번째</b> <c>:</c>만 화자와 대사를 나눈다. 대사 안의 콜론은 그대로 남는다.</item>
///   <item>화자와 대사는 양쪽 공백을 다듬는다.</item>
///   <item>
///     구분자 앞이 화자로 보이지 않으면(<see cref="MaxSpeakerLength"/>자를 넘거나 문장 부호를
///     포함) 자르지 않고 줄 전체를 대사로 둔다. <c>"12:30에 만나자"</c> 같은 줄을 화자
///     <c>12</c>로 잘못 읽지 않기 위해서다.
///   </item>
///   <item>
///     줄 맨 앞의 <c>:</c>는 "화자 없음"을 뜻하는 명시적 표기다. 화자로 오인될 수 있는
///     문장을 작가가 직접 벗어나게 할 수 있다.
///   </item>
///   <item>지원하지 않는 줄을 조용히 버리지 않는다. 언제나 줄로 남기고 문제로 알린다.</item>
/// </list>
/// </summary>
public static class ScriptParser
{
    /// <summary>이보다 긴 앞부분은 화자로 보지 않는다.</summary>
    public const int MaxSpeakerLength = 24;

    private const string SentencePunctuation = ".!?,;\"'()[]{}";

    public static ParsedScript Parse(string text)
    {
        string normalized = Normalize(text ?? string.Empty);
        var lines = new List<ParsedScriptLine>();
        var problems = new List<ScriptParseProblem>();

        string[] rawLines = normalized.Split('\n');

        for (int index = 0; index < rawLines.Length; index++)
        {
            string raw = rawLines[index];
            int lineNumber = index + 1;
            string trimmed = raw.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            lines.Add(ParseLine(trimmed, lineNumber, problems));
        }

        return new ParsedScript(lines, problems, ComputeHash(normalized));
    }

    /// <summary>정규화한 텍스트의 내용 해시. 같은 내용이면 언제나 같은 값이다.</summary>
    public static string ComputeHash(string normalizedText)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedText));
        return "sha256:" + Convert.ToHexStringLower(hash);
    }

    public static string Normalize(string text)
    {
        string value = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return value.Length > 0 && value[0] == '﻿' ? value[1..] : value;
    }

    private static ParsedScriptLine ParseLine(
        string trimmed,
        int lineNumber,
        List<ScriptParseProblem> problems)
    {
        // 맨 앞의 콜론은 "화자 없음"을 뜻하는 명시적 표기다. 문제로 알리지 않는다.
        if (trimmed.StartsWith(':'))
        {
            return new ParsedScriptLine(lineNumber, string.Empty, trimmed[1..].Trim());
        }

        int separator = trimmed.IndexOf(':');

        if (separator < 0)
        {
            problems.Add(new ScriptParseProblem(
                ScriptParseProblemKind.MissingSpeaker,
                lineNumber,
                trimmed,
                "구분자 ':'가 없어 화자를 알 수 없습니다. 화자 없는 줄로 가져옵니다."));
            return new ParsedScriptLine(lineNumber, string.Empty, trimmed);
        }

        string speaker = trimmed[..separator].Trim();
        string body = trimmed[(separator + 1)..].Trim();

        if (speaker.Length == 0)
        {
            problems.Add(new ScriptParseProblem(
                ScriptParseProblemKind.EmptySpeaker,
                lineNumber,
                trimmed,
                "구분자 앞에 화자가 없습니다. 화자 없는 줄로 가져옵니다."));
            return new ParsedScriptLine(lineNumber, string.Empty, body);
        }

        if (!LooksLikeSpeaker(speaker))
        {
            problems.Add(new ScriptParseProblem(
                ScriptParseProblemKind.AmbiguousSpeaker,
                lineNumber,
                trimmed,
                $"'{speaker}'는 화자로 보이지 않아 줄 전체를 대사로 가져옵니다. " +
                "화자를 지정하려면 짧은 이름을, 화자가 없음을 명시하려면 줄 앞에 ':'를 쓰세요."));
            return new ParsedScriptLine(lineNumber, string.Empty, trimmed);
        }

        if (body.Length == 0)
        {
            problems.Add(new ScriptParseProblem(
                ScriptParseProblemKind.EmptyText,
                lineNumber,
                trimmed,
                $"화자 '{speaker}'의 대사가 비어 있습니다. 빈 줄로 가져옵니다."));
        }

        return new ParsedScriptLine(lineNumber, speaker, body);
    }

    private static bool LooksLikeSpeaker(string speaker)
    {
        return speaker.Length <= MaxSpeakerLength &&
            !speaker.Any(character => SentencePunctuation.Contains(character, StringComparison.Ordinal));
    }
}
