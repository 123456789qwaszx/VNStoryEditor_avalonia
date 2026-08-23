using Avalonia.Controls;
using Avalonia.Interactivity;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// <b>무엇이 편집을 잠그나</b>, 그리고 자동 저장.
///
/// 2026-08-17 소유자 — ① "엑셀이 열려있으면 그냥 체크 안되게 해버려. 하는 중에 엑셀이
/// 열리면 현재까지된걸 저장한 뒤에 잠그고" ② "간선을 편집할 때 굳이 적용을 누르지 않아도
/// 바로 변화가 반영되도록".
///
/// 2026-08-24 소유자 — "저걸 툴사용자가 체크하는 게 아니라, 엑셀이 켜지면 자동으로
/// 잠기면서 편집이 불가능하게 막는게 좋겠어." 그래서 [엑셀에서만 편집] 체크가 사라졌다.
/// ①의 요구는 그대로 남고 <b>더 단순해졌다</b>: 풀 수 없는 체크를 두는 대신 체크를 없앴다.
/// </summary>
public sealed class ChapterEditGateTests
{
    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    // ── 엑셀이 잡고 있는 동안만 잠긴다 ─────────────────────────────────────

    [Fact]
    public void 엑셀이_열고_있으면_편집이_잠긴다() => HeadlessUi.Run(() =>
    {
        // ⛔ 이 잠금을 풀 수 있는 스위치는 화면에 없다. 예전에는 있었고, 풀어도 누르는
        // 족족 거부됐다 — 열 수 없는 문을 흔들게 두는 셈이었다.
        using var project = new TempProject();
        (ChapterGraphView view, _) = Show(project, locked: true);

        Assert.True(view.FindControl<Border>("LockBanner")!.IsVisible);

        view.SelectEpisode("main05.02");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(view.FindControl<TextBox>("IdBox")!.IsEnabled);
        Assert.False(view.FindControl<Button>("AddEpisodeButton")!.IsEnabled);
    });

    [Fact]
    public void 편집_중에_엑셀이_열리면_쓰던_값을_먼저_내고_잠근다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject();
        (ChapterGraphView view, _) = Show(project, locked: false);

        view.SelectEdgeKey("main05.02", "branch05.02A", "라루의 제안을 듣는다");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 아직 안 낸 값을 패널에 담아 둔다 — 글자 칸은 초점을 잃어야 나간다.
        view.FindControl<TextBox>("EdgeLockedMsgBox")!.Text = "신뢰가 부족하다";

        // 그 사이 엑셀이 열렸다.
        view.LockProbe = _ => true;
        view.RefreshFromDisk();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 잠기기 전에 나갔다.
        ChapterEdge reread = ChapterWorkbookReader.Read(project.ChapterPath).Edges
            .Single(edge => edge.ToEpisodeId == "branch05.02A");
        Assert.Equal("신뢰가 부족하다", reread.LockedMessage);

        // 그리고 잠겼다.
        Assert.True(view.FindControl<Border>("LockBanner")!.IsVisible);
        Assert.False(view.FindControl<Button>("AddEpisodeButton")!.IsEnabled);
    });

    [Fact]
    public void 엑셀이_닫히면_저절로_다시_열린다() => HeadlessUi.Run(() =>
    {
        // 예전에는 "툴이 스스로 켠 체크만 되돌린다"는 단서가 붙었다 — 사람이 켜 둔 값을
        // 툴이 돌려놓으면 안 됐기 때문이다. 사람이 켤 것이 없어진 지금은 단서도 없다.
        using var project = new TempProject();
        (ChapterGraphView view, _) = Show(project, locked: false);

        view.SelectEpisode("main05.02");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        view.LockProbe = _ => true;
        view.RefreshFromDisk();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(view.FindControl<TextBox>("IdBox")!.IsEnabled);

        view.LockProbe = _ => false;
        view.RefreshFromDisk();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(view.FindControl<Border>("LockBanner")!.IsVisible);
        Assert.True(view.FindControl<TextBox>("IdBox")!.IsEnabled);
    });

    [Fact]
    public void 잠금이_그대로면_상태줄에_같은_말을_되풀이하지_않는다() => HeadlessUi.Run(() =>
    {
        // 이 자리는 다시 그리기·쓰기 결과·감시자 알림마다 불린다. 매번 말하면
        // "엑셀이 닫혔습니다"가 끝없이 흐른다 — 잠금이 <b>움직였을 때만</b> 말한다.
        using var project = new TempProject();
        (ChapterGraphView view, AuthoringSession session) = Show(project, locked: false);

        session.SetStatus("조용한 상태줄");

        view.RefreshFromDisk();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("조용한 상태줄", session.StatusMessage);
    });

    [Fact]
    public void 다른_챕터로_옮기면_그_챕터의_잠금을_다시_묻는다() => HeadlessUi.Run(() =>
    {
        // 엑셀이 ch05를 잡고 있어도 ch06은 남의 일이다. 다시 묻지 않으면 <b>ch06이 잠긴
        // 채로</b> 보인다 — 편집을 사람이 아니라 잠금이 여닫게 된 뒤로는 그것이 곧
        // "ch06을 못 고친다"가 된다.
        //
        // ⚠ 이 규칙을 지고 있는 것은 <b>Draw()의 마지막 줄</b>이다
        // (RefreshPropertyPanel → ApplyEditability → RefreshLockBanner). SelectChapter에
        // 물음을 한 줄 더 두려다 <b>이 테스트가 그대로 통과해서</b> 되물렀다 — 이미 묻고
        // 있었다. 같은 물음을 두 곳에 두는 대신 여기서 붙든다: 그 줄이 사라지면 이 테스트가
        // 먼저 말한다.
        using var project = new TempProject(secondChapter: true);

        var session = new AuthoringSession();
        session.Open(project.ManifestPath);

        var view = new ChapterGraphView
        {
            LockProbe = path => Path.GetFileName(path ?? string.Empty) == "ch05.xlsx"
        };

        var window = new Window { Width = 1400, Height = 800, Content = view };
        window.Show();
        view.Attach(session);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        view.SelectChapter("ch05");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(view.FindControl<Border>("LockBanner")!.IsVisible);

        view.SelectChapter("ch06");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(view.FindControl<Border>("LockBanner")!.IsVisible);

        view.SelectEpisode("main05.02");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(view.FindControl<TextBox>("IdBox")!.IsEnabled);

        window.Close();
    });

    // ── 간선 패널은 고치는 순간 저장된다 ───────────────────────────────────

    [Fact]
    public void 조건을_고르면_적용_없이_엑셀에_써진다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject();
        (ChapterGraphView view, _) = Show(project, locked: false);

        view.SelectEdgeKey("main05.02", "branch05.02A", "라루의 제안을 듣는다");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        view.FindControl<ComboBox>("EdgeVisibleCombo")!.SelectedItem = "지쳐있음";

        Assert.Equal("지쳐있음", ChapterWorkbookReader.Read(project.ChapterPath).Edges
            .Single(edge => edge.ToEpisodeId == "branch05.02A").VisibleConditionLabel);
    });

    [Fact]
    public void 문구를_바꾼_직후에_고른_조건도_써진다() => HeadlessUi.Run(() =>
    {
        // 문구는 간선의 신원이다 — 한 번 고치면 손에 든 모델이 낡는다. 그 상태로 다음
        // 저장을 부르면 간선을 못 찾아 조건이 조용히 안 써졌다(이 테스트가 잡았다).
        using var project = new TempProject();
        (ChapterGraphView view, _) = Show(project, locked: false);

        view.SelectEdgeKey("main05.02", "branch05.02A", "라루의 제안을 듣는다");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        view.FindControl<ComboBox>("EdgeLabelEditBox")!.SelectedItem = "혼자 문을 연다";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        view.FindControl<ComboBox>("EdgeVisibleCombo")!.SelectedItem = "지쳐있음";

        ChapterEdge reread = ChapterWorkbookReader.Read(project.ChapterPath).Edges
            .Single(edge => edge.ToEpisodeId == "branch05.02A");

        Assert.Equal("혼자 문을 연다", reread.OptionLabel);
        Assert.Equal("지쳐있음", reread.VisibleConditionLabel);
    });

    [Fact]
    public void 잠금_안내문은_초점을_잃을_때_써진다() => HeadlessUi.Run(() =>
    {
        // 자판 하나마다 워크북을 열면 엑셀 파일을 쉼 없이 두드리고, 그 사이 파일 사건이
        // 칸을 다시 채워 쓰던 글을 끊는다.
        using var project = new TempProject();
        (ChapterGraphView view, _) = Show(project, locked: false);

        view.SelectEdgeKey("main05.02", "branch05.02A", "라루의 제안을 듣는다");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        string? before = Edge(project).LockedMessage;

        var box = view.FindControl<TextBox>("EdgeLockedMsgBox")!;
        box.Focus();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        box.Text = "아직 이르다";

        Assert.Equal(before, Edge(project).LockedMessage);   // 치는 동안은 안 나간다

        // 초점을 진짜로 옮긴다 — LostFocus는 FocusChangedEventArgs를 요구한다.
        view.FindControl<ComboBox>("EdgeVisibleCombo")!.Focus();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("아직 이르다", Edge(project).LockedMessage);
    });

    [Fact]
    public void 잠겨_있으면_고쳐도_안_써진다() => HeadlessUi.Run(() =>
    {
        // 자동 저장은 잠금 위에서 돈다 — 잠긴 동안에는 아예 울리지 않는다. 화면이 막는
        // 것만으로는 모자란다: 콤보를 코드로 건드리는 길이 남아 있고, 그 길로 쓰기가
        // 나가면 엑셀이 방금 고친 셀을 툴이 덮는다.
        using var project = new TempProject();
        (ChapterGraphView view, _) = Show(project, locked: true);

        view.SelectEdgeKey("main05.02", "branch05.02A", "라루의 제안을 듣는다");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        view.FindControl<ComboBox>("EdgeVisibleCombo")!.SelectedItem = "지쳐있음";

        Assert.Null(Edge(project).VisibleConditionLabel);
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static ChapterEdge Edge(TempProject project) =>
        ChapterWorkbookReader.Read(project.ChapterPath).Edges
            .Single(edge => edge.ToEpisodeId == "branch05.02A");

    private static (ChapterGraphView View, AuthoringSession Session) Show(TempProject project, bool locked)
    {
        var session = new AuthoringSession();
        session.Open(project.ManifestPath);

        var view = new ChapterGraphView { LockProbe = _ => locked };
        var window = new Window { Width = 1400, Height = 800, Content = view };
        window.Show();
        view.Attach(session);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return (view, session);
    }

    private sealed class TempProject : IDisposable
    {
        private readonly string _directory;

        /// <param name="secondChapter">
        /// 같은 내용의 ch06을 하나 더 둔다 — 챕터를 옮길 때 잠금이 따라오는지 보는 용도다.
        /// </param>
        public TempProject(bool secondChapter = false)
        {
            _directory = Path.Combine(
                Path.GetTempPath(), "vn-edit-gate", Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path.Combine(_directory, ChapterLibrary.FolderName));
            File.Copy(SamplePath, ChapterPath);

            if (secondChapter)
            {
                File.Copy(
                    SamplePath,
                    Path.Combine(_directory, ChapterLibrary.FolderName, "ch06.xlsx"));
            }

            ManifestPath = Path.Combine(_directory, "project" + ProjectManifestJson.FileExtension);
            var project = new StoryProject { Title = "편집 스위치 검증" };
            project.Files.Add(new StoryFile("sf_main", "본편", "story/main.vnstory.json"));
            ProjectStore.Save(ManifestPath, project);
        }

        public string ManifestPath { get; }

        public string ChapterPath =>
            Path.Combine(_directory, ChapterLibrary.FolderName, "ch05.xlsx");

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
