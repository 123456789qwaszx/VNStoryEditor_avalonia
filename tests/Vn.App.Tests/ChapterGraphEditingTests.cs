using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
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

        // 에피소드를 고르면 그 패널로 넘어간다 — 간선 패널은 닫힌다.
        view.SelectEpisode("branch05.02A");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(view.FindControl<StackPanel>("PropertyPanel")!.IsVisible);
        Assert.False(view.FindControl<StackPanel>("EdgePanel")!.IsVisible);
    });

    [Fact]
    public void 간선_패널의_선택지도_챕터의_모든_문구에서_고른다() => HeadlessUi.Run(() =>
    {
        // v9 (2026-08-17 소유자) — "간선에서도 선택지 드롭다운이 되도록." 그래프에서 길을
        // 눌러 연 패널에서도 문구를 고르고, [적용] 한 번에 관문과 함께 저장된다.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        view.SelectEdgeKey("main05.02", "branch05.02A", "라루의 제안을 듣는다");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var labels = view.FindControl<ComboBox>("EdgeLabelEditBox")!;
        Assert.True(labels.IsEnabled); // 읽기 전용이 아니다 — 여기서 고친다
        Assert.Equal("라루의 제안을 듣는다", labels.SelectedItem);
        // 목록은 챕터 전체 사전이다 — 다른 에피소드가 쓰는 문구까지 담긴다.
        Assert.Contains("혼자 문을 연다", (IEnumerable<string>)labels.ItemsSource!);

        labels.SelectedItem = "혼자 문을 연다";
        view.FindControl<ComboBox>("EdgeVisibleCombo")!.SelectedItem = "지쳐있음";
        view.ApplyEdgeFromPanel();

        ChapterEdge reread = ChapterWorkbookReader.Read(project.ChapterPath).Edges.Single(edge =>
            edge.FromEpisodeId == "main05.02" && edge.ToEpisodeId == "branch05.02A");

        Assert.Equal("혼자 문을 연다", reread.OptionLabel); // 문구가 한 저장에 함께 실렸다
        Assert.Equal("지쳐있음", reread.VisibleConditionLabel);
        Assert.Equal("신뢰높음", reread.ConditionLabel);

        // 문구는 신원의 일부다 — 바꿔도 선택이 풀리지 않고 그 길이 계속 열려 있다.
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(view.FindControl<StackPanel>("EdgePanel")!.IsVisible);
        Assert.Equal("혼자 문을 연다", view.FindControl<ComboBox>("EdgeLabelEditBox")!.SelectedItem);
    });

    [Fact]
    public void 에피소드를_지워도_챕터의_선택지_문구는_남는다() => HeadlessUi.Run(() =>
    {
        // v9 — 사전은 챕터 전체의 어휘다. 에피소드 하나가 사라졌다고 낱말을 지우면
        // 다른 에피소드의 드롭다운에서도 사라진다.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        view.SelectEpisode("branch05.02A");
        view.DeleteSelectedEpisode();

        ChapterGraphModel reread = ChapterWorkbookReader.Read(project.ChapterPath);

        Assert.Null(reread.FindEpisode("branch05.02A"));
        Assert.DoesNotContain(reread.Edges, edge => edge.ToEpisodeId == "branch05.02A");
        Assert.Contains(reread.ChoiceOptions, option => option.Text == "라루의 제안을 듣는다");
    });

    [Fact]
    public void 엑셀이_잡고_있으면_툴_편집_창구가_닫힌다() => HeadlessUi.Run(() =>
    {
        // 2026-08-24 소유자 — "엑셀이 켜지면 자동으로 잠기면서 편집이 불가능하게 막는게
        // 좋겠어." 예전에는 체크 하나가 문지기였고 기본이 켬이었다. 이제 문을 잠그는 것은
        // 사람이 아니라 <b>엑셀이 그 파일을 잡았다는 사실</b>이다.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project, locked: true);

        view.SelectEpisode("main05.02");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(view.FindControl<Border>("LockBanner")!.IsVisible);
        Assert.False(view.FindControl<TextBox>("IdBox")!.IsEnabled);
        Assert.False(view.FindControl<ComboBox>("VisibleCombo")!.IsEnabled);
        Assert.False(view.FindControl<Button>("AddNextEdgeButton")!.IsVisible);
        Assert.False(view.FindControl<Grid>("EdgeFormPanel")!.IsVisible);
        // 삭제 단추는 늘 같은 자리에 있고 상태만 바뀐다 (2026-08-16 소유자 보고) —
        // 잠금이 풀리는 순간 튀어나오면 그 자리를 누르던 손이 삭제를 누른다.
        var delete = view.FindControl<Button>("DeleteEpisodeButton")!;
        Assert.True(delete.IsVisible);
        Assert.False(delete.IsEnabled);
    });

    [Fact]
    public void 엑셀이_안_잡고_있으면_편집이_열려_있다() => HeadlessUi.Run(() =>
    {
        // ⚠ 이것이 2026-08-24에 뒤집힌 기본값이다 — 전에는 닫힌 채로 시작했다.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        view.SelectEpisode("main05.02");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(view.FindControl<Border>("LockBanner")!.IsVisible);
        Assert.True(view.FindControl<TextBox>("IdBox")!.IsEnabled);
        Assert.True(view.FindControl<Button>("AddNextEdgeButton")!.IsVisible);

        var delete = view.FindControl<Button>("DeleteEpisodeButton")!;
        Assert.True(delete.IsVisible);
        Assert.True(delete.IsEnabled);
    });

    [Fact]
    public void 편집_폼은_도착을_먼저_묻는다() => HeadlessUi.Run(() =>
    {
        // 2026-08-24 소유자 — 길을 놓을 때 사람이 먼저 정하는 것은 "어디로 가나"다.
        // 예전에는 (선택지) → (도착) 순이었다.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        Assert.Equal(0, Grid.GetColumn(view.FindControl<ComboBox>("EdgeTargetCombo")!));
        Assert.Equal(2, Grid.GetColumn(view.FindControl<ComboBox>("EdgeLabelBox")!));
    });

    [Fact]
    public void 열려_있을_때는_이름_칸이_아무_말도_안_한다() => HeadlessUi.Run(() =>
    {
        // 2026-08-24 소유자 "이런 쓸모없는 설명들을 제거해줘" — 하루 전 이 자리에 붙였던
        // "이름 — 고치고 Enter"를 걷었다. 개명 자리인 줄 알리는 몫은 툴팁이 진다.
        //
        // ⛔ 안내를 지우면서 <b>잠금 사유까지</b> 지우지 않는다 — 아래 짝 테스트가 그 반쪽을
        //    붙든다. 기본값은 말할 것이 없고, 사람이 안 한 잠금은 말해 줘야 한다.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);
        view.SelectEpisode("main05.01");

        TextBlock hint = view.FindControl<TextBlock>("IdBoxHint")!;

        Assert.False(hint.IsVisible);
        Assert.True(view.FindControl<TextBox>("IdBox")!.IsEnabled);

        // 툴팁은 남는다 — 물을 때만 뜨고 자리를 안 먹는다.
        var tip = ToolTip.GetTip(view.FindControl<TextBox>("IdBox")!) as string;
        Assert.Contains("Enter", tip!);
    });

    [Fact]
    public void 상시_안내들이_편집_패널에서_사라졌다() => HeadlessUi.Run(() =>
    {
        // 2026-08-24 소유자 — 머리글 "에피소드", 읽기 전용 값 줄(`대사엔트리: … · 엑셀 N행`),
        // "길 하나 = 선택지 하나…" 셋을 걷었다. 고칠 수도 없는 글이 상시로 자리를 먹었다.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);
        view.SelectEpisode("main05.01");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Null(view.FindControl<TextBlock>("EpisodeFactsText"));

        string[] gone =
        [
            "에피소드",
            "길 하나 = 선택지 하나",
            "대사엔트리"
        ];

        List<string> standing = view.FindControl<StackPanel>("PropertyPanel")!
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(block => block.IsVisible)
            .Select(block => block.Text ?? string.Empty)
            .ToList();

        foreach (string phrase in gone)
        {
            Assert.DoesNotContain(standing, text => text.Contains(phrase, StringComparison.Ordinal));
        }

        // 남아야 하는 것 — 선택지 머리글은 표의 이름표라 안내가 아니다.
        Assert.Contains(standing, text => text.Contains("선택지", StringComparison.Ordinal));
    });

    [Fact]
    public void 잠겼을_때는_이름_칸이_왜_잠겼는지_말한다() => HeadlessUi.Run(() =>
    {
        // 비활성 칸이 아무 말도 없으면 "이 기능이 없다"로 읽힌다 — 실제로 그렇게 읽혔다.
        // ⛔ "엑셀에서만 편집 체크를 푸세요"로는 돌아가지 않는다: 풀 체크가 없다.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project, locked: true);
        view.SelectEpisode("main05.01");

        TextBlock hint = view.FindControl<TextBlock>("IdBoxHint")!;

        Assert.True(hint.IsVisible);
        Assert.Contains("엑셀", hint.Text);
        Assert.False(view.FindControl<TextBox>("IdBox")!.IsEnabled);
    });

    [Fact]
    public void 패널에서_간선을_더한다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        view.SelectEpisode("main05.01");
        view.FindControl<ComboBox>("EdgeTargetCombo")!.SelectedItem = "main05.end";
        view.AddEdgeFromPanel();

        ChapterGraphModel reread = ChapterWorkbookReader.Read(project.ChapterPath);

        ChapterEdge edge = reread.Edges.Single(candidate =>
            candidate.FromEpisodeId == "main05.01" && candidate.ToEpisodeId == "main05.end");
        // v12 — 툴이 놓는 길에는 기본 문구가 들어간다. 문구는 그 줄을 눌러 고친다.
        Assert.Equal("계속", edge.OptionLabel);
    });

    [Fact]
    public void 선택지는_챕터의_모든_문구_중에서_고른다() => HeadlessUi.Run(() =>
    {
        // v9 (2026-08-17 소유자) — "자기 것만 고르는 게 아니라 모든 선택지 중에서 자유자재로."
        // 견본의 사전에는 main05.02가 쓰는 문구 둘이 있고, main05.01에서도 그것을 고른다.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        view.SelectEpisode("main05.01");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // [＋] — 빈 폼이 열린다. 아직 아무것도 안 쓴다.
        view.FindControl<Button>("AddNextEdgeButton")!.RaiseEvent(
            new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(view.FindControl<Grid>("EdgeFormPanel")!.IsVisible);
        Assert.Equal("잇기", view.FindControl<Button>("AddEdgeButton")!.Content);

        // 문구 드롭다운은 다른 에피소드가 쓰는 문구까지 전부 담는다.
        var labels = view.FindControl<ComboBox>("EdgeLabelBox")!;
        Assert.Contains("라루의 제안을 듣는다", (IEnumerable<string>)labels.ItemsSource!);

        labels.SelectedItem = "라루의 제안을 듣는다";
        view.FindControl<ComboBox>("EdgeTargetCombo")!.SelectedItem = "main05.end";
        view.SubmitEdgeForm();

        ChapterGraphModel reread = ChapterWorkbookReader.Read(project.ChapterPath);
        ChapterEdge wired = reread.Edges.Single(candidate =>
            candidate.FromEpisodeId == "main05.01" && candidate.ToEpisodeId == "main05.end");
        Assert.Equal("라루의 제안을 듣는다", wired.OptionLabel);

        // 같은 문구를 두 에피소드가 쓰고 있어도 사전에는 한 줄뿐이다.
        Assert.Single(reread.ChoiceOptions, option => option.Text == "라루의 제안을 듣는다");
    });

    [Fact]
    public void 선택지_줄을_클릭하면_그_아래에서_문구와_도착을_고친다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        view.SelectEpisode("main05.02");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ChapterGraphModel model = ChapterWorkbookReader.Read(project.ChapterPath);
        ChapterEdge target = model.Edges.Single(edge =>
            edge.FromEpisodeId == "main05.02" && edge.ToEpisodeId == "branch05.02A");

        view.OpenEdgeForm(target, rowIndex: 0);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(view.FindControl<Grid>("EdgeFormPanel")!.IsVisible);
        Assert.Equal("수정", view.FindControl<Button>("AddEdgeButton")!.Content);
        Assert.Equal("라루의 제안을 듣는다", view.FindControl<ComboBox>("EdgeLabelBox")!.SelectedItem);

        // 문구를 바꿔 저장 — 도착은 그대로다.
        view.FindControl<ComboBox>("EdgeLabelBox")!.SelectedItem = "혼자 문을 연다";
        view.SubmitEdgeForm();

        ChapterGraphModel reread = ChapterWorkbookReader.Read(project.ChapterPath);
        ChapterEdge moved = reread.Edges.Single(edge => edge.ToEpisodeId == "branch05.02A");
        Assert.Equal("혼자 문을 연다", moved.OptionLabel);
        Assert.Equal("신뢰높음", moved.ConditionLabel); // 관문은 길에 붙어 따라온다
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

        // ⚠ 대사 노드는 <b>동기화가 이미 세워 뒀다</b> (2026-08-22) — 예전에는 여기서
        // 손으로 흉내내야 했다. 목록이 달라지면 재읽기가 동기화를 부르므로, 에피소드를
        // 더한 그 자리에서 노드가 선다(그 전에는 엑셀 파일이 생겨 감시자가 울어야 했다).
        Assert.Contains(session.Project.EnumerateNodes().OfType<DialogueNode>(),
            node => node.ExcelEpisodeId == "new01");

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

    /// <summary>
    /// 2026-08-22 소유자 보고 — *"챕터그래프에서 에피소드를 추가했는데 그게 즉각적으로
    /// 연출그래프에 반영이 안 돼. 노드를 더블클릭해서 엑셀을 열어서 엑셀파일을
    /// 생성시켜야지 그제야 노드가 추가되는 버그."*
    ///
    /// 노드를 세우는 것은 동기화뿐인데 그것이 <b>대본 폴더가 바뀔 때만</b> 돌았다 —
    /// 그래서 엑셀 파일이 새로 생겨 감시자가 우는 날에야 노드가 섰다. 이제 <b>에피소드
    /// 목록이 달라지면</b> 폴더가 그대로여도 돈다.
    /// </summary>
    [Fact]
    public void 에피소드를_더하면_그_자리에서_대사_노드가_선다() => HeadlessUi.Run(() =>
    {
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, AuthoringSession session) = Show(project);

        Assert.DoesNotContain(session.Project.EnumerateNodes().OfType<DialogueNode>(),
            node => node.ExcelEpisodeId == "new01");

        view.AddEpisodeFromToolbar();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // QueueReload 한 차례

        // 엑셀을 열지 않았는데도 노드가 서 있다 — 목록이 달라진 것이 곧 동기화의 이유다.
        Assert.Contains(session.Project.EnumerateNodes().OfType<DialogueNode>(),
            node => node.ExcelEpisodeId == "new01");
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
    public void 탭_순서는_편집_화자_챕터다() => HeadlessUi.Run(() =>
    {
        // 2026-08-24 소유자 "편집/화자/챕터가 되도록 챕터를 가장 뒤에 보내" — [편집]은
        // 고른 하나를 다루는 자리라 첫째이고, [챕터]는 판 전체의 읽기 전용 표라 가장 뒤다.
        //
        // ⚠ 첫 화면은 순서가 아니라 `SelectedItem`이 정한다 — 아래 짝 테스트가 그것을
        //    따로 붙든다. 둘을 한 테스트에 묶으면 순서를 바꾸는 날 무엇이 깨졌는지 흐려진다.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        string[] headers = view.FindControl<TabControl>("RightTabs")!
            .Items.OfType<TabItem>()
            .Select(tab => (string)tab.Header!)
            .ToArray();

        Assert.Equal(["편집", "화자", "챕터"], headers);
    });

    [Fact]
    public void 툴바에서_다시_읽기와_픽스처가_사라졌다() => HeadlessUi.Run(() =>
    {
        // 2026-08-24 소유자 "꽤 오래 다뤘는데, 단 한 번도 안 썼어."
        //
        // [다시 읽기]는 감시자가 선 뒤로 할 일이 없었고(화면이 스스로 따라온다), 픽스처
        // 콤보는 2026-08-16부터 숨어 있기만 했다. ⚠ 엑셀의 `픽스처` 시트와
        // `ChapterFixtureWalker`는 안 지웠다 — 워크북 규격이라 지우면 남의 파일이 깨진다.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        Assert.Null(view.FindControl<Button>("ReloadButton"));
        Assert.Null(view.FindControl<ComboBox>("FixtureCombo"));
        Assert.Null(view.FindControl<TextBlock>("FixtureStopText"));

        // 남아야 하는 이웃 — 지우다 같이 쓸려 나가면 여기서 걸린다.
        Assert.NotNull(view.FindControl<Button>("OpenFolderButton"));
        Assert.NotNull(view.FindControl<Button>("AddEpisodeButton"));
        Assert.NotNull(view.FindControl<Button>("DeleteEpisodeButton"));
    });

    [Fact]
    public void 스탯_안내는_조건과_같은_말투로_어디서_고치는지_말한다() => HeadlessUi.Run(() =>
    {
        // 2026-08-24 소유자 — "편집은 챕터 엑셀의 `스탯` 시트에서 합니다."로. 두 표가 같은
        // 탭에 나란히 서 있으니 "어디서 고치나"의 대답도 같은 모양이어야 눈이 한 번에 읽는다.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        List<string> lines = view.FindControl<StackPanel>("ConditionSection")!
            .Children.OfType<TextBlock>()
            .Select(block => block.Text ?? string.Empty)
            .ToList();

        Assert.Contains(lines, text => text.StartsWith("편집은 챕터 엑셀의 `조건` 시트에서 합니다", StringComparison.Ordinal));
        Assert.Contains(lines, text => text.StartsWith("편집은 챕터 엑셀의 `스탯` 시트에서 합니다", StringComparison.Ordinal));
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
    public void 대사는_선택지_아래에_펼쳐져_있다() => HeadlessUi.Run(() =>
    {
        // 2026-08-16 소유자 — 별도 탭이던 대사를 편집 탭의 선택지 아래로 합쳤다
        // ("에피소드 노드의 정보량이 부족하다"). 접었다 펼 수 있다.
        //
        // 2026-08-24 소유자 "대사 미리보기는, 펼쳐둔 걸 디폴트로 보이도록 해줘" — 시작이
        // 뒤집혔다. ⚠ 삼각형·본문·체크 셋이 <b>함께</b> 맞아야 한다. XAML에서 체크만 켜면
        // 핸들러가 붙기 전이라 삼각형이 ▸로 남는다.
        using var project = new TempProject(SamplePath);
        (ChapterGraphView view, _) = Show(project);

        Assert.Null(view.FindControl<TabItem>("DialogueTab"));       // 탭은 사라졌다
        Assert.Null(view.FindControl<Expander>("DialogueExpander")); // 탭 속 상자도 사라졌다

        var toggle = view.FindControl<ToggleButton>("DialogueToggle")!;
        Assert.True(toggle.IsChecked);              // 펼친 채로 시작한다
        Assert.Equal("▾  대사", toggle.Content);    // 삼각형도 같이 뒤집혔다

        view.SelectEpisode("main05.02");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 고르자마자 그 에피소드의 대사가 서 있다 — 펼치는 손이 필요 없다.
        Assert.True(view.FindControl<SelectableTextBlock>("DialoguePreviewText")!.IsVisible);
        Assert.False(string.IsNullOrEmpty(view.FindControl<TextBlock>("DialoguePreviewHeader")!.Text));

        // 접는 길도 그대로다.
        toggle.IsChecked = false;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(view.FindControl<SelectableTextBlock>("DialoguePreviewText")!.IsVisible);
        Assert.Equal("▸  대사", toggle.Content);
    });

    [Fact]
    public void 대사는_한_덩어리로_흐르고_상자를_겹치지_않는다() => HeadlessUi.Run(() =>
    {
        // 2026-08-16 소유자 재지시 — 줄마다 카드로 가르니 읽기가 더 나빠졌다(철회).
        // 대사는 한 덩어리 글이고 화자는 줄 앞에 붙는다. 겹친 상자(탭 속 Expander·
        // 테두리·안쪽 스크롤)도 걷어냈다 — 접기는 제목 토글 하나가 맡는다.
        using var project = new TempProject(SamplePath);
        WriteOptionsWorkbook(project.EpisodesFolder, "main05.02", "라루의 제안을 듣는다");
        (ChapterGraphView view, _) = Show(project);

        view.SelectEpisode("main05.02");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 줄 카드 패널은 사라졌다.
        Assert.Null(view.FindControl<StackPanel>("DialoguePreviewPanel"));

        var text = view.FindControl<SelectableTextBlock>("DialoguePreviewText")!;
        Assert.Contains("윌로: 첫 줄", text.Text!);

        // 접기 토글 하나가 제목이자 스위치다 — 펼친 채로 시작한다(2026-08-24).
        var toggle = view.FindControl<ToggleButton>("DialogueToggle")!;
        Assert.True(toggle.IsChecked);
        Assert.True(text.IsVisible);

        toggle.IsChecked = false;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(text.IsVisible);
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
        // v12 (2026-08-24) — 포트의 원천은 <b>나가는 간선 전부</b>다. 포트 하나 = 길
        // 하나이고, 누르면 그 간선이 선택된다. 예전에는 문구 없는 길("보이지 않는 기본")만
        // 포트에서 빠져 카드 중앙 직행선으로 갔는데, 그 개념이 폐지됐다.
        using var project = new TempProject(SamplePath);

        // 셋째 길을 하나 더 낸다 — 문구는 간선의 `선택지` 칸에 그대로 적힌다.
        ChapterWorkbookWriter.AddEdge(
            project.ChapterPath, "main05.02", "main05.end", optionLabel: "셋째 길");

        (ChapterGraphView view, _) = Show(project);

        var canvas = view.FindControl<Canvas>("GraphCanvas")!;

        static void Press(Control control) => control.RaiseEvent(new Avalonia.Input.PointerPressedEventArgs(
            control, new Avalonia.Input.Pointer(0, Avalonia.Input.PointerType.Mouse, true),
            control, default, 0,
            new Avalonia.Input.PointerPointProperties(
                Avalonia.Input.RawInputModifiers.LeftMouseButton,
                Avalonia.Input.PointerUpdateKind.LeftButtonPressed),
            Avalonia.Input.KeyModifiers.None));

        // 포트 문구 셋 + 포트 원 셋 (main05.02에서 나가는, 문구 붙은 길의 수).
        List<TextBlock> labels = canvas.Children.OfType<TextBlock>()
            .Where(block => (block.Text ?? "").StartsWith("라루의 제안을 듣는다") ||
                            block.Text == "혼자 문을 연다" || block.Text == "셋째 길")
            .ToList();
        Assert.Equal(3, labels.Count);
        // ⚠ 원의 수는 판 전체의 길 수다(v12 — 모든 길이 포트를 받는다). main05.02의 셋만
        // 세던 옛 값(3)은 문구 없는 길이 포트에서 빠지던 시절의 것이다.
        Assert.Equal(
            6, canvas.Children.OfType<Avalonia.Controls.Shapes.Ellipse>().Count(port => port.Width == 9));

        // 포트 클릭 = 그 길 선택. 포트는 늘 간선이 있다(v9 — 포트가 곧 길이다).
        Press(labels.Single(block => block.Text!.StartsWith("라루의 제안을 듣는다")));
        Assert.True(view.FindControl<StackPanel>("EdgePanel")!.IsVisible);

        Press(labels.Single(block => block.Text == "셋째 길"));
        Assert.True(view.FindControl<StackPanel>("EdgePanel")!.IsVisible);
    });

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <param name="locked">
    /// 엑셀이 그 챕터를 잡고 있는가 (2026-08-24). 진짜 잠긴 파일은 <b>쓸 수도 없어서</b>
    /// 실제 잠금으로는 편집 규칙을 검증할 수 없다 — 그래서 손을 갈아 끼운다.
    /// </param>
    private static (ChapterGraphView View, AuthoringSession Session) Show(
        TempProject project, bool locked = false)
    {
        var session = new AuthoringSession();
        session.Open(project.ManifestPath);

        var view = new ChapterGraphView { LockProbe = _ => locked };
        var window = new Window { Width = 1400, Height = 800, Content = view };
        window.Show();
        view.Attach(session);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 이 창은 테스트가 끝날 때 TempProject가 닫는다 — 안 닫으면 어셈블리가 나눠 쓰는
        // 디스패처에 뷰가 살아남아 다음 테스트를 흔든다.
        project.Ui.Own(view, window);

        return (view, session);
    }

    private sealed class TempProject : IDisposable
    {
        private readonly string _directory;
        /// <summary>이 테스트가 띄운 화면. 폴더를 지우기 <b>전에</b> 닫는다.</summary>
        public OpenChapterViews Ui { get; } = new();

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
            // 순서가 규칙이다 — 감시자를 먼저 닫고 폴더를 지운다. 거꾸로 하면 그 삭제가
            // 사건이 되어 다음 테스트의 RunJobs()에서 깨어난다.
            Ui.CloseAll();

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
        string[] headers = ["인덱스", "유형", "LineId", "조건라벨", "화자", "내용"];

        for (int column = 1; column <= headers.Length; column++)
        {
            sheet.Cell(1, column).SetValue(headers[column - 1]);
        }

        sheet.Cell(2, 1).SetValue(10); sheet.Cell(2, 5).SetValue("윌로"); sheet.Cell(2, 6).SetValue("첫 줄");
        sheet.Cell(3, 1).SetValue(20); sheet.Cell(3, 3).SetValue("CHOICE");

        for (int index = 0; index < options.Length; index++)
        {
            int row = 4 + index;
            sheet.Cell(row, 1).SetValue(30 + index * 10);
            sheet.Cell(row, 3).SetValue("OPTION");
            sheet.Cell(row, 6).SetValue(options[index]);
        }

        workbook.SaveAs(Path.Combine(episodesFolder, episodeId + ".xlsx"));
    }
}
