using Vn.Authoring.Editing;
using Vn.Authoring.Graph;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>새 대사 노드가 설 자리</b> (R7 P-4 · `docs/plans/R7.md`).
///
/// ⛔ 지금까지 세 창구가 전부 자리를 안 주고 원점에 세웠다 — 판을 열면 카드가 <b>한 곳에
/// 쌓여</b> 챕터 프레임도 장면 영역도 뜻을 잃었다. 규칙이 셋이면 갈리므로 한 벌이다.
///
/// ⚠ <b>이미 선 카드는 안 옮긴다.</b> 자리는 사람이 끌어 정하는 것이고, 여는 때마다 줄을
/// 다시 세우면 그 손이 지워진다.
/// </summary>
public sealed class NodePlacementTests
{
    [Fact]
    public void 장면마다_제_줄에_선다()
    {
        StoryProject project = World(("opening", "root"), ("classroom", "a"));

        Assert.Equal((0, 0), NodePlacement.For(project, "ch01", "root"));
        Assert.Equal((0, NodePlacement.SceneRow), NodePlacement.For(project, "ch01", "a"));
    }

    [Fact]
    public void 같은_장면의_다음_카드는_그_줄의_오른쪽에_선다()
    {
        StoryProject project = World(("opening", "root"), ("opening", "a"));
        Place(project, "root", 0, 0);

        Assert.Equal((NodePlacement.Column, 0), NodePlacement.For(project, "ch01", "a"));
    }

    [Fact]
    public void 사람이_장면을_통째로_옮겨_뒀으면_그_줄을_따른다()
    {
        // 자리는 사람이 정하는 것이다 — 새 카드가 저 혼자 원래 줄로 돌아가면 안 된다.
        StoryProject project = World(("opening", "root"), ("opening", "a"));
        Place(project, "root", 1000, 700);

        Assert.Equal((1000 + NodePlacement.Column, 700), NodePlacement.For(project, "ch01", "a"));
    }

    [Fact]
    public void 챕터에_없는_이름은_줄을_안_받고_판_오른쪽에_선다()
    {
        // 진행에 안 실리는 노드다 — 장면 줄을 주면 남의 장면 안에 서 버린다.
        StoryProject project = World(("opening", "root"));
        Place(project, "root", 0, 0);

        Assert.Equal((NodePlacement.Column, 0), NodePlacement.For(project, "ch01", "곁가지"));
    }

    [Fact]
    public void 챕터가_아닌_판도_겹치지는_않게_둔다()
    {
        var project = new StoryProject();
        var editor = new ProjectEditor(project);

        string fileId = editor.EnsureChapterBoard("작가의 판");   // 같은 이름의 챕터가 없다
        editor.AddDialogueNode(fileId, 0, 0, "아무거나");

        Assert.Equal((NodePlacement.Column, 0), NodePlacement.For(project, "작가의 판", "또 하나"));
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <summary>그 에피소드의 카드를 그 자리에 세운다 — 사람이 끌어다 둔 것과 같다.</summary>
    private static void Place(StoryProject project, string episodeId, double x, double y)
    {
        var editor = new ProjectEditor(project);

        editor.AddDialogueNode(editor.EnsureChapterBoard("ch01"), x, y, episodeId)
            .MarkedEpisodeId = episodeId;
    }

    /// <summary>ch01과 그 판 — 에피소드는 챕터에 있고, 노드는 아직 없다.</summary>
    private static StoryProject World(params (string SceneId, string EpisodeId)[] episodes)
    {
        var project = new StoryProject();
        var editor = new ProjectEditor(project);

        editor.EnsureChapter("ch01");
        editor.EnsureChapterBoard("ch01");   // 노드는 자리를 물어본 뒤에 선다

        foreach ((string sceneId, string episodeId) in episodes)
        {
            editor.AddEpisode("ch01", episodeId, title: episodeId, 0, 0, sceneId);
        }

        return project;
    }
}
