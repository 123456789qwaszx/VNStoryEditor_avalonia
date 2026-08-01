using Vn.Authoring.Definition;
using Vn.Authoring.Model;

namespace Vn.Authoring.Flow;

/// <summary>
/// 줄에 적힌 전환만 보고 "지금 어떤 조건 갈래 안인가"를 앞에서부터 계산한다.
///
/// 이 클래스가 조건 모델의 유일한 해석자다. 화면이 색을 칠할 때도, 그래프가 포트를 만들 때도,
/// 저장 형식이 검증될 때도 여기를 지난다. 해석이 두 벌 있으면 화면과 그래프가 다른 구조를
/// 보여 주게 되고, 그때 작가는 어느 쪽이 맞는지 알 방법이 없다.
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
        GameDefinition? definition = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(project);

        AvailableConditionCatalog available = AvailableConditionResolver.Resolve(
            project,
            node.Id,
            definition);
        var problems = new List<FlowProblem>();
        var builders = new List<BranchBuilder>();

        // 줄마다 (전환 적용 뒤 갈래, 적용 전 갈래). 아직 완성되지 않은 갈래를 가리킨다.
        var perLine = new (BranchBuilder? Current, BranchBuilder? Preceding)[node.Lines.Count];

        BranchBuilder? active = null;
        int chainIndex = -1;

        for (int index = 0; index < node.Lines.Count; index++)
        {
            LineBox line = node.Lines[index];
            BranchBuilder? preceding = active;

            if (line.Transition is { } transition)
            {
                switch (transition.Kind)
                {
                    case ConditionTransitionKind.BeginIf when active is null:
                        chainIndex++;
                        active = Open(
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
                            line.Id,
                            "조건 안에서 새 조건을 열었습니다. 첫 버전은 중첩 조건을 지원하지 않아 같은 깊이의 다른 갈래로 다룹니다."));
                        active = Open(
                            line,
                            transition,
                            active.ChainIndex,
                            active.BranchIndexInChain + 1,
                            index,
                            builders,
                            problems,
                            project,
                            definition,
                            available);
                        break;

                    case ConditionTransitionKind.BeginElseIf when active is null:
                        problems.Add(new FlowProblem(
                            FlowProblemKind.ElseIfWithoutIf,
                            line.Id,
                            "열린 조건이 없는데 elseif가 있습니다. 새 조건을 여는 것으로 다룹니다."));
                        chainIndex++;
                        active = Open(
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
                        active = Open(
                            line,
                            transition,
                            active.ChainIndex,
                            active.BranchIndexInChain + 1,
                            index,
                            builders,
                            problems,
                            project,
                            definition,
                            available);
                        break;

                    case ConditionTransitionKind.EndIf when active is null:
                        problems.Add(new FlowProblem(
                            FlowProblemKind.EndIfWithoutIf,
                            line.Id,
                            "열린 조건이 없는데 조건 종료가 있습니다. 무시합니다."));
                        break;

                    case ConditionTransitionKind.EndIf:
                        active = null;
                        break;
                }
            }

            active?.Extend(index);
            perLine[index] = (active, preceding);
        }

        IReadOnlyList<ConditionBranch> branches = Complete(node, builders);
        var byOpenLine = branches.ToDictionary(branch => branch.OpenLineId, StringComparer.Ordinal);

        var lines = new List<ResolvedLine>(node.Lines.Count);

        for (int index = 0; index < node.Lines.Count; index++)
        {
            (BranchBuilder? current, BranchBuilder? preceding) = perLine[index];

            ConditionBranch? branch = current is null ? null : byOpenLine[current.OpenLineId];
            ConditionBranch? before = preceding is null ? null : byOpenLine[preceding.OpenLineId];

            lines.Add(new ResolvedLine(
                node.Lines[index],
                index,
                branch is null ? 0 : 1,
                branch,
                before,
                branch is not null && branch.HasExit && branch.LastLineIndex == index));
        }

        AddExitProblems(node, branches, project, problems);

        return new DialogueFlow(lines, branches, problems);
    }

    private static BranchBuilder Open(
        LineBox line,
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
                    line.Id,
                    "이 줄이 가리키는 조건 정의를 찾을 수 없습니다. 조건이 삭제되었을 수 있습니다."));
            }
            else
            {
                problems.Add(new FlowProblem(
                    FlowProblemKind.UnavailableCondition,
                    line.Id,
                    $"조건 '{known.DisplayName}'은 이 대사 노드에 연결된 SetNode 또는 게임 전역 조건에 포함되지 않습니다."));
            }
        }

        var builder = new BranchBuilder(
            line.Id,
            conditionId,
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
                target));
        }

        return branches;
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
            int chainIndex,
            int branchIndexInChain,
            int paletteIndex,
            int firstLineIndex)
        {
            OpenLineId = openLineId;
            ConditionId = conditionId;
            ChainIndex = chainIndex;
            BranchIndexInChain = branchIndexInChain;
            PaletteIndex = paletteIndex;
            FirstLineIndex = firstLineIndex;
            LastLineIndex = firstLineIndex;
        }

        public string OpenLineId { get; }
        public string ConditionId { get; }
        public int ChainIndex { get; }
        public int BranchIndexInChain { get; }
        public int PaletteIndex { get; }
        public int FirstLineIndex { get; }
        public int LastLineIndex { get; private set; }

        public void Extend(int lineIndex) => LastLineIndex = lineIndex;
    }
}
