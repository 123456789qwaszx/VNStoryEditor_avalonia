using System.Text.Json.Nodes;
using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Script;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Results;

public enum PublishProblemKind
{
    /// <summary>읽을 대본이 없다.</summary>
    MissingScript,

    /// <summary>같은 LineId가 결과 안에서 두 번 나온다.</summary>
    DuplicateLineId,

    /// <summary>조건 정의를 찾을 수 없다.</summary>
    UnknownCondition,

    /// <summary>조건 구조가 성립하지 않는다(짝 없는 elseif·endif 등).</summary>
    InvalidConditionStructure,

    /// <summary>출구가 없는 노드를 가리킨다.</summary>
    MissingExitTarget,

    /// <summary>대본에서 사라진 줄에 대사 논리나 연출이 남아 있다. 발행은 막지 않는다.</summary>
    OrphanData,

    /// <summary>연출이 읽을 대사 결과를 아직 고르지 않았다.</summary>
    MissingSourceResult,

    /// <summary>고른 대사 결과를 찾을 수 없거나 내용이 달라졌다.</summary>
    StaleSourceResult
}

/// <param name="IsBlocking">true면 이 문제를 안고 발행할 수 없다.</param>
public sealed record PublishProblem(
    PublishProblemKind Kind,
    string? LineId,
    string Message,
    bool IsBlocking);

/// <summary>
/// 아직 identity가 없는 결과 본문. 검증 결과가 함께 들어 있다.
///
/// 발행 전 미리 보기와 실제 발행이 <b>같은 코드로</b> 만들어지도록 하는 자리이기도 하다.
/// 미리 보기를 따로 합성하면 화면에서 본 것과 발행된 것이 다를 수 있고, 그 차이는
/// 아무도 찾지 못한다.
/// </summary>
public sealed class DialogueDraft
{
    public DialogueDraft(
        string sourceNodeId,
        string sourceNodeName,
        string? sourceScriptId,
        int sourceScriptRevision,
        string locale,
        IReadOnlyList<DialogueResultLine> lines,
        IReadOnlyList<DialogueResultAssignment> assignments,
        string? defaultExitTargetNodeId,
        IReadOnlyList<PublishProblem> problems)
    {
        SourceNodeId = sourceNodeId;
        SourceNodeName = sourceNodeName;
        SourceScriptId = sourceScriptId;
        SourceScriptRevision = sourceScriptRevision;
        Locale = locale;
        Lines = lines;
        Assignments = assignments;
        DefaultExitTargetNodeId = defaultExitTargetNodeId;
        Problems = problems;
    }

    public string SourceNodeId { get; }

    public string SourceNodeName { get; }

    public string? SourceScriptId { get; }

    public int SourceScriptRevision { get; }

    public string Locale { get; }

    public IReadOnlyList<DialogueResultLine> Lines { get; }

    public IReadOnlyList<DialogueResultAssignment> Assignments { get; }

    public string? DefaultExitTargetNodeId { get; }

    public IReadOnlyList<PublishProblem> Problems { get; }

    public bool CanPublish => !Problems.Any(problem => problem.IsBlocking);

    public string BlockingSummary() => string.Join(
        Environment.NewLine,
        Problems.Where(problem => problem.IsBlocking).Select(problem => "• " + problem.Message));
}

/// <summary>
/// DialogueNode의 작업 상태를 불변 결과로 얼린다.
///
/// 얼리는 것은 대사 본문까지 포함한다. 결과가 대본을 참조만 하면 나중에 대본을 고쳤을 때
/// v1이 함께 바뀌고, 그러면 애초에 버전을 매길 이유가 없다.
///
/// 같은 내용을 다시 발행하면 <b>새 버전을 만들지 않고 기존 버전을 돌려준다.</b>
/// 저장 버튼을 두 번 눌렀다는 이유로 v2, v3이 쌓이면 어느 것이 의미 있는 버전인지
/// 알 수 없게 된다. 판정 기준은 내용 해시 하나다.
/// </summary>
public static class DialoguePublisher
{
    public static DialogueDraft Draft(
        StoryProject project,
        string dialogueNodeId,
        GameDefinition? definition = null,
        string? locale = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        DialogueNode node = project.FindDialogue(dialogueNodeId)
            ?? throw new InvalidOperationException($"'{dialogueNodeId}'는 대사 노드가 아닙니다.");

        DialogueScript script = DialogueScriptResolver.Resolve(project, node, locale);
        DialogueFlow flow = ConditionFlowResolver.Resolve(node, script, project, definition);
        AvailableConditionCatalog available = AvailableConditionResolver.Resolve(
            project,
            node.Id,
            definition);

        var problems = new List<PublishProblem>();
        var lines = new List<DialogueResultLine>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (ResolvedLine resolved in flow.Lines)
        {
            DialogueLine line = resolved.Line;

            if (!seen.Add(line.LineId))
            {
                problems.Add(new PublishProblem(
                    PublishProblemKind.DuplicateLineId,
                    line.LineId,
                    $"LineId '{line.LineId}'가 결과 안에서 두 번 나옵니다.",
                    IsBlocking: true));
            }

            node.BranchExits.TryGetValue(line.LineId, out string? branchExit);

            lines.Add(new DialogueResultLine(
                resolved.Index,
                line.LineId,
                line.Revision,
                line.Speaker,
                line.Text,
                Freeze(line.Transition, project, definition, available),
                line.Transition?.OpensBranch == true ? branchExit : null));
        }

        AddFlowProblems(flow, problems);
        AddScriptProblems(project, node, problems);

        var assignments = ConnectedSetNodeResolver.Resolve(project, node.Id)
            .SelectMany(connected => connected.Node.Assignments)
            .Select(assignment => new DialogueResultAssignment(assignment.Variable, assignment.Value))
            .ToList();

        return new DialogueDraft(
            node.Id,
            node.Name,
            script.ScriptId,
            project.FindScript(script.ScriptId)?.SourceRevision ?? 0,
            script.Locale,
            lines,
            assignments,
            node.DefaultExitTargetNodeId,
            problems);
    }

    /// <summary>
    /// 초안에 identity를 붙여 결과로 만든다. 저장소에 넣는 것은 호출자(편집기)가 한다.
    /// 같은 내용의 최신 버전이 이미 있으면 그것을 그대로 돌려준다.
    /// </summary>
    public static DialogueResult Publish(
        ResultRepository results,
        DialogueDraft draft,
        string resultId,
        DateTimeOffset publishedAt,
        out bool created)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(draft);

        if (!draft.CanPublish)
        {
            throw new InvalidOperationException(
                $"'{draft.SourceNodeName}'을 발행할 수 없습니다.{Environment.NewLine}{draft.BlockingSummary()}");
        }

        string hash = ResultHash.Compute(DialogueResultJson.WriteBody(draft));
        DialogueResult? latest = results.LatestDialogue(resultId);

        if (latest is not null &&
            string.Equals(latest.Identity.ContentHash, hash, StringComparison.Ordinal) &&
            latest.Identity.SchemaVersion == DialogueResult.CurrentSchemaVersion)
        {
            created = false;
            return latest;
        }

        var identity = new ResultIdentity(
            resultId,
            results.NextDialogueVersion(resultId),
            DialogueResult.CurrentSchemaVersion,
            hash);

        var result = new DialogueResult(
            identity,
            draft.SourceNodeId,
            draft.SourceNodeName,
            draft.SourceScriptId,
            draft.SourceScriptRevision,
            draft.Locale,
            draft.Lines,
            draft.Assignments,
            draft.DefaultExitTargetNodeId,
            publishedAt);

        results.Add(result);
        created = true;
        return result;
    }

    /// <summary>
    /// 발행하지 않은 채 결과 모양으로 감싼다. 작업 중 미리 보기가 정식 출력과 <b>같은 합성기</b>를
    /// 지나게 하려는 것이다. Version 0이므로 어떤 PresentationResult와도 호환되지 않는다.
    /// </summary>
    public static DialogueResult AsWorkingResult(DialogueDraft draft, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return new DialogueResult(
            ResultIdentity.Working(
                DialogueResult.CurrentSchemaVersion,
                ResultHash.Compute(DialogueResultJson.WriteBody(draft))),
            draft.SourceNodeId,
            draft.SourceNodeName,
            draft.SourceScriptId,
            draft.SourceScriptRevision,
            draft.Locale,
            draft.Lines,
            draft.Assignments,
            draft.DefaultExitTargetNodeId,
            now);
    }

    private static DialogueResultTransition? Freeze(
        LineConditionTransition? transition,
        StoryProject project,
        GameDefinition? definition,
        AvailableConditionCatalog available)
    {
        if (transition is null)
        {
            return null;
        }

        if (transition.Kind == ConditionTransitionKind.EndIf)
        {
            return new DialogueResultTransition(transition.Kind, null, null, null);
        }

        AvailableCondition? condition = available.Find(transition.ConditionId)
            ?? AvailableConditionResolver.FindKnown(project, definition, transition.ConditionId);

        return new DialogueResultTransition(
            transition.Kind,
            transition.ConditionId,
            condition?.DisplayName,
            condition?.Expression);
    }

    private static void AddFlowProblems(DialogueFlow flow, List<PublishProblem> problems)
    {
        foreach (FlowProblem problem in flow.Problems)
        {
            (PublishProblemKind kind, bool blocking) = problem.Kind switch
            {
                FlowProblemKind.MissingScript => (PublishProblemKind.MissingScript, true),
                FlowProblemKind.UnknownCondition => (PublishProblemKind.UnknownCondition, true),
                FlowProblemKind.UnavailableCondition => (PublishProblemKind.UnknownCondition, true),
                FlowProblemKind.ElseIfWithoutIf => (PublishProblemKind.InvalidConditionStructure, true),
                FlowProblemKind.EndIfWithoutIf => (PublishProblemKind.InvalidConditionStructure, true),
                FlowProblemKind.NestedCondition => (PublishProblemKind.InvalidConditionStructure, true),
                FlowProblemKind.MissingExitTarget => (PublishProblemKind.MissingExitTarget, true),
                FlowProblemKind.OrphanedBranchExit => (PublishProblemKind.OrphanData, false),
                FlowProblemKind.OrphanedLineExtension => (PublishProblemKind.OrphanData, false),
                _ => (PublishProblemKind.InvalidConditionStructure, true)
            };

            problems.Add(new PublishProblem(kind, problem.LineId, problem.Message, blocking));
        }
    }

    private static void AddScriptProblems(
        StoryProject project,
        DialogueNode node,
        List<PublishProblem> problems)
    {
        if (project.FindScript(node.ScriptId) is not { } document)
        {
            return;
        }

        if (document.Lines.Count > 0 && document.ActiveLines.Any())
        {
            return;
        }

        problems.Add(new PublishProblem(
            PublishProblemKind.MissingScript,
            null,
            $"대본 '{document.Name}'에 살아 있는 줄이 없습니다.",
            IsBlocking: true));
    }
}
