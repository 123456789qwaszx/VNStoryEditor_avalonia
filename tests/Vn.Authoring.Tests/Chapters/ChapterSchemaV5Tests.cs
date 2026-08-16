using ClosedXML.Excel;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 2026-08-16 소유자 개정 — 시트 규격 v5의 고정.
///
/// ① 간선: 출발|도착|스탯변화(C)|선택지(D)|조건|잠금시 숨김|잠금 안내문
/// ② 에피소드: 인덱스 열 폐지 ③ 조건: 라벨|스탯|연산자|값|설명 (+원문 탈출구)
/// ④ 스탯: 타입(int/bool) ⑤ 연산자 &lt; &gt; 개방 ⑥ 픽스처 시트는 새 챕터에 없다
/// ⑦ 구판 워크북은 Migrator가 통째로 이행한다(.bak).
/// </summary>
public sealed class ChapterSchemaV5Tests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-schema-v5", Guid.NewGuid().ToString("N"));

    public ChapterSchemaV5Tests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    // ── 새 챕터 템플릿 ──────────────────────────────────────────────────────

    [Fact]
    public void 새_챕터는_새_규격의_시트와_드롭다운을_갖고_픽스처는_없다()
    {
        ChapterWorkbookWriter.EnsureChapterWorkbook(_directory, "ch_v5", [("trust", "신뢰")]);

        using var workbook = new XLWorkbook(Path.Combine(_directory, "ch_v5.xlsx"));

        Assert.DoesNotContain(workbook.Worksheets, sheet =>
            sheet.Name == ChapterSheetNames.Fixtures); // 임시 제거 (2026-08-16)

        IXLWorksheet edges = workbook.Worksheet(ChapterSheetNames.Edges);
        Assert.Equal("스탯변화", edges.Cell(1, 3).GetString());
        Assert.Equal("선택지수", edges.Cell(1, 4).GetString()); // v6 — 문구는 선택지 시트로
        Assert.Contains("1", edges.Cell(2, 4).GetDataValidation().Value);

        // 선택지 시트 (v6) — 칸의 출발·도착은 에피소드 드롭다운.
        IXLWorksheet choices = workbook.Worksheet(ChapterSheetNames.Choices);
        Assert.Equal("대본", choices.Cell(1, 4).GetString());
        Assert.Contains(ChapterSheetNames.Episodes, choices.Cell(2, 1).GetDataValidation().Value);

        IXLWorksheet episodes = workbook.Worksheet(ChapterSheetNames.Episodes);
        Assert.Equal("종류", episodes.Cell(1, 3).GetString()); // 인덱스가 없다

        // 조건 시트 — 스탯은 스탯 시트를 가리키는 드롭다운, 연산자는 목록.
        IXLWorksheet conditions = workbook.Worksheet(ChapterSheetNames.Conditions);
        IXLDataValidation statPick = conditions.Cell(2, 2).GetDataValidation();
        Assert.Contains(ChapterSheetNames.Stats, statPick.Value);
        IXLDataValidation operatorPick = conditions.Cell(2, 3).GetDataValidation();
        Assert.Contains("true", operatorPick.Value);
        Assert.Contains(">=", operatorPick.Value);

        // bool이면 값 칸이 회색 — 조건부 서식이 걸려 있다.
        Assert.NotEmpty(conditions.ConditionalFormats);

        // 스탯 시트 — 타입 드롭다운 int/bool.
        IXLWorksheet stats = workbook.Worksheet(ChapterSheetNames.Stats);
        Assert.Equal("타입", stats.Cell(1, 6).GetString());
        Assert.Contains("bool", stats.Cell(2, 6).GetDataValidation().Value);
    }

    // ── 조건의 구조화 열 ────────────────────────────────────────────────────

    private string WriteChapter(
        string?[][]? conditionRows = null,
        string?[][]? statRows = null,
        string?[][]? edgeRows = null)
    {
        var conditions = new List<string?[]> { new string?[] { "라벨", "스탯", "연산자", "값", "설명" } };
        conditions.AddRange(conditionRows ?? []);

        var stats = new List<string?[]> { new string?[] { "스탯키", "표시명", "초기값", "최소", "최대", "타입" } };
        stats.AddRange(statRows ?? new[]
        {
            new string?[] { "trust", "신뢰", "0", "0", "10", null },
            new string?[] { "anger", "분노", "0", "0", "10", null }
        });

        var edges = new List<string?[]>
        {
            new string?[] { "출발", "도착", "스탯변화", "선택지수", "조건", "잠금시 숨김", "잠금 안내문" },
            new string?[] { "ep1", "ep2", null, null, null, "FALSE", null }
        };

        if (edgeRows is not null)
        {
            edges.RemoveAt(1);
            edges.AddRange(edgeRows);
        }

        return XlsxTestWorkbook.Write(_directory, "structured.xlsx",
            ("에피소드", new[]
            {
                new string?[] { "EpisodeId", "제목", "종류", "대사엔트리", "X", "Y", "표시조건", "해금조건", "엔딩키", "메모" },
                new string?[] { "ep1", null, "Main", "Story_ep1", "0", "0", null, null, null, null },
                new string?[] { "ep2", null, "Main", "Story_ep2", "200", "0", null, null, null, null }
            }),
            ("간선", edges.ToArray()),
            ("조건", conditions.ToArray()),
            ("스탯", stats.ToArray()));
    }

    [Fact]
    public void 스탯_연산자_값_세_칸이_조건식으로_조립된다()
    {
        ChapterGraphModel model = ChapterWorkbookReader.Read(WriteChapter(
        [
            ["신뢰높음", "trust", ">=", "3", null],
            ["분노초과", "anger", ">", "5", null],
            ["신뢰미달", "trust", "<", "2", null]
        ]));

        Assert.False(model.HasErrors);
        Assert.Equal("trust >= 3", model.FindCondition("신뢰높음")!.Expression);

        ConditionTerm above = Assert.Single(model.FindCondition("분노초과")!.Parsed);
        Assert.Equal(ConditionComparison.Above, above.Comparison);

        ConditionTerm below = Assert.Single(model.FindCondition("신뢰미달")!.Parsed);
        Assert.Equal(ConditionComparison.Below, below.Comparison);
    }

    [Fact]
    public void 연산자와_값이_비면_스탯_칸이_원문_조건식이다()
    {
        ChapterGraphModel model = ChapterWorkbookReader.Read(WriteChapter(
        [
            ["복도완료", "cleared:ep1", null, null, null],
            ["지쳐있음", "trust >= 4; anger <= 2", null, null, null]
        ]));

        Assert.False(model.HasErrors);
        Assert.Equal(ConditionTermKind.EpisodeCleared,
            Assert.Single(model.FindCondition("복도완료")!.Parsed).Kind);
        Assert.Equal(2, model.FindCondition("지쳐있음")!.Parsed.Count);
    }

    // ── bool 스탯 ───────────────────────────────────────────────────────────

    [Fact]
    public void bool_스탯은_경계가_0과_1이고_true_false_조건이_된다()
    {
        ChapterGraphModel model = ChapterWorkbookReader.Read(WriteChapter(
            conditionRows: [["프롤로그봄", "seen", "true", null, null]],
            statRows:
            [
                ["trust", "신뢰", "0", "0", "10", null],
                ["seen", "프롤로그", "FALSE", null, null, "bool"]
            ]));

        Assert.False(model.HasErrors);

        ChapterStat seen = model.Stats.Single(stat => stat.Key == "seen");
        Assert.Equal(ChapterStatType.Bool, seen.Type);
        Assert.Equal(0, seen.Minimum);
        Assert.Equal(1, seen.Maximum);
        Assert.Equal(0, seen.Initial);

        ChapterCondition condition = model.FindCondition("프롤로그봄")!;
        Assert.Equal("seen == true", condition.Expression);
        ConditionTerm term = Assert.Single(condition.Parsed);
        Assert.Equal(ConditionComparison.Exactly, term.Comparison);
        Assert.Equal(1, term.Value);
    }

    [Fact]
    public void bool_스탯에_크기_비교나_증감을_쓰면_오류다()
    {
        ChapterGraphModel model = ChapterWorkbookReader.Read(WriteChapter(
            conditionRows: [["이상한조건", "seen", ">=", "1", null]],
            statRows:
            [
                ["trust", "신뢰", "0", "0", "10", null],
                ["seen", "프롤로그", "FALSE", null, null, "bool"]
            ],
            edgeRows: [["ep1", "ep2", "seen +1", null, null, "FALSE", null]]));

        Assert.Contains(model.Errors, item =>
            item.Sheet == ChapterSheetNames.Conditions && item.Message.Contains("bool 스탯"));
        Assert.Contains(model.Errors, item =>
            item.Sheet == ChapterSheetNames.Edges && item.Message.Contains("증감"));
    }

    // ── 간선 새 열 순서 ─────────────────────────────────────────────────────

    [Fact]
    public void 간선의_스탯변화는_C열에서_읽힌다()
    {
        ChapterGraphModel model = ChapterWorkbookReader.Read(WriteChapter(
            edgeRows: [["ep1", "ep2", "trust +2", "2", null, "FALSE", null]]));

        Assert.False(model.HasErrors);
        ChapterEdge edge = Assert.Single(model.Edges);
        Assert.Equal(2, edge.ChoiceCount);
        StatDelta delta = Assert.Single(edge.StatChanges);
        Assert.Equal("trust", delta.Key);
        Assert.Equal(2, delta.Amount);
    }

    // ── 툴 패널의 조건 저장 ─────────────────────────────────────────────────

    [Fact]
    public void 조건_저장은_식을_세_칸으로_분해해_쓴다()
    {
        string path = WriteChapter();

        Assert.True(ChapterWorkbookWriter.AddCondition(path, "신뢰높음", "trust >= 3").Written);
        Assert.True(ChapterWorkbookWriter.AddCondition(path, "복도완료", "cleared:ep1").Written);

        using (var workbook = new XLWorkbook(path))
        {
            IXLWorksheet sheet = workbook.Worksheet(ChapterSheetNames.Conditions);
            Assert.Equal("trust", sheet.Cell(2, 2).GetString());
            Assert.Equal(">=", sheet.Cell(2, 3).GetString());
            Assert.Equal("3", sheet.Cell(2, 4).GetString());
            Assert.Equal("cleared:ep1", sheet.Cell(3, 2).GetString()); // 원문 탈출구
            Assert.Equal(string.Empty, sheet.Cell(3, 3).GetString());
        }

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);
        Assert.Equal("trust >= 3", model.FindCondition("신뢰높음")!.Expression);
        Assert.Equal("cleared:ep1", model.FindCondition("복도완료")!.Expression);
    }

    // ── 구판 이행 ───────────────────────────────────────────────────────────

    private string WriteLegacyChapter()
    {
        return XlsxTestWorkbook.Write(_directory, "legacy.xlsx",
            ("에피소드", new[]
            {
                new string?[] { "EpisodeId", "제목", "인덱스", "종류", "대사엔트리", "X", "Y", "표시조건", "해금조건", "엔딩키", "메모" },
                new string?[] { "ep1", "첫 화", "01", "Main", "Story_ep1", "0", "0", null, "신뢰높음", null, "메모다" }
            }),
            ("간선", new[]
            {
                new string?[] { "출발", "도착", "선택지 라벨", "조건", "잠금시 숨김", "잠금 안내문", "스탯변화" },
                new string?[] { "ep1", "ep1", "돈다", "신뢰높음", "FALSE", "잠김", "trust +1" }
            }),
            ("조건", new[]
            {
                new string?[] { "라벨", "조건식", "설명" },
                new string?[] { "신뢰높음", "trust >= 3", "설명이다" },
                new string?[] { "복도완료", "cleared:ep1", null }
            }),
            ("스탯", new[]
            {
                new string?[] { "스탯키", "표시명", "초기값", "최소", "최대" },
                new string?[] { "trust", "신뢰", "0", "0", "10" },
                new string?[] { "anger", "분노", "0", "0", "10" }
            }),
            ("픽스처", new[] { new string?[] { "픽스처명", "활성", "고정 선택 (에피소드ID→도착ID)" } }));
    }

    [Fact]
    public void 구판_워크북이_한_번에_이행되고_다시는_손대지_않는다()
    {
        string path = WriteLegacyChapter();

        ChapterWorkbookMigrator.MigrationResult first = ChapterWorkbookMigrator.Migrate(path);
        Assert.True(first.Migrated);
        Assert.True(File.Exists(path + ".bak")); // 이전 상태가 남는다

        // 값이 새 자리로 옮겨져 그대로 읽힌다.
        ChapterGraphModel model = ChapterWorkbookReader.Read(path);
        Assert.False(model.HasErrors);

        ChapterEpisode episode = Assert.Single(model.Episodes);
        Assert.Equal("첫 화", episode.Title);
        Assert.Equal("Story_ep1", episode.DialogueEntry);
        Assert.Equal("신뢰높음", episode.UnlockConditionLabel);
        Assert.Equal("메모다", episode.Memo);

        ChapterEdge edge = Assert.Single(model.Edges);
        Assert.Equal("돈다", edge.OptionLabel);
        Assert.Equal("신뢰높음", edge.ConditionLabel);
        Assert.Equal("잠김", edge.LockedMessage);
        Assert.Equal(1, Assert.Single(edge.StatChanges).Amount);

        Assert.Equal("trust >= 3", model.FindCondition("신뢰높음")!.Expression);
        Assert.Equal("cleared:ep1", model.FindCondition("복도완료")!.Expression);

        // 빈 픽스처 시트는 사라진다(임시 제거). 데이터 파기가 아니다 — 머리글뿐이었다.
        using (var workbook = new XLWorkbook(path))
        {
            Assert.DoesNotContain(workbook.Worksheets, sheet =>
                sheet.Name == ChapterSheetNames.Fixtures);
            Assert.Equal("타입", workbook.Worksheet(ChapterSheetNames.Stats).Cell(1, 6).GetString());
        }

        // 두 번째 부름은 파일에 손대지 않는다 — 폴더 감시가 맴돌면 안 된다.
        DateTime before = File.GetLastWriteTimeUtc(path);
        ChapterWorkbookMigrator.MigrationResult second = ChapterWorkbookMigrator.Migrate(path);
        Assert.False(second.Migrated);
        Assert.Null(second.Failure);
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void 데이터가_있는_픽스처_시트는_이행이_지우지_않는다()
    {
        string path = XlsxTestWorkbook.Write(_directory, "legacy_fixture.xlsx",
            ("에피소드", new[]
            {
                new string?[] { "EpisodeId", "제목", "인덱스", "종류", "대사엔트리", "X", "Y" },
                new string?[] { "ep1", null, "01", "Main", "Story_ep1", "0", "0" }
            }),
            ("간선", new[] { new string?[] { "출발", "도착", "선택지 라벨", "조건", "잠금시 숨김", "잠금 안내문" } }),
            ("조건", new[] { new string?[] { "라벨", "조건식", "설명" } }),
            ("스탯", new[]
            {
                new string?[] { "스탯키", "표시명", "초기값", "최소", "최대" },
                new string?[] { "trust", "신뢰", "0", "0", "10" },
                new string?[] { "anger", "분노", "0", "0", "10" }
            }),
            ("픽스처", new[]
            {
                new string?[] { "픽스처명", "활성", "trust", "고정 선택 (에피소드ID→도착ID)" },
                new string?[] { "기본", "TRUE", "0", null }
            }));

        Assert.True(ChapterWorkbookMigrator.Migrate(path).Migrated);

        using var workbook = new XLWorkbook(path);
        Assert.Contains(workbook.Worksheets, sheet => sheet.Name == ChapterSheetNames.Fixtures);
    }
}
