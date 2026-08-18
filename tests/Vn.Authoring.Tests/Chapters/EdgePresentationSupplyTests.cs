using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 간선에 매달린 연출 노드의 <b>자동 생성</b> (v11 §4) — 규격은
/// <c>docs/work-orders/edge-presentation-orders.md</c>.
///
/// 선례는 대본이다: 에피소드를 만들면 대본 워크북이 생기고 엑셀노드가 판에 선다.
/// 한 층 위에 같은 손놀림 — 간선에 엔딩키나 연출이 적히면 연출 노드가 선다.
/// <b>툴은 자리만 만들고 내용은 사람이 채운다.</b>
/// </summary>
public sealed class EdgePresentationSupplyTests
{
    [Fact]
    public void 엔딩_간선에_연출_노드가_선다()
    {
        (ProjectEditor editor, string fileId) = Board();

        IReadOnlyList<EpisodeSyncService.EdgePresentationLink> links =
            EpisodeSyncService.SupplyEdgePresentations(editor, fileId, Chapter(
                Edge("ep1", "끝", endingKey: "ch_bad")));

        EpisodeSyncService.EdgePresentationLink link = Assert.Single(links);

        Assert.Equal("엔딩 ch_bad", link.NodeName);
        Assert.True(link.NeedsWriteBack, "툴이 이름을 지었으면 워크북에 되써야 한다");
        Assert.Single(Presentations(editor, fileId), node => node.Name == "엔딩 ch_bad");
    }

    [Fact]
    public void 연출만_적힌_간선도_노드가_선다()
    {
        // 엔딩이 아니어도 된다 — 팀장이 말한 "에피소드 사이 트랜지션 연출"이 이 자리다.
        (ProjectEditor editor, string fileId) = Board();

        IReadOnlyList<EpisodeSyncService.EdgePresentationLink> links =
            EpisodeSyncService.SupplyEdgePresentations(editor, fileId, Chapter(
                Edge("ep1", "ep2", presentation: "페이드")));

        EpisodeSyncService.EdgePresentationLink link = Assert.Single(links);

        Assert.Equal("페이드", link.NodeName);
        Assert.False(link.NeedsWriteBack, "사람이 적은 이름은 되쓰지 않는다");
        Assert.Single(Presentations(editor, fileId), node => node.Name == "페이드");
    }

    [Fact]
    public void 아무것도_안_적힌_간선에는_서지_않는다()
    {
        (ProjectEditor editor, string fileId) = Board();

        Assert.Empty(EpisodeSyncService.SupplyEdgePresentations(editor, fileId, Chapter(
            Edge("ep1", "ep2"))));

        Assert.Empty(Presentations(editor, fileId));
    }

    [Fact]
    public void 두_번_불러도_노드가_하나다()
    {
        // 동기화는 저장마다 돈다 — 멱등이 아니면 판이 같은 노드로 뒤덮인다.
        (ProjectEditor editor, string fileId) = Board();
        ChapterGraphModel chapter = Chapter(Edge("ep1", "끝", endingKey: "ch_bad"));

        EpisodeSyncService.SupplyEdgePresentations(editor, fileId, chapter);
        EpisodeSyncService.SupplyEdgePresentations(editor, fileId, chapter);

        Assert.Single(Presentations(editor, fileId));
    }

    [Fact]
    public void 이름이_이미_적혀_있으면_그것을_쓴다()
    {
        // 되쓰기가 한 번 끝난 뒤의 모습 — 이름이 있으니 다시 짓지 않는다.
        (ProjectEditor editor, string fileId) = Board();

        IReadOnlyList<EpisodeSyncService.EdgePresentationLink> links =
            EpisodeSyncService.SupplyEdgePresentations(editor, fileId, Chapter(
                Edge("ep1", "끝", endingKey: "ch_bad", presentation: "내가 지은 이름")));

        Assert.Equal("내가 지은 이름", Assert.Single(links).NodeName);
        Assert.False(Assert.Single(links).NeedsWriteBack);
    }

    [Fact]
    public void 선택지_간선은_문구로_행을_짚는다()
    {
        // 간선 신원이 (출발, 도착, 문구)라, 되쓸 때 문구가 없으면 어느 행인지 모른다.
        (ProjectEditor editor, string fileId) = Board();

        IReadOnlyList<EpisodeSyncService.EdgePresentationLink> links =
            EpisodeSyncService.SupplyEdgePresentations(editor, fileId, Chapter(
                Edge("ep1", "끝", label: "이대로 떠난다", endingKey: "ch_bad"),
                Edge("ep1", "끝", endingKey: "ch_bad")));

        Assert.Equal("이대로 떠난다", links[0].MatchOptionLabel);
        Assert.Null(links[1].MatchOptionLabel);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static (ProjectEditor Editor, string FileId) Board()
    {
        var editor = new ProjectEditor(new StoryProject { Title = "v11" });
        return (editor, editor.AddStoryFile("ch01").Id);
    }

    private static IEnumerable<PresentationNode> Presentations(ProjectEditor editor, string fileId) =>
        editor.Project.FindFile(fileId)!.Nodes.OfType<PresentationNode>();

    private static ChapterEdge Edge(
        string from,
        string to,
        string? label = null,
        string? endingKey = null,
        string? presentation = null) =>
        new(from, to, label, null, HideWhenLocked: false, null, 2)
        {
            Kind = label is null ? EdgeKind.Auto : EdgeKind.Choice,
            EndingKey = endingKey,
            PresentationNodeName = presentation
        };

    private static ChapterGraphModel Chapter(params ChapterEdge[] edges) => new(
        "ch01",
        string.Empty,
        [
            new ChapterEpisode("ep1", "첫", "", "Main", "ep1", 0, 0, null, null, 2),
            new ChapterEpisode("ep2", "둘", "", "Main", "ep2", 1, 0, null, null, 3),
            new ChapterEpisode("끝", "마지막", "", "Main", "끝", 2, 0, null, null, 4)
        ],
        edges,
        [],
        [],
        [],
        []);
}
