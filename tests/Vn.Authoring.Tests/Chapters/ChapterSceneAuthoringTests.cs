using System.Text.Json;
using ClosedXML.Excel;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>R1 — 엑셀 장면ID가 런타임의 저장·롤백 경계까지 보존되는지 고정한다.</summary>
public sealed class ChapterSceneAuthoringTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-scene-authoring", Guid.NewGuid().ToString("N"));

    public ChapterSceneAuthoringTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 새_워크북은_장면ID를_읽고_쓴다()
    {
        Assert.True(ChapterWorkbookWriter.EnsureChapterWorkbook(_directory, "ch01"));
        string path = Path.Combine(_directory, "ch01.xlsx");

        Assert.True(ChapterWorkbookWriter.AddEpisode(path, "root", "시작", 10, 20).Written);
        Assert.True(ChapterWorkbookWriter.UpdateEpisode(path, "root", sceneId: "opening").Written);

        ChapterEpisode episode = Assert.Single(ChapterWorkbookReader.Read(path).Episodes);
        Assert.Equal("opening", episode.SceneId);
        Assert.Equal("opening", episode.EffectiveSceneId);
    }

    [Fact]
    public void 구판_워크북은_백업과_함께_빈_장면ID열로_이행된다()
    {
        Assert.True(ChapterWorkbookWriter.EnsureChapterWorkbook(_directory, "legacy"));
        string path = Path.Combine(_directory, "legacy.xlsx");
        Assert.True(ChapterWorkbookWriter.AddEpisode(path, "root", "시작", 0, 0).Written);

        using (var workbook = new XLWorkbook(path))
        {
            workbook.Worksheet(ChapterSheetNames.Episodes).Column(4).Delete();
            workbook.Save();
        }

        ChapterWorkbookMigrator.MigrationResult migration = ChapterWorkbookMigrator.Migrate(path);
        Assert.True(migration.Migrated);
        Assert.True(File.Exists(path + ".bak"));

        using (var workbook = new XLWorkbook(path))
        {
            Assert.Equal("장면ID", workbook.Worksheet(ChapterSheetNames.Episodes).Cell(1, 4).GetString());
        }

        ChapterEpisode episode = Assert.Single(ChapterWorkbookReader.Read(path).Episodes);
        Assert.True(string.IsNullOrWhiteSpace(episode.SceneId));
        Assert.Equal("__scene_root", episode.EffectiveSceneId);
        Assert.False(ChapterWorkbookMigrator.Migrate(path).Migrated);
    }

    [Fact]
    public void 같은_장면ID는_산출물에서도_같고_장면_진입점은_표시할_수_있다()
    {
        ChapterGraphModel chapter = Chapter(
            Episode("root", "A", 2), Episode("inside", "A", 3), Episode("next", "B", 4),
            new ChapterEdge("root", "inside", "계속", null, null, 11),
            new ChapterEdge("inside", "next", "다음 장면", null, null, 12));

        ChapterExportResult export = ChapterProgressionExporter.Export(chapter, episodesFolder: null);
        Assert.False(export.Refused);

        using JsonDocument document = JsonDocument.Parse(export.Json!);
        JsonElement[] nodes = document.RootElement.GetProperty("Nodes").EnumerateArray().ToArray();
        Assert.Equal("A", nodes.Single(node => node.GetProperty("EpisodeId").GetString() == "root").GetProperty("SceneId").GetString());
        Assert.Equal("A", nodes.Single(node => node.GetProperty("EpisodeId").GetString() == "inside").GetProperty("SceneId").GetString());
        Assert.True(chapter.IsSceneRoot(chapter.FindEpisode("root")!));
        Assert.False(chapter.IsSceneRoot(chapter.FindEpisode("inside")!));
        Assert.True(chapter.IsSceneRoot(chapter.FindEpisode("next")!));
    }

    [Fact]
    public void 같은_장면으로_두_번_들어가면_두번째_간선행으로_거부를_돌려준다()
    {
        ChapterGraphModel chapter = Chapter(
            Episode("root", "root", 2), Episode("a", "shared", 3), Episode("b", "shared", 4),
            new ChapterEdge("root", "a", "A로", null, null, 11),
            new ChapterEdge("root", "b", "B로", null, null, 12));

        ChapterExportResult export = ChapterProgressionExporter.Export(chapter, episodesFolder: null);

        Assert.True(export.Refused);
        Assert.Contains(export.Validation.All, item =>
            item.Code == ChapterDiagnosticCode.CoreRefusedChapter &&
            item.Sheet == ChapterSheetNames.Edges && item.Row == 12);
    }

    [Fact]
    public void 장면루트로의_재진입은_유효한_순환이다()
    {
        ChapterGraphModel chapter = Chapter(
            Episode("root", "A", 2), Episode("middle", "B", 3),
            new ChapterEdge("root", "middle", "나간다", null, null, 11),
            new ChapterEdge("middle", "root", "돌아온다", null, null, 12));

        ChapterExportResult export = ChapterProgressionExporter.Export(chapter, episodesFolder: null);

        Assert.False(export.Refused, string.Join(" / ", export.Validation.All.Select(item => item.Message)));
    }

    private static ChapterEpisode Episode(string id, string sceneId, int row) =>
        new(id, id, string.Empty, id, 0, 0, null, row) { SceneId = sceneId };

    private static ChapterGraphModel Chapter(
        ChapterEpisode first,
        ChapterEpisode second,
        params ChapterEdge[] edges) =>
        new("scenes", "scenes.xlsx", [first, second], edges, [], [], [], []);

    private static ChapterGraphModel Chapter(
        ChapterEpisode first,
        ChapterEpisode second,
        ChapterEpisode third,
        params ChapterEdge[] edges) =>
        new("scenes", "scenes.xlsx", [first, second, third], edges, [], [], [], []);
}
