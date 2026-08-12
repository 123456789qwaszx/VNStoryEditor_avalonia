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

        // 제목을 고치고 적용 → 엑셀 셀이 바뀐다. 패널에서 뺀 필드(대사엔트리 등, v3)는 그대로다.
        view.FindControl<TextBox>("TitleBox")!.Text = "고친 복도";
        view.ApplySelectedProperties();

        ChapterGraphModel reread = ChapterWorkbookReader.Read(project.ChapterPath);
        Assert.Equal("고친 복도", reread.FindEpisode("main05.02")!.Title);
        Assert.Equal("Story_ch05_02", reread.FindEpisode("main05.02")!.DialogueEntry);
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

    [Fact]
    public void 선택한_노드의_자식으로_에피소드가_생기고_간선이_이어진다() => HeadlessUi.Run(() =>
    {
        // v3 — [＋ 에피소드]는 선택된 노드의 다음으로 만든다. 흐름 연결이 기본값이다.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        view.SelectEpisode("main05.end");
        view.AddEpisodeFromToolbar();

        ChapterGraphModel reread = ChapterWorkbookReader.Read(project.ChapterPath);
        ChapterEpisode added = reread.FindEpisode("new01")!;

        // 간선이 함께 이어졌고, 대사엔트리는 EpisodeId로 자동이다.
        Assert.Contains(reread.Edges, edge =>
            edge.FromEpisodeId == "main05.end" && edge.ToEpisodeId == "new01");
        Assert.Equal("new01", added.DialogueEntry);

        // 새 노드가 선택되어 바로 [개명]할 수 있다 (감시 대신 직접 다시 읽게 한다).
        view.RefreshFromDisk();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("new01", view.FindControl<TextBox>("IdBox")!.Text);
    });

    [Fact]
    public void 간선을_선택하면_패널이_차고_적용이_엑셀로_간다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        view.SelectEdgeKey("main05.02", "branch05.02A");

        Assert.True(view.FindControl<StackPanel>("EdgePanel")!.IsVisible);
        Assert.False(view.FindControl<StackPanel>("PropertyPanel")!.IsVisible);
        Assert.Equal("라루의 제안을 듣는다", view.FindControl<TextBox>("EdgeLabelEditBox")!.Text);
        Assert.Equal("신뢰높음", view.FindControl<ComboBox>("EdgeConditionCombo")!.SelectedItem);

        // 잠기면 숨김으로 바꾸고 안내문을 고쳐 적용 → 엑셀 셀로 간다.
        view.FindControl<CheckBox>("EdgeHideCheck")!.IsChecked = true;
        view.FindControl<TextBox>("EdgeLockedMsgBox")!.Text = "신뢰를 더 쌓아야 한다";
        view.ApplyEdgeFromPanel();

        ChapterEdge edge = ChapterWorkbookReader.Read(project.ChapterPath).Edges.Single(candidate =>
            candidate.FromEpisodeId == "main05.02" && candidate.ToEpisodeId == "branch05.02A");

        Assert.True(edge.HideWhenLocked);
        Assert.Equal("신뢰를 더 쌓아야 한다", edge.LockedMessage);
        Assert.Equal("신뢰높음", edge.ConditionLabel);  // 안 바꾼 필드는 그대로
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
