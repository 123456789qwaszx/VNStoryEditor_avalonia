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
        Assert.Equal("선택지", edges.Cell(1, 4).GetString()); // v9 — 문구 그 자체
        // 드롭다운은 사전의 대본 열(B)을 통째로 가리킨다 — 어느 에피소드에서든 다 고른다.
        Assert.Contains($"{ChapterSheetNames.Choices}'!$B$2", edges.Cell(2, 4).GetDataValidation().Value);

        // 선택지 시트 (v9) — 인덱스·대본·메모. 출발이 없다: 챕터 전체의 문구 사전이다.
        IXLWorksheet choices = workbook.Worksheet(ChapterSheetNames.Choices);
        Assert.Equal("인덱스", choices.Cell(1, 1).GetString());
        Assert.Equal("대본", choices.Cell(1, 2).GetString());
        Assert.Equal("메모", choices.Cell(1, 3).GetString());

        // v14 (2026-08-26) — 신원·내용이 앞, 건네는 열쇠가 가운데, 좌표와 곁말이 뒤.
        IXLWorksheet episodes = workbook.Worksheet(ChapterSheetNames.Episodes);
        Assert.Equal(
            ["EpisodeId", "대사엔트리", "제목", "이벤트키", "X", "Y", "메모"],
            Enumerable.Range(1, 7).Select(column => episodes.Cell(1, column).GetString()));
        Assert.Equal(string.Empty, episodes.Cell(1, 8).GetString()); // 인덱스도 종류도 선택지수도 없다

        // 조건 시트 — 스탯은 스탯 시트를 가리키는 드롭다운, 연산자는 목록.
        IXLWorksheet conditions = workbook.Worksheet(ChapterSheetNames.Conditions);
        IXLDataValidation statPick = conditions.Cell(2, 2).GetDataValidation();
        Assert.Contains(ChapterSheetNames.Stats, statPick.Value);
        IXLDataValidation operatorPick = conditions.Cell(2, 3).GetDataValidation();
        Assert.Contains("true", operatorPick.Value);
        Assert.Contains(">=", operatorPick.Value);

        // bool이면 값 칸이 회색 — 조건부 서식이 걸려 있다.
        Assert.NotEmpty(conditions.ConditionalFormats);

        // 스탯 시트 (v14) — `타입`이 맨 앞이고 그 칸이 int/bool 드롭다운이다.
        IXLWorksheet stats = workbook.Worksheet(ChapterSheetNames.Stats);
        Assert.Equal(
            ["타입", "스탯키", "표시명", "초기값", "최소", "최대"],
            Enumerable.Range(1, 6).Select(column => stats.Cell(1, column).GetString()));
        Assert.Contains("bool", stats.Cell(2, 1).GetDataValidation().Value);

        // ⚠ 조건 시트의 스탯 드롭다운은 <b>스탯키 열(B)</b>을 가리켜야 한다 — v14에서
        //    A는 타입이 됐다. 안 따라가면 목록이 int·bool을 스탯키라고 내민다.
        Assert.Contains("$B$2", statPick.Value);
    }

    // ── 조건의 구조화 열 ────────────────────────────────────────────────────

    private string WriteChapter(
        string?[][]? conditionRows = null,
        string?[][]? statRows = null,
        string?[][]? edgeRows = null)
    {
        var conditions = new List<string?[]> { new string?[] { "라벨", "스탯", "연산자", "값", "설명" } };
        conditions.AddRange(conditionRows ?? []);

        var stats = new List<string?[]> { new string?[] { "타입", "스탯키", "표시명", "초기값", "최소", "최대" } };
        stats.AddRange(statRows ?? new[]
        {
            new string?[] { null, "trust", "신뢰", "0", "0", "10" },
            new string?[] { null, "anger", "분노", "0", "0", "10" }
        });

        var edges = new List<string?[]>
        {
            new string?[] { "출발", "도착", "스탯변화", "선택지", "표시조건", "해금조건", "잠금시 숨김", "잠금 안내문" },
            new string?[] { "ep1", "ep2", null, "계속", null, null, "FALSE", null }
        };

        if (edgeRows is not null)
        {
            edges.RemoveAt(1);
            edges.AddRange(edgeRows);
        }

        return XlsxTestWorkbook.Write(_directory, "structured.xlsx",
            ("에피소드", new[]
            {
                new string?[] { "EpisodeId", "대사엔트리", "제목", "이벤트키", "X", "Y", "메모" },
                new string?[] { "ep1", "Story_ep1", null, null, "0", "0", null, null, null },
                new string?[] { "ep2", "Story_ep2", null, null, "200", "0", null, null, null }
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
            ["지쳐있음", "trust >= 4; anger <= 2", null, null, null]
        ]));

        Assert.False(model.HasErrors);
        Assert.Equal(2, model.FindCondition("지쳐있음")!.Parsed.Count);
    }

    [Fact]
    public void 폐지된_cleared_는_고치는_법까지_말하며_거부한다()
    {
        // 옛 워크북이 열리는 자리다. 조용히 무시하면 관문이 통째로 사라져 잠긴 길이 열린다 —
        // 짚되, 무엇으로 바꿔야 하는지까지 말한다.
        ChapterGraphModel model = ChapterWorkbookReader.Read(WriteChapter(
        [
            ["복도완료", "cleared:ep1", null, null, null]
        ]));

        Assert.True(model.HasErrors);
        Assert.False(model.FindCondition("복도완료")!.IsValid);

        string message = Assert
            .Single(model.Diagnostics, item => item.Message.Contains("cleared:"))
            .Message;

        Assert.Contains("폐지", message);
        Assert.Contains("cleared_ep1", message);   // 깃발 이름 제안
        Assert.Contains("스탯변화", message);        // 켜는 자리까지 짚는다
    }

    // ── bool 스탯 ───────────────────────────────────────────────────────────

    [Fact]
    public void bool_스탯은_경계가_0과_1이고_true_false_조건이_된다()
    {
        ChapterGraphModel model = ChapterWorkbookReader.Read(WriteChapter(
            conditionRows: [["프롤로그봄", "seen", "true", null, null]],
            statRows:
            [
                [null, "trust", "신뢰", "0", "0", "10"],
                ["bool", "seen", "프롤로그", "FALSE", null, null]
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
                [null, "trust", "신뢰", "0", "0", "10"],
                ["bool", "seen", "프롤로그", "FALSE", null, null]
            ],
            edgeRows: [["ep1", "ep2", "seen +1", "계속", null, null, "FALSE", null]]));

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
            edgeRows: [["ep1", "ep2", "trust +2", "왼쪽으로", null, null, "FALSE", null]]));

        Assert.False(model.HasErrors);
        ChapterEdge edge = Assert.Single(model.Edges);
        Assert.Equal("왼쪽으로", edge.OptionLabel); // D열은 문구 그 자체 (v9)
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

        // 세 칸으로 못 쪼개는 식은 스탯 칸에 원문 그대로 간다. `cleared:`가 폐지된
        // 2026-08-25 뒤로 그 탈출구에 남은 것은 <b>복합식</b>이다.
        Assert.True(ChapterWorkbookWriter.AddCondition(path, "지쳐있음", "trust >= 4; anger <= 2").Written);

        using (var workbook = new XLWorkbook(path))
        {
            IXLWorksheet sheet = workbook.Worksheet(ChapterSheetNames.Conditions);
            Assert.Equal("trust", sheet.Cell(2, 2).GetString());
            Assert.Equal(">=", sheet.Cell(2, 3).GetString());
            Assert.Equal("3", sheet.Cell(2, 4).GetString());
            Assert.Equal("trust >= 4; anger <= 2", sheet.Cell(3, 2).GetString()); // 원문 탈출구
            Assert.Equal(string.Empty, sheet.Cell(3, 3).GetString());
        }

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);
        Assert.Equal("trust >= 3", model.FindCondition("신뢰높음")!.Expression);
        Assert.Equal("trust >= 4; anger <= 2", model.FindCondition("지쳐있음")!.Expression);
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

    // ── v9: 선택지 시트는 전역 문구 사전, 간선이 문구를 직접 적는다 ───────────

    [Fact]
    public void 선택지_시트는_인덱스와_대본뿐이고_간선이_문구를_직접_적는다()
    {
        string path = XlsxTestWorkbook.Write(_directory, "v9.xlsx",
            ("에피소드", new[]
            {
                new string?[] { "EpisodeId", "대사엔트리", "제목", "이벤트키", "X", "Y", "메모" },
                new string?[] { "ep1", "Story_ep1", null, null, "0", "0", null },
                new string?[] { "ep2", "Story_ep2", null, null, "200", "0", null }
            }),
            ("간선", new[]
            {
                new string?[] { "출발", "도착", "스탯변화", "선택지", "표시조건", "해금조건", "잠금시 숨김", "잠금 안내문" },
                new string?[] { "ep1", "ep2", null, "왼쪽으로", null, null, "FALSE", null },
                new string?[] { "ep1", "ep2", null, "계속", null, null, "FALSE", null }, // 보이지 않는 기본
                new string?[] { "ep2", "ep1", null, "왼쪽으로", null, null, "FALSE", null } // 같은 문구 재사용
            }),
            ("조건", new[] { new string?[] { "라벨", "스탯", "연산자", "값", "설명" } }),
            ("스탯", new[]
            {
                new string?[] { "타입", "스탯키", "표시명", "초기값", "최소", "최대" },
                new string?[] { null, "trust", "신뢰", "0", "0", "10" },
                new string?[] { null, "anger", "분노", "0", "0", "10" }
            }),
            ("선택지", new[]
            {
                new string?[] { "인덱스", "대본", "메모" },
                new string?[] { "10", "왼쪽으로", null },
                new string?[] { "20", "오른쪽으로", "아직 아무 길도 안 쓴다" }
            }));

        ChapterGraphModel model = ChapterWorkbookReader.Read(path);

        Assert.False(model.HasErrors);

        // 문구는 간선의 D열 값 그대로다 — 파생도 참조도 아니다.
        Assert.Equal(
            ["왼쪽으로", "계속"],
            model.Edges
                .Where(edge => edge.FromEpisodeId == "ep1")
                .Select(edge => edge.OptionLabel));

        // v12 — 문구 없는 길은 없다.
        Assert.DoesNotContain(model.Edges, edge => edge.HasNoOptionLabel);

        // 사전은 챕터 전체의 것이다 — 안 쓰는 낱말이 있어도, 같은 낱말을 여러 길이 써도 좋다.
        // ⚠ `계속`은 간선이 썼지만 사전에는 없다: 사전은 사람이 적는 어휘집이고, 리더가
        // 거기 없는 문구를 올려 주지 않는다.
        Assert.Equal(["왼쪽으로", "오른쪽으로"], model.ChoiceOptions.Select(option => option.Text));
        Assert.Equal(2, model.Edges.Count(edge => edge.OptionLabel == "왼쪽으로"));

        // 순서는 `간선` 시트의 행 순서 — 적은 순서가 곧 화면에 뜨는 순서다.
        string json = ChapterProgressionExporter.Export(model, episodesFolder: null).Json!;
        Assert.True(
            json.IndexOf("왼쪽으로", StringComparison.Ordinal) <
            json.IndexOf("계속", StringComparison.Ordinal),
            "먼저 적은 행(왼쪽으로)이 뒤 행(계속)보다 먼저 나가야 한다");
    }


    [Fact]
    public void 구판_워크북이_한_번에_이행되고_다시는_손대지_않는다()
    {
        string path = WriteLegacyChapter();

        ChapterWorkbookMigrator.MigrationResult first = ChapterWorkbookMigrator.Migrate(path);
        Assert.True(first.Migrated);
        Assert.True(File.Exists(path + ".bak")); // 이전 상태가 남는다

        // 값이 새 자리로 옮겨져 그대로 읽힌다.
        //
        // ⚠ 이 구판 워크북에는 `복도완료`(`cleared:ep1`)가 들어 있다 — 2026-08-25에 폐지된
        // 문법이라 이행 뒤에도 <b>오류로 남는다</b>. 그것이 맞다: 이행기는 칸의 자리를
        // 옮기지 조건식의 뜻을 바꾸지 않는다(식은 사람 소유). 아래에서 그 오류가 안내와
        // 함께 서 있는지도 함께 건다.
        ChapterGraphModel model = ChapterWorkbookReader.Read(path);

        ChapterEpisode episode = Assert.Single(model.Episodes);
        Assert.Equal("첫 화", episode.Title);
        Assert.Equal("Story_ep1", episode.DialogueEntry);
        Assert.Equal("메모다", episode.Memo);

        ChapterEdge edge = Assert.Single(model.Edges);
        Assert.Equal("돈다", edge.OptionLabel);
        Assert.Equal("신뢰높음", edge.ConditionLabel);
        Assert.Equal("잠김", edge.LockedMessage);
        Assert.Equal(1, Assert.Single(edge.StatChanges).Amount);

        Assert.Equal("trust >= 3", model.FindCondition("신뢰높음")!.Expression);

        // 식은 자리만 옮겨 그대로 남고, 폐지 판정은 파서가 낸다 — 이행이 뜻을 바꾸지 않는다.
        Assert.Equal("cleared:ep1", model.FindCondition("복도완료")!.Expression);
        Assert.False(model.FindCondition("복도완료")!.IsValid);
        Assert.Contains(model.Diagnostics, item => item.Message.Contains("폐지"));

        // 빈 픽스처 시트는 사라진다(임시 제거). 데이터 파기가 아니다 — 머리글뿐이었다.
        using (var workbook = new XLWorkbook(path))
        {
            Assert.DoesNotContain(workbook.Worksheets, sheet =>
                sheet.Name == ChapterSheetNames.Fixtures);
            // v14 — `타입`이 맨 앞으로 옮겨졌다(구판에는 그 칸이 아예 없었다).
            Assert.Equal("타입", workbook.Worksheet(ChapterSheetNames.Stats).Cell(1, 1).GetString());
            Assert.Equal("스탯키", workbook.Worksheet(ChapterSheetNames.Stats).Cell(1, 2).GetString());
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
