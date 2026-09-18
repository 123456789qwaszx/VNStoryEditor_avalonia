using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// 챕터 그래프가 <b>분기를 보여 주고 그 조건을 받는다</b> (2026-09-18 소유자: *"챕터 그래프가
/// 보면, 분기는 선택지로 안 이어 주고 있거든?"* · *"어떤 조건일 때 그 분기를 탈 수 있는지는
/// 기존 챕터그래프의 우측탭에서 하던것과 비슷하게"*).
///
/// ⛔ <b>선택지가 아니다.</b> 문구도 스탯변화도 없고, 표식을 만들거나 떼지도 않는다 —
/// 그것은 연출 그래프의 일이다.
/// </summary>
public sealed class ChapterBranchPanelTests
{
    [Fact]
    public void 분기는_점선으로_그려진다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject();
        (ChapterGraphView view, _) = Show(project);

        Line drawn = Assert.Single(
            view.FindControl<Canvas>("GraphCanvas")!.Children.OfType<Line>(),
            line => (line.Tag as string)?.StartsWith("분기:", StringComparison.Ordinal) == true);

        // 점선인 것이 뜻이다 — 사람이 고르는 길(실선)이 아니라 반드시 다녀오는 통로다.
        Assert.NotNull(drawn.StrokeDashArray);
        Assert.NotEmpty(drawn.StrokeDashArray!);
    });

    [Fact]
    public void 분기를_고르면_조건_칸이_열린다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject();
        (ChapterGraphView view, _) = Show(project);

        Assert.False(view.FindControl<StackPanel>("BranchPanel")!.IsVisible);

        view.SelectBranch(project.BranchLineId);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(view.FindControl<StackPanel>("BranchPanel")!.IsVisible);

        // 선택지 패널·에피소드 패널은 닫힌다 — 패널이 무엇을 편집하는지 애매하면 안 된다.
        Assert.False(view.FindControl<StackPanel>("EdgePanel")!.IsVisible);
        Assert.False(view.FindControl<StackPanel>("PropertyPanel")!.IsVisible);

        var combo = view.FindControl<ComboBox>("BranchConditionCombo")!;

        Assert.True(combo.IsEnabled);
        Assert.Equal("(언제나)", combo.SelectedItem);          // 아직 안 걸었다
        Assert.Contains("신뢰높음", (IEnumerable<string>)combo.ItemsSource!);
    });

    [Fact]
    public void 조건을_고르면_다녀갈_에피소드의_첫머리에_적힌다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject();
        (ChapterGraphView view, AuthoringSession session) = Show(project);

        view.SelectBranch(project.BranchLineId);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        view.FindControl<ComboBox>("BranchConditionCombo")!.SelectedItem = "신뢰높음";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        DialogueNode branch = EpisodeNaming.CardFor(session.Project, "ch01", project.BranchEpisodeId)!;

        Assert.Equal("신뢰높음",
            BranchEntryCondition.Read(session.Project, branch.Id, session.Definition).Label);

        // ⛔ 부르는 쪽은 안 바뀐다 — 조건은 다녀간 쪽에만 산다.
        DialogueNode caller = EpisodeNaming.CardFor(session.Project, "ch01", "root")!;
        Assert.Empty(caller.TrailingTransitions);
    });

    [Fact]
    public void 되돌리기_한_번이면_조건이_걷힌다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject();
        (ChapterGraphView view, AuthoringSession session) = Show(project);

        view.SelectBranch(project.BranchLineId);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        view.FindControl<ComboBox>("BranchConditionCombo")!.SelectedItem = "신뢰높음";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        session.Editor.Undo();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        DialogueNode branch = EpisodeNaming.CardFor(session.Project, "ch01", project.BranchEpisodeId)!;

        Assert.Null(BranchEntryCondition.Read(session.Project, branch.Id, session.Definition).Label);
    });

    [Fact]
    public void 손으로_지은_구조는_칸이_잠기고_사유가_선다() => HeadlessUi.Run(() =>
    {
        // ⛔ 작가가 대사 편집기에서 갈래를 얹었다면 덮는 순간 짝이 어긋난 대본이 된다.
        using var project = new TempProject();
        (ChapterGraphView view, AuthoringSession session) = Show(project);

        DialogueNode branch = EpisodeNaming.CardFor(session.Project, "ch01", project.BranchEpisodeId)!;
        string firstLineId = session.Project.FindScript(branch.ScriptId)!.ActiveLines.First().Id;

        session.Editor.SetLineTransitions(
            branch.Id, firstLineId, [LineConditionTransition.BeginChoice()]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        view.SelectBranch(project.BranchLineId);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(view.FindControl<ComboBox>("BranchConditionCombo")!.IsEnabled);

        var blocked = view.FindControl<TextBlock>("BranchBlockedText")!;

        Assert.True(blocked.IsVisible);
        Assert.False(string.IsNullOrWhiteSpace(blocked.Text));
    });

    private static (ChapterGraphView View, AuthoringSession Session) Show(TempProject project)
    {
        var session = new AuthoringSession();
        session.Open(project.ManifestPath);

        var view = new ChapterGraphView { LockProbe = _ => false };
        var window = new Window { Width = 1400, Height = 800, Content = view };
        window.Show();
        view.Attach(session);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        project.Ui.Own(view, window);

        return (view, session);
    }

    /// <summary>에피소드 하나에서 분기 하나를 뚫어 둔 프로젝트. 조건 `신뢰높음`이 공급돼 있다.</summary>
    private sealed class TempProject : IDisposable
    {
        private readonly string _directory;

        public OpenChapterViews Ui { get; } = new();

        public TempProject()
        {
            _directory = Path.Combine(
                Path.GetTempPath(), "vn-branch-panel", Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(_directory);

            var editor = new ProjectEditor(new StoryProject { Title = "분기 검증" });

            editor.EnsureChapter("ch01");
            string fileId = editor.EnsureChapterBoard("ch01");
            editor.AddEpisode("ch01", "root", title: "root", 0, 0);

            ChapterDocument chapter = editor.FindChapter("ch01")!;
            chapter.Stats.Add(new ChapterStat("trust", "신뢰", 0, 0, 10, SourceRow: 2));
            editor.AddChapterCondition("ch01", "신뢰높음", "trust >= 3");

            ChapterBoardSupply.SupplyChapterConditionsToBoard(
                editor, Vn.Authoring.Definition.GameDefinition.Empty, fileId,
                chapter.ToGraphModel("ch01.xlsx"));

            DialogueNode caller = EpisodeNaming.CardFor(editor.Project, "ch01", "root")!;

            BranchLineId = editor.Project.FindScript(caller.ScriptId)!.ActiveLines.First().Id;
            BranchEpisodeId = EpisodeNaming.EpisodeIdOf(
                editor.AddBranchMarker(caller.Id, BranchLineId));

            ManifestPath = Path.Combine(_directory, "project" + ProjectManifestJson.FileExtension);
            ProjectStore.Save(ManifestPath, editor.Project);
        }

        public string ManifestPath { get; }

        public string BranchLineId { get; }

        public string BranchEpisodeId { get; }

        public void Dispose()
        {
            Ui.CloseAll();

            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
