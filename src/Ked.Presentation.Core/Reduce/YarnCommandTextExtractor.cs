using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 대사 라인 경계 하나. Commands는 "직전 대사 이후 ~ 이 대사까지" 사이의 커맨드들이다.
    /// 파일 끝의 대사 없는 꼬리 커맨드들은 LineText가 null인 마지막 항목으로 남는다.
    /// </summary>
    public sealed class YarnLineGroup
    {
        public string LineText;                 // 대사 원문 (화자 포함, 태그 제외). 꼬리 그룹이면 null
        public string LineId;                   // #line: 태그 값. 없으면 null
        public readonly List<StageCommand> Commands = new List<StageCommand>();
    }

    /// <summary>
    /// U14 등가성 하네스 전용 — yarn "원문"에서 커맨드 열을 라인 경계 단위로 뽑는다.
    ///
    /// ⚠ 용도 한정: 이것은 Yarn 문법 해석기가 아니다. 대표 에피소드처럼 선형인
    /// 파일에서 "재생이 만나는 순서 그대로"의 커맨드 열을 얻는 것이 목적이다.
    /// 분기(if/jump)·옵션은 다루지 않으며, 만나면 커맨드가 아니라 경고 목록에 남긴다 —
    /// 그런 파일은 이 추출기로 접으면 안 된다는 신호다.
    /// (VnTool의 붙여넣기 해석과는 별개의 물건이다 — 그쪽은 저작 규칙, 이쪽은 하네스.)
    /// </summary>
    /// <summary>노드 하나의 라인 그룹들.</summary>
    public sealed class YarnNodeGroups
    {
        public string NodeName;
        public readonly List<YarnLineGroup> Groups = new List<YarnLineGroup>();
    }

    public static class YarnCommandTextExtractor
    {
        /// <summary>파일 전체를 노드 구분 없이 하나의 흐름으로 뽑는다(단일 노드 파일용).</summary>
        public static List<YarnLineGroup> Extract(
            string yarnText, string sourceName, List<string> warnings = null)
        {
            List<YarnLineGroup> flattened = new List<YarnLineGroup>();

            foreach (YarnNodeGroups node in ExtractNodes(yarnText, sourceName, warnings))
                flattened.AddRange(node.Groups);

            return flattened;
        }

        /// <summary>노드별로 뽑는다. 한 파일에 여러 노드가 있을 수 있다(예: Set + Pres).</summary>
        public static List<YarnNodeGroups> ExtractNodes(
            string yarnText, string sourceName, List<string> warnings = null)
        {
            if (yarnText == null)
                throw new ArgumentNullException(nameof(yarnText));

            List<YarnNodeGroups> nodes = new List<YarnNodeGroups>();
            YarnNodeGroups node = null;
            YarnLineGroup current = new YarnLineGroup();
            string pendingTitle = null;

            string[] lines = yarnText.Replace("\r\n", "\n").Split('\n');
            bool inHeader = true;

            void FlushNode()
            {
                if (node == null)
                    return;

                if (current.Commands.Count > 0)
                    node.Groups.Add(current); // 꼬리 커맨드 그룹 (LineText null)

                nodes.Add(node);
                node = null;
                current = new YarnLineGroup();
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                string line = StripComment(raw).Trim();

                if (line.Length == 0)
                    continue;

                // 노드 헤더: title: ~ --- 사이. === 는 노드 끝.
                if (inHeader)
                {
                    if (line.StartsWith("title:", StringComparison.Ordinal))
                        pendingTitle = line.Substring("title:".Length).Trim();

                    if (line == "---")
                    {
                        node = new YarnNodeGroups { NodeName = pendingTitle };
                        pendingTitle = null;
                        inHeader = false;
                    }

                    continue;
                }

                if (line == "===")
                {
                    FlushNode();
                    inHeader = true;
                    continue;
                }

                if (line.StartsWith("<<", StringComparison.Ordinal))
                {
                    if (TryParseCommand(line, $"{sourceName}:{i + 1}", out StageCommand command))
                    {
                        if (IsFlowControl(command.Name))
                        {
                            warnings?.Add(
                                $"{sourceName}:{i + 1} 분기/제어 커맨드 '{command.Name}' — " +
                                "이 추출기는 선형 파일 전용이다. 결과를 믿지 말 것.");
                        }

                        current.Commands.Add(command);
                    }
                    else
                    {
                        warnings?.Add($"{sourceName}:{i + 1} 커맨드를 읽지 못했다: {line}");
                    }

                    continue;
                }

                if (line.StartsWith("->", StringComparison.Ordinal))
                {
                    warnings?.Add($"{sourceName}:{i + 1} 옵션(->)은 다루지 않는다 — 선형 파일 전용.");
                    continue;
                }

                // 대사 라인 = 경계.
                current.LineText = StripTags(line, out string lineId);
                current.LineId = lineId;
                node?.Groups.Add(current);
                current = new YarnLineGroup();
            }

            FlushNode();
            return nodes;
        }

        /// <summary>"&lt;&lt;cmd a "b c" d&gt;&gt;" → StageCommand. 따옴표 인자를 존중한다.</summary>
        public static bool TryParseCommand(string line, string source, out StageCommand command)
        {
            command = default;

            string trimmed = line.Trim();

            if (!trimmed.StartsWith("<<", StringComparison.Ordinal) ||
                !trimmed.EndsWith(">>", StringComparison.Ordinal))
            {
                return false;
            }

            string body = trimmed.Substring(2, trimmed.Length - 4).Trim();

            if (body.Length == 0)
                return false;

            List<string> tokens = SplitRespectingQuotes(body);

            if (tokens.Count == 0)
                return false;

            string name = tokens[0];
            tokens.RemoveAt(0);

            command = new StageCommand(name, tokens.ToArray(), source);
            return true;
        }

        private static bool IsFlowControl(string name)
        {
            switch (name)
            {
                // 선형성을 깨는 것만 경고한다. set/declare는 무대 상태와 무관하고
                // 분기하지 않으므로 그냥 흘러가 리듀서의 Unhandled로 남는다.
                case "if":
                case "elseif":
                case "else":
                case "endif":
                case "jump":
                case "stop":
                    return true;
                default:
                    return false;
            }
        }

        private static List<string> SplitRespectingQuotes(string body)
        {
            List<string> tokens = new List<string>();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            bool inQuotes = false;

            foreach (char c in body)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes && char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0)
                    {
                        tokens.Add(sb.ToString());
                        sb.Clear();
                    }

                    continue;
                }

                sb.Append(c);
            }

            if (sb.Length > 0)
                tokens.Add(sb.ToString());

            return tokens;
        }

        private static string StripComment(string line)
        {
            int index = line.IndexOf("//", StringComparison.Ordinal);
            return index < 0 ? line : line.Substring(0, index);
        }

        private static string StripTags(string line, out string lineId)
        {
            lineId = null;

            int hash = line.IndexOf('#');

            if (hash < 0)
                return line.Trim();

            string tags = line.Substring(hash);
            string text = line.Substring(0, hash).Trim();

            foreach (string tag in tags.Split('#'))
            {
                string t = tag.Trim();

                // Yarn 런타임의 라인 ID는 "line:" 접두사를 포함한 전체다
                // (#line:abc 태그 → ID "line:abc"). 접두사를 벗기면 조회가 빗나간다.
                if (t.StartsWith("line:", StringComparison.Ordinal))
                    lineId = t;
            }

            return text;
        }
    }
}
