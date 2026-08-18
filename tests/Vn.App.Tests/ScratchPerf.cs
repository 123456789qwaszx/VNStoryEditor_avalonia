using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;
using Xunit.Abstractions;
using Path = System.IO.Path;

namespace Vn.App.Tests;

public sealed class ScratchPerf(ITestOutputHelper output)
{
    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(100)]
    public void 규모별_시간(int count)
    {
        string dir = Path.Combine(Path.GetTempPath(), "vn-perf", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string manifest = Path.Combine(dir, "p" + ProjectManifestJson.FileExtension);
        string chapters = Path.Combine(dir, ChapterLibrary.FolderName);

        var sw = Stopwatch.StartNew();
        ChapterWorkbookWriter.EnsureChapterWorkbook(chapters, "ch01", [("trust", "신뢰")]);
        string path = Path.Combine(chapters, "ch01.xlsx");

        string previous = string.Empty;
        for (int i = 0; i < count; i++)
        {
            string id = $"ep{i}";
            ChapterWorkbookWriter.AddEpisode(path, id, title: $"제목{i}", i % 10, i / 10);
            if (previous.Length > 0) { ChapterWorkbookWriter.AddEdge(path, previous, id); }
            previous = id;
        }
        long build = sw.ElapsedMilliseconds;

        ProjectStore.Save(manifest, new StoryProject { Title = "perf" });

        sw.Restart();
        ChapterWorkbookReader.Read(path);
        long read = sw.ElapsedMilliseconds;

        long attach = 0, redraw = 0;
        HeadlessUi.Run(() =>
        {
            var session = new AuthoringSession();
            session.Open(manifest);

            var view = new ChapterGraphView();
            var window = new Window { Width = 1200, Height = 800, Content = view };
            window.Show();

            var t = Stopwatch.StartNew();
            view.Attach(session);
            window.Measure(new Size(1200, 800));
            window.Arrange(new Rect(0, 0, 1200, 800));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            attach = t.ElapsedMilliseconds;

            var canvas = view.FindControl<Canvas>("GraphCanvas")!;
            output.WriteLine($"  캔버스 자식 수 = {canvas.Children.Count}");

            t.Restart();
            for (int i = 0; i < 10; i++)
            {
                view.RefreshFromDisk();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }
            redraw = t.ElapsedMilliseconds;

            window.Close();
        });

        output.WriteLine($"N={count,4}  워크북생성={build,6}ms  읽기={read,5}ms  첫그리기={attach,5}ms  Reload×10={redraw,6}ms ({redraw / 10.0:F1}ms/회)");
        Directory.Delete(dir, recursive: true);
    }
}
