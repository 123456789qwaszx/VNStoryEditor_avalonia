using Avalonia.Controls;
using Avalonia.VisualTree;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// 2단계 무대 1번 — 엑셀노드 배지 + 본문 잠금. 엑셀 소유 대본이 툴에서 고쳐지는 척하다
/// 다음 동기화에 증발하는 사고를 화면 단에서 막는다. 자유 노드는 그대로 편집된다.
/// </summary>
public sealed class ExcelNodeLockTests
{
    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    [Fact]
    public void 엑셀노드는_이름과_줄_추가가_잠기고_배너가_선다() => HeadlessUi.Run(() =>
    {
        (DialogueNodeEditor editor, AuthoringSession session, string nodeId) = ShowSyncedNode();

        Assert.True(editor.FindControl<TextBox>("NameBox")!.IsReadOnly);
        Assert.False(editor.FindControl<Button>("AddLineButton")!.IsEnabled);

        // 배너 — 왜 잠겼고 어디서 고치는지가 화면에 있다.
        var host = editor.FindControl<StackPanel>("LineHost")!;
        Assert.Contains(host.Children.OfType<Border>(), border =>
            (border.Child as TextBlock)?.Text?.Contains("엑셀노드") == true);

        // 줄 추가를 우회 호출해도 막힌다 — 대본 줄 수가 그대로다.
        DialogueNode node = session.Project.FindDialogue(nodeId)!;
        int before = session.Project.FindScript(node.ScriptId)!.ActiveLines.Count();
        editor.FindControl<Button>("AddLineButton")!.Command?.Execute(null);
        Assert.Equal(before, session.Project.FindScript(node.ScriptId)!.ActiveLines.Count());
    });

    [Fact]
    public void 엑셀노드는_본문_칸과_조건_set_태그까지_잠긴다() => HeadlessUi.Run(() =>
    {
        // 소유자 보고 — 화자는 잠겼는데 본문 칸은 클릭하면 열려 보이고, 조건·set 태그는
        // 눌러서 편집 플라이아웃이 열렸다. 읽기 전용이어도 "고쳐질 것 같은 모양"이면 실패다.
        (DialogueNodeEditor editor, _, _) = ShowSyncedNode();
        var host = editor.FindControl<StackPanel>("LineHost")!;

        // 본문 칸 — 캐럿조차 안 선다. (화자 자동완성 내부의 TextBox는 부모 비활성으로 이미 잠겨 제외.)
        List<TextBox> bodies = host.GetVisualDescendants().OfType<TextBox>()
            .Where(box => box.FindAncestorOfType<AutoCompleteBox>() is null)
            .ToList();
        Assert.NotEmpty(bodies);
        Assert.All(bodies, box => Assert.False(box.IsHitTestVisible));

        // 조건·선택·set 태그 — 버튼이 아니라 표시 전용 칩이다. 눌러 열 편집 길이 없다.
        List<string> tagButtons = host.GetVisualDescendants().OfType<Button>()
            .Select(button => (button.Content as TextBlock)?.Text ?? string.Empty)
            .Where(text =>
                text.StartsWith("set ", StringComparison.Ordinal) ||
                text.StartsWith("선택 ", StringComparison.Ordinal) ||
                text is "조건 종료" or "선택지 끝" or "신뢰높음")
            .ToList();
        Assert.Empty(tagButtons);
    });

    [Fact]
    public void 출구_후보에_엑셀노드가_없다() => HeadlessUi.Run(() =>
    {
        // 소유자 결정 (2026-08-14) — 에피소드 사이 흐름은 챕터 간선(기획자) 소유다.
        // 자유 노드의 출구로 엑셀노드를 고를 수 있으면 챕터 장부(표시/해금·스탯 환산·
        // cleared)를 지나치는 뒷길이 생기고, Yarn 점프라 "복귀"도 처음부터 다시 재생된다.
        (DialogueNodeEditor editor, AuthoringSession session, _) = ShowSyncedNode();

        string fileId = session.EnsureChapterBoard("ch05");
        DialogueNode free = session.Editor.AddDialogueNode(fileId, name: "곁가지");
        session.Editor.AddDialogueNode(fileId, name: "곁가지2");

        editor.Show(free.Id);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var targets = (List<StoryNode>)editor.FindControl<ComboBox>("DefaultExitCombo")!.Tag!;

        // 자유 노드는 후보다 — 자유끼리는 넘길 수 있다.
        Assert.Contains(targets, target => target.Name == "곁가지2");
        Assert.DoesNotContain(targets, target =>
            target is DialogueNode { ExcelEpisodeId: not null });

        // 출구가 꺼져 있으면 그 의미를 화면이 말한다 — 챕터 판이므로 "에피소드 종료".
        var hint = editor.FindControl<TextBlock>("ExitHintText")!;
        Assert.True(hint.IsVisible);
        Assert.Contains("에피소드 종료", hint.Text);
    });

    [Fact]
    public void 엑셀노드로_향하는_출구는_검증이_크게_말한다() => HeadlessUi.Run(() =>
    {
        // 편집기는 후보에서 빼지만, 이미 있는 연결(옛 프로젝트)은 막지 않고 경고한다.
        (_, AuthoringSession session, string excelNodeId) = ShowSyncedNode();

        string fileId = session.EnsureChapterBoard("ch05");
        DialogueNode free = session.Editor.AddDialogueNode(fileId, name: "우회로");
        session.Editor.SetExitTarget(free.Id, Vn.Authoring.Flow.ExitPortKind.Default, null, excelNodeId);

        ChapterGraphModel chapter = ChapterWorkbookReader.Read(
            Path.Combine(EpisodesRoot(session), "..", "chapters", "ch05.xlsx"));

        var warnings = EpisodeSyncService.WarnExitsIntoExcelNodes(session.Editor, fileId, chapter);

        ChapterDiagnostic warning = Assert.Single(warnings);
        Assert.Equal(ChapterDiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("우회로", warning.Message);
        Assert.Contains("챕터 간선", warning.Message);
    });

    [Fact]
    public void 자유_노드는_그대로_편집된다() => HeadlessUi.Run(() =>
    {
        var session = new AuthoringSession();
        using var project = new TempProject(SamplePath);
        session.Open(project.ManifestPath);

        string fileId = session.EnsureChapterBoard("ch05");
        DialogueNode free = session.Editor.AddDialogueNode(fileId, name: "자유씬");

        var editor = new DialogueNodeEditor();
        var window = new Window { Width = 1200, Height = 800, Content = editor };
        window.Show();
        editor.Attach(session);
        editor.Show(free.Id);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(editor.FindControl<TextBox>("NameBox")!.IsReadOnly);
        Assert.True(editor.FindControl<Button>("AddLineButton")!.IsEnabled);
    });

    [Fact]
    public void 그래프_카드_배지에_엑셀_표식이_붙는다() => HeadlessUi.Run(() =>
    {
        (_, AuthoringSession session, string nodeId) = ShowSyncedNode();

        Vn.Authoring.Graph.GraphProjection projection = Vn.Authoring.Graph.GraphProjectionBuilder.Build(
            session.Project,
            session.Project.Files.Select(file => file.Id).ToHashSet(StringComparer.Ordinal));

        Vn.Authoring.Graph.ExpandedNodeProjection card = projection.Items
            .OfType<Vn.Authoring.Graph.ExpandedNodeProjection>()
            .Single(item => item.NodeId == nodeId);

        Assert.StartsWith("📄 엑셀", card.Badge);
    });

    [Fact]
    public void 자유_노드가_챕터_조건을_바로_쓴다() => HeadlessUi.Run(() =>
    {
        // 2단계 4번 — 작가가 설정노드를 손으로 잇지 않아도, 판 위의 자유 노드가
        // 조건 드롭다운에서 챕터 라벨(A 계층)을 바로 고를 수 있어야 한다.
        (_, AuthoringSession session, _) = ShowSyncedNode();

        string fileId = session.EnsureChapterBoard("ch05");
        DialogueNode free = session.Editor.AddDialogueNode(fileId, name: "자유씬");

        ChapterGraphModel chapter = ChapterWorkbookReader.Read(
            Path.Combine(EpisodesRoot(session), "..", "chapters", "ch05.xlsx"));

        EpisodeSyncService.SupplyChapterConditionsToBoard(
            session.Editor, session.Definition, fileId, chapter);

        Vn.Authoring.Flow.AvailableConditionCatalog available =
            Vn.Authoring.Flow.AvailableConditionResolver.Resolve(
                session.Project, free.Id, session.Definition);

        Assert.Contains(available.Conditions, condition => condition.Name == "신뢰높음");

        // 멱등 — 두 번 불러도 공급 노드·조건이 늘지 않는다.
        EpisodeSyncService.SupplyChapterConditionsToBoard(
            session.Editor, session.Definition, fileId, chapter);

        Assert.Single(session.Project.EnumerateNodes().OfType<SetNode>(),
            node => node.Name == "챕터 ch05 조건");
    });

    [Fact]
    public void 자유_노드가_스탯을_set으로_바꾸면_경고한다() => HeadlessUi.Run(() =>
    {
        // 가드레일 — 스탯 변화의 원천은 엑셀 J열 하나여야 도달성 증명이 참을 말한다.
        (_, AuthoringSession session, _) = ShowSyncedNode();

        string fileId = session.EnsureChapterBoard("ch05");
        DialogueNode free = session.Editor.AddDialogueNode(fileId, name: "몰래스탯");
        string lineId = session.Project.FindScript(free.ScriptId)!.ActiveLines.First().Id;
        session.Editor.SetLineSetOperations(free.Id, lineId,
        [
            new SetOperation { Variable = "trust", Operator = SetOperatorKind.Add, Value = "1" }
        ]);

        ChapterGraphModel chapter = ChapterWorkbookReader.Read(
            Path.Combine(EpisodesRoot(session), "..", "chapters", "ch05.xlsx"));

        var warnings = EpisodeSyncService.WarnFreeNodeStatWrites(session.Editor, fileId, chapter);

        Assert.Contains(warnings, warning =>
            warning.Severity == ChapterDiagnosticSeverity.Warning &&
            warning.Message.Contains("몰래스탯") &&
            warning.Message.Contains("trust"));

        // 엑셀노드는 대상이 아니다 — J열이 원천이니까.
        Assert.DoesNotContain(warnings, warning => warning.Message.Contains("Story_ch05_02"));
    });

    private static string EpisodesRoot(AuthoringSession session) =>
        EpisodeLibrary.FolderFor(session.ProjectPath)!;

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <summary>견본 에피소드를 동기화해 엑셀노드를 만들고, 그 노드를 편집기에 띄운다.</summary>
    private static (DialogueNodeEditor Editor, AuthoringSession Session, string NodeId) ShowSyncedNode()
    {
        var session = new AuthoringSession();
        var project = new TempProject(SamplePath);
        session.Open(project.ManifestPath);

        string fileId = session.EnsureChapterBoard("ch05");
        ChapterGraphModel chapter = ChapterWorkbookReader.Read(project.ChapterPath);

        string workbook = Path.Combine(
            Path.GetDirectoryName(project.ChapterPath)!, "..", "episodes", "main05.02.xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(workbook)!);
        File.Copy(SamplePath, workbook);

        EpisodeSyncReport report = EpisodeSyncService.Sync(
            session.Editor, session.Definition, fileId, workbook, chapter);

        Assert.True(report.Applied, string.Join(" / ", report.Problems));

        var editor = new DialogueNodeEditor();
        var window = new Window { Width = 1200, Height = 800, Content = editor };
        window.Show();
        editor.Attach(session);
        editor.Show(report.DialogueNodeId);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return (editor, session, report.DialogueNodeId!);
    }

    private sealed class TempProject : IDisposable
    {
        private readonly string _directory;

        public TempProject(string samplePath)
        {
            _directory = Path.Combine(
                Path.GetTempPath(), "vn-excel-lock", Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path.Combine(_directory, ChapterLibrary.FolderName));
            File.Copy(samplePath, ChapterPath);

            ManifestPath = Path.Combine(_directory, "project" + ProjectManifestJson.FileExtension);
            ProjectStore.Save(ManifestPath, new StoryProject { Title = "잠금 검증" });
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
