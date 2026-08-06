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
        int choiceBlockOrdinal = -1;
        int choiceOptionIndex = -1;
        bool showDialogue = options.IncludeDialogueText ||
            options.IncludeLocalizedDialogue ||
            options.IncludeSpeaker ||
            options.IncludeLineId;

        // 갈래 출구는 여는 줄이 소유하지만(§4.2) 실행은 갈래의 끝에서 일어난다.
        // 여는 줄 바로 뒤에 jump를 두면 갈래 본문이 그 아래 묻혀 실행되지 않는다.
        // 그래서 자기 체인의 다음 전환이 갈래를 닫는 순간에 내보낸다. 조건 갈래의 출구는
        // 안에 열린 선택 블록(W54)의 전환에는 흘러나오지 않는다 — 소유 체인이 다르다.
        (RenderSourceReference Source, string Target, bool IsChoice)? pendingBranchJump = null;

        // W54: 조건 갈래 안 선택 블록 — 들여쓰기는 (조건 열림 ? 1 : 0) + (옵션 본문 ? 1 : 0).
        bool conditionOpen = false;

        foreach (DialogueResultLine line in dialogue.Lines)
        {
            var lineSource = new RenderSourceReference(
                NodeId: dialogue.SourceNodeId,
                LineId: line.LineId,
                ConditionId: line.Transition?.ConditionId,
                DialogueResultId: dialogue.Identity.ResultId);

            bool isOptionLabel = false;

            if (line.Transition is { } transition)
            {
                bool isChoiceTransition = transition.Kind is ConditionTransitionKind.BeginChoice
                    or ConditionTransitionKind.BeginNextOption
                    or ConditionTransitionKind.EndChoice;

                // 자기 체인의 전환만 대기 중인 출구를 흘려보낸다 (W54) — 조건 갈래의 출구가
                // 안의 선택 전환에서 새어 나오면 선택지가 제시되기 전에 점프해 버린다.
                if (options.IncludeExecutionJumps && pendingBranchJump is { } pending &&
                    pending.IsChoice == isChoiceTransition)
                {
                    AddBranchJump(segments, project, pending.Source, pending.Target);
                    pendingBranchJump = null;
                }
                else if (!isChoiceTransition)
                {
                    // 조건 전환은 어떤 대기든 닫는다(깨진 구조의 안전망 — 잃지 않고 낸다).
                    if (options.IncludeExecutionJumps && pendingBranchJump is { } stale)
                    {
                        AddBranchJump(segments, project, stale.Source, stale.Target);
                    }

                    pendingBranchJump = null;
                }

                switch (transition.Kind)
                {
                    case ConditionTransitionKind.BeginChoice:
                    case ConditionTransitionKind.BeginNextOption:
                        isOptionLabel = true;
                        depth = conditionOpen ? 2 : 1; // 옵션 본문 깊이

                        if (transition.Kind == ConditionTransitionKind.BeginChoice)
                        {
                            choiceBlockOrdinal++;
                            choiceOptionIndex = 0;
                        }
                        else
                        {
                            choiceOptionIndex++;
                        }

                        if (showDialogue)
                        {
                            // 라벨 라인은 일반 대사가 아니라 버튼이다. 본문 줄 수를 세는
                            // 규칙(계약서 B)에서 빠지도록 별도 종류로 낸다.
                            segments.Add(new RenderedSegment(
                                Id: $"choice:{line.LineId}",
                                Kind: RenderedSegmentKind.ChoiceOption,
                                Layer: DocumentLayer.Dialogue,
                                Source: lineSource,
                                IndentLevel: conditionOpen ? 1 : 0, // 라벨은 감싼 조건 깊이 (W54)
                                Text: options.IncludeDialogueText ? line.Text : null,
                                LocalizedText: options.IncludeLocalizedDialogue
                                    ? localization?.GetLocalizedText(line.LineId)
                                    : null,
                                Speaker: options.IncludeSpeaker ? line.CharacterName : null,
                                OptionId: transition.OptionId,
                                ChoiceBlockOrdinal: choiceBlockOrdinal,
                                ChoiceOptionIndex: choiceOptionIndex,
                                Tags: BuildEffectTags(line.Sets)));
                        }

                        break;

                    case ConditionTransitionKind.EndChoice:
                        depth = conditionOpen ? 1 : 0; // 조건 안이었다면 그 갈래로 돌아간다 (W54)

                        if (showDialogue)
                        {
                            segments.Add(new RenderedSegment(
                                Id: $"choice:{line.LineId}:end",
                                Kind: RenderedSegmentKind.ChoiceEnd,
                                Layer: DocumentLayer.Dialogue,
                                Source: lineSource,
                                ChoiceBlockOrdinal: choiceBlockOrdinal));
                        }

                        break;

                    default:
                        conditionOpen = transition.Kind is not ConditionTransitionKind.EndIf;
                        depth = conditionOpen ? 1 : 0;

                        if (options.IncludeConditions)
                        {
                            AddTransition(segments, line, transition, lineSource);
                        }

                        break;
                }
            }

            int indent = options.IncludeConditions || isOptionLabel ? depth : 0;

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
                if (isOptionLabel)
                {
                    // 옵션 라벨은 메인 레인에서 advance를 소비하지 않으므로(계약서 B)
                    // 서브 레인에 짝지을 자리가 없다. 붙은 연출은 조용히 버리지 않고 알린다.
                    if (options.IncludeDiagnostics &&
                        presentation.FindBinding(line.LineId) is { IsOrphan: false, Commands.Count: > 0 })
                    {
                        segments.Add(new RenderedSegment(
                            Id: $"warning:{presentation.Identity.ResultId}:{line.LineId}:label",
                            Kind: RenderedSegmentKind.Warning,
                            Layer: DocumentLayer.Diagnostics,
                            Source: lineSource,
                            Text: $"옵션 라벨 라인 '{line.LineId}'의 연출은 서브 레인에 짝지을 자리가 없어 출력에서 빠집니다."));
                    }
                }
                else
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
            }

            if (showDialogue && !isOptionLabel)
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
                // 갈래 출구의 소유자는 화면에 보이는 마지막 줄이 아니라 갈래를 여는 LineId다.
                // 결과에서도 같은 규칙을 지켜야 Preview에서 원본으로 돌아갈 수 있다.
                pendingBranchJump = (
                    lineSource,
                    branchTarget,
                    line.Transition?.Kind is ConditionTransitionKind.BeginChoice
                        or ConditionTransitionKind.BeginNextOption);
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

    /// <summary>
    /// 옵션 라벨의 표시용 스탯 미리보기 태그 (계약서 D5). 실제 효과가 아니라 표시 전용이다.
    /// 정수 증감(+=/-=)만 태그가 되고, 대입(=)과 비정수는 만들지 않는다(런타임 파서가 버린다).
    /// 키는 소문자 — 런타임 누적 표시 조회가 소문자 키로 일어난다.
    /// </summary>
    private static IReadOnlyList<string>? BuildEffectTags(
        IReadOnlyList<DialogueResultSetOperation> sets)
    {
        List<string>? tags = null;

        foreach (DialogueResultSetOperation operation in sets)
        {
            if (operation.Operator == SetOperatorKind.Assign ||
                !int.TryParse(
                    operation.Value.Trim(),
                    System.Globalization.NumberStyles.AllowLeadingSign,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int value))
            {
                continue;
            }

            int signed = operation.Operator == SetOperatorKind.Add ? value : -value;
            tags ??= new List<string>();
            tags.Add($"#{operation.Variable.ToLowerInvariant()}:{(signed < 0 ? "-" : "+")}{Math.Abs(signed)}");
        }

        return tags;
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
            Arguments: ResolveArguments(definition, command),
            Note: command.Note));
    }

    /// <summary>인자 순서·트레일링 생략 규칙은 <see cref="CommandText.ResolveOrdered"/> 하나다.</summary>
    private static IReadOnlyList<RenderedArgument> ResolveArguments(
        PresentationCommandDefinition? definition,
        PresentationResultCommand command)
    {
        return CommandText.ResolveOrdered(definition, command.Arguments)
            .Select(argument => new RenderedArgument(argument.Name, argument.Value))
            .ToArray();
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
