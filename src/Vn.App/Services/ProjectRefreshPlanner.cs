using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.App.Services;

/// <summary>
/// 도메인 변경 한 번이 각 화면에 어떤 갱신을 요구하는지 결정한다.
///
/// 이 판단을 MainWindow의 switch 안에 흩어 놓으면 변경 종류를 추가할 때마다
/// SetNode 편집 컨트롤을 불필요하게 다시 만드는 회귀가 생긴다. 특히 조건 TextBox의
/// LostFocus 커밋은 다음 클릭이 도착하기 전에 컨트롤을 교체해서 두 번째 칸을 클릭할 수 없게 했다.
/// </summary>
internal static class ProjectRefreshPlanner
{
    public static ProjectRefreshPlan Plan(ProjectChangeKind kind, StoryNode? selectedNode)
    {
        return kind switch
        {
            ProjectChangeKind.Layout => new ProjectRefreshPlan(
                RebuildGraph: false,
                RefreshGraphPositions: true,
                RebuildInspector: false),

            ProjectChangeKind.Structure => new ProjectRefreshPlan(
                RebuildGraph: true,
                RefreshGraphPositions: false,
                RebuildInspector: true),

            ProjectChangeKind.ConditionDefinition => new ProjectRefreshPlan(
                RebuildGraph: true,
                RefreshGraphPositions: false,
                // 조건을 편집 중인 SetNode는 그대로 둔다. DialogueNode가 선택되어 있다면
                // 드롭다운과 갈래 라벨을 새 조건 이름으로 다시 계산한다.
                RebuildInspector: selectedNode is DialogueNode),

            ProjectChangeKind.NodeMetadata => new ProjectRefreshPlan(
                RebuildGraph: true,
                RefreshGraphPositions: false,
                RebuildInspector: false),

            _ => default
        };
    }
}

internal readonly record struct ProjectRefreshPlan(
    bool RebuildGraph,
    bool RefreshGraphPositions,
    bool RebuildInspector);
