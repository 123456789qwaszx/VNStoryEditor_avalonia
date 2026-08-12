using Avalonia.Controls;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// G-2 v2 — 그래프 편집이 엑셀 셀로 왕복한다. 패널의 [적용]·[개명]·간선·조건·＋에피소드와
/// 드래그 커밋이 전부 워크북에 써지고, 다시 읽으면 그대로 나온다.
/// </summary>
public sealed class ChapterGraphEditingTests
{
    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    [Fact]
    public void 선택하면_패널이_현재_값으로_차고_적용이_엑셀로_간다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        view.SelectEpisode("main05.02");

        Assert.True(view.FindControl<StackPanel>("PropertyPanel")!.IsVisible);
        Assert.Equal("조용한 복도", view.FindControl<TextBox>("TitleBox")!.Text);
        Assert.Equal("Story_ch05_02", view.FindControl<TextBox>("EntryBox")!.Text);

        // 제목을 고치고 적용 → 엑셀 셀이 바뀐다. 나머지는 그대로다.
        view.FindControl<TextBox>("TitleBox")!.Text = "고친 복도";
        view.ApplySelectedProperties();

        ChapterGraphModel reread = ChapterWorkbookReader.Read(project.ChapterPath);
        Assert.Equal("고친 복도", reread.FindEpisode("main05.02")!.Title);
        Assert.Equal("Story_ch05_02", reread.FindEpisode("main05.02")!.DialogueEntry);
    });

    [Fact]
    public void 드래그_커밋이_평행이동을_역산해_엑셀_좌표로_쓴다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        // 견본의 최소 좌표는 X=0·Y=-120이므로 배치 여백이 60이면 캔버스 (60,180) = 엑셀 (0,0)이다.
        view.CommitNodePosition("main05.01", 160, 240);

        ChapterEpisode moved = ChapterWorkbookReader.Read(project.ChapterPath).FindEpisode("main05.01")!;
        Assert.Equal(100d, moved.X);
        Assert.Equal(60d, moved.Y);
    });

    [Fact]
    public void 패널에서_간선을_더하고_조건을_더한다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        view.SelectEpisode("main05.01");
        view.FindControl<ComboBox>("EdgeTargetCombo")!.SelectedItem = "main05.end";
        view.FindControl<TextBox>("EdgeLabelBox")!.Text = "지름길";
        view.AddEdgeFromPanel();

        view.FindControl<TextBox>("ConditionLabelBox")!.Text = "새조건";
        view.FindControl<TextBox>("ConditionExprBox")!.Text = "anger <= 1";
        view.SaveConditionFromPanel();

        ChapterGraphModel reread = ChapterWorkbookReader.Read(project.ChapterPath);

        ChapterEdge edge = reread.Edges.Single(candidate =>
            candidate.FromEpisodeId == "main05.01" && candidate.ToEpisodeId == "main05.end");
        Assert.Equal("지름길", edge.OptionLabel);

        Assert.Equal("anger <= 1", reread.FindCondition("새조건")!.Expression);
    });

    [Fact]
    public void 에피소드_추가와_개명이_왕복한다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        view.AddEpisodeFromToolbar();

        ChapterGraphModel afterAdd = ChapterWorkbookReader.Read(project.ChapterPath);
        Assert.NotNull(afterAdd.FindEpisode("new01"));

        // 감시 대신 직접 다시 읽게 한 뒤 개명한다 — 자리표시 Id를 사람이 정한 이름으로.
        view.SelectEpisode("new01");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        view.FindControl<TextBox>("IdBox")!.Text = "main05.05";
        view.RenameSelectedEpisode();

        ChapterGraphModel afterRename = ChapterWorkbookReader.Read(project.ChapterPath);
        Assert.Null(afterRename.FindEpisode("new01"));
        Assert.NotNull(afterRename.FindEpisode("main05.05"));
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

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

        public TempProject(string samplePath)
        {
            _directory = Path.Combine(
                Path.GetTempPath(), "vn-chapter-editing", Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path.Combine(_directory, ChapterLibrary.FolderName));
            File.Copy(samplePath, ChapterPath);

            ManifestPath = Path.Combine(_directory, "project" + ProjectManifestJson.FileExtension);
            var project = new StoryProject { Title = "편집 검증" };
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
