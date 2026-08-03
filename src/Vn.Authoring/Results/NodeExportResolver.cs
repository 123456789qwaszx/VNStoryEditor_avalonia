using Vn.Authoring.Model;

namespace Vn.Authoring.Results;

/// <summary>대사 노드 하나의 내보내기 짝 — 대사 결과와 (있다면) 공급된 연출 결과.</summary>
public sealed record NodeExport(
    string DialogueNodeId,
    DialogueResult? Dialogue,
    PresentationResult? Presentation,
    IReadOnlyList<CompositionProblem> Problems)
{
    public bool CanExport => Dialogue is not null && !Problems.Any(problem => problem.IsBlocking);

    public string ProblemSummary() =>
        string.Join(" / ", Problems.Select(problem => problem.Message));
}

/// <summary>
/// 대사 노드 하나의 내보내기 입력을 <b>연결에서</b> 계산한다.
///
/// 명시적 합성 레코드 대신, 연출 노드가 발행한 결과를 대사 노드에 연결한
/// <see cref="NodeLinkKind.PresentationSupply"/> link가 짝을 정한다.
/// 연출 결과는 자신이 읽은 대사 결과(Id·버전·해시)를 이미 못박고 있으므로,
/// 내보내는 대사 결과는 <b>연출이 읽은 바로 그 버전</b>이다 — 짝이 구조적으로 호환된다.
/// 대사에 더 새 발행이 있으면 경고로 알리되 막지 않는다.
///
/// 공급 연결이 없으면 최신 대사 결과만으로 Story 단독 내보내기가 된다.
/// </summary>
public static class NodeExportResolver
{
    public static NodeExport Resolve(StoryProject project, string dialogueNodeId)
    {
        ArgumentNullException.ThrowIfNull(project);

        var problems = new List<CompositionProblem>();

        if (project.FindDialogue(dialogueNodeId) is not { } node)
        {
            problems.Add(new CompositionProblem(
                CompositionProblemKind.MissingDialogueResult,
                $"'{dialogueNodeId}'는 대사 노드가 아닙니다.",
                IsBlocking: true));
            return new NodeExport(dialogueNodeId, null, null, problems);
        }

        DialogueResult? latest = project.Results.DialogueResultsOf(node.Id).LastOrDefault();
        NodeLink? supply = SupplyLinkOf(project, node.Id);

        if (supply is null)
        {
            if (latest is null)
            {
                problems.Add(new CompositionProblem(
                    CompositionProblemKind.MissingDialogueResult,
                    $"'{node.Name}'에 발행된 대사 결과가 없습니다. 먼저 발행하세요.",
                    IsBlocking: true));
            }

            return new NodeExport(node.Id, latest, null, problems);
        }

        PresentationResult? presentation = project.Results
            .PresentationResultsOf(supply.SourceNodeId)
            .LastOrDefault();
        string presentationName = project.FindPresentation(supply.SourceNodeId)?.Name
            ?? supply.SourceNodeId;

        if (presentation is null)
        {
            problems.Add(new CompositionProblem(
                CompositionProblemKind.MissingPresentationResult,
                $"연출 노드 '{presentationName}'이 아직 발행하지 않았습니다. 발행하거나 공급 연결을 끊으세요.",
                IsBlocking: true));
            return new NodeExport(node.Id, latest, null, problems);
        }

        DialogueResult? paired = project.Results.FindDialogue(
            presentation.Source.ResultId,
            presentation.Source.Version);

        if (paired is null)
        {
            problems.Add(new CompositionProblem(
                CompositionProblemKind.MissingDialogueResult,
                $"연출 '{presentationName}'이 읽은 대사 결과 '{presentation.Source.Label}'을 찾을 수 없습니다.",
                IsBlocking: true));
            return new NodeExport(node.Id, latest, presentation, problems);
        }

        if (!string.Equals(paired.SourceNodeId, node.Id, StringComparison.Ordinal))
        {
            problems.Add(new CompositionProblem(
                CompositionProblemKind.SourceMismatch,
                $"연출 '{presentationName}'은 다른 대사 노드('{paired.SourceNodeName}')의 결과 위에서 " +
                "만들어졌습니다. 이 대사 노드에 공급할 수 없습니다.",
                IsBlocking: true));
            return new NodeExport(node.Id, latest, presentation, problems);
        }

        problems.AddRange(RuntimeCompositionResolver.CheckCompatibility(paired, presentation));

        if (latest is not null && latest.Identity.Version > paired.Identity.Version)
        {
            problems.Add(new CompositionProblem(
                CompositionProblemKind.SourceMismatch,
                $"대사 최신 발행은 v{latest.Identity.Version}이지만 연출은 v{paired.Identity.Version}을 " +
                "읽습니다. 연출 노드에서 새 결과를 읽고 재발행하면 최신 짝으로 내보냅니다.",
                IsBlocking: false));
        }

        return new NodeExport(node.Id, paired, presentation, problems);
    }

    /// <summary>이 대사 노드에 연출 결과를 공급하는 활성 link. 여럿이면 Order가 앞선 하나만 쓴다.</summary>
    public static NodeLink? SupplyLinkOf(StoryProject project, string dialogueNodeId)
    {
        return project.Links
            .Select((link, index) => (Link: link, Index: index))
            .Where(item =>
                item.Link.Kind == NodeLinkKind.PresentationSupply &&
                item.Link.IsEnabled &&
                string.Equals(item.Link.TargetNodeId, dialogueNodeId, StringComparison.Ordinal))
            .OrderBy(item => item.Link.Order)
            .ThenBy(item => item.Index)
            .Select(item => item.Link)
            .FirstOrDefault();
    }
}
