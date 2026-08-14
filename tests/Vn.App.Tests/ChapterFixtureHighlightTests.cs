using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// Gate C 2번 — 픽스처 전환 시 경로 하이라이트가 바뀐다. 화면 없는 렌더로 닫는다.
/// (내보내기에 픽스처가 섞이지 않는 것은 Vn.Authoring 쪽 테스트가 고정한다.)
/// </summary>
public sealed class ChapterFixtureHighlightTests
{
    private static readonly Color PathGreen = Color.Parse("#3E9B57");

    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    [Fact]
    public void 픽스처를_바꾸면_하이라이트된_간선이_바뀐다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        ChapterGraphView view = Show(project);

        var combo = view.FindControl<ComboBox>("FixtureCombo")!;

        // 시트의 활성 픽스처(기본 루트)가 기본으로 골라져 있다.
        Assert.Equal("기본 루트", combo.SelectedItem);

        // 간선 표식은 라벨까지 담는다 (2026-08-15 — 신원 = 출발·도착·라벨).
        string[] basicPath = HighlightedEdges(view);
        Assert.Contains("main05.02→main05.03 [혼자 문을 연다]", basicPath);
        Assert.DoesNotContain(basicPath, tag => tag.StartsWith("main05.02→branch05.02A"));

        // 신뢰 루트로 전환 → 경로가 신뢰 분기로 바뀐다.
        combo.SelectedItem = "신뢰 루트";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        string[] trustPath = HighlightedEdges(view);
        Assert.Contains("main05.02→branch05.02A [라루의 제안을 듣는다]", trustPath);
        Assert.Contains("branch05.02A→main05.03", trustPath);
        Assert.DoesNotContain(trustPath, tag => tag.StartsWith("main05.02→main05.03"));

        // 끄면 하이라이트가 없다.
        combo.SelectedItem = "(끄기)";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Empty(HighlightedEdges(view));
    });

    private static string[] HighlightedEdges(ChapterGraphView view)
    {
        Canvas canvas = view.FindControl<Canvas>("GraphCanvas")!;

        return canvas.Children.OfType<Line>()
            .Where(line => line.Stroke is SolidColorBrush brush && brush.Color == PathGreen)
            .Select(line => (string)line.Tag!)
            .ToArray();
    }

    private static ChapterGraphView Show(TempProject project)
    {
        var session = new AuthoringSession();
        session.Open(project.ManifestPath);

        var view = new ChapterGraphView();
        var window = new Window { Width = 1280, Height = 800, Content = view };
        window.Show();
        view.Attach(session);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return view;
    }

    private sealed class TempProject : IDisposable
    {
        private readonly string _directory;

        public TempProject(string samplePath)
        {
            _directory = Path.Combine(
                Path.GetTempPath(), "vn-fixture-highlight", Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path.Combine(_directory, ChapterLibrary.FolderName));
            File.Copy(samplePath, Path.Combine(_directory, ChapterLibrary.FolderName, "ch05.xlsx"));

            ManifestPath = Path.Combine(_directory, "project" + ProjectManifestJson.FileExtension);
            var project = new StoryProject { Title = "픽스처 검증" };
            project.Files.Add(new StoryFile("sf_main", "본편", "story/main.vnstory.json"));
            ProjectStore.Save(ManifestPath, project);
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
