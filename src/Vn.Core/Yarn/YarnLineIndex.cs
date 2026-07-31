using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Yarn.Compiler;
using Vn.Core.Story;

namespace Vn.Core.Yarn;

/// <summary>
/// 노드 본문을 박스 단위로 쪼갠다.
///
/// 박스 경계 규칙: 명령과 빈 줄은 쌓이고, 재생되는 라인이 나타나면 박스가 닫힌다.
///
/// <see cref="YarnSymbolIndex"/>와 같은 방식(렉서 토큰 스트림 훑기)을 쓰지만 별도 파일이다.
/// 그쪽은 진단 위치의 원본이라, 건드렸다가 어긋나면 모든 진단이 엉뚱한 곳을 가리킨다.
///
/// <b><see cref="CompilationResult.StringTable"/>은 쓰지 않는다.</b> 실측해 보면
/// 라인 집합 자체는 정확하지만 텍스트가 이미 가공되어 있다.
///   <c>값은 {$favor}입니다.</c>          → <c>값은 {0}입니다.</c>
///   <c>-&gt; 믿어본다 &lt;&lt;if $favor &gt;= 8&gt;&gt;</c> → <c>믿어본다</c>
/// 인터폴레이션이 자리표시자로 바뀌고 조건부 선택지의 조건식이 잘려 있어서,
/// 그것을 그대로 들고 있다가 저장하면 작가의 원고가 파괴된다.
/// 그래서 토큰 위치로 원본을 잘라 쓴다.
/// </summary>
internal sealed class YarnLineIndex
{
    /// <summary>들여쓰기 공백 몇 칸이 깊이 1인지.</summary>
    private const int SpacesPerDepth = 4;

    private readonly IReadOnlyDictionary<string, List<Entry>> _files;

    private YarnLineIndex(IReadOnlyDictionary<string, List<Entry>> files)
    {
        _files = files;
    }

    public static YarnLineIndex Build(
        CompilationResult result,
        Func<string, string> normalizePath)
    {
        var files = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);

        foreach (FileParseResult parseResult in result.ParseResults)
        {
            string path = normalizePath(parseResult.FileName);
            List<Entry> entries = Scan(parseResult, path);

            if (files.TryGetValue(path, out List<Entry>? existing))
            {
                existing.AddRange(entries);
                existing.Sort((left, right) => left.Line.CompareTo(right.Line));
                continue;
            }

            files[path] = entries;
        }

        return new YarnLineIndex(files);
    }

    /// <summary>
    /// 노드 본문 범위 안의 박스를 순서대로 돌려준다.
    ///
    /// 쌓기는 노드 범위 안에서만 한다. 파일 전체로 쌓으면 앞 노드에 남은 명령이
    /// 다음 노드의 첫 라인에 붙는다.
    /// 마지막 라인 뒤에 남은 명령은 버린다. 어느 박스도 닫지 않았기 때문이다.
    /// </summary>
    public IReadOnlyList<StoryLine> GetLines(
        string filePath,
        int bodyStartLine,
        int bodyEndLine)
    {
        if (!_files.TryGetValue(filePath, out List<Entry>? entries))
        {
            return Array.Empty<StoryLine>();
        }

        var lines = new List<StoryLine>();
        var pending = new List<string>();

        foreach (Entry entry in entries)
        {
            if (entry.Line < bodyStartLine || entry.Line > bodyEndLine)
            {
                continue;
            }

            if (entry.Command is not null)
            {
                pending.Add(entry.Command);
                continue;
            }

            lines.Add(entry.ToStoryLine(filePath, pending.ToArray()));
            pending.Clear();
        }

        return lines;
    }

    private static List<Entry> Scan(FileParseResult parseResult, string path)
    {
        var entries = new List<Entry>();

        IList<IToken> tokens = parseResult.Tokens.GetTokens();
        ICharStream source = parseResult.Tokens.TokenSource.InputStream;

        // 물리적 줄 하나씩 본다. 한 줄의 첫 토큰이 그 줄의 정체를 결정한다.
        for (int index = 0; index < tokens.Count;)
        {
            int line = tokens[index].Line;

            int end = index;
            while (end < tokens.Count && tokens[end].Line == line)
            {
                end++;
            }

            AddEntry(entries, tokens, index, end, source);
            index = end;
        }

        return entries;
    }

    private static void AddEntry(
        List<Entry> entries,
        IList<IToken> tokens,
        int start,
        int end,
        ICharStream source)
    {
        IToken? first = FindFirstMeaningfulToken(tokens, start, end);

        if (first is null)
        {
            return;
        }

        if (first.Type == YarnSpinnerLexer.COMMAND_START)
        {
            IToken? commandEnd = FindToken(tokens, start, end, YarnSpinnerLexer.COMMAND_END);

            if (commandEnd is not null)
            {
                entries.Add(Entry.ForCommand(
                    first.Line,
                    Slice(source, first.StartIndex, commandEnd.StopIndex)));
            }

            return;
        }

        bool isOption = first.Type == YarnSpinnerLexer.SHORTCUT_ARROW;

        if (!isOption && first.Type != YarnSpinnerLexer.TEXT)
        {
            // 헤더, ---, ===, 그 밖의 구조 토큰. 라인도 명령도 아니다.
            return;
        }

        entries.Add(BuildLineEntry(tokens, start, end, source, first, isOption));
    }

    private static Entry BuildLineEntry(
        IList<IToken> tokens,
        int start,
        int end,
        ICharStream source,
        IToken first,
        bool isOption)
    {
        // 텍스트는 첫 해시태그나 첫 주석 중 앞선 것에서 끝난다.
        // 주석까지 보는 이유는 "대사 //#box:blackbook" 같은 줄 때문이다.
        // 해시태그만 경계로 삼으면 주석이 통째로 대사 안에 딸려 들어온다.
        int boundary = int.MaxValue;
        var hashtags = new List<string>();

        for (int index = start; index < end; index++)
        {
            IToken token = tokens[index];

            if (token.Channel == YarnSpinnerLexer.COMMENTS)
            {
                boundary = Math.Min(boundary, token.StartIndex);
                continue;
            }

            if (token.Type == YarnSpinnerLexer.HASHTAG)
            {
                boundary = Math.Min(boundary, token.StartIndex);
                continue;
            }

            if (token.Type == YarnSpinnerLexer.HASHTAG_TEXT)
            {
                hashtags.Add(token.Text);
            }
        }

        int textStart = isOption
            ? first.StopIndex + 1
            : first.StartIndex;

        int textEnd = boundary == int.MaxValue
            ? LastTextIndex(tokens, start, end)
            : boundary - 1;

        string text = Slice(source, textStart, textEnd).Trim();

        SplitSpeaker(text, out string? speaker, out string body);

        return Entry.ForLine(
            first.Line,
            ToOneBasedColumn(first.Column),
            first.Column / SpacesPerDepth,
            isOption,
            speaker,
            body,
            hashtags);
    }

    /// <summary>
    /// 첫 <c>:</c> 앞을 화자로 본다. 다만 그 앞부분에 공백이나
    /// <c>{</c>·<c>&lt;</c>·<c>//</c>가 있으면 화자가 아니라 그냥 문장이다.
    ///
    /// <c>결과: 피로도 {$fatigue}</c>가 화자 <c>결과</c>가 되는 것은 의도된 결과다.
    /// Yarn과 Unity가 그렇게 해석하므로, 도구가 다르게 보면 그쪽이 거짓말이 된다.
    /// </summary>
    private static void SplitSpeaker(string text, out string? speaker, out string body)
    {
        speaker = null;
        body = text;

        int colon = text.IndexOf(':');

        if (colon <= 0)
        {
            return;
        }

        string candidate = text[..colon];

        if (candidate.Contains("//", StringComparison.Ordinal))
        {
            return;
        }

        foreach (char character in candidate)
        {
            if (char.IsWhiteSpace(character) || character is '{' or '<')
            {
                return;
            }
        }

        speaker = candidate;
        body = text[(colon + 1)..].TrimStart();
    }

    /// <summary>
    /// 줄의 정체를 정하는 첫 토큰. 들여쓰기 토큰과 공백은 건너뛴다.
    /// INDENT/DEDENT는 들여쓰기가 바뀌는 줄에서만 맨 앞에 나타나므로,
    /// 건너뛰지 않으면 그 줄만 정체를 잃는다.
    /// </summary>
    private static IToken? FindFirstMeaningfulToken(
        IList<IToken> tokens,
        int start,
        int end)
    {
        for (int index = start; index < end; index++)
        {
            IToken token = tokens[index];

            if (token.Channel != TokenConstants.DefaultChannel)
            {
                continue;
            }

            if (token.Type is YarnSpinnerLexer.INDENT
                or YarnSpinnerLexer.DEDENT
                or YarnSpinnerLexer.NEWLINE)
            {
                continue;
            }

            return token;
        }

        return null;
    }

    private static IToken? FindToken(
        IList<IToken> tokens,
        int start,
        int end,
        int type)
    {
        for (int index = start; index < end; index++)
        {
            if (tokens[index].Type == type)
            {
                return tokens[index];
            }
        }

        return null;
    }

    private static int LastTextIndex(IList<IToken> tokens, int start, int end)
    {
        int last = -1;

        for (int index = start; index < end; index++)
        {
            IToken token = tokens[index];

            if (token.Type is YarnSpinnerLexer.NEWLINE or TokenConstants.EOF)
            {
                continue;
            }

            last = Math.Max(last, token.StopIndex);
        }

        return last;
    }

    private static string Slice(ICharStream source, int start, int stop)
    {
        if (stop < start || start < 0)
        {
            return string.Empty;
        }

        return source.GetText(Interval.Of(start, stop));
    }

    private static int ToOneBasedColumn(int zeroBased)
    {
        return zeroBased < 0
            ? 1
            : zeroBased + 1;
    }

    /// <summary>
    /// 스캔 결과 한 줄. <see cref="Command"/>가 있으면 쌓이는 명령이고, 없으면 박스를 닫는 라인이다.
    /// </summary>
    private sealed record Entry(
        int Line,
        string? Command,
        int Column,
        int Depth,
        bool IsOption,
        string? Speaker,
        string Text,
        IReadOnlyList<string> Hashtags)
    {
        public static Entry ForCommand(int line, string raw)
        {
            return new Entry(line, raw, 0, 0, false, null, string.Empty, Array.Empty<string>());
        }

        public static Entry ForLine(
            int line,
            int column,
            int depth,
            bool isOption,
            string? speaker,
            string text,
            IReadOnlyList<string> hashtags)
        {
            return new Entry(line, null, column, depth, isOption, speaker, text, hashtags);
        }

        public StoryLine ToStoryLine(string filePath, IReadOnlyList<string> commands)
        {
            return new StoryLine(
                Speaker,
                Text,
                Hashtags,
                filePath,
                Line,
                Column,
                Depth,
                IsOption,
                commands);
        }
    }
}
