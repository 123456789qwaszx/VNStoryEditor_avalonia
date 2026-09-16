using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>판에 남은 옛 자유 씬이 에피소드로 올라온다</b> (R7 P-6 · 결정 ⑤ · 2026-09-17).
///
/// ⛔ 자유 씬은 <b>종류가 아니라 빈자리</b>였다 — 에피소드가 없는 대사 노드. 종류가
/// 없어졌으므로 빈자리도 없어진다.
/// </summary>
public sealed class FreeSceneLiftTests
{
    [Fact]
    public void 에피소드가_없던_카드가_에피소드를_얻는다()
    {
        (ProjectEditor editor, DialogueNode free) = World();

        Assert.Equal(["곁가지"], editor.LiftFreeScenes("ch01"));

        Assert.Equal("곁가지", free.ExcelEpisodeId);
        Assert.Contains(Chapter(editor).Episodes, episode =>
            string.Equals(episode.EpisodeId, "곁가지", StringComparison.Ordinal));
    }

    [Fact]
    public void 올린_카드는_도달_불가로_울지_않는다()
    {
        // ⛔ 들어오는 간선이 없다 — 대본의 갈래가 `<<detour>>`로 부르던 곁가지다.
        //    도달성 증명은 저쪽 런타임의 오라클이라 <b>한 줄도 안 건드린다</b>.
        (ProjectEditor editor, DialogueNode _) = World();

        editor.LiftFreeScenes("ch01");

        Assert.True(Episode(editor, "곁가지").AllowUnreachable);
    }

    [Fact]
    public void 부른_쪽의_장면에_선다()
    {
        // 곁가지는 제 본줄 옆에 있어야 판에서 읽힌다.
        (ProjectEditor editor, DialogueNode free) = World();
        editor.UpdateEpisode("ch01", "root", sceneId: "sc_아침");

        DialogueNode source = Source(editor);
        string lineId = editor.Project.FindScript(source.ScriptId)!.ActiveLines.First().Id;
        source.RequireExtension(lineId).DetourTargetNodeId = free.Id;

        editor.LiftFreeScenes("ch01");

        Assert.Equal("sc_아침", Episode(editor, "곁가지").SceneId);
    }

    [Fact]
    public void 아무도_안_부르면_장면_밖에_선다()
    {
        (ProjectEditor editor, DialogueNode _) = World();

        editor.LiftFreeScenes("ch01");

        Assert.Null(Episode(editor, "곁가지").SceneId);
    }

    [Fact]
    public void 올린_카드에도_선택지_세_칸이_뚫린다()
    {
        // 올리는 까닭이 이것이다 — 칸은 챕터 간선을 쓰고, 간선은 양 끝이 에피소드여야 선다.
        (ProjectEditor editor, DialogueNode free) = World();

        editor.LiftFreeScenes("ch01");

        Assert.Equal(3, Graph.GraphProjectionBuilder
            .Build(editor.Project, new HashSet<string>(editor.Project.Files.Select(file => file.Id)))
            .Items.OfType<Graph.ExpandedNodeProjection>()
            .Single(item => string.Equals(item.NodeId, free.Id, StringComparison.Ordinal))
            .OutputPorts.Count(port => port.Kind == Graph.GraphOutputPortKind.Choice));
    }

    [Fact]
    public void 이름이_같은_카드가_둘이면_하나만_올린다()
    {
        // ⛔ 둘 다 올리면 같은 EpisodeId가 두 번 선다 — 챕터가 통째로 못 읽히는 상태다.
        (ProjectEditor editor, DialogueNode _) = World();
        DialogueNode twin = editor.AddDialogueNode(BoardOf(editor), 0, 0, "곁가지2");
        twin.ExcelEpisodeId = null;
        twin.Name = "곁가지";
        Chapter(editor).Episodes.RemoveAll(episode =>
            string.Equals(episode.EpisodeId, "곁가지2", StringComparison.Ordinal));

        Assert.Single(editor.LiftFreeScenes("ch01"));
        Assert.Single(Chapter(editor).Episodes, episode =>
            string.Equals(episode.EpisodeId, "곁가지", StringComparison.Ordinal));
    }

    [Fact]
    public void 올릴_것이_없으면_아무것도_안_바꾼다()
    {
        // ⚠ 판을 열 때마다 도는 자리다 — 바꿀 것이 없는데 손대면 매번 더럽혀진다.
        (ProjectEditor editor, DialogueNode _) = World();
        editor.LiftFreeScenes("ch01");

        string before = ProjectSnapshotCodec.Encode(editor.Project);

        Assert.Empty(editor.LiftFreeScenes("ch01"));
        Assert.Equal(before, ProjectSnapshotCodec.Encode(editor.Project));
    }

    [Fact]
    public void 되돌리기_한_번에_통째로_돌아간다()
    {
        (ProjectEditor editor, DialogueNode _) = World();
        int episodes = Chapter(editor).Episodes.Count;

        editor.LiftFreeScenes("ch01");
        editor.Undo();

        // ⚠ `Restore`는 `Project`를 통째로 갈아 끼운다 — 되돌린 뒤에 다시 읽는다.
        Assert.Equal(episodes, Chapter(editor).Episodes.Count);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static ChapterDocument Chapter(ProjectEditor editor) =>
        editor.Project.Chapters.Single();

    private static string BoardOf(ProjectEditor editor) =>
        editor.Project.Files.Single(file =>
            string.Equals(file.Name, "ch01", StringComparison.Ordinal)).Id;

    private static ChapterEpisode Episode(ProjectEditor editor, string episodeId) =>
        Chapter(editor).Episodes.Single(episode =>
            string.Equals(episode.EpisodeId, episodeId, StringComparison.Ordinal));

    private static DialogueNode Source(ProjectEditor editor) =>
        editor.Project.EnumerateNodes().OfType<DialogueNode>()
            .First(node => string.Equals(node.Name, "root", StringComparison.Ordinal));

    /// <summary>에피소드 `root`와, 에피소드가 없는 옛 자유 씬 `곁가지`.</summary>
    private static (ProjectEditor Editor, DialogueNode Free) World()
    {
        var project = new StoryProject();
        var editor = new ProjectEditor(project);

        editor.EnsureChapter("ch01");
        string fileId = editor.EnsureChapterBoard("ch01");

        editor.AddEpisode("ch01", "root", title: "root", 0, 0);
        editor.AddDialogueNode(fileId, 0, 0, "root");

        DialogueNode free = editor.AddDialogueNode(fileId, 400, 0, "곁가지");

        // ⚠ 판에 세운 카드는 이제 에피소드를 함께 만든다(P-6) — 옛 프로젝트의 자유 씬을
        //    흉내 내려면 표식과 에피소드를 도로 걷어야 한다.
        free.ExcelEpisodeId = null;
        Chapter(editor).Episodes.RemoveAll(episode =>
            string.Equals(episode.EpisodeId, "곁가지", StringComparison.Ordinal));

        return (editor, free);
    }
}
