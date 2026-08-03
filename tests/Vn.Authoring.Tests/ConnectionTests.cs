using Vn.Authoring.Flow;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests;

/// <summary>
/// 그래프의 포트·간선과 노드 내부의 다음 노드 설정은 별개의 상태가 아니다.
/// 포트는 저장되지 않고 조건 전환에서 계산되므로, 한쪽을 바꾸면 다른 쪽이 자동으로 따라온다.
/// </summary>
public class ConnectionTests
{
    [Fact]
    public void 조건_갈래마다_출력_포트가_하나씩_생긴다()
    {
        var sample = new Sample();
        var (_, l1, _, l3, _, _, _) = sample.BuildSpecExample();

        IReadOnlyList<ExitPort> ports = NodeConnections.PortsOf(sample.Dialogue, sample.Project);

        Assert.Equal(3, ports.Count);
        Assert.Equal(l1, ports[0].BranchOpenLineId);
        Assert.Equal(l3, ports[1].BranchOpenLineId);
        Assert.Equal(ExitPortKind.Default, ports[2].Kind);
    }

    [Fact]
    public void 출구가_없어도_포트는_보인다()
    {
        var sample = new Sample();
        sample.Line("if", LineConditionTransition.BeginIf(sample.ConditionA.Id));

        ExitPort port = NodeConnections.PortsOf(sample.Dialogue, sample.Project)[0];

        // 아직 연결하지 않았어도 포트가 있어야 작가가 그래프에서 끌어다 이을 수 있다.
        Assert.Equal(ExitPortKind.Branch, port.Kind);
        Assert.False(port.IsConnected);
    }

    [Fact]
    public void 조건_포트에는_작가용_조건_이름이_붙는다()
    {
        var sample = new Sample();
        sample.BuildSpecExample();

        IReadOnlyList<ExitPort> ports = NodeConnections.PortsOf(sample.Dialogue, sample.Project);

        // 내부 Id가 아니라 작가가 지은 이름이 보인다.
        Assert.Equal("호감 높음", ports[0].Label);

        // 같은 체인의 두 번째 갈래는 elseif라는 것을 함께 알려 준다.
        Assert.Equal("신뢰 높음 (elseif)", ports[1].Label);
    }

    [Fact]
    public void 조건_이름을_바꾸면_간선_라벨도_바뀐다()
    {
        var sample = new Sample();
        sample.BuildSpecExample();

        sample.Editor.UpdateCondition(sample.ConditionA.Id, "호감 아주 높음", "favor >= 9");

        Assert.Equal(
            "호감 아주 높음",
            NodeConnections.PortsOf(sample.Dialogue, sample.Project)[0].Label);
    }

    [Fact]
    public void 그래프에서_조건_포트를_연결하면_해당_갈래_출구가_생긴다()
    {
        var sample = new Sample();
        var (_, l1, _, _, _, _, _) = sample.BuildSpecExample();

        ExitPort port = NodeConnections.PortsOf(sample.Dialogue, sample.Project)[0];
        sample.Editor.SetExitTarget(port, sample.TargetA.Id);

        Assert.Equal(sample.TargetA.Id, sample.Dialogue.BranchExits[l1]);
    }

    [Fact]
    public void 조건_간선을_삭제하면_해당_갈래_출구가_사라진다()
    {
        var sample = new Sample();
        sample.BuildSpecExample();

        ExitPort port = NodeConnections.PortsOf(sample.Dialogue, sample.Project)[0];
        sample.Editor.SetExitTarget(port, sample.TargetA.Id);
        sample.Editor.SetExitTarget(port, null);

        Assert.Empty(sample.Dialogue.BranchExits);
        Assert.DoesNotContain(
            NodeConnections.AllConnections(sample.Project),
            connection => connection.Kind == ExitPortKind.Branch);
    }

    /// <summary>
    /// 노드 내부 화면이 하는 일과 그래프가 하는 일이 같은 함수를 지난다는 사실 자체가
    /// 두 화면의 동기화 방식이다. 별도의 동기화 코드가 없다.
    /// </summary>
    [Fact]
    public void 노드_내부에서_바꾸든_그래프에서_바꾸든_같은_상태다()
    {
        var sample = new Sample();
        var (_, l1, _, _, _, _, _) = sample.BuildSpecExample();

        // 노드 내부 화면이 하는 일
        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Branch, l1, sample.TargetA.Id);

        ExitPort fromGraph = NodeConnections.AllConnections(sample.Project)
            .Single(port => port.Kind == ExitPortKind.Branch);

        Assert.Equal(sample.TargetA.Id, fromGraph.TargetNodeId);

        // 그래프가 하는 일
        sample.Editor.SetExitTarget(fromGraph, sample.TargetB.Id);

        Assert.Equal(sample.TargetB.Id, sample.Dialogue.BranchExits[l1]);
    }

    [Fact]
    public void 설정_노드는_실행_출구를_가지지_않는다()
    {
        // SetNode는 조건 공급자다. 실행 흐름은 DialogueNode만 정한다.
        var sample = new Sample();

        Assert.Empty(NodeConnections.PortsOf(sample.SetNode, sample.Project));

        // 실행 출구를 억지로 지정해도 조용히 무시된다.
        sample.Editor.SetExitTarget(sample.SetNode.Id, ExitPortKind.Default, null, sample.Dialogue.Id);
        Assert.Null(sample.SetNode.DefaultExitTargetNodeId);
    }

    [Fact]
    public void 연출_공급_노드도_실행_출구를_가지지_않는다()
    {
        var sample = new Sample();
        CommandSupplyNode supply = sample.Editor.AddCommandSupplyNode(sample.File.Id, name: "공급");

        Assert.Empty(NodeConnections.PortsOf(supply, sample.Project));

        sample.Editor.SetExitTarget(supply.Id, ExitPortKind.Default, null, sample.Dialogue.Id);
        Assert.Null(supply.DefaultExitTargetNodeId);
    }

    [Fact]
    public void 없는_노드로는_연결되지_않는다()
    {
        var sample = new Sample();

        sample.Editor.SetExitTarget(sample.Dialogue.Id, ExitPortKind.Default, null, "nd_없는것");

        Assert.Null(sample.Dialogue.DefaultExitTargetNodeId);
    }
}
