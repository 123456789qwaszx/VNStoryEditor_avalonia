using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Graph;
using Vn.Authoring.Model;

// ⚠ `Vn.Authoring.Tests.Graph` 네임스페이스를 만들면 안 된다 — 다른 테스트가 쓰는
// `Graph.GraphProjection`이 그쪽으로 붙어 어셈블리가 통째로 안 선다(2026-09-16에 겪었다).
namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>노드 아래의 선택지 칸</b> (R7 P-1 · `docs/plans/R7.md`).
///
/// ⛔ 이 칸이 지고 있는 것은 <b>챕터 간선</b>이다 — 연출 층의 값이 아니다. 이어 붙는 순간
/// 고쳐지는 것이 <c>ChapterDocument.Edges</c>이고, 그것이 내보내기·도달성 증명·런타임이
/// 보는 그 값이다(§2 ①). 정본을 늘리지 않는 것이 이 작업의 첫 줄이다.
/// </summary>
public sealed class ChoiceSlotProjectionTests
{
    [Fact]
    public void 아직_아무_길도_없으면_빈_칸이_셋이다()
    {
        // 소유자: "선택지의 갯수는 기본 3개로 고정해서 미리 포트를 만들어둬" (§2 ③).
        IReadOnlyList<GraphChoicePort> slots = SlotsOf(World(), "root");

        Assert.Equal(3, slots.Count);
        Assert.All(slots, slot => Assert.True(slot.IsEmpty));
        Assert.Equal([0, 1, 2], slots.Select(slot => slot.Slot));
    }

    [Fact]
    public void 이미_있는_간선이_앞칸부터_찬다()
    {
        ProjectEditor editor = World();
        editor.AddEdge("ch01", "root", "a", optionLabel: "왼쪽으로");

        IReadOnlyList<GraphChoicePort> slots = SlotsOf(editor, "root");

        Assert.Equal(3, slots.Count);
        Assert.Equal("왼쪽으로", slots[0].Label);
        Assert.Equal("a", slots[0].ToEpisodeId);
        Assert.False(slots[0].IsEmpty);
        Assert.True(slots[1].IsEmpty);
    }

    [Fact]
    public void 간선이_셋을_넘으면_넘는_대로_전부_낸다()
    {
        // ⛔ 3은 <b>새로 만드는 칸</b>의 수이지 상한이 아니다. 엑셀에서 넷을 만든 챕터의
        //    넷째를 화면이 숨기면 사람은 사라진 줄 안다 — 그리고 상한을 모델에 박는 것은
        //    v9가 없앤 `선택지수` 칸을 되살리는 일이다.
        ProjectEditor editor = World();

        foreach (string to in (string[])["a", "b", "c", "d"])
        {
            editor.AddEdge("ch01", "root", to, optionLabel: "→" + to);
        }

        IReadOnlyList<GraphChoicePort> slots = SlotsOf(editor, "root");

        Assert.Equal(4, slots.Count);
        Assert.DoesNotContain(slots, slot => slot.IsEmpty);
    }

    [Fact]
    public void 판에_세운_노드는_곧_에피소드라_칸이_선다()
    {
        // ⚠ 뒤집힌 자리다 (R7 P-6 · 결정 ⑤ · 2026-09-17). 전에는 `자유_씬에는_칸이_없다`로,
        //    에피소드가 아닌 대사 노드에는 칸을 안 줬다. 자유 씬이라는 종류가 없어졌으므로
        //    그 규칙은 지킬 것이 없다 — 챕터 판의 대사 노드는 <b>전부</b> 에피소드다.
        ProjectEditor editor = World();
        DialogueNode made = editor.AddDialogueNode(BoardOf(editor), name: "곁가지");

        Assert.Equal("곁가지", made.ExcelEpisodeId);
        Assert.Equal(3, SlotsOf(editor, "곁가지").Count);
    }

    [Fact]
    public void 챕터가_아닌_판에는_칸이_없다()
    {
        var project = new StoryProject();
        var editor = new ProjectEditor(project);

        string fileId = editor.EnsureChapterBoard("작가의 판");   // 같은 이름의 챕터가 없다
        editor.AddDialogueNode(fileId, name: "아무거나");

        Assert.DoesNotContain(Ports(project), port => port.Kind == GraphOutputPortKind.Choice);
    }

    [Fact]
    public void 대본이_아직_없는_에피소드로_가는_길도_산다()
    {
        // ⚠ 대본이 없는 것과 길이 없는 것은 다르다 — 도착 노드를 못 찾아도 간선은 있다.
        ProjectEditor editor = World();
        editor.AddEpisode("ch01", "아직안씀", title: "빈 자리", 0, 0);
        editor.AddEdge("ch01", "root", "아직안씀", optionLabel: "그쪽으로");

        GraphChoicePort slot = SlotsOf(editor, "root")[0];

        Assert.Equal("아직안씀", slot.ToEpisodeId);
        Assert.Null(slot.ToNodeId);
        Assert.False(slot.IsEmpty);
    }

    [Fact]
    public void 자동_길은_문구가_없고_그렇다고_말한다()
    {
        // 자동 길의 문구는 비어야 한다(`AutoEdgeHasChoiceLabel`) — 빈 칸과 헷갈리지 않게
        // 슬롯이 그 사실을 따로 지고 있어야 한다.
        ProjectEditor editor = World();
        editor.FindChapter("ch01")!.Edges.Add(
            new ChapterEdge("root", "a", null, null, null, 0) { Auto = true });

        GraphChoicePort slot = SlotsOf(editor, "root")[0];

        Assert.True(slot.IsAuto);
        Assert.Equal(string.Empty, slot.Label);
        Assert.False(slot.IsEmpty);
    }

    [Fact]
    public void 칸은_어느_챕터의_어느_에피소드에서_나가는지_안다()
    {
        // 끌어다 놓았을 때 `AddEdge(챕터, 출발, …)`를 부를 수 있어야 한다 (P-3).
        GraphChoicePort slot = SlotsOf(World(), "root")[0];

        Assert.Equal("ch01", slot.ChapterId);
        Assert.Equal("root", slot.FromEpisodeId);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static IReadOnlyList<GraphChoicePort> SlotsOf(ProjectEditor editor, string nodeName) =>
        Ports(editor.Project)
            .Where(port => port.Kind == GraphOutputPortKind.Choice &&
                           editor.Project.FindNode(port.NodeId)?.Name == nodeName)
            .Select(port => port.ChoicePort!)
            .ToList();

    private static IEnumerable<GraphOutputPortProjection> Ports(StoryProject project) =>
        // ⚠ 판을 펴야 카드가 선다 — 접힌 판은 프록시 하나로 와서 포트가 없다.
        GraphProjectionBuilder.Build(
                project,
                new HashSet<string>(project.Files.Select(file => file.Id), StringComparer.Ordinal))
            .Items.OfType<ExpandedNodeProjection>()
            .SelectMany(node => node.OutputPorts);

    private static string BoardOf(ProjectEditor editor) => editor.EnsureChapterBoard("ch01");

    /// <summary>ch01 — root · a · b · c · d, 각각 대사 노드가 선 판.</summary>
    private static ProjectEditor World()
    {
        var project = new StoryProject();
        var editor = new ProjectEditor(project);

        editor.EnsureChapter("ch01");
        string fileId = editor.EnsureChapterBoard("ch01");

        foreach (string episodeId in (string[])["root", "a", "b", "c", "d"])
        {
            editor.AddEpisode("ch01", episodeId, title: episodeId, 0, 0);
            editor.AddDialogueNode(fileId, name: episodeId).ExcelEpisodeId = episodeId;
        }

        return editor;
    }
}
