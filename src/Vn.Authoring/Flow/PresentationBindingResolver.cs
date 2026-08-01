using Vn.Authoring.Model;

namespace Vn.Authoring.Flow;

/// <summary>
/// PresentationNode의 LineId binding이 현재 연결 대상 Dialogue에서 유효한지 계산한다.
/// orphan 여부는 저장하지 않는다. Dialogue 줄이나 link가 바뀔 때마다 공식 모델에서 다시 계산한다.
/// </summary>
public static class PresentationBindingResolver
{
    public static DialogueNode? ResolveTarget(StoryProject project, string presentationNodeId)
    {
        ArgumentNullException.ThrowIfNull(project);

        NodeLink? link = project.Links.FirstOrDefault(item =>
            item.Kind == NodeLinkKind.Presentation &&
            item.IsEnabled &&
            string.Equals(item.SourceNodeId, presentationNodeId, StringComparison.Ordinal));

        return link is null ? null : project.FindDialogue(link.TargetNodeId);
    }

    public static IReadOnlyList<ResolvedPresentationBinding> Resolve(
        StoryProject project,
        PresentationNode presentation)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(presentation);

        DialogueNode? target = ResolveTarget(project, presentation.Id);
        var lines = new Dictionary<string, LineBox>(StringComparer.Ordinal);

        if (target is not null)
        {
            foreach (LineBox line in target.Lines)
            {
                lines.TryAdd(line.Id, line);
            }
        }

        return presentation.Bindings
            .Select(binding =>
            {
                lines.TryGetValue(binding.LineId, out LineBox? line);
                return new ResolvedPresentationBinding(
                    binding,
                    target?.Id,
                    line,
                    IsOrphan: line is null);
            })
            .ToList();
    }
}

public sealed record ResolvedPresentationBinding(
    PresentationLineBinding Binding,
    string? DialogueNodeId,
    LineBox? Line,
    bool IsOrphan);
