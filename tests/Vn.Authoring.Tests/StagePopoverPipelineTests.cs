using Ked.Presentation.Core;
using Vn.Authoring.Assets;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// 무대 팝오버 조작이 화면까지 실제로 닿는가 — 소유자 버그 보고(2026-08-06:
/// "표정·반전은 적용되는데 place·depth는 안 된다")의 재현 하네스.
/// 조작(<see cref="PresentationStageActions"/>) → 드래프트(<c>InspectPresentationPublish</c>)
/// → 합성 폴드(<see cref="CoreStageFold"/>)의 실제 경로를 그대로 지난다.
/// </summary>
public class StagePopoverPipelineTests
{
    private static readonly PresentationCommandCatalog Catalog = PresentationCommandCatalog.Default;

    private static readonly string FixtureDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TuningFixtures", "ExportedTuning"));

    private static StageReducerTuning Tuning { get; } =
        RuntimeTuningLibrary.Load(FixtureDirectory, (1920, 1080)).Tuning!;

    private static (ProjectEditor Editor, PresentationNode Node) BuildEditor()
    {
        var project = new StoryProject();
        var file = new StoryFile("sf_pp", "테스트", "story/pp.vnstory.json");
        project.Files.Add(file);
        var editor = new ProjectEditor(project);
        PresentationNode node = editor.AddPresentationNode(file.Id, name: "연출");
        return (editor, node);
    }

    private static Dictionary<string, string> Args(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    /// <summary>PresentationNodeEditor의 프리뷰 경로 그대로: 드래프트 → LinesUpTo → 합성 폴드.</summary>
    private static CoreStageFoldResult FoldOf(ProjectEditor editor, PresentationNode node, string lineId)
    {
        PresentationDraft draft = editor.InspectPresentationPublish(node.Id);

        DialogueResult dialogue = new(
            new ResultIdentity("rs_pp", 1, DialogueResult.CurrentSchemaVersion, "sha256:test"),
            "nd_pp",
            "장면",
            "sc_pp",
            1,
            "ko-KR",
            [new DialogueResultLine(0, lineId, 1, "화자", "대사")],
            Array.Empty<DialogueResultAssignment>(),
            null,
            DateTimeOffset.UnixEpoch);

        return CoreStageFold.Fold(
            Catalog,
            draft.SetupCommands,
            MiniStageFold.LinesUpTo(dialogue, draft.Bindings, lineId),
            Tuning);
    }

    /// <summary>팝오버와 같은 호출로 슬롯·캐스팅·등장을 만든다.</summary>
    private static void StageCharacter(
        ProjectEditor editor, PresentationNode node, string slotKey, string characterId)
    {
        PresentationStageActions.ApplyToSetup(
            editor, Catalog, node.Id, "slot",
            Args(("slotKey", slotKey), ("stage", "stage00"), ("layer", "mid")));
        PresentationStageActions.ApplyToSetup(
            editor, Catalog, node.Id, "cast",
            Args(("slot", slotKey), ("characterKey", characterId)));
        PresentationStageActions.ApplyVisibility(
            editor, Catalog, node.Id, "ln_x", slotKey, visible: true);
    }

    [Fact]
    public void 팝오버_place는_코어_좌표를_실제로_움직인다()
    {
        (ProjectEditor editor, PresentationNode node) = BuildEditor();
        StageCharacter(editor, node, "c1", "parkeunseol");

        Vec2 before = FoldOf(editor, node, "ln_x")
            .CoreState!.Nodes.GetState("c1/CharSlot_Track_Focus").AnchoredPosition;

        // 캐릭터 팝오버의 place 격자와 같은 호출.
        PresentationStageActions.Apply(
            editor, Catalog, node.Id, "ln_x", "place", Args(("slot", "c1"), ("screenPoint", "left")));

        CoreStageFoldResult after = FoldOf(editor, node, "ln_x");

        Assert.DoesNotContain(after.CoreState!.Unhandled, item => item.Command.Name == "place");
        Assert.DoesNotContain(after.State.Unhandled, item => item.CommandName == "place");
        Assert.NotEqual(
            before.X,
            after.CoreState!.Nodes.GetState("c1/CharSlot_Track_Focus").AnchoredPosition.X);
    }

    [Fact]
    public void 팝오버_depth는_코어_스케일을_실제로_바꾼다()
    {
        (ProjectEditor editor, PresentationNode node) = BuildEditor();
        StageCharacter(editor, node, "c1", "parkeunseol");

        // 캐릭터 팝오버의 깊이 행과 같은 호출.
        PresentationStageActions.Apply(
            editor, Catalog, node.Id, "ln_x", "size", Args(("slot", "c1"), ("depth", "close")));

        CoreStageFoldResult close = FoldOf(editor, node, "ln_x");
        Assert.DoesNotContain(close.CoreState!.Unhandled, item => item.Command.Name == "size");
        Assert.DoesNotContain(close.State.Unhandled, item => item.CommandName == "size");

        PresentationStageActions.Apply(
            editor, Catalog, node.Id, "ln_x", "size", Args(("slot", "c1"), ("depth", "far")));

        CoreStageFoldResult far = FoldOf(editor, node, "ln_x");

        Vec3 closeScale = close.CoreState!.Nodes.GetState("c1/CharSlot_DepthScale").LocalScale;
        Vec3 farScale = far.CoreState!.Nodes.GetState("c1/CharSlot_DepthScale").LocalScale;
        Assert.True(
            closeScale.X > farScale.X,
            $"close({closeScale.X})는 far({farScale.X})보다 커야 한다");
    }

    [Fact]
    public void 치수_없는_캐릭터도_place와_depth가_움직인다()
    {
        // 소유자의 실제 게임 캐릭터는 덤프 치수에 없다 — 사이징 진단이 남아도
        // 위치·깊이는 코어에 접혀 화면이 움직여야 한다.
        (ProjectEditor editor, PresentationNode node) = BuildEditor();
        StageCharacter(editor, node, "c1", "laru");

        PresentationStageActions.Apply(
            editor, Catalog, node.Id, "ln_x", "place", Args(("slot", "c1"), ("screenPoint", "left")));
        PresentationStageActions.Apply(
            editor, Catalog, node.Id, "ln_x", "size", Args(("slot", "c1"), ("depth", "close")));

        CoreStageFoldResult fold = FoldOf(editor, node, "ln_x");

        Assert.DoesNotContain(fold.State.Unhandled, item => item.CommandName is "place" or "size");
        Assert.NotEqual(
            0f,
            fold.CoreState!.Nodes.GetState("c1/CharSlot_Track_Focus").AnchoredPosition.X);
        Assert.NotEqual(
            1f,
            fold.CoreState!.Nodes.GetState("c1/CharSlot_DepthScale").LocalScale.X);
    }
}
