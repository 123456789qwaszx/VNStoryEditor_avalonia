using Vn.Authoring.Definition;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;

namespace Vn.Authoring.Results;

/// <summary>라이브 합성 한 번의 결과. 번들이 없으면 왜 없는지가 Problems에 있다.</summary>
public sealed record LiveComposition(
    string DialogueNodeId,
    string DialogueNodeName,
    YarnBundle? Bundle,
    DialogueResult? WorkingDialogue,
    PresentationResult? WorkingPresentation,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> BlockingProblems)
{
    public bool CanWrite => Bundle is not null && !Bundle.HasBlockingProblems && BlockingProblems.Count == 0;
}

/// <summary>
/// 대사노드 = CompositionNode (D-1)의 합성 경로. <b>새 이미터가 아니다</b> — 현재 작업
/// 상태를 발행과 같은 Freeze(DialoguePublisher.Draft·PresentationPublisher.Draft)로
/// 얼려 결과 모양으로 감싼 뒤(AsWorkingResult), 기존 <see cref="YarnBundleEmitter"/> 하나를
/// 지난다. 계약서 A5·B·C1·D2·E2 전 조항은 그 이미터 안에 있으므로 라이브 출력에도
/// 그대로 적용된다(불변식 3). 수동 내보내기도 같은 함수를 쓴다 — 바이트가 같다.
///
/// 발행(D-2)은 여기서 아무 게이트도 아니다. 다만 연출이 읽은 발행본과 현재 대사가
/// 어긋나면 기존 버전 짝 추적대로 <b>경고</b>한다 — 조용한 드리프트 금지.
/// </summary>
public static class LiveNodeComposer
{
    public static LiveComposition Compose(
        StoryProject project,
        string dialogueNodeId,
        GameDefinition definition,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(definition);

        var warnings = new List<string>();
        var blocking = new List<string>();

        DialogueNode? node = project.FindDialogue(dialogueNodeId);

        if (node is null)
        {
            return new LiveComposition(
                dialogueNodeId, dialogueNodeId, null, null, null, warnings,
                new[] { $"'{dialogueNodeId}'는 대사 노드가 아닙니다." });
        }

        // 현재 작업 상태의 Freeze — 발행과 같은 해석이다.
        DialogueDraft draft = DialoguePublisher.Draft(project, node.Id);

        foreach (PublishProblem problem in draft.Problems)
        {
            (problem.IsBlocking ? blocking : warnings).Add(problem.Message);
        }

        if (blocking.Count > 0)
        {
            return new LiveComposition(node.Id, node.Name, null, null, null, warnings, blocking);
        }

        DialogueResult working = DialoguePublisher.AsWorkingResult(draft, now);
        PresentationResult? presentation = null;

        // 공급 짝은 내보내기와 같은 규칙(NodeExportResolver)이다 — 사본 금지.
        if (NodeExportResolver.SupplyLinkOf(project, node.Id) is { } supply &&
            project.FindPresentation(supply.SourceNodeId) is { } presentationNode)
        {
            PresentationDraft presentationDraft = PresentationPublisher.Draft(project, presentationNode.Id);

            if (presentationDraft.Problems.Any(problem => problem.IsBlocking))
            {
                warnings.AddRange(presentationDraft.Problems
                    .Where(problem => problem.IsBlocking)
                    .Select(problem => $"연출 '{presentationNode.Name}' 제외: {problem.Message}"));
            }
            else
            {
                presentation = PresentationPublisher.AsWorkingResult(presentationDraft, now);

                // 버전 짝 추적 (D-2에서도 유지): 연출이 읽은 발행본과 현재 대사가 다르면 경고.
                if (presentation is not null &&
                    !string.Equals(
                        presentation.Source.ContentHash,
                        working.Identity.ContentHash,
                        StringComparison.Ordinal))
                {
                    warnings.Add(
                        $"연출 '{presentationNode.Name}'이 읽은 발행본과 현재 대사 내용이 다릅니다. " +
                        "대사를 다시 발행하고 연출 입력을 갱신하면 경고가 사라집니다.");
                }
            }
        }

        YarnBundle bundle = YarnBundleEmitter.Emit(working, presentation, project, definition);

        if (bundle.HasBlockingProblems)
        {
            blocking.Add(bundle.BlockingSummary());
        }

        return new LiveComposition(node.Id, node.Name, bundle, working, presentation, warnings, blocking);
    }
}
