using Vn.Authoring.Definition;
using Vn.Authoring.Model;

namespace Vn.Authoring.Flow;

public enum ExitPortKind
{
    /// <summary>실행 가능한 노드 전체가 끝난 뒤의 출구.</summary>
    Default,

    /// <summary>특정 조건 갈래를 지났을 때의 출구.</summary>
    Branch,

    /// <summary>
    /// 챕터 간선(선택지) 하나를 골랐을 때의 출구 (v9, 2026-08-17). <b>열쇠는 선택지 문구</b>다 —
    /// 대본에 OPTION이 없어도(v9에서는 없는 것이 정상) 선택지마다 자유 씬을 달 수 있다.
    ///
    /// 대본의 줄에 매이지 않으므로 대본을 고쳐도 사라지지 않는다(문구의 주인은 챕터다).
    /// </summary>
    Choice
}

/// <summary>
/// 그래프에서 끌어다 연결할 수 있는 실행 출력 포트 하나.
///
/// Settings·Presentation link는 실행 포트가 아니며 <see cref="StoryProject.Links"/>에 별도로 저장된다.
/// 이 타입은 오직 다음 실행 노드를 결정하는 기본/조건 출구만 표현한다.
/// </summary>
/// <param name="IsChoice">선택지 옵션의 갈래인가 (IF 갈래와 구분 — 철도 배선 T2가 쓴다).</param>
/// <param name="ChoiceText">옵션의 원문 라벨. 챕터 간선과 라벨 짝을 맞추는 열쇠다.</param>
public sealed record ExitPort(
    ExitPortKind Kind,
    string NodeId,
    string? BranchOpenLineId,
    string Label,
    string? TargetNodeId,
    int PaletteIndex,
    bool IsChoice = false,
    string? ChoiceText = null)
{
    public bool IsConnected => TargetNodeId is not null;

    /// <summary>
    /// 이 포트를 저장할 때의 열쇠. 갈래는 여는 줄의 LineId, 선택지는 <b>문구</b>다(v9).
    /// 기본 출구는 열쇠가 없다 — 노드에 자리가 하나뿐이다.
    /// </summary>
    public string? ExitKey => Kind == ExitPortKind.Choice ? ChoiceText : BranchOpenLineId;

    public bool SamePortAs(ExitPort other)
    {
        return Kind == other.Kind &&
            string.Equals(NodeId, other.NodeId, StringComparison.Ordinal) &&
            string.Equals(ExitKey, other.ExitKey, StringComparison.Ordinal);
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

        if (node is not DialogueNode)
        {
            // 실행 출구는 DialogueNode만 가진다. SetNode·CommandSupplyNode는 공급자이고
            // (조건·커맨드를 주는 쪽), PresentationNode는 결과 소비자다 — 셋 다
            // 실행 순서를 결정하지 않는다. 공급 포트는 GraphProjection이 별도로 계산한다.
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
                    branch.PaletteIndex,
                    IsChoice: branch.IsChoice,
                    ChoiceText: branch.IsChoice ? flow.Script.Find(branch.OpenLineId)?.Text : null));
            }

            // 기본 출구는 엑셀노드만 가진다 (2026-08-21 소유자) — 커스텀(자유) 노드는
            // detour로 재생되고 호출한 갈래로 돌아가므로 출구 자체가 없다.
            // 다른 커스텀 씬으로 잇는 것도 조건 갈래(detour)의 몫이다.
            if (dialogue.ExcelEpisodeId is not null)
            {
                ports.Add(new ExitPort(
                    ExitPortKind.Default,
                    dialogue.Id,
                    null,
                    "기본",
                    dialogue.DefaultExitTargetNodeId,
                    -1));
            }
        }

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
            name = AvailableConditionResolver.LayeredLabel(condition);
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
