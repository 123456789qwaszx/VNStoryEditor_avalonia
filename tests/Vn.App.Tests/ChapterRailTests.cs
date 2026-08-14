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
    public void 아직_동기화_전인_에피소드로는_선을_긋지_않는다() => HeadlessUi.Run(() =>
    {
        // 없는 노드로 선을 그으면 거짓말이다 — 동기화가 노드를 세우면 따라 나타난다.
        (GraphEditorView graph, AuthoringSession session, string fileId) = ShowBoard("ch01");

        AddExcelNode(session, fileId, "EP00"); // EP01은 아직 없다

        graph.SupplyChapters([Chapter("ch01",
            episodes: ["EP00", "EP01"],
            edges: [("EP00", "EP01", "라루를 믿는다", null)])]);
        graph.Rebuild();

        var canvas = graph.FindControl<Canvas>("GraphCanvas")!;
        Assert.Empty(canvas.Children.OfType<Line>());
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
                ("EP00", "EP01", "라루를 믿는다", "trust +1")
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

        // ② 간선 짝 옵션은 간선 칩, 안 이은 옵션은 종료 스텁.
        List<string> chips = canvas.Children.OfType<TextBlock>()
            .Select(block => block.Text ?? string.Empty)
            .Where(text => text.StartsWith("●", StringComparison.Ordinal))
            .ToList();
        Assert.Contains("● 라루를 믿는다", chips);
        Assert.Contains("● 의심한다", chips);
        Assert.Contains(canvas.Children.OfType<TextBlock>(),
            block => block.Text == "⏹ 종료");

        // 순서 — 선택지(대본 OPTION 순서)가 위, 진행이 아래 (소유자 보고: 반대면 헷갈린다).
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

        // ③ 배선 — 스텁 옵션에 자유 씬을 달면(칩이 부르는 그 SetExitTarget) 철도가 씬을 경유한다.
        ExitPort option = NodeConnections.PortsOf(excel, session.Project, session.Definition)
            .Single(port => port.ChoiceText == "의심한다");
        session.Editor.SetExitTarget(excel.Id, ExitPortKind.Branch, option.BranchOpenLineId, free.Id);

        graph.Rebuild();

        // 씬 카드로 진입하는 선 — 같은 높이면 왼쪽(x=카드-8), 아니면 카드 가운데(x=700+105)로 꺾인다.
        Assert.Contains(canvas.Children.OfType<Avalonia.Controls.Shapes.Line>(),
            line => Math.Abs(line.EndPoint.X - (700 - 8)) < 0.5 ||
                    Math.Abs(line.EndPoint.X - (700 + 105)) < 0.5);

        Directory.Delete(episodes, recursive: true);
    });

    private static void WriteEpisode(string path)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        ClosedXML.Excel.IXLWorksheet sheet = workbook.AddWorksheet("대본");
        string[] headers = ["인덱스", "LineId", "유형", "태그", "조건라벨", "IN", "OUT", "화자", "내용"];

        for (int column = 1; column <= headers.Length; column++)
        {
            sheet.Cell(1, column).SetValue(headers[column - 1]);
        }

        sheet.Cell(2, 1).SetValue(10); sheet.Cell(2, 8).SetValue("윌로"); sheet.Cell(2, 9).SetValue("첫 줄");
        sheet.Cell(3, 1).SetValue(20); sheet.Cell(3, 3).SetValue("CHOICE");
        sheet.Cell(4, 1).SetValue(30); sheet.Cell(4, 3).SetValue("OPTION"); sheet.Cell(4, 9).SetValue("라루를 믿는다");
        sheet.Cell(5, 1).SetValue(40); sheet.Cell(5, 3).SetValue("OPTION"); sheet.Cell(5, 9).SetValue("의심한다");
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
                id, id, "01", "Main", id, index * 100, 0, null, null, null, null, index + 2))
            .ToList();

        List<ChapterEdge> edgeList = edges
            .Select((edge, index) => new ChapterEdge(
                edge.From, edge.To, edge.Label, null, false, null, index + 2)
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
