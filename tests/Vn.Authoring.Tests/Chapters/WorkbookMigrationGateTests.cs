using ClosedXML.Excel;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 이행 프로브를 <b>몇 번 하는가</b> (2026-08-24 성능).
///
/// 두 이행기는 "고칠 것이 있는가"를 워크북을 통째로 파싱해서 판정하는데, 그 답은 거의
/// 언제나 "없음"이고 앱은 다시 읽을 때마다 모든 워크북에 그 질문을 다시 했다. 실측으로
/// 워크북 72개에서 <b>작업의 절반</b>(1,071ms 중)이 그 프로브였다.
///
/// ⚠ 여기서 <b>시간을 재지 않는다</b> — 기계마다 다르다. 대신 게이트의 계약을 건다:
/// 내용이 그대로면 "이미 판정 끝"이고, 한 글자라도 바뀌면 다시 물어야 한다. 느려지는
/// 회귀는 언제나 "몇 번 하는가"가 먼저 늘어난다
/// (`ChapterGraphWorkAmountTests`와 같은 결).
///
/// <b>그리고 캐시가 만드는 가장 나쁜 거짓말을 여기서 막는다</b>: 낡은 답을 붙들어
/// "구판 파일을 갖다 놨는데 툴이 이행을 안 한다"가 되는 것.
/// </summary>
public sealed class WorkbookMigrationGateTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "vn-migration-gate", Guid.NewGuid().ToString("N"));

    public WorkbookMigrationGateTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ── 게이트의 계약 ───────────────────────────────────────────────────────

    [Fact]
    public void 처음_보는_파일은_모른다고_답한다()
    {
        // 모를 때 아는 척하면 구판 파일이 영영 이행되지 않는다.
        string path = WriteWorkbook("처음.xlsx", "가");

        Assert.False(WorkbookMigrationGate.IsKnownCurrent(path));
    }

    [Fact]
    public void 판정을_기록하면_같은_내용_동안은_안다고_답한다()
    {
        string path = WriteWorkbook("기억.xlsx", "가");

        WorkbookMigrationGate.MarkCurrent(path);

        Assert.True(WorkbookMigrationGate.IsKnownCurrent(path));
    }

    [Fact]
    public void 내용이_바뀌면_다시_모른다고_답한다()
    {
        // ⛔ 이것이 무너지면 "구판을 갖다 놨는데 툴이 이행을 안 한다"가 된다 —
        // 캐시가 만드는 가장 나쁜 거짓말이다.
        string path = WriteWorkbook("바뀜.xlsx", "가");
        WorkbookMigrationGate.MarkCurrent(path);

        WriteWorkbook("바뀜.xlsx", "나");

        Assert.False(WorkbookMigrationGate.IsKnownCurrent(path));
    }

    [Fact]
    public void 못_읽는_파일은_안다고_답하지_않는다()
    {
        // 잠겼거나 없는 파일 — 모를 때는 아는 척하지 않는다. 이행기가 평소대로
        // 프로브하고 그쪽이 사유를 남긴다.
        Assert.False(WorkbookMigrationGate.IsKnownCurrent(
            Path.Combine(_root, "없는파일.xlsx")));

        Assert.False(WorkbookMigrationGate.IsKnownCurrent(null));

        string path = WriteWorkbook("잠김.xlsx", "가");

        using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.False(WorkbookMigrationGate.IsKnownCurrent(path));
    }

    [Fact]
    public void 못_읽는_파일은_기록도_안_한다()
    {
        // 해시를 못 얻었는데 "알았다"고 적으면 그 뒤로 영영 안 묻는다.
        string missing = Path.Combine(_root, "없는파일.xlsx");

        WorkbookMigrationGate.MarkCurrent(missing);

        Assert.False(WorkbookMigrationGate.IsKnownCurrent(missing));
    }

    // ── 이행기가 실제로 게이트를 지나는가 ───────────────────────────────────

    [Fact]
    public void 챕터_이행기가_판정을_남긴다()
    {
        // 게이트만 맞고 이행기가 안 쓰면 아무것도 안 빨라진다 — 배선을 건다.
        string chapters = Path.Combine(_root, "chapters");
        ChapterWorkbookWriter.EnsureChapterWorkbook(chapters, "ch01", [("trust", "신뢰")]);
        string path = Path.Combine(chapters, "ch01.xlsx");

        Assert.False(WorkbookMigrationGate.IsKnownCurrent(path));

        ChapterWorkbookMigrator.Migrate(path);

        Assert.True(
            WorkbookMigrationGate.IsKnownCurrent(path),
            "이행기가 판정을 남기지 않으면 다음 재읽기가 또 통째로 판다");
    }

    [Fact]
    public void 대본_이행기가_판정을_남긴다()
    {
        string folder = Path.Combine(_root, "episodes", "ch01");
        Directory.CreateDirectory(folder);
        EpisodeLibrary.EnsureWorkbook(folder, "ep1");
        string path = EpisodeLibrary.FindExisting(folder, "ep1")!;

        Assert.False(WorkbookMigrationGate.IsKnownCurrent(path));

        EpisodeWorkbookMigrator.Migrate(path);

        Assert.True(WorkbookMigrationGate.IsKnownCurrent(path));
    }

    [Fact]
    public void 이행을_실제로_한_뒤에도_판정을_남긴다()
    {
        // 이행 직후 한 번 더 파고들 이유가 없다 — 방금 우리가 쓴 내용이다.
        string folder = Path.Combine(_root, "episodes", "ch01");
        Directory.CreateDirectory(folder);

        string path = WriteLegacyEpisode(folder, "구판.xlsx");

        Assert.True(EpisodeWorkbookMigrator.Migrate(path).Migrated, "구판이라 이행이 돌아야 한다");
        Assert.True(WorkbookMigrationGate.IsKnownCurrent(path));

        // 그리고 두 번째 호출은 이행하지 않는다(이미 새 규격이다).
        Assert.False(EpisodeWorkbookMigrator.Migrate(path).Migrated);
    }

    [Fact]
    public void 구판_파일로_덮으면_다시_이행한다()
    {
        // ⛔ 게이트가 "이 경로는 이미 봤다"로 기억하면 여기서 무너진다. 기억의 열쇠는
        // 경로가 아니라 <b>내용</b>이라야 한다.
        string folder = Path.Combine(_root, "episodes", "ch01");
        Directory.CreateDirectory(folder);

        string path = WriteLegacyEpisode(folder, "되돌림.xlsx");
        Assert.True(EpisodeWorkbookMigrator.Migrate(path).Migrated);

        // 같은 경로에 구판을 다시 갖다 놓는다 — 사람이 백업을 되돌리는 실제 흐름이다.
        WriteLegacyEpisode(folder, "되돌림.xlsx");

        Assert.False(WorkbookMigrationGate.IsKnownCurrent(path));
        Assert.True(
            EpisodeWorkbookMigrator.Migrate(path).Migrated,
            "구판을 갖다 놨는데 이행이 안 돌면 캐시가 거짓말을 한 것이다");
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <summary>내용이 다르면 해시도 다르다 — 게이트의 열쇠를 흔들어 보는 용도.</summary>
    private string WriteWorkbook(string fileName, string cell)
    {
        string path = Path.Combine(_root, fileName);

        using var workbook = new XLWorkbook();
        workbook.AddWorksheet("시트").Cell(1, 1).SetValue(cell);
        workbook.SaveAs(path);

        return path;
    }

    /// <summary>구판 대본(9열 평면) — 이행기가 반드시 손대는 모양.</summary>
    private static string WriteLegacyEpisode(string folder, string fileName)
    {
        string path = Path.Combine(folder, fileName);

        using var workbook = new XLWorkbook();
        IXLWorksheet sheet = workbook.AddWorksheet("대본");

        // ⚠ 순서가 규격이다 — 이행기는 이 머리글 배열이 정확히 맞는 시트만 구판으로 본다.
        string[] headers =
            ["인덱스", "LineId", "유형", "태그", "조건라벨", "IN", "OUT", "화자", "내용"];

        for (int column = 0; column < headers.Length; column++)
        {
            sheet.Cell(1, column + 1).SetValue(headers[column]);
        }

        sheet.Cell(2, 1).SetValue(10);
        sheet.Cell(2, 8).SetValue("라루");
        sheet.Cell(2, 9).SetValue("첫 줄");

        workbook.SaveAs(path);
        return path;
    }
}
