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

    /// <summary>변수 이름이 비어 있는 등 성립하지 않는 변수 변경.</summary>
    InvalidSetOperation,

    /// <summary>옵션 라벨에 안정 Id가 없거나 선택 구조가 성립하지 않는다.</summary>
    InvalidChoiceOption,

    /// <summary>
    /// 발행된 최신 결과와 비교해 옵션 순서가 바뀌었거나 기존 옵션 위에 삽입되었다.
    /// 선택지 리플레이는 위치 기반이므로(계약서 C3) 출시된 세이브가 다른 선택지를 리플레이하게 된다.
    /// 발행을 막지는 않는다 — 출시 전에는 자유롭게 고친다.
    /// </summary>
    ChoiceOrderChanged,

    /// <summary>미리보기 태그 생성에 관한 알림(대문자 변수 소문자화 등). 발행은 막지 않는다.</summary>
    ChoicePreviewNotice,

    /// <summary>대본에서 사라진 줄에 대사 논리나 연출이 남아 있다. 발행은 막지 않는다.</summary>
    OrphanData,

    /// <summary>연출이 읽을 대사 결과를 아직 고르지 않았다.</summary>
    MissingSourceResult,

    /// <summary>커맨드가 참조하는 프리셋을 찾을 수 없거나 연결된 공급 범위에 없다.</summary>
    UnknownPreset,

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

            foreach (SetOperation operation in line.Sets)
            {
                if (string.IsNullOrWhiteSpace(operation.Variable))
                {
                    problems.Add(new PublishProblem(
                        PublishProblemKind.InvalidSetOperation,
                        line.LineId,
                        $"LineId '{line.LineId}'의 변수 변경에 변수 이름이 없습니다.",
                        IsBlocking: true));
                }
            }

            if (line.Transition is { OpensOption: true } optionTransition)
            {
                if (string.IsNullOrWhiteSpace(optionTransition.OptionId))
                {
                    problems.Add(new PublishProblem(
                        PublishProblemKind.InvalidChoiceOption,
                        line.LineId,
                        $"옵션 라벨 라인 '{line.LineId}'에 안정 OptionId가 없습니다. " +
                        "전환을 다시 지정해 Id를 발급받으세요.",
                        IsBlocking: true));
                }

                // 미리보기 태그 조회는 런타임에서 소문자 키로 일어난다(계약서 D5).
                foreach (SetOperation operation in line.Sets)
                {
                    if (operation.Variable.Any(char.IsUpper))
                    {
                        problems.Add(new PublishProblem(
                            PublishProblemKind.ChoicePreviewNotice,
                            line.LineId,
                            $"옵션 효과 변수 '{operation.Variable}'에 대문자가 있어 미리보기 태그는 " +
                            "소문자로 출력됩니다. 누적 표시 조회는 소문자 키로 일어납니다.",
                            IsBlocking: false));
                    }
                }
            }

            lines.Add(new DialogueResultLine(
                resolved.Index,
                line.LineId,
                line.Revision,
                line.Speaker,
                line.Text,
                Freeze(line.Transition, project, definition, available),
                line.Transition?.OpensBranch == true ? branchExit : null,
                line.Sets.Count == 0
                    ? null
                    : line.Sets
                        .Select(operation => new DialogueResultSetOperation(
                            operation.Variable,
                            operation.Operator,
                            operation.Value))
                        .ToArray(),
                // 둘째 전환부터 — 겹쳐 닫기·연달아 열기가 여기 실린다 (2026-08-17).
                line.Transitions.Count <= 1
                    ? null
                    : line.Transitions
                        .Skip(1)
                        .Select(transition => Freeze(transition, project, definition, available))
                        .OfType<DialogueResultTransition>()
                        .ToArray()));
        }

        AddFlowProblems(flow, problems);
        AddScriptProblems(project, node, problems);
        AddChoiceOrderProblems(project, node, lines, problems);

        // 선택지로 끝나는 노드는 정상이다 (2단계 포트 규칙, 2026-08-14 소유자 승인) —
        // 옵션들이 곧 노드의 끝이고, 이어진 옵션은 출구로 점프, 안 이은 옵션은 에피소드
        // 종료다. 합성기가 문서 끝에서 블록을 닫는다. 다만 "잊고 안 닫은" 경우와 화면상
        // 구분이 안 되므로, 막지 않고 알리기만 한다.
        bool choiceOpen = false;

        foreach (DialogueResultLine line in lines)
        {
            choiceOpen = line.Transition?.Kind switch
            {
                ConditionTransitionKind.BeginChoice or ConditionTransitionKind.BeginNextOption => true,
                // 조건 종료는 열린 선택지도 함께 닫는다 (W55) — 합성기가 암묵 ChoiceEnd를 낸다.
                ConditionTransitionKind.EndChoice or ConditionTransitionKind.EndIf => false,
                _ => choiceOpen
            };
        }

        if (choiceOpen)
        {
            problems.Add(new PublishProblem(
                PublishProblemKind.InvalidChoiceOption,
                null,
                "선택 블록이 문서 끝까지 열려 있습니다 — 이 옵션들이 노드의 끝입니다. " +
                "이어지는 줄을 두려면 블록 뒤 첫 일반 줄에 '선택지 끝'을 지정하세요.",
                IsBlocking: false));
        }

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

    /// <summary>
    /// 선택 블록의 옵션 순서를 발행된 최신 결과와 비교한다 (계약서 C3).
    /// 리플레이는 {블록 서수, 옵션 인덱스}이므로 순서 변경·중간 삽입·삭제는 전부
    /// 기존 세이브가 다른 선택지를 고르게 만든다. 경고만 하고 막지 않는다.
    /// </summary>
    private static void AddChoiceOrderProblems(
        StoryProject project,
        DialogueNode node,
        IReadOnlyList<DialogueResultLine> lines,
        List<PublishProblem> problems)
    {
        DialogueResult? latest = project.Results.DialogueResults
            .LastOrDefault(result => string.Equals(
                result.SourceNodeId,
                node.Id,
                StringComparison.Ordinal));

        if (latest is null)
        {
            return;
        }

        IReadOnlyList<IReadOnlyList<string>> before = OptionSequences(latest.Lines);
        IReadOnlyList<IReadOnlyList<string>> after = OptionSequences(lines);

        for (int ordinal = 0; ordinal < Math.Min(before.Count, after.Count); ordinal++)
        {
            IReadOnlyList<string> old = before[ordinal];
            IReadOnlyList<string> now = after[ordinal];

            string[] survivors = now.Where(id => old.Contains(id)).ToArray();
            bool reorderedOrRemoved = !survivors.SequenceEqual(old, StringComparer.Ordinal);

            int lastSurvivor = now.ToList().FindLastIndex(id => old.Contains(id));
            bool insertedAbove = now
                .Where((id, index) => !old.Contains(id) && index < lastSurvivor)
                .Any();

            if (reorderedOrRemoved || insertedAbove)
            {
                problems.Add(new PublishProblem(
                    PublishProblemKind.ChoiceOrderChanged,
                    null,
                    $"선택 블록 {ordinal + 1}의 옵션 순서가 발행본(v{latest.Identity.Version})과 다릅니다. " +
                    "선택지 리플레이는 위치 기반이라 출시된 세이브가 다른 선택지를 리플레이하게 됩니다. " +
                    "출시 후에는 옵션을 기존 항목 위에 삽입하거나 순서를 바꾸지 마세요.",
                    IsBlocking: false));
            }
        }
    }

    /// <summary>블록 서수 순서대로, 각 블록의 OptionId 목록.</summary>
    private static IReadOnlyList<IReadOnlyList<string>> OptionSequences(
        IReadOnlyList<DialogueResultLine> lines)
    {
        var blocks = new List<IReadOnlyList<string>>();
        List<string>? current = null;

        foreach (DialogueResultLine line in lines)
        {
            switch (line.Transition?.Kind)
            {
                case ConditionTransitionKind.BeginChoice:
                    current = new List<string> { line.Transition.OptionId ?? string.Empty };
                    blocks.Add(current);
                    break;

                case ConditionTransitionKind.BeginNextOption:
                    current?.Add(line.Transition.OptionId ?? string.Empty);
                    break;

                case ConditionTransitionKind.EndChoice:
                    current = null;
                    break;
            }
        }

        return blocks;
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

        if (transition.IsChoiceKind)
        {
            // 선택 전환은 조건 카탈로그와 무관하다. 옵션의 정체성만 얼린다.
            return new DialogueResultTransition(transition.Kind, null, null, null, transition.OptionId);
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
                FlowProblemKind.MixedChain => (PublishProblemKind.InvalidChoiceOption, true),
                FlowProblemKind.OptionWithoutChoice => (PublishProblemKind.InvalidChoiceOption, true),
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
