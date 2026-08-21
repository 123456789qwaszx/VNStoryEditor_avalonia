using System.Text;

namespace Vn.Authoring.Rendering;

/// <summary>
/// 중립적인 <see cref="RenderedSegment"/> 목록을 Yarn 저작 문법과 닮은 읽기 전용 문자열로 만든다.
///
/// 이 결과는 Preview일 뿐 저장 원본이나 역파싱 입력이 아니다. 노드 title과 jump에는 이름이
/// 아니라 안정된 NodeId를 사용하여 같은 이름의 노드가 있어도 참조가 모호해지지 않게 한다.
/// 실제 런타임 파일은 <see cref="YarnBundleEmitter"/>가 만들되, 표기 조립은
/// <see cref="YarnSyntax"/>를 함께 쓴다.
/// </summary>
public static class YarnPreviewFormatter
{
    public static string Format(RenderedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new StringBuilder();

        foreach (RenderedSegment segment in document.Segments)
        {
            AppendSegment(builder, document, segment);
        }

        return builder.ToString();
    }

    private static void AppendSegment(StringBuilder builder, RenderedDocument document, RenderedSegment segment)
    {
        string indent = YarnSyntax.IndentOf(segment.IndentLevel);

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
                builder.Append(indent);
                YarnSyntax.AppendSet(builder, segment);
                builder.Append('\n');
                break;

            case RenderedSegmentKind.ConditionBegin:
                builder.Append(indent);
                YarnSyntax.AppendCondition(builder, "if", segment.Expression);
                builder.Append('\n');
                break;

            case RenderedSegmentKind.ConditionElseIf:
                builder.Append(indent);
                YarnSyntax.AppendCondition(builder, "elseif", segment.Expression);
                builder.Append('\n');
                break;

            case RenderedSegmentKind.ConditionEnd:
                builder.Append(indent).Append("<<endif>>\n");
                break;

            case RenderedSegmentKind.PresentationCommand:
                builder.Append(indent);
                YarnSyntax.AppendCommand(builder, segment);
                builder.Append('\n');
                break;

            case RenderedSegmentKind.DialogueLine:
                builder.Append(indent);
                YarnSyntax.AppendDialogue(builder, segment);

                if (document.Options.IncludeLineId && segment.Source.LineId is { Length: > 0 } lineId)
                {
                    builder.Append(" #line:").Append(lineId);
                }

                builder.Append('\n');
                break;

            case RenderedSegmentKind.ChoiceOption:
                // 라벨은 접두 없이 순수 텍스트다 (계약서 D6 결정). 식별은 내부 OptionId로만 한다.
                builder.Append(indent).Append("-> ").Append(segment.Text ?? string.Empty);

                foreach (string tag in segment.Tags ?? Array.Empty<string>())
                {
                    builder.Append(' ').Append(tag);
                }

                if (document.Options.IncludeLineId && segment.Source.LineId is { Length: > 0 } optionLineId)
                {
                    builder.Append(" #line:").Append(optionLineId);
                }

                builder.Append('\n');
                break;

            case RenderedSegmentKind.ChoiceEnd:
                break;

            case RenderedSegmentKind.BranchJump:
            case RenderedSegmentKind.DefaultJump:
                builder.Append(indent);
                YarnSyntax.AppendJump(builder, segment.TargetNodeId ?? "missing-target");
                builder.Append('\n');
                break;

            case RenderedSegmentKind.BranchDetour:
                builder.Append(indent);
                YarnSyntax.AppendDetour(builder, segment.TargetNodeId ?? "missing-target");
                builder.Append('\n');
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
}
