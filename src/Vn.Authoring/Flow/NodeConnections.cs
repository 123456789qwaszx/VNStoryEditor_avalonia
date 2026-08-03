using Vn.Authoring.Definition;
using Vn.Authoring.Model;

namespace Vn.Authoring.Flow;

public enum ExitPortKind
{
    /// <summary>실행 가능한 노드 전체가 끝난 뒤의 출구.</summary>
    Default,

    /// <summary>특정 조건 갈래를 지났을 때의 출구.</summary>
    Branch
}

/// <summary>
/// 그래프에서 끌어다 연결할 수 있는 실행 출력 포트 하나.
///
/// Settings·Presentation link는 실행 포트가 아니며 <see cref="StoryProject.Links"/>에 별도로 저장된다.
/// 이 타입은 오직 다음 실행 노드를 결정하는 기본/조건 출구만 표현한다.
/// </summary>
public sealed record ExitPort(
    ExitPortKind Kind,
    string NodeId,
    string? BranchOpenLineId,
    string Label,
    string? TargetNodeId,
    int PaletteIndex)
{
    public bool IsConnected => TargetNodeId is not null;

    public bool SamePortAs(ExitPort other)
    {
        return Kind == other.Kind &&
            string.Equals(NodeId, other.NodeId, StringComparison.Ordinal) &&
            string.Equals(BranchOpenLineId, other.BranchOpenLineId, StringComparison.Ordinal);
    }
}

/// <summary>
/// 노드의 실행 출력 포트 목록을 계산한다.
/// Settings·Presentation 공급 연결은 여기에 섞지 않는다.
/// </summary>
public static class NodeConnections
{
    public static IReadOnlyList<ExitPort> PortsOf(
        StoryNode node,
        StoryProject project,
        GameDefinition? definition = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(project);

        var ports = new List<ExitPort>();

        if (node is PresentationNode)
        {
            // PresentationNode는 실행 순서를 결정하지 않는다. Dialogue에 연출 데이터를
            // 공급하는 Presentation link만 GraphProjection에서 별도 포트로 계산한다.
            return ports;
        }

        if (node is DialogueNode dialogue)
        {
            DialogueFlow flow = ConditionFlowResolver.Resolve(dialogue, project, definition);
            AvailableConditionCatalog available = AvailableConditionResolver.Resolve(
                project,
                dialogue.Id,
                definition);

            foreach (ConditionBranch branch in flow.Branches)
            {
                ports.Add(new ExitPort(
                    ExitPortKind.Branch,
                    dialogue.Id,
                    branch.OpenLineId,
                    branch.IsChoice
                        ? ChoiceLabelFor(branch, flow)
                        : LabelFor(branch, project, definition, available),
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

        ports.Add(new ExitPort(
            ExitPortKind.Default,
            node.Id,
            null,
            "기본",
            node.DefaultExitTargetNodeId,
            -1));

        return ports;
    }

    /// <summary>프로젝트 전체의 실행 간선. Settings link는 포함하지 않는다.</summary>
    public static IReadOnlyList<ExitPort> AllConnections(
        StoryProject project,
        GameDefinition? definition = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        return project.EnumerateNodes()
            .SelectMany(node => PortsOf(node, project, definition))
            .Where(port => port.IsConnected)
            .ToList();
    }

    /// <summary>옵션 포트의 라벨은 버튼 문구다. 문구가 비어 있으면 옵션 순번으로 보여 준다.</summary>
    private static string ChoiceLabelFor(ConditionBranch branch, DialogueFlow flow)
    {
        string? text = flow.Script.Find(branch.OpenLineId)?.Text;

        return string.IsNullOrWhiteSpace(text)
            ? $"옵션 {branch.BranchIndexInChain + 1}"
            : $"→ {text}";
    }

    private static string LabelFor(
        ConditionBranch branch,
        StoryProject project,
        GameDefinition? definition,
        AvailableConditionCatalog available)
    {
        AvailableCondition? condition = available.Find(branch.ConditionId);
        string name;

        if (condition is not null)
        {
            name = condition.DisplayName;
        }
        else
        {
            AvailableCondition? known = AvailableConditionResolver.FindKnown(
                project,
                definition,
                branch.ConditionId);
            name = known is null
                ? "알 수 없는 조건"
                : AvailableConditionResolver.UnavailableLabel(known, branch.ConditionId);
        }

        return branch.BranchIndexInChain == 0
            ? name
            : $"{name} (elseif)";
    }
}
