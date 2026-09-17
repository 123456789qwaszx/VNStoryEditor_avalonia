using Vn.Authoring.Chapters;
using Vn.Authoring.Graph;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>접힌 판 → 장면 → 노드</b> (R6 S-3 · `docs/plans/R6-explorer.md` §5).
///
/// ⛔ 못 박는 것 중 제일 중요한 것은 <b>행 번호가 장면 머리글을 센다</b>는 것이다.
/// 간선 끝점의 Y가 <c>머리글높이 + 행번호 × 행높이</c>라, 안 세면 머리글 아래 노드의
/// <b>간선이 한 칸씩 위로 붙는다</b> — 화면은 멀쩡해 보이고 선만 어긋난다.
/// </summary>
public sealed class CollapsedSceneGroupTests
{
    [Fact]
    public void 장면별로_묶이고_자유_씬은_장면_밖으로_간다()
    {
        StoryProject project = Project(
            ("opening", "ep01"), ("opening", "ep02"), ("classroom", "ep03"));

        AddFreeNode(project, "곁가지");

        CollapsedFileProjection proxy = Proxy(project);

        Assert.Equal(
            ["opening", "classroom", "장면 밖"],
            proxy.Scenes.Select(scene => scene.DisplayName));

        Assert.Equal(["ep01", "ep02"], proxy.Scenes[0].Entries.Select(entry => entry.NodeName));
        Assert.Equal(["곁가지"], proxy.Scenes[2].Entries.Select(entry => entry.NodeName));

        // 평평하게 편 것이 곧 행 순서다 — 묶으면서 순서가 바뀌므로 둘이 같아야 한다.
        Assert.Equal(
            proxy.Scenes.SelectMany(scene => scene.Entries).Select(entry => entry.NodeId),
            proxy.Nodes.Select(entry => entry.NodeId));
    }

    [Fact]
    public void 행_번호가_장면_머리글을_센다()
    {
        // ⛔ 여기가 이 조각의 관문이다. 머리글은 화면에서 <b>한 행을 차지</b>하므로
        //    그 아래 노드의 행 번호가 하나씩 밀려야 간선이 제자리에 붙는다.
        StoryProject project = Project(("opening", "ep01"), ("classroom", "ep02"));

        GraphProjection projection = GraphProjectionBuilder.Build(project, new HashSet<string>(StringComparer.Ordinal));
        var proxy = projection.Items.OfType<CollapsedFileProjection>().Single();

        Assert.All(proxy.Scenes, scene => Assert.True(scene.HasHeader));

        // 머리글(0) · ep01(1) · 머리글(2) · ep02(3)
        Assert.Equal(1, RowOf(projection, "ep01"));
        Assert.Equal(3, RowOf(projection, "ep02"));
    }

    [Fact]
    public void 장면ID를_하나도_안_적은_챕터는_묶음이_하나고_머리글이_없다()
    {
        // 대본 탭 트리와 <b>같은 규칙</b>이다(규격 §2) — 그대로 묶으면 에피소드 수만큼
        // 머리글이 생겨 프록시가 통째로 노이즈가 된다.
        StoryProject project = Project((null, "ep01"), (null, "ep02"));

        CollapsedFileProjection proxy = Proxy(project);
        CollapsedSceneGroup only = Assert.Single(proxy.Scenes);

        Assert.False(only.HasHeader);
        Assert.Equal(["ep01", "ep02"], only.Entries.Select(entry => entry.NodeName));

        // 머리글이 없으니 행 번호도 안 밀린다.
        GraphProjection projection = GraphProjectionBuilder.Build(project, new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(0, RowOf(projection, "ep01"));
        Assert.Equal(1, RowOf(projection, "ep02"));
    }

    [Fact]
    public void 챕터를_못_찾는_판은_묶지_않는다()
    {
        // 작가의 자유 판 — 챕터가 아니므로 장면 경계가 없다.
        var project = new StoryProject();
        var file = new StoryFile { Name = "작가의 판" };
        project.Files.Add(file);
        file.Nodes.Add(new DialogueNode(name: "아무거나"));

        CollapsedSceneGroup only = Assert.Single(Proxy(project).Scenes);

        Assert.False(only.HasHeader);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <summary>그 노드가 선 화면 행 번호 — 투영이 간선 끝에 쓰는 그 값이다.</summary>
    private static int RowOf(GraphProjection projection, string nodeName)
    {
        CollapsedFileProjection proxy = projection.Items.OfType<CollapsedFileProjection>().Single();

        int row = 0;

        foreach (CollapsedSceneGroup scene in proxy.Scenes)
        {
            if (scene.HasHeader)
            {
                row++;
            }

            foreach (CollapsedNodeEntry entry in scene.Entries)
            {
                if (string.Equals(entry.NodeName, nodeName, StringComparison.Ordinal))
                {
                    return row;
                }

                row++;
            }
        }

        return -1;
    }

    private static CollapsedFileProjection Proxy(StoryProject project) =>
        GraphProjectionBuilder.Build(project, new HashSet<string>(StringComparer.Ordinal)).Items
            .OfType<CollapsedFileProjection>()
            .Single();

    private static void AddFreeNode(StoryProject project, string name) =>
        project.Files[0].Nodes.Add(new DialogueNode(name: name));

    /// <summary>챕터 하나와 그 판 — 판 이름이 챕터 Id인 것이 둘을 잇는 규약이다.</summary>
    private static StoryProject Project(params (string? SceneId, string EpisodeId)[] episodes)
    {
        var project = new StoryProject();
        var chapter = new ChapterDocument { ChapterId = "ch01" };
        var file = new StoryFile { Name = "ch01" };

        project.Chapters.Add(chapter);
        project.Files.Add(file);

        for (int index = 0; index < episodes.Length; index++)
        {
            (string? sceneId, string episodeId) = episodes[index];

            chapter.Episodes.Add(new ChapterEpisode(
                episodeId, episodeId, string.Empty, episodeId, 0, 0, null, index + 2)
            {
                SceneId = sceneId
            });

            file.Nodes.Add(new DialogueNode(name: episodeId) { MarkedEpisodeId = episodeId });
        }

        return project;
    }
}
