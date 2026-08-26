using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Flow;
using Vn.Authoring.Graph;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.App.Tests;

/// <summary>
/// T1 — 시나리오 그래프의 철도 배선 (소유자 그림). 챕터 간선이 챕터 판에 미러되고,
/// 선택지 칩은 문구만 보인다(T-D4 — 수치는 챕터 그래프의 책임). 선택은 항상 에피소드 끝이므로
/// 분기점은 출발 카드당 하나다.
/// </summary>
public sealed class ChapterRailTests
{
    [Fact]
    public void 챕터_간선이_철도_배선으로_미러되고_칩은_문구만_보인다() => HeadlessUi.Run(() =>
    {
        (GraphEditorView graph, AuthoringSession session, string fileId) = ShowBoard("ch01");

        // 엑셀노드 셋 — 챕터의 에피소드와 이름·표식이 짝이다.
        AddExcelNode(session, fileId, "EP00");
        AddExcelNode(session, fileId, "EP01");
        AddExcelNode(session, fileId, "EP02");

        graph.SupplyChapters([Chapter("ch01",
            episodes: ["EP00", "EP01", "EP02"],
            edges:
            [
                ("EP00", "EP01", "라루를 믿는다", "trust +1"),
                ("EP00", "EP02", "의심한다", "suspicion +1")
            ])]);

        graph.Rebuild();

        var canvas = graph.FindControl<Canvas>("GraphCanvas")!;

        // 철도 선이 섰다 — 줄기·가지·진입 (간선 2개면 선이 여럿이다).
        List<Line> rails = canvas.Children.OfType<Line>().ToList();
        Assert.True(rails.Count >= 4, $"철도 선이 {rails.Count}개뿐입니다");

        // 칩 — 문구만. 스탯변화·조건 문자열은 기본 화면에 없다(T-D4).
        List<string> chips = canvas.Children.OfType<TextBlock>()
            .Select(block => block.Text ?? string.Empty)
            .Where(text => text.StartsWith("●", StringComparison.Ordinal))
            .ToList();

        Assert.Contains("● 라루를 믿는다", chips);
        Assert.Contains("● 의심한다", chips);
        Assert.DoesNotContain(chips, chip => chip.Contains("trust") || chip.Contains("suspicion"));

        // 같은 출발의 가지들은 같은 분기점 X(줄기)를 공유한다 — 단일 분기점.
        List<double> chipXs = canvas.Children.OfType<TextBlock>()
            .Where(block => (block.Text ?? "").StartsWith("●", StringComparison.Ordinal))
            .Select(Canvas.GetLeft)
            .Distinct()
            .ToList();

        Assert.Single(chipXs);

        // 도착 포트 — 들어오는 가지가 있는 카드(EP01·EP02) 앞에 접점이 서고,
        // 가지들은 카드에 직접 붙지 않고 그 접점 X로 모인다 (소유자 보고로 고정).
        List<Ellipse> ports = canvas.Children.OfType<Ellipse>()
            .Where(port => port.Width == 10)
            .ToList();
        Assert.Equal(2, ports.Count);

        foreach (Ellipse port in ports)
        {
            double junctionX = Canvas.GetLeft(port) + 5;
            double junctionY = Canvas.GetTop(port) + 5;
            Assert.Contains(canvas.Children.OfType<Line>(), line =>
                Math.Abs(line.EndPoint.X - junctionX) < 0.5 &&
                (Math.Abs(line.EndPoint.Y - junctionY) < 0.5 || Math.Abs(line.StartPoint.X - junctionX) < 0.5));
        }
    });

    [Fact]
    public void 챕터가_아닌_판에는_철도가_생기지_않는다() => HeadlessUi.Run(() =>
    {
        (GraphEditorView graph, AuthoringSession session, string fileId) = ShowBoard("일반판");

        session.Editor.AddDialogueNode(fileId, name: "자유A");
        session.Editor.AddDialogueNode(fileId, name: "자유B");

        graph.SupplyChapters([Chapter("ch01", episodes: ["EP00"], edges: [])]);
        graph.Rebuild();

        var canvas = graph.FindControl<Canvas>("GraphCanvas")!;
        Assert.Empty(canvas.Children.OfType<Line>());
        Assert.DoesNotContain(canvas.Children.OfType<TextBlock>(),
            block => (block.Text ?? "").StartsWith("●", StringComparison.Ordinal));
    });

    [Fact]
    public void 동기화_전인_도착은_스텁으로_보인다() => HeadlessUi.Run(() =>
    {
        // 없는 노드로 선을 그으면 거짓말이지만, 가지를 통째로 숨기면 선택지가 사라져
        // 고장으로 보인다(소유자 보고 2026-08-15). 가지는 "동기화 전" 스텁에서 멈춘다.
        (GraphEditorView graph, AuthoringSession session, string fileId) = ShowBoard("ch01");

        AddExcelNode(session, fileId, "EP00"); // EP01은 아직 없다

        graph.SupplyChapters([Chapter("ch01",
            episodes: ["EP00", "EP01"],
            edges: [("EP00", "EP01", "라루를 믿는다", null)])]);
        graph.Rebuild();

        var canvas = graph.FindControl<Canvas>("GraphCanvas")!;
        Assert.Contains(canvas.Children.OfType<TextBlock>(), block =>
            (block.Text ?? "").Contains("EP01", StringComparison.Ordinal) &&
            (block.Text ?? "").Contains("동기화 전", StringComparison.Ordinal));
    });

    [Fact]
    public void 엑셀노드와_자유_씬은_한눈에_갈린다() => HeadlessUi.Run(() =>
    {
        // 소유자 보고 (2026-08-15) — "대본노드와 엑셀노드가 똑같이 생겨서 구분이 어렵다."
        // 종별 시각 언어: 엑셀노드 = 각진 미색 서류(📄), 자유 씬 = 둥근 흰 원고(✎).
        (GraphEditorView graph, AuthoringSession session, string fileId) = ShowBoard("ch01");

        DialogueNode excel = AddExcelNode(session, fileId, "EP00");
        DialogueNode free = session.Editor.AddDialogueNode(fileId, name: "자유씬");
        graph.Rebuild();

        var canvas = graph.FindControl<Canvas>("GraphCanvas")!;
        Border excelCard = canvas.Children.OfType<Border>()
            .Single(border => Equals(border.Tag, excel.Id));
        Border freeCard = canvas.Children.OfType<Border>()
            .Single(border => Equals(border.Tag, free.Id));

        Assert.NotEqual(freeCard.CornerRadius, excelCard.CornerRadius);
        Assert.NotEqual(
            ((Avalonia.Media.SolidColorBrush)freeCard.Background!).Color,
            ((Avalonia.Media.SolidColorBrush)excelCard.Background!).Color);
    });

    [Fact]
    public void 챕터에서_지운_에피소드_노드에는_레일이_없다() => HeadlessUi.Run(() =>
    {
        // 챕터 밖의 노드에 진행·종료를 그리는 것은 거짓말이다 — 동기화 보고가 알릴 일이다.
        (GraphEditorView graph, AuthoringSession session, string fileId) = ShowBoard("ch01");

        AddExcelNode(session, fileId, "EP99"); // 챕터에는 없는 에피소드

        graph.SupplyChapters([Chapter("ch01",
            episodes: ["EP00"],
            edges: [])]);
        graph.Rebuild();

        var canvas = graph.FindControl<Canvas>("GraphCanvas")!;
        Assert.Empty(canvas.Children.OfType<Line>());
        Assert.DoesNotContain(canvas.Children.OfType<TextBlock>(),
            block => (block.Text ?? "").Contains('⏹'));
    });

    [Fact]
    public void 옵션_포트가_칩으로_이사하고_스텁과_체인이_선다() => HeadlessUi.Run(() =>
    {
        // T2 — 실제 동기화로 옵션 줄이 있는 엑셀노드를 만든다: CHOICE + 옵션 둘.
        (GraphEditorView graph, AuthoringSession session, string fileId) = ShowBoard("ch01");

        string episodes = Path.Combine(Path.GetTempPath(), "vn-rail-ep", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(episodes);
        string workbook = Path.Combine(episodes, "EP00.xlsx");
        WriteEpisode(workbook);

        // 진행 간선을 시트에서 일부러 먼저 둔다 — 그래도 화면은 선택지가 위여야 한다.
        ChapterEntry entry = Chapter("ch01",
            episodes: ["EP00", "EP01"],
            edges:
            [
                ("EP00", "EP01", null, null),
                ("EP00", "EP01", "라루를 믿는다", "trust +1"),
                ("EP00", "EP01", "의심한다", null)
            ]);

        EpisodeSyncReport report = EpisodeSyncService.Sync(
            session.Editor, session.Definition, fileId, workbook, entry.Model);
        Assert.True(report.Applied, string.Join(" / ", report.Problems));

        DialogueNode ep01 = AddExcelNode(session, fileId, "EP01");
        ep01.Layout.X = 1200;
        ep01.Layout.Y = 60;

        DialogueNode free = session.Editor.AddDialogueNode(fileId, name: "자유씬A");
        free.Layout.X = 700;
        free.Layout.Y = 420;

        graph.SupplyChapters([entry]);
        graph.Rebuild();

        var canvas = graph.FindControl<Canvas>("GraphCanvas")!;

        // ① 엑셀노드 카드에서 옵션·기본 포트가 사라졌다 — 칩으로 이사(IF 갈래만 남는다).
        DialogueNode excel = session.Project.EnumerateNodes().OfType<DialogueNode>()
            .Single(node => node.ExcelEpisodeId == "EP00");
        ExpandedNodeProjection projected = GraphProjectionBuilder
            .Build(session.Project, session.Project.Files.Select(file => file.Id)
                .ToHashSet(StringComparer.Ordinal))
            .Items.OfType<ExpandedNodeProjection>()
            .Single(item => item.NodeId == excel.Id);

        Assert.DoesNotContain(projected.OutputPorts,
            port => port.Kind == GraphOutputPortKind.ExecutionDefault);
        Assert.DoesNotContain(projected.OutputPorts, port => port.ExecutionPort is { IsChoice: true });

        // ② 칩의 주인은 챕터 `간선` 시트다. (구판 OPTION 스텁은 v10에서 사라졌다 —
        //    대본이 선택지를 선언하지 않으므로 "안 이은 옵션"이라는 상태가 없다.)
        List<string> chips = canvas.Children.OfType<TextBlock>()
            .Select(block => block.Text ?? string.Empty)
            .Where(text => text.StartsWith("●", StringComparison.Ordinal))
            .ToList();
        Assert.Contains("● 라루를 믿는다", chips);
        Assert.Contains("● 의심한다", chips);

        // 순서 — 선택지(간선 시트의 행 순서)가 위, 진행이 아래 (소유자 보고: 반대면 헷갈린다).
        // EP01(엔딩)의 진행 스텁도 있으므로 EP00 분기점(같은 X)의 칩들만 본다.
        double trunkLeft = canvas.Children.OfType<TextBlock>()
            .Where(block => block.Text == "● 라루를 믿는다")
            .Select(Canvas.GetLeft)
            .Single();

        double TopOf(string text) => canvas.Children.OfType<TextBlock>()
            .Where(block => block.Text == text && Math.Abs(Canvas.GetLeft(block) - trunkLeft) < 0.5)
            .Select(Canvas.GetTop)
            .Single();

        Assert.True(TopOf("● 라루를 믿는다") < TopOf("● 의심한다"));
        Assert.True(TopOf("● 의심한다") < TopOf("○ 진행"));

        // ③ 배선 — 칩에 자유 씬을 달면(칩이 부르는 그 SetExitTarget) 철도가 씬을 경유한다.
        // v10에서 선택지 출구의 열쇠는 <b>문구</b>다 — 대본 줄이 아니라(대본은 선택지를
        // 선언하지 않는다). 그래서 대본을 고쳐도 이 배선이 살아 있다.
        session.Editor.SetExitTarget(excel.Id, ExitPortKind.Choice, "의심한다", free.Id);

        graph.Rebuild();

        // 씬 카드로 진입하는 선 — 같은 높이면 왼쪽(x=카드-8), 아니면 카드 가운데(x=700+105)로 꺾인다.
        Assert.Contains(canvas.Children.OfType<Avalonia.Controls.Shapes.Line>(),
            line => Math.Abs(line.EndPoint.X - (700 - 8)) < 0.5 ||
                    Math.Abs(line.EndPoint.X - (700 + 105)) < 0.5);

        // ④ 커스텀 노드에는 출구가 없다 (2026-08-21 소유자) — 커스텀→커스텀 배선은
        // 조건 갈래(detour)의 몫이라 기본 출구 쓰기는 조용히 거절되고, 씬은 곧장
        // (진행)으로 합류한다.
        DialogueNode sceneC = session.Editor.AddDialogueNode(fileId, name: "다음씬C");
        sceneC.Layout.X = 960;
        sceneC.Layout.Y = 300;
        session.Editor.SetExitTarget(free.Id, ExitPortKind.Default, null, sceneC.Id);
        Assert.Null(free.DefaultExitTargetNodeId);

        graph.Rebuild();

        // sceneC로 들어가는 선이 없다 — 배선되지 않았다.
        Assert.DoesNotContain(canvas.Children.OfType<Avalonia.Controls.Shapes.Line>(),
            line => Math.Abs(line.EndPoint.X - (960 - 8)) < 0.5 ||
                    Math.Abs(line.EndPoint.X - (960 + 105)) < 0.5);

        // 첫 씬(자유씬A)에서 합류선이 레인 끝으로 나간다 — 출구 없음 = (진행).
        Assert.Contains(canvas.Children.OfType<Avalonia.Controls.Shapes.Line>(),
            line => Math.Abs(line.StartPoint.X - (700 + 210 + 6)) < 0.5);

        // ⑤ (진행) 합류 (소유자 제안) — 척추 밖으로 확장한 웹의 노드 B: A의 갈래 출구로
        // 이어지고 기본 출구는 비어 있다(= 진행). 그 출력도 레인 끝으로 이어져 보인다.
        // (가짜 LineId 배선이라 이 뒤로는 편집기를 부르지 않는다 — 검증기가 막는다.)
        DialogueNode webB = session.Editor.AddDialogueNode(fileId, name: "곁가지B");
        webB.Layout.X = 950;
        webB.Layout.Y = 560;
        free.BranchExits["ln_fake_option"] = webB.Id;

        graph.Rebuild();

        double webOutX = 950 + 210 + 6; // 카드 오른쪽 + 6
        Assert.Contains(canvas.Children.OfType<Avalonia.Controls.Shapes.Line>(),
            line => Math.Abs(line.StartPoint.X - webOutX) < 0.5);

        Directory.Delete(episodes, recursive: true);
    });

    [Fact]
    public void 대본에_OPTION이_없어도_간선마다_칩이_서고_자유_씬을_단다() => HeadlessUi.Run(() =>
    {
        // v9 (2026-08-17 소유자 보고) — "'이 문구의 Option이 대본에 없습니다' 라면서 레거시의
        // 그런 느낌이야." 선택지의 주인이 챕터로 온 뒤 대본에 OPTION이 없는 것이 정상인데,
        // 철도는 여전히 대본 OPTION을 기준으로 삼아 모든 간선을 유령으로 그리고 있었다.
        (GraphEditorView graph, AuthoringSession session, string fileId) = ShowBoard("ch01");

        DialogueNode ep00 = AddExcelNode(session, fileId, "EP00"); // 대본에 OPTION 줄이 없다
        AddExcelNode(session, fileId, "EP01");
        AddExcelNode(session, fileId, "EP02");

        DialogueNode free = session.Editor.AddDialogueNode(fileId, name: "자유씬A");
        free.Layout.X = 700;
        free.Layout.Y = 420;

        graph.SupplyChapters([Chapter("ch01",
            episodes: ["EP00", "EP01", "EP02"],
            edges:
            [
                ("EP00", "EP01", "라루를 믿는다", null),
                ("EP00", "EP02", "의심한다", null)
            ])]);
        graph.Rebuild();

        var canvas = graph.FindControl<Canvas>("GraphCanvas")!;

        // 간선 둘 = 칩 둘. 유령(흐린 칩)도, 레거시 경고도 없다.
        List<TextBlock> chips = canvas.Children.OfType<TextBlock>()
            .Where(block => (block.Text ?? string.Empty).StartsWith("●", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(["● 라루를 믿는다", "● 의심한다"], chips.Select(block => block.Text));
        Assert.All(chips, chip => Assert.Equal(1, chip.Opacity));

        // 문구를 열쇠로 자유 씬을 단다 — 대본의 줄이 아니라 챕터의 문구가 자리다.
        session.Editor.SetExitTarget(ep00.Id, ExitPortKind.Choice, "라루를 믿는다", free.Id);

        Assert.Equal(free.Id, ep00.ChoiceExits["라루를 믿는다"]);

        graph.Rebuild();

        // 철도가 그 씬 카드로 진입한다 — 같은 높이면 왼쪽(x=카드-8), 아니면 가운데(700+105).
        Assert.Contains(canvas.Children.OfType<Line>(),
            line => Math.Abs(line.EndPoint.X - (700 - 8)) < 0.5 ||
                    Math.Abs(line.EndPoint.X - (700 + 105)) < 0.5);

        // 대본을 고쳐도 이 배선은 살아 있다 — 문구의 주인은 챕터라 줄 청소에 쓸리지 않는다.
        session.Editor.AddDialogueNode(fileId, name: "무관한노드");
        Assert.Equal(free.Id, ep00.ChoiceExits["라루를 믿는다"]);

        // 저장·재적재를 건너도 남는다 — 판 파일에 제 자리가 있다.
        StoryFile saved = StoryFileJson.Read(
            StoryFileJson.Write(session.Project.Files.Single(file => file.Id == fileId)));
        DialogueNode reloaded = saved.Nodes.OfType<DialogueNode>()
            .Single(node => node.ExcelEpisodeId == "EP00");
        Assert.Equal(free.Id, reloaded.ChoiceExits["라루를 믿는다"]);
    });

    private static void WriteEpisode(string path)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        ClosedXML.Excel.IXLWorksheet sheet = workbook.AddWorksheet("대본");
        string[] headers = ["유형", "조건라벨", "인덱스", "LineId", "화자", "내용"];

        for (int column = 1; column <= headers.Length; column++)
        {
            sheet.Cell(1, column).SetValue(headers[column - 1]);
        }

        // v14 자리 — 인덱스는 C열(3)이다.
        sheet.Cell(2, 3).SetValue(10); sheet.Cell(2, 5).SetValue("윌로"); sheet.Cell(2, 6).SetValue("첫 줄");
        // v10 — 대본은 선택지를 선언하지 않는다. 칩의 주인은 챕터 `간선` 시트다.
        sheet.Cell(3, 3).SetValue(20); sheet.Cell(3, 5).SetValue("라루"); sheet.Cell(3, 6).SetValue("둘째 줄");
        workbook.SaveAs(path);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static DialogueNode AddExcelNode(AuthoringSession session, string fileId, string episodeId)
    {
        DialogueNode node = session.Editor.AddDialogueNode(fileId, name: episodeId);
        node.ExcelEpisodeId = episodeId;
        return node;
    }

    private static ChapterEntry Chapter(
        string chapterId,
        IReadOnlyList<string> episodes,
        IReadOnlyList<(string From, string To, string? Label, string? Stats)> edges)
    {
        List<ChapterEpisode> episodeList = episodes
            .Select((id, index) => new ChapterEpisode(
                id, id, "01", id, index * 100, 0, null, index + 2))
            .ToList();

        List<ChapterEdge> edgeList = edges
            .Select((edge, index) => new ChapterEdge(
                edge.From, edge.To, edge.Label, null, null, index + 2)
            {
                StatChanges = edge.Stats is null
                    ? []
                    : [new StatDelta(edge.Stats.Split(' ')[0], int.Parse(edge.Stats.Split('+')[1]))]
            })
            .ToList();

        var model = new ChapterGraphModel(
            chapterId, chapterId + ".xlsx", episodeList, edgeList,
            Array.Empty<ChapterCondition>(), Array.Empty<ChapterStat>(),
            Array.Empty<ChapterFixture>(), Array.Empty<ChapterDiagnostic>());

        return new ChapterEntry(chapterId, chapterId + ".xlsx", model, null);
    }

    private static (GraphEditorView Graph, AuthoringSession Session, string FileId) ShowBoard(string name)
    {
        var session = new AuthoringSession();
        string directory = Path.Combine(
            Path.GetTempPath(), "vn-rail", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        string manifest = Path.Combine(directory, "project" + ProjectManifestJson.FileExtension);
        ProjectStore.Save(manifest, new StoryProject { Title = "철도 검증" });
        session.Open(manifest);

        string fileId = session.EnsureChapterBoard(name);

        var graph = new GraphEditorView();
        var window = new Window { Width = 1400, Height = 900, Content = graph };
        window.Show();
        graph.Attach(session);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return (graph, session, fileId);
    }
}
