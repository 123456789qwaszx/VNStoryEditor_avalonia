using Vn.Authoring.Model;

namespace Vn.Authoring.Flow;

/// <summary>
/// DialogueNode에 활성 Settings link로 연결된 SetNode를 결정적인 순서로 계산한다.
///
/// 조건 드롭다운과 평면 문서 합성기가 서로 다른 링크 순서를 사용하면 같은 DialogueNode를
/// 두 화면이 다르게 해석하게 된다. 그래서 Settings link의 대상·활성 상태·Order 해석은
/// 이 한 곳에만 둔다.
/// </summary>
public static class ConnectedSetNodeResolver
{
    public static IReadOnlyList<ConnectedSetNode> Resolve(
        StoryProject project,
        string dialogueNodeId)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (project.FindDialogue(dialogueNodeId) is null)
        {
            return Array.Empty<ConnectedSetNode>();
        }

        IEnumerable<(NodeLink Link, int Index)> links = project.Links
            .Select((link, index) => (Link: link, Index: index))
            .Where(item =>
                item.Link.Kind == NodeLinkKind.Settings &&
                item.Link.IsEnabled &&
                string.Equals(item.Link.TargetNodeId, dialogueNodeId, StringComparison.Ordinal))
            .OrderBy(item => item.Link.Order)
            .ThenBy(item => item.Index);

        var connected = new List<ConnectedSetNode>();

        foreach ((NodeLink link, _) in links)
        {
            if (project.FindNode(link.SourceNodeId) is SetNode setNode)
            {
                connected.Add(new ConnectedSetNode(link, setNode));
            }
        }

        return connected;
    }
}

/// <summary>Settings link와 그 링크가 공급하는 실제 SetNode.</summary>
public sealed record ConnectedSetNode(NodeLink Link, SetNode Node);
