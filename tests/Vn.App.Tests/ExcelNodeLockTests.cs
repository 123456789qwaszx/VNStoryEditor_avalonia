using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Flow;
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
    public void 엑셀노드는_이름과_줄_추가가_잠긴다() => HeadlessUi.Run(() =>
    {
        (DialogueNodeEditor editor, AuthoringSession session, string nodeId) = ShowSyncedNode();

        Assert.True(editor.FindControl<TextBox>("NameBox")!.IsReadOnly);
        Assert.False(editor.FindControl<Button>("AddLineButton")!.IsEnabled);

        // ⚠ 안내 띠는 2026-08-22에 사라졌다 (소유자) — 카드가 이미 잠겨 있고 손대려 하면
        // 상태줄이 같은 말을 하므로, 목록 맨 위의 세 줄은 매번 되풀이되는 소음이었다.
        var host = editor.FindControl<StackPanel>("LineHost")!;
        Assert.DoesNotContain(host.Children.OfType<Border>(), border =>
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
    public void 엑셀노드는_화자_고르기_단추까지_잠긴다() => HeadlessUi.Run(() =>
    {
        // 소유자 보고 (2026-08-23) — "대사편집에서 화자가 선택가능하고, 실제 반영은 안되더라도
        // 표기상으로 바꿔지는 것처럼 보이는데." 화자 칸은 잠겨 있었지만 옆의 ▾가 살아 있어서,
        // 거기서 고른 이름이 잠긴 칸에 써졌다(프로그램이 넣는 값은 IsEnabled를 안 본다).
        (DialogueNodeEditor editor, AuthoringSession session, _) = ShowSyncedNode();
        var host = editor.FindControl<StackPanel>("LineHost")!;

        // 화자 칸과 ▾가 함께 잠긴다 — 문이 둘이면 빗장도 둘이어야 한다.
        List<AutoCompleteBox> speakers = host.GetVisualDescendants().OfType<AutoCompleteBox>().ToList();
        Assert.NotEmpty(speakers);
        Assert.All(speakers, box => Assert.False(box.IsEnabled));

        List<Button> picks = host.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Content as string == "▾")
            .ToList();
        Assert.NotEmpty(picks);
        Assert.All(picks, button => Assert.False(button.IsEnabled));

        // 우회 호출로도 목록이 열리지 않고, 화자 글자가 그대로다.
        string before = speakers[0].Text ?? string.Empty;
        picks[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(before, speakers[0].Text ?? string.Empty);

        // 2026-08-24 — 말이 바뀌었다. 이제 잠금은 풀 수 있는 것이라, "엑셀에서 합니다"로
        // 끝내면 <b>여기서는 못 한다</b>는 거짓말이 된다. 어디서 푸는지를 말한다.
        Assert.Contains("읽기 전용", session.StatusMessage);
        Assert.Contains("대사 잠김", session.StatusMessage);
    });

    // ── 잠금을 풀 수 있다 (2026-08-24 소유자) ───────────────────────────────

    [Fact]
    public void 자물쇠_토글은_엑셀노드에만_선다() => HeadlessUi.Run(() =>
    {
        // 자유 노드는 늘 열려 있어 잠글 것이 없다 — 단추가 있으면 "여기도 잠기나" 싶다.
        (DialogueNodeEditor editor, AuthoringSession session, _) = ShowSyncedNode();

        var toggle = editor.FindControl<ToggleButton>("ExcelTextLockToggle")!;

        Assert.True(toggle.IsVisible);
        Assert.True(toggle.IsEnabled, "되쓸 엑셀 자리를 찾았어야 열 수 있다");
        Assert.False(toggle.IsChecked);

        string fileId = session.EnsureChapterBoard("ch05");
        DialogueNode free = session.Editor.AddDialogueNode(fileId, name: "자유씬");

        editor.Show(free.Id);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(toggle.IsVisible);
    });

    [Fact]
    public void 풀면_본문과_화자가_열린다() => HeadlessUi.Run(() =>
    {
        (DialogueNodeEditor editor, _, _) = ShowSyncedNode();

        Unlock(editor);

        var host = editor.FindControl<StackPanel>("LineHost")!;

        List<TextBox> bodies = host.GetVisualDescendants().OfType<TextBox>()
            .Where(box => box.FindAncestorOfType<AutoCompleteBox>() is null)
            .ToList();

        Assert.NotEmpty(bodies);
        Assert.All(bodies, box => Assert.False(box.IsReadOnly));
        Assert.All(bodies, box => Assert.True(box.IsHitTestVisible));

        List<AutoCompleteBox> speakers =
            host.GetVisualDescendants().OfType<AutoCompleteBox>().ToList();

        Assert.NotEmpty(speakers);
        Assert.All(speakers, box => Assert.True(box.IsEnabled));
    });

    [Fact]
    public void 풀어도_줄_추가와_이름은_잠긴_채다() => HeadlessUi.Run(() =>
    {
        // ⛔ 푼 것은 화자·내용 두 칸뿐이다. 표의 구조와 노드의 신원은 엑셀 소유 그대로다 —
        // 그쪽까지 열면 인덱스 재배치를 두 곳이 갖는다.
        (DialogueNodeEditor editor, _, _) = ShowSyncedNode();

        Unlock(editor);

        Assert.True(editor.FindControl<TextBox>("NameBox")!.IsReadOnly);
        Assert.False(editor.FindControl<Button>("AddLineButton")!.IsEnabled);
    });

    [Fact]
    public void 풀고_고친_대사가_엑셀_셀까지_간다() => HeadlessUi.Run(() =>
    {
        // ⛔ 이 기능의 전부다. 노드만 고치면 다음 동기화가 지운다
        // (`EpisodeLineEditorTests.노드만_고치면_다음_동기화가_지운다`).
        (DialogueNodeEditor editor, AuthoringSession session, string nodeId) = ShowSyncedNode();

        Unlock(editor);

        var host = editor.FindControl<StackPanel>("LineHost")!;
        TextBox body = host.GetVisualDescendants().OfType<TextBox>()
            .First(box => box.FindAncestorOfType<AutoCompleteBox>() is null);

        // 엑셀노드는 초점을 잃을 때 낸다 — 자판마다 워크북을 두드리지 않는다.
        // ⚠ 초점을 진짜로 옮긴다: LostFocus는 FocusChangedEventArgs를 요구해서
        // RaiseEvent로 흉내내면 캐스트에서 터진다(`ChapterEditGateTests`가 같은 것을 배웠다).
        Type(editor, body, "연출 그래프에서 고친 대사");

        DialogueNode node = session.Project.FindDialogue(nodeId)!;
        string workbook = EpisodeLibrary.FindExisting(
            EpisodeLibrary.FolderFor(session.ProjectPath, "ch05")!, "main05.02")!;

        Assert.Contains(
            EpisodeWorkbookReader.Read(workbook).Rows,
            row => row.Text == "연출 그래프에서 고친 대사");

        // 그리고 노드도 같은 말을 한다 — 둘이 어긋나면 다음 동기화가 사람의 글을 지운다.
        Assert.Contains(
            session.Project.FindScript(node.ScriptId)!.Locales
                .Single(locale => locale.Locale == session.Project.FindScript(node.ScriptId)!.PrimaryLocale)
                .Entries.Values,
            line => line.Text == "연출 그래프에서 고친 대사");
    });

    [Fact]
    public void 엑셀이_잡고_있으면_노드도_안_고친다() => HeadlessUi.Run(() =>
    {
        // ⛔ 순서가 곧 규칙이다. 셀에 못 썼는데 노드만 고치면 화면과 파일이 다른 말을 하고,
        // 다음 동기화가 사람이 방금 쓴 글을 지운다.
        (DialogueNodeEditor editor, AuthoringSession session, string nodeId) = ShowSyncedNode();

        Unlock(editor);

        var host = editor.FindControl<StackPanel>("LineHost")!;
        TextBox body = host.GetVisualDescendants().OfType<TextBox>()
            .First(box => box.FindAncestorOfType<AutoCompleteBox>() is null);

        DialogueNode node = session.Project.FindDialogue(nodeId)!;
        string workbook = EpisodeLibrary.FindExisting(
            EpisodeLibrary.FolderFor(session.ProjectPath, "ch05")!, "main05.02")!;

        using (new FileStream(workbook, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Type(editor, body, "엑셀이 잡고 있는 동안 쓴 글");
        }

        // 침묵 금지 — 안 써졌다는 사실이 사람에게 닿는다.
        Assert.Contains("엑셀이", session.StatusMessage);

        // 노드는 그대로다.
        Assert.DoesNotContain(
            session.Project.FindScript(node.ScriptId)!.Locales
                .SelectMany(locale => locale.Entries.Values),
            line => line.Text == "엑셀이 잡고 있는 동안 쓴 글");
    });

    [Fact]
    public void 다른_노드로_옮기면_다시_잠긴다() => HeadlessUi.Run(() =>
    {
        // ⚠ 열어 둔 것을 잊고 다음 노드에서 무심코 고치는 길을 없앤다 —
        // 원본은 여전히 엑셀이고, 여는 것은 잠깐의 예외여야 한다.
        (DialogueNodeEditor editor, AuthoringSession session, string nodeId) = ShowSyncedNode();

        Unlock(editor);

        var toggle = editor.FindControl<ToggleButton>("ExcelTextLockToggle")!;
        Assert.True(toggle.IsChecked);

        string fileId = session.EnsureChapterBoard("ch05");
        DialogueNode other = session.Editor.AddDialogueNode(fileId, name: "다른 씬");

        editor.Show(other.Id);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 그리고 돌아와도 잠겨 있다.
        editor.Show(nodeId);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(toggle.IsChecked);
        Assert.Contains("잠김", toggle.Content as string);
    });

    [Fact]
    public void 엑셀노드의_분기_이후_레일은_표시뿐이다() => HeadlessUi.Run(() =>
    {
        // 소유자 보고 (2026-08-23) — "그 아래 → 분기 이후, 이걸 클릭해서도 연결이 끊고
        // 이어지는 기능이 있는데, 이 기능도 엑셀노드에서는 사용이 안되게 막아줘."
        // 잇고 떼는 자리는 연출 그래프 카드의 IF 갈래 포트 하나로 모은다.
        (DialogueNodeEditor editor, AuthoringSession session, string nodeId) = ShowSyncedNode();

        DialogueNode node = session.Project.FindDialogue(nodeId)!;
        string fileId = session.Project.Files.Single(file => file.Nodes.Contains(node)).Id;
        DialogueNode scene = session.Editor.AddDialogueNode(fileId, name: "곁가지씬");

        // 갈래 하나에 실제로 씬을 매단다 — 레일은 출구가 있는 줄에만 선다.
        DialogueFlow flow = ConditionFlowResolver.Resolve(node, session.Project, session.Definition);
        ConditionBranch branch = flow.Branches.First(candidate => candidate.OpenLineId.Length > 0);
        session.Editor.SetExitTarget(nodeId, ExitPortKind.Branch, branch.OpenLineId, scene.Id);

        editor.Show(nodeId);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var host = editor.FindControl<StackPanel>("LineHost")!;

        List<TextBlock> rails = host.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.Text?.StartsWith("→ 분기 이후", StringComparison.Ordinal) == true)
            .ToList();

        Assert.NotEmpty(rails);

        // 글이지 단추가 아니다 — 잠김이 모양으로 보인다. 어디로 가는지는 계속 읽힌다.
        Assert.All(rails, block =>
            Assert.Null(block.FindAncestorOfType<Button>()));
        Assert.Contains(rails, block => block.Text!.Contains("곁가지씬"));
    });

    [Fact]
    public void 출구_후보에_엑셀노드가_없다() => HeadlessUi.Run(() =>
    {
        // 소유자 결정 (2026-08-14) — 에피소드 사이 흐름은 챕터 간선(기획자) 소유다.
        // 자유 노드의 출구로 엑셀노드를 고를 수 있으면 챕터 장부(표시/해금·스탯 환산·
        // cleared)를 지나치는 뒷길이 생기고, Yarn 점프라 "복귀"도 처음부터 다시 재생된다.
        (DialogueNodeEditor editor, AuthoringSession session, string excelNodeId) = ShowSyncedNode();

        string fileId = session.EnsureChapterBoard("ch05");
        DialogueNode free = session.Editor.AddDialogueNode(fileId, name: "곁가지");
        session.Editor.AddDialogueNode(fileId, name: "곁가지2");

        // ⚠ [기본 출구] 편집 구역은 2026-08-22에 사라졌다 (소유자) — 같은 값을 판의
        // 레일 칩이 편집하므로 창구를 하나로 줄였다. 후보 규칙은 갈래(detour) 출구가
        // 그대로 물려받았고, 여기서 지키는 것은 그 규칙이다.
        editor.Show(excelNodeId);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Null(editor.FindControl<Grid>("DefaultExitControls"));
        Assert.Null(editor.FindControl<ComboBox>("DefaultExitCombo"));
        Assert.Null(editor.FindControl<TextBlock>("DefaultExitSubtitle"));

        // 갈래 출구의 후보 — 자유 노드만 남고 엑셀노드는 빠진다.
        List<StoryNode> targets = editor.ExitTargetsProbe(free.Id);

        Assert.Contains(targets, target => target.Name == "곁가지2");
        Assert.DoesNotContain(targets, target =>
            target is DialogueNode { ExcelEpisodeId: not null });
    });

    [Fact]
    public void 엑셀노드로_향하는_출구는_검증이_크게_말한다() => HeadlessUi.Run(() =>
    {
        // 편집기는 후보에서 빼지만, 이미 있는 연결(옛 프로젝트)은 막지 않고 경고한다.
        (_, AuthoringSession session, string excelNodeId) = ShowSyncedNode();

        string fileId = session.EnsureChapterBoard("ch05");
        DialogueNode free = session.Editor.AddDialogueNode(fileId, name: "우회로");

        // 갈래(detour) 출구가 엑셀노드를 가리키면 여전히 크게 말한다.
        free.BranchExits["ln_legacy"] = excelNodeId;

        // 커스텀 노드의 기본 출구는 죽었다 (2026-08-21) — 구판 데이터가 엑셀노드를
        // 가리키고 있어도 실행이 안 보는 값이라 경고도 내지 않는다.
        free.DefaultExitTargetNodeId = excelNodeId;

        ChapterGraphModel chapter = ChapterWorkbookReader.Read(
            Path.Combine(EpisodesRoot(session), "..", "chapters", "ch05.xlsx"));

        var warnings = EpisodeSyncService.WarnExitsIntoExcelNodes(session.Editor, fileId, chapter);

        ChapterDiagnostic warning = Assert.Single(warnings);
        Assert.Equal(ChapterDiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("우회로", warning.Message);
        Assert.Contains("갈래 출구", warning.Message);
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
    public void 챕터_조건_공급노드는_시나리오_그래프에_보이지_않는다() => HeadlessUi.Run(() =>
    {
        // A 계층 격리 (2026-08-15 소유자) — 챕터의 조건 식(스탯 변수)은 기획자의 자료다.
        // 공급 설정노드가 시나리오 그래프에 카드·링크로 서 있으면 작가에게 노출된다.
        // 데이터(공급·드롭다운 라벨)는 살아 있되, 화면에서는 존재하지 않는다.
        (_, AuthoringSession session, _) = ShowSyncedNode();

        string fileId = session.EnsureChapterBoard("ch05");
        DialogueNode free = session.Editor.AddDialogueNode(fileId, name: "자유씬");

        ChapterGraphModel chapter = ChapterWorkbookReader.Read(
            Path.Combine(EpisodesRoot(session), "..", "chapters", "ch05.xlsx"));

        EpisodeSyncService.SupplyChapterConditionsToBoard(
            session.Editor, session.Definition, fileId, chapter);

        SetNode supply = session.Project.EnumerateNodes().OfType<SetNode>()
            .Single(node => node.Name == "챕터 ch05 조건");

        // 펼친 판 — 카드도, 조건 공급 간선도 없다.
        Vn.Authoring.Graph.GraphProjection expanded = Vn.Authoring.Graph.GraphProjectionBuilder.Build(
            session.Project,
            session.Project.Files.Select(file => file.Id).ToHashSet(StringComparer.Ordinal));

        Assert.DoesNotContain(expanded.Items.OfType<Vn.Authoring.Graph.ExpandedNodeProjection>(),
            item => item.NodeId == supply.Id);
        Assert.DoesNotContain(expanded.Connections,
            connection => connection.SourceNodeId == supply.Id || connection.TargetNodeId == supply.Id);

        // 접힌 파일 프록시의 행 목록에도 없다.
        Vn.Authoring.Graph.GraphProjection collapsed = Vn.Authoring.Graph.GraphProjectionBuilder.Build(
            session.Project, new HashSet<string>(StringComparer.Ordinal));

        Assert.DoesNotContain(
            collapsed.Items.OfType<Vn.Authoring.Graph.CollapsedFileProjection>()
                .SelectMany(proxy => proxy.Nodes),
            entry => entry.NodeId == supply.Id);

        // 공급 자체는 살아 있다 — 작가는 라벨만 본다.
        Vn.Authoring.Flow.AvailableConditionCatalog available =
            Vn.Authoring.Flow.AvailableConditionResolver.Resolve(
                session.Project, free.Id, session.Definition);

        Assert.Contains(available.Conditions, condition => condition.Name == "신뢰높음");
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

    /// <summary>
    /// 칸에 글을 치고 <b>초점을 진짜로 옮긴다</b> — 엑셀노드는 그때 낸다. 이름 칸으로
    /// 옮기는 것은 그것이 늘 있고 읽기 전용이라 아무 일도 안 일으키기 때문이다.
    /// </summary>
    private static void Type(DialogueNodeEditor editor, TextBox box, string text)
    {
        box.Focus();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        box.Text = text;

        editor.FindControl<TextBox>("NameBox")!.Focus();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>대사 잠금을 푼다 — 사람이 토글을 누르는 것과 같은 길이다.</summary>
    private static void Unlock(DialogueNodeEditor editor)
    {
        editor.FindControl<ToggleButton>("ExcelTextLockToggle")!.IsChecked = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

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

        // ⚠ 대본은 <b>그 챕터의</b> 폴더에 산다 — episodes/{ChapterId}/{Id}.xlsx
        // (2026-08-16). 예전 이 헬퍼는 구판 평면 자리(episodes/{Id}.xlsx)에 두었는데,
        // 그러면 되쓰기가 그 줄의 엑셀 자리를 못 찾아 대사 잠금 토글이 열리지 않는다
        // (2026-08-24에 그 토글을 만들며 드러났다).
        string workbook = Path.Combine(
            Path.GetDirectoryName(project.ChapterPath)!, "..", "episodes", "ch05", "main05.02.xlsx");
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
