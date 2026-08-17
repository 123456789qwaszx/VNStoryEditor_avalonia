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
    public void 설정노드가_챕터_스탯을_배정하면_경고한다()
    {
        // 후보에서 빼는 것만으로는 안 된다 — 변수 칸은 자유 입력이라 손으로 `trust`를
        // 적을 수 있고, 설정노드의 배정은 Set_ 노드 본문이 되어 실제로 <<set>>이 나간다.
        // 대사 줄만 훑던 검사가 이 길을 놓치고 있었다 (2026-08-17).
        Board board = BuildBoard();
        board.Editor.SetAssignments(board.Own.Id,
        [
            new VariableAssignment { Variable = "trust", Value = "1" },
            new VariableAssignment { Variable = "mood", Value = "1" } // 작가 변수 — 조용해야 한다
        ]);

        var chapter = new ChapterGraphModel(
            "ch01", "ch01.xlsx",
            episodes: [],
            edges: [],
            conditions: [],
            stats: [new ChapterStat("trust", "신뢰", 0, 0, 10, 2)],
            fixtures: [],
            diagnostics: []);

        IReadOnlyList<ChapterDiagnostic> warnings = EpisodeSyncService.WarnFreeNodeStatWrites(
            board.Editor, board.Project.Files.Single().Id, chapter);

        ChapterDiagnostic warning = Assert.Single(warnings);
        Assert.Equal(ChapterDiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("작가 조건", warning.Message);   // 어느 설정노드인지
        Assert.Contains("trust", warning.Message);
        Assert.Contains("간선", warning.Message);        // 어디가 제자리인지
        Assert.DoesNotContain("mood", warning.Message);
    }

    [Fact]
    public void 동기화는_대사노드마다_링크를_잇지_않는다()
    {
        // 2026-08-17 — 공급 범위가 판(챕터) 전체가 되면서 배관이 사라졌다. 노드를 만들 때마다
        // 링크를 다시 잇지 않아도 조건이 보인다.
        Board board = BuildBoard();
        StoryFile file = board.Project.Files.Single();

        var chapter = new ChapterGraphModel(
            "ch01", "ch01.xlsx",
            episodes: [],
            edges: [],
            conditions:
            [
                new ChapterCondition("신뢰높음", "trust >= 3", null,
                    [new ConditionTerm(ConditionTermKind.StatComparison, "trust", ConditionComparison.AtLeast, 3)],
                    IsValid: true, SourceRow: 2)
            ],
            stats: [new ChapterStat("trust", "신뢰", 0, 0, 10, 2)],
            fixtures: [],
            diagnostics: []);

        int linksBefore = board.Project.Links.Count;
        EpisodeSyncService.SupplyChapterConditionsToBoard(
            board.Editor, GameDefinition.Empty, file.Id, chapter);

        Assert.Equal(linksBefore, board.Project.Links.Count);

        // 링크가 없어도 그 판의 대사노드는 챕터 조건을 본다(고르지는 못한다 — A계층).
        AvailableConditionCatalog catalog = AvailableConditionResolver.Resolve(
            board.Project, board.Dialogue.Id);
        Assert.Contains(catalog.Conditions, item =>
            item.SourceKind == AvailableConditionSourceKind.ChapterLayer);
    }

    [Fact]
    public void 챕터마다_설정_노드_하나가_상시로_선다()
    {
        // 2026-08-17 소유자 — "조건 노드라기보다는 챕터별로 자동으로 저장되는 컨트롤러에
        // 가까운 것 같아. 챕터 하나에 저런 설정을 담아두는 노드가 상시로 뜨는거지."
        var project = new StoryProject();
        var file = new StoryFile("sf_ch01", "ch01");
        project.Files.Add(file);
        var editor = new ProjectEditor(project);

        SetNode first = editor.EnsureChapterSettingsNode(file.Id);
        Assert.Equal("ch01 설정", first.Name);

        // 멱등이다 — 두 번 불러도 하나뿐이다.
        Assert.Same(first, editor.EnsureChapterSettingsNode(file.Id));
        Assert.Single(file.Nodes.OfType<SetNode>());

        // A계층 공급 노드는 세지 않는다 — 그건 기획자 자료를 나르는 배관이라, 그것만
        // 있는 판에도 작가의 설정 노드가 따로 선다.
        var other = new StoryFile("sf_ch02", "ch02");
        project.Files.Add(other);
        editor.AddSetNode(other.Id, name: EpisodeSyncService.ConditionSupplyNodeName("ch02"));

        SetNode writerNode = editor.EnsureChapterSettingsNode(other.Id);
        Assert.Equal("ch02 설정", writerNode.Name);
        Assert.Equal(2, other.Nodes.OfType<SetNode>().Count());
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
