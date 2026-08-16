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
    public void 길의_관문_콤보가_엑셀_셀로_왕복한다() => HeadlessUi.Run(() =>
    {
        // v8 (2026-08-16 소유자) — "보일지 말지는 이제 간선이 정한다". 표시·해금조건이
        // 에피소드에서 길(간선)로 옮겨 왔고, 편집도 간선 패널에서 한다.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        view.FindControl<CheckBox>("ExcelOnlyCheck")!.IsChecked = false;

        view.SelectEdgeKey("main05.02", "branch05.02A", "라루의 제안을 듣는다");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(view.FindControl<StackPanel>("EdgePanel")!.IsVisible);
        // 견본의 이 길에는 해금조건 '신뢰높음'이 걸려 있다.
        Assert.Equal("신뢰높음", view.FindControl<ComboBox>("EdgeConditionCombo")!.SelectedItem);

        view.FindControl<ComboBox>("EdgeVisibleCombo")!.SelectedItem = "지쳐있음";
        view.ApplyEdgeFromPanel();

        ChapterEdge reread = ChapterWorkbookReader.Read(project.ChapterPath).Edges.Single(edge =>
            edge.FromEpisodeId == "main05.02" && edge.ToEpisodeId == "branch05.02A");

        Assert.Equal("지쳐있음", reread.VisibleConditionLabel);
        Assert.Equal("신뢰높음", reread.ConditionLabel); // 안 바꾼 값은 그대로

        // 에피소드 패널은 읽기 전용 정보만 남는다.
        view.SelectEpisode("branch05.02A");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
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
        // 삭제 단추는 늘 같은 자리에 있고 상태만 바뀐다 (2026-08-16 소유자 보고) —
        // 체크를 푸는 순간 튀어나오면 그 자리를 누르던 손이 삭제를 누른다.
        var delete = view.FindControl<Button>("DeleteEpisodeButton")!;
        Assert.True(delete.IsVisible);
        Assert.False(delete.IsEnabled);

        // 체크를 풀면 열린다.
        view.FindControl<CheckBox>("ExcelOnlyCheck")!.IsChecked = false;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(view.FindControl<TextBox>("IdBox")!.IsEnabled);
        Assert.True(view.FindControl<Button>("AddNextEdgeButton")!.IsVisible);
        Assert.True(delete.IsVisible);
        Assert.True(delete.IsEnabled);
    });

    [Fact]
    public void 패널에서_간선을_더한다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);
        view.FindControl<CheckBox>("ExcelOnlyCheck")!.IsChecked = false;

        view.SelectEpisode("main05.01");
        view.FindControl<ComboBox>("EdgeTargetCombo")!.SelectedItem = "main05.end";
        view.AddEdgeFromPanel();

        ChapterGraphModel reread = ChapterWorkbookReader.Read(project.ChapterPath);

        ChapterEdge edge = reread.Edges.Single(candidate =>
            candidate.FromEpisodeId == "main05.01" && candidate.ToEpisodeId == "main05.end");
        // 칸 하나와 1:1로 짝했다 — 문구는 엑셀 `선택지` 시트의 대본 칸에서 적는다.
        Assert.NotNull(edge.ChoiceIndex);
        Assert.Contains(reread.ChoiceOptionsFor("main05.01"), slot => slot.Index == edge.ChoiceIndex);
    });

    [Fact]
    public void 선택지_줄을_클릭하면_그_아래에서_도착을_잇는다() => HeadlessUi.Run(() =>
    {
        // v7 — 목록의 줄 = 선택지 칸. [＋]가 칸을 늘리고(에피소드 선택지수 +1), 줄을 누르면
        // 그 줄 아래 폼에서 도착을 골라 잇는다(칸 하나 = 길 하나).
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);
        view.FindControl<CheckBox>("ExcelOnlyCheck")!.IsChecked = false;

        view.SelectEpisode("main05.01");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 칸 하나 추가 — 아직 간선은 없다.
        view.FindControl<Button>("AddNextEdgeButton")!.RaiseEvent(
            new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        view.RefreshFromDisk();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ChapterGraphModel added = ChapterWorkbookReader.Read(project.ChapterPath);
        Assert.Equal(2, added.FindEpisode("main05.01")!.ChoiceCount);

        string freeIndex = added.ChoiceOptionsFor("main05.01")
            .Single(slot => !added.Edges.Any(edge =>
                edge.FromEpisodeId == "main05.01" && edge.ChoiceIndex == slot.Index))
            .Index;

        // 그 줄을 눌러 폼을 열고 도착을 고른다.
        view.OpenSlotForm(freeIndex, rowIndex: 1, currentTarget: null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(view.FindControl<Grid>("EdgeFormPanel")!.IsVisible);
        Assert.Equal("잇기", view.FindControl<Button>("AddEdgeButton")!.Content);

        view.FindControl<ComboBox>("EdgeTargetCombo")!.SelectedItem = "main05.end";
        view.SubmitEdgeForm();

        ChapterGraphModel reread = ChapterWorkbookReader.Read(project.ChapterPath);
        ChapterEdge wired = reread.Edges.Single(candidate =>
            candidate.FromEpisodeId == "main05.01" && candidate.ToEpisodeId == "main05.end");
        Assert.Equal(freeIndex, wired.ChoiceIndex);
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

        // 대본 파일이 새 이름으로 옮겨졌고, 옛 이름은 남지 않았다 (그 챕터 폴더 안에서).
        Assert.NotNull(EpisodeLibrary.FindExisting(project.EpisodesFolder, "ep_renamed"));
        Assert.Null(EpisodeLibrary.FindExisting(project.EpisodesFolder, "new01"));

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

        string workbook = EpisodeLibrary.FindExisting(project.EpisodesFolder, "new01")!;

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
        Assert.NotNull(EpisodeLibrary.FindExisting(project.EpisodesFolder, "new01"));
        Assert.Null(EpisodeLibrary.FindExisting(project.EpisodesFolder, "ep_locked"));
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
    public void 처음_보이는_탭은_편집이고_선택이_탭을_끌고_다니지_않는다() => HeadlessUi.Run(() =>
    {
        // 2026-08-16 소유자 — 처음 열었을 때 손이 가는 곳은 지금 고른 하나(편집)이지
        // 챕터 전체 표가 아니다. 그리고 무언가를 골라도 보던 탭이 유지된다.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        var tabs = view.FindControl<TabControl>("RightTabs")!;
        var conditionTab = view.FindControl<TabItem>("ConditionTab")!;
        var editTab = view.FindControl<TabItem>("EditTab")!;

        Assert.Same(editTab, tabs.SelectedItem);

        // 노드를 골라도 탭은 그대로다.
        view.SelectEpisode("main05.02");
        Assert.Same(editTab, tabs.SelectedItem);

        // 챕터 탭을 보며 다른 노드로 갈아타도 그대로다.
        tabs.SelectedItem = conditionTab;
        view.SelectEpisode("main05.01");
        Assert.Same(conditionTab, tabs.SelectedItem);

        // 간선을 골라도 마찬가지다.
        view.SelectEdgeKey("main05.02", "branch05.02A", "라루의 제안을 듣는다");
        Assert.Same(conditionTab, tabs.SelectedItem);
    });

    [Fact]
    public void 대사는_선택지_아래에_접혀_있다() => HeadlessUi.Run(() =>
    {
        // 2026-08-16 소유자 — 별도 탭이던 대사를 편집 탭의 선택지 아래로 합쳤다
        // ("에피소드 노드의 정보량이 부족하다"). 접었다 펼 수 있다.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        Assert.Null(view.FindControl<TabItem>("DialogueTab")); // 탭은 사라졌다

        var dialogue = view.FindControl<Expander>("DialogueExpander")!;
        Assert.False(dialogue.IsExpanded); // 접힌 채로 시작한다

        view.SelectEpisode("main05.02");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 펼치면 그 에피소드의 대사 안내가 서 있다.
        dialogue.IsExpanded = true;
        Assert.NotNull(view.FindControl<SelectableTextBlock>("DialoguePreviewText"));
        Assert.False(string.IsNullOrEmpty(view.FindControl<TextBlock>("DialoguePreviewHeader")!.Text));
    });

    [Fact]
    public void 대사는_줄마다_카드로_서고_화자가_따로_보인다() => HeadlessUi.Run(() =>
    {
        // 2026-08-16 소유자 — 한 덩어리 글이던 미리보기를 줄 단위로 갈랐다:
        // 화자는 작고 진하게 위에, 대사는 그 아래.
        using var project = new TempProject(SamplePath);
        WriteOptionsWorkbook(project.EpisodesFolder, "main05.02", "라루의 제안을 듣는다");
        (ChapterGraphView view, _) = Show(project);

        view.SelectEpisode("main05.02");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var panel = view.FindControl<StackPanel>("DialoguePreviewPanel")!;
        Assert.NotEmpty(panel.Children);

        // 화자 줄(작고 진한 TextBlock)과 대사 줄(SelectableTextBlock)이 갈려 있다.
        List<TextBlock> speakers = panel.Children.OfType<Border>()
            .Select(card => card.Child)
            .OfType<StackPanel>()
            .SelectMany(stack => stack.Children.OfType<TextBlock>())
            .Where(block => block.FontWeight == Avalonia.Media.FontWeight.SemiBold)
            .ToList();

        Assert.Contains(speakers, block => block.Text == "윌로");

        // 복사용 통짜 텍스트는 남아 있되 화면에는 안 선다.
        var raw = view.FindControl<SelectableTextBlock>("DialoguePreviewText")!;
        Assert.False(raw.IsVisible);
        Assert.Contains("윌로: 첫 줄", raw.Text!);
    });

    [Fact]
    public void 간선을_선택하면_패널이_차고_적용이_엑셀로_간다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        view.SelectEdgeKey("main05.02", "branch05.02A", "라루의 제안을 듣는다");

        Assert.True(view.FindControl<StackPanel>("EdgePanel")!.IsVisible);
        Assert.False(view.FindControl<StackPanel>("PropertyPanel")!.IsVisible);
        // 짝 칸이 읽기 전용으로 보인다 (v7 — 문구는 엑셀 `선택지` 시트에서 고친다).
        Assert.Contains("라루의 제안을 듣는다",
            (string)view.FindControl<ComboBox>("EdgeLabelEditBox")!.SelectedItem!);
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

        // 아직 안 이은 칸 하나를 더한다 (v7 — 칸은 에피소드의 것, 간선은 인덱스로 짝한다).
        using (var workbook = new ClosedXML.Excel.XLWorkbook(project.ChapterPath))
        {
            ClosedXML.Excel.IXLWorksheet choices = workbook.Worksheet(ChapterSheetNames.Choices);
            int row = choices.LastRowUsed()!.RowNumber() + 1;
            choices.Cell(row, 1).SetValue("main05.02");
            choices.Cell(row, 2).SetValue(30);
            choices.Cell(row, 3).SetValue("셋째 길");
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

        /// <summary>그 챕터의 대본 폴더 — episodes/{ChapterId}/ (2026-08-16 챕터별 격리).</summary>
        public string EpisodesFolder => Path.Combine(_directory, "episodes", "ch05");

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
