using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = System.IO.Path;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
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

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static void AddExcelNode(AuthoringSession session, string fileId, string episodeId)
    {
        DialogueNode node = session.Editor.AddDialogueNode(fileId, name: episodeId);
        node.ExcelEpisodeId = episodeId;
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
