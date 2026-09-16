using ClosedXML.Excel;
using Vn.Authoring.Chapters;
using Vn.Authoring.Chapters.Import;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>챕터 워크북 → 프로젝트, 한 번만</b> (R-F · 2026-09-16, 지시서 §5).
///
/// <see cref="EpisodeWorkbookImporter"/>의 짝이고 지키는 불변식도 같다:
/// ① 임포트는 <b>명시적</b>이다 — 감시가 부르지 않는다
/// ② 임포트 뒤 그 프로젝트는 <b>다시 읽지 않는다</b>
/// ③ 임포트가 <b>부분 성공하지 않는다</b> (§5.2)
/// </summary>
public sealed class ChapterWorkbookImporterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-chapter-import", Guid.NewGuid().ToString("N"));

    private string ManifestPath => Path.Combine(_directory, "p" + ProjectManifestJson.FileExtension);

    private string ChaptersFolder => Path.Combine(_directory, ChapterLibrary.FolderName);

    public ChapterWorkbookImporterTests()
    {
        Directory.CreateDirectory(ChaptersFolder);
        ProjectStore.Save(ManifestPath, new StoryProject { Title = "챕터를 들여온다" });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 들여오면_챕터가_프로젝트_안에_선다()
    {
        Write("ch01", ("ep01", "ep02"));

        var editor = new ProjectEditor(new StoryProject());
        ChapterProjectImport import = ChapterWorkbookImporter.Run(editor, ManifestPath);

        Assert.True(import.Applied, string.Join(" / ", import.Diagnostics.Select(item => item.Message)));
        Assert.Equal(["ch01"], import.ChapterIds);

        ChapterDocument chapter = Assert.Single(editor.Project.Chapters);

        Assert.Equal("ch01", chapter.ChapterId);
        Assert.Equal(["ep01", "ep02"], chapter.Episodes.Select(episode => episode.EpisodeId));
        Assert.Equal([("ep01", "ep02")], chapter.Edges.Select(edge => (edge.FromEpisodeId, edge.ToEpisodeId)));
    }

    [Fact]
    public void 들여온_뒤에는_워크북을_바깥에서_고쳐도_프로젝트가_안_변한다()
    {
        // ⛔ <b>§5.2의 불변식이다.</b> 다시 읽는 길이 하나라도 남아 있으면 뒤집기가 안 끝난다 —
        //    툴이 쓴 값을 툴이 되읽는 왕복이 살아나고, 어느 쪽이 원본인지 다시 흐려진다.
        Write("ch01", ("ep01", "ep02"));

        var editor = new ProjectEditor(new StoryProject());
        Assert.True(ChapterWorkbookImporter.Run(editor, ManifestPath).Applied);

        // 바깥에서 에피소드를 하나 더 붙인다 — 엑셀에서 사람이 고친 것과 같다.
        ChapterWorkbookWriter.AddEpisode(
            Path.Combine(ChaptersFolder, "ch01.xlsx"), "ep03", title: "몰래 더한 것", 0, 0);

        ChapterDocument chapter = Assert.Single(editor.Project.Chapters);

        Assert.Equal(["ep01", "ep02"], chapter.Episodes.Select(episode => episode.EpisodeId));
    }

    [Fact]
    public void 하나가_깨지면_아무것도_안_들여온다()
    {
        // ⛔ §5.2 — "임포트가 부분 성공하지 않는다". 절반만 들어온 프로젝트는 어느 챕터가
        //    원본인지 사람이 알 수 없게 만들고, 되돌릴 길인 워크북은 곧 산출물이 된다.
        Write("ch01", ("ep01", "ep02"));
        Write("ch02", ("ep01", "없는에피소드"));   // 도착이 없는 간선 = 오류

        var editor = new ProjectEditor(new StoryProject());
        ChapterProjectImport import = ChapterWorkbookImporter.Run(editor, ManifestPath);

        Assert.False(import.Applied);
        Assert.Empty(import.ChapterIds);

        // 멀쩡했던 ch01조차 안 들어왔다 — 그것이 "부분 성공하지 않는다"의 뜻이다.
        Assert.Empty(editor.Project.Chapters);

        Assert.Contains(import.Errors, item => item.Code == ChapterDiagnosticCode.EdgeEndpointUnknown);
    }

    [Fact]
    public void 못_연_파일도_같은_관문을_지난다()
    {
        // 데이터의 흠이 아니라 접근 실패지만 관문은 같다 — 무엇이 안 들어왔는지 모르는 채로
        // 절반을 들이지 않는다.
        Write("ch01", ("ep01", "ep02"));

        var editor = new ProjectEditor(new StoryProject());

        using (new FileStream(
                   Path.Combine(ChaptersFolder, "ch01.xlsx"),
                   FileMode.Open, FileAccess.Read, FileShare.None))
        {
            ChapterProjectImport import = ChapterWorkbookImporter.Run(editor, ManifestPath);

            Assert.False(import.Applied);
            Assert.Empty(editor.Project.Chapters);

            Assert.Contains(
                import.Errors,
                item => item.Code == ChapterDiagnosticCode.ChapterFileUnreadable);
        }
    }

    [Fact]
    public void 두_번_들여오면_합치지_않고_갈아_끼운다()
    {
        // ⛔ 행 단위로 맞추려면 신원 매칭이 필요하고, 그것이 곧 R-D에서 철거한 동기화 기계다.
        Write("ch01", ("ep01", "ep02"));

        var editor = new ProjectEditor(new StoryProject());
        Assert.True(ChapterWorkbookImporter.Run(editor, ManifestPath).Applied);

        File.Delete(Path.Combine(ChaptersFolder, "ch01.xlsx"));
        Write("ch01", ("ep10", "ep20"));

        Assert.True(ChapterWorkbookImporter.Run(editor, ManifestPath).Applied);

        ChapterDocument chapter = Assert.Single(editor.Project.Chapters);

        Assert.Equal(["ep10", "ep20"], chapter.Episodes.Select(episode => episode.EpisodeId));
    }

    [Fact]
    public void 챕터_폴더가_없으면_빈_성공이다()
    {
        // 아직 챕터를 안 만든 프로젝트다 — 오류로 세우면 새 프로젝트가 붉은 화면으로 열린다.
        Directory.Delete(ChaptersFolder, recursive: true);

        var editor = new ProjectEditor(new StoryProject());
        ChapterProjectImport import = ChapterWorkbookImporter.Run(editor, ManifestPath);

        Assert.True(import.Applied);
        Assert.Empty(import.ChapterIds);
        Assert.Empty(import.Diagnostics);
    }

    /// <summary>에피소드 몇 개와 그것을 잇는 간선 하나짜리 챕터 워크북.</summary>
    private void Write(string chapterId, (string From, string To) edge)
    {
        ChapterWorkbookWriter.EnsureChapterWorkbook(ChaptersFolder, chapterId, [("trust", "신뢰")]);

        string path = Path.Combine(ChaptersFolder, chapterId + ".xlsx");

        ChapterWorkbookWriter.AddEpisode(path, edge.From, title: edge.From, 0, 0);

        // 도착이 없는 간선을 만들려면 그 에피소드를 안 세운다 — 거부 사례를 위한 것이다.
        if (!string.Equals(edge.To, "없는에피소드", StringComparison.Ordinal))
        {
            ChapterWorkbookWriter.AddEpisode(path, edge.To, title: edge.To, 200, 0);
        }

        ChapterWorkbookWriter.AddEdge(path, edge.From, edge.To, optionLabel: "다음");
    }
}
