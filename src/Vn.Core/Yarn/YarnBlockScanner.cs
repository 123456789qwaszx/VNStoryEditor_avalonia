using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Yarn.Compiler;

namespace Vn.Core.Yarn;

/// <summary>
/// 노드 본문의 물리적 줄 하나가 무엇인지.
/// </summary>
internal enum YarnLineKind
{
    /// <summary>재생되는 대사 줄.</summary>
    Line,

    /// <summary><c>-&gt;</c>로 시작하는 선택지 줄.</summary>
    Option,

    /// <summary>쌓이는 명령. <c>&lt;&lt;jump&gt;&gt;</c>, <c>&lt;&lt;set&gt;&gt;</c>, 사용자 명령 등.</summary>
    Command,

    If,
    ElseIf,
    Else,
    EndIf
}

/// <summary>분류된 줄 하나.</summary>
/// <param name="Raw">명령이면 <c>&lt;&lt;&gt;&gt;</c>를 포함한 원본 문자열. 그 밖에는 빈 문자열이다.</param>
internal sealed record YarnScannedLine(
    int Line,
    int Depth,
    YarnLineKind Kind,
    string Raw);

/// <summary>
/// 토큰 스트림을 훑어 줄마다 종류·깊이·원본을 매긴다. 트리는 만들지 않는다.
///
/// <see cref="YarnLineIndex"/>를 고치지 않고 따로 훑는 이유는 둘이 필요로 하는 것이 다르기 때문이다.
/// 평평한 라인 모델은 마지막 라인 뒤에 남은 명령을 버리는데, 노드 끝에 닿는
/// <c>&lt;&lt;endif&gt;&gt;</c>가 거기 해당해서 블록을 닫을 수 없다.
/// 여기서는 버리지 않고 전부 낸다.
/// </summary>
internal static class YarnBlockScanner
{
    /// <summary>들여쓰기 공백 몇 칸이 깊이 1인지. <see cref="Story.StoryLine.Depth"/>와 같은 규칙이다.</summary>
    private const int SpacesPerDepth = 4;

    public static IReadOnlyDictionary<string, List<YarnScannedLine>> Scan(
        CompilationResult result,
        Func<string, string> normalizePath)
    {
        var files = new Dictionary<string, List<YarnScannedLine>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (FileParseResult parseResult in result.ParseResults)
        {
            string path = normalizePath(parseResult.FileName);
            List<YarnScannedLine> scanned = ScanFile(parseResult);

            if (files.TryGetValue(path, out List<YarnScannedLine>? existing))
            {
                existing.AddRange(scanned);
                existing.Sort((left, right) => left.Line.CompareTo(right.Line));
                continue;
            }

            files[path] = scanned;
        }

        return files;
    }

    public static List<YarnScannedLine> ScanFile(FileParseResult parseResult)
    {
        var scanned = new List<YarnScannedLine>();

        IList<IToken> tokens = parseResult.Tokens.GetTokens();
        ICharStream source = parseResult.Tokens.TokenSource.InputStream;

        for (int index = 0; index < tokens.Count;)
        {
            int line = tokens[index].Line;

            int end = index;
            while (end < tokens.Count && tokens[end].Line == line)
            {
                end++;
            }

            YarnScannedLine? classified = Classify(tokens, index, end, source);

            if (classified is not null)
            {
                scanned.Add(classified);
            }

            index = end;
        }

        return scanned;
    }

    private static YarnScannedLine? Classify(
        IList<IToken> tokens,
        int start,
        int end,
        ICharStream source)
    {
        int firstIndex = FindFirstMeaningfulIndex(tokens, start, end);

        if (firstIndex < 0)
        {
            return null;
        }

        IToken first = tokens[firstIndex];
        int depth = first.Column / SpacesPerDepth;

        if (first.Type == YarnSpinnerLexer.SHORTCUT_ARROW)
        {
            return new YarnScannedLine(first.Line, depth, YarnLineKind.Option, string.Empty);
        }

        if (first.Type == YarnSpinnerLexer.TEXT)
        {
            return new YarnScannedLine(first.Line, depth, YarnLineKind.Line, string.Empty);
        }

        if (first.Type != YarnSpinnerLexer.COMMAND_START)
        {
            // 헤더, ---, === 같은 구조 토큰.
            return null;
        }

        // 키워드는 첫 의미 있는 토큰 <em>다음</em>부터 찾아야 한다.
        // 들여쓴 줄은 INDENT가 앞에 오므로, 줄의 시작 인덱스를 기준으로 삼으면
        // COMMAND_START 자신을 키워드로 읽고 조건문이 일반 명령으로 떨어진다.
        int keywordIndex = FindFirstMeaningfulIndex(tokens, firstIndex + 1, end);

        YarnLineKind kind = keywordIndex < 0
            ? YarnLineKind.Command
            : tokens[keywordIndex].Type switch
            {
                YarnSpinnerLexer.COMMAND_IF => YarnLineKind.If,
                YarnSpinnerLexer.COMMAND_ELSEIF => YarnLineKind.ElseIf,
                YarnSpinnerLexer.COMMAND_ELSE => YarnLineKind.Else,
                YarnSpinnerLexer.COMMAND_ENDIF => YarnLineKind.EndIf,
                _ => YarnLineKind.Command
            };

        IToken? commandEnd = FindToken(tokens, start, end, YarnSpinnerLexer.COMMAND_END);

        string raw = commandEnd is null
            ? string.Empty
            : Slice(source, first.StartIndex, commandEnd.StopIndex);

        return new YarnScannedLine(first.Line, depth, kind, raw);
    }

    /// <summary>
    /// 줄의 정체를 정하는 첫 토큰의 <em>인덱스</em>. 들여쓰기 토큰과 숨은 채널은 건너뛴다.
    /// 토큰 자체가 아니라 인덱스를 돌려주는 이유는, 그 다음부터 이어서 찾아야 하는 자리가 있기 때문이다.
    /// </summary>
    private static int FindFirstMeaningfulIndex(IList<IToken> tokens, int start, int end)
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

            return index;
        }

        return -1;
    }

    private static IToken? FindToken(IList<IToken> tokens, int start, int end, int type)
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

    private static string Slice(ICharStream source, int start, int stop)
    {
        return stop < start || start < 0
            ? string.Empty
            : source.GetText(Interval.Of(start, stop));
    }
}
