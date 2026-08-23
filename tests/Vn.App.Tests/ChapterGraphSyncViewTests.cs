using Avalonia.Controls;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.Authoring.Editing;
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
    public void 에피소드_동기화가_대사노드로_이어진다() => HeadlessUi.Run(() =>
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
    });

    [Fact]
    public void 잘된_동기화는_검증_보고에_아무_말도_안_한다() => HeadlessUi.Run(() =>
    {
        // 2026-08-24 소유자 — "동기화가 몇개 반영됬는지 표기할 필요는 없어. 그런 동기화
        // 문구들은 굳이 표시가 안 되도록." 여기는 <b>검증</b> 보고이고, 잘된 일의 개수는
        // 검증할 것이 아니다 — 그것이 목록을 채우면 정작 봐야 할 줄이 그 사이에 묻힌다.
        using var project = new TempProject(SamplePath);
        Directory.CreateDirectory(project.EpisodesFolder);
        File.Copy(SamplePath, Path.Combine(project.EpisodesFolder, "main05.02.xlsx"));

        (ChapterGraphView view, _) = Show(project);

        view.SyncEpisodes();

        var expander = view.FindControl<Expander>("DiagnosticsExpander")!;
        Assert.DoesNotContain("반영", (string)expander.Header!);

        List<string> texts = view.FindControl<StackPanel>("DiagnosticsPanel")!
            .Children.OfType<TextBlock>()
            .Select(block => block.Text ?? string.Empty)
            .ToList();

        // ⚠ 동기화 보고의 이름표는 <b>정확히</b> "에피소드 {Id}"다. 부분일치로 재면
        // 진단 줄이 파일 경로에 그 이름을 달고 있어 헛물을 켠다 — 처음 쓴 판이 그래서
        // 필터를 꺼도 통과했다.
        Assert.DoesNotContain(texts, text => text.Trim() == "에피소드 main05.02");
        Assert.DoesNotContain(texts, text => text.Contains("반영됨"));
        Assert.DoesNotContain(texts, text => text.Contains("LineId를 발급"));

        // ⛔ 거부는 이 침묵에 휩쓸리지 않는다 — 조용한 무반영이 최악이다(G3-1).
        // 그쪽은 `깨진_에피소드는_거부가_배지와_패널에_보인다`가 지킨다.
    });

    [Fact]
    public void 잘된_동기화는_상태줄에도_아무_말도_안_한다() => HeadlessUi.Run(() =>
    {
        // 검증 보고와 <b>같은 선</b>이다 (2026-08-24). 동기화는 사람이 시켜서 도는 일이
        // 아니라 챕터를 고르거나 워크북이 저장될 때마다 저절로 도는데, 잘됐다고 매번
        // 상태줄을 차지하면 사람이 방금 누른 것에 대한 답이 그 아래 깔린다.
        using var project = new TempProject(SamplePath);
        Directory.CreateDirectory(project.EpisodesFolder);
        File.Copy(SamplePath, Path.Combine(project.EpisodesFolder, "main05.02.xlsx"));

        (ChapterGraphView view, AuthoringSession session) = Show(project);

        session.SetStatus("사람이 방금 누른 것의 답");

        view.SyncEpisodes();

        Assert.Equal("사람이 방금 누른 것의 답", session.StatusMessage);
    });

    [Fact]
    public void 짚을_것이_있는_에피소드는_이름표와_함께_맨_위에_선다() => HeadlessUi.Run(() =>
    {
        // 잘된 것을 지웠다고 <b>짚을 것까지 지우면 안 된다.</b> 그리고 그 줄들은 여전히
        // 목록 맨 위다 — 사람이 방금 한 행동의 결과를 알림 더미 아래 스크롤 밖에 묻지
        // 않는다(실사례: "숫자는 2개라는데 볼 방법이 없어").
        using var project = new TempProject(SamplePath);
        Directory.CreateDirectory(project.EpisodesFolder);

        EpisodeLibrary.EnsureWorkbook(project.EpisodesFolder, "main05.01");
        BreakWorkbook(Path.Combine(project.EpisodesFolder, "main05.01.xlsx"));

        (ChapterGraphView view, _) = Show(project);

        view.SyncEpisodes();

        List<string> texts = view.FindControl<StackPanel>("DiagnosticsPanel")!
            .Children.OfType<TextBlock>()
            .Select(block => block.Text ?? string.Empty)
            .ToList();

        int syncIndex = texts.FindIndex(text => text.Contains("에피소드 main05.01"));
        Assert.True(syncIndex >= 0, "짚을 것이 있으면 어느 에피소드인지 이름표가 있어야 한다");

        int firstDiagnosticIndex = texts.FindIndex(text => text.Contains(".xlsx ·"));
        Assert.True(firstDiagnosticIndex < 0 || syncIndex < firstDiagnosticIndex,
            $"동기화 줄({syncIndex})이 진단({firstDiagnosticIndex})보다 아래에 있습니다.");
    });

    [Fact]
    public void 에피소드를_고르면_대사_탭에서_대사가_읽힌다() => HeadlessUi.Run(() =>
    {
        // 소유자 요청 — 시나리오 그래프까지 안 가도, 챕터 그래프의 [대사] 탭에서
        // 고른 에피소드의 대사가 (읽기 전용으로) 보여야 한다.
        using var project = new TempProject(SamplePath);
        Directory.CreateDirectory(project.EpisodesFolder);
        File.Copy(SamplePath, Path.Combine(project.EpisodesFolder, "main05.02.xlsx"));

        (ChapterGraphView view, _) = Show(project);

        view.SelectEpisode("main05.02");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var preview = view.FindControl<SelectableTextBlock>("DialoguePreviewText")!;

        // v10 — 조건 블록은 들여쓰기로 보인다(ENDIF 줄은 안 세운다). 견본은 중첩과
        // ELSEIF를 한 판에 담으므로 세 겹이 전부 이 한 줄 검사에 걸린다.
        Assert.Equal(
            """
            윌로: 복도는 조용했다.
            라루: 여기서 기다릴까?
            IF 신뢰높음
              윌로: 너를 믿어.
              IF 지쳐있음
                라루: 다리가 무거워.
            ELSEIF 분노누적
              라루: 아직도 화가 나.
            윌로: 문이 열렸다.
            """.ReplaceLineEndings("\n"),
            preview.Text!.ReplaceLineEndings("\n"));

        // 아직 대본 없는 에피소드는 빈 화면 대신 그 사실을 말한다.
        view.SelectEpisode("main05.01");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(string.Empty, preview.Text);
        Assert.Contains("아직 적힌 대사가 없습니다",
            view.FindControl<TextBlock>("DialoguePreviewHeader")!.Text);
    });

    [Fact]
    public void 챕터_탭이_스탯을_읽기_전용으로_세운다() => HeadlessUi.Run(() =>
    {
        // 소유자 점검 — 스탯은 어디에서도 값이 안 보이던 표다. [챕터] 탭이 조건 아래에
        // 읽기 전용으로 세운다. 에피소드·간선은 그래프가 이미 그린다.
        //
        // 2026-08-24 소유자 — 나란히 서 있던 픽스처 표는 걷었다("한 번도 안 썼어").
        // 엑셀의 `픽스처` 시트는 그대로다: 화면만 없앴다.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        List<string> stats = view.FindControl<StackPanel>("StatListPanel")!
            .Children.OfType<TextBlock>().Select(block => block.Text ?? string.Empty).ToList();

        Assert.Contains(stats, text => text.Contains("trust") && text.Contains("초기"));
        Assert.Null(view.FindControl<StackPanel>("FixtureListPanel"));
    });

    [Fact]
    public void 툴이_꺼진_사이_적힌_대사도_열면_바로_노드로_선다() => HeadlessUi.Run(() =>
    {
        // 소유자 보고 — 시트에서 대사를 쓰고 프로젝트를 다시 열었더니 시나리오 그래프에
        // 노드가 없었다. 감시는 "저장 순간"만 잡으므로, 켤 때 한 번 따라잡아야 한다.
        using var project = new TempProject(SamplePath);
        Directory.CreateDirectory(project.EpisodesFolder);
        File.Copy(SamplePath, Path.Combine(project.EpisodesFolder, "main05.02.xlsx"));

        // Show가 곧 "툴을 켠다"다 — SyncEpisodes를 직접 부르지 않는다.
        (_, AuthoringSession session) = Show(project);

        Assert.Contains(session.Project.EnumerateNodes().OfType<DialogueNode>(),
            node => node.Name == "Story_ch05_02");
    });

    [Fact]
    public void 동기화가_반영되면_구조_변경으로_알려_열린_편집_화면이_다시_선다() => HeadlessUi.Run(() =>
    {
        // 실사례 — 시트에서 대사를 고치면 모델은 바뀌는데 열려 있는 줄 목록은 옛 줄을
        // 들고 있었다. 대사 수정이 "타이핑 보호"(편집 컨트롤 보존) 경로로 전달됐기 때문이다.
        // 바깥(엑셀)에서 온 변경은 구조 변경으로 격상해 화면을 다시 만들게 한다.
        using var project = new TempProject(SamplePath);
        Directory.CreateDirectory(project.EpisodesFolder);
        File.Copy(SamplePath, Path.Combine(project.EpisodesFolder, "main05.02.xlsx"));

        (ChapterGraphView view, AuthoringSession session) = Show(project);

        // ⚠ <b>실제로 무언가를 바꿔야 한다</b> (2026-08-24). 예전에는 그냥 SyncEpisodes를
        // 한 번 더 부르면 됐다 — 아무것도 안 바뀌어도 방송이 나갔기 때문이다. 그 방송이
        // 타이핑하던 칸을 파괴해서 지금은 <b>바뀐 때만</b> 나간다
        // (`ChapterGraphWorkAmountTests.아무것도_안_바뀐_동기화는_다시_그리라고_방송하지_않는다`).
        // 그래서 여기서도 작가가 시트에서 고친 것과 같은 일을 한다 — 이 테스트가 원래
        // 지키려던 것이 바로 그 경우다("시트에서 대사를 고치면").
        WriteFirstLine(
            Path.Combine(project.EpisodesFolder, "main05.02.xlsx"),
            "라루", "시트에서 고친 대사");

        var kinds = new List<ProjectChangeKind>();
        session.Changed += (_, args) => kinds.Add(args.Kind);

        view.SyncEpisodes();

        Assert.Contains(ProjectChangeKind.Structure, kinds);
    });

    /// <summary>대본 워크북의 첫 데이터 행에 한 줄 적는다 — 작가가 엑셀에서 하는 일.</summary>
    private static void WriteFirstLine(string path, string speaker, string text)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook(path);
        ClosedXML.Excel.IXLWorksheet sheet = workbook.Worksheets
            .First(candidate => candidate.Cell(1, 1).GetString().Trim() == "인덱스");

        sheet.Cell(2, 5).SetValue(speaker);   // E · 화자
        sheet.Cell(2, 6).SetValue(text);      // F · 내용

        workbook.SaveAs(path);
    }

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

        // ⚠ 2026-08-24 — <b>저절로 펼치지 않는다</b>. 알림은 머리글의 표식이 든다
        // (소유자: "그것까지 꺼줘. 대신에 … 시각적인 이모티콘을 붙여놓기만 해").
        var expander = view.FindControl<Expander>("DiagnosticsExpander")!;
        Assert.Contains("동기화 거부·경고", (string)expander.Header!);
        Assert.Contains("🟡", (string)expander.Header!);   // 경고 = 노랑
        Assert.False(expander.IsExpanded);

        var panel = view.FindControl<StackPanel>("DiagnosticsPanel")!;
        Assert.Contains(panel.Children.OfType<TextBlock>(),
            block => block.Text?.Contains("반영 거부") == true);

        // 깨진 표는 노드를 만들지 않는다.
        Assert.DoesNotContain(session.Project.EnumerateNodes().OfType<DialogueNode>(),
            node => node.Name == "Story_ch05_01");
    });

    // ── G8 내보내기 — 사람 손을 기다리지 않는다 (2026-08-17) ────────────────

    [Fact]
    public void 검증_오류가_있으면_JSON이_안_나가고_사유를_말한다() => HeadlessUi.Run(() =>
    {
        // 견본 챕터는 에피소드 워크북 없이는 branch05.02A가 도달 불가다 → 거부.
        // 쓰레기가 런타임으로 넘어가는 것보다 파일이 안 나가는 편이 낫다(G8).
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        Assert.False(Directory.Exists(project.ExportFolder));

        // 사유는 상태줄이 아니라 검증 보고에 선다 — 상태줄은 동기화 보고가 곧 덮는다.
        // ⚠ 접힌 채로도 <b>머리글이 말한다</b> — 런타임으로 나갈 것이 안 나간 상태다.
        var expander = view.FindControl<Expander>("DiagnosticsExpander")!;
        Assert.Contains("진행 JSON 미출력", (string)expander.Header!);
        Assert.Contains("🔴", (string)expander.Header!);   // 안 나간 것은 오류 급 = 빨강
        Assert.False(expander.IsExpanded);

        Assert.Contains(
            view.FindControl<StackPanel>("DiagnosticsPanel")!.Children.OfType<TextBlock>(),
            line => line.Text!.Contains("진행 JSON이 나가지 않았습니다", StringComparison.Ordinal)
                && line.Text.Contains("ch05", StringComparison.Ordinal));
    });

    [Fact]
    public void 검증을_통과하면_규약_폴더에_JSON이_저절로_나간다() => HeadlessUi.Run(() =>
    {
        // 도달 불가를 `도달불가 허용`(D3)으로 명시 예외 처리하면 검증을 통과한다.
        // 단추를 누르지 않는다 — 챕터를 읽는 그 순간 나간다.
        using var project = new TempProject(SamplePath);
        AllowUnreachable(project.ChapterPath, "branch05.02A");

        (ChapterGraphView view, _) = Show(project);

        string path = Path.Combine(project.ExportFolder, "ch05.progression.json");
        Assert.True(File.Exists(path));

        // 잘 나갔을 때는 아무 말도 없다 — 파일이 있다는 것이 곧 증거다.
        Assert.DoesNotContain(
            "진행 JSON", (string)view.FindControl<Expander>("DiagnosticsExpander")!.Header!);

        string json = File.ReadAllText(path);
        Assert.Contains("\"StartEpisodeId\": \"main05.01\"", json);
        Assert.DoesNotContain("기본 루트", json);   // 픽스처는 섞이지 않는다
    });

    [Fact]
    public void 엑셀을_고치면_JSON도_따라_갱신된다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        AllowUnreachable(project.ChapterPath, "branch05.02A");

        (ChapterGraphView view, _) = Show(project);

        string path = Path.Combine(project.ExportFolder, "ch05.progression.json");
        Assert.DoesNotContain("새로판길", File.ReadAllText(path));

        ChapterWorkbookWriter.AddEpisode(project.ChapterPath, "새로판길", title: "", 3, 1);

        // ⚠ 문구를 준다 (2026-08-23). 문구 없는 간선은 "보이지 않는 기본"이고 에피소드당
        // 하나뿐인데, main05.01에는 이미 하나가 있다. 둘이 되면 **진행 코어가 그 챕터를
        // 못 싣고**, 내보내기가 그것을 옳게 거부한다 — 예전에는 그대로 나가서 게임에서만
        // 안 되던 자리다. 이 테스트가 재려는 것은 "엑셀을 고치면 JSON이 따라오는가"이지
        // 잘못된 데이터가 나가는가가 아니다.
        ChapterWorkbookWriter.AddEdge(
            project.ChapterPath, "main05.01", "새로판길", optionLabel: "새로 판 길로");

        view.RefreshFromDisk();

        Assert.Contains("새로판길", File.ReadAllText(path));
    });

    [Fact]
    public void 고른_챕터만이_아니라_모든_챕터가_나간다() => HeadlessUi.Run(() =>
    {
        // 고른 챕터만 내보내면 나머지는 누른 순간의 낡은 판으로 굳는다 — 사람 손을
        // 없앤 이유가 그것이다.
        using var project = new TempProject(SamplePath);
        AllowUnreachable(project.ChapterPath, "branch05.02A");

        string second = Path.Combine(
            Path.GetDirectoryName(project.ChapterPath)!, "ch99.xlsx");
        File.Copy(project.ChapterPath, second);

        Show(project);

        Assert.True(File.Exists(Path.Combine(project.ExportFolder, "ch05.progression.json")));
        Assert.True(File.Exists(Path.Combine(project.ExportFolder, "ch99.progression.json")));
    });

    /// <summary>`도달불가 허용` 열(K — 2026-08-16 인덱스 폐지 후)을 켠다 — D3의 명시 예외.</summary>
    private static void AllowUnreachable(string chapterPath, string episodeId)
    {
        using var memory = new MemoryStream(File.ReadAllBytes(chapterPath));
        using var workbook = new ClosedXML.Excel.XLWorkbook(memory);
        ClosedXML.Excel.IXLWorksheet sheet = workbook.Worksheet("에피소드");

        sheet.Cell(1, 11).SetValue("도달불가 허용");

        foreach (ClosedXML.Excel.IXLRow row in sheet.RowsUsed().Skip(1))
        {
            if (row.Cell(1).GetString() == episodeId)
            {
                row.Cell(11).SetValue("TRUE");
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
        sheet.Cell(2, 2).SetValue("IF");
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

        /// <summary>그 챕터의 대본 폴더 — episodes/{ChapterId}/ (2026-08-16 챕터별 격리).</summary>
        public string EpisodesFolder =>
            Path.Combine(_directory, EpisodeLibrary.FolderName, "ch05");

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
