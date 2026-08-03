using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;
using Vn.Authoring.Editing;
using Vn.Authoring.Script;

namespace Vn.Authoring.Tests;

/// <summary>
/// samples/Authoring은 손으로 쓴 프로젝트 파일이다.
/// 형식이 정말 사람이 읽고 쓸 수 있는지, 그리고 그것이 도구가 계산하는 구조와 같은지 확인한다.
/// 저장 형식을 고치면 이 테스트가 먼저 깨진다.
/// </summary>
public class SampleProjectTests
{
    private static string SamplePath =>
        Path.GetFullPath("../../../../../samples/Authoring/project.vnproject.json");

    private static StoryProject Load() => ProjectStore.Load(SamplePath).Project;

    [Fact]
    public void 손으로_쓴_샘플을_읽는다()
    {
        StoryProject project = Load();

        Assert.Equal("게리에 1장", project.Title);
        Assert.Equal(6, project.EnumerateNodes().Count());
        Assert.Equal("nd_setup", project.StartNodeId);
        StoryFile file = Assert.Single(project.Files);
        Assert.Equal("sf_chapter01", file.Id);
        Assert.IsType<SetNode>(file.Nodes[0]);
        NodeLink settings = Assert.Single(project.Links);
        Assert.Equal(NodeLinkKind.Settings, settings.Kind);
        Assert.Equal("nd_setup", settings.SourceNodeId);
        Assert.Equal("nd_scene", settings.TargetNodeId);
    }

    /// <summary>
    /// 화자·대사는 대본 파일에만 있다. 노드 파일에는 LineId와 조건만 있다.
    /// 이 두 가지를 한 파일에서 찾을 수 있게 되는 순간 진실이 두 곳이 된다.
    /// </summary>
    [Fact]
    public void 대본이_화자와_대사를_소유하고_노드는_LineId만_가리킨다()
    {
        StoryProject project = Load();
        ScriptDocument script = project.FindScript("sc_chapter01")!;
        DialogueNode scene = project.FindDialogue("nd_scene")!;

        Assert.Equal(8, script.ActiveLines.Count());
        Assert.Equal(new LocalizedLine("라루", "3분의 1이요?"), script.Text("ln_a2"));

        // 노드는 조건이 붙은 세 줄만 알고 있다. 나머지 줄은 대본이 순서를 정한다.
        Assert.Equal(
            new[] { "ln_b1", "ln_c1", "ln_d1" },
            scene.LineExtensions.Select(extension => extension.LineId));

        string storyText = File.ReadAllText(
            Path.Combine(Path.GetDirectoryName(SamplePath)!, "story", "chapter01.vnstory.json"));
        Assert.DoesNotContain("3분의 1이요?", storyText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"speaker\"", storyText, StringComparison.Ordinal);
    }

    /// <summary>
    /// 손으로 쓴 원본 대본을 다시 읽어도 LineId가 하나도 바뀌지 않아야 한다.
    /// 이것이 깨지면 연출·녹음·번역이 전부 끊어진다.
    /// </summary>
    [Fact]
    public void 원본_대본을_다시_읽어도_LineId가_그대로다()
    {
        StoryProject project = Load();
        var editor = new ProjectEditor(project);
        string rawPath = Path.Combine(Path.GetDirectoryName(SamplePath)!, "raw", "chapter01.txt");

        ScriptSyncPlan plan = editor.PlanScriptSync(
            "sc_chapter01",
            File.ReadAllText(rawPath),
            rawPath);

        Assert.False(plan.HasConflicts);
        Assert.True(plan.IsNoOp);
        Assert.Equal(
            new[] { "ln_a1", "ln_a2", "ln_b1", "ln_b2", "ln_c1", "ln_c2", "ln_d1", "ln_d2" },
            plan.Entries.Select(entry => entry.LineId));
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
        Assert.Null(flow.Lines.Single(line => line.Line.LineId == "ln_d1").Branch);

        // 각 갈래의 마지막 줄만 출구로 표시된다.
        Assert.Equal(
            new[] { "ln_b2", "ln_c2" },
            flow.Lines.Where(line => line.IsBranchExit).Select(line => line.Line.LineId));
    }

    [Fact]
    public void 그래프_간선이_기대대로_만들어진다()
    {
        StoryProject project = Load();

        var connections = NodeConnections.AllConnections(project)
            .Select(port => (port.NodeId, port.Kind, port.Label, port.TargetNodeId))
            .ToList();

        // SetNode(nd_setup)는 조건 공급자라 실행 간선을 만들지 않는다.
        Assert.Contains(("nd_scene", ExitPortKind.Branch, "호감 높음", "nd_good"), connections);
        Assert.Contains(("nd_scene", ExitPortKind.Branch, "신뢰 높음 (elseif)", "nd_trust"), connections);
        Assert.Contains(("nd_scene", ExitPortKind.Default, "기본", "nd_normal"), connections);
        Assert.Equal(3, connections.Count);
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
        StoryProject roundTripped = ProjectSnapshotCodec.Decode(ProjectSnapshotCodec.Encode(original));

        Assert.Equal(
            original.EnumerateNodes().Select(node => node.Id),
            roundTripped.EnumerateNodes().Select(node => node.Id));

        DialogueNode before = original.FindDialogue("nd_scene")!;
        DialogueNode after = roundTripped.FindDialogue("nd_scene")!;

        Assert.Equal(before.BranchExits, after.BranchExits);
        Assert.Equal(
            DialogueScriptResolver.Resolve(original, before)
                .Lines.Select(line => (line.LineId, line.Speaker, line.Text, line.Transition?.Kind, line.Transition?.ConditionId)),
            DialogueScriptResolver.Resolve(roundTripped, after)
                .Lines.Select(line => (line.LineId, line.Speaker, line.Text, line.Transition?.Kind, line.Transition?.ConditionId)));

        // 한 번 더 써도 같은 문자열이다.
        Assert.Equal(
            ProjectSnapshotCodec.Encode(roundTripped),
            ProjectSnapshotCodec.Encode(ProjectSnapshotCodec.Decode(ProjectSnapshotCodec.Encode(roundTripped))));
    }
}
