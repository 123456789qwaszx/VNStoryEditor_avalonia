using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// 연출 채널 자동화 (2026-08-21 소유자 — "미리 다 해둔 다음에 무대 프리뷰측에서 뭘 할지
/// 고르도록"). 예전에 사람이 그래프에서 하던 네 단계(대사 발행 → 연출 노드 만들기 →
/// 발행 결과 연결 → 공급 연결)를 EnsurePresentationChannel 한 번이 대신한다.
/// 고정용 데이터(발행 버전)는 그대로 산다 — 사람이 배선을 안 할 뿐이다.
/// </summary>
public class PresentationChannelTests
{
    [Fact]
    public void 채널_확보_한_번이_발행과_배선을_전부_한다()
    {
        var sample = new Sample();
        sample.Line("첫 줄");

        PresentationChannelOutcome outcome =
            sample.Editor.EnsurePresentationChannel(sample.Dialogue.Id);

        Assert.True(outcome.Ready);
        PresentationNode channel = outcome.Presentation!;

        // 대사가 발행됐고,
        DialogueResult dialogue = Assert.Single(sample.Project.Results.DialogueResults);
        Assert.Equal(sample.Dialogue.Id, dialogue.SourceNodeId);

        // 연출 노드가 그 발행본을 읽으며,
        Assert.Equal("본문 연출", channel.Name);
        Assert.Equal(dialogue.Identity.ResultId, channel.Source!.Value.ResultId);
        Assert.Equal(dialogue.Identity.Version, channel.Source.Value.Version);

        // 공급 연결(내보내기 짝)도 서 있고,
        NodeLink supply = Assert.Single(sample.Project.Links, link =>
            link.Kind == NodeLinkKind.PresentationSupply && link.IsEnabled);
        Assert.Equal(channel.Id, supply.SourceNodeId);
        Assert.Equal(sample.Dialogue.Id, supply.TargetNodeId);

        // 연출도 발행돼 내보내기 짝이 완성이다.
        NodeExport export = NodeExportResolver.Resolve(sample.Project, sample.Dialogue.Id);
        Assert.True(export.CanExport, export.ProblemSummary());
        Assert.NotNull(export.Presentation);
    }

    [Fact]
    public void 두_번_불러도_같은_채널이고_버전이_늘지_않는다()
    {
        var sample = new Sample();
        sample.Line("첫 줄");

        PresentationNode first = sample.Editor.EnsurePresentationChannel(sample.Dialogue.Id).Presentation!;
        PresentationNode second = sample.Editor.EnsurePresentationChannel(sample.Dialogue.Id).Presentation!;

        Assert.Equal(first.Id, second.Id);
        Assert.Single(sample.Project.EnumerateNodes().OfType<PresentationNode>());
        Assert.Single(sample.Project.Results.DialogueResults);
        Assert.Single(sample.Project.Results.PresentationResults);
    }

    [Fact]
    public void 대사가_바뀌면_다시_고정하되_바인딩은_지키다()
    {
        var sample = new Sample();
        string lineId = sample.Line("첫 줄");

        PresentationNode channel = sample.Editor.EnsurePresentationChannel(sample.Dialogue.Id).Presentation!;
        sample.Editor.AddPresentationCommand(channel.Id, lineId, "camera.closeup");

        // 대사를 고친다 — LineId는 그대로다.
        sample.Editor.SetScriptLineText(sample.Script.Id, lineId, "라루", "고친 첫 줄");

        PresentationChannelOutcome outcome =
            sample.Editor.EnsurePresentationChannel(sample.Dialogue.Id);

        // 새 버전이 발행되고 입력이 자동으로 따라온다.
        Assert.Equal(2, outcome.Presentation!.Source!.Value.Version);
        Assert.Equal(2, sample.Project.Results.DialogueResults.Count());

        // 그 줄의 연출은 살아 있다 — 채널 갱신은 binding을 손대지 않는다.
        Assert.NotNull(sample.Project.FindPresentation(channel.Id)!
            .FindBinding(lineId));
    }

    [Fact]
    public void 대사를_고정할_수_없으면_이유를_돌려준다()
    {
        var sample = new Sample();
        var empty = sample.Editor.AddScript("빈 대본");
        DialogueNode node = sample.Editor.AddDialogueNode(
            sample.File.Id, name: "빈 씬", scriptId: empty.Id);

        PresentationChannelOutcome outcome = sample.Editor.EnsurePresentationChannel(node.Id);

        Assert.False(outcome.Ready);
        Assert.Contains("고정할 수 없습니다", outcome.Problem, StringComparison.Ordinal);

        // 반쪽 채널을 남기지 않는다 — 연출 노드도 공급 연결도 안 생겼다.
        Assert.Empty(sample.Project.EnumerateNodes().OfType<PresentationNode>());
        Assert.DoesNotContain(sample.Project.Links, link =>
            link.Kind == NodeLinkKind.PresentationSupply);
    }
}
