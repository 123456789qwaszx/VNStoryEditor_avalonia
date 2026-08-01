using System.Text;

namespace Vn.Authoring.Rendering;

/// <summary>
/// 중립적인 <see cref="RenderedSegment"/> 목록을 Yarn 저작 문법과 닮은 읽기 전용 문자열로 만든다.
///
/// 이 결과는 Preview일 뿐 저장 원본이나 역파싱 입력이 아니다. 노드 title과 jump에는 이름이
/// 아니라 안정된 NodeId를 사용하여 같은 이름의 노드가 있어도 참조가 모호해지지 않게 한다.
/// </summary>
public static class YarnPreviewFormatter
{
    private const string Indent = "    ";

    public static string Format(RenderedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new StringBuilder();

        foreach (RenderedSegment segment in document.Segments)
        {
            AppendSegment(builder, segment);
        }

        return builder.ToString();
    }

    private static void AppendSegment(StringBuilder builder, RenderedSegment segment)
    {
        string indent = string.Concat(Enumerable.Repeat(Indent, Math.Max(0, segment.IndentLevel)));

        switch (segment.Kind)
        {
            case RenderedSegmentKind.NodeHeader:
                builder.Append("title: ")
                    .Append(segment.Source.NodeId ?? "unknown")
                    .Append('\n');

                if (!string.IsNullOrWhiteSpace(segment.Text))
                {
                    builder.Append("// name: ")
                        .Append(segment.Text)
                        .Append('\n');
                }

                builder.Append("---\n");
                break;

            case RenderedSegmentKind.SetAssignment:
                builder.Append(indent)
                    .Append("<<set ")
                    .Append(NormalizeVariable(segment.Variable))
                    .Append(" = ")
                    .Append(segment.Value ?? string.Empty)
                    .Append(">>\n");
                break;

            case RenderedSegmentKind.ConditionBegin:
                AppendCondition(builder, indent, "if", segment.Expression);
                break;

            case RenderedSegmentKind.ConditionElseIf:
                AppendCondition(builder, indent, "elseif", segment.Expression);
                break;

            case RenderedSegmentKind.ConditionEnd:
                builder.Append(indent).Append("<<endif>>\n");
                break;

            case RenderedSegmentKind.PresentationCommand:
                builder.Append(indent)
                    .Append("<<")
                    .Append(string.IsNullOrWhiteSpace(segment.CommandName)
                        ? segment.DefinitionId ?? "presentation"
                        : segment.CommandName);

                foreach ((string key, string value) in (segment.Arguments ??
                             new Dictionary<string, string>()).OrderBy(
                                 pair => pair.Key,
                                 StringComparer.Ordinal))
                {
                    builder.Append(' ')
                        .Append(key)
                        .Append('=')
                        .Append(value);
                }

                builder.Append(">>\n");
                break;

            case RenderedSegmentKind.DialogueLine:
                builder.Append(indent);

                if (!string.IsNullOrWhiteSpace(segment.Speaker))
                {
                    builder.Append(segment.Speaker).Append(": ");
                }

                builder.Append(segment.Text ?? string.Empty);

                if (segment.Source.LineId is { Length: > 0 } lineId)
                {
                    builder.Append(" #line:").Append(lineId);
                }

                builder.Append('\n');
                break;

            case RenderedSegmentKind.BranchJump:
            case RenderedSegmentKind.DefaultJump:
                builder.Append(indent)
                    .Append("<<jump ")
                    .Append(segment.TargetNodeId ?? "missing-target")
                    .Append(">>\n");
                break;

            case RenderedSegmentKind.Warning:
                builder.Append(indent)
                    .Append("// WARNING: ")
                    .Append(segment.Text ?? string.Empty)
                    .Append('\n');
                break;

            case RenderedSegmentKind.NodeFooter:
                builder.Append("===\n");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(segment), segment.Kind, "알 수 없는 문서 Segment 종류입니다.");
        }
    }

    private static void AppendCondition(
        StringBuilder builder,
        string indent,
        string keyword,
        string? expression)
    {
        builder.Append(indent)
            .Append("<<")
            .Append(keyword)
            .Append(' ')
            .Append(string.IsNullOrWhiteSpace(expression) ? "false" : expression)
            .Append(">>\n");
    }

    private static string NormalizeVariable(string? variable)
    {
        string value = variable?.Trim() ?? string.Empty;

        if (value.StartsWith('$'))
        {
            return value;
        }

        return "$" + value;
    }
}
