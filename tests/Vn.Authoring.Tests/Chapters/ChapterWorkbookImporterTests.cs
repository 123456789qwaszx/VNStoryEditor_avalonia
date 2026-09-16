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
    public void 저작_오류는_들여오기를_막지_않고_짚어만_준다()
    {
        // ⛔ <b>§5.2를 한 번 잘못 읽었다</b> (2026-09-16). "하나라도 해석 못 하면 아무것도
        //    들여오지 않는다"를 <i>오류 진단이 하나라도 있으면</i>으로 읽어, 도달 불가·없는
        //    도착 같은 <b>저작 오류</b>까지 관문으로 막았다. 그러면 <b>작업 중인 프로젝트는
        //    영영 못 들어온다</b> — 리더가 <i>"오류가 있어도 모델은 만든다, 조용히 빈 화면을
        //    주지 않는다"</i>를 규격으로 삼는 바로 그 이유를 임포트가 뒤집는 셈이다(규칙 14).
        //
        //    잃을 위험도 없다: 임포트는 <b>아무것도 쓰지 않는다</b>. 워크북이 덮이는 것은
        //    사람이 그 챕터를 고쳤을 때뿐이고, 그때는 오류가 이미 검증 보고에 서 있다.
        Write("ch01", ("ep01", "ep02"));
        Write("ch02", ("ep01", "없는에피소드"));   // 도착이 없는 간선 = 저작 오류

        var editor = new ProjectEditor(new StoryProject());
        ChapterProjectImport import = ChapterWorkbookImporter.Run(editor, ManifestPath);

        Assert.True(import.Applied);
        Assert.Equal(["ch01", "ch02"], import.ChapterIds);
        Assert.Equal(2, editor.Project.Chapters.Count);

        // 그래도 조용하지 않다 — 무엇이 잘못됐는지는 함께 실려 온다.
        Assert.Contains(import.Errors, item => item.Code == ChapterDiagnosticCode.EdgeEndpointUnknown);
    }

    [Fact]
    public void 못_연_파일_하나가_전부를_막는다()
    {
        // ⛔ <b>이것만이 관문을 닫는다</b> (§5.2). 데이터의 흠이 아니라 접근 실패라,
        //    무엇이 안 들어왔는지 알 길조차 없다 — 그 상태로 절반을 들이지 않는다.
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
