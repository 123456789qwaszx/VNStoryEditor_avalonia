using ClosedXML.Excel;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>자리를 옮기되 격자는 그대로</b> (2026-08-24 소유자: "지금처럼 딱딱 맞는걸 유지하되,
/// 에피소드의 위치를 오른쪽 열로 옮기거나 아래쪽의 에피소드와 위치를 바꾸는 기능을
/// 추가해줄래?").
///
/// ⚠ <b>좌표를 사람에게 쥐여 주지 않는다.</b> 드래그를 되살리면 배치의 주인이 둘이 되고
/// (v3가 드래그를 없앤 이유가 그것이다), 흐름을 고칠 때마다 손으로 맞춘 자리가 어긋난다.
/// 대신 배치가 <b>읽는 두 값</b>을 고친다:
///
/// <list type="bullet">
///   <item>← → : `열보정` — 제 깊이에서 몇 열 오른쪽인가</item>
///   <item>↑ ↓ : <b>시트 행 순서</b> — 열 안 순서가 곧 시트 행 순서이므로 두 행을 맞바꾼다</item>
/// </list>
///
/// ↑↓에 새 칸이 필요 없다는 것이 이 설계의 값이다.
/// </summary>
public sealed class ChapterPlacementTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "vn-placement", Guid.NewGuid().ToString("N"));

    public ChapterPlacementTests() => Directory.CreateDirectory(_folder);

    // ⛔ 여기서 `WorkbookParseCache.Clear()`를 부르지 않는다. 그것은 <b>정적</b> 캐시라
    // 나란히 도는 다른 테스트 클래스의 것까지 지운다 — 실제로 그렇게 해서
    // `WorkbookParseCacheTests`를 전체 실행에서만 깨뜨렸다(혼자 돌리면 통과했다).
    // 지울 이유도 없다: 캐시 열쇠가 <b>내용 해시</b>라 파일을 쓰면 저절로 빗나가고,
    // 임시 폴더가 테스트마다 달라 서로 섞이지도 않는다.
    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    // ── 열보정이 배치에 얹힌다 ──────────────────────────────────────────────

    [Fact]
    public void 보정이_없으면_열은_깊이_그대로다()
    {
        // 기본값이 안 바뀌었다는 것부터 못 박는다 — 새 칸이 옛 판을 흔들면 안 된다.
        ChapterGraphModel model = Read(Chapter());

        Assert.Equal(
            new Dictionary<string, int> { ["a"] = 0, ["b"] = 1, ["c"] = 2 },
            ChapterBranchPlanner.Columns(model));
    }

    [Fact]
    public void 보정만큼_오른쪽으로_밀린다()
    {
        ChapterGraphModel model = Read(Chapter(nudgeForB: "2"));

        IReadOnlyDictionary<string, int> columns = ChapterBranchPlanner.Columns(model);

        Assert.Equal(3, columns["b"]);   // 깊이 1 + 보정 2
        Assert.Equal(0, columns["a"]);   // 이웃은 안 흔들린다
        Assert.Equal(2, columns["c"]);
    }

    [Fact]
    public void 밀려도_격자에_딱_맞는다()
    {
        // ⛔ 소유자가 요구한 것의 본체 — 옮겨도 자리는 여전히 열·줄의 정수배다.
        ChapterGraphModel model = Read(Chapter(nudgeForB: "2"));

        IReadOnlyDictionary<string, (double X, double Y)> layout =
            ChapterBranchPlanner.Layout(model);

        Assert.All(layout.Values, position =>
        {
            Assert.Equal(0, position.X % ChapterBranchPlanner.ColumnWidth);
            Assert.Equal(0, position.Y % ChapterBranchPlanner.RowHeight);
        });

        Assert.Equal(3 * ChapterBranchPlanner.ColumnWidth, layout["b"].X);
    }

    [Fact]
    public void 음수_보정은_0으로_읽고_말한다()
    {
        // ⛔ 제 깊이보다 왼쪽에 서면 부모가 자기 오른쪽에 놓여 간선이 뒤로 꺾인다 —
        //    이 그래프가 깊이 배치를 고른 유일한 이유가 그것이다.
        ChapterGraphModel model = Read(Chapter(nudgeForB: "-1"));

        Assert.Equal(1, ChapterBranchPlanner.Columns(model)["b"]);

        Assert.Contains(model.Diagnostics, item =>
            item.Severity == ChapterDiagnosticSeverity.Warning &&
            item.Message.Contains("음수라 0으로", StringComparison.Ordinal));
    }

    [Fact]
    public void 숫자가_아닌_보정도_0으로_읽고_말한다()
    {
        // 조용히 무시하면 "적었는데 안 먹는다"가 된다.
        ChapterGraphModel model = Read(Chapter(nudgeForB: "오른쪽"));

        Assert.Equal(1, ChapterBranchPlanner.Columns(model)["b"]);
        Assert.Contains(model.Diagnostics, item => item.Message.Contains("정수가 아니라"));
    }

    [Fact]
    public void 열보정_열이_아예_없는_옛_워크북도_그대로_읽힌다()
    {
        // 선택 열이라 이행이 필요 없다 — `도달불가 허용`과 같은 방식이다.
        ChapterGraphModel model = Read(Chapter(withNudgeColumn: false));

        Assert.Empty(model.Errors);
        Assert.Equal(1, ChapterBranchPlanner.Columns(model)["b"]);
    }

    // ── 갈 수 있는 방향 ─────────────────────────────────────────────────────

    [Fact]
    public void 제자리에서는_왼쪽이_없다()
    {
        ChapterBranchPlanner.ChapterMoves moves =
            ChapterBranchPlanner.Moves(Read(Chapter()), "b");

        Assert.False(moves.Left);
        Assert.True(moves.Right);
    }

    [Fact]
    public void 밀려_있으면_왼쪽이_생긴다()
    {
        ChapterBranchPlanner.ChapterMoves moves =
            ChapterBranchPlanner.Moves(Read(Chapter(nudgeForB: "1")), "b");

        Assert.True(moves.Left);
    }

    [Fact]
    public void 위아래는_같은_열의_이웃일_때만_있다()
    {
        // a → b, a → b2 : b와 b2가 같은 열(깊이 1)에 나란히 선다.
        ChapterGraphModel model = Read(Forked());

        ChapterBranchPlanner.ChapterMoves upper = ChapterBranchPlanner.Moves(model, "b");
        ChapterBranchPlanner.ChapterMoves lower = ChapterBranchPlanner.Moves(model, "b2");

        Assert.Null(upper.SwapUp);       // 맨 위
        Assert.Equal("b2", upper.SwapDown);
        Assert.Equal("b", lower.SwapUp);
        Assert.Null(lower.SwapDown);     // 맨 아래
    }

    [Fact]
    public void 옆으로_밀면_위아래_이웃이_사라진다()
    {
        // 같은 열이 아니게 되면 바꿀 상대도 없다 — 이웃은 <b>열 안</b>의 개념이다.
        ChapterGraphModel model = Read(Forked(nudgeForB2: "1"));

        Assert.Null(ChapterBranchPlanner.Moves(model, "b").SwapDown);
        Assert.Null(ChapterBranchPlanner.Moves(model, "b2").SwapUp);
    }

    [Fact]
    public void 고아는_못_옮긴다()
    {
        // 도달 불가 노드는 판 아래 한 줄에 서는데, 그 줄은 배치가 아니라 <b>보고</b>다.
        // 옮겨 정돈하면 "도달 불가"라는 ⚠가 흐려진다 — 잇는 것이 고치는 길이다.
        ChapterGraphModel model = Read(Chapter(extraOrphan: true));

        Assert.False(ChapterBranchPlanner.Moves(model, "orphan").Any);
    }

    // ── 쓰기 ────────────────────────────────────────────────────────────────

    [Fact]
    public void 보정을_쓰면_다시_읽어_그대로_나온다()
    {
        string path = Chapter();

        Assert.True(ChapterWorkbookWriter.SetColumnNudge(path, "b", 3).Written);

        Assert.Equal(4, ChapterBranchPlanner.Columns(Read(path))["b"]);   // 깊이 1 + 3
    }

    [Fact]
    public void 열보정_열이_없으면_만들어_쓴다()
    {
        // 옛 워크북에 처음 옮기는 순간 — 머리글까지 함께 선다.
        string path = Chapter(withNudgeColumn: false);

        Assert.True(ChapterWorkbookWriter.SetColumnNudge(path, "b", 1).Written);

        using var book = new XLWorkbook(path);
        IXLWorksheet sheet = book.Worksheet(ChapterSheetNames.Episodes);

        Assert.Contains(
            Enumerable.Range(1, 14).Select(column => sheet.Cell(1, column).GetString().Trim()),
            header => header == "열보정");

        Assert.Equal(2, ChapterBranchPlanner.Columns(Read(path))["b"]);
    }

    [Fact]
    public void 보정_0은_빈칸으로_돌아간다()
    {
        // 0을 적어 두면 시트를 보는 사람이 무슨 뜻인지 한 번 더 생각하게 된다.
        string path = Chapter(nudgeForB: "2");

        ChapterWorkbookWriter.SetColumnNudge(path, "b", 0);

        using var book = new XLWorkbook(path);
        IXLWorksheet sheet = book.Worksheet(ChapterSheetNames.Episodes);

        Assert.Equal(string.Empty, sheet.Cell(3, 8).GetString());
    }

    [Fact]
    public void 자리_바꾸기가_시트_행을_통째로_맞바꾼다()
    {
        // ⚠ 아는 칸만 옮기면 선택 열(도달불가 허용·열보정)이 제자리에 남아 두 에피소드의
        //    값이 섞인다. 그래서 쓰인 폭 전체를 옮긴다 — 여기서는 `열보정`이 증인이다.
        string path = Forked(nudgeForB2: "2");

        Assert.True(ChapterWorkbookWriter.SwapEpisodeRows(path, "b", "b2").Written);

        ChapterGraphModel model = Read(path);

        // 순서가 뒤집혔다.
        Assert.Equal(["a", "b2", "b", "c"], model.Episodes.Select(episode => episode.EpisodeId));

        // 그리고 보정은 <b>제 에피소드를 따라갔다</b> — 행에 남지 않았다.
        Assert.Equal(2, model.FindEpisode("b2")!.ColumnNudge);
        Assert.Equal(0, model.FindEpisode("b")!.ColumnNudge);
    }

    [Fact]
    public void 자리를_바꿔도_이야기는_한_글자도_안_바뀐다()
    {
        // 행 번호는 신원이 아니다 — 간선도 조건도 EpisodeId로 서로를 부른다.
        string path = Forked();
        ChapterGraphModel before = Read(path);

        ChapterWorkbookWriter.SwapEpisodeRows(path, "b", "b2");

        ChapterGraphModel after = Read(path);

        Assert.Equal(
            before.Edges.Select(edge => $"{edge.FromEpisodeId}→{edge.ToEpisodeId}").Order(),
            after.Edges.Select(edge => $"{edge.FromEpisodeId}→{edge.ToEpisodeId}").Order());

        Assert.Equal(before.Episodes.Count, after.Episodes.Count);
        Assert.Equal("갈래 하나", after.FindEpisode("b")!.Title);   // 제목도 따라갔다
    }

    [Fact]
    public void 같은_에피소드끼리는_못_바꾼다()
    {
        string path = Chapter();

        ChapterWriteResult result = ChapterWorkbookWriter.SwapEpisodeRows(path, "b", "b");

        Assert.False(result.Written);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <summary>캐시 열쇠가 내용 해시라 쓴 뒤 그냥 읽으면 새 값이 온다 — 비울 것이 없다.</summary>
    private static ChapterGraphModel Read(string path) => ChapterWorkbookReader.Read(path);

    /// <summary>a → b → c 한 줄. `열보정` 열은 여덟째 칸에 선다.</summary>
    private string Chapter(
        string? nudgeForB = null, bool withNudgeColumn = true, bool extraOrphan = false)
    {
        List<string?[]> episodes =
        [
            ["a", "시작", "Main", "장면_a", "0", "0", null, null],
            ["b", "가운데", "Main", "장면_b", "0", "0", null, nudgeForB],
            ["c", "끝", "Main", "장면_c", "0", "0", null, null]
        ];

        if (extraOrphan)
        {
            episodes.Add(["orphan", "홀로", "Main", "장면_o", "0", "0", null, null]);
        }

        return Write("straight.xlsx", withNudgeColumn, episodes,
        [
            ["a", "b", null, "계속"],
            ["b", "c", null, "계속"]
        ]);
    }

    /// <summary>a가 b·b2로 갈라지고 둘 다 c로 모인다 — b와 b2가 같은 열에 나란히 선다.</summary>
    private string Forked(string? nudgeForB2 = null) =>
        Write("forked.xlsx", withNudgeColumn: true,
        [
            ["a", "시작", "Main", "장면_a", "0", "0", null, null],
            ["b", "갈래 하나", "Main", "장면_b", "0", "0", null, null],
            ["b2", "갈래 둘", "Main", "장면_b2", "0", "0", null, nudgeForB2],
            ["c", "합류", "Main", "장면_c", "0", "0", null, null]
        ],
        [
            ["a", "b", null, "왼쪽"],
            ["a", "b2", null, "오른쪽"],
            ["b", "c", null, "계속"],
            ["b2", "c", null, "계속"]
        ]);

    private string Write(
        string fileName, bool withNudgeColumn, List<string?[]> episodes, string?[][] edges)
    {
        string path = Path.Combine(_folder, fileName);

        using var book = new XLWorkbook();

        string[] headers = withNudgeColumn
            ? ["EpisodeId", "제목", "종류", "대사엔트리", "X", "Y", "메모", "열보정"]
            : ["EpisodeId", "제목", "종류", "대사엔트리", "X", "Y", "메모"];

        Sheet(book, ChapterSheetNames.Episodes, headers,
            episodes.Select(row => row.Take(headers.Length).ToArray()).ToArray());

        Sheet(book, ChapterSheetNames.Edges,
            ["출발", "도착", "스탯변화", "선택지", "표시조건", "해금조건"], edges);

        Sheet(book, ChapterSheetNames.Conditions, ["라벨", "스탯", "연산자", "값", "설명"], []);
        Sheet(book, ChapterSheetNames.Stats, ["키", "이름", "초기", "최소", "최대"], []);
        Sheet(book, ChapterSheetNames.Choices, ["인덱스", "대본", "메모"], []);

        book.SaveAs(path);
        return path;
    }

    private static void Sheet(XLWorkbook book, string name, string[] headers, string?[][] rows)
    {
        IXLWorksheet sheet = book.AddWorksheet(name);

        for (int column = 0; column < headers.Length; column++)
        {
            sheet.Cell(1, column + 1).SetValue(headers[column]);
        }

        for (int row = 0; row < rows.Length; row++)
        {
            for (int column = 0; column < rows[row].Length; column++)
            {
                if (rows[row][column] is { Length: > 0 } value)
                {
                    sheet.Cell(row + 2, column + 1).SetValue(value);
                }
            }
        }
    }
}
