using Vn.Authoring.Chapters;
using Vn.Authoring.Chapters.Import;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// R-A — 임포트 경계 (<c>docs/work-orders/tool-owns-workbooks-orders.md</c> 부록 A).
///
/// <b>이 묶음이 지키는 것은 자리다.</b> 워크북 → 모델이 화면 밖의 한 자리를 지나는가 —
/// 그래야 철거(R-D) 때 무엇을 걷어야 하는지가 보인다. <b>횟수</b>(감시자를 떼고 명시적
/// 가져오기로 바꾸는 일)는 R-A의 다음 조각이고 여기서 걸지 않는다.
/// </summary>
public sealed class ChapterImportServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-chapter-import-tests", Guid.NewGuid().ToString("N"));

    public ChapterImportServiceTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    /// <summary>매니페스트 경로만 돌려준다 — 파일을 만들 필요는 없다(폴더 규칙만 쓴다).</summary>
    private string SeedProject(params string[] chapterIds)
    {
        string chapters = Path.Combine(_directory, ChapterLibrary.FolderName);
        Directory.CreateDirectory(chapters);

        foreach (string chapterId in chapterIds)
        {
            File.Copy(SamplePath, Path.Combine(chapters, chapterId + ".xlsx"));
        }

        return Path.Combine(_directory, "예제.vnproject.json");
    }

    [Fact]
    public void 챕터_폴더를_들여와_읽을_수_있는_모델을_돌려준다()
    {
        ChapterImport import = ChapterImportService.Run(SeedProject("ch01", "ch02"));

        Assert.Equal(2, import.Entries.Count);
        Assert.All(import.Entries, entry => Assert.True(entry.IsReadable, entry.OpenFailure));
        Assert.Equal(["ch01", "ch02"], import.Entries.Select(entry => entry.ChapterId).Order());
    }

    [Fact]
    public void 폴더가_없으면_빈_결과다()
    {
        // 아직 챕터를 안 만든 프로젝트다 — 오류가 아니다.
        ChapterImport import = ChapterImportService.Run(
            Path.Combine(_directory, "예제.vnproject.json"));

        Assert.Empty(import.Entries);
        Assert.Empty(import.Notices);
    }

    [Fact]
    public void 매니페스트_경로가_없으면_빈_결과다()
    {
        ChapterImport import = ChapterImportService.Run(null);

        Assert.Empty(import.Entries);
        Assert.Empty(import.Notices);
    }

    [Fact]
    public void 엑셀_잠금_파일은_워크북으로_세지_않는다()
    {
        string manifest = SeedProject("ch01");

        // 엑셀이 파일을 여는 순간 만드는 `~$` 잠금 파일. 이행도 읽기도 건드리면 안 된다.
        string chapters = Path.Combine(_directory, ChapterLibrary.FolderName);
        File.Copy(SamplePath, Path.Combine(chapters, "~$ch01.xlsx"));

        ChapterImport import = ChapterImportService.Run(manifest);

        Assert.Single(import.Entries);
        Assert.Equal("ch01", import.Entries[0].ChapterId);
    }

    [Fact]
    public void 구판_견본은_이행되고_그_사실을_알린다()
    {
        // ⚠ <b>견본(`docs/chapter-graph-sample.xlsx`)은 현행 규격이 아니다</b> — 열면
        //    이행된다(2026-09-16 실측, 이 테스트를 쓰다 알았다). 이행은 조용히 하지
        //    않는다는 것이 규율이므로, 말이 나오는 것이 옳은 동작이다.
        ChapterImport import = ChapterImportService.Run(SeedProject("ch01"));

        string notice = Assert.Single(import.Notices);
        Assert.Contains("이행했습니다", notice, StringComparison.Ordinal);
        Assert.Contains(".bak", notice, StringComparison.Ordinal);
    }

    [Fact]
    public void 이미터가_낸_파일은_이행이_필요_없다()
    {
        // <b>현행 규격의 실물은 이미터의 산출물이다</b>(견본이 아니라 — 위 테스트 참조).
        // 그래서 "손댈 것이 없으면 침묵한다"는 이행기의 규율은 이 재료로 걸어야 한다.
        //
        // 겸해서 이것이 R-B의 파수꾼이다: 이미터가 리더 규격에서 흘러내리면 낸 파일이
        // 구판으로 보여 이행이 돌고, 그 순간 여기서 잡힌다.
        ChapterGraphModel model = ChapterWorkbookReader.Read(SamplePath);

        string chapters = Path.Combine(_directory, ChapterLibrary.FolderName);
        Directory.CreateDirectory(chapters);

        ChapterWriteResult written = ChapterWorkbookEmitter.Emit(
            Path.Combine(chapters, "ch01.xlsx"), model);

        Assert.True(written.Written, written.Failure);

        ChapterImport import = ChapterImportService.Run(
            Path.Combine(_directory, "예제.vnproject.json"));

        Assert.Empty(import.Notices);
        Assert.Single(import.Entries);
    }
}
