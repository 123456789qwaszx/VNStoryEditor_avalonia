using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// A계층(기획자, 챕터 엑셀)과 B계층(작가, 시나리오 그래프)의 분리 (2026-08-17 소유자:
/// "시나리오 설정노드의 조건·변수·화자와 챕터 엑셀의 그것은 서로 완전히 다른 계층이야").
///
/// 조건은 <b>작가가 고르는 목록에서 빠지되 이미 쓰인 것은 읽힌다</b>. 가르는 규칙은
/// <see cref="EpisodeSyncService.IsConditionSupplyNode"/> 하나뿐이다.
/// </summary>
public sealed class ChapterLayerSeparationTests
{
    private sealed record Board(
        StoryProject Project, ProjectEditor Editor, DialogueNode Dialogue, SetNode Supply, SetNode Own);

    /// <summary>판 이름 = 챕터 Id (1:1). 공급 노드는 그 이름 규약으로 신원이 정해진다.</summary>
    private static Board BuildBoard()
    {
        var project = new StoryProject();
        var file = new StoryFile("sf_ch01", "ch01");
        project.Files.Add(file);
        var editor = new ProjectEditor(project);

        DialogueNode dialogue = editor.AddDialogueNode(file.Id, name: "EP00");

        // A계층 — 동기화가 만드는 공급 노드(이름이 곧 신원).
        SetNode supply = editor.AddSetNode(file.Id, name: EpisodeSyncService.ConditionSupplyNodeName("ch01"));
        editor.AddCondition(supply.Id, "신뢰높음", "$trust >= 3");
        editor.AddSettingsLink(supply.Id, dialogue.Id);

        // B계층 — 작가가 직접 만든 설정노드.
        SetNode own = editor.AddSetNode(file.Id, name: "작가 조건");
        editor.AddCondition(own.Id, "우호적", "$mood == 1");
        editor.AddSettingsLink(own.Id, dialogue.Id);

        return new Board(project, editor, dialogue, supply, own);
    }

    [Fact]
    public void 챕터_조건은_작가가_고르는_목록에서_빠진다()
    {
        Board board = BuildBoard();

        AvailableConditionCatalog catalog = AvailableConditionResolver.Resolve(
            board.Project, board.Dialogue.Id);

        // 둘 다 읽히지만 종류가 갈린다.
        Assert.Equal(
            AvailableConditionSourceKind.ChapterLayer,
            catalog.Conditions.Single(item => item.Name == "신뢰높음").SourceKind);
        Assert.Equal(
            AvailableConditionSourceKind.SetNode,
            catalog.Conditions.Single(item => item.Name == "우호적").SourceKind);

        // 고를 수 있는 것은 작가의 것뿐이다.
        Assert.Equal(["우호적"], catalog.Selectable.Select(item => item.Name));

        // 조건 고르기 목록에도 챕터 조건이 없다.
        IReadOnlyList<ConditionChoice> choices = ConditionChoices.For(
            preceding: null, board.Dialogue, board.Project, GameDefinition.Empty);

        Assert.DoesNotContain(choices, choice => choice.Label.Contains("신뢰높음"));
        Assert.Contains(choices, choice => choice.Label == "우호적");
    }

    [Fact]
    public void 이미_쓰인_챕터_조건은_이름이_보이고_출처가_붙는다()
    {
        // 숨긴다고 "알 수 없는 조건"이 되면 그게 더 나쁘다 — 엑셀노드의 조건라벨이
        // 가리키는 A계층 조건은 이름이 그대로 읽히되, 왜 못 고치는지가 이름에 있다.
        Board board = BuildBoard();

        AvailableCondition chapter = AvailableConditionResolver
            .Resolve(board.Project, board.Dialogue.Id)
            .Conditions.Single(item => item.Name == "신뢰높음");

        Assert.Equal("[기획] 신뢰높음", AvailableConditionResolver.LayeredLabel(chapter));

        AvailableCondition own = AvailableConditionResolver
            .Resolve(board.Project, board.Dialogue.Id)
            .Conditions.Single(item => item.Name == "우호적");

        Assert.Equal("우호적", AvailableConditionResolver.LayeredLabel(own));
    }

    [Fact]
    public void 계층을_가르는_규칙은_이름_규약_하나다()
    {
        Board board = BuildBoard();
        StoryFile file = board.Project.Files.Single();

        Assert.True(EpisodeSyncService.IsConditionSupplyNode(board.Supply, file));
        Assert.False(EpisodeSyncService.IsConditionSupplyNode(board.Own, file));
        Assert.False(EpisodeSyncService.IsConditionSupplyNode(board.Dialogue, file));

        Assert.Equal([board.Supply.Id], EpisodeSyncService.ConditionSupplyNodeIds(board.Project));
    }
}
