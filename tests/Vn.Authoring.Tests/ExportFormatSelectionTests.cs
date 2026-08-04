using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests;

/// <summary>X13 — 내보내기는 선택한 양식만 산출하고, 선택은 프로젝트에 저장·복원된다.</summary>
public class ExportFormatSelectionTests
{
    [Fact]
    public void 기본_선택은_저장_파일에_쓰이지_않는다()
    {
        Assert.DoesNotContain(
            "exportFormats",
            ProjectManifestJson.Write(new StoryProject()),
            StringComparison.Ordinal);
    }

    [Fact]
    public void 기본이_아닌_선택은_manifest와_스냅샷을_왕복한다()
    {
        var project = new StoryProject();
        project.ExportFormats.ReviewCsv = false;
        project.ExportFormats.DirectionCsv = false;

        ProjectManifest manifest = ProjectManifestJson.Read(ProjectManifestJson.Write(project));
        Assert.True(manifest.ExportFormats.YarnTrio);
        Assert.True(manifest.ExportFormats.ScriptCsv);
        Assert.False(manifest.ExportFormats.ReviewCsv);
        Assert.False(manifest.ExportFormats.DirectionCsv);

        StoryProject decoded = ProjectSnapshotCodec.Decode(ProjectSnapshotCodec.Encode(project));
        Assert.False(decoded.ExportFormats.ReviewCsv);
        Assert.True(decoded.ExportFormats.ScriptCsv);
    }

    [Fact]
    public void 선택_변경은_편집_통로를_지나고_되돌릴_수_있다()
    {
        var editor = new ProjectEditor(new StoryProject());

        editor.SetExportFormats(new ExportFormatSelection { YarnTrio = false });
        Assert.False(editor.Project.ExportFormats.YarnTrio);

        editor.Undo();
        Assert.True(editor.Project.ExportFormats.YarnTrio);
    }

    [Fact]
    public void CSV는_선택한_종만_산출된다()
    {
        var bundle = new CsvBundle("ep", "script", "review", "direction");

        Assert.Equal(
            ["Script_ep.csv", "Review_ep.csv", "Direction_ep.csv"],
            bundle.FilesFor(new ExportFormatSelection()).Select(file => file.FileName));

        Assert.Equal(
            ["Script_ep.csv"],
            bundle.FilesFor(new ExportFormatSelection { ReviewCsv = false, DirectionCsv = false })
                .Select(file => file.FileName));

        string directory = Path.Combine(Path.GetTempPath(), $"VnTool.Csv.{Guid.NewGuid():N}");

        try
        {
            IReadOnlyList<string> written = CsvBundleExporter.WriteTo(
                bundle,
                directory,
                new ExportFormatSelection { ScriptCsv = false, DirectionCsv = false });

            Assert.Equal(["Review_ep.csv"], written.Select(Path.GetFileName));
            Assert.Equal(["Review_ep.csv"], Directory.GetFiles(directory).Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
