using System;
using System.Collections.Generic;

namespace Vn.App.Services;

/// <summary>
/// 원본 텍스트의 특정 줄만 바꾼다.
///
/// <see cref="Vn.Core.Story.StoryLine"/>은 손실 압축이라 그것으로 Yarn을 재조립하면
/// 작가가 대사 한 줄만 고쳤는데 파일 전체의 공백과 주석이 바뀐다.
/// 그래서 트리를 다시 쓰지 않고, 원본 문자열에서 그 줄의 구간만 갈아 끼운다.
/// 나머지 바이트는 손대지 않는다. 줄바꿈도 원본 그대로 남는다.
/// </summary>
internal static class StoryLineReplacer
{
    /// <summary>
    /// 1부터 세는 줄 번호의 내용을 <paramref name="replacement"/>으로 바꾼 텍스트.
    /// 줄바꿈 문자는 건드리지 않는다. 그런 줄이 없으면 원본을 그대로 돌려준다.
    /// </summary>
    public static string Replace(string text, int oneBasedLine, string replacement)
    {
        if (!TryFindLine(text, oneBasedLine, out int start, out int end))
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, start), replacement, text.AsSpan(end));
    }

    /// <summary>줄바꿈을 뺀 그 줄의 내용. 그런 줄이 없으면 null이다.</summary>
    public static string? ReadLine(string text, int oneBasedLine)
    {
        return TryFindLine(text, oneBasedLine, out int start, out int end)
            ? text[start..end]
            : null;
    }

    /// <summary>
    /// 대사 한 줄을 다시 쓴다. 원본의 들여쓰기를 그대로 살린다.
    /// 화자가 있으면 <c>화자: 텍스트</c> 형태가 된다.
    /// </summary>
    public static string Compose(string indent, string? speaker, string text)
    {
        return string.IsNullOrEmpty(speaker)
            ? indent + text
            : $"{indent}{speaker}: {text}";
    }

    /// <summary>줄 맨 앞의 공백. 들여쓰기는 모델이 깊이로 뭉개므로 원본에서 가져와야 한다.</summary>
    public static string ReadIndent(string line)
    {
        int index = 0;

        while (index < line.Length && (line[index] == ' ' || line[index] == '\t'))
        {
            index++;
        }

        return line[..index];
    }

    private static bool TryFindLine(string text, int oneBasedLine, out int start, out int end)
    {
        start = 0;
        end = 0;

        if (oneBasedLine < 1)
        {
            return false;
        }

        int index = 0;

        for (int line = 1; line < oneBasedLine; line++)
        {
            int next = text.IndexOf('\n', index);

            if (next < 0)
            {
                return false;
            }

            index = next + 1;
        }

        if (index > text.Length)
        {
            return false;
        }

        start = index;
        end = index;

        // CR을 줄 끝으로 본다. CRLF 사이를 가르면 줄바꿈이 깨진다.
        while (end < text.Length && text[end] != '\r' && text[end] != '\n')
        {
            end++;
        }

        return true;
    }
}

/// <summary>고친 대사 한 줄. 줄 번호가 원본에서의 자리를 가리킨다.</summary>
public sealed record StoryLineEdit(
    int Line,
    string? Speaker,
    string Text,
    string? OriginalLine = null);

/// <summary>
/// 고친 줄들을 원본에 반영한다. 뒤에서 앞으로 적용해 앞선 교체가 뒤 줄의 자리를 밀지 않게 한다.
/// </summary>
internal static class StoryLineEditor
{
    public static string Apply(string text, IEnumerable<StoryLineEdit> edits)
    {
        var ordered = new List<StoryLineEdit>(edits);
        ordered.Sort((left, right) => right.Line.CompareTo(left.Line));

        string result = text;

        foreach (StoryLineEdit edit in ordered)
        {
            string? original = StoryLineReplacer.ReadLine(result, edit.Line);

            if (original is null)
            {
                continue;
            }

            result = StoryLineReplacer.Replace(
                result,
                edit.Line,
                StoryLineReplacer.Compose(
                    StoryLineReplacer.ReadIndent(original),
                    edit.Speaker,
                    edit.Text));
        }

        return result;
    }
}
