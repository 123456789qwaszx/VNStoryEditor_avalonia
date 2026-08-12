using System.IO.Compression;
using System.Text;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 소유자 보고 — "화자·내용만 타이핑했는데 구글 드라이브가 .xlsm으로 바꾼다."
/// 툴이 만든 템플릿이 <b>정말 평범한 .xlsx인지</b>, 매크로 사용 형식으로 선언돼 있지는
/// 않은지를 컨테이너 수준에서 확인한다. 추측 대신 파일을 뜯어본다.
/// </summary>
public sealed class EpisodeTemplateFormatTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-template-format", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 에피소드_템플릿은_매크로_사용_형식이_아니다()
    {
        EpisodeLibrary.EnsureWorkbook(_directory, "ep01");

        string contentTypes = ReadEntry(
            EpisodeLibrary.PathFor(_directory, "ep01"), "[Content_Types].xml");

        // 매크로 사용 통합 문서(.xlsm)의 선언이 있으면 안 된다.
        Assert.DoesNotContain("macroEnabled", contentTypes, StringComparison.OrdinalIgnoreCase);

        // 평범한 통합 문서로 선언돼 있어야 한다.
        Assert.Contains("spreadsheetml.sheet.main+xml", contentTypes, StringComparison.Ordinal);
    }

    [Fact]
    public void 에피소드_템플릿에_매크로_바이너리가_없다()
    {
        EpisodeLibrary.EnsureWorkbook(_directory, "ep02");

        using ZipArchive archive = ZipFile.OpenRead(EpisodeLibrary.PathFor(_directory, "ep02"));

        Assert.DoesNotContain(archive.Entries, entry =>
            entry.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LineId_되쓰기로_다시_저장해도_매크로_형식이_되지_않는다()
    {
        // 툴이 파일을 다시 쓰는 유일한 자리는 LineId 되쓰기다(ClosedXML SaveAs).
        // 그 경로를 지난 뒤에도 컨테이너 선언이 그대로인지 확인한다.
        EpisodeLibrary.EnsureWorkbook(_directory, "ep04");
        string path = EpisodeLibrary.PathFor(_directory, "ep04");

        using (var memory = new MemoryStream())
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                stream.CopyTo(memory);
            }

            memory.Position = 0;

            using var workbook = new ClosedXML.Excel.XLWorkbook(memory);
            workbook.Worksheets.First().Cell(2, 2).SetValue("ln_0001");
            workbook.SaveAs(path);
        }

        string contentTypes = ReadEntry(path, "[Content_Types].xml");

        Assert.DoesNotContain("macroEnabled", contentTypes, StringComparison.OrdinalIgnoreCase);

        using ZipArchive archive = ZipFile.OpenRead(path);
        Assert.DoesNotContain(archive.Entries, entry =>
            entry.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>템플릿이 실제로 무엇을 담고 있는지 — 구글 시트가 손댈 수 있는 것들의 목록.</summary>
    [Fact]
    public void 템플릿이_담은_엑셀_전용_장치를_기록한다()
    {
        EpisodeLibrary.EnsureWorkbook(_directory, "ep03");

        string sheet = ReadEntry(EpisodeLibrary.PathFor(_directory, "ep03"), "xl/worksheets/sheet1.xml");

        // 드롭다운은 남는다(시트에서도 유용). 시트 보호는 v4에서 뺐다 — 툴이 이 파일을
        // 쓰지 않으므로 지킬 셀이 없고, 외부 편집기가 재저장할 때 깨질 것도 줄었다.
        Assert.Contains("dataValidation", sheet, StringComparison.Ordinal);
        Assert.DoesNotContain("sheetProtection", sheet, StringComparison.Ordinal);
    }

    private static string ReadEntry(string workbookPath, string entryName)
    {
        using ZipArchive archive = ZipFile.OpenRead(workbookPath);
        ZipArchiveEntry entry = archive.GetEntry(entryName)
            ?? throw new InvalidOperationException($"'{entryName}'이 없습니다.");

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
