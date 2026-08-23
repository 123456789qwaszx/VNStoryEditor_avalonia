using ClosedXML.Excel;
using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 워크북을 <b>몇 번 파고드는가</b> (2026-08-24 성능, 소유자: "대본들은 개별 엑셀인데,
/// 그것들 중 변경된 것만 읽으면 될 것 같습니다").
///
/// 실측: 대본 32개로 "아무것도 안 바뀐 동기화 한 바퀴"가 594ms였고 그중 <b>540ms(91%)가
/// 파싱</b>이었다. 대본 하나만 저장해도 그 챕터의 대본을 전부 다시 파고들었기 때문이다.
/// 캐시 뒤 594 → <b>17ms</b>.
///
/// ⚠ 여기서 <b>시간을 재지 않는다</b> — 기계마다 다르다. "다시 파싱하지 않았다"는
/// <b>같은 인스턴스가 돌아오는가</b>로 증명한다. 모델이 불변이라 성립하는 증명이다.
///
/// <b>그리고 캐시가 만드는 가장 나쁜 거짓말을 막는다</b>: 파일이나 부가 입력이 바뀌었는데
/// 옛 답을 붙드는 것.
/// </summary>
public sealed class WorkbookParseCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "vn-parse-cache", Guid.NewGuid().ToString("N"));

    public WorkbookParseCacheTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    // ── 대본 ────────────────────────────────────────────────────────────────

    [Fact]
    public void 같은_대본을_두_번_읽으면_다시_파고들지_않는다()
    {
        // 같은 인스턴스 = 파싱이 한 번뿐이었다는 증명. 모델이 불변이라 나눠 써도 된다.
        string path = CopySample("ep1.xlsx");

        EpisodeWorkbookModel first = EpisodeWorkbookReader.Read(path);
        EpisodeWorkbookModel second = EpisodeWorkbookReader.Read(path);

        Assert.Same(first, second);
    }

    [Fact]
    public void 대본이_바뀌면_다시_읽는다()
    {
        // ⛔ 이것이 무너지면 "엑셀에서 고쳤는데 툴이 그대로"가 된다.
        string path = CopySample("ep1.xlsx");

        EpisodeWorkbookModel before = EpisodeWorkbookReader.Read(path);

        WriteFirstLine(path, "라루", "고친 대사");

        EpisodeWorkbookModel after = EpisodeWorkbookReader.Read(path);

        Assert.NotSame(before, after);
        Assert.Contains(after.Rows, row => row.Text == "고친 대사");
    }

    [Fact]
    public void 조건_라벨이_다르면_다른_답이_나온다()
    {
        // 같은 파일이라도 라벨 목록이 다르면 <b>진단이 다르다</b> — 파일 밖의 입력도
        // 열쇠에 들어가야 한다. 빠뜨리면 남의 답을 그대로 돌려준다.
        string path = CopySample("ep1.xlsx");

        EpisodeWorkbookModel known = EpisodeWorkbookReader.Read(path, ["지쳐있음", "신뢰높음"]);
        EpisodeWorkbookModel unknown = EpisodeWorkbookReader.Read(path, []);

        Assert.NotSame(known, unknown);

        // 라벨을 모르면 그 자리를 오류로 짚는다 — 알면 조용하다.
        Assert.True(
            unknown.Diagnostics.Count > known.Diagnostics.Count,
            "라벨을 모르는 쪽이 더 많이 짚어야 한다");
    }

    [Fact]
    public void 서로_다른_부가_입력이_서로를_밀어내지_않는다()
    {
        // ⚠ 실제로 이렇게 쓴다: 같은 대본을 <b>대사 미리보기는 라벨 없이</b>, 동기화·검증은
        // <b>라벨과 함께</b> 읽는다. 경로 하나에 칸 하나만 두면 둘이 번갈아 캐시를 깨서
        // 고치기 전보다 느려진다(해시 값까지 얹힌다). 재 보고 알았다.
        string path = CopySample("ep1.xlsx");

        EpisodeWorkbookModel withLabels = EpisodeWorkbookReader.Read(path, ["지쳐있음"]);
        EpisodeWorkbookModel without = EpisodeWorkbookReader.Read(path);

        // 서로 오간 뒤에도 둘 다 그대로 살아 있다.
        Assert.Same(withLabels, EpisodeWorkbookReader.Read(path, ["지쳐있음"]));
        Assert.Same(without, EpisodeWorkbookReader.Read(path));
    }

    // ── 챕터 ────────────────────────────────────────────────────────────────

    [Fact]
    public void 같은_챕터를_두_번_읽으면_다시_파고들지_않는다()
    {
        string path = CopySample("ch05.xlsx");

        Assert.Same(
            ChapterWorkbookReader.Read(path),
            ChapterWorkbookReader.Read(path));
    }

    [Fact]
    public void 정의의_변수가_달라지면_다시_읽는다()
    {
        // ⛔ 여기가 이 캐시에서 가장 틀리기 쉬운 자리다 — <b>파일 밖의 입력</b>.
        // 이 리더가 정의에서 보는 것은 "스탯이 정의에 있는가"뿐인데, 그것을 열쇠에서
        // 빠뜨리면 변수를 더한 뒤에도 <b>옛 경고가 그대로 남는다.</b>
        string path = CopySample("ch05.xlsx");

        ChapterGraphModel without = ChapterWorkbookReader.Read(path, Definition());

        Assert.Contains(without.Diagnostics, item =>
            item.Code == ChapterDiagnosticCode.StatMissingFromGameDefinition);

        // 그 스탯을 정의에 더한다 — 경고가 사라져야 한다.
        string[] stats = without.Stats.Select(stat => stat.Key).ToArray();
        ChapterGraphModel with = ChapterWorkbookReader.Read(path, Definition(stats));

        Assert.NotSame(without, with);
        Assert.DoesNotContain(with.Diagnostics, item =>
            item.Code == ChapterDiagnosticCode.StatMissingFromGameDefinition);
    }

    // ── 모를 때는 아는 척하지 않는다 ────────────────────────────────────────

    [Fact]
    public void 해시를_못_얻으면_캐시가_손을_뗀다()
    {
        // 잠긴 파일 — 읽기는 평소대로 시도하고, 실패하면 그쪽이 사유를 낸다.
        // 여기서 옛 답을 돌려주면 잠금이 풀린 뒤에도 낡은 모델을 붙든다.
        string path = CopySample("ep1.xlsx");
        _ = EpisodeWorkbookReader.Read(path);

        using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Throws<XlsxReadException>(() => EpisodeWorkbookReader.Read(path));
    }

    [Fact]
    public void 파싱이_터지면_기억하지_않는다()
    {
        // 실패를 기억하면 고친 뒤에도 계속 터진다.
        string path = Path.Combine(_root, "깨진.xlsx");
        File.WriteAllText(path, "이건 워크북이 아니다");

        Assert.Throws<XlsxReadException>(() => EpisodeWorkbookReader.Read(path));

        File.Delete(path);
        CopySample("깨진.xlsx");

        Assert.NotEmpty(EpisodeWorkbookReader.Read(path).Rows);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <summary>견본은 챕터 시트와 대본 시트를 함께 갖고 있어 양쪽 리더에 쓴다.</summary>
    private string CopySample(string fileName)
    {
        string path = Path.Combine(_root, fileName);
        File.Copy(SamplePath, path, overwrite: true);
        return path;
    }

    private static GameDefinition Definition(params string[] variables) =>
        GameDefinition.Parse(
            "{ \"variables\": [" +
            string.Join(",", variables.Select(name =>
                $"{{ \"name\": \"{name}\", \"type\": \"int\" }}")) +
            "] }")!;

    private static void WriteFirstLine(string path, string speaker, string text)
    {
        using var workbook = new XLWorkbook(path);
        IXLWorksheet sheet = workbook.Worksheets
            .First(candidate => candidate.Cell(1, 1).GetString().Trim() == "인덱스");

        sheet.Cell(2, 5).SetValue(speaker);   // E · 화자
        sheet.Cell(2, 6).SetValue(text);      // F · 내용

        workbook.SaveAs(path);
    }
}
