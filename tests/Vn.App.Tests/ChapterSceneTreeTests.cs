using Avalonia.Controls;
using Avalonia.VisualTree;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.App.Tests;

/// <summary>
/// <b>Chapter → Scene → Episode 탐색기</b> (R6 S-2 · `docs/plans/R6-explorer.md`).
///
/// ⛔ 이 컨트롤이 드는 상태는 <b>접힘과 선택 둘뿐</b>이다. 마디는 그릴 때마다 만드는
/// 투영이라, 장면을 들고 있는 자리가 여기에도 없다.
/// </summary>
public sealed class ChapterSceneTreeTests
{
    [Fact]
    public void 장면ID를_하나도_안_적은_챕터는_장면_단이_생략된다() => HeadlessUi.Run(() =>
    {
        // ⛔ 규격 §2 — 그대로 그리면 에피소드 수만큼 장면 마디가 생겨 트리가 통째로
        //    노이즈가 된다. 구판 프로젝트를 열었을 때가 정확히 그 상태다.
        ChapterSceneTree tree = Show(Chapter("ch01", (null, "ep01"), (null, "ep02")));

        // 챕터가 펴져 있어야 아래를 세는 단언이 뜻을 갖는다.
        Assert.Equal(2, tree.Rows.Count(row => row.Kind == SceneTreeRowKind.Episode));
        Assert.DoesNotContain(tree.Rows, row => row.Kind == SceneTreeRowKind.Scene);

        // 그리고 그 사실을 챕터 줄이 한 번 말한다 — 안 말하면 "장면이 왜 없지"가 된다.
        Assert.Contains("장면 미지정", tree.Rows.Single(row => row.Kind == SceneTreeRowKind.Chapter).Text);

        // 에피소드는 챕터 바로 아래다.
        Assert.All(
            tree.Rows.Where(row => row.Kind == SceneTreeRowKind.Episode),
            row => Assert.Equal(1, row.Depth));
    });

    [Fact]
    public void 하나라도_적혀_있으면_전부_장면_단으로_본다() => HeadlessUi.Run(() =>
    {
        // ⚠ 섞어서 숨기지 않는다 — 적힌 것만 묶고 안 적은 것을 챕터 밑에 두면 같은 화면에
        //    두 규칙이 서서 "이건 왜 밖에 있지"가 된다.
        ChapterSceneTree tree = Show(Chapter("ch01", ("opening", "ep01"), (null, "ep02")));

        Assert.Equal(
            ["opening", "미지정 · ep02"],
            tree.Rows.Where(row => row.Kind == SceneTreeRowKind.Scene).Select(row => row.Text));

        Assert.All(
            tree.Rows.Where(row => row.Kind == SceneTreeRowKind.Episode),
            row => Assert.Equal(2, row.Depth));
    });

    [Fact]
    public void 장면_줄을_눌러도_선택이_안_바뀐다() => HeadlessUi.Run(() =>
    {
        // ⛔ 규격 §1 — 고를 수 있는 것은 에피소드뿐이다. 챕터·장면은 담는 자리다.
        ChapterSceneTree tree = Show(Chapter("ch01", ("opening", "ep01"), ("opening", "ep02")));

        tree.Select("ch01", "ep02");
        ChapterEpisodePick? before = tree.Selection;

        Press(tree, SceneTreeRowKind.Scene);

        Assert.Equal(before, tree.Selection);
    });

    [Fact]
    public void 접은_것은_다시_그려도_안_펴진다() => HeadlessUi.Run(() =>
    {
        // ⛔ 규격 §4 — 편집 한 번이 판을 다시 그리는데 그때마다 펴지면 접는 행위 자체가
        //    뜻을 잃는다.
        ChapterDocument chapter = Chapter("ch01", ("opening", "ep01"));
        ChapterSceneTree tree = Show(chapter);

        tree.Select("ch01", "ep01");
        Assert.Contains(tree.Rows, row => row.Kind == SceneTreeRowKind.Episode);

        Press(tree, SceneTreeRowKind.Chapter);   // 접는다
        Assert.DoesNotContain(tree.Rows, row => row.Kind == SceneTreeRowKind.Episode);

        tree.Rebuild(Project(chapter), (_, _) => true);   // 편집 한 번이 다시 그린다

        Assert.DoesNotContain(tree.Rows, row => row.Kind == SceneTreeRowKind.Episode);
    });

    [Fact]
    public void 장면_루트는_하나고_들어오는_자리가_둘이면_짚어_준다() => HeadlessUi.Run(() =>
    {
        // root → a, root → b 라 shared 장면에 착지점이 둘이다(코어의 `VerifySceneEntries`
        // 위반). 트리는 <b>그려 놓고 짚는다</b> — 거부는 내보내기 관문의 일이다.
        ChapterDocument chapter = Chapter(
            "ch01", ("opening", "root"), ("shared", "a"), ("shared", "b"));

        chapter.Edges.Add(new ChapterEdge("root", "a", "A로", null, null, 0));
        chapter.Edges.Add(new ChapterEdge("root", "b", "B로", null, null, 0));

        ChapterSceneTree tree = Show(chapter);
        tree.Select("ch01", "a");

        Assert.True(tree.Rows.Single(row => row.SceneId == "shared" &&
                                            row.Kind == SceneTreeRowKind.Scene).HasSplitEntry);

        Assert.Single(tree.Rows, row =>
            row.Kind == SceneTreeRowKind.Episode && row.SceneId == "shared" && row.IsSceneRoot);
    });

    [Fact]
    public void 대본이_없는_에피소드는_흐리게_선다() => HeadlessUi.Run(() =>
    {
        // 아직 아무도 안 쓴 자리라는 것이 <b>목록에서</b> 보여야 작가가 어디부터 쓸지 정한다.
        ChapterDocument chapter = Chapter("ch01", ("opening", "written"), ("opening", "empty"));

        var tree = new ChapterSceneTree();
        var window = new Window { Width = 300, Height = 500, Content = tree };
        window.Show();

        tree.Rebuild(
            Project(chapter),
            (_, episodeId) => string.Equals(episodeId, "written", StringComparison.Ordinal));

        tree.Select("ch01", "written");

        Assert.False(Row(tree, "written").IsEmptyScript);
        Assert.True(Row(tree, "empty").IsEmptyScript);

        window.Close();
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static SceneTreeRow Row(ChapterSceneTree tree, string episodeId) =>
        tree.Rows.Single(row => row.EpisodeId == episodeId);

    /// <summary>그 종류의 첫 줄을 누른다 — 사람이 누르는 것과 같은 길이다.</summary>
    private static void Press(ChapterSceneTree tree, SceneTreeRowKind kind)
    {
        int index = tree.Rows
            .Select((row, at) => (row, at))
            .First(item => item.row.Kind == kind).at;

        tree.GetVisualDescendants().OfType<Button>().ElementAt(index)
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// 트리를 세우고 <b>첫 에피소드를 고른다</b>.
    ///
    /// ⚠ 고르지 않으면 규격 §4대로 <b>전부 접혀 있어</b> 줄이 챕터 하나뿐이다 — 그 상태에서
    /// 자식을 세는 단언은 빈 집합을 보고 <b>거짓으로 통과</b>한다(2026-09-16에 실제로 그랬다).
    /// </summary>
    private static ChapterSceneTree Show(ChapterDocument chapter)
    {
        var tree = new ChapterSceneTree();
        var window = new Window { Width = 300, Height = 500, Content = tree };
        window.Show();

        tree.Rebuild(Project(chapter), (_, _) => true);
        tree.Select(chapter.ChapterId, chapter.Episodes[0].EpisodeId);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return tree;
    }

    private static StoryProject Project(ChapterDocument chapter)
    {
        var project = new StoryProject();
        project.Chapters.Add(chapter);
        return project;
    }

    private static ChapterDocument Chapter(
        string chapterId, params (string? SceneId, string EpisodeId)[] episodes)
    {
        var chapter = new ChapterDocument { ChapterId = chapterId };

        for (int index = 0; index < episodes.Length; index++)
        {
            (string? sceneId, string episodeId) = episodes[index];

            chapter.Episodes.Add(new ChapterEpisode(
                episodeId, episodeId, string.Empty, episodeId, 0, 0, null, index + 2)
            {
                SceneId = sceneId
            });
        }

        return chapter;
    }
}
