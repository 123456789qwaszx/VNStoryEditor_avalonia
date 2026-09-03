using System.Globalization;
using System.Text;
using System.Text.Json;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

public sealed class ChapterDeterministicExportTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "vn-deterministic-export", Guid.NewGuid().ToString("N"));
    private string ProjectPath => Path.Combine(_root, "story.vnproject.json");

    public ChapterDeterministicExportTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void 같은_객체를_반복_직렬화하면_바이트와_checksum이_같다()
    {
        ChapterGraphModel chapter = Chapter(orderReversedInMemory: true);

        ChapterExportResult first = ChapterProgressionExporter.Export(chapter, null);
        ChapterExportResult second = ChapterProgressionExporter.Export(chapter, null);

        Assert.Equal(ChapterExportBytes.Encode(first.Json!), ChapterExportBytes.Encode(second.Json!));
        Assert.Equal(first.Checksum, second.Checksum);
        Assert.Equal(64, first.Checksum!.Length);
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            ChapterExportBytes.Sha256("abc"));
    }

    [Fact]
    public void 워크북을_다시_열어_export해도_바이트가_같다()
    {
        ChapterWorkbookWriter.EnsureChapterWorkbook(_root, "reload", [("trust", "신뢰")]);
        string path = Path.Combine(_root, "reload.xlsx");
        ChapterWorkbookWriter.AddEpisode(path, "ep1", "첫 화", 0, 0);
        ChapterWorkbookWriter.AddEpisode(path, "ep2", "둘째", 200, 0);
        ChapterWorkbookWriter.AddEdge(path, "ep1", "ep2", optionLabel: "다음");

        byte[] first = ChapterExportBytes.Encode(
            ChapterProgressionExporter.Export(ChapterWorkbookReader.Read(path), null).Json!);
        byte[] reopened = ChapterExportBytes.Encode(
            ChapterProgressionExporter.Export(ChapterWorkbookReader.Read(path), null).Json!);

        Assert.Equal(first, reopened);
    }

    [Fact]
    public void 문화권과_OS개행은_출력_바이트에_영향을_주지_않는다()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ko-KR");
            string korean = ChapterProgressionExporter.Export(Chapter(true), null).Json!;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            string french = ChapterProgressionExporter.Export(Chapter(true), null).Json!;

            Assert.Equal(korean, french);
            Assert.DoesNotContain('\r', korean);
            Assert.False(ChapterExportBytes.Encode(korean).AsSpan().StartsWith(Encoding.UTF8.Preamble));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Nodes와_Stats는_SourceRow_순서로_고정된다()
    {
        using JsonDocument json = JsonDocument.Parse(
            ChapterProgressionExporter.Export(Chapter(true), null).Json!);

        Assert.Equal(["trust", "flag"], json.RootElement.GetProperty("Stats")
            .EnumerateArray().Select(x => x.GetProperty("Key").GetString()!).ToArray());
        Assert.Equal(["ep1", "ep2", "ep3"], json.RootElement.GetProperty("Nodes")
            .EnumerateArray().Select(x => x.GetProperty("EpisodeId").GetString()!).ToArray());
    }

    [Fact]
    public void NextOptions는_간선_SourceRow_순서를_보존한다()
    {
        using JsonDocument json = JsonDocument.Parse(
            ChapterProgressionExporter.Export(Chapter(true), null).Json!);
        JsonElement options = json.RootElement.GetProperty("Nodes")[0].GetProperty("NextOptions");

        Assert.Equal("ep2", options[0].GetProperty("TargetEpisodeId").GetString());
        Assert.Equal("ep3", options[1].GetProperty("TargetEpisodeId").GetString());
    }

    [Fact]
    public void 출시_기준선은_자동_export와_분리해_같은_바이트로_복사한다()
    {
        string export = ChapterExportService.ExportPathFor(ProjectPath, "ch");
        Directory.CreateDirectory(Path.GetDirectoryName(export)!);
        byte[] expected = ChapterExportBytes.Encode("{\r\n  \"x\": 1\r\n}");
        File.WriteAllBytes(export, expected);

        Assert.True(ChapterReleaseBaseline.Capture(ProjectPath, "ch"));
        Assert.Equal(expected, File.ReadAllBytes(ChapterReleaseBaseline.PathFor(ProjectPath, "ch")));
        Assert.NotEqual(Path.GetDirectoryName(export),
            Path.GetDirectoryName(ChapterReleaseBaseline.PathFor(ProjectPath, "ch")));
    }

    [Theory]
    [InlineData("swap")]
    [InlineData("insert-front")]
    [InlineData("delete-front")]
    public void 기존_OptionIndex를_이동시키는_변경을_찾는다(string mutation)
    {
        string baseline = JsonWithOptions("A", "B");
        string current = mutation switch
        {
            "swap" => JsonWithOptions("B", "A"),
            "insert-front" => JsonWithOptions("X", "A", "B"),
            _ => JsonWithOptions("B")
        };

        Assert.Equal(["ep1"], ChapterReleaseBaseline.FindOrderChanges(baseline, current));
    }

    [Fact]
    public void 끝_추가와_비순서_콘텐츠_수정은_막지_않는다()
    {
        Assert.Empty(ChapterReleaseBaseline.FindOrderChanges(
            JsonWithOptions("A", "B"), JsonWithOptions("A", "B", "C")));

        string changedLabel = JsonWithOptions("A*", "B");
        Assert.Empty(ChapterReleaseBaseline.FindOrderChanges(JsonWithOptions("A", "B"), changedLabel));
    }

    [Fact]
    public void 출시_기준선_뒤_순서_변경은_기존_export를_보존하고_이유를_말한다()
    {
        string workbook = Path.Combine(_root, "ch.xlsx");
        File.WriteAllBytes(workbook, [1, 2, 3]);
        var service = new ChapterExportService();
        ChapterGraphModel original = Chapter(true);

        ChapterExportRun first = service.ExportAll(
            [new ChapterEntry("ch", workbook, original, null)], ProjectPath);
        Assert.True(first.AllExported, first.Notice);
        Assert.True(ChapterReleaseBaseline.Capture(ProjectPath, "ch"));
        byte[] released = File.ReadAllBytes(ChapterExportService.ExportPathFor(ProjectPath, "ch"));

        ChapterGraphModel swapped = new(
            original.ChapterId, original.SourcePath, original.Episodes,
            original.Edges.Select(edge => edge with { SourceRow = 20 - edge.SourceRow }).ToArray(),
            original.Conditions, original.Stats, original.Fixtures, original.Diagnostics);
        ChapterExportRun run = service.ExportAll(
            [new ChapterEntry("ch", workbook, swapped, null)], ProjectPath);

        Assert.False(run.AllExported);
        Assert.Contains(run.Blocked, item => item.StartsWith("ch(ep1)", StringComparison.Ordinal));
        Assert.Contains("OptionIndex", run.Notice);
        Assert.Equal(released, File.ReadAllBytes(ChapterExportService.ExportPathFor(ProjectPath, "ch")));
    }

    private static string JsonWithOptions(params string[] labels) => JsonSerializer.Serialize(new
    {
        Nodes = new[] { new { EpisodeId = "ep1", NextOptions = labels.Select(label => new
        {
            TargetEpisodeId = "to-" + label.TrimEnd('*'), ChoiceLabel = label
        }) } }
    });

    private static ChapterGraphModel Chapter(bool orderReversedInMemory) => new(
        "ch", "",
        orderReversedInMemory
            ? [Episode("ep3", 4), Episode("ep1", 2), Episode("ep2", 3)]
            : [Episode("ep1", 2), Episode("ep2", 3), Episode("ep3", 4)],
        [
            new ChapterEdge("ep1", "ep3", "B", null, null, 9),
            new ChapterEdge("ep1", "ep2", "A", null, null, 8)
        ],
        [],
        [
            new ChapterStat("flag", "깃발", 0, 0, 1, 3, ChapterStatType.Bool),
            new ChapterStat("trust", "신뢰", 0, 0, 5, 2)
        ],
        [], []);

    private static ChapterEpisode Episode(string id, int row) =>
        new(id, id, "", id, 0, 0, null, row);
}
