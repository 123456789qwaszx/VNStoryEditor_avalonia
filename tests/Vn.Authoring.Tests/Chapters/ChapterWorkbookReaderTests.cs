using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// G1 수용 기준 — 견본이 정확히 읽히고, 규격 위반이 <b>시트·행·열까지 짚혀</b> 보고된다.
/// 오류 케이스마다 표를 코드로 세워 "이 표에서 이 오류"가 한눈에 보이게 한다.
/// </summary>
public sealed class ChapterWorkbookReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "vn-chapter-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    // ── 견본 워크북 ─────────────────────────────────────────────────────────

    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    [Fact]
    public void 견본_워크북이_오류_없이_읽힌다()
    {
        ChapterGraphModel model = ChapterWorkbookReader.Read(SamplePath);

        Assert.Empty(model.Errors);
        Assert.False(model.HasErrors);

        Assert.Equal(6, model.Episodes.Count);
        Assert.Equal(5, model.Edges.Count);
        Assert.Equal(4, model.Conditions.Count);
        Assert.Equal(3, model.Stats.Count);
        Assert.Equal(3, model.Fixtures.Count);
    }

    [Fact]
    public void 견본의_노드_위치가_엑셀에_적힌_그대로다()
    {
        ChapterGraphModel model = ChapterWorkbookReader.Read(SamplePath);

        Assert.Equal((0d, 0d), Position(model, "main05.01"));
        Assert.Equal((220d, 0d), Position(model, "main05.02"));
        Assert.Equal((440d, -120d), Position(model, "branch05.02A"));
        Assert.Equal((440d, 120d), Position(model, "main05.03"));
        Assert.Equal((220d, 170d), Position(model, "attach05.02s"));
        Assert.Equal((680d, 0d), Position(model, "main05.end"));
    }

    [Fact]
    public void 견본의_간선_관계가_엑셀에_적힌_그대로다()
    {
        ChapterGraphModel model = ChapterWorkbookReader.Read(SamplePath);

        Assert.Equal(
            [
                ("main05.01", "main05.02"),
                ("main05.02", "branch05.02A"),
                ("main05.02", "main05.03"),
                ("branch05.02A", "main05.03"),
                ("main05.03", "main05.end")
            ],
            model.Edges.Select(edge => (edge.FromEpisodeId, edge.ToEpisodeId)).ToArray());

        // 선택지 라벨이 있는 간선만 분기다 — 나머지는 일반 진행.
        ChapterEdge branch = model.Edges.Single(edge => edge.ToEpisodeId == "branch05.02A");
        Assert.Equal("라루의 제안을 듣는다", branch.OptionLabel);
        Assert.Equal("신뢰높음", branch.ConditionLabel);
        Assert.False(branch.HideWhenLocked);
        Assert.Equal("신뢰가 부족하다", branch.LockedMessage);

        Assert.True(model.Edges.Single(edge => edge.FromEpisodeId == "main05.01").IsPlainAdvance);
    }

    [Fact]
    public void 견본의_엔딩과_잠금이_구분된다()
    {
        ChapterGraphModel model = ChapterWorkbookReader.Read(SamplePath);

        Assert.True(model.FindEpisode("main05.end")!.IsEnding);
        Assert.Equal("ch05_normal", model.FindEpisode("main05.end")!.EndingKey);

        Assert.True(model.FindEpisode("branch05.02A")!.HasGate);
        Assert.Equal("신뢰높음", model.FindEpisode("branch05.02A")!.UnlockConditionLabel);
        Assert.Equal("복도완료", model.FindEpisode("attach05.02s")!.VisibleConditionLabel);
        Assert.False(model.FindEpisode("main05.01")!.HasGate);
    }

    [Fact]
    public void 견본의_조건식이_해석된다()
    {
        ChapterGraphModel model = ChapterWorkbookReader.Read(SamplePath);

        ConditionTerm trust = Assert.Single(model.FindCondition("신뢰높음")!.Parsed);
        Assert.Equal(ConditionTermKind.StatComparison, trust.Kind);
        Assert.Equal("trust", trust.Key);
        Assert.Equal(ConditionComparison.AtLeast, trust.Comparison);
        Assert.Equal(3, trust.Value);

        // AND는 ';' — 항이 둘이 된다.
        Assert.Equal(2, model.FindCondition("지쳐있음")!.Parsed.Count);

        ConditionTerm cleared = Assert.Single(model.FindCondition("복도완료")!.Parsed);
        Assert.Equal(ConditionTermKind.EpisodeCleared, cleared.Kind);
        Assert.Equal("main05.02", cleared.Key);
    }

    [Fact]
    public void 견본의_픽스처가_읽히고_활성은_하나다()
    {
        ChapterGraphModel model = ChapterWorkbookReader.Read(SamplePath);

        ChapterFixture active = Assert.Single(model.Fixtures, fixture => fixture.IsActive);
        Assert.Equal("기본 루트", active.Name);

        ChapterFixture trustRoute = model.Fixtures.Single(fixture => fixture.Name == "신뢰 루트");
        Assert.Equal(5, trustRoute.Stats["trust"]);
        Assert.Equal(
            new ChapterFixtureChoice("main05.02", "branch05.02A"),
            Assert.Single(trustRoute.Choices));
    }

    [Fact]
    public void 규격에_없는_시트는_읽지_않고_알린다()
    {
        ChapterGraphModel model = ChapterWorkbookReader.Read(SamplePath);

        // 견본 워크북에는 설명용 시트가 둘 더 있다. 조용히 지나치지 않는다(규칙 14).
        string[] ignored = model.Diagnostics
            .Where(item => item.Code == ChapterDiagnosticCode.SheetIgnored)
            .Select(item => item.Sheet!)
            .ToArray();

        Assert.Equal(2, ignored.Length);
        Assert.Contains("규격 안내", ignored);
        Assert.All(model.Diagnostics
                .Where(item => item.Code == ChapterDiagnosticCode.SheetIgnored),
            item => Assert.Equal(ChapterDiagnosticSeverity.Info, item.Severity));
    }

    // ── 오류 케이스 (G1 수용 기준의 5종) ────────────────────────────────────

    [Fact]
    public void 미등록_스탯키가_시트_행_열까지_짚혀_보고된다()
    {
        var sheets = Baseline();
        sheets[2].Rows[1][1] = "karma >= 3";

        ChapterDiagnostic problem = SingleError(sheets, ChapterDiagnosticCode.StatKeyUnknown);

        Assert.Equal("조건", problem.Sheet);
        Assert.Equal(2, problem.Row);
        Assert.Equal("B", problem.Column);
        Assert.Contains("karma", problem.Message);
        // 오타를 비슷한 이름으로 고쳐 주지 않는다 — 대신 선언된 키를 보여 준다.
        Assert.Contains("trust", problem.Message);
    }

    [Fact]
    public void 소수점이_오류로_잡힌다()
    {
        var sheets = Baseline();
        sheets[2].Rows[1][1] = "trust >= 2.5";

        ChapterDiagnostic problem = SingleError(sheets, ChapterDiagnosticCode.StatValueNotInteger);

        Assert.Equal("조건", problem.Sheet);
        Assert.Equal(2, problem.Row);
        Assert.Equal("B", problem.Column);
        Assert.Contains("정수", problem.Message);
    }

    [Fact]
    public void 스탯_시트의_소수점도_오류로_잡힌다()
    {
        var sheets = Baseline();
        sheets[3].Rows[1][4] = "10.5";

        ChapterDiagnostic problem = SingleError(sheets, ChapterDiagnosticCode.StatValueNotInteger);

        Assert.Equal("스탯", problem.Sheet);
        Assert.Equal("E", problem.Column);
    }

    [Fact]
    public void 빈_대사엔트리가_오류로_잡힌다()
    {
        var sheets = Baseline();
        sheets[0].Rows[1][4] = null;

        ChapterDiagnostic problem = SingleError(sheets, ChapterDiagnosticCode.DialogueEntryBlank);

        Assert.Equal("에피소드", problem.Sheet);
        Assert.Equal(2, problem.Row);
        Assert.Equal("E", problem.Column);
    }

    [Fact]
    public void 중복_EpisodeId가_오류로_잡힌다()
    {
        var sheets = Baseline();
        sheets[0].Rows[2][0] = "ep1";

        ChapterDiagnostic problem = SingleError(sheets, ChapterDiagnosticCode.EpisodeIdDuplicated);

        Assert.Equal("에피소드", problem.Sheet);
        Assert.Equal(3, problem.Row);
        Assert.Equal("A", problem.Column);
        Assert.Contains("2행", problem.Message);
    }

    [Fact]
    public void 미정의_조건_라벨이_오류로_잡힌다()
    {
        var sheets = Baseline();
        sheets[0].Rows[2][8] = "없는라벨";

        ChapterDiagnostic problem = SingleError(sheets, ChapterDiagnosticCode.ConditionLabelUndefined);

        Assert.Equal("에피소드", problem.Sheet);
        Assert.Equal(3, problem.Row);
        Assert.Equal("I", problem.Column);
        Assert.Contains("없는라벨", problem.Message);
    }

    // ── 그 밖의 무결성 ──────────────────────────────────────────────────────

    [Fact]
    public void 간선_끝점이_없는_에피소드면_오류다()
    {
        var sheets = Baseline();
        sheets[1].Rows[1][1] = "ep99";

        ChapterDiagnostic problem = SingleError(sheets, ChapterDiagnosticCode.EdgeEndpointUnknown);

        Assert.Equal("간선", problem.Sheet);
        Assert.Equal(2, problem.Row);
        Assert.Equal("B", problem.Column);
    }

    [Fact]
    public void 시트가_없으면_오류로_알린다()
    {
        var sheets = Baseline().Where(sheet => sheet.Name != "간선").ToArray();

        ChapterDiagnostic problem = SingleError(sheets, ChapterDiagnosticCode.SheetMissing);

        Assert.Equal("간선", problem.Sheet);
        Assert.Null(problem.Row);
    }

    [Fact]
    public void 머리글이_규격과_다르면_경고한다()
    {
        var sheets = Baseline();
        sheets[0].Rows[0][2] = "순서";

        ChapterGraphModel model = ReadGenerated(sheets);

        ChapterDiagnostic warning = Assert.Single(model.Diagnostics, item => item.Code == ChapterDiagnosticCode.ColumnHeaderUnexpected);

        Assert.Equal(ChapterDiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal("C", warning.Column);
        Assert.False(model.HasErrors);
    }

    [Fact]
    public void game_definition에_없는_스탯은_경고이지_오류가_아니다()
    {
        var definition = new GameDefinition
        {
            Variables = { new VariableSpec { Name = "trust", Type = "int" } }
        };

        ChapterGraphModel model = ChapterWorkbookReader.Read(WriteGenerated(Baseline()), definition);

        ChapterDiagnostic warning = Assert.Single(model.Diagnostics, item => item.Code == ChapterDiagnosticCode.StatMissingFromGameDefinition);

        Assert.Equal(ChapterDiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal("anger", ValueAt(warning, "anger"));
        Assert.False(model.HasErrors);
    }

    [Fact]
    public void 스탯_범위가_뒤집히면_오류다()
    {
        var sheets = Baseline();
        sheets[3].Rows[1][3] = "5";
        sheets[3].Rows[1][4] = "1";

        ChapterDiagnostic problem = SingleError(sheets, ChapterDiagnosticCode.StatRangeInvalid);

        Assert.Equal("스탯", problem.Sheet);
        Assert.Contains("도달성", problem.Message);
    }

    [Fact]
    public void 진단은_파일_시트_행_열을_한_줄로_말한다()
    {
        var sheets = Baseline();
        sheets[0].Rows[1][4] = null;

        ChapterDiagnostic problem = SingleError(sheets, ChapterDiagnosticCode.DialogueEntryBlank);

        Assert.StartsWith("chapter.xlsx · 에피소드 · 2행 · E열 — ", problem.Describe());
    }

    [Fact]
    public void 워크북_파일이_없으면_경로를_담아_알린다()
    {
        string missing = Path.Combine(_directory, "없는파일.xlsx");

        XlsxReadException failure =
            Assert.Throws<XlsxReadException>(() => ChapterWorkbookReader.Read(missing));

        Assert.Equal(missing, failure.Path);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static (double X, double Y) Position(ChapterGraphModel model, string episodeId)
    {
        ChapterEpisode episode = model.FindEpisode(episodeId)!;
        return (episode.X, episode.Y);
    }

    private static string ValueAt(ChapterDiagnostic diagnostic, string expected) =>
        diagnostic.Message.Contains(expected, StringComparison.Ordinal) ? expected : diagnostic.Message;

    private ChapterDiagnostic SingleError(
        (string Name, string?[][] Rows)[] sheets,
        ChapterDiagnosticCode code)
    {
        ChapterGraphModel model = ReadGenerated(sheets);
        return Assert.Single(model.Errors, item => item.Code == code);
    }

    private ChapterGraphModel ReadGenerated((string Name, string?[][] Rows)[] sheets) =>
        ChapterWorkbookReader.Read(WriteGenerated(sheets));

    private string WriteGenerated((string Name, string?[][] Rows)[] sheets) =>
        XlsxTestWorkbook.Write(_directory, "chapter.xlsx", sheets);

    /// <summary>오류가 하나도 없는 최소 챕터. 각 테스트는 여기서 한 칸만 망가뜨린다.</summary>
    private static (string Name, string?[][] Rows)[] Baseline() =>
    [
        ("에피소드", [
            ["EpisodeId", "제목", "인덱스", "종류", "대사엔트리", "X", "Y", "표시조건", "해금조건", "엔딩키", "메모"],
            ["ep1", "첫 화", "01", "Main", "Story_ep1", "0", "0", null, null, null, null],
            ["ep2", "둘째 화", "02", "Main", "Story_ep2", "200", "0", null, null, null, null]
        ]),
        ("간선", [
            ["출발", "도착", "선택지 라벨", "조건", "잠금시 숨김", "잠금 안내문"],
            ["ep1", "ep2", null, null, "FALSE", null]
        ]),
        ("조건", [
            ["라벨", "조건식", "설명"],
            ["신뢰높음", "trust >= 3", "라루를 신뢰"]
        ]),
        ("스탯", [
            ["스탯키", "표시명", "초기값", "최소", "최대"],
            ["trust", "신뢰", "0", "0", "10"],
            ["anger", "분노", "0", "0", "10"]
        ]),
        ("픽스처", [
            ["픽스처명", "활성", "trust", "anger", "고정 선택 (에피소드ID→도착ID)"],
            ["기본", "TRUE", "0", "0", "ep1→ep2"]
        ])
    ];
}
