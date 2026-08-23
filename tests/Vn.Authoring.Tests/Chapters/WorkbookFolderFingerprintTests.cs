using ClosedXML.Excel;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 지문이 <b>엑셀이 쥐고 있는 동안에도</b> 움직이는가 (2026-08-24 소유자 보고).
///
/// "엑셀에서 대사를 추가했는데 연출그래프에서는 반영이 안 돼. 챕터그래프의 읽기전용
/// 대사목록에서는 반영이 되는데… <b>엑셀을 닫으니까 그제서야 반영이 된다.</b>"
///
/// 정체는 지문이었다. 옛 판은 <c>File.ReadAllBytes</c>로 해시했는데 그것은
/// <c>FileShare.Read</c>로 열어서, 엑셀이 쓰기 권한으로 쥔 파일에서는 IOException이 난다.
/// 그때 <c>'?'</c>를 적었고 <b>'?'는 상수라 다음 저장에도 그대로</b>였다 — 감시자가 깨워도
/// "안 바뀌었다"로 판정되어 조용히 돌아갔다. 엑셀을 닫으면 진짜 해시가 나오니 그제서야
/// 달라 보였다.
///
/// ⚠ 그래서 여기서 재는 것은 <b>"잠긴 채로 두 번 저장했을 때 지문이 달라지는가"</b>다.
/// 한 번만 보면 옛 판도 통과한다 — 첫 저장은 해시에서 '?'로 바뀌므로.
/// </summary>
public sealed class WorkbookFolderFingerprintTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "vn-fingerprint", Guid.NewGuid().ToString("N"));

    public WorkbookFolderFingerprintTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void 내용이_그대로면_지문도_그대로다()
    {
        Write("ep1.xlsx", "가");

        Assert.Equal(WorkbookFolderFingerprint.Of(_folder), WorkbookFolderFingerprint.Of(_folder));
    }

    [Fact]
    public void 내용이_바뀌면_지문이_바뀐다()
    {
        Write("ep1.xlsx", "가");
        string before = WorkbookFolderFingerprint.Of(_folder);

        Write("ep1.xlsx", "나");

        Assert.NotEqual(before, WorkbookFolderFingerprint.Of(_folder));
    }

    [Fact]
    public void 엑셀이_쥐고_있어도_내용을_읽어_지문을_낸다()
    {
        // 리더가 이미 이 공유 모드로 읽고 있다 — 지문만 못 읽고 있었다.
        Write("ep1.xlsx", "가");

        using var excel = Hold("ep1.xlsx");

        Assert.DoesNotContain("잠김", WorkbookFolderFingerprint.Of(_folder));
    }

    [Fact]
    public void 엑셀이_쥔_채_두_번_저장해도_그때마다_지문이_달라진다()
    {
        // ⛔ <b>이것이 그 결함이다.</b> 옛 판은 잠긴 파일을 상수 '?'로 적어서, 두 번째
        // 저장부터 "안 바뀌었다"가 되어 반영이 영영 안 왔다.
        Write("ep1.xlsx", "가");

        using (FileStream excel = Hold("ep1.xlsx"))
        {
            string first = WorkbookFolderFingerprint.Of(_folder);

            WriteThrough(excel, "나");
            string second = WorkbookFolderFingerprint.Of(_folder);

            Assert.NotEqual(first, second);

            WriteThrough(excel, "다");
            string third = WorkbookFolderFingerprint.Of(_folder);

            Assert.NotEqual(second, third);
        }
    }

    [Fact]
    public void 엑셀_잠금_파일은_지문에_안_든다()
    {
        // ~$…는 내용이 아니다 — 열고 닫는 것이 저장이 아니다.
        Write("ep1.xlsx", "가");
        string before = WorkbookFolderFingerprint.Of(_folder);

        File.WriteAllText(Path.Combine(_folder, "~$ep1.xlsx"), "excel");

        Assert.Equal(before, WorkbookFolderFingerprint.Of(_folder));
    }

    [Fact]
    public void 없는_폴더도_그_사실을_적는다()
    {
        // null과 "없음"이 뭉개지면 프로젝트를 닫았다 여는 순간이 안 보인다.
        Assert.NotEqual(
            WorkbookFolderFingerprint.Of((string?)null),
            WorkbookFolderFingerprint.Of(Path.Combine(_folder, "없는폴더")));
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private string Write(string name, string cell)
    {
        string path = Path.Combine(_folder, name);

        using var book = new XLWorkbook();
        book.AddWorksheet("시트").Cell(1, 1).SetValue(cell);
        book.SaveAs(path);

        return path;
    }

    /// <summary>엑셀이 여는 방식 — 읽고 쓰되 남에게는 읽기만 허용한다.</summary>
    private FileStream Hold(string name) => new(
        Path.Combine(_folder, name), FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

    /// <summary>쥔 손 그대로 파일을 고친다 — 엑셀이 저장하는 모양에 가깝다.</summary>
    private static void WriteThrough(FileStream stream, string marker)
    {
        stream.Seek(0, SeekOrigin.End);
        stream.Write(System.Text.Encoding.UTF8.GetBytes(marker));
        stream.Flush(flushToDisk: true);
    }
}
