using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>간선에 매달렸던 연출 씬이 길 가운데로 올라온다</b> (R7 P-6 · 결정 ⑤ · 2026-09-17).
///
/// ⛔ <c>ViaNode</c>는 <b>재생 순서를 말하는 두 번째 방법</b>이었다. 같은 순서를 간선 두 개로
/// 그대로 말할 수 있으므로 곁칸이 아니라 간선이 남는다 — 같은 것을 두 데서 말하면 갈린다.
///
/// <code>
/// 전: A —"문구"→ B   (간선에 Via가 매달려 있다)
/// 후: A —"문구"→ Via —자동→ B
/// </code>
/// </summary>
public sealed class ViaSceneLiftTests
{
    [Fact]
    public void 매달린_씬이_길_가운데_에피소드로_선다()
    {
        (ProjectEditor editor, DialogueNode _) = World();

        LiftedViaScene lifted = Assert.Single(editor.LiftViaScenes("ch01"));

        Assert.Equal("곁들임", lifted.ViaEpisodeId);
        Assert.Equal("root", lifted.FromEpisodeId);
        Assert.Equal("b", lifted.ToEpisodeId);

        Assert.Contains(Chapter(editor).Episodes, episode =>
            string.Equals(episode.EpisodeId, "곁들임", StringComparison.Ordinal));
    }

    [Fact]
    public void 간선_하나가_둘로_펴진다()
    {
        (ProjectEditor editor, DialogueNode _) = World();

        editor.LiftViaScenes("ch01");

        ChapterEdge first = Assert.Single(Chapter(editor).Edges, edge =>
            string.Equals(edge.FromEpisodeId, "root", StringComparison.Ordinal));
        ChapterEdge second = Assert.Single(Chapter(editor).Edges, edge =>
            string.Equals(edge.FromEpisodeId, "곁들임", StringComparison.Ordinal));

        // 문구는 앞 간선에 남는다 — 사람이 고른 자리가 거기다.
        Assert.Equal("곁으로", first.OptionLabel);
        Assert.Equal("곁들임", first.ToEpisodeId);

        // 뒤는 자동 길이다 — 묻지 않고 지나간다.
        Assert.True(second.Auto);
        Assert.Equal("b", second.ToEpisodeId);
        Assert.Null(second.OptionLabel);
    }

    [Fact]
    public void 뒤_자동_길은_장면을_안_넘는다()
    {
        // ⛔ 자동 길은 같은 장면 안에서만 선다(`AutoEdgeCrossesScene`). 그래서 Via는
        //    출발이 아니라 <b>도착의 장면</b>에 서야 한다.
        (ProjectEditor editor, DialogueNode _) = World();
        editor.UpdateEpisode("ch01", "root", sceneId: "sc_앞");
        editor.UpdateEpisode("ch01", "b", sceneId: "sc_뒤");

        editor.LiftViaScenes("ch01");

        Assert.Equal(
            "sc_뒤",
            Chapter(editor).Episodes
                .Single(episode => string.Equals(episode.EpisodeId, "곁들임", StringComparison.Ordinal))
                .SceneId);
    }

    [Fact]
    public void 올린_뒤에는_곁칸이_비어_두_번_재생되지_않는다()
    {
        // ⚠ 남겨 두면 같은 씬이 두 번 재생된다 — 간선으로 한 번, 곁칸으로 한 번.
        (ProjectEditor editor, DialogueNode source) = World();

        editor.LiftViaScenes("ch01");

        Assert.Empty(source.ChoiceExits);
    }

    [Fact]
    public void 이미_에피소드인_노드는_안_건드린다()
    {
        // 그건 연출 씬이 아니라 잘못 이어진 배선이고, `WarnExitsIntoExcelNodes`가 짚는다.
        (ProjectEditor editor, DialogueNode source) = World();
        Via(editor).ExcelEpisodeId = "b";

        Assert.Empty(editor.LiftViaScenes("ch01"));
        Assert.Single(source.ChoiceExits);
    }

    [Fact]
    public void 매달린_것이_없으면_아무것도_안_바꾼다()
    {
        // ⚠ 판을 열 때마다 도는 자리다 — 바꿀 것이 없는데도 손을 대면 안 고친 프로젝트가
        //    매번 더럽혀지고 되돌리기 기록이 뜻 없이 쌓인다.
        (ProjectEditor editor, DialogueNode source) = World();
        source.ChoiceExits.Clear();

        string before = ProjectSnapshotCodec.Encode(editor.Project);

        Assert.Empty(editor.LiftViaScenes("ch01"));
        Assert.Equal(before, ProjectSnapshotCodec.Encode(editor.Project));
    }

    [Fact]
    public void 되돌리기_한_번에_통째로_돌아간다()
    {
        // ⛔ 갈라 두면 에피소드만 남거나 간선만 펴진 <b>반쪽</b>이 생긴다.
        (ProjectEditor editor, DialogueNode _) = World();
        int episodes = Chapter(editor).Episodes.Count;
        int edges = Chapter(editor).Edges.Count;

        editor.LiftViaScenes("ch01");
        editor.Undo();

        // ⚠ `Restore`는 `Project`를 통째로 갈아 끼운다 — 되돌린 뒤에 다시 읽어야 한다.
        Assert.Equal(episodes, Chapter(editor).Episodes.Count);
        Assert.Equal(edges, Chapter(editor).Edges.Count);
    }

    [Fact]
    public void 이름이_이미_쓰이고_있으면_조용히_개명하지_않는다()
    {
        // 조용히 이름을 바꾸면 어느 씬이 어디로 갔는지 아무도 모른다 — 사람이 판에서
        // 고치면 다음 번에 올라간다.
        (ProjectEditor editor, DialogueNode source) = World();
        editor.AddEpisode("ch01", "곁들임", title: "남의 자리", 0, 0);

        Assert.Empty(editor.LiftViaScenes("ch01"));
        Assert.Single(source.ChoiceExits);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static ChapterDocument Chapter(ProjectEditor editor) =>
        editor.Project.Chapters.Single();

    private static DialogueNode Via(ProjectEditor editor) =>
        editor.Project.EnumerateNodes().OfType<DialogueNode>()
            .First(node => string.Equals(node.Name, "곁들임", StringComparison.Ordinal));

    /// <summary>`root —"곁으로"→ b` 간선에 연출 씬 `곁들임`이 매달린 챕터.</summary>
    private static (ProjectEditor Editor, DialogueNode Source) World()
    {
        var project = new StoryProject();
        var editor = new ProjectEditor(project);

        editor.EnsureChapter("ch01");
        string fileId = editor.EnsureChapterBoard("ch01");

        editor.AddEpisode("ch01", "root", title: "root", 0, 0);
        editor.AddEpisode("ch01", "b", title: "b", 0, 0);
        editor.AddEdge("ch01", "root", "b", optionLabel: "곁으로");

        DialogueNode source = editor.AddDialogueNode(fileId, 0, 0, "root");
        DialogueNode via = editor.AddDialogueNode(fileId, 400, 0, "곁들임");

        // ⚠ 판에 세운 카드는 이제 에피소드를 함께 만든다(P-6) — 옛 프로젝트의 자유 씬을
        //    흉내 내려면 그 표식과 에피소드를 도로 걷어야 한다.
        via.ExcelEpisodeId = null;
        Chapter(editor).Episodes.RemoveAll(episode =>
            string.Equals(episode.EpisodeId, "곁들임", StringComparison.Ordinal));

        source.ChoiceExits["곁으로"] = via.Id;


        return (editor, source);
    }
}
