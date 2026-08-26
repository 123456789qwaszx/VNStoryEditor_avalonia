using ClosedXML.Excel;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 툴이 에피소드를 더할 때 첫 대사를 함께 심는다 (2026-08-26 소유자: "에피소드 추가할 시
/// 자동으로 기본 대사문구를 한 줄 추가하는 게 좋겠어"). 빈 대본은 검증이 오류로 막으므로
/// (줄 없는 노드는 재생이 안 된다), 방금 만든 에피소드가 그 오류부터 들고 시작하지 않는다.
///
/// ⚠ 기본은 그대로 빈 템플릿이다 — <see cref="EpisodeLibrary.EnsureWorkbook"/>은 동기화도
/// 부르는 자리라(엑셀에서 더한 에피소드의 첫 대본), 심는 쪽이 명시해야 한다.
/// </summary>
public sealed class EpisodeFirstLineTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-episode-first-line", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 첫_대사를_심으면_2행_내용에_적힌다()
    {
        EpisodeLibrary.EnsureWorkbook(
            _directory, "ep01", firstLine: EpisodeLibrary.DefaultFirstLine);

        using var workbook = new XLWorkbook(EpisodeLibrary.PathFor(_directory, "ep01"));
        IXLWorksheet sheet = workbook.Worksheets.First();

        // v14 6열 — 내용은 F(6). 인덱스는 깔기가 이미 놓은 번호(2행 = 10)를 그대로 쓴다.
        Assert.Equal(EpisodeLibrary.DefaultFirstLine, sheet.Cell(2, 6).GetString());
        Assert.Equal(10, sheet.Cell(2, 3).GetValue<int>());

        // 화자는 비워 둔다 — 빈 화자 = 지문. LineId는 첫 동기화가 발급한다.
        Assert.Equal(string.Empty, sheet.Cell(2, 5).GetString());
        Assert.Equal(string.Empty, sheet.Cell(2, 4).GetString());
    }

    [Fact]
    public void 안_심으면_예전처럼_빈_템플릿이다()
    {
        EpisodeLibrary.EnsureWorkbook(_directory, "ep02");

        using var workbook = new XLWorkbook(EpisodeLibrary.PathFor(_directory, "ep02"));

        Assert.Equal(string.Empty, workbook.Worksheets.First().Cell(2, 6).GetString());
    }

    [Fact]
    public void 비어_있는_유물_워크북에도_심는다()
    {
        // 에피소드 삭제는 대본 파일을 남기므로, 지운 Id를 다시 더하면 EnsureWorkbook이
        // 빈 유물을 그대로 재사용해 첫 대사가 안 심겼다(2026-08-26 소유자가 바로 밟았다).
        EpisodeLibrary.EnsureWorkbook(_directory, "ep03");   // 기능 전의 빈 워크북
        string path = EpisodeLibrary.PathFor(_directory, "ep03");

        (ChapterWriteResult result, bool seeded) =
            EpisodeWorkbookWriter.SeedFirstLineIfEmpty(path, EpisodeLibrary.DefaultFirstLine);

        Assert.True(result.Written);
        Assert.True(seeded);

        using var workbook = new XLWorkbook(path);
        Assert.Equal(EpisodeLibrary.DefaultFirstLine, workbook.Worksheets.First().Cell(2, 6).GetString());
    }

    [Fact]
    public void 사람의_흔적이_있으면_물러난다()
    {
        // 유형·화자·내용 어느 칸이든 — 쓰다 만 대본에 툴이 글을 얹으면 "안 쓴 글이 생겼다"가 된다.
        EpisodeLibrary.EnsureWorkbook(_directory, "ep04");
        string path = EpisodeLibrary.PathFor(_directory, "ep04");

        using (var workbook = new XLWorkbook(path))
        {
            workbook.Worksheets.First().Cell(7, 6).SetValue("사람이 적던 대사");
            workbook.Save();
        }

        (ChapterWriteResult result, bool seeded) =
            EpisodeWorkbookWriter.SeedFirstLineIfEmpty(path, EpisodeLibrary.DefaultFirstLine);

        Assert.True(result.Written);   // 물러난 것도 성공이다 — 파일에 손대지 않았다.
        Assert.False(seeded);

        using var reread = new XLWorkbook(path);
        Assert.Equal(string.Empty, reread.Worksheets.First().Cell(2, 6).GetString());
        Assert.Equal("사람이 적던 대사", reread.Worksheets.First().Cell(7, 6).GetString());
    }
}
