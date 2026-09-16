using Avalonia.Controls;
using Avalonia.Input;
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
    public void 이름_쪽을_눌러도_안_접힌다() => HeadlessUi.Run(() =>
    {
        // ⛔ 2026-09-16 소유자: 줄 전체가 접기 손잡이라 <b>끌기와 이름 고치기가 물리적으로
        //    막혔다</b>. 누를 때마다 접혔다 펴지고, 그때 줄이 통째로 다시 서서 더블클릭의
        //    두 번째 누름이 <b>새 컨트롤</b>에 떨어졌다. 손잡이는 삼각형뿐이다.
        ChapterSceneTree tree = Show(Chapter("ch01", ("opening", "ep01")));

        int before = tree.Rows.Count;

        PressName(tree, SceneTreeRowKind.Chapter);
        Assert.Equal(before, tree.Rows.Count);

        PressName(tree, SceneTreeRowKind.Scene);
        Assert.Equal(before, tree.Rows.Count);

        // 그리고 삼각형은 여전히 접는다.
        Press(tree, SceneTreeRowKind.Chapter);
        Assert.True(tree.Rows.Count < before);
    });

    [Fact]
    public void 에피소드를_골라도_줄이_다시_서지_않는다() => HeadlessUi.Run(() =>
    {
        // ⛔ 같은 사고의 다른 얼굴이다 — 고를 때마다 <see cref="Draw"/>를 부르면 컨트롤이
        //    갈려서 더블클릭이 성립하지 않는다. 구조가 안 바뀌는 변화는 칠만 한다.
        ChapterSceneTree tree = Show(Chapter("ch01", ("opening", "ep01"), ("opening", "ep02")));

        Button before = tree.GetVisualDescendants().OfType<Button>().Last();

        PressName(tree, SceneTreeRowKind.Episode);

        Assert.Same(before, tree.GetVisualDescendants().OfType<Button>().Last());
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

    [Fact]
    public void 접은_것이_앱을_다시_열어도_남는다() => HeadlessUi.Run(() =>
    {
        // 규격 §4 — 접힘은 <b>사람의 것</b>이라 프로젝트를 따라다닌다. 세션 안에서만 살면
        // 탭을 옮겼다 오거나 앱을 다시 켤 때마다 접어 둔 것이 도로 펴진다.
        string project = TempProject();
        ChapterDocument chapter = Chapter("ch01", ("opening", "ep01"));

        ChapterSceneTree first = Show(chapter, project);
        Press(first, SceneTreeRowKind.Chapter);
        Assert.DoesNotContain(first.Rows, row => row.Kind == SceneTreeRowKind.Episode);

        // 새 컨트롤 = 앱을 다시 켠 것. 같은 프로젝트를 다시 연다.
        ChapterSceneTree reopened = Show(chapter, project);

        Assert.DoesNotContain(reopened.Rows, row => row.Kind == SceneTreeRowKind.Episode);
    });

    [Fact]
    public void 다른_프로젝트를_열면_앞_프로젝트의_접힘을_안_쓴다() => HeadlessUi.Run(() =>
    {
        // ⛔ 열쇠는 챕터 Id로 만든다 — 프로젝트를 갈아도 안 놓으면 <b>이름만 같은 다른
        //    챕터</b>가 엉뚱하게 접힌 채로 열린다.
        ChapterDocument chapter = Chapter("ch01", ("opening", "ep01"));

        ChapterSceneTree tree = Show(chapter, TempProject());
        Press(tree, SceneTreeRowKind.Chapter);
        Assert.DoesNotContain(tree.Rows, row => row.Kind == SceneTreeRowKind.Episode);

        tree.Remember(TempProject());
        tree.Rebuild(Project(chapter), (_, _) => true);
        tree.Select("ch01", "ep01");

        Assert.Contains(tree.Rows, row => row.Kind == SceneTreeRowKind.Episode);
    });

    [Fact]
    public void 커서가_지나가는_것만으로는_글이_안_열린다() => HeadlessUi.Run(() =>
    {
        // ⛔ 커서와 선택은 <b>다른 것</b>이다(규격 §7 — 고르는 것은 Enter). ↑↓로 훑을 때마다
        //    오른쪽 글이 바뀌면 목록을 훑어볼 수가 없다.
        ChapterSceneTree tree = Show(Chapter("ch01", ("opening", "ep01"), ("opening", "ep02")));

        Stroke(tree, Key.Down);   // 고른 줄(ep01)에서 한 칸

        Assert.Equal("ep02", tree.CursorRow!.EpisodeId);
        Assert.Equal("ep01", tree.Selection!.EpisodeId);

        Stroke(tree, Key.Enter);

        Assert.Equal("ep02", tree.Selection!.EpisodeId);
    });

    [Fact]
    public void 왼쪽은_접고_이미_접혔으면_부모로_간다() => HeadlessUi.Run(() =>
    {
        // 규격 §7. 유니티 하이어라키와 같은 손버릇이라 설명 없이 손이 먼저 안다.
        ChapterSceneTree tree = Show(Chapter("ch01", ("opening", "ep01")));

        Stroke(tree, Key.Up);   // ep01 → 장면 줄
        Assert.Equal(SceneTreeRowKind.Scene, tree.CursorRow!.Kind);

        Stroke(tree, Key.Left);   // 접는다
        Assert.DoesNotContain(tree.Rows, row => row.Kind == SceneTreeRowKind.Episode);
        Assert.Equal(SceneTreeRowKind.Scene, tree.CursorRow!.Kind);

        Stroke(tree, Key.Left);   // 이미 접혔으니 부모로
        Assert.Equal(SceneTreeRowKind.Chapter, tree.CursorRow!.Kind);
    });

    [Fact]
    public void 오른쪽은_펼치고_이미_펼쳤으면_첫_자식으로_간다() => HeadlessUi.Run(() =>
    {
        ChapterSceneTree tree = Show(Chapter("ch01", ("opening", "ep01")));

        Stroke(tree, Key.Up);
        Stroke(tree, Key.Up);   // 챕터 줄
        Assert.Equal(SceneTreeRowKind.Chapter, tree.CursorRow!.Kind);

        Stroke(tree, Key.Left);   // 접는다
        Assert.DoesNotContain(tree.Rows, row => row.Kind == SceneTreeRowKind.Scene);

        Stroke(tree, Key.Right);   // 편다
        Assert.Contains(tree.Rows, row => row.Kind == SceneTreeRowKind.Scene);
        Assert.Equal(SceneTreeRowKind.Chapter, tree.CursorRow!.Kind);

        Stroke(tree, Key.Right);   // 이미 펴졌으니 첫 자식으로
        Assert.Equal(SceneTreeRowKind.Scene, tree.CursorRow!.Kind);
    });

    [Fact]
    public void 키보드로_접은_것도_기억한다() => HeadlessUi.Run(() =>
    {
        // 누른 것과 키로 접은 것이 <b>같은 길</b>을 지나야 한다 — 하나만 기억되면
        // 사람은 어느 쪽이 남는지 알 수 없다.
        string project = TempProject();
        ChapterDocument chapter = Chapter("ch01", ("opening", "ep01"));

        ChapterSceneTree tree = Show(chapter, project);
        Stroke(tree, Key.Up);
        Stroke(tree, Key.Up);
        Stroke(tree, Key.Left);

        Assert.DoesNotContain(tree.Rows, row => row.Kind == SceneTreeRowKind.Scene);
        Assert.DoesNotContain(Show(chapter, project).Rows, row => row.Kind == SceneTreeRowKind.Scene);
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <summary>키 한 번 — 사람이 누르는 것과 같은 길이다.</summary>
    private static void Stroke(ChapterSceneTree tree, Key key)
    {
        tree.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key
        });

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>진짜 파일이어야 한다 — 없는 프로젝트의 접힘은 저장할 때 버려진다(가지치기).</summary>
    private static string TempProject()
    {
        string directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "vn-tree-collapse", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        string path = System.IO.Path.Combine(directory, "project.vnproj");
        File.WriteAllText(path, "{}");

        return path;
    }

    private static SceneTreeRow Row(ChapterSceneTree tree, string episodeId) =>
        tree.Rows.Single(row => row.EpisodeId == episodeId);

    /// <summary>
    /// 그 종류의 첫 줄을 <b>삼각형에서</b> 접었다 편다 — 사람이 누르는 그 자리다.
    ///
    /// ⛔ 이름 쪽이 아니다 (2026-09-16 소유자). 줄 전체가 접기 손잡이였을 때 끌기와 이름
    /// 고치기가 물리적으로 막혔다 — 누를 때마다 줄이 다시 서서 더블클릭이 성립하지 않았다.
    /// </summary>
    private static void Press(ChapterSceneTree tree, SceneTreeRowKind kind)
    {
        int index = tree.Rows
            .Select((row, at) => (row, at))
            .First(item => item.row.Kind == kind).at;

        Border arrow = tree.GetVisualDescendants().OfType<DockPanel>().ElementAt(index)
            .Children.OfType<Border>().First();

        arrow.RaiseEvent(new PointerPressedEventArgs(
            arrow, new Pointer(0, PointerType.Mouse, true), arrow, default, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None));

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>이름 쪽을 누른다 — 고르기만 하고 <b>접히지 않아야</b> 한다.</summary>
    private static void PressName(ChapterSceneTree tree, SceneTreeRowKind kind)
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
    private static ChapterSceneTree Show(ChapterDocument chapter, string? projectPath = null)
    {
        var tree = new ChapterSceneTree();
        var window = new Window { Width = 300, Height = 500, Content = tree };
        window.Show();

        tree.Remember(projectPath);
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
