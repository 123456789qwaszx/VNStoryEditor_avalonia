using System.Text.Json;
using ClosedXML.Excel;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>R2 — 자동 진행은 공백 추론이 아니라 워크북의 명시적 계약이다.</summary>
public sealed class ChapterAutoAuthoringTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-chapter-auto", Guid.NewGuid().ToString("N"));

    public ChapterAutoAuthoringTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string NewChapter()
    {
        ChapterWorkbookWriter.EnsureChapterWorkbook(_directory, "auto");
        string path = Path.Combine(_directory, "auto.xlsx");
        ChapterWorkbookWriter.AddEpisode(path, "ep1", "첫째", 0, 0);
        ChapterWorkbookWriter.AddEpisode(path, "ep2", "둘째", 200, 0);
        ChapterWorkbookWriter.UpdateEpisode(path, "ep1", sceneId: "scene-a");
        ChapterWorkbookWriter.UpdateEpisode(path, "ep2", sceneId: "scene-a");
        return path;
    }

    [Fact]
    public void 자동_FALSE의_빈_문구는_자동으로_추측하지_않고_거부한다()
    {
        string path = NewChapter();
        ChapterWorkbookWriter.AddEdge(path, "ep1", "ep2", optionLabel: "", auto: false);

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);

        Assert.Contains(model.Errors, item => item.Code == ChapterDiagnosticCode.OptionLabelBlank);
        Assert.False(Assert.Single(model.Edges).Auto);
    }

    [Fact]
    public void 올바른_자동_간선은_읽고_명시적으로_내보낸다()
    {
        string path = NewChapter();
        ChapterWorkbookWriter.AddEdge(path, "ep1", "ep2", auto: true);

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);
        ChapterEdge edge = Assert.Single(model.Edges);
        Assert.True(edge.Auto);
        Assert.DoesNotContain(model.Errors, item => item.Code == ChapterDiagnosticCode.OptionLabelBlank);

        ChapterExportResult export = ChapterProgressionExporter.Export(model, episodesFolder: null);
        Assert.False(export.Refused, string.Join(" / ", export.Validation.All.Select(item => item.Message)));

        using JsonDocument json = JsonDocument.Parse(export.Json!);
        JsonElement option = json.RootElement.GetProperty("Nodes")[0].GetProperty("NextOptions")[0];
        Assert.True(option.GetProperty("Auto").GetBoolean());
        Assert.Equal(string.Empty, option.GetProperty("ChoiceLabel").GetString());
    }

    [Fact]
    public void 자동_불변식은_위반마다_서로_다른_진단을_낸다()
    {
        string path = NewChapter();
        ChapterWorkbookWriter.AddEpisode(path, "ep3", "셋째", 400, 0);
        ChapterWorkbookWriter.UpdateEpisode(path, "ep3", sceneId: "scene-b");
        ChapterWorkbookWriter.AddCondition(path, "항상", "trust >= 0");

        using (var workbook = new XLWorkbook(path))
        {
            IXLWorksheet stats = workbook.Worksheet(ChapterSheetNames.Stats);
            stats.Cell(2, 1).SetValue("int");
            stats.Cell(2, 2).SetValue("trust");
            stats.Cell(2, 3).SetValue("신뢰");
            stats.Cell(2, 4).SetValue(0);
            stats.Cell(2, 5).SetValue(0);
            stats.Cell(2, 6).SetValue(10);

            IXLWorksheet edges = workbook.Worksheet(ChapterSheetNames.Edges);
            edges.Cell(2, 1).SetValue("ep1");
            edges.Cell(2, 2).SetValue("ep3");
            edges.Cell(2, 3).SetValue("trust +1");
            edges.Cell(2, 4).SetValue("계속");
            edges.Cell(2, 5).SetValue("항상");
            edges.Cell(2, 8).SetValue(true);
            edges.Cell(3, 1).SetValue("ep1");
            edges.Cell(3, 2).SetValue("ep2");
            edges.Cell(3, 4).SetValue("다른 길");
            workbook.SaveAs(path);
        }

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);
        ChapterDiagnosticCode[] codes = model.Errors.Select(item => item.Code).ToArray();

        Assert.Contains(ChapterDiagnosticCode.AutoEdgeHasSiblings, codes);
        Assert.Contains(ChapterDiagnosticCode.AutoEdgeHasConditions, codes);
        Assert.Contains(ChapterDiagnosticCode.AutoEdgeHasStatChanges, codes);
        Assert.Contains(ChapterDiagnosticCode.AutoEdgeCrossesScene, codes);
        Assert.Contains(ChapterDiagnosticCode.AutoEdgeHasChoiceLabel, codes);
    }

    [Fact]
    public void 구판_이행은_자동열을_FALSE로_추가하고_공백을_추측하지_않는다()
    {
        string path = NewChapter();
        ChapterWorkbookWriter.AddEdge(path, "ep1", "ep2", optionLabel: "");

        using (var workbook = new XLWorkbook(path))
        {
            workbook.Worksheet(ChapterSheetNames.Edges).Column(8).Delete();
            workbook.SaveAs(path);
        }

        ChapterWorkbookMigrator.MigrationResult result = ChapterWorkbookMigrator.Migrate(path);
        Assert.True(result.Migrated);
        Assert.True(File.Exists(path + ".bak"));

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);
        Assert.False(Assert.Single(model.Edges).Auto);
        Assert.Contains(model.Errors, item => item.Code == ChapterDiagnosticCode.OptionLabelBlank);

        using var migrated = new XLWorkbook(path);
        Assert.Equal("자동", migrated.Worksheet(ChapterSheetNames.Edges).Cell(1, 8).GetString());
        Assert.Equal("FALSE", migrated.Worksheet(ChapterSheetNames.Edges).Cell(2, 8).GetFormattedString());
    }
}
