using ClosedXML.Excel;
using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// G2-b — 표를 X11 문법 텍스트로 편다 (§3.4).
///
/// <b>v15에서 평평화는 "줄을 순서대로 적는 일"이 됐다.</b> 조건 블록이 폐지되면서 들여쓰기도
/// 깊이도 <c>&lt;&lt;if&gt;&gt;</c> 조립도 사라졌다 — 분기의 주인은 챕터 `간선` 시트다.
/// 여기서 고정하는 것은 <b>산출 텍스트의 모양</b>과 <b>LineId 전역 유일성</b>(계약서 C1)이다.
/// </summary>
public sealed class EpisodeFlattenerTests : IDisposable
{
    private static readonly Dictionary<string, ChapterCondition> NoConditions = new(StringComparer.Ordinal);

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

    [Fact]
    public void 표가_줄_순서_그대로_펴진다()
    {
        EpisodeFlattenResult result = Flatten(Baseline());

        Assert.Empty(result.Errors);

        // 화자가 빈 행은 지문이다 — 이름표 없이 내용만 나간다.
        Assert.Equal(
            """
            윌로: 복도는 조용했다. #line:ln_0001
            라루: 여기서 기다릴까? #line:ln_0002
            문이 열렸다. #line:ln_0003
            윌로: 가자. #line:ln_0004

            """.ReplaceLineEndings("\n"),
            result.Text.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void 모든_LineId가_정확히_한_번씩_나온다()
    {
        // 계약서 C1(LineId 전역 유일성). 걷기가 각 행을 한 번만 지나므로 이 성질이
        // 구조에서 나온다 — 구간 재사용 금지 같은 규칙이 필요 없다.
        EpisodeWorkbookModel model = Read(Baseline());
        EpisodeFlattenResult result = EpisodeFlattener.Flatten(model, NoConditions);

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
        // 그대로 지나며, 본문과 LineId가 전부 해석된다.
        EpisodeFlattenResult flattened = Flatten(Baseline());

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
    }

    [Fact]
    public void 신원_맵이_B열보다_우선한다()
    {
        // v4 — 행 신원의 원천은 프로젝트의 ExcelLineMap이고, B열은 과거 파일의 이행 seed다.
        EpisodeWorkbookModel model = Read(Baseline());

        EpisodeFlattenResult result = EpisodeFlattener.Flatten(
            model, NoConditions, identity: new Dictionary<int, string> { [10] = "ln_매핑" });

        Assert.Contains("#line:ln_매핑", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("#line:ln_0001", result.Text, StringComparison.Ordinal);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private EpisodeFlattenResult Flatten(string?[][] rows) =>
        EpisodeFlattener.Flatten(Read(rows), NoConditions);

    private EpisodeWorkbookModel Read(string?[][] rows)
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

        return EpisodeWorkbookReader.Read(path);
    }

    /// <summary>v15 4열 대본 — 대사 셋과 지문 하나.</summary>
    private static string?[][] Baseline() =>
    [
        ["인덱스", "LineId", "화자", "내용"],
        ["10", "ln_0001", "윌로", "복도는 조용했다."],
        ["20", "ln_0002", "라루", "여기서 기다릴까?"],
        ["30", "ln_0003", null, "문이 열렸다."],
        ["40", "ln_0004", "윌로", "가자."]
    ];
}
