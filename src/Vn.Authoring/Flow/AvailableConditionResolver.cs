using Vn.Authoring.Definition;
using Vn.Authoring.Model;

namespace Vn.Authoring.Flow;

public enum AvailableConditionSourceKind
{
    GameGlobal,
    SetNode
}

/// <summary>
/// DialogueNode가 현재 선택할 수 있는 조건 하나의 읽기 모델.
/// 프로젝트 조건과 게임 전역 조건의 저장 타입이 달라도 편집 화면과 흐름 해석기는
/// 이 중립적인 형태만 본다.
/// </summary>
public sealed record AvailableCondition(
    string Id,
    string Name,
    string Expression,
    AvailableConditionSourceKind SourceKind,
    string? SourceNodeId)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name;
}

public sealed class AvailableConditionCatalog
{
    private readonly Dictionary<string, AvailableCondition> _byId;

    public AvailableConditionCatalog(IReadOnlyList<AvailableCondition> conditions)
    {
        Conditions = conditions;
        _byId = conditions.ToDictionary(condition => condition.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<AvailableCondition> Conditions { get; }

    public AvailableCondition? Find(string? conditionId)
    {
        return conditionId is not null && _byId.TryGetValue(conditionId, out AvailableCondition? condition)
            ? condition
            : null;
    }
}

/// <summary>
/// DialogueNode가 사용할 수 있는 조건의 범위를 계산한다.
///
/// 게임 전역 조건은 항상 들어오고, 프로젝트 조건은 활성화된 Settings link로 연결된
/// SetNode의 것만 들어온다. 같은 Id가 여러 곳에서 공급되면 먼저 나온 하나만 사용한다.
/// 순서는 게임 전역 조건 → Settings link Order → 프로젝트의 link 저장 순서 → SetNode 내부 순서다.
/// </summary>
public static class AvailableConditionResolver
{
    public static AvailableConditionCatalog Resolve(
        StoryProject project,
        string dialogueNodeId,
        GameDefinition? definition = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (project.FindDialogue(dialogueNodeId) is null)
        {
            return new AvailableConditionCatalog(Array.Empty<AvailableCondition>());
        }

        definition ??= GameDefinition.Empty;
        var conditions = new List<AvailableCondition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (ConditionSpec condition in definition.Conditions)
        {
            if (string.IsNullOrWhiteSpace(condition.Id) || !seen.Add(condition.Id))
            {
                continue;
            }

            conditions.Add(new AvailableCondition(
                condition.Id,
                condition.Name,
                condition.Expression,
                AvailableConditionSourceKind.GameGlobal,
                SourceNodeId: null));
        }

        IEnumerable<(NodeLink Link, int Index)> links = project.Links
            .Select((link, index) => (Link: link, Index: index))
            .Where(item =>
                item.Link.Kind == NodeLinkKind.Settings &&
                item.Link.IsEnabled &&
                string.Equals(item.Link.TargetNodeId, dialogueNodeId, StringComparison.Ordinal))
            .OrderBy(item => item.Link.Order)
            .ThenBy(item => item.Index);

        foreach ((NodeLink link, _) in links)
        {
            if (project.FindNode(link.SourceNodeId) is not SetNode setNode)
            {
                continue;
            }

            foreach (ConditionDefinition condition in setNode.Conditions)
            {
                if (string.IsNullOrWhiteSpace(condition.Id) || !seen.Add(condition.Id))
                {
                    continue;
                }

                conditions.Add(new AvailableCondition(
                    condition.Id,
                    condition.Name,
                    condition.Expression,
                    AvailableConditionSourceKind.SetNode,
                    setNode.Id));
            }
        }

        return new AvailableConditionCatalog(conditions);
    }

    /// <summary>
    /// 현재 Dialogue 범위와 무관하게 조건 Id가 어디엔가 정의되어 있는지 찾는다.
    /// Settings link가 끊긴 조건과 완전히 삭제된 조건을 구분하는 데 사용한다.
    /// </summary>
    public static AvailableCondition? FindKnown(
        StoryProject project,
        GameDefinition? definition,
        string? conditionId)
    {
        if (string.IsNullOrEmpty(conditionId))
        {
            return null;
        }

        definition ??= GameDefinition.Empty;

        ConditionSpec? global = definition.Conditions.FirstOrDefault(
            condition => string.Equals(condition.Id, conditionId, StringComparison.Ordinal));

        if (global is not null)
        {
            return new AvailableCondition(
                global.Id,
                global.Name,
                global.Expression,
                AvailableConditionSourceKind.GameGlobal,
                SourceNodeId: null);
        }

        foreach (SetNode setNode in project.EnumerateNodes().OfType<SetNode>())
        {
            ConditionDefinition? local = setNode.Conditions.FirstOrDefault(
                condition => string.Equals(condition.Id, conditionId, StringComparison.Ordinal));

            if (local is not null)
            {
                return new AvailableCondition(
                    local.Id,
                    local.Name,
                    local.Expression,
                    AvailableConditionSourceKind.SetNode,
                    setNode.Id);
            }
        }

        return null;
    }

    public static string UnavailableLabel(AvailableCondition? known, string conditionId)
    {
        string name = known?.DisplayName ?? conditionId;
        return $"사용할 수 없음 · {name}";
    }
}
