using Vn.Authoring.Definition;
using Vn.Authoring.Model;

namespace Vn.Authoring.Flow;

/// <summary>
/// 줄에 적힌 전환만 보고 "지금 어떤 조건 갈래 안인가"를 앞에서부터 계산한다.
///
/// 이 클래스가 조건 모델의 유일한 해석자다. 화면이 색을 칠할 때도, 그래프가 포트를 만들 때도,
/// 결과를 발행할 때도 여기를 지난다. 해석이 두 벌 있으면 화면과 그래프가 다른 구조를
/// 보여 주게 되고, 그때 작가는 어느 쪽이 맞는지 알 방법이 없다.
///
/// 줄과 그 순서는 대본이 준다. 이 해석기는 <see cref="DialogueScriptResolver"/>가 합친
/// 투영만 보고, 대본을 직접 열지 않는다.
///
/// 조건의 존재 여부는 프로젝트 전체 목록이 아니라 현재 DialogueNode에 Settings link로 연결된
/// SetNode와 게임 전역 조건을 합친 카탈로그로 검증한다. 연결이 끊겼을 때 Transition은 보존하고
/// <see cref="FlowProblemKind.UnavailableCondition"/>으로 알린다.
/// </summary>
public static class ConditionFlowResolver
{
    public static DialogueFlow Resolve(
        DialogueNode node,
        StoryProject project,
        GameDefinition? definition = null,
        string? locale = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(project);

        return Resolve(
            node,
            DialogueScriptResolver.Resolve(project, node, locale),
            project,
            definition);
    }

    /// <summary>
    /// 이미 합쳐 놓은 대본 투영으로 계산한다.
    /// 한 화면에서 대본과 흐름을 모두 쓸 때 같은 join을 두 번 하지 않기 위한 입구다.
    /// </summary>
    public static DialogueFlow Resolve(
        DialogueNode node,
        DialogueScript script,
        StoryProject project,
        GameDefinition? definition = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(project);

        AvailableConditionCatalog available = AvailableConditionResolver.Resolve(
            project,
            node.Id,
            definition);
        var problems = new List<FlowProblem>();
        var builders = new List<BranchBuilder>();

        if (script.ScriptId is null)
        {
            problems.Add(new FlowProblem(
                FlowProblemKind.MissingScript,
                null,
                "이 대사 노드가 읽을 대본이 없습니다. 대본을 만들고 노드에 연결하세요."));
        }
        else if (!script.IsResolved)
        {
            problems.Add(new FlowProblem(
                FlowProblemKind.MissingScript,
                null,
                $"대본 '{script.ScriptId}'를 프로젝트에서 찾을 수 없습니다."));
        }

        // 줄마다 (전환 적용 뒤 갈래, 적용 전 갈래, 깊이, 적용 전 깊이). 아직 완성되지 않은 갈래를 가리킨다.
        var perLine = new (BranchBuilder? Current, BranchBuilder? Preceding, int Depth, int PrecedingDepth)[script.Lines.Count];

        // W54: 조건 갈래 <b>안의</b> 선택지를 지원한다 — 바깥 조건 하나 + 안 선택지 하나의
        // 두 칸 스택이다. active = 안쪽(선택지가 열려 있으면 그것, 아니면 조건).
        // 여전히 비지원(문제로 알림): 조건 안 조건, 선택지 안 조건, 선택지 안 선택지,
        // 선택지가 닫히기 전의 조건 전환.
        BranchBuilder? conditionActive = null;
        BranchBuilder? choiceActive = null;
        int chainIndex = -1;

        for (int index = 0; index < script.Lines.Count; index++)
        {
            DialogueLine line = script.Lines[index];
            BranchBuilder? active = choiceActive ?? conditionActive;
            BranchBuilder? preceding = active;
            int precedingDepth = (conditionActive is null ? 0 : 1) + (choiceActive is null ? 0 : 1);

            if (line.Transition is { } transition)
            {
                if (choiceActive is not null && !transition.IsChoiceKind)
                {
                    if (transition.Kind is ConditionTransitionKind.EndIf)
                    {
                        // 조건 종료는 열린 선택지도 함께 닫는다 (W55) — 한 줄로 둘 다 끝낸다.
                        choiceActive = null;
                    }
                    else
                    {
                        // 그 밖의 조건 전환(elseif 등)은 여전히 중첩 위반 — 선택을 먼저 닫아야 한다.
                        problems.Add(new FlowProblem(
                            FlowProblemKind.MixedChain,
                            line.LineId,
                            "선택 블록이 닫히기 전에 조건 전환이 나왔습니다. 선택지 끝을 먼저 넣으세요."));
                        choiceActive = null;
                    }
                }
                // 반대 방향(조건 안 선택 전환)은 정식 구성이다 (W54) — 문제로 알리지 않는다.

                active = choiceActive ?? conditionActive;

                switch (transition.Kind)
                {
                    case ConditionTransitionKind.BeginIf when conditionActive is null:
                        chainIndex++;
                        conditionActive = Open(
                            line,
                            transition,
                            chainIndex,
                            0,
                            index,
                            builders,
                            problems,
                            project,
                            definition,
                            available);
                        break;

                    case ConditionTransitionKind.BeginIf:
                        problems.Add(new FlowProblem(
                            FlowProblemKind.NestedCondition,
                            line.LineId,
                            "조건 안에서 새 조건을 열었습니다. 조건 중첩은 지원하지 않아 같은 깊이의 다른 갈래로 다룹니다."));
                        conditionActive = Open(
                            line,
                            transition,
                            conditionActive.ChainIndex,
                            conditionActive.BranchIndexInChain + 1,
                            index,
                            builders,
                            problems,
                            project,
                            definition,
                            available);
                        break;

                    case ConditionTransitionKind.BeginElseIf when conditionActive is null:
                        problems.Add(new FlowProblem(
                            FlowProblemKind.ElseIfWithoutIf,
                            line.LineId,
                            "열린 조건이 없는데 elseif가 있습니다. 새 조건을 여는 것으로 다룹니다."));
                        chainIndex++;
                        conditionActive = Open(
                            line,
                            transition,
                            chainIndex,
                            0,
                            index,
                            builders,
                            problems,
                            project,
                            definition,
                            available);
                        break;

                    case ConditionTransitionKind.BeginElseIf:
                        conditionActive = Open(
                            line,
                            transition,
                            conditionActive.ChainIndex,
                            conditionActive.BranchIndexInChain + 1,
                            index,
                            builders,
                            problems,
                            project,
                            definition,
                            available);
                        break;

                    case ConditionTransitionKind.EndIf when conditionActive is null:
                        problems.Add(new FlowProblem(
                            FlowProblemKind.EndIfWithoutIf,
                            line.LineId,
                            "열린 조건이 없는데 조건 종료가 있습니다. 무시합니다."));
                        break;

                    case ConditionTransitionKind.EndIf:
                        conditionActive = null;
                        break;

                    case ConditionTransitionKind.BeginChoice when choiceActive is null:
                        // 바깥이든 조건 갈래 안이든(W54) 새 선택 블록을 연다.
                        chainIndex++;
                        choiceActive = OpenOption(line, transition, chainIndex, 0, index, builders);
                        break;

                    case ConditionTransitionKind.BeginChoice:
                        // 선택 블록 안에서 다시 블록을 열었다. 다음 옵션으로 다루되 알린다.
                        problems.Add(new FlowProblem(
                            FlowProblemKind.NestedCondition,
                            line.LineId,
                            "선택 블록 안에서 새 선택 블록을 열었습니다. 같은 블록의 다음 옵션으로 다룹니다."));
                        choiceActive = OpenOption(
                            line,
                            transition,
                            choiceActive.ChainIndex,
                            choiceActive.BranchIndexInChain + 1,
                            index,
                            builders);
                        break;

                    case ConditionTransitionKind.BeginNextOption when choiceActive is null:
                        problems.Add(new FlowProblem(
                            FlowProblemKind.OptionWithoutChoice,
                            line.LineId,
                            "열린 선택 블록이 없는데 다음 옵션이 있습니다. 새 블록을 여는 것으로 다룹니다."));
                        chainIndex++;
                        choiceActive = OpenOption(line, transition, chainIndex, 0, index, builders);
                        break;

                    case ConditionTransitionKind.BeginNextOption:
                        choiceActive = OpenOption(
                            line,
                            transition,
                            choiceActive.ChainIndex,
                            choiceActive.BranchIndexInChain + 1,
                            index,
                            builders);
                        break;

                    case ConditionTransitionKind.EndChoice when choiceActive is null:
                        problems.Add(new FlowProblem(
                            FlowProblemKind.OptionWithoutChoice,
                            line.LineId,
                            "열린 선택 블록이 없는데 선택 종료가 있습니다. 무시합니다."));
                        break;

                    case ConditionTransitionKind.EndChoice:
                        // 선택만 닫는다 — 조건 갈래 안이었다면 그 갈래로 돌아간다 (W54).
                        choiceActive = null;
                        break;
                }
            }

            conditionActive?.Extend(index); // 바깥 조건 갈래는 안의 선택지 줄까지 덮는다
            choiceActive?.Extend(index);
            perLine[index] = (
                choiceActive ?? conditionActive,
                preceding,
                (conditionActive is null ? 0 : 1) + (choiceActive is null ? 0 : 1),
                precedingDepth);
        }

        IReadOnlyList<ConditionBranch> branches = Complete(node, builders);
        var byOpenLine = branches.ToDictionary(branch => branch.OpenLineId, StringComparer.Ordinal);

        var lines = new List<ResolvedLine>(script.Lines.Count);

        for (int index = 0; index < script.Lines.Count; index++)
        {
            (BranchBuilder? current, BranchBuilder? preceding, int depth, int precedingDepth) = perLine[index];

            ConditionBranch? branch = current is null ? null : byOpenLine[current.OpenLineId];
            ConditionBranch? before = preceding is null ? null : byOpenLine[preceding.OpenLineId];

            lines.Add(new ResolvedLine(
                script.Lines[index],
                index,
                depth,
                branch,
                before,
                branch is not null && branch.HasExit && branch.LastLineIndex == index,
                precedingDepth));
        }

        AddOrphanProblems(script, problems);
        AddExitProblems(node, branches, project, problems);

        return new DialogueFlow(script, lines, branches, problems);
    }

    private static BranchBuilder Open(
        DialogueLine line,
        LineConditionTransition transition,
        int chainIndex,
        int branchIndexInChain,
        int lineIndex,
        List<BranchBuilder> builders,
        List<FlowProblem> problems,
        StoryProject project,
        GameDefinition? definition,
        AvailableConditionCatalog available)
    {
        string conditionId = transition.ConditionId ?? string.Empty;

        if (available.Find(conditionId) is null)
        {
            AvailableCondition? known = AvailableConditionResolver.FindKnown(
                project,
                definition,
                conditionId);

            if (known is null)
            {
                problems.Add(new FlowProblem(
                    FlowProblemKind.UnknownCondition,
                    line.LineId,
                    "이 줄이 가리키는 조건 정의를 찾을 수 없습니다. 조건이 삭제되었을 수 있습니다."));
            }
            else
            {
                problems.Add(new FlowProblem(
                    FlowProblemKind.UnavailableCondition,
                    line.LineId,
                    // 2026-08-17 — 범위가 판(챕터)이 됐다. "연결된 SetNode"는 더 이상 규칙이
                    // 아니므로 문구도 그 사실을 말한다: 다른 챕터에 있으면 여기서는 못 쓴다.
                    $"조건 '{known.DisplayName}'은 이 챕터의 설정노드에도 게임 전역 조건에도 " +
                    "없습니다 — 다른 챕터의 조건은 여기서 쓸 수 없습니다."));
            }
        }

        var builder = new BranchBuilder(
            line.LineId,
            conditionId,
            optionId: null,
            chainIndex,
            branchIndexInChain,
            builders.Count,
            lineIndex);

        builders.Add(builder);
        return builder;
    }

    /// <summary>
    /// 옵션 갈래를 연다. 조건과 달리 카탈로그 검증이 없다 — 옵션의 정체성은
    /// 밖에서 공급되는 것이 아니라 이 줄이 소유하는 OptionId다.
    /// </summary>
    private static BranchBuilder OpenOption(
        DialogueLine line,
        LineConditionTransition transition,
        int chainIndex,
        int branchIndexInChain,
        int lineIndex,
        List<BranchBuilder> builders)
    {
        var builder = new BranchBuilder(
            line.LineId,
            conditionId: string.Empty,
            // 손으로 고친 파일이라 OptionId가 없을 수 있다. 빈 값으로 두면
            // 발행 검증이 잡는다 — 여기서 새로 발급하면 열 때마다 Id가 달라진다.
            optionId: transition.OptionId ?? string.Empty,
            chainIndex,
            branchIndexInChain,
            builders.Count,
            lineIndex);

        builders.Add(builder);
        return builder;
    }

    private static IReadOnlyList<ConditionBranch> Complete(
        DialogueNode node,
        List<BranchBuilder> builders)
    {
        var branches = new List<ConditionBranch>(builders.Count);

        foreach (BranchBuilder builder in builders)
        {
            node.BranchExits.TryGetValue(builder.OpenLineId, out string? target);

            branches.Add(new ConditionBranch(
                builder.OpenLineId,
                builder.ConditionId,
                builder.ChainIndex,
                builder.BranchIndexInChain,
                builder.PaletteIndex,
                builder.FirstLineIndex,
                builder.LastLineIndex,
                target,
                builder.OptionId));
        }

        return branches;
    }

    private static void AddOrphanProblems(DialogueScript script, List<FlowProblem> problems)
    {
        foreach (OrphanLineExtension orphan in script.Orphans)
        {
            if (orphan.Extension.IsEmpty)
            {
                continue;
            }

            problems.Add(new FlowProblem(
                FlowProblemKind.OrphanedLineExtension,
                orphan.Extension.LineId,
                orphan.IsRetired
                    ? "대본에서 사라진 줄에 조건 전환이 남아 있습니다. 지우지 않고 그대로 둡니다."
                    : "이 대본에 없는 LineId에 조건 전환이 남아 있습니다. 대본을 바꾸었을 수 있습니다."));
        }
    }

    private static void AddExitProblems(
        DialogueNode node,
        IReadOnlyList<ConditionBranch> branches,
        StoryProject project,
        List<FlowProblem> problems)
    {
        HashSet<string> openLines = branches
            .Select(branch => branch.OpenLineId)
            .ToHashSet(StringComparer.Ordinal);

        foreach ((string lineId, string target) in node.BranchExits.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!openLines.Contains(lineId))
            {
                problems.Add(new FlowProblem(
                    FlowProblemKind.OrphanedBranchExit,
                    lineId,
                    "더 이상 갈래를 열지 않는 줄에 조건 출구가 남아 있습니다."));
                continue;
            }

            if (project.FindNode(target) is null)
            {
                problems.Add(new FlowProblem(
                    FlowProblemKind.MissingExitTarget,
                    lineId,
                    "조건 출구가 가리키는 노드를 찾을 수 없습니다."));
            }
        }

        if (node.DefaultExitTargetNodeId is not null &&
            project.FindNode(node.DefaultExitTargetNodeId) is null)
        {
            problems.Add(new FlowProblem(
                FlowProblemKind.MissingExitTarget,
                null,
                "기본 출구가 가리키는 노드를 찾을 수 없습니다."));
        }
    }

    private sealed class BranchBuilder
    {
        public BranchBuilder(
            string openLineId,
            string conditionId,
            string? optionId,
            int chainIndex,
            int branchIndexInChain,
            int paletteIndex,
            int firstLineIndex)
        {
            OpenLineId = openLineId;
            ConditionId = conditionId;
            OptionId = optionId;
            ChainIndex = chainIndex;
            BranchIndexInChain = branchIndexInChain;
            PaletteIndex = paletteIndex;
            FirstLineIndex = firstLineIndex;
            LastLineIndex = firstLineIndex;
        }

        public string OpenLineId { get; }
        public string ConditionId { get; }
        public string? OptionId { get; }
        public bool IsChoice => OptionId is not null;
        public int ChainIndex { get; }
        public int BranchIndexInChain { get; }
        public int PaletteIndex { get; }
        public int FirstLineIndex { get; }
        public int LastLineIndex { get; private set; }

        public void Extend(int lineIndex) => LastLineIndex = lineIndex;
    }
}
