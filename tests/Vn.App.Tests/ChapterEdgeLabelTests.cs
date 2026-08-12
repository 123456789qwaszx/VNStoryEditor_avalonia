using Avalonia.Controls;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// "간선의 이름이 수정이 안 돼" — 소유자 보고의 재현. 견본이 아니라 <b>사람이 툴에서 만든
/// 새 챕터</b>에서, 에피소드를 잇고 그 간선에 이름을 붙이는 실제 순서 그대로 간다.
/// </summary>
public sealed class ChapterEdgeLabelTests
{
    [Fact]
    public void 새_챕터에서_만든_간선에_이름을_붙일_수_있다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject();
        (ChapterGraphView view, _) = Show(project);

        // 사람이 하는 그대로: 노드 하나 → 그 자식 하나 (간선은 라벨 없이 생긴다).
        view.AddEpisodeFromToolbar();
        view.RefreshFromDisk();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        view.SelectEpisode("new01");
        view.AddEpisodeFromToolbar();
        view.RefreshFromDisk();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Contains(ChapterWorkbookReader.Read(project.ChapterPath).Edges,
            edge => edge.FromEpisodeId == "new01" && edge.ToEpisodeId == "new02");

        // 그 간선을 고르고 이름을 붙인다.
        view.SelectEdgeKey("new01", "new02");
        view.FindControl<TextBox>("EdgeLabelEditBox")!.Text = "왼쪽 길로 간다";
        view.ApplyEdgeFromPanel();

        ChapterEdge saved = ChapterWorkbookReader.Read(project.ChapterPath).Edges.Single(edge =>
            edge.FromEpisodeId == "new01" && edge.ToEpisodeId == "new02");

        Assert.Equal("왼쪽 길로 간다", saved.OptionLabel);
    });

    [Fact]
    public void 간선_패널의_에피소드를_누르면_그_에피소드_편집으로_간다() => HeadlessUi.Run(() =>
    {
        // 소유자 보고 — 간선을 클릭한 뒤 거기 보이는 에피소드 이름을 직접 고치려 했다.
        // 그 자리는 읽기 전용이었다. 이제 누르면 그 에피소드의 속성 패널(Id [개명])로 간다.
        using var project = new TempProject();
        (ChapterGraphView view, _) = Show(project);

        view.AddEpisodeFromToolbar();
        view.RefreshFromDisk();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        view.SelectEpisode("new01");
        view.AddEpisodeFromToolbar();
        view.RefreshFromDisk();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        view.SelectEdgeKey("new01", "new02");
        Assert.True(view.FindControl<StackPanel>("EdgePanel")!.IsVisible);

        // 도착 쪽 고리를 누른다 (출발 → 화살표 → 도착 순).
        var links = view.FindControl<StackPanel>("EdgeFromToPanel")!;
        var target = (TextBlock)links.Children[2];
        Assert.Contains("new02", target.Text);

        target.RaiseEvent(new Avalonia.Input.PointerPressedEventArgs(
            target, new Avalonia.Input.Pointer(0, Avalonia.Input.PointerType.Mouse, true),
            target, default, 0,
            new Avalonia.Input.PointerPointProperties(
                Avalonia.Input.RawInputModifiers.LeftMouseButton,
                Avalonia.Input.PointerUpdateKind.LeftButtonPressed),
            Avalonia.Input.KeyModifiers.None));

        // 이제 에피소드 편집 자리다 — 여기서 [개명]으로 이름을 바꾼다.
        Assert.True(view.FindControl<StackPanel>("PropertyPanel")!.IsVisible);
        Assert.Equal("new02", view.FindControl<TextBox>("IdBox")!.Text);

        view.FindControl<TextBox>("IdBox")!.Text = "ep_left";
        view.RenameSelectedEpisode();

        ChapterGraphModel reread = ChapterWorkbookReader.Read(project.ChapterPath);
        Assert.NotNull(reread.FindEpisode("ep_left"));
        Assert.Contains(reread.Edges, edge =>
            edge.FromEpisodeId == "new01" && edge.ToEpisodeId == "ep_left");
    });

    [Fact]
    public void 다시_읽기가_끼어들어도_적던_이름이_사라지지_않는다() => HeadlessUi.Run(() =>
    {
        // 저장 감시는 언제든 울린다(엑셀이 파일을 건드리기만 해도). 그 사이에 적어 둔 이름이
        // 조용히 모델 값으로 되돌아가면, 사람 눈에는 "적용을 눌러도 안 바뀐다"로 보인다.
        using var project = new TempProject();
        (ChapterGraphView view, _) = Show(project);

        view.AddEpisodeFromToolbar();
        view.RefreshFromDisk();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        view.SelectEpisode("new01");
        view.AddEpisodeFromToolbar();
        view.RefreshFromDisk();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        view.SelectEdgeKey("new01", "new02");
        view.FindControl<TextBox>("EdgeLabelEditBox")!.Text = "오른쪽 길로 간다";

        // 글상자에 포커스가 없는 상태(콤보·체크박스를 만졌거나 마우스만 올려 둔 상태)에서
        // 감시가 울린다.
        view.RefreshFromDisk();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        view.ApplyEdgeFromPanel();

        ChapterEdge saved = ChapterWorkbookReader.Read(project.ChapterPath).Edges.Single(edge =>
            edge.FromEpisodeId == "new01" && edge.ToEpisodeId == "new02");

        Assert.Equal("오른쪽 길로 간다", saved.OptionLabel);
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

    /// <summary>툴이 [＋ 챕터]로 만드는 것과 같은 빈 챕터 — 견본이 아니다.</summary>
    private sealed class TempProject : IDisposable
    {
        private readonly string _directory;

        public TempProject()
        {
            _directory = Path.Combine(
                Path.GetTempPath(), "vn-edge-label", Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(_directory);

            ChapterWorkbookWriter.EnsureChapterWorkbook(
                Path.Combine(_directory, ChapterLibrary.FolderName), "ch01",
                [("trust", "신뢰"), ("anger", "분노")]);

            ManifestPath = Path.Combine(_directory, "project" + ProjectManifestJson.FileExtension);
            ProjectStore.Save(ManifestPath, new StoryProject { Title = "간선 이름 검증" });
        }

        public string ManifestPath { get; }

        public string ChapterPath =>
            Path.Combine(_directory, ChapterLibrary.FolderName, "ch01.xlsx");

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
