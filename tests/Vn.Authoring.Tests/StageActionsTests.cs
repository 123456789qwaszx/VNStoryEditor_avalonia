using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// W20 직접 조작. 클릭·드래그가 만드는 것은 언제나 ProjectEditor를 지나는 보통의
/// 커맨드 편집이고 — 같은 종류는 수정, 시퀀스는 개별 커맨드, 되돌리기 한 번 원복.
/// </summary>
public class StageActionsTests
{
    private static readonly PresentationCommandCatalog Catalog = PresentationCommandCatalog.Default;

    private static (ProjectEditor Editor, PresentationNode Node) BuildEditor()
    {
        var project = new StoryProject();
        var file = new StoryFile("sf_w20", "테스트", "story/w20.vnstory.json");
        project.Files.Add(file);
        var editor = new ProjectEditor(project);
        PresentationNode node = editor.AddPresentationNode(file.Id, name: "연출");
        return (editor, node);
    }

    private static Dictionary<string, string> Args(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static MiniStageState FoldOf(PresentationNode node)
    {
        PresentationResultCommand[] commands = node.FindBinding("ln_x")?.Commands
            .Select(command => new PresentationResultCommand(command.Id, command.DefinitionId, command.Arguments))
            .ToArray() ?? [];

        return MiniStageFold.Fold(
            Catalog,
            [],
            [new MiniStageFoldLine("ln_x", false, commands)]);
    }

    [Fact]
    public void 표정_클릭은_face_swap을_만들고_두_번째는_수정한다()
    {
        (ProjectEditor editor, PresentationNode node) = BuildEditor();

        PresentationStageActions.Applied first = PresentationStageActions.Apply(
            editor, Catalog, node.Id, "ln_x", "face_swap", Args(("slot", "c1"), ("emotion", "5")));

        Assert.False(first.Updated);
        Assert.Equal("char_rig_presentation.face_swap", first.Command.DefinitionId);

        PresentationStageActions.Applied second = PresentationStageActions.Apply(
            editor, Catalog, node.Id, "ln_x", "face_swap", Args(("slot", "c1"), ("emotion", "7")));

        // 같은 라인·같은 대상 → 중복 추가가 아니라 값 교체다.
        Assert.True(second.Updated);
        Assert.Same(first.Command, second.Command);
        Assert.Equal("7", first.Command.Arguments["emotion"]);
        Assert.Single(node.FindBinding("ln_x")!.Commands);

        // 다른 슬롯은 별개의 연출이다.
        PresentationStageActions.Apply(
            editor, Catalog, node.Id, "ln_x", "face_swap", Args(("slot", "c2"), ("emotion", "3")));
        Assert.Equal(2, node.FindBinding("ln_x")!.Commands.Count);
    }

    [Fact]
    public void 수정도_되돌리기_한_번으로_원복된다()
    {
        (ProjectEditor editor, PresentationNode node) = BuildEditor();

        PresentationStageActions.Apply(
            editor, Catalog, node.Id, "ln_x", "face_swap", Args(("slot", "c1"), ("emotion", "5"), ("duration", "4fr")));
        PresentationStageActions.Apply(
            editor, Catalog, node.Id, "ln_x", "face_swap", Args(("slot", "c1"), ("emotion", "7"), ("duration", "10fr")));

        editor.Undo(); // 인자 두 개가 함께 바뀌었어도 한 단계다

        PresentationCommandInstance command = editor.Project
            .FindPresentation(node.Id)!.FindBinding("ln_x")!.Commands.Single();
        Assert.Equal("5", command.Arguments["emotion"]);
        Assert.Equal("4fr", command.Arguments["duration"]);
    }

    [Fact]
    public void 캐스팅_시퀀스는_개별_커맨드_3개가_그대로_보인다()
    {
        (ProjectEditor editor, PresentationNode node) = BuildEditor();

        IReadOnlyList<PresentationCommandInstance> added = PresentationStageActions.ApplyCastingSequence(
            editor, Catalog, MiniStageState.Empty, node.Id, "ln_x", "c1", "laru", emotionKey: "2");

        // 매크로가 아니다 — slot, cast, fade_in이 바인딩에 개별로 남는다.
        Assert.Equal(
            ["char_rig_cast.slot", "char_rig_cast.cast", "char_rig_presentation.fade_in"],
            added.Select(command => command.DefinitionId));
        Assert.Equal(
            added.Select(command => command.Id),
            node.FindBinding("ln_x")!.Commands.Select(command => command.Id));

        // 폴드도 그 라인에서 캐릭터가 보인다고 말한다.
        MiniStageState state = FoldOf(node);
        Assert.True(state.Slots["c1"].Visible);
        Assert.Equal("laru", state.Slots["c1"].CharacterId);

        // 조작 하나 = 되돌리기 한 번.
        editor.Undo();
        Assert.Null(editor.Project.FindPresentation(node.Id)!.FindBinding("ln_x"));
    }

    [Fact]
    public void 이미_아는_슬롯이면_slot_커맨드를_생략한다()
    {
        (ProjectEditor editor, PresentationNode node) = BuildEditor();
        MiniStageState state = MiniStageState.Empty with
        {
            Slots = new Dictionary<string, MiniStageSlot>(StringComparer.Ordinal)
            {
                ["c1"] = new(null, null, null, Visible: false, Mirrored: false)
            }
        };

        IReadOnlyList<PresentationCommandInstance> added = PresentationStageActions.ApplyCastingSequence(
            editor, Catalog, state, node.Id, "ln_x", "c1", "willo");

        Assert.Equal(
            ["char_rig_cast.cast", "char_rig_presentation.fade_in"],
            added.Select(command => command.DefinitionId));
    }

    [Fact]
    public void 배경_선택은_리그_유무로_bg_spawn과_bg_sprite를_가른다()
    {
        (string command, IReadOnlyDictionary<string, string> arguments) =
            PresentationStageActions.BackgroundCommandFor(MiniStageState.Empty, "office");
        Assert.Equal("bg_spawn", command);
        Assert.Equal("bg0", arguments["rigKey"]);
        Assert.Equal("office", arguments["spriteKey"]);

        MiniStageState withRig = MiniStageState.Empty with { BackgroundRigKey = "bg1", BackgroundKey = "office" };
        (command, arguments) = PresentationStageActions.BackgroundCommandFor(withRig, "street_night");
        Assert.Equal("bg_sprite", command);
        Assert.Equal("bg1", arguments["rigKey"]);
        Assert.Equal("street_night", arguments["spriteKey"]);
    }

    [Fact]
    public void 초상화_드래그의_커맨드는_무대_위_여부가_가른다()
    {
        var slots = new Dictionary<string, MiniStageSlot>(StringComparer.Ordinal)
        {
            ["c1"] = new("laru", "a", "01", Visible: true, Mirrored: false),
            ["c2"] = new("willo", "a", "01", Visible: false, Mirrored: false)
        };
        MiniStageState state = MiniStageState.Empty with { Slots = slots };

        Assert.Equal(("face_swap", "c1"), PresentationStageActions.FaceCommandFor(state, "laru"));
        Assert.Equal(("face", "c2"), PresentationStageActions.FaceCommandFor(state, "willo"));
        Assert.Null(PresentationStageActions.FaceCommandFor(state, "nobody")); // 캐스팅 시퀀스 필요
    }

    [Fact]
    public void Setup_조작은_노드_Setup에_담기고_같은_대상은_수정된다()
    {
        (ProjectEditor editor, PresentationNode node) = BuildEditor();

        // 슬롯 생성 — 어느 라인에서 조작했든 장면 준비는 Setup에 속한다.
        PresentationStageActions.Applied slot = PresentationStageActions.ApplyToSetup(
            editor, Catalog, node.Id, "slot", Args(("slotKey", "c1")));

        Assert.False(slot.Updated);
        Assert.Single(node.SetupCommands);
        Assert.Empty(node.Bindings); // 라인 바인딩에는 아무것도 없다

        // 같은 슬롯을 다시 만들면 커맨드가 쌓이지 않는다.
        PresentationStageActions.ApplyToSetup(
            editor, Catalog, node.Id, "slot", Args(("slotKey", "c1")));
        Assert.Single(node.SetupCommands);

        // 캐스팅 → Setup. 같은 슬롯 재캐스팅은 값 교체다.
        PresentationStageActions.ApplyToSetup(
            editor, Catalog, node.Id, "cast", Args(("slot", "c1"), ("characterKey", "laru")));
        PresentationStageActions.Applied recast = PresentationStageActions.ApplyToSetup(
            editor, Catalog, node.Id, "cast",
            Args(("slot", "c1"), ("characterKey", "laru"), ("variantKey", "b"), ("emotionKey", "3")));

        Assert.True(recast.Updated);
        Assert.Equal(2, node.SetupCommands.Count); // slot + cast — 세 번 조작해도 두 개다
        Assert.Equal("b", recast.Command.Arguments["variantKey"]);

        // 다른 슬롯의 캐스팅은 별개다.
        PresentationStageActions.ApplyToSetup(
            editor, Catalog, node.Id, "slot", Args(("slotKey", "c2")));
        PresentationStageActions.ApplyToSetup(
            editor, Catalog, node.Id, "cast", Args(("slot", "c2"), ("characterKey", "willo")));
        Assert.Equal(4, node.SetupCommands.Count);
    }

    [Fact]
    public void 표시_상태_전환은_반대_방향_fade를_걷어낸다()
    {
        (ProjectEditor editor, PresentationNode node) = BuildEditor();

        // 숨김 → 표시 → 숨김을 반복해도 라인에는 마지막 방향 하나만 남는다.
        PresentationStageActions.ApplyVisibility(editor, Catalog, node.Id, "ln_x", "c1", visible: false);
        Assert.Equal(
            ["char_rig_presentation.fade_out"],
            node.FindBinding("ln_x")!.Commands.Select(command => command.DefinitionId));

        PresentationStageActions.ApplyVisibility(editor, Catalog, node.Id, "ln_x", "c1", visible: true);
        Assert.Equal(
            ["char_rig_presentation.fade_in"],
            node.FindBinding("ln_x")!.Commands.Select(command => command.DefinitionId));

        PresentationStageActions.ApplyVisibility(editor, Catalog, node.Id, "ln_x", "c1", visible: false);
        Assert.Equal(
            ["char_rig_presentation.fade_out"],
            node.FindBinding("ln_x")!.Commands.Select(command => command.DefinitionId));

        // 폴드도 마지막 선택을 말한다.
        Assert.False(FoldOf(node).Slots["c1"].Visible);

        // 다른 슬롯의 fade는 건드리지 않는다.
        PresentationStageActions.ApplyVisibility(editor, Catalog, node.Id, "ln_x", "c2", visible: true);
        Assert.Equal(2, node.FindBinding("ln_x")!.Commands.Count);
    }

    [Fact]
    public void 위치_지정은_place_하나가_수정된다()
    {
        (ProjectEditor editor, PresentationNode node) = BuildEditor();

        PresentationStageActions.Apply(
            editor, Catalog, node.Id, "ln_x", "place", Args(("slot", "c1"), ("screenPoint", "left")));
        PresentationStageActions.Applied moved = PresentationStageActions.Apply(
            editor, Catalog, node.Id, "ln_x", "place", Args(("slot", "c1"), ("screenPoint", "right")));

        // 위치를 여러 번 바꿔도 place는 슬롯당 하나다.
        Assert.True(moved.Updated);
        Assert.Single(node.FindBinding("ln_x")!.Commands);
        Assert.Equal("right", moved.Command.Arguments["screenPoint"]);
    }

    [Fact]
    public void 폴드가_bg_리그_키를_기억한다()
    {
        MiniStageState state = MiniStageFold.Fold(
            Catalog,
            [
                new PresentationResultCommand(
                    Identifier.PresentationCommand(),
                    "background.bg_spawn",
                    new Dictionary<string, string> { ["rigKey"] = "bg7", ["spriteKey"] = "office" })
            ],
            []);

        Assert.Equal("bg7", state.BackgroundRigKey);
        Assert.Equal("office", state.BackgroundKey);
    }
}
