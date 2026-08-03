using Vn.Authoring.Model;
using Vn.Authoring.Results;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

/// <summary>
/// 내보내기 짝은 명시적 합성 레코드가 아니라 연출 공급 연결(PresentationSupply link)에서
/// 계산한다. 연출 결과가 자신이 읽은 대사 결과를 못박고 있으므로 짝은 구조적으로 호환된다.
/// </summary>
public class NodeExportResolverTests
{
    [Fact]
    public void 공급_연결이_없으면_최신_대사_결과로_Story_단독_내보내기다()
    {
        var sample = new Sample();
        sample.Line("혼자 가는 줄");
        DialogueResult dialogue = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        NodeExport export = NodeExportResolver.Resolve(sample.Project, sample.Dialogue.Id);

        Assert.True(export.CanExport);
        Assert.Equal(dialogue.Identity, export.Dialogue!.Identity);
        Assert.Null(export.Presentation);
    }

    [Fact]
    public void 발행이_없으면_내보낼_수_없다()
    {
        var sample = new Sample();
        sample.Line("발행 전");

        NodeExport export = NodeExportResolver.Resolve(sample.Project, sample.Dialogue.Id);

        Assert.False(export.CanExport);
        Assert.Contains("발행", export.ProblemSummary(), StringComparison.Ordinal);
    }

    [Fact]
    public void 공급_연결이_있으면_연출이_읽은_바로_그_대사_결과와_짝이_된다()
    {
        SupplyWorld world = BuildWorld();

        NodeExport export = NodeExportResolver.Resolve(world.Sample.Project, world.Sample.Dialogue.Id);

        Assert.True(export.CanExport);
        Assert.Equal(world.Presentation.Identity, export.Presentation!.Identity);
        Assert.Equal(world.Presentation.Source.ResultId, export.Dialogue!.Identity.ResultId);
        Assert.Equal(world.Presentation.Source.Version, export.Dialogue.Identity.Version);
        Assert.Empty(export.Problems);
    }

    [Fact]
    public void 대사에_더_새_발행이_있으면_경고하되_막지_않는다()
    {
        SupplyWorld world = BuildWorld();

        // 대사를 고쳐 v2를 발행한다. 연출은 여전히 v1을 읽는다.
        sampleRepublish(world);

        NodeExport export = NodeExportResolver.Resolve(world.Sample.Project, world.Sample.Dialogue.Id);

        Assert.True(export.CanExport);
        Assert.Equal(1, export.Dialogue!.Identity.Version);
        CompositionProblem warning = Assert.Single(export.Problems);
        Assert.False(warning.IsBlocking);
        Assert.Contains("v2", warning.Message, StringComparison.Ordinal);

        static void sampleRepublish(SupplyWorld world)
        {
            string line = world.Sample.Script.ActiveLines.First().Id;
            world.Sample.Editor.SetScriptLineText(world.Sample.Script.Id, line, "라루", "고친 줄");
            world.Sample.Editor.PublishDialogue(world.Sample.Dialogue.Id);
        }
    }

    [Fact]
    public void 연출이_발행_전이면_내보내기를_막는다()
    {
        SupplyWorld world = BuildWorld(publishPresentation: false);

        NodeExport export = NodeExportResolver.Resolve(world.Sample.Project, world.Sample.Dialogue.Id);

        Assert.False(export.CanExport);
        Assert.Contains("발행하지 않았습니다", export.ProblemSummary(), StringComparison.Ordinal);
    }

    [Fact]
    public void 다른_대사_노드의_결과를_읽은_연출은_공급할_수_없다()
    {
        SupplyWorld world = BuildWorld();

        // 다른 대사 노드로 공급 대상을 바꾼다 — 연출의 Source는 원래 노드의 결과다.
        world.Sample.Editor.PublishDialogue(world.Sample.TargetA.Id);
        world.Sample.Editor.SetPresentationSupplyTarget(
            world.PresentationNode.Id,
            world.Sample.TargetA.Id);

        NodeExport export = NodeExportResolver.Resolve(world.Sample.Project, world.Sample.TargetA.Id);

        Assert.False(export.CanExport);
        Assert.Contains("다른 대사 노드", export.ProblemSummary(), StringComparison.Ordinal);
    }

    [Fact]
    public void 공급_대상은_한_쌍이다_다시_지정하면_기존_연결이_걷힌다()
    {
        SupplyWorld world = BuildWorld();

        // 두 번째 연출 노드가 같은 대사에 공급을 가져가면 첫 연결은 사라진다.
        PresentationNode second = world.Sample.Editor.AddPresentationNode(world.Sample.File.Id, name: "연출 2");
        world.Sample.Editor.SetPresentationSupplyTarget(second.Id, world.Sample.Dialogue.Id);

        NodeLink link = Assert.Single(
            world.Sample.Project.Links,
            item => item.Kind == NodeLinkKind.PresentationSupply);
        Assert.Equal(second.Id, link.SourceNodeId);

        // null이면 연결 해제다.
        world.Sample.Editor.SetPresentationSupplyTarget(second.Id, null);
        Assert.DoesNotContain(
            world.Sample.Project.Links,
            item => item.Kind == NodeLinkKind.PresentationSupply);
    }

    [Fact]
    public void 공급_연결은_저장_왕복된다()
    {
        SupplyWorld world = BuildWorld();

        StoryProject reloaded = ProjectSnapshotCodec.Decode(
            ProjectSnapshotCodec.Encode(world.Sample.Project));

        NodeLink link = Assert.Single(
            reloaded.Links,
            item => item.Kind == NodeLinkKind.PresentationSupply);
        Assert.Equal(world.PresentationNode.Id, link.SourceNodeId);
        Assert.Equal(world.Sample.Dialogue.Id, link.TargetNodeId);
    }

    private static SupplyWorld BuildWorld(bool publishPresentation = true)
    {
        var sample = new Sample();
        string line = sample.Line("공급될 줄");
        DialogueResult dialogue = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        PresentationNode node = sample.Editor.AddPresentationNode(sample.File.Id, name: "연출");
        sample.Editor.SetPresentationSource(node.Id, dialogue.Identity.ResultId, dialogue.Identity.Version);
        sample.Editor.AddPresentationCommand(node.Id, line, "camera.closeup");

        PresentationResult? presentation = publishPresentation
            ? sample.Editor.PublishPresentation(node.Id).Result
            : null;

        sample.Editor.SetPresentationSupplyTarget(node.Id, sample.Dialogue.Id);

        return new SupplyWorld(sample, dialogue, node, presentation!);
    }

    private sealed record SupplyWorld(
        Sample Sample,
        DialogueResult Dialogue,
        PresentationNode PresentationNode,
        PresentationResult Presentation);
}
