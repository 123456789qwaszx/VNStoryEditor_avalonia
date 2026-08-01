using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

/// <summary>
/// samples/Authoring은 손으로 쓴 프로젝트 파일이다.
/// 형식이 정말 사람이 읽고 쓸 수 있는지, 그리고 그것이 도구가 계산하는 구조와 같은지 확인한다.
/// 저장 형식을 고치면 이 테스트가 먼저 깨진다.
/// </summary>
public class SampleProjectTests
{
    private static string SamplePath =>
        Path.GetFullPath("../../../../../samples/Authoring/story.vnstory.json");

    private static StoryProject Load() => ProjectJson.Load(SamplePath);

    [Fact]
    public void 손으로_쓴_샘플을_읽는다()
    {
        StoryProject project = Load();

        Assert.Equal("게리에 1장", project.Title);
        Assert.Equal(5, project.Nodes.Count);
        Assert.Equal("nd_setup", project.StartNodeId);
        Assert.IsType<SetNode>(project.Nodes[0]);
    }

    [Fact]
    public void 설정_노드가_조건_두_개를_공급한다()
    {
        List<ConditionDefinition> conditions = Load().EnumerateConditions().ToList();

        Assert.Equal(new[] { "호감 높음", "신뢰 높음" }, conditions.Select(item => item.Name));
        Assert.Equal("favor >= 5", conditions[0].Expression);
    }

    [Fact]
    public void 조건_갈래와_출구가_기대대로_계산된다()
    {
        StoryProject project = Load();
        DialogueNode scene = project.FindDialogue("nd_scene")!;

        DialogueFlow flow = ConditionFlowResolver.Resolve(scene, project);

        Assert.Empty(flow.Problems);
        Assert.Equal(2, flow.Branches.Count);

        // 첫 갈래: 호감 → 호감 결말
        Assert.Equal("cd_favor", flow.Branches[0].ConditionId);
        Assert.Equal("nd_good", flow.Branches[0].ExitTargetNodeId);
        Assert.Equal(0, flow.Branches[0].BranchIndexInChain);

        // 둘째 갈래: 신뢰 → 신뢰 경로. 같은 체인의 elseif라 깊이가 늘지 않는다.
        Assert.Equal("cd_trust", flow.Branches[1].ConditionId);
        Assert.Equal("nd_trust", flow.Branches[1].ExitTargetNodeId);
        Assert.Equal(1, flow.Branches[1].BranchIndexInChain);
        Assert.Equal(flow.Branches[0].ChainIndex, flow.Branches[1].ChainIndex);

        // 깊이는 0 또는 1뿐이다.
        Assert.All(flow.Lines, line => Assert.InRange(line.Depth, 0, 1));

        // endif 줄부터 다시 바깥이다.
        Assert.Null(flow.Lines.Single(line => line.Line.Id == "ln_d1").Branch);

        // 각 갈래의 마지막 줄만 출구로 표시된다.
        Assert.Equal(
            new[] { "ln_b2", "ln_c2" },
            flow.Lines.Where(line => line.IsBranchExit).Select(line => line.Line.Id));
    }

    [Fact]
    public void 그래프_간선이_기대대로_만들어진다()
    {
        StoryProject project = Load();

        var connections = NodeConnections.AllConnections(project)
            .Select(port => (port.NodeId, port.Kind, port.Label, port.TargetNodeId))
            .ToList();

        Assert.Contains(("nd_setup", ExitPortKind.Default, "기본", "nd_scene"), connections);
        Assert.Contains(("nd_scene", ExitPortKind.Branch, "호감 높음", "nd_good"), connections);
        Assert.Contains(("nd_scene", ExitPortKind.Branch, "신뢰 높음 (elseif)", "nd_trust"), connections);
        Assert.Contains(("nd_scene", ExitPortKind.Default, "기본", "nd_normal"), connections);
        Assert.Equal(4, connections.Count);
    }

    [Fact]
    public void 게임_정의가_변수_후보를_공급한다()
    {
        GameDefinition definition = GameDefinition.LoadBeside(SamplePath);

        Assert.Equal(4, definition.Variables.Count);
        Assert.Contains(definition.Variables, variable => variable.Name == "favor");

        // 도구는 이 이름들을 코드로 알지 못한다. 파일이 공급할 뿐이다.
        Assert.Equal(2, definition.Events.Count);
    }

    /// <summary>
    /// 손으로 쓴 파일을 도구가 다시 써도 의미가 같아야 한다.
    /// 키 순서나 공백은 도구 형식으로 정규화되지만 구조는 그대로다.
    /// </summary>
    [Fact]
    public void 다시_저장해도_의미가_같다()
    {
        StoryProject original = Load();
        StoryProject roundTripped = ProjectJson.Read(ProjectJson.Write(original));

        Assert.Equal(
            original.Nodes.Select(node => node.Id),
            roundTripped.Nodes.Select(node => node.Id));

        DialogueNode before = original.FindDialogue("nd_scene")!;
        DialogueNode after = roundTripped.FindDialogue("nd_scene")!;

        Assert.Equal(before.BranchExits, after.BranchExits);
        Assert.Equal(
            before.Lines.Select(line => (line.Id, line.Speaker, line.Text, line.Transition?.Kind, line.Transition?.ConditionId)),
            after.Lines.Select(line => (line.Id, line.Speaker, line.Text, line.Transition?.Kind, line.Transition?.ConditionId)));

        // 한 번 더 써도 같은 문자열이다.
        Assert.Equal(ProjectJson.Write(roundTripped), ProjectJson.Write(ProjectJson.Read(ProjectJson.Write(roundTripped))));
    }
}
