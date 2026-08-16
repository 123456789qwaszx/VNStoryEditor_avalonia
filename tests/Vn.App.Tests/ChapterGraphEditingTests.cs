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
    public void 조건_콤보를_고르면_바로_엑셀_셀에_저장된다() => HeadlessUi.Run(() =>
    {
        // 2026-08-16 소유자 — [적용] 단추 폐지. 표시·해금 콤보를 고르는 순간 그 셀이 써진다.
        // 제목·엔딩키·메모 칸은 뺐다(그 값들은 엑셀에서). 기본은 읽기 전용이라 체크를 먼저 푼다.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        view.FindControl<CheckBox>("ExcelOnlyCheck")!.IsChecked = false;

        view.SelectEpisode("branch05.02A");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(view.FindControl<StackPanel>("PropertyPanel")!.IsVisible);
        Assert.Equal("branch05.02A", view.FindControl<TextBox>("IdBox")!.Text);

        // 선택하면 현재 값이 채워진다 — 해금조건 콤보에 이 노드의 신뢰높음이 골라져 있다.
        Assert.Equal("신뢰높음", view.FindControl<ComboBox>("UnlockCombo")!.SelectedItem);

        // 콤보를 고르는 것만으로 저장된다 — 단추가 없다.
        view.FindControl<ComboBox>("VisibleCombo")!.SelectedItem = "지쳐있음";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ChapterGraphModel reread = ChapterWorkbookReader.Read(project.ChapterPath);
        ChapterEpisode episode = reread.FindEpisode("branch05.02A")!;

        Assert.Equal("지쳐있음", episode.VisibleConditionLabel);
        Assert.Equal("신뢰높음", episode.UnlockConditionLabel); // 안 바꾼 값은 그대로

        // 편집 칸이 없는 값은 여전히 읽기 전용으로 보인다.
        Assert.Contains("대사엔트리", view.FindControl<TextBlock>("EpisodeFactsText")!.Text!);
    });

    [Fact]
    public void 엑셀에서만_편집이_기본이라_툴_편집_창구가_닫혀_있다() => HeadlessUi.Run(() =>
    {
        // 2026-08-16 소유자 — 기본 동작: 모든 데이터는 엑셀에서 만지고 툴은 보여준다.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        Assert.True(view.FindControl<CheckBox>("ExcelOnlyCheck")!.IsChecked);

        view.SelectEpisode("main05.02");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(view.FindControl<TextBox>("IdBox")!.IsEnabled);
        Assert.False(view.FindControl<ComboBox>("VisibleCombo")!.IsEnabled);
        Assert.False(view.FindControl<Button>("AddNextEdgeButton")!.IsVisible);
        Assert.False(view.FindControl<Grid>("EdgeFormPanel")!.IsVisible);
        Assert.False(view.FindControl<Button>("DeleteEpisodeButton")!.IsVisible);

        // 체크를 풀면 열린다.
        view.FindControl<CheckBox>("ExcelOnlyCheck")!.IsChecked = false;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(view.FindControl<TextBox>("IdBox")!.IsEnabled);
        Assert.True(view.FindControl<Button>("AddNextEdgeButton")!.IsVisible);
    });

    [Fact]
    public void 패널에서_간선을_더한다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);
        view.FindControl<CheckBox>("ExcelOnlyCheck")!.IsChecked = false;

        view.SelectEpisode("main05.01");
        view.FindControl<ComboBox>("EdgeTargetCombo")!.SelectedItem = "main05.end";
        view.FindControl<ComboBox>("EdgeLabelBox")!.SelectedItem = "1";
        view.AddEdgeFromPanel();

        ChapterGraphModel reread = ChapterWorkbookReader.Read(project.ChapterPath);

        ChapterEdge edge = reread.Edges.Single(candidate =>
            candidate.FromEpisodeId == "main05.01" && candidate.ToEpisodeId == "main05.end");
        Assert.Equal(1, edge.ChoiceCount);
        // 칸이 함께 섰다 — 빈 대본(보이지 않는 기본). 문구는 선택지 시트에서 적는다.
        Assert.Contains(reread.ChoiceOptionsFor("main05.01"), slot =>
            slot.ToEpisodeId == "main05.end" && slot.IsInvisibleDefault);
    });

    [Fact]
    public void 간선_줄을_클릭하면_폼에_실려_선택지수를_고친다() => HeadlessUi.Run(() =>
    {
        // 2026-08-16 소유자 — 줄 클릭이 간선 선택으로 건너뛰지 않는다. 그 줄 바로 아래에
        // 폼이 열려 도착·선택지수가 실리고, 수를 올리면 선택지 시트에 칸이 함께 선다.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);
        view.FindControl<CheckBox>("ExcelOnlyCheck")!.IsChecked = false;

        view.SelectEpisode("main05.02");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ChapterGraphModel model = ChapterWorkbookReader.Read(project.ChapterPath);
        ChapterEdge edge = model.Edges.Single(candidate =>
            candidate.FromEpisodeId == "main05.02" && candidate.ToEpisodeId == "branch05.02A");

        view.LoadEdgeIntoForm(edge, 0);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 폼이 열려 클릭한 간선의 값이 실려 있고, 도착은 신원이라 잠긴다.
        Assert.True(view.FindControl<Grid>("EdgeFormPanel")!.IsVisible);
        Assert.Equal("branch05.02A", view.FindControl<ComboBox>("EdgeTargetCombo")!.SelectedItem);
        Assert.Equal("1", view.FindControl<ComboBox>("EdgeLabelBox")!.SelectedItem);
        Assert.False(view.FindControl<ComboBox>("EdgeTargetCombo")!.IsEnabled);
        Assert.Equal("수정", view.FindControl<Button>("AddEdgeButton")!.Content);

        // 선택지수를 올리면 칸이 함께 선다.
        view.FindControl<ComboBox>("EdgeLabelBox")!.SelectedItem = "2";
        view.SubmitEdgeForm();

        ChapterGraphModel reread = ChapterWorkbookReader.Read(project.ChapterPath);
        ChapterEdge updated = reread.Edges.Single(candidate =>
            candidate.FromEpisodeId == "main05.02" && candidate.ToEpisodeId == "branch05.02A");
        Assert.Equal(2, updated.ChoiceCount);
        Assert.Equal(2, reread.ChoiceOptions.Count(slot =>
            slot.EpisodeId == "main05.02" && slot.ToEpisodeId == "branch05.02A"));
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
    public void 개명하면_대본_파일과_대사_노드가_함께_따라간다() => HeadlessUi.Run(() =>
    {
        // 실사례 — 개명이 챕터 시트만 따라가서, 옛 이름의 .xlsm이 원고를 든 채 남고
        // 새 이름으로 빈 워크북이 하나 더 생겼다.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, AuthoringSession session) = Show(project);

        view.AddEpisodeFromToolbar(); // new01 + 대본 워크북 생성
        view.RefreshFromDisk();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 대사 노드가 이미 서 있는 상황을 흉내낸다 (규약: 노드 이름 = 에피소드 Id).
        string fileId = session.EnsureChapterBoard("ch05");
        DialogueNode standing = session.Editor.AddDialogueNode(fileId, name: "new01");
        standing.ExcelEpisodeId = "new01";

        view.SelectEpisode("new01");
        view.FindControl<TextBox>("IdBox")!.Text = "ep_renamed";
        view.RenameSelectedEpisode();

        string episodes = Path.Combine(Path.GetDirectoryName(project.ChapterPath)!, "..", "episodes");

        // 대본 파일이 새 이름으로 옮겨졌고, 옛 이름은 남지 않았다.
        Assert.NotNull(EpisodeLibrary.FindExisting(episodes, "ep_renamed"));
        Assert.Null(EpisodeLibrary.FindExisting(episodes, "new01"));

        // 대사 노드도 새 이름이다 — 새로 만들지 않고 이름만 바꿔 연출·신원이 보존된다.
        // 엑셀 표식(ExcelEpisodeId)도 함께 간다 — 옛 Id로 남으면 시나리오 그래프가
        // 챕터 밖 노드로 보고 레일을 끊는다.
        Assert.Contains(session.Project.EnumerateNodes().OfType<DialogueNode>(),
            node => node.Name == "ep_renamed" && node.ExcelEpisodeId == "ep_renamed");
        Assert.DoesNotContain(session.Project.EnumerateNodes().OfType<DialogueNode>(),
            node => node.Name == "new01");
    });

    [Fact]
    public void 잠긴_대본_파일이면_개명_전체가_멈춘다() => HeadlessUi.Run(() =>
    {
        // 실사례 (2026-08-15) — 엑셀이 원고를 열어 둔 채 개명하면 챕터·노드만 새 이름이 되고
        // 원고는 옛 이름에 남았다. 새 이름을 여는 순간 빈 워크북이 생겨 "파일이 새로
        // 만들어진다"로 보였다. 파일을 못 옮기면 아무것도 바뀌지 않아야 한다.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, AuthoringSession session) = Show(project);

        view.AddEpisodeFromToolbar(); // new01 + 대본 워크북 생성
        view.RefreshFromDisk();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        string fileId = session.EnsureChapterBoard("ch05");
        session.Editor.AddDialogueNode(fileId, name: "new01");

        string episodes = Path.Combine(Path.GetDirectoryName(project.ChapterPath)!, "..", "episodes");
        string workbook = EpisodeLibrary.FindExisting(episodes, "new01")!;

        view.SelectEpisode("new01");
        view.FindControl<TextBox>("IdBox")!.Text = "ep_locked";

        // 엑셀이 잡고 있는 상황 — 공유 삭제 없이 연 핸들은 이동(File.Move)을 막는다.
        using (File.Open(workbook, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            view.RenameSelectedEpisode();
        }

        // 전부 그대로다 — 챕터 시트도, 대사 노드도, 파일도.
        ChapterGraphModel reread = ChapterWorkbookReader.Read(project.ChapterPath);
        Assert.NotNull(reread.FindEpisode("new01"));
        Assert.Null(reread.FindEpisode("ep_locked"));
        Assert.NotNull(EpisodeLibrary.FindExisting(episodes, "new01"));
        Assert.Null(EpisodeLibrary.FindExisting(episodes, "ep_locked"));
        Assert.Contains(session.Project.EnumerateNodes().OfType<DialogueNode>(),
            node => node.Name == "new01");
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
    public void 선택이_탭을_끌고_다니지_않는다() => HeadlessUi.Run(() =>
    {
        // 2026-08-16 소유자 — 대사 탭을 보며 노드를 갈아타는 흐름이 실사용의 대부분이라,
        // 무언가를 골라도 지금 보던 탭이 유지된다(편집 탭 강제 전환 폐지).
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        var tabs = view.FindControl<TabControl>("RightTabs")!;
        var conditionTab = view.FindControl<TabItem>("ConditionTab")!;
        var dialogueTab = view.FindControl<TabItem>("DialogueTab")!;

        // 아무것도 안 고른 처음에는 조건 탭이 앞에서 먼저 보인다.
        Assert.Equal(0, tabs.IndexFromContainer(conditionTab));
        Assert.Same(conditionTab, tabs.SelectedItem);

        // 노드를 골라도 탭은 그대로다.
        view.SelectEpisode("main05.02");
        Assert.Same(conditionTab, tabs.SelectedItem);

        // 대사 탭을 보며 다른 노드로 갈아타도 그대로다.
        tabs.SelectedItem = dialogueTab;
        view.SelectEpisode("main05.01");
        Assert.Same(dialogueTab, tabs.SelectedItem);

        // 간선을 골라도 마찬가지다.
        view.SelectEdgeKey("main05.02", "branch05.02A", "라루의 제안을 듣는다");
        Assert.Same(dialogueTab, tabs.SelectedItem);
    });

    [Fact]
    public void 간선을_선택하면_패널이_차고_적용이_엑셀로_간다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        view.SelectEdgeKey("main05.02", "branch05.02A", "라루의 제안을 듣는다");

        Assert.True(view.FindControl<StackPanel>("EdgePanel")!.IsVisible);
        Assert.False(view.FindControl<StackPanel>("PropertyPanel")!.IsVisible);
        Assert.Equal("1", view.FindControl<ComboBox>("EdgeLabelEditBox")!.SelectedItem); // 선택지수
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

    [Fact]
    public void 선택지_포트가_카드_오른쪽에_뚫린다() => HeadlessUi.Run(() =>
    {
        // 2026-08-16 개정 — 포트의 원천은 챕터 `선택지` 시트의 보이는 칸이다.
        // 주인 간선 있는 포트 = 채운 원(클릭 = 간선 선택), 주인 없는 칸 = 빈 원(클릭 = 에피소드 선택).
        using var project = new TempProject(SamplePath);

        // 주인 없는 칸 하나를 더한다 — main05.02→main05.end 간선은 없다.
        using (var workbook = new ClosedXML.Excel.XLWorkbook(project.ChapterPath))
        {
            ClosedXML.Excel.IXLWorksheet choices = workbook.Worksheet(ChapterSheetNames.Choices);
            int row = choices.LastRowUsed()!.RowNumber() + 1;
            choices.Cell(row, 1).SetValue("main05.02");
            choices.Cell(row, 2).SetValue("main05.end");
            choices.Cell(row, 3).SetValue(30);
            choices.Cell(row, 4).SetValue("셋째 길");
            workbook.Save();
        }

        (ChapterGraphView view, _) = Show(project);

        var canvas = view.FindControl<Canvas>("GraphCanvas")!;

        static void Press(Control control) => control.RaiseEvent(new Avalonia.Input.PointerPressedEventArgs(
            control, new Avalonia.Input.Pointer(0, Avalonia.Input.PointerType.Mouse, true),
            control, default, 0,
            new Avalonia.Input.PointerPointProperties(
                Avalonia.Input.RawInputModifiers.LeftMouseButton,
                Avalonia.Input.PointerUpdateKind.LeftButtonPressed),
            Avalonia.Input.KeyModifiers.None));

        // 포트 문구 셋 + 포트 원 셋 (main05.02의 보이는 칸 수).
        List<TextBlock> labels = canvas.Children.OfType<TextBlock>()
            .Where(block => (block.Text ?? "").StartsWith("라루의 제안을 듣는다") ||
                            block.Text == "혼자 문을 연다" || block.Text == "셋째 길")
            .ToList();
        Assert.Equal(3, labels.Count);
        Assert.Equal(3, canvas.Children.OfType<Avalonia.Controls.Shapes.Ellipse>().Count(port => port.Width == 9));

        // 주인 간선 있는 포트 클릭 = 그 간선 선택.
        Press(labels.Single(block => block.Text!.StartsWith("라루의 제안을 듣는다")));
        Assert.True(view.FindControl<StackPanel>("EdgePanel")!.IsVisible);

        // 주인 없는 칸 클릭 = 에피소드 선택 (검증 보고가 유령 칸을 따로 잡는다).
        Press(labels.Single(block => block.Text == "셋째 길"));
        Assert.True(view.FindControl<StackPanel>("PropertyPanel")!.IsVisible);
        Assert.Equal("main05.02", view.FindControl<TextBox>("IdBox")!.Text);
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

        public string EpisodesFolder => Path.Combine(_directory, "episodes");

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }

    /// <summary>라벨 드롭다운의 원천 — 그 에피소드 대본에 CHOICE/OPTION을 깔아 준다.</summary>
    internal static void WriteOptionsWorkbook(string episodesFolder, string episodeId, params string[] options)
    {
        Directory.CreateDirectory(episodesFolder);

        using var workbook = new ClosedXML.Excel.XLWorkbook();
        ClosedXML.Excel.IXLWorksheet sheet = workbook.AddWorksheet("대본");
        string[] headers = ["인덱스", "LineId", "유형", "태그", "조건라벨", "IN", "OUT", "화자", "내용"];

        for (int column = 1; column <= headers.Length; column++)
        {
            sheet.Cell(1, column).SetValue(headers[column - 1]);
        }

        sheet.Cell(2, 1).SetValue(10); sheet.Cell(2, 8).SetValue("윌로"); sheet.Cell(2, 9).SetValue("첫 줄");
        sheet.Cell(3, 1).SetValue(20); sheet.Cell(3, 3).SetValue("CHOICE");

        for (int index = 0; index < options.Length; index++)
        {
            int row = 4 + index;
            sheet.Cell(row, 1).SetValue(30 + index * 10);
            sheet.Cell(row, 3).SetValue("OPTION");
            sheet.Cell(row, 9).SetValue(options[index]);
        }

        workbook.SaveAs(Path.Combine(episodesFolder, episodeId + ".xlsx"));
    }
}
