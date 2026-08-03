using System.Text;

namespace Vn.Authoring.Rendering;

/// <summary>
/// 프리셋의 목적에 맞는 읽기 전용 문자열 표현을 선택한다.
/// Segment 목록과 원본 매핑은 그대로 두고, 사람이 보는 표기만 바꾼다.
/// </summary>
public static class DocumentPreviewFormatter
{
    public static string Format(RenderedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.Options.Format switch
        {
            DocumentOutputFormat.YarnRuntime => YarnPreviewFormatter.Format(document),
            DocumentOutputFormat.Scenario => FormatScenario(document),
            DocumentOutputFormat.Recording => FormatRecording(document),
            DocumentOutputFormat.Localization => FormatLocalization(document),
            DocumentOutputFormat.Direction => FormatDirection(document),
            _ => throw new ArgumentOutOfRangeException(
                nameof(document),
                document.Options.Format,
                "알 수 없는 문서 출력 형식입니다.")
        };
    }

    private static string FormatScenario(RenderedDocument document)
    {
        var builder = new StringBuilder();

        foreach (RenderedSegment segment in document.Segments)
        {
            switch (segment.Kind)
            {
                case RenderedSegmentKind.NodeHeader:
                    builder.Append("[장면] ")
                        .Append(string.IsNullOrWhiteSpace(segment.Text)
                            ? segment.Source.NodeId
                            : segment.Text)
                        .Append('\n');
                    break;

                case RenderedSegmentKind.ConditionBegin:
                    AppendConditionLabel(builder, "조건", segment);
                    break;

                case RenderedSegmentKind.ConditionElseIf:
                    AppendConditionLabel(builder, "다른 조건", segment);
                    break;

                case RenderedSegmentKind.ConditionEnd:
                    builder.Append("[조건 끝]\n");
                    break;

                case RenderedSegmentKind.DialogueLine:
                    AppendDialogue(builder, document.Options, segment, lineIdPrefix: null);
                    break;

                case RenderedSegmentKind.Warning:
                    builder.Append("[주의] ").Append(segment.Text).Append('\n');
                    break;

                case RenderedSegmentKind.NodeFooter:
                    break;
            }
        }

        return builder.ToString();
    }

    private static string FormatRecording(RenderedDocument document)
    {
        var builder = new StringBuilder();

        foreach (RenderedSegment segment in document.Segments)
        {
            switch (segment.Kind)
            {
                case RenderedSegmentKind.PresentationCommand:
                    builder.Append("    [연기] ")
                        .Append(CommandDisplay(segment))
                        .Append('\n');
                    break;

                case RenderedSegmentKind.DialogueLine:
                    AppendDialogue(builder, document.Options, segment, lineIdPrefix: "LINE");
                    break;
            }
        }

        return builder.ToString();
    }

    private static string FormatLocalization(RenderedDocument document)
    {
        var builder = new StringBuilder();
        builder.Append("LineId\tSpeaker\tSourceText\tLocalizedText\n");

        foreach (RenderedSegment segment in document.Segments.Where(item =>
                     item.Kind == RenderedSegmentKind.DialogueLine))
        {
            builder.Append(EscapeTabular(segment.Source.LineId))
                .Append('\t')
                .Append(EscapeTabular(segment.Speaker))
                .Append('\t')
                .Append(EscapeTabular(segment.Text))
                .Append('\t')
                .Append(EscapeTabular(segment.LocalizedText))
                .Append('\n');
        }

        return builder.ToString();
    }

    private static string FormatDirection(RenderedDocument document)
    {
        var builder = new StringBuilder();

        foreach (RenderedSegment segment in document.Segments)
        {
            switch (segment.Kind)
            {
                case RenderedSegmentKind.PresentationCommand:
                    builder.Append('[')
                        .Append(CategoryLabel(segment))
                        .Append("] ")
                        .Append(CommandDisplay(segment))
                        .Append('\n');
                    break;

                case RenderedSegmentKind.DialogueLine:
                    AppendDialogue(builder, document.Options, segment, lineIdPrefix: "LINE");
                    builder.Append('\n');
                    break;
            }
        }

        return builder.ToString();
    }

    private static void AppendConditionLabel(
        StringBuilder builder,
        string prefix,
        RenderedSegment segment)
    {
        string description = !string.IsNullOrWhiteSpace(segment.Text)
            ? segment.Text
            : segment.Expression ?? segment.Source.ConditionId ?? "알 수 없는 조건";

        builder.Append('[')
            .Append(prefix)
            .Append(": ")
            .Append(description)
            .Append("]\n");
    }

    private static void AppendDialogue(
        StringBuilder builder,
        DocumentOutputOptions options,
        RenderedSegment segment,
        string? lineIdPrefix)
    {
        if (options.IncludeLineId && segment.Source.LineId is { Length: > 0 } lineId)
        {
            if (lineIdPrefix is null)
            {
                builder.Append('[').Append(lineId).Append("] ");
            }
            else
            {
                builder.Append('[')
                    .Append(lineIdPrefix)
                    .Append(' ')
                    .Append(lineId)
                    .Append("] ");
            }
        }

        if (options.IncludeSpeaker && !string.IsNullOrWhiteSpace(segment.Speaker))
        {
            builder.Append(segment.Speaker).Append(": ");
        }

        if (options.IncludeDialogueText)
        {
            builder.Append(segment.Text ?? string.Empty);
        }

        builder.Append('\n');
    }

    private static string CommandDisplay(RenderedSegment segment)
    {
        var builder = new StringBuilder();
        builder.Append(string.IsNullOrWhiteSpace(segment.Text)
            ? segment.DefinitionId ?? segment.CommandName ?? "연출"
            : segment.Text);

        if (segment.Arguments is { Count: > 0 })
        {
            builder.Append(" (")
                .Append(string.Join(
                    ", ",
                    segment.Arguments.Select(argument => $"{argument.Name}={argument.Value}")))
                .Append(')');
        }

        return builder.ToString();
    }

    private static string CategoryLabel(RenderedSegment segment)
    {
        // 범주 어휘는 게임 정의가 공급한다. 표시 이름 → Id → 중립 라벨 순서로 고른다.
        return segment.PresentationCategoryName
            ?? segment.PresentationCategoryId
            ?? "Presentation";
    }

    private static string EscapeTabular(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
    }
}
