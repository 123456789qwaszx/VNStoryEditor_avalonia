using Avalonia.Controls;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// 사람이 툴에서 만든 <b>새 챕터</b>에서 간선과 선택지 칸이 실제 순서 그대로 도는지 —
/// 2026-08-16 개정: 문구의 정본은 챕터 `선택지` 시트의 대본 text이고(자유 수정),
/// 툴의 간선 편집은 선택지수만 만진다.
/// </summary>
public sealed class ChapterEdgeLabelTests
{
    [Fact]
    public void 선택지_칸에_대본_text를_적으면_보이는_선택지가_된다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject();
        (ChapterGraphView view, _) = Show(project);

        // 사람이 하는 그대로: 노드 하나 → 그 자식 하나 (간선 + 빈 선택지 칸이 함께 생긴다).
        view.AddEpisodeFromToolbar();
        view.RefreshFromDisk();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        view.SelectEpisode("new01");
        view.AddEpisodeFromToolbar();
        view.RefreshFromDisk();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ChapterGraphModel created = ChapterWorkbookReader.Read(project.ChapterPath);
        Assert.Contains(created.Edges,
            edge => edge.FromEpisodeId == "new01" && edge.ToEpisodeId == "new02");
        ChapterChoiceOption slot = Assert.Single(created.ChoiceOptionsFor("new01"));
        Assert.True(slot.IsInvisibleDefault); // 아직 빈 칸 — 보이지 않는 기본

        // 기획자가 엑셀에서 하듯 대본 text를 적는다 — 그 순간 보이는 선택지다.
        using (var workbook = new ClosedXML.Excel.XLWorkbook(project.ChapterPath))
        {
            ClosedXML.Excel.IXLWorksheet choices = workbook.Worksheet(ChapterSheetNames.Choices);
            choices.Cell(slot.SourceRow, 3).SetValue("왼쪽 길로 간다"); // 대본은 C열 (v7)
            workbook.Save();
        }

        ChapterEdge saved = ChapterWorkbookReader.Read(project.ChapterPath).Edges.Single(edge =>
            edge.FromEpisodeId == "new01" && edge.ToEpisodeId == "new02");

        Assert.Equal("왼쪽 길로 간다", saved.OptionLabel);
        Assert.False(saved.IsPlainAdvance);
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
    public void 다시_읽기가_끼어들어도_적던_잠금_안내문이_사라지지_않는다() => HeadlessUi.Run(() =>
    {
        // 저장 감시는 언제든 울린다(엑셀이 파일을 건드리기만 해도). 그 사이에 적어 둔 값이
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
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        view.FindControl<TextBox>("EdgeLockedMsgBox")!.Text = "아직 이르다";

        // 적어 둔 값에 포커스가 없는 상태에서 감시가 울린다 — 모델 값으로 되돌아가면 안 된다.
        view.RefreshFromDisk();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        view.ApplyEdgeFromPanel();

        ChapterEdge saved = ChapterWorkbookReader.Read(project.ChapterPath).Edges.Single(edge =>
            edge.FromEpisodeId == "new01" && edge.ToEpisodeId == "new02");

        Assert.Equal("아직 이르다", saved.LockedMessage);
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

        /// <summary>그 챕터의 대본 폴더 — episodes/{ChapterId}/ (2026-08-16 챕터별 격리).</summary>
        public string EpisodesFolder => Path.Combine(_directory, "episodes", "ch01");

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
