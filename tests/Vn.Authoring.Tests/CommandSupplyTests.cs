using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

/// <summary>
/// 연출 공급 노드는 "어떤 커맨드군이 드롭다운에 보이는가"를 정하고, 프리셋은 값이 세팅된
/// 커맨드를 공급한다. 발행 시에는 참조가 아니라 <b>해석된 최종 인자 값</b>이 얼어붙는다 —
/// 프리셋을 나중에 고쳐도 발행된 결과는 불변이다.
/// </summary>
public class CommandSupplyTests
{
    [Fact]
    public void 공급_노드가_없으면_전체_카탈로그로_폴백한다()
    {
        var sample = new Sample();
        PresentationNode presentation = sample.Editor.AddPresentationNode(sample.File.Id, name: "연출");

        AvailablePresentationCommands available = AvailablePresentationCommandResolver.Resolve(
            sample.Project,
            presentation.Id,
            Sample.Definition);

        Assert.False(available.IsRestricted);
        Assert.Equal(
            new[] { "camera", "screen_effect", "character_acting" },
            available.Categories.Select(category => category.Id));
        Assert.Empty(available.Presets);
    }

    [Fact]
    public void 카메라_노드를_연결하면_그_범주만_보인다()
    {
        SupplyWorld world = BuildWorld();

        AvailablePresentationCommands available = AvailablePresentationCommandResolver.Resolve(
            world.Sample.Project,
            world.Presentation.Id,
            Sample.Definition);

        Assert.True(available.IsRestricted);
        Assert.Equal(new[] { "camera" }, available.Categories.Select(category => category.Id));
        AvailablePreset preset = Assert.Single(available.Presets);
        Assert.Equal("급접근", preset.DisplayName);
        Assert.Single(available.PresetsFor("camera"));
        Assert.Empty(available.PresetsFor("screen_effect"));
    }

    [Fact]
    public void 링크를_끊으면_다시_전체_카탈로그다()
    {
        SupplyWorld world = BuildWorld();
        NodeLink link = world.Sample.Project.Links.Single(item =>
            item.Kind == NodeLinkKind.CommandSupply);

        world.Sample.Editor.SetLinkEnabled(link.Id, enabled: false);

        AvailablePresentationCommands available = AvailablePresentationCommandResolver.Resolve(
            world.Sample.Project,
            world.Presentation.Id,
            Sample.Definition);

        Assert.False(available.IsRestricted);
        Assert.Equal(3, available.Categories.Count);
    }

    [Fact]
    public void 공급_노드와_프리셋과_링크는_저장_왕복된다()
    {
        SupplyWorld world = BuildWorld();

        StoryProject reloaded = ProjectSnapshotCodec.Decode(
            ProjectSnapshotCodec.Encode(world.Sample.Project));

        CommandSupplyNode supply = (CommandSupplyNode)reloaded.FindNode(world.Supply.Id)!;
        Assert.Equal(new[] { "camera" }, supply.Categories);
        CommandPreset preset = Assert.Single(supply.Presets);
        Assert.Equal("급접근", preset.DisplayName);
        Assert.Equal("camera.closeup", preset.CommandDefinitionId);
        Assert.Equal("closeup", preset.ArgumentValues["preset"]);

        NodeLink link = Assert.Single(
            reloaded.Links,
            item => item.Kind == NodeLinkKind.CommandSupply);
        Assert.Equal(world.Supply.Id, link.SourceNodeId);
        Assert.Equal(world.Presentation.Id, link.TargetNodeId);

        // 프리셋을 참조하는 커맨드 인스턴스도 참조를 유지한다.
        PresentationNode presentation = reloaded.FindPresentation(world.Presentation.Id)!;
        Assert.Equal(
            preset.Id,
            presentation.Bindings.Single().Commands.Single().PresetId);
    }

    [Fact]
    public void 발행_결과에는_프리셋_참조가_아니라_해석된_값이_얼어붙는다()
    {
        SupplyWorld world = BuildWorld();

        PresentationResult result =
            world.Sample.Editor.PublishPresentation(world.Presentation.Id).Result;

        Assert.Equal(3, result.Identity.SchemaVersion);
        PresentationResultCommand frozen = result.Bindings.Single().Commands.Single();
        Assert.Equal("camera.closeup", frozen.DefinitionId);
        Assert.Equal("closeup", frozen.Arguments["preset"]);
    }

    [Fact]
    public void 프리셋을_고쳐도_기존_발행_결과의_해시는_불변이고_재발행만_바뀐다()
    {
        SupplyWorld world = BuildWorld();
        PresentationResult v1 = world.Sample.Editor.PublishPresentation(world.Presentation.Id).Result;
        string hashBefore = v1.Identity.ContentHash;

        world.Sample.Editor.UpdateCommandPreset(
            world.Supply.Id,
            world.Preset.Id,
            commandDefinitionId: "camera.wide",
            argumentValues: new Dictionary<string, string> { ["preset"] = "wide" });

        // 기존 결과는 불변이다.
        Assert.Equal(hashBefore, v1.Identity.ContentHash);
        Assert.Equal(
            "closeup",
            world.Sample.Project.Results.PresentationResults.Single()
                .Bindings.Single().Commands.Single().Arguments["preset"]);

        // 재발행하면 새 해석 값으로 v2가 생긴다.
        PublishOutcome<PresentationResult> outcome =
            world.Sample.Editor.PublishPresentation(world.Presentation.Id);
        Assert.True(outcome.Created);
        Assert.Equal(2, outcome.Result.Identity.Version);
        Assert.Equal("wide", outcome.Result.Bindings.Single().Commands.Single().Arguments["preset"]);
    }

    [Fact]
    public void 인스턴스_인자는_프리셋_값_위를_덮는다()
    {
        SupplyWorld world = BuildWorld();
        PresentationCommandInstance command =
            world.Presentation.Bindings.Single().Commands.Single();
        command.Arguments["preset"] = "override";

        PresentationResult result =
            world.Sample.Editor.PublishPresentation(world.Presentation.Id).Result;

        Assert.Equal("override", result.Bindings.Single().Commands.Single().Arguments["preset"]);
    }

    [Fact]
    public void 삭제된_프리셋을_참조하면_발행을_막는다()
    {
        SupplyWorld world = BuildWorld();
        world.Sample.Editor.RemoveCommandPreset(world.Supply.Id, world.Preset.Id);

        PublishRejectedException error = Assert.Throws<PublishRejectedException>(
            () => world.Sample.Editor.PublishPresentation(world.Presentation.Id));

        Assert.Contains(error.Problems, problem => problem.Kind == PublishProblemKind.UnknownPreset);
    }

    [Fact]
    public void 프리셋_사용_라인의_출력_텍스트는_직접_인자와_같다()
    {
        SupplyWorld world = BuildWorld();
        PresentationResult presentation =
            world.Sample.Editor.PublishPresentation(world.Presentation.Id).Result;

        YarnBundle bundle = YarnBundleEmitter.Emit(
            world.Dialogue,
            presentation,
            world.Sample.Project,
            Sample.Definition,
            bundleName: "supply_ep");

        // 프리셋으로 만든 커맨드도 파라미터 순서의 포지셔널 조립을 그대로 지난다.
        Assert.Contains("<<camera closeup>>", bundle.StoryText, StringComparison.Ordinal);
        Assert.False(bundle.HasBlockingProblems);
    }

    /// <summary>
    /// 수용 기준의 카메라 노드 예제 — camera 범주 공급 + 프리셋 하나,
    /// 프리셋을 참조하는 라인 연출 하나.
    /// </summary>
    private static SupplyWorld BuildWorld()
    {
        var sample = new Sample();
        string line = sample.Line("공급 대상 줄");
        DialogueResult dialogue = sample.Editor.PublishDialogue(sample.Dialogue.Id).Result;

        CommandSupplyNode supply = sample.Editor.AddCommandSupplyNode(sample.File.Id, name: "카메라 노드");
        sample.Editor.SetSupplyCategories(supply.Id, new[] { "camera" });
        CommandPreset preset = sample.Editor.AddCommandPreset(
            supply.Id,
            "camera.closeup",
            displayName: "급접근",
            argumentValues: new Dictionary<string, string> { ["preset"] = "closeup" });

        PresentationNode presentation = sample.Editor.AddPresentationNode(sample.File.Id, name: "연출");
        sample.Editor.SetPresentationSource(
            presentation.Id,
            dialogue.Identity.ResultId,
            dialogue.Identity.Version);
        sample.Editor.AddCommandSupplyLink(supply.Id, presentation.Id);
        sample.Editor.AddPresentationCommand(
            presentation.Id,
            line,
            "camera.closeup",
            presetId: preset.Id);

        return new SupplyWorld(sample, line, dialogue, supply, preset, presentation);
    }

    private sealed record SupplyWorld(
        Sample Sample,
        string LineId,
        DialogueResult Dialogue,
        CommandSupplyNode Supply,
        CommandPreset Preset,
        PresentationNode Presentation);
}
