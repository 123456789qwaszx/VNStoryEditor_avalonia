using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;
using Path = System.IO.Path;

namespace Vn.App.Tests;

/// <summary>
/// <b>[대본] 탭에서 구조를 세운다</b> (2026-09-16 소유자) — 머리글의 [＋]로 챕터,
/// 트리 우클릭으로 장면과 에피소드.
///
/// ⛔ <b>장면은 여기서도 엔티티가 아니다.</b> [장면 추가]는 프로젝트에 아무것도 안 쓴다 —
/// <b>빈 자리</b>를 하나 여는 것뿐이고, 에피소드가 들어와야 비로소 장면이 된다(R6 §3-1).
/// 그래서 [장면 삭제]도 에피소드를 지우지 않는다: 이름표를 떼면 미지정이 될 뿐이다.
/// </summary>
public sealed class ScriptTabAuthoringTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-script-authoring", Guid.NewGuid().ToString("N"));

    private string ManifestPath => Path.Combine(_directory, "p" + ProjectManifestJson.FileExtension);

    public ScriptTabAuthoringTests()
    {
        Directory.CreateDirectory(_directory);
        ProjectStore.Save(ManifestPath, new StoryProject { Title = "작가가 세우는 자리" });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 머리글의_더하기로_만든_챕터가_곧바로_트리에_선다() => HeadlessUi.Run(() =>
    {
        // 작가가 [챕터 그래프]로 건너갔다 오지 않아도 첫 칸을 밟을 수 있어야 한다.
        (ScriptView view, AuthoringSession session) = Show();

        Assert.Empty(Rows(view, SceneTreeRowKind.Chapter));

        Assert.Null(session.CreateChapter("ch01"));   // [＋]의 창구가 부르는 그 길
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(["ch01"], Rows(view, SceneTreeRowKind.Chapter));
        Assert.NotNull(session.Editor.FindChapter("ch01"));
    });

    [Fact]
    public void 챕터_그래프와_대본_탭이_같은_길로_챕터를_만든다() => HeadlessUi.Run(() =>
    {
        // ⛔ 창구가 두 벌이면 어느 쪽으로 만들었느냐에 따라 챕터가 달라진다. 스탯이 그
        //    증거다 — 정의 파일의 변수가 깔려야 도달성 증명에 탐색 경계가 있다.
        (_, AuthoringSession session) = Show();

        Assert.Null(session.CreateChapter("ch01"));

        Assert.Equal(
            session.Definition.Variables.Select(variable => variable.Name),
            session.Editor.FindChapter("ch01")!.Stats.Select(stat => stat.Key));
    });

    [Fact]
    public void 챕터를_우클릭해_장면을_더하면_자식으로_붙는다() => HeadlessUi.Run(() =>
    {
        (ScriptView view, AuthoringSession session) = Show();
        Chapter(session, "ch01", "ep01");

        Menu(view, SceneTreeRowKind.Chapter, "장면 추가");

        SceneTreeRow draft = Assert.Single(Tree(view).Rows, row => row.IsDraft);

        Assert.Equal(SceneTreeRowKind.Scene, draft.Kind);
        Assert.Equal("ch01", draft.ChapterId);
        Assert.Equal(1, draft.Depth);   // 챕터의 자식이다

        // ⚠ 아직 프로젝트에는 아무것도 안 썼다 — 장면은 에피소드가 있어야 있는 것이다.
        Assert.DoesNotContain(
            session.Editor.FindChapter("ch01")!.Episodes,
            episode => string.Equals(episode.SceneId, draft.SceneId, StringComparison.Ordinal));
    });

    [Fact]
    public void 빈_장면에_에피소드를_넣으면_그때_진짜가_된다() => HeadlessUi.Run(() =>
    {
        (ScriptView view, AuthoringSession session) = Show();
        Chapter(session, "ch01", "ep01");

        Menu(view, SceneTreeRowKind.Chapter, "장면 추가");
        string sceneId = Assert.Single(Tree(view).Rows, row => row.IsDraft).SceneId!;

        Menu(view, SceneTreeRowKind.Scene, "에피소드 추가", sceneId);

        // 프로젝트에 실렸다 — 이제 자리표시가 아니다.
        ChapterEpisode made = Assert.Single(
            session.Editor.FindChapter("ch01")!.Episodes,
            episode => string.Equals(episode.SceneId, sceneId, StringComparison.Ordinal));

        Assert.DoesNotContain(Tree(view).Rows, row => row.IsDraft);
        Assert.Contains(Tree(view).Rows, row =>
            row.Kind == SceneTreeRowKind.Episode &&
            row.SceneId == sceneId &&
            row.EpisodeId == made.EpisodeId);
    });

    [Fact]
    public void 새_장면의_첫_에피소드는_섬으로_두지_않는다() => HeadlessUi.Run(() =>
    {
        // 챕터 그래프의 [＋ 에피소드]가 세운 규율 그대로다(v12) — 간선이 함께 선다.
        // 그 간선 하나가 이 장면의 <b>들어오는 자리</b>가 된다.
        (ScriptView view, AuthoringSession session) = Show();
        Chapter(session, "ch01", "ep01");

        Menu(view, SceneTreeRowKind.Chapter, "장면 추가");
        string sceneId = Assert.Single(Tree(view).Rows, row => row.IsDraft).SceneId!;
        Menu(view, SceneTreeRowKind.Scene, "에피소드 추가", sceneId);

        ChapterDocument chapter = session.Editor.FindChapter("ch01")!;
        ChapterEpisode made = chapter.Episodes.Last();

        Assert.Contains(chapter.Edges, edge =>
            edge.FromEpisodeId == "ep01" && edge.ToEpisodeId == made.EpisodeId);

        // 그리고 그 자리는 하나뿐이다 — 저작 시점 진단이 조용해야 한다.
        Assert.DoesNotContain(
            chapter.ToGraphModel("chapters/ch01.xlsx").Errors,
            item => item.Code == ChapterDiagnosticCode.SceneHasManyEntries);
    });

    [Fact]
    public void 장면을_지워도_에피소드와_글은_남는다() => HeadlessUi.Run(() =>
    {
        // ⛔ 장면은 담는 그릇이 아니라 에피소드에 붙은 이름표다. 이름표를 뗀다고 작가가
        //    쓴 글이 사라질 이유가 없다.
        (ScriptView view, AuthoringSession session) = Show();
        Chapter(session, "ch01", "ep01");
        session.Editor.UpdateEpisode("ch01", "ep01", sceneId: "opening");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Menu(view, SceneTreeRowKind.Scene, "장면 삭제…", "opening");
        view.ConfirmButton!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ChapterEpisode kept = Assert.Single(session.Editor.FindChapter("ch01")!.Episodes);

        Assert.Equal("ep01", kept.EpisodeId);
        Assert.True(string.IsNullOrEmpty(kept.SceneId));
        Assert.DoesNotContain(Tree(view).Rows, row => row.SceneId == "opening");
    });

    [Fact]
    public void 빈_장면_닫기는_아무것도_안_지운다() => HeadlessUi.Run(() =>
    {
        (ScriptView view, AuthoringSession session) = Show();
        Chapter(session, "ch01", "ep01");

        Menu(view, SceneTreeRowKind.Chapter, "장면 추가");
        string sceneId = Assert.Single(Tree(view).Rows, row => row.IsDraft).SceneId!;

        Menu(view, SceneTreeRowKind.Scene, "빈 장면 닫기", sceneId);

        // 확인을 묻지 않는다 — 지울 것이 없다.
        Assert.Null(view.ConfirmButton);
        Assert.DoesNotContain(Tree(view).Rows, row => row.IsDraft);
        Assert.Single(session.Editor.FindChapter("ch01")!.Episodes);
    });

    [Fact]
    public void 챕터_삭제는_한_번_더_눌러야_지운다() => HeadlessUi.Run(() =>
    {
        (ScriptView view, AuthoringSession session) = Show();

        // ⚠ 둘을 세운다 — <b>마지막 판은 지울 수 없다</b>(새 노드가 갈 자리가 없어진다).
        Chapter(session, "ch01", "ep01");
        Chapter(session, "ch02", "ep01");

        Menu(view, SceneTreeRowKind.Chapter, "챕터 삭제…");

        // 차림표를 누른 것은 첫 걸음일 뿐이다 — 아직 그대로다.
        Assert.NotNull(session.Editor.FindChapter("ch01"));
        Assert.NotNull(view.ConfirmButton);

        view.ConfirmButton!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Null(session.Editor.FindChapter("ch01"));
        Assert.Equal(["ch02"], Rows(view, SceneTreeRowKind.Chapter));
    });

    [Fact]
    public void 빈_자리를_우클릭하면_챕터_추가가_뜬다() => HeadlessUi.Run(() =>
    {
        // ⚠ 머리글의 [＋]와 같은 일이지만, 트리가 비었을 때 사람이 먼저 누르는 것은
        //    <b>비어 있는 그 자리</b>다 (2026-09-18 소유자).
        (ScriptView view, _) = Show();

        ContextMenu menu = Tree(view).FindControl<ScrollViewer>("TreeScroll")!.ContextMenu!;

        MenuItem add = Assert.Single(
            menu.ItemsSource!.OfType<MenuItem>(),
            item => string.Equals(item.Header as string, "챕터 추가", StringComparison.Ordinal));

        add.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 이름을 받는 플라이아웃이 열린다 — 여기서 바로 만들지 않는다(머리글과 같은 길).
        Assert.NotNull(view.FindControl<Button>("ChapterAddButton"));
    });

    // ── 에피소드 줄의 차림표 (2026-09-18 소유자) ──────────────────────────

    [Fact]
    public void 에피소드_줄에서_더하면_그_뒤에_붙는다() => HeadlessUi.Run(() =>
    {
        // ⚠ 누른 줄이 <b>어디에 붙일지</b>를 정한다 — 장면 줄은 그 장면의 끝, 에피소드
        //    줄은 그 뒤. 이야기를 쓰다가 "여기 한 칸 더"가 이 자리다.
        (ScriptView view, AuthoringSession session) = Show();
        Chapter(session, "ch01", "ep01", "ep02");

        Menu(view, SceneTreeRowKind.Episode, "에피소드 추가", episodeId: "ep01");

        ChapterDocument chapter = session.Editor.FindChapter("ch01")!;
        ChapterEpisode made = Assert.Single(
            chapter.Episodes, episode => episode.EpisodeId.StartsWith("new", StringComparison.Ordinal));

        // 끝이 아니라 <b>누른 것 뒤</b>에 붙었다 — 간선이 그것을 말한다.
        Assert.Contains(chapter.Edges, edge =>
            edge.FromEpisodeId == "ep01" && edge.ToEpisodeId == made.EpisodeId);

        // 그리고 바로 쓸 수 있다 — 카드와 대본이 함께 선다.
        Assert.Contains(
            session.Project.EnumerateNodes().OfType<DialogueNode>(),
            node => EpisodeNaming.EpisodeIdOf(node) == made.EpisodeId && node.ScriptId is not null);
    });

    [Fact]
    public void 에피소드_삭제도_한_번_더_눌러야_지운다() => HeadlessUi.Run(() =>
    {
        // 사라지는 것이 줄거리 한 칸이 아니라 <b>원고</b>다 — 되돌리기가 없는 쪽이다.
        (ScriptView view, AuthoringSession session) = Show();
        Chapter(session, "ch01", "ep01", "ep02");

        Menu(view, SceneTreeRowKind.Episode, "에피소드 삭제…", episodeId: "ep01");

        Assert.Contains("ep01", session.Editor.FindChapter("ch01")!.Episodes.Select(e => e.EpisodeId));
        Assert.NotNull(view.ConfirmButton);

        view.ConfirmButton!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain("ep01", session.Editor.FindChapter("ch01")!.Episodes.Select(e => e.EpisodeId));

        // 비어 있던 카드도 함께 걷힌다 — 남으면 사라진 에피소드를 사칭하는 유령이 된다.
        Assert.DoesNotContain(
            session.Project.EnumerateNodes().OfType<DialogueNode>(),
            node => EpisodeNaming.EpisodeIdOf(node) == "ep01");

        Assert.DoesNotContain(Rows(view, SceneTreeRowKind.Episode), id => id == "ep01");
    });

    // ── 이름 고치기 (줄을 더블클릭) ────────────────────────────────────────

    [Fact]
    public void 에피소드_이름을_고치면_대본과_대사_노드가_따라간다() => HeadlessUi.Run(() =>
    {
        // ⛔ 규율은 `EpisodeRenamer` 하나다 — [챕터 그래프]의 이름 칸과 같은 길을 지난다.
        //    사본을 뜨면 한쪽만 노드를 따라가게 되고, 그러면 원고가 고아가 된다.
        (ScriptView view, AuthoringSession session) = Show();
        Chapter(session, "ch01", "ep01");

        // ⚠ 카드는 `Chapter(...)`가 부른 `AddEpisode`가 이미 세웠다 (2026-09-18) — 여기서
        //    또 만들면 이름이 같은 카드가 둘이 되고, 개명이 그중 하나만 따라간다.
        DialogueNode node = session.Project.EnumerateNodes().OfType<DialogueNode>()
            .Single(item => Vn.Authoring.Chapters.EpisodeNaming.EpisodeIdOf(item) == "ep01");

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Rename(view, SceneTreeRowKind.Episode, "prologue");

        Assert.Equal(["prologue"], session.Editor.FindChapter("ch01")!.Episodes.Select(e => e.EpisodeId));
        Assert.Equal("prologue", node.MarkedEpisodeId);
        Assert.Equal("prologue", node.Name);
    });

    [Fact]
    public void 장면_이름을_고치면_그_장면의_에피소드가_전부_따라간다() => HeadlessUi.Run(() =>
    {
        // 장면에는 고칠 제 칸이 없다 — 이름표이기 때문이다. 붙은 것을 한 번에 바꾼다.
        (ScriptView view, AuthoringSession session) = Show();
        Chapter(session, "ch01", "ep01", "ep02");
        session.Editor.UpdateEpisodeScenes("ch01", ["ep01", "ep02"], "opening");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Rename(view, SceneTreeRowKind.Scene, "classroom");

        Assert.All(
            session.Editor.FindChapter("ch01")!.Episodes,
            episode => Assert.Equal("classroom", episode.SceneId));
    });

    [Fact]
    public void 이름을_비우거나_Esc를_누르면_없던_일이_된다() => HeadlessUi.Run(() =>
    {
        (ScriptView view, AuthoringSession session) = Show();
        Chapter(session, "ch01", "ep01");

        ChapterSceneTree tree = Tree(view);
        int index = tree.Rows.Select((row, at) => (row, at))
            .First(item => item.row.Kind == SceneTreeRowKind.Episode).at;

        tree.GetVisualDescendants().OfType<Button>().ElementAt(index)
            .RaiseEvent(new Avalonia.Input.TappedEventArgs(
                Avalonia.Controls.Control.DoubleTappedEvent, null!));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        tree.RenameBox!.Text = "안바꿈";
        tree.RenameBox.RaiseEvent(new Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
            Key = Avalonia.Input.Key.Escape
        });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(["ep01"], session.Editor.FindChapter("ch01")!.Episodes.Select(e => e.EpisodeId));
        Assert.Null(tree.RenameBox);
    });

    // ── 끌어다 놓기 ────────────────────────────────────────────────────────

    [Fact]
    public void 장면을_다른_챕터에_놓으면_에피소드째_간다() => HeadlessUi.Run(() =>
    {
        (ScriptView view, AuthoringSession session) = Show();
        Chapter(session, "ch01", "ep01", "ep02");
        Chapter(session, "ch02");
        session.Editor.UpdateEpisodeScenes("ch01", ["ep01", "ep02"], "opening");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ChapterSceneTree tree = Tree(view);

        tree.Drag(
            tree.Rows.Single(row => row.Kind == SceneTreeRowKind.Scene && row.SceneId == "opening"),
            tree.Rows.Single(row => row.Kind == SceneTreeRowKind.Chapter && row.ChapterId == "ch02"));

        Assert.Empty(session.Editor.FindChapter("ch01")!.Episodes);
        Assert.Equal(
            ["ep01", "ep02"],
            session.Editor.FindChapter("ch02")!.Episodes.Select(episode => episode.EpisodeId));

        // 장면 이름이 따라갔다 — 장면째 옮긴 것이니 다른 장면이 되면 안 된다.
        Assert.All(
            session.Editor.FindChapter("ch02")!.Episodes,
            episode => Assert.Equal("opening", episode.SceneId));
    });

    [Fact]
    public void 에피소드를_다른_장면에_놓으면_그_장면이_된다() => HeadlessUi.Run(() =>
    {
        (ScriptView view, AuthoringSession session) = Show();
        Chapter(session, "ch01", "ep01", "ep02");
        session.Editor.UpdateEpisodeScenes("ch01", ["ep01"], "opening");
        session.Editor.UpdateEpisodeScenes("ch01", ["ep02"], "classroom");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ChapterSceneTree tree = Tree(view);

        tree.Drag(
            tree.Rows.Single(row => row.EpisodeId == "ep02"),
            tree.Rows.Single(row => row.Kind == SceneTreeRowKind.Scene && row.SceneId == "opening"));

        Assert.All(
            session.Editor.FindChapter("ch01")!.Episodes,
            episode => Assert.Equal("opening", episode.SceneId));
    });

    [Fact]
    public void 손을_뗀_자리로_놓는다_누른_단추가_아니라() => HeadlessUi.Run(() =>
    {
        // ⛔ 2026-09-16 소유자: "여전히 드래그 기능은 안돼".
        //
        //    Button은 눌리는 순간 <b>포인터를 잡는다</b>. 그래서 손을 어디서 떼든 놓임
        //    이벤트는 <b>누른 그 단추</b>로 온다 — 도착을 "이벤트가 온 컨트롤"로 찾으면
        //    출발과 도착이 늘 같아 보여 드롭이 한 번도 성립하지 않는다. 좌표로 찾는다.
        //
        //    이 테스트는 그 상황을 그대로 만든다: 놓임을 <b>출발 단추에</b> 보내되 자리는
        //    도착 줄 위다.
        (ScriptView view, AuthoringSession session) = Show();
        Chapter(session, "ch01", "ep01", "ep02");
        session.Editor.UpdateEpisodeScenes("ch01", ["ep01"], "opening");
        session.Editor.UpdateEpisodeScenes("ch01", ["ep02"], "classroom");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ChapterSceneTree tree = Tree(view);

        int from = Index(tree, row => row.EpisodeId == "ep02");
        int onto = Index(tree, row => row.Kind == SceneTreeRowKind.Scene && row.SceneId == "opening");

        Button source = tree.GetVisualDescendants().OfType<Button>().ElementAt(from);
        Control landing = tree.GetVisualDescendants().OfType<DockPanel>().ElementAt(onto);

        // ⚠ 포인터 이벤트의 자리는 <b>최상위 기준</b>이다 — 진짜 포인터가 그렇게 온다.
        //   트리 기준으로 주면 좌표가 통째로 어긋나 엉뚱한 줄을 짚는다.
        var root = (Visual)TopLevel.GetTopLevel(tree)!;
        Point at = landing.TranslatePoint(new Point(20, 4), root)!.Value;

        source.RaiseEvent(new PointerPressedEventArgs(
            source, new Pointer(0, PointerType.Mouse, true), root, default, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None));

        source.RaiseEvent(new PointerReleasedEventArgs(
            source, new Pointer(0, PointerType.Mouse, true), root, at, 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None, MouseButton.Left));

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.All(
            session.Editor.FindChapter("ch01")!.Episodes,
            episode => Assert.Equal("opening", episode.SceneId));
    });

    [Fact]
    public void 놓을_수_없는_자리에는_안_받는다() => HeadlessUi.Run(() =>
    {
        // ⚠ 에피소드를 챕터에 놓는 것은 안 받는다 — 장면을 안 정한 채로 남는데, 그것은
        //   "미지정"이라는 뜻이 되어 사람이 의도한 것과 다를 수 있다.
        (ScriptView view, AuthoringSession session) = Show();
        Chapter(session, "ch01", "ep01");
        Chapter(session, "ch02");
        session.Editor.UpdateEpisodeScenes("ch01", ["ep01"], "opening");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ChapterSceneTree tree = Tree(view);

        SceneTreeRow episode = tree.Rows.Single(row => row.EpisodeId == "ep01");
        SceneTreeRow chapter = tree.Rows.Single(row =>
            row.Kind == SceneTreeRowKind.Chapter && row.ChapterId == "ch02");

        Assert.False(ChapterSceneTree.Accepts(episode, chapter));

        tree.Drag(episode, chapter);

        Assert.Single(session.Editor.FindChapter("ch01")!.Episodes);
        Assert.Empty(session.Editor.FindChapter("ch02")!.Episodes);
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <summary>그 줄을 더블클릭하고 새 이름을 적어 Enter — 사람이 하는 길 그대로다.</summary>
    private static void Rename(ScriptView view, SceneTreeRowKind kind, string wanted)
    {
        ChapterSceneTree tree = Tree(view);

        int index = tree.Rows.Select((row, at) => (row, at))
            .First(item => item.row.Kind == kind).at;

        tree.GetVisualDescendants().OfType<Button>().ElementAt(index)
            .RaiseEvent(new Avalonia.Input.TappedEventArgs(
                Avalonia.Controls.Control.DoubleTappedEvent, null!));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        tree.RenameBox!.Text = wanted;
        tree.RenameBox.RaiseEvent(new Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
            Key = Avalonia.Input.Key.Enter
        });

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>그 줄이 몇 번째인가 — 그려진 컨트롤과 줄 목록은 같은 순서다.</summary>
    private static int Index(ChapterSceneTree tree, Func<SceneTreeRow, bool> match) =>
        tree.Rows.Select((row, at) => (row, at)).First(item => match(item.row)).at;

    private static ChapterSceneTree Tree(ScriptView view) =>
        view.FindControl<ChapterSceneTree>("EpisodeTree")!;

    private static IReadOnlyList<string> Rows(ScriptView view, SceneTreeRowKind kind) =>
        Tree(view).Rows.Where(row => row.Kind == kind)
            .Select(row => row.EpisodeId ?? row.SceneId ?? row.ChapterId)
            .ToList();

    /// <summary>그 줄을 우클릭해 차림표의 그 항목을 누른다 — 사람이 하는 길 그대로다.</summary>
    private static void Menu(
        ScriptView view, SceneTreeRowKind kind, string header,
        string? sceneId = null, string? episodeId = null)
    {
        ChapterSceneTree tree = Tree(view);

        int index = tree.Rows
            .Select((row, at) => (row, at))
            .First(item => item.row.Kind == kind &&
                           (sceneId is null || item.row.SceneId == sceneId) &&
                           (episodeId is null || item.row.EpisodeId == episodeId)).at;

        ContextMenu menu = tree.GetVisualDescendants().OfType<Button>().ElementAt(index).ContextMenu!;

        menu.ItemsSource!.OfType<MenuItem>()
            .Single(item => string.Equals(item.Header as string, header, StringComparison.Ordinal))
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>[＋]가 지나는 그 길로 챕터를 세운다 — 판까지 함께 서야 실제와 같다.</summary>
    private static void Chapter(AuthoringSession session, string chapterId, params string[] episodeIds)
    {
        Assert.Null(session.CreateChapter(chapterId));

        foreach (string episodeId in episodeIds)
        {
            session.Editor.AddEpisode(chapterId, episodeId, title: episodeId, 0, 0);
        }

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private (ScriptView View, AuthoringSession Session) Show()
    {
        var session = new AuthoringSession();
        session.Open(ManifestPath);

        var view = new ScriptView();
        var window = new Window { Width = 1100, Height = 700, Content = view };
        window.Show();
        view.Attach(session);

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (view, session);
    }
}
