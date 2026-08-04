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

                // 조건은 실제 Yarn 문법으로 쓴다 (X11). ScenarioOnly가 편집·붙여넣기
                // 입력면이 되므로(X12) 표기 문법과 파싱 문법이 같아야 왕복이 성립한다.
                // 조립은 YarnSyntax 재사용 — Preview·이미터와 같은 규칙 하나다.
                case RenderedSegmentKind.ConditionBegin:
                    YarnSyntax.AppendCondition(builder, "if", segment.Expression);
                    builder.Append('\n');
                    break;

                case RenderedSegmentKind.ConditionElseIf:
                    YarnSyntax.AppendCondition(builder, "elseif", segment.Expression);
                    builder.Append('\n');
                    break;

                case RenderedSegmentKind.ConditionEnd:
                    builder.Append("<<endif>>\n");
                    break;

                case RenderedSegmentKind.DialogueLine:
                    AppendDialogue(builder, document.Options, segment, lineIdPrefix: null);
                    break;

                case RenderedSegmentKind.ChoiceOption:
                    builder.Append("-> ").Append(segment.Text ?? string.Empty);

                    if (segment.Tags is { Count: > 0 })
                    {
                        builder.Append(" (").Append(string.Join(' ', segment.Tags)).Append(')');
                    }

                    builder.Append('\n');
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

        // 옵션 라벨도 번역 대상이다. 버튼 문구가 원문 그대로 남으면 번역본에서 빠진다.
        foreach (RenderedSegment segment in document.Segments.Where(item =>
                     item.Kind is RenderedSegmentKind.DialogueLine or RenderedSegmentKind.ChoiceOption))
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
