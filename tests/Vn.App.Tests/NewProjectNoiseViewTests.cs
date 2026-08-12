using Avalonia.Controls;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// 소유자 보고 — "완전 새 프로젝트인데 검증 보고가 동기화 거부·경고를 계속 띄운다."
/// 툴이 방금 만든 빈 에피소드 워크북을 툴 스스로 거부하면 안 된다.
/// </summary>
public sealed class NewProjectNoiseViewTests
{
    [Fact]
    public void 방금_만든_빈_에피소드는_거부로_보고되지_않는다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject();
        (ChapterGraphView view, _) = Show(project);

        // [＋ 에피소드] → 노드 더블클릭(빈 워크북 생성) → 감시가 울려 동기화.
        view.AddEpisodeFromToolbar();
        view.RefreshFromDisk();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        view.OpenWorkbookFile = _ => { };
        view.WorkbookHandlerProbe = () => @"C:\Program Files\Microsoft Office\EXCEL.EXE";
        view.OpenEpisode("new01");

        view.SyncEpisodes();

        var panel = view.FindControl<StackPanel>("DiagnosticsPanel")!;
        string[] shown = panel.Children.OfType<TextBlock>()
            .Select(block => block.Text ?? string.Empty)
            .Where(text => text.Length > 0)
            .ToArray();

        Assert.DoesNotContain(shown, text => text.Contains("거부"));
    });

    private static (ChapterGraphView View, AuthoringSession Session) Show(TempProject project)
    {
        var session = new AuthoringSession();
        session.Open(project.ManifestPath);

        var view = new ChapterGraphView();
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
                Path.GetTempPath(), "vn-new-noise", Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(_directory);

            ChapterWorkbookWriter.EnsureChapterWorkbook(
                Path.Combine(_directory, ChapterLibrary.FolderName), "ch01",
                [("trust", "신뢰"), ("anger", "분노")]);

            ManifestPath = Path.Combine(_directory, "project" + ProjectManifestJson.FileExtension);
            ProjectStore.Save(ManifestPath, new StoryProject { Title = "새 프로젝트" });
        }

        public string ManifestPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
