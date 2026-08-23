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
        ChapterWorkbookWriter.AddEdge(path, "ep1", "ep2", optionLabel: "계속");
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
        ChapterWorkbookWriter.AddEdge(path, "ep2", "ep3", optionLabel: "계속", conditionLabel: "신뢰높음");
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
        ChapterWorkbookWriter.AddEdge(path, "ep2", "ep3", optionLabel: "계속", conditionLabel: "신뢰높음");
        ChapterWorkbookWriter.AddCondition(path, "신뢰높음", "trust >= 3");
        ChapterWorkbookWriter.UpdateEdge(path, "ep1", "ep2", statChanges: "trust +3");

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);
        ChapterReachabilityResult result = ChapterReachabilityProver.Prove(model);

        Assert.Contains("ep3", result.ReachableEpisodeIds);
    }

    [Fact]
    public void 길_하나가_선택지_하나이고_문구는_챕터의_사전에서_온다()
    {
        // v9 (2026-08-17 소유자) — "선택지는 인덱스를 가져오는 게 아니라 그냥 깡으로 대사만."
        // 간선 D열에 문구가 그대로 들어가고, 신원은 (출발, 도착, 문구)다.
        string path = BuildChapter(); // ep1→ep2, 문구 "계속" (v12 — 문구 없는 길은 폐지)

        ChapterGraphModel first = ChapterWorkbookReader.Read(path);
        Assert.Equal("계속", Assert.Single(first.Edges).OptionLabel);

        // 같은 도착으로 가는 길을 문구만 달리해 둘 더 낸다 — 흔한 패턴이고 허용된다.
        Assert.True(ChapterWorkbookWriter.AddEdge(path, "ep1", "ep2", optionLabel: "믿는다").Written);
        Assert.True(ChapterWorkbookWriter.AddEdge(path, "ep1", "ep2", optionLabel: "무시한다").Written);

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);
        Assert.Equal(3, model.Edges.Count);
        Assert.False(model.HasErrors);

        // 쓴 문구는 사전에도 올라 다음번 드롭다운 재료가 된다.
        Assert.Equal(["계속", "믿는다", "무시한다"], model.ChoiceOptions.Select(option => option.Text));

        // 셋(출발·도착·문구)이 다 같은 길을 두 번 낼 수는 없다.
        Assert.False(ChapterWorkbookWriter.AddEdge(path, "ep1", "ep2", optionLabel: "믿는다").Written);

        // 문구는 어느 에피소드에서든 다시 쓴다 — 사전은 챕터 전체의 것이다.
        Assert.True(ChapterWorkbookWriter.AddEdge(path, "ep2", "ep1", optionLabel: "믿는다").Written);
        // v12 — 툴이 놓는 첫 길에 "계속"이 들어가므로 사전 낱말이 셋이다.
        Assert.Equal(3, ChapterWorkbookReader.Read(path).ChoiceOptions.Count);

        // 길을 지워도 문구는 사전에 남는다(어휘집이지 배선이 아니다).
        Assert.True(ChapterWorkbookWriter.RemoveEdge(path, "ep1", "ep2", "무시한다").Written);
        ChapterGraphModel after = ChapterWorkbookReader.Read(path);
        Assert.Equal(3, after.Edges.Count);
        Assert.Contains(after.ChoiceOptions, option => option.Text == "무시한다");
    }

    [Fact]
    public void 선택지_수정은_문구와_도착을_한_저장으로_옮긴다()
    {
        string path = BuildChapter();
        ChapterWorkbookWriter.AddEpisode(path, "ep3", title: "", 2, 0);
        ChapterWorkbookWriter.AddEdge(path, "ep1", "ep2", optionLabel: "믿는다");

        Assert.True(ChapterWorkbookWriter
            .SetEdgeRoute(path, "ep1", "ep2", "믿는다", "ep3", "의심한다").Written);

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);
        ChapterEdge moved = Assert.Single(model.Edges, edge => edge.ToEpisodeId == "ep3");
        Assert.Equal("ep3", moved.ToEpisodeId);
        Assert.Equal("의심한다", moved.OptionLabel);
    }

    [Fact]
    public void 같은_에피소드에서_같은_문구가_갈리면_경고한다()
    {
        // 플레이어에게는 같은 버튼 둘이라 어느 쪽인지 고를 수 없다.
        string path = BuildChapter();
        ChapterWorkbookWriter.AddEpisode(path, "ep3", title: "", 2, 0);
        ChapterWorkbookWriter.AddEdge(path, "ep1", "ep2", optionLabel: "간다");
        ChapterWorkbookWriter.AddEdge(path, "ep1", "ep3", optionLabel: "간다");

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);

        Assert.False(model.HasErrors); // 막지는 않는다 — 사람이 판단할 일이다
        Assert.Contains(model.Diagnostics, item =>
            item.Severity == ChapterDiagnosticSeverity.Warning &&
            item.Message.Contains("여러 갈래"));
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
