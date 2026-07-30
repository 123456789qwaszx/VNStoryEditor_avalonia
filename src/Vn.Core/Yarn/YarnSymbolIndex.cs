using Antlr4.Runtime;
using Yarn.Compiler;
using Vn.Core.Story;

namespace Vn.Core.Yarn;

/// <summary>
/// 변수와 명령이 소스에서 실제로 쓰인 줄·열을 모아둔다.
///
/// <see cref="NodeMetadata.VariableReferences"/>와 <see cref="NodeMetadata.CommandCalls"/>는
/// 이름만 주고 위치를 주지 않는다. 그래서 그대로 쓰면 진단이 노드 헤더만 가리키게 되고,
/// 200줄짜리 노드에서는 "이 노드 어딘가에 있다"는 말밖에 못 한다.
///
/// 다행히 <see cref="CompilationResult.ParseResults"/>는 공개 API이고
/// 여기에 렉서 토큰 스트림이 들어 있다. 파스 트리를 직접 걸어가지 않고
/// 토큰만 훑어도 정확한 위치가 나온다. Yarn 내부 API에 의존하지 않는다.
/// </summary>
internal sealed class YarnSymbolIndex
{
    private readonly IReadOnlyDictionary<string, FileSymbols> _files;

    private YarnSymbolIndex(IReadOnlyDictionary<string, FileSymbols> files)
    {
        _files = files;
    }

    public static YarnSymbolIndex Empty { get; } =
        new(new Dictionary<string, FileSymbols>(StringComparer.OrdinalIgnoreCase));

    public static YarnSymbolIndex Build(
        CompilationResult result,
        Func<string, string> normalizePath)
    {
        var files =
            new Dictionary<string, FileSymbols>(StringComparer.OrdinalIgnoreCase);

        foreach (FileParseResult parseResult in result.ParseResults)
        {
            string path = normalizePath(parseResult.FileName);
            FileSymbols symbols = Scan(parseResult, path);

            // 같은 파일이 두 번 나오는 경우는 없지만, 나오더라도 조용히 덮어쓰지 않는다.
            if (files.TryGetValue(path, out FileSymbols? existing))
            {
                existing.Merge(symbols);
                continue;
            }

            files[path] = symbols;
        }

        return new YarnSymbolIndex(files);
    }

    /// <summary>
    /// 노드 안에서 <paramref name="names"/>가 쓰인 지점을 모두 찾는다.
    ///
    /// 찾는 순서는 본문 → 헤더 → 실패다.
    /// 헤더를 따로 보는 이유는 Yarn의 <c>when:</c> 절처럼 본문 밖에서 변수를 쓰는 자리가 있기 때문이다.
    /// 그 토큰은 이미 인덱스에 들어 있으므로, 본문에서 못 찾았다고 바로 헤더 1열로 포기하면
    /// 알고 있는 위치를 스스로 버리는 셈이 된다.
    ///
    /// 세 단계 모두 실패해도 진단 자체를 버리지는 않는다. 위치가 부정확한 진단이
    /// 진단이 없는 것보다는 낫다.
    /// 같은 이름을 한 노드에서 두 번 쓰면 두 개가 나오며, 이는 의도된 동작이다.
    /// 고칠 곳이 두 군데면 진단도 두 개여야 한다.
    /// </summary>
    public IReadOnlyList<StoryReference> Resolve(
        YarnSymbolKind kind,
        string filePath,
        int headerLine,
        int bodyStartLine,
        int bodyEndLine,
        IEnumerable<string> names)
    {
        IReadOnlyList<Occurrence> candidates =
            _files.TryGetValue(filePath, out FileSymbols? symbols)
                ? symbols.Get(kind)
                : Array.Empty<Occurrence>();

        var references = new List<StoryReference>();

        foreach (string name in names.Distinct(StringComparer.Ordinal))
        {
            if (Collect(candidates, name, filePath, bodyStartLine, bodyEndLine, references))
            {
                continue;
            }

            // 헤더 구간: 헤더 첫 줄부터 본문 시작 직전까지.
            // headerLine이 0이면 위치 정보 자체가 없는 것이므로 건너뛴다.
            if (headerLine > 0 &&
                Collect(candidates, name, filePath, headerLine, bodyStartLine - 1, references))
            {
                continue;
            }

            references.Add(new StoryReference(
                name,
                filePath,
                headerLine,
                1));
        }

        return references
            .OrderBy(reference => reference.Line)
            .ThenBy(reference => reference.Column)
            .ThenBy(reference => reference.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// 주어진 줄 범위 안에서 이름이 쓰인 지점을 전부 모은다.
    /// 하나라도 찾으면 true. 범위가 뒤집혀 있으면(비어 있으면) 아무것도 하지 않는다.
    /// </summary>
    private static bool Collect(
        IReadOnlyList<Occurrence> candidates,
        string name,
        string filePath,
        int fromLine,
        int toLine,
        ICollection<StoryReference> references)
    {
        if (fromLine <= 0 || toLine < fromLine)
        {
            return false;
        }

        bool found = false;

        foreach (Occurrence occurrence in candidates)
        {
            if (occurrence.Line < fromLine ||
                occurrence.Line > toLine ||
                !string.Equals(occurrence.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            references.Add(new StoryReference(
                name,
                filePath,
                occurrence.Line,
                occurrence.Column));

            found = true;
        }

        return found;
    }

    private static FileSymbols Scan(FileParseResult parseResult, string path)
    {
        var variables = new List<Occurrence>();
        var commands = new List<Occurrence>();

        IList<IToken> tokens = parseResult.Tokens.GetTokens();

        for (int index = 0; index < tokens.Count; index++)
        {
            IToken token = tokens[index];

            if (token.Channel != TokenConstants.DefaultChannel)
            {
                continue;
            }

            if (token.Type == YarnSpinnerLexer.VAR_ID)
            {
                variables.Add(new Occurrence(
                    token.Text,
                    token.Line,
                    ToOneBasedColumn(token.Column)));

                continue;
            }

            if (token.Type == YarnSpinnerLexer.COMMAND_START &&
                TryReadCommandName(tokens, index, out Occurrence command))
            {
                commands.Add(command);
            }
        }

        return new FileSymbols(path, variables, commands);
    }

    /// <summary>
    /// <c>&lt;&lt;play_bgm "track"&gt;&gt;</c>에서 명령 이름과 그 시작 위치를 읽는다.
    ///
    /// 렉서는 명령 본문을 COMMAND_TEXT 토큰 여러 개로 쪼개 내보내므로
    /// (실제로 <c>"p"</c>와 <c>"lay_bgm ..."</c>처럼 나뉜다) 이어 붙인 뒤 첫 낱말을 취한다.
    /// 다음 토큰이 COMMAND_SET·COMMAND_JUMP 같은 키워드면 명령 호출이 아니라
    /// 문법 구조이므로 건너뛴다.
    /// </summary>
    private static bool TryReadCommandName(
        IList<IToken> tokens,
        int commandStartIndex,
        out Occurrence occurrence)
    {
        occurrence = default;

        IToken? first = null;
        string text = string.Empty;

        for (int index = commandStartIndex + 1; index < tokens.Count; index++)
        {
            IToken token = tokens[index];

            if (token.Channel != TokenConstants.DefaultChannel)
            {
                continue;
            }

            if (token.Type != YarnSpinnerLexer.COMMAND_TEXT)
            {
                break;
            }

            first ??= token;
            text += token.Text;
        }

        if (first is null)
        {
            return false;
        }

        int nameStart = 0;
        while (nameStart < text.Length && char.IsWhiteSpace(text[nameStart]))
        {
            nameStart++;
        }

        int nameEnd = nameStart;
        while (nameEnd < text.Length && !char.IsWhiteSpace(text[nameEnd]))
        {
            nameEnd++;
        }

        if (nameEnd == nameStart)
        {
            return false;
        }

        occurrence = new Occurrence(
            text[nameStart..nameEnd],
            first.Line,
            ToOneBasedColumn(first.Column) + nameStart);

        return true;
    }

    private static int ToOneBasedColumn(int zeroBased)
    {
        return zeroBased < 0
            ? 1
            : zeroBased + 1;
    }

    private readonly record struct Occurrence(
        string Name,
        int Line,
        int Column);

    private sealed class FileSymbols
    {
        private readonly List<Occurrence> _variables;
        private readonly List<Occurrence> _commands;

        public FileSymbols(
            string path,
            List<Occurrence> variables,
            List<Occurrence> commands)
        {
            Path = path;
            _variables = variables;
            _commands = commands;
        }

        public string Path { get; }

        public IReadOnlyList<Occurrence> Get(YarnSymbolKind kind)
        {
            return kind == YarnSymbolKind.Variable
                ? _variables
                : _commands;
        }

        public void Merge(FileSymbols other)
        {
            _variables.AddRange(other._variables);
            _commands.AddRange(other._commands);
        }
    }
}

internal enum YarnSymbolKind
{
    Variable,
    Command
}