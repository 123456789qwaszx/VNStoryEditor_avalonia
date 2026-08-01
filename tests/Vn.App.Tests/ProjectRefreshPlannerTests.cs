using Vn.App.Services;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.App.Tests;

/// <summary>
/// SetNode의 TextBox가 LostFocus에서 값을 커밋해도 그 TextBox 자체를 다시 만들지 않는다는 계약.
/// 그래프와 Dialogue 조건 선택지는 새 조건 이름을 읽어야 하므로 조건 정의 변경은
/// 단순 Content와도, 전체 Structure 변경과도 다르게 처리한다.
/// </summary>
public class ProjectRefreshPlannerTests
{
    [Fact]
    public void 조건을_편집_중인_SetNode는_유지하고_그래프만_갱신한다()
    {
        ProjectRefreshPlan plan = ProjectRefreshPlanner.Plan(
            ProjectChangeKind.ConditionDefinition,
            new SetNode(name: "설정"));

        Assert.True(plan.RebuildGraph);
        Assert.False(plan.RefreshGraphPositions);
        Assert.False(plan.RebuildInspector);
    }

    [Fact]
    public void Dialogue가_선택되어_있으면_조건_드롭다운도_다시_계산한다()
    {
        ProjectRefreshPlan plan = ProjectRefreshPlanner.Plan(
            ProjectChangeKind.ConditionDefinition,
            new DialogueNode(name: "장면"));

        Assert.True(plan.RebuildGraph);
        Assert.True(plan.RebuildInspector);
    }

    [Fact]
    public void assignment_내용_변경은_컨트롤을_재생성하지_않는다()
    {
        ProjectRefreshPlan plan = ProjectRefreshPlanner.Plan(
            ProjectChangeKind.Content,
            new SetNode(name: "설정"));

        Assert.False(plan.RebuildGraph);
        Assert.False(plan.RefreshGraphPositions);
        Assert.False(plan.RebuildInspector);
    }


    [Fact]
    public void Dialogue_내용_변경은_편집기를_재생성하지_않고_Preview만_갱신한다()
    {
        ProjectRefreshPlan plan = ProjectRefreshPlanner.Plan(
            ProjectChangeKind.DialogueContent,
            new DialogueNode(name: "장면"));

        Assert.False(plan.RebuildGraph);
        Assert.False(plan.RebuildInspector);
        Assert.True(plan.RefreshPreview);
    }

    [Fact]
    public void 그래프_좌표_이동은_Preview를_다시_합성하지_않는다()
    {
        ProjectRefreshPlan plan = ProjectRefreshPlanner.Plan(
            ProjectChangeKind.Layout,
            new DialogueNode(name: "장면"));

        Assert.True(plan.RefreshGraphPositions);
        Assert.False(plan.RefreshPreview);
    }

    [Fact]
    public void 구조_변경만_그래프와_Inspector를_모두_다시_만든다()
    {
        ProjectRefreshPlan plan = ProjectRefreshPlanner.Plan(
            ProjectChangeKind.Structure,
            new SetNode(name: "설정"));

        Assert.True(plan.RebuildGraph);
        Assert.True(plan.RebuildInspector);
    }
    [Fact]
    public void Settings_link_변경은_SetNode_편집기를_유지하고_그래프만_갱신한다()
    {
        ProjectRefreshPlan plan = ProjectRefreshPlanner.Plan(
            ProjectChangeKind.Connections,
            new SetNode(name: "설정"));

        Assert.True(plan.RebuildGraph);
        Assert.False(plan.RefreshGraphPositions);
        Assert.False(plan.RebuildInspector);
    }

    [Fact]
    public void Settings_link_변경은_선택된_Dialogue의_조건_목록을_다시_계산한다()
    {
        ProjectRefreshPlan plan = ProjectRefreshPlanner.Plan(
            ProjectChangeKind.Connections,
            new DialogueNode(name: "장면"));

        Assert.True(plan.RebuildGraph);
        Assert.False(plan.RefreshGraphPositions);
        Assert.True(plan.RebuildInspector);
    }

    [Fact]
    public void Presentation_link_변경은_선택된_Presentation의_대상과_orphan을_다시_계산한다()
    {
        ProjectRefreshPlan plan = ProjectRefreshPlanner.Plan(
            ProjectChangeKind.Connections,
            new PresentationNode(name: "연출"));

        Assert.True(plan.RebuildGraph);
        Assert.True(plan.RebuildInspector);
    }

    [Fact]
    public void Presentation_Command_편집은_현재_연출_편집기를_재생성하지_않는다()
    {
        ProjectRefreshPlan plan = ProjectRefreshPlanner.Plan(
            ProjectChangeKind.PresentationContent,
            new PresentationNode(name: "연출"));

        Assert.False(plan.RebuildGraph);
        Assert.False(plan.RebuildInspector);
        Assert.False(plan.RefreshPreview);
    }

    [Fact]
    public void Dialogue_내용이_바뀌면_선택된_Presentation의_읽기_전용_행을_다시_만든다()
    {
        ProjectRefreshPlan plan = ProjectRefreshPlanner.Plan(
            ProjectChangeKind.DialogueContent,
            new PresentationNode(name: "연출"));

        Assert.True(plan.RebuildInspector);
        Assert.False(plan.RefreshPreview);
    }

}
