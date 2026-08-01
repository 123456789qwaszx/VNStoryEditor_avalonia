using Yarn.Compiler;
using Vn.Core.Story;

namespace Vn.Core.Yarn;

/// <summary>
/// 분류된 줄을 분기 블록 트리로 접는다.
///
/// 두 블록의 경계 판정 방식이 다르며, 그것이 정상이다.
///   선택지 — 들여쓰기로 갈린다. Yarn이 선택지 본문에 들여쓰기를 요구한다.
///   조건   — 구분자로 갈린다. <c>&lt;&lt;if&gt;&gt;</c> 본문은 들여쓰지 않아도 되고
///            실제 대본이 그렇게 쓰여 있다. 깊이로 판정하면 갈래가 통째로 비어버린다.
///
/// 둘은 서로 중첩되므로 한 파서가 두 규칙을 함께 다룬다.
/// </summary>
internal sealed class YarnBlockIndex
{
    private readonly IReadOnlyDictionary<string, List<YarnScannedLine>> _files;

    private YarnBlockIndex(IReadOnlyDictionary<string, List<YarnScannedLine>> files)
    {
        _files = files;
    }

    public static YarnBlockIndex Build(
        CompilationResult result,
        Func<string, string> normalizePath)
    {
        return new YarnBlockIndex(YarnBlockScanner.Scan(result, normalizePath));
    }

    /// <summary>
    /// 노드 본문의 트리. 최상위에 라인과 블록이 원본 순서대로 섞여 있다.
    /// </summary>
    /// <param name="lines">
    /// 같은 노드의 평평한 라인 목록. 라인 내용은 여기서 줄 번호로 가져온다.
    /// 같은 것을 두 번 계산해 두 모델이 서로 다른 말을 하게 만들지 않기 위해서다.
    /// </param>
    public IReadOnlyList<StoryElement> GetBody(
        string filePath,
        int bodyStartLine,
        int bodyEndLine,
        IReadOnlyList<StoryLine> lines)
    {
        if (!_files.TryGetValue(filePath, out List<YarnScannedLine>? all))
        {
            return Array.Empty<StoryElement>();
        }

        List<YarnScannedLine> scanned = all
            .Where(item => item.Line >= bodyStartLine && item.Line <= bodyEndLine)
            .ToList();

        var parser = new Parser(
            scanned,
            lines.ToDictionary(line => line.Line),
            filePath);

        int index = 0;
        return parser.ParseElements(ref index, minDepth: 0, stopAtDelimiter: false);
    }

    private sealed class Parser
    {
        private readonly List<YarnScannedLine> _items;
        private readonly IReadOnlyDictionary<int, StoryLine> _lines;
        private readonly string _filePath;

        public Parser(
            List<YarnScannedLine> items,
            IReadOnlyDictionary<int, StoryLine> lines,
            string filePath)
        {
            _items = items;
            _lines = lines;
            _filePath = filePath;
        }

        /// <param name="commands">
        /// 어느 박스도 닫지 못하고 이 층에 남은 명령이 여기 담긴다.
        /// 라인 앞에 온 명령은 그 라인의 박스로 들어가므로 여기 오지 않는다 —
        /// 양쪽에 다 넣으면 화면에서 같은 명령이 두 번 보인다.
        /// null이면 버린다. 최상위에는 담아 둘 갈래가 없다.
        /// </param>
        public List<StoryElement> ParseElements(
            ref int index,
            int minDepth,
            bool stopAtDelimiter,
            List<StoryCommand>? commands = null)
        {
            var elements = new List<StoryElement>();
            bool leftForOuter = false;

            // 이 층에서 직전 라인 이후에 나타난 명령. 이 층의 다음 라인이 닫는다.
            // 층마다 따로 두는 것이 핵심이다. 하나로 공유하면 <<if>> 갈래의 명령이
            // <<else>> 갈래의 첫 대사에 붙는다 — 원본에서 바로 다음 라인이기 때문이다.
            // 갈래가 바뀌면 쌓인 것은 그 갈래에 남고 다음 갈래로 넘어가지 않는다.
            var pending = new List<StoryCommand>();

            while (index < _items.Count)
            {
                YarnScannedLine item = _items[index];

                if (stopAtDelimiter &&
                    item.Kind is YarnLineKind.ElseIf or YarnLineKind.Else or YarnLineKind.EndIf)
                {
                    break;
                }

                switch (item.Kind)
                {
                    // 조건 갈래의 끝은 구분자가 정하지만, 조건 블록이 <em>어디에 속하는지</em>는
                    // 여전히 깊이가 정한다. 갈래보다 얕은 <<if>>는 그 갈래 바깥의 것이다.
                    // 가드가 없으면 선택지 그룹 다음에 오는 조건문이 마지막 갈래 안으로 빨려 들어가고,
                    // 그 안의 대사와 <<endif>>가 블록 밖으로 밀려난다.
                    case YarnLineKind.If when item.Depth >= minDepth:
                        elements.Add(new StoryBlockElement(
                            ParseCondition(ref index, minDepth)));
                        break;

                    case YarnLineKind.Option when item.Depth >= minDepth:
                        elements.Add(new StoryBlockElement(
                            ParseOptionGroup(ref index, item.Depth, stopAtDelimiter)));
                        break;

                    case YarnLineKind.Line when item.Depth >= minDepth:
                        if (_lines.TryGetValue(item.Line, out StoryLine? line))
                        {
                            elements.Add(new StoryLineElement(
                                line with { CommandsSincePreviousLine = pending.ToArray() }));
                        }

                        pending.Clear();
                        index++;
                        break;

                    case YarnLineKind.Option:
                    case YarnLineKind.Line:
                    case YarnLineKind.If:
                        // 여기보다 얕다. 바깥 갈래의 것이므로 넘긴다.
                        leftForOuter = true;
                        break;

                    default:
                        // 짝 없는 구분자도 여기로 와서 조용히 지나간다. 멈추면 무한 반복이 된다.
                        if (item.Kind == YarnLineKind.Command)
                        {
                            pending.Add(new StoryCommand(
                                item.Raw,
                                item.Line,
                                item.Column,
                                item.Depth));
                        }

                        index++;
                        break;
                }

                if (leftForOuter)
                {
                    break;
                }
            }

            // 남은 것은 뒤에 라인이 없어 어느 박스도 닫지 못한 명령이다.
            // 갈래 끝의 <<jump>>가 여기 해당하며, 목적지로 올라가는 것도 이 자리에서다.
            commands?.AddRange(pending);

            return elements;
        }

        private StoryBlock ParseOptionGroup(ref int index, int depth, bool stopAtDelimiter)
        {
            int startLine = _items[index].Line;
            int endLine = startLine;

            var branches = new List<StoryBranch>();

            while (index < _items.Count &&
                   _items[index].Kind == YarnLineKind.Option &&
                   _items[index].Depth == depth)
            {
                YarnScannedLine header = _items[index];
                index++;

                var commands = new List<StoryCommand>();

                List<StoryElement> children =
                    ParseElements(ref index, depth + 1, stopAtDelimiter, commands);

                // 끝줄은 목적지를 올리기 전에 잰다. 올리고 나면 그 점프가 명령 목록에서 빠져
                // 갈래가 실제보다 짧아 보인다.
                endLine = LastLine(children, commands, header.Line);

                branches.Add(new StoryBranch(
                    LabelOf(header),
                    header.Line,
                    LiftDestination(commands, children),
                    commands,
                    children));
            }

            return new StoryBlock(
                StoryBlockKind.Option,
                branches,
                _filePath,
                startLine,
                endLine,
                depth);
        }

        private StoryBlock ParseCondition(ref int index, int minDepth)
        {
            YarnScannedLine opening = _items[index];

            int startLine = opening.Line;
            int endLine = startLine;

            var branches = new List<StoryBranch>();

            while (index < _items.Count)
            {
                YarnScannedLine header = _items[index];

                if (header.Kind is not (YarnLineKind.If or YarnLineKind.ElseIf or YarnLineKind.Else))
                {
                    break;
                }

                index++;

                var commands = new List<StoryCommand>();

                // 조건 갈래의 끝은 깊이가 아니라 구분자가 정한다.
                // 그래서 바깥에서 받은 minDepth를 그대로 물려준다.
                List<StoryElement> children =
                    ParseElements(ref index, minDepth, stopAtDelimiter: true, commands);

                // 조건 갈래에는 Destination을 두지 않는다. 목적지는 선택지의 개념이다.
                branches.Add(new StoryBranch(
                    header.Raw,
                    header.Line,
                    null,
                    commands,
                    children));

                endLine = LastLine(children, commands, header.Line);
            }

            if (index < _items.Count && _items[index].Kind == YarnLineKind.EndIf)
            {
                endLine = _items[index].Line;
                index++;
            }

            return new StoryBlock(
                StoryBlockKind.Condition,
                branches,
                _filePath,
                startLine,
                endLine,
                opening.Depth);
        }

        private string LabelOf(YarnScannedLine option)
        {
            // 선택지 라벨은 화면에 뜨는 표시 텍스트다. 평평한 모델이 이미 뽑아 둔 것을 쓴다.
            // 조건부 선택지의 <<if ...>>는 텍스트에 그대로 남는다. 그 if는 갈래를 만들지 않고
            // 한 선택지의 등장 여부만 정하므로 블록으로 승격하지 않는다.
            return _lines.TryGetValue(option.Line, out StoryLine? line)
                ? line.Text
                : string.Empty;
        }


        /// <summary>
        /// 갈래 끝의 <c>&lt;&lt;jump X&gt;&gt;</c> 하나를 목적지로 올리고 명령 목록에서 뺀다.
        ///
        /// 올리는 조건은 셋이다. 점프가 정확히 하나이고, 그것이 명령 중 마지막이며,
        /// 그 뒤에 라인이나 중첩 블록이 없어야 한다. 중간에 있는 점프는 조건부 흐름일 수 있고,
        /// 점프가 여럿이면 규약 밖이다. 어느 쪽도 갈래 전체의 목적지라고 말할 수 없다.
        ///
        /// 점프가 없으면 null이다. 오류가 아니라 "갈래가 끝나면 그룹 다음으로 흘러간다"는 뜻이다.
        /// 판정만 하고 진단은 내지 않는다.
        /// </summary>
        private static string? LiftDestination(
            List<StoryCommand> commands,
            IReadOnlyList<StoryElement> children)
        {
            if (commands.Count == 0)
            {
                return null;
            }

            var jumps = commands
                .Select((command, position) => (command, position))
                .Where(item => TryReadJump(item.command.Raw, out _))
                .ToList();

            if (jumps.Count != 1)
            {
                return null;
            }

            (StoryCommand jump, int index) = jumps[0];

            if (index != commands.Count - 1)
            {
                return null;
            }

            int lastChildLine = LastLine(children, Array.Empty<StoryCommand>(), 0);

            if (lastChildLine > jump.Line)
            {
                return null;
            }

            TryReadJump(jump.Raw, out string destination);
            commands.RemoveAt(index);

            return destination;
        }

        /// <summary>
        /// <c>&lt;&lt;jump Ending&gt;&gt;</c>에서 대상 이름을 읽는다.
        /// <c>&lt;&lt;detour&gt;&gt;</c>나 <c>&lt;&lt;beat&gt;&gt;</c>는 노드 이름을 받아도 점프가 아니다.
        /// </summary>
        private static bool TryReadJump(string raw, out string destination)
        {
            destination = string.Empty;

            if (raw.Length < 4 || !raw.StartsWith("<<", StringComparison.Ordinal) ||
                !raw.EndsWith(">>", StringComparison.Ordinal))
            {
                return false;
            }

            string inner = raw[2..^2].Trim();

            if (!inner.StartsWith("jump", StringComparison.Ordinal))
            {
                return false;
            }

            string rest = inner[4..];

            if (rest.Length == 0 || !char.IsWhiteSpace(rest[0]))
            {
                return false;
            }

            destination = rest.Trim();
            return destination.Length > 0;
        }

        private static int LastLine(
            IReadOnlyList<StoryElement> children,
            IReadOnlyList<StoryCommand> commands,
            int fallback)
        {
            int last = fallback;

            foreach (StoryElement child in children)
            {
                last = Math.Max(last, child switch
                {
                    StoryLineElement lineElement => lineElement.Line.Line,
                    StoryBlockElement blockElement => blockElement.Block.EndLine,
                    _ => fallback
                });
            }

            foreach (StoryCommand command in commands)
            {
                last = Math.Max(last, command.Line);
            }

            return last;
        }
    }
}
