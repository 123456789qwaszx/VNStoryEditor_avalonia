using System.Text.Json;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 간선 `스탯변화` (2026-08-14 소유자 결정) — 스탯이 변하는 유일한 자리는 에피소드 사이,
/// 간선을 타는 순간의 1회 커밋이다. 쓰기→읽기→픽스처 걷기→내보내기가 한 값을 본다.
/// </summary>
public sealed class ChapterEdgeStatTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-edge-stat", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string BuildChapter()
    {
        ChapterWorkbookWriter.EnsureChapterWorkbook(
            _directory, "ch01", [("trust", "신뢰")]);
        string path = Path.Combine(_directory, "ch01.xlsx");

        ChapterWorkbookWriter.AddEpisode(path, "ep1", title: "", 0, 0);
        ChapterWorkbookWriter.AddEpisode(path, "ep2", title: "", 1, 0);
        ChapterWorkbookWriter.AddEdge(path, "ep1", "ep2");
        return path;
    }

    [Fact]
    public void 간선_스탯변화가_쓰기와_읽기를_왕복한다()
    {
        string path = BuildChapter();

        Assert.True(ChapterWorkbookWriter.UpdateEdge(
            path, "ep1", "ep2", statChanges: "trust +2").Written);

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);
        ChapterEdge edge = Assert.Single(model.Edges);

        StatDelta delta = Assert.Single(edge.StatChanges);
        Assert.Equal("trust", delta.Key);
        Assert.Equal(2, delta.Amount);
    }

    [Fact]
    public void 미등록_스탯키는_간선에서_바로_오류다()
    {
        string path = BuildChapter();
        ChapterWorkbookWriter.UpdateEdge(path, "ep1", "ep2", statChanges: "karma +1");

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);

        Assert.Contains(model.Diagnostics, item =>
            item.Severity == ChapterDiagnosticSeverity.Error &&
            item.Code == ChapterDiagnosticCode.StatKeyUnknown &&
            item.Message.Contains("karma"));
    }

    [Fact]
    public void 픽스처_걷기가_간선_증감을_커밋하며_걷는다()
    {
        // ep1 →(+3)→ ep2 →(신뢰높음 관문)→ ep3. 시작 trust 0이라도 첫 간선에서 +3이
        // 커밋되므로 ep2에서 관문이 열려 있어야 한다 — 걷기가 증감을 반영한다는 증거.
        string path = BuildChapter();
        ChapterWorkbookWriter.AddEpisode(path, "ep3", title: "", 2, 0);
        ChapterWorkbookWriter.AddEdge(path, "ep2", "ep3", conditionLabel: "신뢰높음");
        ChapterWorkbookWriter.AddCondition(path, "신뢰높음", "trust >= 3");
        ChapterWorkbookWriter.UpdateEdge(path, "ep1", "ep2", statChanges: "trust +3");

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);
        var fixture = new ChapterFixture(
            "기본", IsActive: true,
            new Dictionary<string, int>(StringComparer.Ordinal),
            Array.Empty<ChapterFixtureChoice>(), SourceRow: 2);

        FixtureWalkResult walk = ChapterFixtureWalker.Walk(model, fixture);

        Assert.Equal(["ep1", "ep2", "ep3"], walk.EpisodeIds);
        Assert.Null(walk.StoppedBecause);
    }

    [Fact]
    public void 도달성_증명이_간선_증감으로_관문을_연다()
    {
        // 같은 챕터를 증명기로 — 간선 +3이 없으면 ep3는 도달 불가였을 구조다.
        string path = BuildChapter();
        ChapterWorkbookWriter.AddEpisode(path, "ep3", title: "", 2, 0);
        ChapterWorkbookWriter.AddEdge(path, "ep2", "ep3", conditionLabel: "신뢰높음");
        ChapterWorkbookWriter.AddCondition(path, "신뢰높음", "trust >= 3");
        ChapterWorkbookWriter.UpdateEdge(path, "ep1", "ep2", statChanges: "trust +3");

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);
        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(model);

        Assert.Contains("ep3", result.ReachableEpisodeIds);
    }

    [Fact]
    public void 간선은_선택지_칸과_1대1이고_칸_수는_에피소드가_정한다()
    {
        // v7 (2026-08-16 소유자) — 에피소드가 선택지수를 지정하고 그 수만큼 칸이 선다.
        // 간선은 칸 하나와 1:1로 짝하며(인덱스), 잇는 순간 길이 된다.
        string path = BuildChapter(); // ep1→ep2 간선 + 칸 하나(인덱스 10)가 이미 있다

        ChapterGraphModel first = ChapterWorkbookReader.Read(path);
        ChapterEdge wired = Assert.Single(first.Edges);
        ChapterChoiceOption slot = Assert.Single(first.ChoiceOptionsFor("ep1"));
        Assert.Equal(slot.Index, wired.ChoiceIndex);
        Assert.Equal(1, first.FindEpisode("ep1")!.ChoiceCount);

        // 칸을 더하면 에피소드의 선택지수가 따라 오른다 — 간선은 아직 없다(잇는 순간 생긴다).
        Assert.True(ChapterWorkbookWriter.AddChoiceSlotToEpisode(path, "ep1").Written);

        ChapterGraphModel added = ChapterWorkbookReader.Read(path);
        Assert.Equal(2, added.FindEpisode("ep1")!.ChoiceCount);
        Assert.Equal(2, added.ChoiceOptionsFor("ep1").Count());
        Assert.Single(added.Edges);

        // 그 빈 칸에 도착을 이으면 같은 도착이라도 다른 길이 된다(인덱스가 다르다).
        string freeIndex = added.ChoiceOptionsFor("ep1")
            .Single(candidate => candidate.Index != wired.ChoiceIndex).Index;
        Assert.True(ChapterWorkbookWriter.AddEdge(path, "ep1", "ep2", choiceIndex: freeIndex).Written);

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);
        Assert.Equal(2, model.Edges.Count);
        Assert.False(model.HasErrors);

        // 같은 칸을 두 번 잇지는 못한다.
        Assert.False(ChapterWorkbookWriter.AddEdge(path, "ep1", "ep2", choiceIndex: freeIndex).Written);

        // 칸을 지우면 그 칸에 이어진 간선도 함께 걷힌다.
        Assert.True(ChapterWorkbookWriter.RemoveChoiceSlot(path, "ep1", freeIndex).Written);
        ChapterGraphModel after = ChapterWorkbookReader.Read(path);
        Assert.Single(after.Edges);
        Assert.Single(after.ChoiceOptionsFor("ep1"));
    }

    [Fact]
    public void 내보내기에_간선_스탯변화가_실린다()
    {
        string path = BuildChapter();
        ChapterWorkbookWriter.UpdateEdge(path, "ep1", "ep2", statChanges: "trust +2");

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);
        ChapterExportResult result = ChapterProgressionExporter.Export(model, episodesFolder: null);

        Assert.False(result.Refused,
            string.Join(" / ", result.Validation.All.Select(item => item.Message)));

        using JsonDocument document = JsonDocument.Parse(result.Json!);
        JsonElement option = document.RootElement
            .GetProperty("Nodes")[0].GetProperty("NextOptions")[0];

        JsonElement change = option.GetProperty("StatChanges")[0];
        Assert.Equal("trust", change.GetProperty("Key").GetString());
        Assert.Equal(2, change.GetProperty("Amount").GetInt32());
    }
}
