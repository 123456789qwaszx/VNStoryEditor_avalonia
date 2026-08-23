using ClosedXML.Excel;
using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// G2-b — 표를 X11 문법 텍스트로 편다 (§3.4).
///
/// <b>v10에서 평평화는 한 번 훑기가 됐다.</b> 구간을 옮겨 넣던 시절의 약속("같은 대사가 두 번
/// 나오지 않는다")은 이제 걷기의 성질이라 따로 지킬 것이 없다 — 각 행을 한 번 지나가면 끝이다.
/// 여기서 고정하는 것은 <b>블록이 들여쓰기와 <c>&lt;&lt;endif&gt;&gt;</c>로 정확히 재현되는가</b>다.
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

        // 시트의 행 순서 그대로다 — 옮겨 오는 구간이 없으므로 눈으로 대조된다.
        // 중첩 블록은 두 겹으로 들여써지고, ELSEIF는 자기 체인의 바깥 깊이에 선다.
        Assert.Equal(
            """
            윌로: 복도는 조용했다. #line:ln_0001
            라루: 여기서 기다릴까? #line:ln_0002
            <<if $trust >= 3>>
                윌로: 너를 믿어. #line:ln_0003
                <<if $fatigue >= 4 && $anger <= 2>>
                    라루: 다리가 무거워. #line:ln_0004
                <<endif>>
            <<elseif $anger >= 5>>
                라루: 아직도 화가 나. #line:ln_0005
            <<endif>>
            윌로: 문이 열렸다. #line:ln_0006

            """.ReplaceLineEndings("\n"),
            result.Text.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void 모든_LineId가_정확히_한_번씩_나온다()
    {
        // 계약서 C1(LineId 전역 유일성). v10에서는 걷기가 각 행을 한 번만 지나므로
        // 이 성질이 구조에서 나온다 — 구간 재사용 금지 같은 규칙이 필요 없다.
        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(SamplePath, Labels);
        EpisodeFlattenResult result = EpisodeFlattener.Flatten(model, Expressions);

        Assert.Equal(
            result.EmittedLineIds.Count,
            result.EmittedLineIds.Distinct(StringComparer.Ordinal).Count());

        Assert.Equal(
            model.Rows
                .Where(row => row.LineId is not null)
                .Select(row => row.LineId!)
                .OrderBy(id => id, StringComparer.Ordinal),
            result.EmittedLineIds.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void 평평화_산출물을_기존_파서가_한_줄도_남기지_않고_읽는다()
    {
        // G3의 증명 — 새 임포터를 만들지 않았다. 엑셀에서 나온 텍스트가 X12(a) 파서를
        // 그대로 지나며, 조건·본문·LineId가 전부 해석된다.
        EpisodeFlattenResult flattened = FlattenSample();

        ScenarioParseResult parsed = ScenarioTextParser.Parse(
            flattened.Text,
            GameDefinition.Parse("""
                { "speakers": [ { "name": "라루", "characterId": "laru" },
                                { "name": "윌로", "characterId": "willo" } ] }
                """)!);

        Assert.Empty(parsed.UnparsedLines);

        Assert.Equal(
            flattened.EmittedLineIds,
            parsed.Lines.Where(line => line.LineId is not null).Select(line => line.LineId!));

        Assert.Contains(parsed.Lines, line => line.Transition?.Kind == ConditionTransitionKind.BeginIf);
        Assert.Contains(parsed.Lines, line => line.Transition?.Kind == ConditionTransitionKind.EndIf);
    }

    // ── 블록 ────────────────────────────────────────────────────────────────

    [Fact]
    public void 조건으로_끝나는_대본도_파서가_남김없이_읽는다()
    {
        // 2026-08-17 소유자 보고 — "<<endif>> 뒤따르는 대사 줄이 없어 붙일 곳이 없습니다".
        // 닫힘은 다음 줄이 실어 나르는데 블록이 마지막이면 그 줄이 없다. 그건 정상이고,
        // 산출 쪽이 문서 끝에서 닫는다.
        var rows = Baseline();
        rows = [.. rows.Take(rows.Length - 1)];   // 블록 뒤의 마지막 대사를 뗀다

        EpisodeFlattenResult flattened = Flatten(rows);

        ScenarioParseResult parsed = ScenarioTextParser.Parse(
            flattened.Text, GameDefinition.Parse("""{ "speakers": [] }""")!);

        Assert.Empty(parsed.UnparsedLines);
    }

    [Fact]
    public void 블록이_안_닫혀도_산출물의_괄호는_맞춘다()
    {
        // 리더가 이미 오류로 잡은 상태다. 그래도 반쯤 열린 Yarn을 내보내면 컴파일러의
        // 오류가 진짜 원인을 덮는다 — 여기서 닫아 둔다.
        var rows = Baseline();
        rows[8] = [null, null, "80", null, "라루", "닫는 줄이었던 자리"];

        EpisodeFlattenResult result = Flatten(rows);

        Assert.Equal(
            result.Text.Split("<<if").Length,
            result.Text.Split("<<endif>>").Length);
    }

    [Fact]
    public void 조건을_못_세우면_그_블록만_통째로_빠진다()
    {
        var rows = Baseline();
        rows[3][1] = null;   // IF의 조건라벨 제거

        EpisodeFlattenResult result = Flatten(rows);

        // 그 블록 안의 줄만 안 나간다 — 바깥도, <b>다음 블록도</b> 멀쩡하다.
        Assert.DoesNotContain("ln_0003", result.Text, StringComparison.Ordinal);
        Assert.Contains("ln_0001", result.Text, StringComparison.Ordinal);
        Assert.Contains("ln_0004", result.Text, StringComparison.Ordinal);
        Assert.Contains("ln_0005", result.Text, StringComparison.Ordinal);

        // 빠진 블록의 <<endif>>도 함께 빠진다 — 짝이 어긋난 Yarn을 내보내지 않는다.
        Assert.Single(result.Text.Split("<<if").Skip(1));
        Assert.Single(result.Text.Split("<<endif>>").Skip(1));
    }

    [Fact]
    public void 조건_조립은_YarnSyntax를_지난다()
    {
        // YarnSyntax.AppendCondition은 빈 식을 "false"로 떨군다. 자기 문자열 연결이었다면
        // 그 처리가 없어 "<<if >>"가 나온다 — 조립기를 지났는지 이 차이로 확인된다.
        var rows = Baseline();
        rows[3][1] = "빈식";

        EpisodeFlattenResult result = Flatten(rows, new Dictionary<string, ChapterCondition>(Expressions)
        {
            ["빈식"] = Condition("빈식", "   ")
        });

        // 빈 식은 번역 단계에서 이미 거부된다 — 조립기까지 가지 않는다.
        Assert.Contains(result.Errors, item => item.Message.Contains("유효하지 않아", StringComparison.Ordinal));
        Assert.DoesNotContain("<<if >>", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void 라벨의_식을_못_찾으면_사유를_말한다()
    {
        var rows = Baseline();
        rows[3][1] = "복도완료";

        EpisodeFlattenResult result = Flatten(rows, new Dictionary<string, ChapterCondition>());

        Assert.Contains(result.Errors, item =>
            item.Message.Contains("챕터 `조건` 시트에서 찾지 못해", StringComparison.Ordinal));
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private static EpisodeFlattenResult FlattenSample()
    {
        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(SamplePath, Labels);
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

        // 라벨 목록은 리더의 것이다 — 식을 못 찾는 경우를 만들려면 라벨은 살아 있어야 한다.
        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(path, Labels);

        return EpisodeFlattener.Flatten(model, expressions ?? Expressions);
    }

    /// <summary>블록 둘이 나란히 있고 그 뒤에 대사가 오는 최소 표 (견본과 같은 모양).</summary>
    private static string?[][] Baseline() =>
    [
        ["유형", "조건라벨", "인덱스", "LineId", "화자", "내용"],
        [null, null, "10", "ln_0001", "윌로", "첫 줄"],
        [null, null, "20", "ln_0002", "라루", "둘째 줄"],
        ["IF", "신뢰높음", null, null, null, null],
        [null, null, "40", "ln_0003", "윌로", "첫 블록 안"],
        ["ENDIF", null, null, null, null, null],
        ["IF", "지쳐있음", null, null, null, null],
        [null, null, "70", "ln_0004", "라루", "둘째 블록 안"],
        ["ENDIF", null, null, null, null, null],
        [null, null, "90", "ln_0005", "윌로", "끝 줄"]
    ];
}
