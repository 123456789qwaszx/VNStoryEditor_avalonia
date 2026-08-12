using Avalonia.Controls;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// G5의 화면 배선 — 노드 클릭이 워크북을 만들어 열고, 에피소드 저장이 대사노드 반영과
/// 보고 패널·배지로 이어진다. 실제 엑셀은 띄우지 않는다(여는 손을 갈아끼운다).
/// </summary>
public sealed class ChapterGraphSyncViewTests
{
    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    [Fact]
    public void 노드를_열면_워크북이_규격대로_생기고_여는_손이_호출된다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        var opened = new List<string>();
        view.OpenWorkbookFile = opened.Add;
        view.WorkbookHandlerProbe = () => @"C:\Program Files\Microsoft Office\EXCEL.EXE";

        view.OpenEpisode("main05.03");

        string expected = Path.Combine(project.EpisodesFolder, "main05.03.xlsx");
        Assert.Equal(expected, Assert.Single(opened));
        Assert.True(File.Exists(expected));

        // 두 번째 열기는 만들지 않고 그대로 연다 — 기존 파일은 절대 덮어쓰지 않는다.
        byte[] before = File.ReadAllBytes(expected);
        view.OpenEpisode("main05.03");
        Assert.Equal(2, opened.Count);
        Assert.Equal(before, File.ReadAllBytes(expected));
    });

    [Fact]
    public void 에피소드_동기화가_대사노드와_보고_패널로_이어진다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);

        // 견본 워크북을 에피소드로도 배치한다 — 리더가 머리글로 시트를 찾으므로 그대로 통한다.
        Directory.CreateDirectory(project.EpisodesFolder);
        File.Copy(SamplePath, Path.Combine(project.EpisodesFolder, "main05.02.xlsx"));

        (ChapterGraphView view, AuthoringSession session) = Show(project);

        view.SyncEpisodes();

        // 대사노드가 챕터의 대사엔트리 이름으로 생겼다.
        Assert.Contains(session.Project.EnumerateNodes().OfType<DialogueNode>(),
            node => node.Name == "Story_ch05_02");

        // 배지(머리글)와 패널에 동기화 결과가 보인다.
        var expander = view.FindControl<Expander>("DiagnosticsExpander")!;
        Assert.Contains("동기화 1건 반영", (string)expander.Header!);

        var panel = view.FindControl<StackPanel>("DiagnosticsPanel")!;
        Assert.Contains(panel.Children.OfType<TextBlock>(),
            block => block.Text?.Contains("에피소드 main05.02 — 반영됨") == true);
    });

    [Fact]
    public void 깨진_에피소드는_거부가_배지와_패널에_보인다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        Directory.CreateDirectory(project.EpisodesFolder);

        // IN이 가리키는 구간이 없는 깨진 에피소드 (§3.3 규칙 1 위반).
        EpisodeLibrary.EnsureWorkbook(project.EpisodesFolder, "main05.01");
        BreakWorkbook(Path.Combine(project.EpisodesFolder, "main05.01.xlsx"));

        (ChapterGraphView view, AuthoringSession session) = Show(project);

        view.SyncEpisodes();

        var expander = view.FindControl<Expander>("DiagnosticsExpander")!;
        Assert.Contains("동기화 거부·경고", (string)expander.Header!);
        Assert.True(expander.IsExpanded);

        var panel = view.FindControl<StackPanel>("DiagnosticsPanel")!;
        Assert.Contains(panel.Children.OfType<TextBlock>(),
            block => block.Text?.Contains("반영 거부") == true);

        // 깨진 표는 노드를 만들지 않는다.
        Assert.DoesNotContain(session.Project.EnumerateNodes().OfType<DialogueNode>(),
            node => node.Name == "Story_ch05_01");
    });

    // ── G8 내보내기 버튼 ────────────────────────────────────────────────────

    [Fact]
    public void 검증_오류가_있으면_내보내기가_거부되고_보고가_펼쳐진다() => HeadlessUi.Run(() =>
    {
        // 견본 챕터는 에피소드 워크북 없이는 branch05.02A가 도달 불가다 → 거부.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, AuthoringSession session) = Show(project);

        string? path = view.Export();

        Assert.Null(path);
        Assert.Contains("거부", session.StatusMessage);
        Assert.True(view.FindControl<Expander>("DiagnosticsExpander")!.IsExpanded);
        Assert.False(Directory.Exists(project.ExportFolder));
    });

    [Fact]
    public void 검증을_통과하면_규약_폴더에_JSON이_나간다() => HeadlessUi.Run(() =>
    {
        // 도달 불가를 `도달불가 허용`(D3)으로 명시 예외 처리하면 검증을 통과한다.
        using var project = new TempProject(SamplePath);
        AllowUnreachable(project.ChapterPath, "branch05.02A");

        (ChapterGraphView view, AuthoringSession session) = Show(project);

        string? path = view.Export();

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.Equal(
            Path.Combine(project.ExportFolder, "ch05.progression.json"), path);
        Assert.Contains("내보냈습니다", session.StatusMessage);

        string json = File.ReadAllText(path!);
        Assert.Contains("\"StartEpisodeId\": \"main05.01\"", json);
        Assert.DoesNotContain("기본 루트", json);   // 픽스처는 섞이지 않는다
    });

    /// <summary>`도달불가 허용` 열(L)을 켠다 — D3의 명시 예외.</summary>
    private static void AllowUnreachable(string chapterPath, string episodeId)
    {
        using var memory = new MemoryStream(File.ReadAllBytes(chapterPath));
        using var workbook = new ClosedXML.Excel.XLWorkbook(memory);
        ClosedXML.Excel.IXLWorksheet sheet = workbook.Worksheet("에피소드");

        sheet.Cell(1, 12).SetValue("도달불가 허용");

        foreach (ClosedXML.Excel.IXLRow row in sheet.RowsUsed().Skip(1))
        {
            if (row.Cell(1).GetString() == episodeId)
            {
                row.Cell(12).SetValue("TRUE");
            }
        }

        workbook.SaveAs(chapterPath);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static (ChapterGraphView View, AuthoringSession Session) Show(TempProject project)
    {
        var session = new AuthoringSession();
        session.Open(project.ManifestPath);

        var view = new ChapterGraphView();
        var window = new Window { Width = 1280, Height = 800, Content = view };
        window.Show();
        view.Attach(session);

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (view, session);
    }

    private static void BreakWorkbook(string path)
    {
        using var memory = new MemoryStream(File.ReadAllBytes(path));
        using var workbook = new ClosedXML.Excel.XLWorkbook(memory);
        ClosedXML.Excel.IXLWorksheet sheet = workbook.Worksheets.First();
        sheet.Cell(2, 1).SetValue(10);
        sheet.Cell(2, 3).SetValue("IF");
        sheet.Cell(2, 5).SetValue("신뢰높음");
        sheet.Cell(2, 6).SetValue(900);   // 없는 구간을 가리킨다
        workbook.SaveAs(path);
    }

    private sealed class TempProject : IDisposable
    {
        private readonly string _directory;

        public TempProject(string samplePath)
        {
            _directory = Path.Combine(
                Path.GetTempPath(), "vn-chapter-sync-view", Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path.Combine(_directory, ChapterLibrary.FolderName));
            File.Copy(samplePath, Path.Combine(_directory, ChapterLibrary.FolderName, "ch05.xlsx"));

            ManifestPath = Path.Combine(_directory, "project" + ProjectManifestJson.FileExtension);

            // 실제 프로젝트의 모양 — 시나리오 파일이 하나는 있다. 동기화가 만든 노드가 여기 담긴다.
            var project = new StoryProject { Title = "동기화 검증" };
            project.Files.Add(new StoryFile("sf_main", "본편", "story/main.vnstory.json"));
            ProjectStore.Save(ManifestPath, project);
        }

        public string ManifestPath { get; }

        public string EpisodesFolder => Path.Combine(_directory, EpisodeLibrary.FolderName);

        public string ExportFolder => Path.Combine(_directory, ChapterGraphView.ExportFolderName);

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
