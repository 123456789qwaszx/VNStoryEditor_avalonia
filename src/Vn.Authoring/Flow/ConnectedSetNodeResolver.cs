using Vn.Authoring.Model;

namespace Vn.Authoring.Flow;

/// <summary>
/// DialogueNode가 쓸 수 있는 SetNode(조건·변수 공급자)를 결정적인 순서로 계산한다.
///
/// <b>범위는 챕터다</b> (2026-08-17 소유자: "시나리오 작가가 만든 조건, 변수 등은 챕터
/// 단위로 기록이 되어서 사용됐으면 해. 이거는 챕터단위로 전역에 쓰이는거야").
/// 판 = 챕터 1:1이므로 <b>같은 판에 있는 SetNode는 그 판의 모든 대사노드에 자동으로
/// 미친다</b> — 개별 Settings link를 걸 필요가 없다.
///
/// 예전에는 링크를 하나하나 걸어야 조건이 보였다. 작가가 노드를 만들 때마다 배관을 다시
/// 잇는 일이었고, 잊으면 "조건이 왜 안 보이지"가 됐다. 링크 데이터는 남아 있어도 이제
/// 범위를 좁히지 않는다(무시된다).
///
/// 순서는 판 안의 노드 순서 — 두 화면이 같은 하나를 본다.
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

        StoryFile? file = project.Files.FirstOrDefault(candidate =>
            candidate.Nodes.Any(node =>
                string.Equals(node.Id, dialogueNodeId, StringComparison.Ordinal)));

        if (file is null)
        {
            return Array.Empty<ConnectedSetNode>();
        }

        return file.Nodes.OfType<SetNode>()
            .Select(setNode => new ConnectedSetNode(setNode))
            .ToList();
    }
}

/// <summary>그 판(챕터)이 쓰는 조건·변수 공급자 하나.</summary>
public sealed record ConnectedSetNode(SetNode Node);
