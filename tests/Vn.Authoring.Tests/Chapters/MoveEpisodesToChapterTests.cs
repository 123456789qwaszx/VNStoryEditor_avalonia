using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>장면째 다른 챕터로</b> (2026-09-16 소유자 — 탐색기에서 끌어다 놓기).
///
/// ⛔ 못 박는 것 중 제일 중요한 것은 <b>글이 따라간다</b>는 것이다. 대본은 대사 노드가 들고
/// 있으므로 노드가 안 따라가면 에피소드만 옮겨 앉고 원고는 옛 판에 고아로 남는다.
///
/// ⚠ 그리고 <b>가로지르게 된 간선은 걷힌다</b> — 간선은 챕터 안의 길이라 두 챕터를 이을 수
/// 없다. 조용히 지우지 않고 몇 개였는지 돌려준다.
/// </summary>
public sealed class MoveEpisodesToChapterTests
{
    [Fact]
    public void 장면의_에피소드와_안쪽_간선이_함께_간다()
    {
        (ProjectEditor editor, _) = World();

        int cut = editor.MoveEpisodesToChapter("ch01", ["a", "b"], "ch02");

        ChapterDocument from = editor.FindChapter("ch01")!;
        ChapterDocument to = editor.FindChapter("ch02")!;

        Assert.Equal(["root"], from.Episodes.Select(episode => episode.EpisodeId));
        Assert.Equal(["far", "a", "b"], to.Episodes.Select(episode => episode.EpisodeId));

        // a→b는 장면 안의 길이라 함께 갔다.
        Assert.Contains(to.Edges, edge => edge.FromEpisodeId == "a" && edge.ToEpisodeId == "b");

        // root→a는 이제 챕터를 가로지른다 — 걷혔고, 그 사실을 셌다.
        Assert.Equal(1, cut);
        Assert.Empty(from.Edges);
    }

    [Fact]
    public void 장면_이름이_따라간다()
    {
        // 장면째 옮기는 것이니 장면이 그대로여야 한다 — 이름이 바뀌면 그건 다른 장면이다.
        (ProjectEditor editor, _) = World();

        editor.MoveEpisodesToChapter("ch01", ["a", "b"], "ch02");

        Assert.All(
            editor.FindChapter("ch02")!.Episodes.Where(episode => episode.EpisodeId != "far"),
            episode => Assert.Equal("shared", episode.SceneId));
    }

    [Fact]
    public void 대사_노드가_따라가서_글이_안_고아가_된다()
    {
        // ⛔ 여기가 이 조각의 관문이다. 대본은 노드가 들고 있다 — 노드가 남으면 [대본] 탭은
        //    옮긴 에피소드의 글을 못 찾고, 옛 판에는 챕터 밖 유령이 남는다.
        (ProjectEditor editor, StoryProject project) = World();

        editor.MoveEpisodesToChapter("ch01", ["a", "b"], "ch02");

        StoryFile from = editor.Project.Files.Single(file => file.Name == "ch01");
        StoryFile to = editor.Project.Files.Single(file => file.Name == "ch02");

        Assert.Equal(["root"], from.Nodes.OfType<DialogueNode>().Select(node => node.ExcelEpisodeId));
        Assert.Equal(["far", "a", "b"], to.Nodes.OfType<DialogueNode>().Select(node => node.ExcelEpisodeId));
    }

    [Fact]
    public void 도착에_같은_Id가_있으면_하나도_안_옮긴다()
    {
        // 겹치는 것만 빼고 옮기면 그 장면이 두 챕터에 갈려 앉는다.
        (ProjectEditor editor, _) = World();
        editor.AddEpisode("ch02", "a", title: "남의 a", 0, 0);

        Assert.Throws<InvalidOperationException>(
            () => editor.MoveEpisodesToChapter("ch01", ["a", "b"], "ch02"));

        Assert.Equal(["root", "a", "b"], editor.FindChapter("ch01")!.Episodes.Select(item => item.EpisodeId));
    }

    [Fact]
    public void 같은_챕터_안이면_장면만_다시_긋는다()
    {
        // 에피소드를 같은 챕터의 다른 장면에 놓은 경우다 — 옮길 것이 없다.
        (ProjectEditor editor, _) = World();

        int cut = editor.MoveEpisodesToChapter("ch01", ["a"], "ch01", sceneId: "opening");

        Assert.Equal(0, cut);
        Assert.Equal("opening", editor.FindChapter("ch01")!.Episodes.Single(e => e.EpisodeId == "a").SceneId);

        // 간선은 그대로다 — 장면이 갈렸을 뿐 챕터를 가로지르지 않는다.
        Assert.Equal(2, editor.FindChapter("ch01")!.Edges.Count);
    }

    [Fact]
    public void 되돌리기_한_번에_통째로_돌아온다()
    {
        // 에피소드·간선·노드가 한 번의 변경이어야 한다 — 갈라 두면 되돌린 뒤 에피소드는
        // 돌아왔는데 그 글은 저쪽 판에 남는 상태가 된다.
        (ProjectEditor editor, StoryProject project) = World();

        editor.MoveEpisodesToChapter("ch01", ["a", "b"], "ch02");
        editor.Undo();

        Assert.Equal(["root", "a", "b"], editor.FindChapter("ch01")!.Episodes.Select(item => item.EpisodeId));
        Assert.Equal(2, editor.FindChapter("ch01")!.Edges.Count);
        // ⚠ 되돌리기는 프로젝트를 통째로 갈아 끼운다 — 들고 있던 참조가 아니라 편집기에게 묻는다.
        Assert.Equal(
            ["root", "a", "b"],
            editor.Project.Files.Single(file => file.Name == "ch01")
                .Nodes.OfType<DialogueNode>()
                .Where(node => node.ExcelEpisodeId is not null)
                .Select(node => node.ExcelEpisodeId));
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// ch01: root(opening) → a(shared) → b(shared) · ch02: far(other).
    /// 판마다 에피소드와 짝이 되는 대사 노드가 선다.
    /// </summary>
    private static (ProjectEditor Editor, StoryProject Project) World()
    {
        var project = new StoryProject();
        var editor = new ProjectEditor(project);

        editor.EnsureChapter("ch01");
        editor.EnsureChapter("ch02");

        string first = editor.EnsureChapterBoard("ch01");
        string second = editor.EnsureChapterBoard("ch02");

        editor.AddEpisode("ch01", "root", title: "시작", 0, 0, sceneId: "opening");
        editor.AddNextEpisode("ch01", "root", "a", title: "가", 260, 0, optionLabel: "가자", sceneId: "shared");
        editor.AddNextEpisode("ch01", "a", "b", title: "나", 520, 0, optionLabel: "다음", sceneId: "shared");
        editor.AddEpisode("ch02", "far", title: "저쪽", 0, 0, sceneId: "other");

        foreach (string episodeId in (string[])["root", "a", "b"])
        {
            editor.AddDialogueNode(first, name: episodeId).ExcelEpisodeId = episodeId;
        }

        editor.AddDialogueNode(second, name: "far").ExcelEpisodeId = "far";

        return (editor, project);
    }
}
