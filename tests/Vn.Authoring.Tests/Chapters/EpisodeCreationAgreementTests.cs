using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>에피소드를 만드는 길은 하나다</b> (2026-09-18 소유자 — *"같은거여야 되는데"*).
///
/// ⛔ <b>세 창구가 서로 다른 것을 만들고 있었다.</b> 「에피소드에는 대사노드가 있다」가
/// 편집기 명령이 아니라 <b>화면에</b> 살았기 때문이다 — 챕터 그래프는 <c>AddEpisode</c> 뒤에
/// 노드 만들기를 한 줄 더 불렀고, 대본 탭은 <b>그 한 줄이 없었다</b>. 그래서 작가의 탭에서
/// 만든 에피소드만 쓸 자리가 없었다. Id 규약도 갈렸다: 연출 그래프는 <c>장면 3</c>,
/// 나머지는 <c>new01</c>.
///
/// ⚠ <b>이 파일이 다음 갈림을 막는다.</b> 창구가 하나 더 생겨도 같은 것을 만들어야 한다.
/// </summary>
public sealed class EpisodeCreationAgreementTests
{
    [Fact]
    public void 에피소드를_만들면_카드와_대본이_함께_선다()
    {
        ProjectEditor editor = World();

        ChapterEpisode episode = editor.AddEpisode("ch01", "ep01", title: string.Empty, 0, 0);

        Assert.Equal("ep01", episode.EpisodeId);
        AssertWritable(editor, "ep01");
    }

    [Fact]
    public void 다음_에피소드도_같은_것을_만든다()
    {
        // 분기 저작의 길 — 에피소드·카드·대본·간선이 한 변경이다.
        ProjectEditor editor = World();
        editor.AddEpisode("ch01", "root", title: string.Empty, 0, 0);

        editor.AddNextEpisode("ch01", "root", "ep02", title: string.Empty, 260, 0, optionLabel: "다음");

        AssertWritable(editor, "ep02");
        Assert.Single(editor.FindChapter("ch01")!.Edges);
    }

    [Fact]
    public void 되돌리기_한_번에_에피소드와_카드가_함께_사라진다()
    {
        // ⚠ 전에는 화면이 둘로 나눠 했으므로 <b>되돌리기가 두 걸음</b>이었다 — 한 번 누르면
        //    에피소드는 남고 카드만 사라지는 <b>반쪽 상태</b>가 진짜로 존재했다.
        ProjectEditor editor = World();
        editor.AddNextEpisode(
            "ch01", Seed(editor), "ep02", title: string.Empty, 260, 0, optionLabel: "다음");

        editor.Undo();

        ChapterDocument chapter = editor.FindChapter("ch01")!;

        Assert.DoesNotContain("ep02", chapter.Episodes.Select(episode => episode.EpisodeId));
        Assert.Empty(chapter.Edges);
        Assert.DoesNotContain(
            "ep02",
            editor.Project.EnumerateNodes().OfType<DialogueNode>().Select(EpisodeNaming.EpisodeIdOf));
    }

    [Fact]
    public void 판에_카드를_세우는_길도_같은_Id_규약을_쓴다()
    {
        // ⛔ 연출 그래프의 [＋대사노드]는 이름을 안 주고 부른다. 그때 <c>장면 3</c>이 나오면
        //    그것이 곧 EpisodeId가 되고, <b>공백 든 Id가 대본 워크북의 파일 이름</b>으로
        //    나간다. 다른 두 창구가 쓰던 `new01` 규약으로 맞춘다.
        ProjectEditor editor = World();
        string fileId = editor.EnsureChapterBoard("ch01");

        DialogueNode created = editor.AddDialogueNode(fileId, 100, 100);

        Assert.Equal("new01", EpisodeNaming.EpisodeIdOf(created));
        AssertWritable(editor, "new01");
    }

    [Fact]
    public void 챕터가_아닌_판에서는_에피소드가_안_선다()
    {
        // 작가의 낙서판에는 챕터가 없다 — 거기 세운 카드는 진행에 안 실린다.
        var project = new StoryProject();
        var editor = new ProjectEditor(project);
        string fileId = editor.AddStoryFile("낙서").Id;

        DialogueNode created = editor.AddDialogueNode(fileId, 0, 0);

        Assert.StartsWith("장면", created.Name, StringComparison.Ordinal);
        Assert.Null(created.MarkedEpisodeId);
        Assert.Empty(project.Chapters);
    }

    /// <summary>에피소드가 <b>쓸 수 있는 상태</b>로 섰는가 — 카드와 대본이 붙어 있는가.</summary>
    private static void AssertWritable(ProjectEditor editor, string episodeId)
    {
        Assert.Contains(
            episodeId,
            editor.FindChapter("ch01")!.Episodes.Select(episode => episode.EpisodeId));

        DialogueNode card = Assert.Single(
            editor.Project.EnumerateNodes().OfType<DialogueNode>(),
            node => string.Equals(EpisodeNaming.EpisodeIdOf(node), episodeId, StringComparison.Ordinal));

        Assert.NotNull(card.ScriptId);
        Assert.NotNull(editor.Project.FindScript(card.ScriptId));
    }

    private static string Seed(ProjectEditor editor)
    {
        editor.AddEpisode("ch01", "root", title: string.Empty, 0, 0);
        return "root";
    }

    private static ProjectEditor World()
    {
        var editor = new ProjectEditor(new StoryProject());
        editor.EnsureChapter("ch01");

        return editor;
    }
}
