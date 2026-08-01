using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;

namespace Vn.Authoring.Rendering;

/// <summary>
/// 구조화된 DialogueNode와 연결된 SetNode를 평평한 Segment 목록으로 합성한다.
///
/// 조건 의미는 직접 다시 계산하지 않고 <see cref="ConditionFlowResolver"/> 결과를 사용한다.
/// 이 클래스는 문자열 문법을 모르며, Yarn 표기는 <see cref="YarnPreviewFormatter"/>가 맡는다.
/// </summary>
public static class DialogueDocumentComposer
{
    public static RenderedDocument Compose(
        StoryProject project,
        string dialogueNodeId,
        GameDefinition? definition = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        DialogueNode dialogue = project.FindDialogue(dialogueNodeId)
            ?? throw new InvalidOperationException($"DialogueNode '{dialogueNodeId}'를 찾을 수 없습니다.");
        StoryFile dialogueFile = project.FindFileContainingNode(dialogue.Id)
            ?? throw new InvalidOperationException($"DialogueNode '{dialogue.Id}'의 StoryFile을 찾을 수 없습니다.");

        definition ??= GameDefinition.Empty;

        var segments = new List<RenderedSegment>();
        RenderSourceReference dialogueSource = SourceFor(dialogueFile, dialogue);

        segments.Add(new RenderedSegment(
            Id: $"node:{dialogue.Id}:header",
            Kind: RenderedSegmentKind.NodeHeader,
            Layer: DocumentLayer.Structure,
            Source: dialogueSource,
            Text: dialogue.Name));

        AddConnectedSetAssignments(project, dialogue, segments);

        DialogueFlow flow = ConditionFlowResolver.Resolve(dialogue, project, definition);
        AddProblems(flow, dialogueFile, dialogue, segments);

        AvailableConditionCatalog available = AvailableConditionResolver.Resolve(
            project,
            dialogue.Id,
            definition);

        Dictionary<int, List<ConditionBranch>> exitsByLastLine = flow.Branches
            .Where(branch => branch.HasExit)
            .GroupBy(branch => branch.LastLineIndex)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(branch => branch.PaletteIndex).ToList());

        foreach (ResolvedLine resolved in flow.Lines)
        {
            AddTransition(
                project,
                definition,
                available,
                dialogueFile,
                dialogue,
                resolved.Line,
                resolved.Line.Transition,
                segments);

            segments.Add(new RenderedSegment(
                Id: $"line:{resolved.Line.Id}",
                Kind: RenderedSegmentKind.DialogueLine,
                Layer: DocumentLayer.Dialogue,
                Source: SourceFor(dialogueFile, dialogue, resolved.Line.Id),
                IndentLevel: resolved.Depth,
                Text: resolved.Line.Text,
                Speaker: resolved.Line.Speaker));

            if (exitsByLastLine.TryGetValue(resolved.Index, out List<ConditionBranch>? exits) &&
                exits is not null)
            {
                foreach (ConditionBranch branch in exits)
                {
                    AddBranchJump(project, dialogueFile, dialogue, branch, segments);
                }
            }
        }

        if (dialogue.DefaultExitTargetNodeId is { } defaultTargetId)
        {
            StoryNode? target = project.FindNode(defaultTargetId);
            segments.Add(new RenderedSegment(
                Id: $"node:{dialogue.Id}:default-jump",
                Kind: RenderedSegmentKind.DefaultJump,
                Layer: DocumentLayer.ExecutionJumps,
                Source: dialogueSource,
                TargetNodeId: defaultTargetId,
                TargetNodeName: target?.Name));
        }

        segments.Add(new RenderedSegment(
            Id: $"node:{dialogue.Id}:footer",
            Kind: RenderedSegmentKind.NodeFooter,
            Layer: DocumentLayer.Structure,
            Source: dialogueSource));

        return new RenderedDocument(dialogue.Id, segments);
    }

    private static void AddConnectedSetAssignments(
        StoryProject project,
        DialogueNode dialogue,
        List<RenderedSegment> segments)
    {
        foreach (ConnectedSetNode connected in ConnectedSetNodeResolver.Resolve(project, dialogue.Id))
        {
            StoryFile sourceFile = project.FindFileContainingNode(connected.Node.Id)
                ?? throw new InvalidOperationException(
                    $"SetNode '{connected.Node.Id}'의 StoryFile을 찾을 수 없습니다.");

            for (int index = 0; index < connected.Node.Assignments.Count; index++)
            {
                VariableAssignment assignment = connected.Node.Assignments[index];

                segments.Add(new RenderedSegment(
                    Id: $"set:{connected.Link.Id}:{index}",
                    Kind: RenderedSegmentKind.SetAssignment,
                    Layer: DocumentLayer.SetAssignments,
                    Source: new RenderSourceReference(
                        StoryFileId: sourceFile.Id,
                        NodeId: connected.Node.Id,
                        SetNodeId: connected.Node.Id,
                        LinkId: connected.Link.Id),
                    Variable: assignment.Variable,
                    Value: assignment.Value));
            }
        }
    }

    private static void AddProblems(
        DialogueFlow flow,
        StoryFile dialogueFile,
        DialogueNode dialogue,
        List<RenderedSegment> segments)
    {
        for (int index = 0; index < flow.Problems.Count; index++)
        {
            FlowProblem problem = flow.Problems[index];
            string? conditionId = problem.LineId is null
                ? null
                : dialogue.Lines.FirstOrDefault(line =>
                    string.Equals(line.Id, problem.LineId, StringComparison.Ordinal))
                    ?.Transition?.ConditionId;

            segments.Add(new RenderedSegment(
                Id: $"warning:{dialogue.Id}:{index}",
                Kind: RenderedSegmentKind.Warning,
                Layer: DocumentLayer.Diagnostics,
                Source: SourceFor(dialogueFile, dialogue, problem.LineId, conditionId),
                Text: problem.Message));
        }
    }

    private static void AddTransition(
        StoryProject project,
        GameDefinition definition,
        AvailableConditionCatalog available,
        StoryFile dialogueFile,
        DialogueNode dialogue,
        LineBox line,
        LineConditionTransition? transition,
        List<RenderedSegment> segments)
    {
        if (transition is null)
        {
            return;
        }

        RenderSourceReference source = SourceFor(
            dialogueFile,
            dialogue,
            line.Id,
            transition.ConditionId);

        if (transition.Kind == ConditionTransitionKind.EndIf)
        {
            segments.Add(new RenderedSegment(
                Id: $"condition:{line.Id}:end",
                Kind: RenderedSegmentKind.ConditionEnd,
                Layer: DocumentLayer.Conditions,
                Source: source));
            return;
        }

        string conditionId = transition.ConditionId ?? string.Empty;
        AvailableCondition? condition = available.Find(conditionId)
            ?? AvailableConditionResolver.FindKnown(project, definition, conditionId);
        string expression = condition?.Expression ?? string.Empty;
        if (string.IsNullOrWhiteSpace(expression))
        {
            expression = conditionId;
        }

        segments.Add(new RenderedSegment(
            Id: $"condition:{line.Id}:open",
            Kind: transition.Kind == ConditionTransitionKind.BeginIf
                ? RenderedSegmentKind.ConditionBegin
                : RenderedSegmentKind.ConditionElseIf,
            Layer: DocumentLayer.Conditions,
            Source: source,
            Text: condition?.DisplayName,
            Expression: expression));
    }

    private static void AddBranchJump(
        StoryProject project,
        StoryFile dialogueFile,
        DialogueNode dialogue,
        ConditionBranch branch,
        List<RenderedSegment> segments)
    {
        if (branch.ExitTargetNodeId is not { } targetId)
        {
            return;
        }

        StoryNode? target = project.FindNode(targetId);

        // 조건 출구의 소유자는 화면에 표시되는 마지막 줄이 아니라 갈래를 여는 LineId다.
        // Preview에서 이 Segment를 선택했을 때도 같은 안정된 소유 줄로 돌아가야 한다.
        segments.Add(new RenderedSegment(
            Id: $"branch:{branch.OpenLineId}:jump",
            Kind: RenderedSegmentKind.BranchJump,
            Layer: DocumentLayer.ExecutionJumps,
            Source: SourceFor(dialogueFile, dialogue, branch.OpenLineId, branch.ConditionId),
            IndentLevel: 1,
            TargetNodeId: targetId,
            TargetNodeName: target?.Name));
    }

    private static RenderSourceReference SourceFor(
        StoryFile file,
        DialogueNode dialogue,
        string? lineId = null,
        string? conditionId = null)
    {
        return new RenderSourceReference(
            StoryFileId: file.Id,
            NodeId: dialogue.Id,
            LineId: lineId,
            ConditionId: conditionId);
    }
}
