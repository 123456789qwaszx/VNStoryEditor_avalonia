using ClosedXML.Excel;
using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>블록 행에는 못 적는다</b> (2026-08-24 소유자: "만약 If,ElseIf,EndIf일 경우 LineId가
/// 생성되면 안돼. 그리고 옆쪽으로 화자와 내용을 지금 적을 수 있게 되어있는데 그것도
/// If,ElseIf,EndIf일 경우 안 적게 막아줘").
///
/// 빗장은 <b>두 겹</b>이고 두 겹의 힘이 다르다:
///   ① <b>엑셀</b> — 손에서 먼저 막는다. 그런데 한 칸에 검증은 하나뿐이라 화자 열에는
///      못 건다(그 자리는 화자 드롭다운이 쓴다). 붙여넣기로도 빠져나간다.
///   ② <b>리더</b> — 셋 다 오류로 짚는다. 이쪽이 규칙의 <em>주인</em>이다.
///
/// 그래서 ①이 없어도 사고는 안 나지만, ②가 없으면 난다.
/// </summary>
public sealed class EpisodeBlockRowGuardTests : IDisposable
{
    private static readonly string[] Labels = ["신뢰높음"];

    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "vn-block-guard", Guid.NewGuid().ToString("N"));

    public EpisodeBlockRowGuardTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    // ── ① 엑셀이 손에서 막는다 ──────────────────────────────────────────────

    [Fact]
    public void 새_대본에_인덱스_LineId_내용_빗장이_걸려_있다()
    {
        EpisodeLibrary.EnsureWorkbook(_folder, "ep1");

        using var book = new XLWorkbook(EpisodeLibrary.FindExisting(_folder, "ep1")!);
        IXLWorksheet sheet = book.Worksheets.First();

        // v14 — 인덱스(C) · LineId(D) · 내용(F) 셋에 사용자 정의 검증이 있다.
        foreach (int column in new[] { 3, 4, 6 })
        {
            Assert.Contains(
                sheet.DataValidations,
                validation =>
                    validation.AllowedValues == XLAllowedValues.Custom &&
                    validation.Ranges.Any(range =>
                        range.RangeAddress.FirstAddress.ColumnNumber == column));
        }
    }

    [Fact]
    public void 빗장은_유형이_비었거나_대사일_때만_통과시킨다()
    {
        // 수식이 유형 열(A)을 가리켜야 한다 — 열이 옮겨 가면 여기가 먼저 깨진다.
        EpisodeLibrary.EnsureWorkbook(_folder, "ep1");

        using var book = new XLWorkbook(EpisodeLibrary.FindExisting(_folder, "ep1")!);

        IXLDataValidation guard = book.Worksheets.First().DataValidations
            .First(validation => validation.AllowedValues == XLAllowedValues.Custom);

        Assert.Contains("$A2", guard.Value);
        Assert.Contains("대사", guard.Value);
        Assert.True(guard.IgnoreBlanks, "빈칸은 언제나 통과해야 한다 — 막는 것은 적는 것뿐이다");
    }

    // ── ② 리더가 규칙의 주인이다 ────────────────────────────────────────────

    [Theory]
    [InlineData("IF")]
    [InlineData("ELSEIF")]
    [InlineData("ENDIF")]
    public void 블록_행의_LineId는_오류다(string kind)
    {
        ChapterDiagnostic problem = SingleError(Block(kind, lineId: "ln_9999"), "라인이 아니므로");

        Assert.Equal("D", problem.Column);   // v14 — LineId는 D열
    }

    [Theory]
    [InlineData("IF")]
    [InlineData("ELSEIF")]
    [InlineData("ENDIF")]
    public void 블록_행의_화자와_내용은_오류다(string kind)
    {
        // ⚠ 붙여넣기는 엑셀 검증을 지나친다 — 그래서 리더가 마지막 빗장이다.
        ChapterDiagnostic problem = SingleError(
            Block(kind, speaker: "라루", text: "여기 적으면 버려진다"), "블록의 흐름만 그립니다");

        Assert.Equal("F", problem.Column);
        Assert.Contains(kind, problem.Message);
    }

    [Fact]
    public void 대사_행에는_아무_빗장도_안_걸린다()
    {
        // 빗장이 넓어지면 정상 대사가 막힌다 — 그 반대쪽도 함께 못 박는다.
        var rows = new string?[][]
        {
            ["유형", "조건라벨", "인덱스", "LineId", "화자", "내용"],
            [null, null, "10", "ln_0001", "라루", "평범한 대사"],
            ["대사", null, "20", "ln_0002", "윌로", "유형을 적은 대사"]
        };

        Assert.Empty(Read(rows).Errors);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    /// <summary>여는 IF와 닫는 ENDIF 사이에 문제의 행 하나 — 블록 자체는 성립한다.</summary>
    private static string?[][] Block(
        string kind, string? lineId = null, string? speaker = null, string? text = null)
    {
        // v14 자리 — 유형 · 조건라벨 · 인덱스 · LineId · 화자 · 내용.
        // ⚠ 블록 행에는 인덱스를 안 적는다. 적어도 리더가 안 싣지만, 여기서 적으면
        //    "블록 행이 번호를 갖는다"는 옛 그림이 픽스처에 남는다.
        string?[] target = kind switch
        {
            "IF" => ["IF", "신뢰높음", null, lineId, speaker, text],
            "ELSEIF" => ["ELSEIF", "신뢰높음", null, lineId, speaker, text],
            _ => ["ENDIF", null, null, lineId, speaker, text]
        };

        return kind == "ENDIF"
            ?
            [
                ["유형", "조건라벨", "인덱스", "LineId", "화자", "내용"],
                ["IF", "신뢰높음", null, null, null, null],
                target
            ]
            :
            [
                ["유형", "조건라벨", "인덱스", "LineId", "화자", "내용"],
                ["IF", "신뢰높음", null, null, null, null],
                target,
                ["ENDIF", null, null, null, null, null]
            ];
    }

    private ChapterDiagnostic SingleError(string?[][] rows, string contains)
    {
        List<ChapterDiagnostic> matched = Read(rows).Errors
            .Where(item => item.Message.Contains(contains, StringComparison.Ordinal))
            .ToList();

        return Assert.Single(matched);
    }

    private EpisodeWorkbookModel Read(string?[][] rows)
    {
        string path = Path.Combine(_folder, $"ep_{Guid.NewGuid():N}.xlsx");

        using (var book = new XLWorkbook())
        {
            IXLWorksheet sheet = book.AddWorksheet("대본");

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

            book.SaveAs(path);
        }

        return EpisodeWorkbookReader.Read(path, Labels);
    }
}
