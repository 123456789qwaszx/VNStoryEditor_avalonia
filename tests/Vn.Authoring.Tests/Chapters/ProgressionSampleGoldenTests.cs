using ClosedXML.Excel;
using Vn.Authoring.Chapters;
using Path = System.IO.Path;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// `docs/ch01.progression.sample.json`을 <b>실물 출력으로</b> 붙들어 둔다.
///
/// 그 표본은 다른 저장소(`ked-progression`)가 로더의 첫 입력으로 쓰는 파일이다. 손으로
/// 만들어 두었더니 v11에서 엔딩키가 실리기 시작했는데도 <b>엔딩이 하나도 없는 옛 모양</b>
/// 그대로 남아 있었다 — 남에게 건넨 표본이 낡는 것은 계약서가 낡는 것과 같은 무게다.
///
/// 없으면 쓰고(첫 실행), 있으면 견준다. 규격을 바꿨다면 파일을 지우고 한 번 돌리면 된다.
/// </summary>
public sealed class ProgressionSampleGoldenTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-progression-sample", Guid.NewGuid().ToString("N"));

    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "docs", "ch01.progression.sample.json"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 건네는_표본이_지금_나가는_출력과_같다()
    {
        ChapterExportResult result = ChapterProgressionExporter.Export(
            BuildChapter(), episodesFolder: null);

        Assert.False(result.Refused,
            string.Join(" / ", result.Validation.All.Select(item => item.Message)));

        string produced = result.Json!.ReplaceLineEndings("\n");

        if (!File.Exists(SamplePath))
        {
            File.WriteAllText(SamplePath, produced);
            return;
        }

        Assert.Equal(
            File.ReadAllText(SamplePath).ReplaceLineEndings("\n"),
            produced);
    }

    [Fact]
    public void 표본이_v11_엔딩을_실제로_담고_있다()
    {
        // 골든 비교만으로는 "둘 다 낡은 것"을 못 잡는다. 표본이 무엇을 보여 주기로 한
        // 파일인지를 따로 건다 — 엔딩 둘, 서로 다른 키.
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(SamplePath));

        string[] endings = document.RootElement
            .GetProperty("Nodes")
            .EnumerateArray()
            .Where(node => node.GetProperty("IsChapterEndingCandidate").GetBoolean())
            .Select(node => node.GetProperty("EndingKey").GetString()!)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["ch01_alone", "ch01_true"], endings);

        // ⚠ v12 (2026-08-24) — 저작의 `연출` 칸이 폐지됐다. 계약의 `ViaNodeId`는 남아
        // 있지만 이제 **언제나 빈 문자열**이다. 저쪽 DTO를 바꾸지 않는다 — 안 채울 뿐이다.
        // 표본이 그 사실을 보여 주는 파일이므로 그것도 함께 건다.
        Assert.All(
            document.RootElement
                .GetProperty("Nodes")
                .EnumerateArray()
                .SelectMany(node => node.GetProperty("NextOptions").EnumerateArray()),
            option => Assert.Equal(string.Empty, option.GetProperty("ViaNodeId").GetString()));
    }

    /// <summary>
    /// 표본 챕터 — 갈라졌다 <b>서로 다른 엔딩으로</b> 끝나는 최소 모양.
    ///
    /// 한 파일에서 보여 주려는 것: 스탯 사전 · 스탯 증감이 붙은 선택지 · 문구 없는 자동
    /// 간선 · 그리고 v11의 엔딩키(도착 에피소드에 실린다).
    /// </summary>
    private ChapterGraphModel BuildChapter()
    {
        Directory.CreateDirectory(_directory);

        string path = Path.Combine(_directory, "ch01.xlsx");

        using (var workbook = new XLWorkbook())
        {
            Sheet(workbook, ChapterSheetNames.Episodes,
                ["EpisodeId", "제목", "종류", "대사엔트리", "X", "Y", "메모"],
                [
                    ["시작", "복도", "Main", "시작", "0", "0", null],
                    ["믿는길", "라루를 믿는다", "Main", "믿는길", "1", "0", null],
                    ["혼자길", "혼자 간다", "Main", "혼자길", "1", "1", null],
                    ["좋은끝", "함께 문을 연다", "Main", "좋은끝", "2", "0", null],
                    ["쓸쓸한끝", "혼자 문을 연다", "Main", "쓸쓸한끝", "2", "1", null]
                ]);

            Sheet(workbook, ChapterSheetNames.Edges,
                [
                    // `잠금시 숨김`은 2026-08-24에 폐지됐다 — 잠금 안내문·엔딩키가 한 칸씩 당겨진다.
                    "출발", "도착", "스탯변화", "선택지", "표시조건", "해금조건",
                    "잠금 안내문", "엔딩키"
                ],
                [
                    ["시작", "믿는길", "trust +2", "라루를 믿는다", null, null, null, null],
                    ["시작", "혼자길", "fatigue +1", "혼자 간다", null, null, null, null],
                    ["믿는길", "좋은끝", null, "문을 연다", null, null, null, "ch01_true"],
                    ["혼자길", "쓸쓸한끝", null, "문을 연다", null, null, null, "ch01_alone"]
                ]);

            Sheet(workbook, ChapterSheetNames.Conditions,
                ["라벨", "스탯", "연산자", "값", "설명"], []);

            Sheet(workbook, ChapterSheetNames.Stats,
                ["스탯키", "표시명", "초기값", "최소", "최대", "타입"],
                [
                    ["trust", "신뢰", "0", "0", "10", null],
                    ["fatigue", "피로", "0", "0", "10", null]
                ]);

            Sheet(workbook, ChapterSheetNames.Speakers, ["이름", "캐릭터키", "메모"], []);
            Sheet(workbook, ChapterSheetNames.Choices, ["인덱스", "대본", "메모"], []);

            workbook.SaveAs(path);
        }

        return ChapterWorkbookReader.Read(path);
    }

    private static void Sheet(
        XLWorkbook workbook,
        string name,
        string?[] headers,
        string?[][] rows)
    {
        IXLWorksheet sheet = workbook.Worksheets.Add(name);

        for (int column = 0; column < headers.Length; column++)
        {
            sheet.Cell(1, column + 1).SetValue(headers[column]);
        }

        for (int row = 0; row < rows.Length; row++)
        {
            for (int column = 0; column < rows[row].Length; column++)
            {
                if (rows[row][column] is { } value)
                {
                    sheet.Cell(row + 2, column + 1).SetValue(value);
                }
            }
        }
    }
}
