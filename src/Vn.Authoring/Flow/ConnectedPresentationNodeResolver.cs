using Vn.Authoring.Model;

namespace Vn.Authoring.Flow;

/// <summary>DialogueNode에 활성 Presentation link로 연결된 연출 노드를 결정적인 순서로 찾는다.</summary>
public static class ConnectedPresentationNodeResolver
{
    public static IReadOnlyList<ConnectedPresentationNode> Resolve(
        StoryProject project,
        string dialogueNodeId,
        IReadOnlySet<string>? includedPresentationNodeIds = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        var result = new List<ConnectedPresentationNode>();

        foreach (var item in project.Links
                     .Select((link, index) => new { Link = link, Index = index })
                     .Where(item =>
                         item.Link.Kind == NodeLinkKind.Presentation &&
                         item.Link.IsEnabled &&
                         string.Equals(item.Link.TargetNodeId, dialogueNodeId, StringComparison.Ordinal) &&
                         (includedPresentationNodeIds is null ||
                          includedPresentationNodeIds.Contains(item.Link.SourceNodeId)))
                     .OrderBy(item => item.Link.Order)
                     .ThenBy(item => item.Index))
        {
            if (project.FindNode(item.Link.SourceNodeId) is PresentationNode node)
            {
                result.Add(new ConnectedPresentationNode(item.Link, node));
            }
        }

        return result;
    }
}

public sealed record ConnectedPresentationNode(NodeLink Link, PresentationNode Node);
