using Vn.Authoring.Definition;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Rendering;

/// <summary>
/// 호환되지 않는 결과 조합으로 정식 문서를 만들려 했을 때.
/// </summary>
public sealed class IncompatibleCompositionException : InvalidOperationException
{
    public IncompatibleCompositionException(ResolvedComposition composition)
        : base("호환되지 않는 결과 조합으로는 정식 출력을 만들지 않습니다. " +
            composition.ProblemSummary())
    {
        Composition = composition;
    }

    public ResolvedComposition Composition { get; }
}

/// <summary>
/// 발행된 <see cref="DialogueResult"/>와 (있다면) <see cref="PresentationResult"/>를
/// 출력 옵션에 맞는 평평한 Segment 목록으로 합성한다.
///
/// <b>여기가 정식 출력의 유일한 입구다.</b> Runtime Full, 대본집, 녹음 대본, 번역표,
/// 연출 지시서가 모두 같은 Segment 목록에서 나온다. 형식마다 합성기를 따로 두면
/// 같은 원고에서 나온 문서들이 조금씩 다른 이야기를 하게 된다.
///
/// 이 클래스는 조건 의미를 다시 계산하지 않는다. 결과가 이미 얼어붙은 구조를 들고 있다.
/// 문자열 문법도 모른다. 실제 표기는 Formatter가 맡는다.
/// </summary>
public static class ResultDocumentComposer
{
    /// <summary>조합 하나를 합성한다. 호환되지 않으면 문서를 만들지 않고 거부한다.</summary>
    public static RenderedDocument Compose(
        ResolvedComposition composition,
        StoryProject? project = null,
        GameDefinition? definition = null,
        DocumentOutputOptions? options = null,
        OutputPresetId? presetId = null,
        ILocalizedLineProvider? localization = null)
    {
        ArgumentNullException.ThrowIfNull(composition);

        if (!composition.IsCompatible)
        {
            throw new IncompatibleCompositionException(composition);
        }

        return Compose(
            composition.Dialogue!,
            composition.Presentation,
            project,
            definition,
            options,
            presetId,
            localization);
    }

    public static RenderedDocument ComposePreset(
        ResolvedComposition composition,
        OutputPreset preset,
        StoryProject? project = null,
        GameDefinition? definition = null,
        ILocalizedLineProvider? localization = null)
    {
        ArgumentNullException.ThrowIfNull(preset);

        return Compose(composition, project, definition, preset.Options, preset.Id, localization);
    }

    /// <summary>
    /// 결과 두 개를 직접 합성한다. 호환성 검사는 이미 끝났다고 본다.
    /// 작업 중 미리 보기도 이 경로를 지난다. 미리 보기와 정식 출력이 다른 코드로 만들어지면
    /// 화면에서 본 것과 발행된 것의 차이를 아무도 찾지 못한다.
    /// </summary>
    public static RenderedDocument Compose(
        DialogueResult dialogue,
        PresentationResult? presentation = null,
        StoryProject? project = null,
        GameDefinition? definition = null,
        DocumentOutputOptions? options = null,
        OutputPresetId? presetId = null,
        ILocalizedLineProvider? localization = null)
    {
        ArgumentNullException.ThrowIfNull(dialogue);

        options ??= OutputPresetCatalog.RuntimeFull.Options;
        PresentationCommandCatalog catalog = PresentationCommandCatalog.For(definition);

        var segments = new List<RenderedSegment>();
        var nodeSource = new RenderSourceReference(
            NodeId: dialogue.SourceNodeId,
            DialogueResultId: dialogue.Identity.ResultId);

        if (options.IncludeStructure)
        {
            segments.Add(new RenderedSegment(
                Id: $"node:{dialogue.SourceNodeId}:header",
                Kind: RenderedSegmentKind.NodeHeader,
                Layer: DocumentLayer.Structure,
                Source: nodeSource,
                Text: dialogue.SourceNodeName));
        }

        if (options.IncludeSetAssignments)
        {
            for (int index = 0; index < dialogue.Assignments.Count; index++)
            {
                DialogueResultAssignment assignment = dialogue.Assignments[index];

                segments.Add(new RenderedSegment(
                    Id: $"set:{dialogue.Identity.ResultId}:{index}",
                    Kind: RenderedSegmentKind.SetAssignment,
                    Layer: DocumentLayer.SetAssignments,
                    Source: nodeSource,
                    Variable: assignment.Variable,
                    Value: assignment.Value));
            }
        }

        if (options.IncludePresentation && presentation is not null)
        {
            // Setup은 어느 줄에도 속하지 않는 장면 준비다. 이미터에서 Set_ 노드 본문이 되고,
            // Preview에서도 첫 줄보다 앞에 놓는다.
            foreach (PresentationResultCommand command in presentation.SetupCommands)
            {
                AddCommandSegment(
                    segments,
                    dialogue,
                    presentation,
                    lineId: null,
                    command,
                    indentLevel: 0,
                    catalog,
                    options);
            }
        }

        int depth = 0;

        // 갈래 출구는 여는 줄이 소유하지만(§4.2) 실행은 갈래의 끝에서 일어난다.
        // 여는 줄 바로 뒤에 jump를 두면 갈래 본문이 그 아래 묻혀 실행되지 않는다.
        // 그래서 다음 전환(elseif/endif)이 갈래를 닫는 순간에 내보낸다.
        (RenderSourceReference Source, string Target)? pendingBranchJump = null;

        foreach (DialogueResultLine line in dialogue.Lines)
        {
            var lineSource = new RenderSourceReference(
                NodeId: dialogue.SourceNodeId,
                LineId: line.LineId,
                ConditionId: line.Transition?.ConditionId,
                DialogueResultId: dialogue.Identity.ResultId);

            if (line.Transition is { } transition)
            {
                if (options.IncludeExecutionJumps && pendingBranchJump is { } pending)
                {
                    AddBranchJump(segments, project, pending.Source, pending.Target);
                }

                pendingBranchJump = null;
                depth = transition.Kind == ConditionTransitionKind.EndIf ? 0 : 1;

                if (options.IncludeConditions)
                {
                    AddTransition(segments, line, transition, lineSource);
                }
            }

            int indent = options.IncludeConditions ? depth : 0;

            if (options.IncludeSetAssignments)
            {
                for (int index = 0; index < line.Sets.Count; index++)
                {
                    DialogueResultSetOperation operation = line.Sets[index];

                    segments.Add(new RenderedSegment(
                        Id: $"set:{line.LineId}:{index}",
                        Kind: RenderedSegmentKind.SetAssignment,
                        Layer: DocumentLayer.SetAssignments,
                        Source: lineSource,
                        IndentLevel: indent,
                        Variable: operation.Variable,
                        Operator: SetOperators.Symbol(operation.Operator),
                        Value: operation.Value));
                }
            }

            if (options.IncludePresentation && presentation is not null)
            {
                AddPresentationCommands(
                    segments,
                    dialogue,
                    presentation,
                    line,
                    indent,
                    catalog,
                    options);
            }

            if (options.IncludeDialogueText ||
                options.IncludeLocalizedDialogue ||
                options.IncludeSpeaker ||
                options.IncludeLineId)
            {
                segments.Add(new RenderedSegment(
                    Id: $"line:{line.LineId}",
                    Kind: RenderedSegmentKind.DialogueLine,
                    Layer: DocumentLayer.Dialogue,
                    Source: lineSource,
                    IndentLevel: indent,
                    Text: options.IncludeDialogueText ? line.Text : null,
                    LocalizedText: options.IncludeLocalizedDialogue
                        ? localization?.GetLocalizedText(line.LineId)
                        : null,
                    Speaker: options.IncludeSpeaker ? line.CharacterName : null));
            }

            if (line.BranchExitTargetNodeId is { } branchTarget)
            {
                // 조건 출구의 소유자는 화면에 보이는 마지막 줄이 아니라 갈래를 여는 LineId다.
                // 결과에서도 같은 규칙을 지켜야 Preview에서 원본으로 돌아갈 수 있다.
                pendingBranchJump = (lineSource, branchTarget);
            }
        }

        if (options.IncludeExecutionJumps && pendingBranchJump is { } lastPending)
        {
            // 구조가 올바르면 endif가 갈래를 닫아 여기 도달하지 않지만,
            // 잘못된 구조라도 출구를 조용히 버리지는 않는다.
            AddBranchJump(segments, project, lastPending.Source, lastPending.Target);
        }

        if (options.IncludeExecutionJumps && dialogue.DefaultExitTargetNodeId is { } defaultTarget)
        {
            segments.Add(new RenderedSegment(
                Id: $"node:{dialogue.SourceNodeId}:default-jump",
                Kind: RenderedSegmentKind.DefaultJump,
                Layer: DocumentLayer.ExecutionJumps,
                Source: nodeSource,
                TargetNodeId: defaultTarget,
                TargetNodeName: project?.FindNode(defaultTarget)?.Name));
        }

        if (options.IncludeDiagnostics && presentation is not null)
        {
            AddOrphanWarnings(segments, dialogue, presentation);
        }

        if (options.IncludeStructure)
        {
            segments.Add(new RenderedSegment(
                Id: $"node:{dialogue.SourceNodeId}:footer",
                Kind: RenderedSegmentKind.NodeFooter,
                Layer: DocumentLayer.Structure,
                Source: nodeSource));
        }

        return new RenderedDocument(
            dialogue.SourceNodeId,
            dialogue.Identity,
            presentation?.Identity,
            segments,
            options,
            presetId);
    }

    private static void AddBranchJump(
        List<RenderedSegment> segments,
        StoryProject? project,
        RenderSourceReference source,
        string targetNodeId)
    {
        segments.Add(new RenderedSegment(
            Id: $"branch:{source.LineId}:jump",
            Kind: RenderedSegmentKind.BranchJump,
            Layer: DocumentLayer.ExecutionJumps,
            Source: source,
            IndentLevel: 1,
            TargetNodeId: targetNodeId,
            TargetNodeName: project?.FindNode(targetNodeId)?.Name));
    }

    private static void AddTransition(
        List<RenderedSegment> segments,
        DialogueResultLine line,
        DialogueResultTransition transition,
        RenderSourceReference source)
    {
        if (transition.Kind == ConditionTransitionKind.EndIf)
        {
            segments.Add(new RenderedSegment(
                Id: $"condition:{line.LineId}:end",
                Kind: RenderedSegmentKind.ConditionEnd,
                Layer: DocumentLayer.Conditions,
                Source: source));
            return;
        }

        string expression = transition.Expression is { Length: > 0 } value
            ? value
            : transition.ConditionId ?? string.Empty;

        segments.Add(new RenderedSegment(
            Id: $"condition:{line.LineId}:open",
            Kind: transition.Kind == ConditionTransitionKind.BeginIf
                ? RenderedSegmentKind.ConditionBegin
                : RenderedSegmentKind.ConditionElseIf,
            Layer: DocumentLayer.Conditions,
            Source: source,
            Text: transition.ConditionName,
            Expression: expression));
    }

    private static void AddPresentationCommands(
        List<RenderedSegment> segments,
        DialogueResult dialogue,
        PresentationResult presentation,
        DialogueResultLine line,
        int indentLevel,
        PresentationCommandCatalog catalog,
        DocumentOutputOptions options)
    {
        if (presentation.FindBinding(line.LineId) is not { IsOrphan: false } binding)
        {
            return;
        }

        foreach (PresentationResultCommand command in binding.Commands)
        {
            AddCommandSegment(
                segments,
                dialogue,
                presentation,
                line.LineId,
                command,
                indentLevel,
                catalog,
                options);
        }
    }

    private static void AddCommandSegment(
        List<RenderedSegment> segments,
        DialogueResult dialogue,
        PresentationResult presentation,
        string? lineId,
        PresentationResultCommand command,
        int indentLevel,
        PresentationCommandCatalog catalog,
        DocumentOutputOptions options)
    {
        PresentationCommandDefinition? definition = catalog.Find(command.DefinitionId);

        if (!options.IncludesPresentation(definition?.CategoryId))
        {
            return;
        }

        segments.Add(new RenderedSegment(
            Id: $"presentation:{presentation.Identity.ResultId}:{command.CommandId}",
            Kind: RenderedSegmentKind.PresentationCommand,
            Layer: DocumentLayer.Presentation,
            // 연출 Segment도 대사 결과 Id를 함께 들고 있다. 프리셋 필터로 대사가 빠져도
            // 이 명령이 어느 결과의 어느 줄에 붙는 것인지 잃지 않아야 한다.
            // Setup 커맨드는 어느 줄의 것도 아니므로 LineId가 없다.
            Source: new RenderSourceReference(
                NodeId: presentation.SourceNodeId,
                LineId: lineId,
                DialogueResultId: dialogue.Identity.ResultId,
                PresentationResultId: presentation.Identity.ResultId,
                PresentationNodeId: presentation.SourceNodeId,
                PresentationCommandId: command.CommandId),
            IndentLevel: indentLevel,
            Text: definition?.DisplayName,
            DefinitionId: command.DefinitionId,
            CommandName: definition?.OutputCommandName ?? command.DefinitionId,
            PresentationCategoryId: definition?.CategoryId,
            PresentationCategoryName: catalog.FindCategory(definition?.CategoryId)?.DisplayName,
            Arguments: ResolveArguments(definition, command)));
    }

    /// <summary>
    /// 카탈로그의 파라미터 순서대로 인자 값을 해석한다. 작성 값이 없으면 정의의 기본값을 쓰고,
    /// 값이 아예 없는 파라미터부터는 트레일링 생략으로 자른다(뒤쪽부터만 생략 규칙).
    /// 정의를 모르는 명령이나 파라미터 밖의 인자는 버리지 않고 이름순으로 뒤에 붙인다.
    /// </summary>
    private static IReadOnlyList<RenderedArgument> ResolveArguments(
        PresentationCommandDefinition? definition,
        PresentationResultCommand command)
    {
        var arguments = new List<RenderedArgument>();
        var consumed = new HashSet<string>(StringComparer.Ordinal);

        if (definition is not null)
        {
            foreach (PresentationCommandParameter parameter in definition.Parameters)
            {
                string? value = command.Arguments.TryGetValue(parameter.Name, out string? provided)
                    ? provided
                    : parameter.Default;

                if (value is null)
                {
                    break;
                }

                arguments.Add(new RenderedArgument(parameter.Name, value));
                consumed.Add(parameter.Name);
            }
        }

        foreach ((string key, string value) in command.Arguments
                     .Where(pair => !consumed.Contains(pair.Key))
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            arguments.Add(new RenderedArgument(key, value));
        }

        return arguments;
    }

    private static void AddOrphanWarnings(
        List<RenderedSegment> segments,
        DialogueResult dialogue,
        PresentationResult presentation)
    {
        foreach (PresentationResultBinding binding in presentation.Bindings)
        {
            if (dialogue.ContainsLine(binding.LineId))
            {
                continue;
            }

            segments.Add(new RenderedSegment(
                Id: $"warning:{presentation.Identity.ResultId}:{binding.LineId}",
                Kind: RenderedSegmentKind.Warning,
                Layer: DocumentLayer.Diagnostics,
                Source: new RenderSourceReference(
                    LineId: binding.LineId,
                    PresentationResultId: presentation.Identity.ResultId,
                    PresentationNodeId: presentation.SourceNodeId),
                Text: $"LineId '{binding.LineId}'의 연출이 이 대사 결과에 붙지 않습니다."));
        }
    }
}
