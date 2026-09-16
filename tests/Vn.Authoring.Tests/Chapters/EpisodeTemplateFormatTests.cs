using System.IO.Compression;
using ClosedXML.Excel;
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
        // ⚠ v15 — 남은 드롭다운은 <b>화자 하나뿐</b>이라 목록을 줘야 선다(유형 드롭다운과
        // 블록 행 빗장은 조건 블록과 함께 사라졌다). 목록 없이 만들면 검증이 하나도 없다.
        EpisodeLibrary.EnsureWorkbook(_directory, "ep03", ["라루", "윌로"]);

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

    /// <summary>
    /// 빈 워크북의 규격 (2026-09-16에 `EpisodeSyncServiceTests`에서 옮겨 왔다 — R-D).
    /// 그 파일은 역방향 동기화의 것이라 걷혔는데, 이것은 <b>템플릿의 모양</b>을 재는 것이라
    /// 여기가 제자리다.
    /// </summary>
    [Fact]
    public void 없는_에피소드_워크북은_규격대로_생성된다()
    {
        string folder = Path.Combine(_directory, "episodes");

        Assert.True(EpisodeLibrary.EnsureWorkbook(folder, "main05.03"));
        Assert.False(EpisodeLibrary.EnsureWorkbook(folder, "main05.03")); // 두 번째는 그대로 둔다

        string path = EpisodeLibrary.PathFor(folder, "main05.03");

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);
        IXLWorksheet sheet = workbook.Worksheets.First();

        // v15 머리글 (2026-09-16) — 넷 다 대사 줄의 것이다.
        Assert.Equal(
            ["인덱스", "LineId", "화자", "내용"],
            Enumerable.Range(1, 4).Select(column => sheet.Cell(1, column).GetString()));

        Assert.Equal(string.Empty, sheet.Cell(1, 5).GetString());
        Assert.Equal(10, sheet.Cell(2, 1).GetDouble());   // 인덱스 사다리는 A열에 깔린다

        // 시트 보호는 없다 (v4) — 툴이 이 파일을 쓰지 않으므로 지킬 셀이 없고,
        // 외부 편집기(구글 시트)가 재저장할 때 깨질 것도 하나 줄었다.
        Assert.False(sheet.Protection.IsProtected);

        // v15 — 검증이 하나도 안 선다. `유형` 드롭다운과 블록 행 빗장 셋이 조건 블록과
        // 함께 사라졌고, 화자 드롭다운은 목록을 받았을 때만 선다(여기서는 안 줬다).
        Assert.Empty(sheet.DataValidations);
    }
}
