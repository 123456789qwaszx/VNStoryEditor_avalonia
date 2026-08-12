using ClosedXML.Excel;
using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// G2-b — 표를 X11 문법 텍스트로 편다 (§3.4).
///
/// 여기서 지키는 두 약속: <b>같은 대사가 두 번 나오지 않는다</b>(구간은 복제가 아니라 이동이다)와
/// <b><c>OUT</c>이 사실인지 대조한다</b>(선언이지 명령이 아니다).
/// </summary>
public sealed class EpisodeFlattenerTests : IDisposable
{
    private static readonly string[] Labels = ["신뢰높음", "분노누적", "지쳐있음", "복도완료"];
    private static readonly string[] Stats = ["trust", "anger", "fatigue"];

    // 라벨 → 챕터 조건. 식은 기획자 언어이고, 평평화가 Yarn으로 번역해 싣는다.
    private static readonly Dictionary<string, ChapterCondition> Expressions =
        new[]
        {
            Condition("신뢰높음", "trust >= 3"),
            Condition("분노누적", "anger >= 5"),
            Condition("지쳐있음", "fatigue >= 4; anger <= 2"),
            Condition("복도완료", "cleared:main05.02")
        }.ToDictionary(condition => condition.Label, StringComparer.Ordinal);

    private static ChapterCondition Condition(string label, string expression)
    {
        ConditionParseResult parsed = ConditionExpressionParser.Parse(expression, Stats);
        return new ChapterCondition(label, expression, null, parsed.Terms, parsed.IsValid, SourceRow: 2);
    }

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-flatten-tests", Guid.NewGuid().ToString("N"));

    public EpisodeFlattenerTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    // ── 견본 ────────────────────────────────────────────────────────────────

    [Fact]
    public void 견본이_규격의_평평화_결과와_같게_펴진다()
    {
        EpisodeFlattenResult result = FlattenSample();

        Assert.Empty(result.Errors);

        // 견본 `규격 안내`가 그림으로 적어 둔 순서 그대로다:
        //   10, 20 → <<if>> 900,905,908 <<endif>> → 40, 50, 60 → 옵션 71(920,928) / 옵션 72
        Assert.Equal(
            """
            윌로: 복도는 생각보다 조용했다. #line:ln_0001
            라루: 여기서 잠깐 쉬어도 될까? #line:ln_0002
            <<if $trust >= 3>>
                윌로: 어머니가 같은 말을 했었다. #line:ln_0100
                라루: …그 얘기 해준 적 있었나? #line:ln_0101
                윌로: 아니. 처음 해. #line:ln_0102
            <<endif>>
            라루: …왜 그런 표정이야? #line:ln_0003
            윌로: 아무것도 아니야. #line:ln_0004
            라루: 그럼, 어떻게 할래? #line:ln_0005
            -> 라루의 제안을 듣는다 #line:ln_0007
                라루: 고맙다는 말은 안 할래. #line:ln_0110
                윌로: 알아. 너답네. #line:ln_0111
            -> 혼자 문을 연다 #line:ln_0008

            """.ReplaceLineEndings("\n"),
            result.Text.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void 같은_대사가_두_번_나오지_않는다()
    {
        // Gate B 2번. 구간은 복제가 아니라 이동이므로 각 LineId가 정확히 한 번 나온다 —
        // 이것이 계약서 C1(LineId 전역 유일성)의 구조적 보장이다.
        EpisodeFlattenResult result = FlattenSample();

        Assert.Equal(
            result.EmittedLineIds.Count,
            result.EmittedLineIds.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void 구간의_줄은_원래_자리에서_사라진다()
    {
        EpisodeFlattenResult result = FlattenSample();
        string[] lines = result.Text.ReplaceLineEndings("\n").Split('\n');

        // 900번 구간의 첫 줄은 <<if>> 바로 다음에만 있고, 40 앞뒤 어디에도 또 있지 않다.
        Assert.Single(lines, line => line.Contains("ln_0100", StringComparison.Ordinal));

        int conditionAt = Array.FindIndex(lines, line => line.StartsWith("<<if", StringComparison.Ordinal));
        int movedAt = Array.FindIndex(lines, line => line.Contains("ln_0100", StringComparison.Ordinal));

        Assert.Equal(conditionAt + 1, movedAt);
    }

    [Fact]
    public void CHOICE_행을_뺀_모든_LineId가_산출물에_나온다()
    {
        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(SamplePath, Labels, Stats);
        EpisodeFlattenResult result = EpisodeFlattener.Flatten(model, Expressions);

        // CHOICE 행의 LineId만 실리지 않는다 — Yarn에 "선택 시작" 줄이 없어 붙일 자리가 없다.
        // 조용히 빠뜨리지 않고 알림으로 남긴다(규칙 14).
        Assert.Equal(
            model.Rows
                .Where(row => row.LineId is not null && row.Kind != EpisodeRowKind.Choice)
                .Select(row => row.LineId!)
                .OrderBy(id => id, StringComparer.Ordinal),
            result.EmittedLineIds.OrderBy(id => id, StringComparer.Ordinal));

        ChapterDiagnostic notice = Assert.Single(
            result.Diagnostics, item => item.Severity == ChapterDiagnosticSeverity.Info);

        Assert.Equal("B", notice.Column);
        Assert.Contains("ln_0006", notice.Message);
        Assert.Contains("붙일 자리가 없습니다", notice.Message);
    }

    [Fact]
    public void 조건_조립은_YarnSyntax를_지난다()
    {
        // YarnSyntax.AppendCondition은 빈 식을 "false"로 떨군다. 자기 문자열 연결이었다면
        // 그 처리가 없어 "<<if >>"가 나온다 — 조립기를 지났는지 이 차이로 확인된다.
        var rows = Baseline();
        rows[3][4] = "빈식";

        EpisodeFlattenResult result = Flatten(rows, new Dictionary<string, ChapterCondition>(Expressions)
        {
            ["빈식"] = Condition("빈식", "   ")
        });

        // 빈 식은 번역 단계에서 이미 거부된다 — 조립기까지 가지 않는다.
        Assert.Contains(result.Errors, item => item.Message.Contains("유효하지 않아", StringComparison.Ordinal));
        Assert.DoesNotContain("<<if >>", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void 평평화_산출물을_기존_파서가_한_줄도_남기지_않고_읽는다()
    {
        // G3의 증명 — 새 임포터를 만들지 않았다. 엑셀에서 나온 텍스트가 X12(a) 파서를
        // 그대로 지나며, 조건·옵션·본문·LineId가 전부 해석된다.
        EpisodeFlattenResult flattened = FlattenSample();

        ScenarioParseResult parsed = ScenarioTextParser.Parse(
            flattened.Text,
            GameDefinition.Parse("""
                { "speakers": [ { "name": "라루", "characterId": "laru" },
                                { "name": "윌로", "characterId": "willo" } ] }
                """)!);

        Assert.Empty(parsed.UnparsedLines);

        // 평평화가 실은 LineId 전부가 파서를 지나 그대로 나온다.
        Assert.Equal(
            flattened.EmittedLineIds,
            parsed.Lines.Where(line => line.LineId is not null).Select(line => line.LineId!));

        // 조건 갈래와 선택 블록이 구조로 잡힌다.
        Assert.Contains(parsed.Lines, line => line.Transition?.Kind == ConditionTransitionKind.BeginIf);
        Assert.Contains(parsed.Lines, line => line.Transition?.Kind == ConditionTransitionKind.EndIf);
        Assert.Contains(parsed.Lines, line => line.Transition?.Kind == ConditionTransitionKind.BeginChoice);
        Assert.Contains(parsed.Lines, line => line.Transition?.Kind == ConditionTransitionKind.BeginNextOption);
    }

    // ── §3.3 규칙 6 — OUT 대조 ──────────────────────────────────────────────

    [Fact]
    public void 규칙6_OUT이_자연_수렴_지점이_아니면_오류다()
    {
        var rows = Baseline();
        rows[8][6] = "50";  // 실제로는 40으로 흐르는데 50이라고 적었다

        EpisodeFlattenResult result = Flatten(rows);

        ChapterDiagnostic problem = Assert.Single(result.Errors);

        Assert.Equal("G", problem.Column);
        Assert.Contains("OUT=50이라고 적혀 있지만", problem.Message);
        Assert.Contains("실제 수렴 지점은 40입니다", problem.Message);
        Assert.Contains("점프 명령이 아니라", problem.Message);
    }

    [Fact]
    public void 규칙6_자연_수렴이_에피소드_끝이면_END여야_한다()
    {
        var rows = Baseline();
        // IF를 마지막 주 흐름 행으로 만든다 → 조건 뒤에 아무것도 없다.
        rows[4] = ["40", null, null, null, null, null, null, null, null, null, null];
        rows[5] = ["50", null, null, null, null, null, null, null, null, null, null];

        var trimmed = new[] { rows[0], rows[1], rows[2], rows[3], rows[6], rows[7], rows[8] };

        EpisodeFlattenResult result = Flatten(trimmed);

        ChapterDiagnostic problem = Assert.Single(result.Errors);
        Assert.Contains("에피소드 끝(END)", problem.Message);
    }

    [Fact]
    public void OUT이_맞으면_오류가_없다()
    {
        EpisodeFlattenResult result = Flatten(Baseline());

        Assert.Empty(result.Errors);
    }

    // ── D6 — 옵션별 OUT 분기 ────────────────────────────────────────────────

    [Fact]
    public void 옵션별_OUT이_서로_다른_비END_인덱스면_오류다()
    {
        EpisodeFlattenResult result = Flatten(TwoOptionOutlets(first: "40", second: "50"));

        ChapterDiagnostic problem = Assert.Single(result.Errors);

        Assert.Contains("옵션마다 OUT이 갈립니다", problem.Message);
        Assert.Contains("v1에서는 오류", problem.Message);
    }

    [Fact]
    public void 옵션들이_같은_곳으로_수렴하면_문제가_없다()
    {
        EpisodeFlattenResult result = Flatten(TwoOptionOutlets(first: "40", second: "40"));

        Assert.DoesNotContain(result.Errors,
            item => item.Message.Contains("옵션마다 OUT이 갈립니다", StringComparison.Ordinal));
    }

    [Fact]
    public void 옵션들이_모두_END면_문제가_없다()
    {
        EpisodeFlattenResult result = Flatten(TwoOptionOutlets(first: "END", second: "END"));

        Assert.DoesNotContain(result.Errors,
            item => item.Message.Contains("옵션마다 OUT이 갈립니다", StringComparison.Ordinal));
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static EpisodeFlattenResult FlattenSample()
    {
        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(SamplePath, Labels, Stats);
        return EpisodeFlattener.Flatten(model, Expressions);
    }

    private EpisodeFlattenResult Flatten(
        string?[][] rows, IReadOnlyDictionary<string, ChapterCondition>? expressions = null)
    {
        string path = Path.Combine(_directory, $"ep_{Guid.NewGuid():N}.xlsx");

        using (var workbook = new XLWorkbook())
        {
            IXLWorksheet sheet = workbook.AddWorksheet("본문");

            for (int row = 0; row < rows.Length; row++)
            {
                for (int column = 0; column < rows[row].Length; column++)
                {
                    if (rows[row][column] is { Length: > 0 } value)
                    {
                        sheet.Cell(row + 1, column + 1).SetValue(value);
                    }
                }
            }

            workbook.SaveAs(path);
        }

        var labels = (expressions ?? Expressions).Keys.ToArray();
        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(path, labels, Stats);

        return EpisodeFlattener.Flatten(model, expressions ?? Expressions);
    }

    /// <summary>조건 구간 하나가 40으로 수렴하는 최소 표.</summary>
    private static string?[][] Baseline() =>
    [
        ["인덱스", "LineId", "유형", "태그", "조건라벨", "IN", "OUT", "화자", "내용", "스탯변화", "메모"],
        ["10", "ln_0001", null, null, null, null, null, "윌로", "첫 줄", null, null],
        ["20", "ln_0002", null, null, null, null, null, "라루", "둘째 줄", null, null],
        ["30", null, "IF", null, "신뢰높음", "900", null, null, null, null, null],
        ["40", "ln_0003", null, null, null, null, null, "라루", "수렴 지점", null, null],
        ["50", "ln_0004", null, null, null, null, null, "윌로", "끝 줄", null, null],
        ["900", "ln_0100", null, "INPUT", null, null, null, "윌로", "구간 첫 줄", null, null],
        ["905", "ln_0101", null, null, null, null, null, "라루", "구간 가운데", null, null],
        ["908", "ln_0102", null, "OUT", null, null, "40", "윌로", "구간 끝", null, null]
    ];

    /// <summary>선택지 두 옵션이 각자 구간을 갖고, 그 구간의 OUT이 갈리는 표.</summary>
    private static string?[][] TwoOptionOutlets(string first, string second) =>
    [
        ["인덱스", "LineId", "유형", "태그", "조건라벨", "IN", "OUT", "화자", "내용", "스탯변화", "메모"],
        ["10", "ln_0001", null, null, null, null, null, "윌로", "첫 줄", null, null],
        ["40", "ln_0002", null, null, null, null, null, "라루", "수렴 후보", null, null],
        ["50", "ln_0003", null, null, null, null, null, "윌로", "다른 수렴 후보", null, null],
        ["70", "ln_0006", "CHOICE", null, null, null, null, null, null, null, null],
        ["71", "ln_0007", "OPTION", null, null, "900", null, null, "첫 선택", null, null],
        ["72", "ln_0008", "OPTION", null, null, "920", null, null, "둘째 선택", null, null],
        ["900", "ln_0100", null, "INPUT", null, null, null, "윌로", "첫 본문", null, null],
        ["908", "ln_0101", null, "OUT", null, null, first, "윌로", "첫 본문 끝", null, null],
        ["920", "ln_0110", null, "INPUT", null, null, null, "라루", "둘째 본문", null, null],
        ["928", "ln_0111", null, "OUT", null, null, second, "라루", "둘째 본문 끝", null, null]
    ];
}
