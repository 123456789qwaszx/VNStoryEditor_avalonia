using Vn.Authoring.Model;

namespace Vn.Authoring.Flow;

public enum ExitPortKind
{
    /// <summary>노드 전체가 끝난 뒤의 출구. 모든 노드에 하나씩 있다.</summary>
    Default,

    /// <summary>특정 조건 갈래를 지났을 때의 출구.</summary>
    Branch
}

/// <summary>
/// 그래프에서 끌어다 연결할 수 있는 출력 포트 하나.
///
/// 포트는 저장되지 않는다. 노드의 조건 전환에서 계산된다. 그래서 작가가 대사 화면에서
/// <c>elseif</c>를 하나 추가하면 그래프에도 포트가 하나 늘고, 그 반대도 마찬가지다.
/// 두 화면이 각자 연결 상태를 들고 있지 않기 때문에 어긋날 자리가 없다.
/// </summary>
/// <param name="BranchOpenLineId">조건 포트일 때 그 갈래를 여는 줄. 기본 포트면 null이다.</param>
public sealed record ExitPort(
    ExitPortKind Kind,
    string NodeId,
    string? BranchOpenLineId,
    string Label,
    string? TargetNodeId,
    int PaletteIndex)
{
    public bool IsConnected => TargetNodeId is not null;

    /// <summary>같은 포트를 가리키는지. 그래프의 간선과 모델을 잇는 열쇠다.</summary>
    public bool SamePortAs(ExitPort other)
    {
        return Kind == other.Kind &&
            string.Equals(NodeId, other.NodeId, StringComparison.Ordinal) &&
            string.Equals(BranchOpenLineId, other.BranchOpenLineId, StringComparison.Ordinal);
    }
}

/// <summary>
/// 노드의 출력 포트 목록을 계산한다.
///
/// 그래프 간선과 노드 내부의 다음 노드 설정은 같은 상태다. 둘 다 이 계산을 지나므로
/// 한쪽에서 바꾸면 다른 쪽이 자동으로 같은 결과를 본다.
/// </summary>
public static class NodeConnections
{
    public static IReadOnlyList<ExitPort> PortsOf(StoryNode node, StoryProject project)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(project);

        var ports = new List<ExitPort>();

        if (node is DialogueNode dialogue)
        {
            DialogueFlow flow = ConditionFlowResolver.Resolve(dialogue, project);

            foreach (ConditionBranch branch in flow.Branches)
            {
                ports.Add(new ExitPort(
                    ExitPortKind.Branch,
                    dialogue.Id,
                    branch.OpenLineId,
                    LabelFor(branch, project),
                    branch.ExitTargetNodeId,
                    branch.PaletteIndex));
            }

            ports.Add(new ExitPort(
                ExitPortKind.Default,
                dialogue.Id,
                null,
                "기본",
                dialogue.DefaultExitTargetNodeId,
                -1));

            return ports;
        }

        // 설정 노드는 조건 갈래를 갖지 않는다. 값만 준비하고 다음으로 넘긴다.
        ports.Add(new ExitPort(
            ExitPortKind.Default,
            node.Id,
            null,
            "기본",
            node.DefaultExitTargetNodeId,
            -1));

        return ports;
    }

    /// <summary>프로젝트 전체의 간선. 그래프가 그대로 그린다.</summary>
    public static IReadOnlyList<ExitPort> AllConnections(StoryProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return project.EnumerateNodes()
            .SelectMany(node => PortsOf(node, project))
            .Where(port => port.IsConnected)
            .ToList();
    }

    private static string LabelFor(ConditionBranch branch, StoryProject project)
    {
        ConditionDefinition? condition = project.FindCondition(branch.ConditionId);

        string name = condition is null
            ? "알 수 없는 조건"
            : ConditionChoices.DisplayName(condition);

        // 같은 체인의 두 번째 갈래부터는 elseif라는 것을 함께 보여 준다.
        // 조건 이름만 보면 그것이 첫 갈래인지 나중 갈래인지 알 수 없다.
        return branch.BranchIndexInChain == 0
            ? name
            : $"{name} (elseif)";
    }
}
