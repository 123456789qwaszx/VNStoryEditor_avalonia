using ClosedXML.Excel;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// R-B — 대본 워크북 이미터 (<c>docs/work-orders/tool-owns-workbooks-orders.md</c>).
///
/// <b>이 묶음이 지키는 것은 왕복이다.</b> 이미터가 낸 파일을 <see cref="EpisodeWorkbookReader"/>가
/// 오류 없이 읽고 같은 줄을 돌려주는지 — 그것이 R-B의 합격 기준이자, 뒤집기 전 구간의
/// 안전망이다(지시서 §9: <i>"R-D를 지나면 워크북에서 다시 임포트하는 길이 닫힌다"</i>).
/// 리더가 이미터의 산출물을 못 읽으면 그 안전망이 R-B에서 이미 끊긴 것이다.
/// </summary>
public sealed class EpisodeWorkbookEmitterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-episode-emitter-tests", Guid.NewGuid().ToString("N"));

    public EpisodeWorkbookEmitterTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string Path0(string name) => Path.Combine(_directory, name);

    private static IReadOnlyList<EmittedEpisodeLine> Sample =>
    [
        new(10, "윌로", "복도는 조용했다."),
        new(20, null, "멀리서 문이 닫히는 소리."),
        new(30, "라루", "왜 그런 표정이야?")
    ];

    // ── 왕복 ────────────────────────────────────────────────────────────────

    [Fact]
    public void 낸_워크북을_리더가_오류_없이_읽는다()
    {
        string path = Path0("main05.02.xlsx");

        ChapterWriteResult result = EpisodeWorkbookEmitter.Emit(path, Sample);

        Assert.True(result.Written, result.Failure);
        Assert.True(File.Exists(path));

        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(path);

        // 머리글 네 칸을 규격 그대로 내는 이유가 이것이다 — 하나라도 어긋나면 리더가
        // 시트를 못 찾고, 그 순간 임포트라는 되돌림 경로가 끊긴다.
        List<ChapterDiagnostic> errors = model.Diagnostics
            .Where(item => item.Severity == ChapterDiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0, string.Join(" / ", errors.Select(item => item.Message)));
    }

    [Fact]
    public void 화자와_대사가_그대로_돌아온다()
    {
        string path = Path0("main05.02.xlsx");
        EpisodeWorkbookEmitter.Emit(path, Sample);

        List<EpisodeRow> lines = EpisodeWorkbookReader.Read(path).Rows
            .Where(row => row.IsLine && !row.IsBlank)
            .OrderBy(row => row.Index)
            .ToList();

        Assert.Equal(3, lines.Count);

        Assert.Equal(10, lines[0].Index);
        Assert.Equal("윌로", lines[0].Speaker);
        Assert.Equal("복도는 조용했다.", lines[0].Text);

        // 빈 화자는 지문이다 — 오류가 아니고, 빈 칸으로 돌아와야 한다.
        Assert.Equal(20, lines[1].Index);
        Assert.Equal(string.Empty, lines[1].Speaker);
        Assert.Equal("멀리서 문이 닫히는 소리.", lines[1].Text);

        Assert.Equal(30, lines[2].Index);
        Assert.Equal("라루", lines[2].Speaker);
    }

    [Fact]
    public void 낸_파일은_v15_네_칸이다()
    {
        // 지시서 §3 — 대본 층의 조건은 폐지됐다. 이미터가 `유형`·`조건라벨`을 다시 내면
        // 이행기가 그 파일을 구판으로 보고 되돌려 깎는다(이미터↔리더 규격 드리프트).
        string path = Path0("main05.02.xlsx");
        EpisodeWorkbookEmitter.Emit(path, Sample);

        using var book = new ClosedXML.Excel.XLWorkbook(path);
        IXLWorksheet sheet = book.Worksheets.First();

        // ⚠ 머리글이 2행이다 (§4.4 · 2026-09-16) — 1행은 "이 파일은 산출물입니다" 안내문이고,
        //    리더는 행 번호를 상수로 들지 않고 `WorkbookOutputNotice.HeaderRowOf`로 찾는다.
        Assert.StartsWith("⚠ 이 파일은 VnTool", sheet.Cell(1, 1).GetString());

        Assert.Equal(
            ["인덱스", "LineId", "화자", "내용"],
            Enumerable.Range(1, 4).Select(column => sheet.Cell(2, column).GetString()));

        Assert.Equal(string.Empty, sheet.Cell(1, 5).GetString());
    }

    // ── 번호 매기기 ─────────────────────────────────────────────────────────

    [Fact]
    public void 순서만_주면_십_단위로_번호를_매긴다()
    {
        IReadOnlyList<EmittedEpisodeLine> numbered = EpisodeWorkbookEmitter.Number(
            new (string? Speaker, string Text)[]
            {
                ("윌로", "하나"),
                (null, "둘")
            });

        // 사이에 끼울 자리를 남긴다 (G-5).
        Assert.Equal(new[] { 10, 20 }, numbered.Select(line => line.Index).ToArray());
    }

    // ── 갈아 끼우기 ─────────────────────────────────────────────────────────

    [Fact]
    public void 다시_내면_통째로_갈리고_옛_줄이_남지_않는다()
    {
        string path = Path0("main05.02.xlsx");
        EpisodeWorkbookEmitter.Emit(path, Sample);

        EpisodeWorkbookEmitter.Emit(path, [new EmittedEpisodeLine(10, "윌로", "다시 쓴 한 줄.")]);

        List<EpisodeRow> lines = EpisodeWorkbookReader.Read(path).Rows
            .Where(row => row.IsLine && !row.IsBlank)
            .ToList();

        // 전체를 새로 쓰므로 행 삭제라는 개념이 없다 — 옛 2·3번 줄은 흔적도 없어야 한다.
        EpisodeRow only = Assert.Single(lines);
        Assert.Equal("다시 쓴 한 줄.", only.Text);
    }

    [Fact]
    public void 이미_있던_파일은_bak으로_남는다()
    {
        string path = Path0("main05.02.xlsx");
        EpisodeWorkbookEmitter.Emit(path, Sample);

        Assert.False(File.Exists(path + ".bak")); // 첫 저장에는 되돌릴 자리가 없다

        EpisodeWorkbookEmitter.Emit(path, [new EmittedEpisodeLine(10, "윌로", "두 번째.")]);

        Assert.True(File.Exists(path + ".bak"));

        // .bak은 <b>직전 내용</b>이어야 한다 — 되돌릴 자리가 방금 것이면 쓸모가 없다.
        EpisodeWorkbookModel previous = EpisodeWorkbookReader.Read(path + ".bak");
        Assert.Equal(
            3,
            previous.Rows.Count(row => row.IsLine && !row.IsBlank));
    }

    [Fact]
    public void 임시_파일을_남기지_않는다()
    {
        string path = Path0("main05.02.xlsx");

        // ⚠ 성공을 먼저 건다 — Emit이 실패해도 임시 파일은 치워지므로, 잔류만 보면
        //   "아무것도 안 낸" 실패가 통과로 지나간다(실제로 첫 판에 그렇게 지나갔다).
        ChapterWriteResult result = EpisodeWorkbookEmitter.Emit(path, Sample);
        Assert.True(result.Written, result.Failure);

        // 원자적 교체의 흔적이 폴더에 남으면 다음 열거가 그것을 대본으로 센다.
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
        Assert.Single(Directory.EnumerateFiles(_directory, "*.xlsx"));
    }
}
