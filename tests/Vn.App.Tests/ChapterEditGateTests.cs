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
/// 편집 스위치와 자동 저장 (2026-08-17 소유자) — ① "엑셀이 열려있으면 그냥 체크 안되게
/// 해버려. 하는 중에 엑셀이 열리면 현재까지된걸 저장한 뒤에 잠그고" ② "간선을 편집할 때
/// 굳이 적용을 누르지 않아도 바로 변화가 반영되도록".
/// </summary>
public sealed class ChapterEditGateTests
{
    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    // ── 엑셀이 잡고 있으면 편집 스위치가 잠긴다 ────────────────────────────

    [Fact]
    public void 엑셀이_열고_있으면_체크를_풀_수_없다() => HeadlessUi.Run(() =>
    {
        // 전에는 풀 수는 있는데 누르는 족족 거부됐다 — 열 수 없는 문을 흔들게 두는 셈이었다.
        using var project = new TempProject();
        (ChapterGraphView view, _) = Show(project, locked: true);

        var check = view.FindControl<CheckBox>("ExcelOnlyCheck")!;

        Assert.False(check.IsEnabled);
        Assert.True(check.IsChecked);
        Assert.True(view.FindControl<Border>("LockBanner")!.IsVisible);
    });

    [Fact]
    public void 편집_중에_엑셀이_열리면_쓰던_값을_먼저_내고_잠근다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject();
        (ChapterGraphView view, _) = Show(project, locked: false);

        var check = view.FindControl<CheckBox>("ExcelOnlyCheck")!;
        check.IsChecked = false;

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
        Assert.False(check.IsEnabled);
        Assert.True(check.IsChecked);
    });

    [Fact]
    public void 엑셀이_닫히면_툴이_가져간_스위치를_돌려준다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject();
        (ChapterGraphView view, _) = Show(project, locked: false);

        var check = view.FindControl<CheckBox>("ExcelOnlyCheck")!;
        check.IsChecked = false;

        view.LockProbe = _ => true;
        view.RefreshFromDisk();
        Assert.True(check.IsChecked);

        view.LockProbe = _ => false;
        view.RefreshFromDisk();

        Assert.False(check.IsChecked);   // 스스로 켠 것만 되돌린다
        Assert.True(check.IsEnabled);
    });

    [Fact]
    public void 사람이_켜_둔_체크는_엑셀이_닫혀도_그대로다() => HeadlessUi.Run(() =>
    {
        // 툴이 만지지 않은 스위치를 툴이 돌려놓으면 안 된다.
        using var project = new TempProject();
        (ChapterGraphView view, _) = Show(project, locked: true);

        var check = view.FindControl<CheckBox>("ExcelOnlyCheck")!;
        Assert.True(check.IsChecked);

        view.LockProbe = _ => false;
        view.RefreshFromDisk();

        Assert.True(check.IsChecked);
        Assert.True(check.IsEnabled);
    });

    // ── 간선 패널은 고치는 순간 저장된다 ───────────────────────────────────

    [Fact]
    public void 조건을_고르면_적용_없이_엑셀에_써진다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject();
        (ChapterGraphView view, _) = Show(project, locked: false);
        view.FindControl<CheckBox>("ExcelOnlyCheck")!.IsChecked = false;

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
        view.FindControl<CheckBox>("ExcelOnlyCheck")!.IsChecked = false;

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
        view.FindControl<CheckBox>("ExcelOnlyCheck")!.IsChecked = false;

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
    public void 읽기_전용이면_고쳐도_안_써진다() => HeadlessUi.Run(() =>
    {
        // [엑셀에서만 편집]이 켜져 있으면 자동 저장도 울리지 않는다.
        using var project = new TempProject();
        (ChapterGraphView view, _) = Show(project, locked: false);

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

        public TempProject()
        {
            _directory = Path.Combine(
                Path.GetTempPath(), "vn-edit-gate", Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path.Combine(_directory, ChapterLibrary.FolderName));
            File.Copy(SamplePath, ChapterPath);

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
