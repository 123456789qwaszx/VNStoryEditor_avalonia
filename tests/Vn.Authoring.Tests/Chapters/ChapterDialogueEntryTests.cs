using ClosedXML.Excel;

using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 챕터의 에피소드가 <b>판의 어느 대사 노드로 재생되는가</b> — 계약의
/// <c>DialogueEntryId</c>가 판을 따라가는가.
///
/// ⛔ 이 파일은 한때 <b>간선에 매달린 연출 씬</b>이 계약의 `ViaNodeId`로 나가는지를 쟀다.
/// 그 칸은 2026-09-17에 <b>양쪽에서 걷혔고</b>(같은 재생 순서를 에피소드 한 칸으로 말할
/// 수 있다), 옛 프로젝트 호환도 안 본다(소유자: *"이전의 프로젝트들은 모두 버리고"*).
/// 남은 것은 <b>이름의 주인이 판이다</b>를 지키는 검사들이다.
/// </summary>
public sealed class ChapterDialogueEntryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-entry-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 비어_있는_에피소드도_오류로_막는다()
    {
        // 소유자 보고 (2026-08-25): "YarnProject 자체가 대사노드가 텅 비어있으면 아예
        // 넣지를 않네." 그래서 빈 에피소드는 재생 시작 자체를 막는다 — 챕터 그래프에서
        // 세워야 유니티까지 안 간다.
        ChapterGraphModel chapter = BuildChapter();

        var project = new StoryProject();
        var file = new StoryFile(name: chapter.ChapterId);

        foreach (ChapterEpisode episode in chapter.Episodes)
        {
            // 판에는 노드가 다 있는데 대본이 하나도 없는 상태.
            file.Nodes.Add(new DialogueNode(name: episode.EpisodeId)
            {
                MarkedEpisodeId = episode.EpisodeId
            });
        }

        project.Files.Add(file);

        ChapterValidationResult validation =
            ChapterValidator.Validate(chapter, episodesFolder: null, project);

        // 에피소드마다 제 행에서 선다 — 세 개가 비었으면 세 줄이다.
        Assert.Equal(
            3,
            validation.All.Count(item => item.Code == ChapterDiagnosticCode.EpisodeSceneEmpty));

        ChapterDiagnostic problem = Assert.Single(
            validation.All,
            item => item.Code == ChapterDiagnosticCode.EpisodeSceneEmpty &&
                item.Message.Contains("'시작'의", StringComparison.Ordinal));

        Assert.Equal(ChapterDiagnosticSeverity.Error, problem.Severity);
        Assert.Equal(ChapterSheetNames.Episodes, problem.Sheet);
        Assert.DoesNotContain("YarnProject", problem.Message);   // 사정은 코드 주석의 것이다
        Assert.Contains("더블클릭", problem.Message);   // 고치는 법을 말한다
    }

    [Fact]
    public void 판에서_노드를_개명해도_DialogueEntryId가_따라간다()
    {
        // ⛔ 이것이 "진행 JSON이 부르는 노드가 YarnProject에 없다"의 뿌리였다 (2026-08-25).
        //    이름의 주인이 둘이었다 — 내보내기는 엑셀의 `대사엔트리` 글자로 짓고, .yarn은
        //    판 노드의 이름으로 선다. 판에서 개명하는 순간 둘이 갈리고, 로드·검증·증명은
        //    전부 통과하는데 재생만 안 된다.
        ChapterGraphModel chapter = BuildChapter();

        var project = new StoryProject();
        var file = new StoryFile(name: chapter.ChapterId);
        var excel = new DialogueNode(name: "Id를_바꿧음") { MarkedEpisodeId = "시작" };

        file.Nodes.Add(excel);
        project.Files.Add(file);

        ChapterExportResult result =
            ChapterProgressionExporter.Export(chapter, episodesFolder: null, project);

        using var document = System.Text.Json.JsonDocument.Parse(result.Json!);

        string entry = document.RootElement
            .GetProperty("Nodes").EnumerateArray()
            .Single(node => node.GetProperty("EpisodeId").GetString() == "시작")
            .GetProperty("DialogueEntryId").GetString()!;

        // 엑셀은 아직 `시작`이라고 적혀 있지만, 재생될 yarn 노드는 개명된 쪽이다.
        Assert.Equal("Id를_바꿧음", entry);
    }

    [Fact]
    public void 판을_못_보면_엑셀_글자로_되돌아간다()
    {
        // 챕터 모델만으로 부르는 자리(CLI·테스트)가 그대로 살아 있어야 한다.
        ChapterExportResult result =
            ChapterProgressionExporter.Export(BuildChapter(), episodesFolder: null);

        using var document = System.Text.Json.JsonDocument.Parse(result.Json!);

        Assert.Equal(
            "시작",
            document.RootElement
                .GetProperty("Nodes").EnumerateArray()
                .Single(node => node.GetProperty("EpisodeId").GetString() == "시작")
                .GetProperty("DialogueEntryId").GetString());
    }

    /// <summary>내보낸 JSON에서 그 길의 <c>ViaNodeId</c>를 꺼낸다.</summary>
    private ChapterGraphModel BuildChapter() => ChapterWorkbookReader.Read(WriteWorkbook());

    private string WriteWorkbook()
    {
        Directory.CreateDirectory(_directory);

        string path = Path.Combine(_directory, "ch01.xlsx");

        if (File.Exists(path))
        {
            return path;
        }

        using var workbook = new XLWorkbook();

        Sheet(workbook, ChapterSheetNames.Episodes,
            ["EpisodeId", "대사엔트리", "제목", "이벤트키", "X", "Y", "메모"],
            [
                ["시작", "시작", "복도", null, "0", "0", null],
                ["믿는길", "믿는길", "믿는다", null, "1", "0", null],
                ["혼자길", "혼자길", "혼자 간다", null, "1", "1", null]
            ]);

        Sheet(workbook, ChapterSheetNames.Edges,
            [
                "출발", "도착", "스탯변화", "선택지", "표시조건", "해금조건",
                "잠금 안내문"
            ],
            [
                ["시작", "믿는길", null, "믿는다", null, null, null, null],
                ["시작", "혼자길", null, "혼자 간다", null, null, null, null],
                ["믿는길", "혼자길", null, "계속", null, null, null, null],
                ["혼자길", "믿는길", null, "계속", null, null, null, null]
            ]);

        Sheet(workbook, ChapterSheetNames.Conditions, ["라벨", "스탯", "연산자", "값", "설명"], []);
        Sheet(workbook, ChapterSheetNames.Stats,
            ["타입", "스탯키", "표시명", "초기값", "최소", "최대"], []);
        Sheet(workbook, ChapterSheetNames.Speakers, ["이름", "캐릭터키", "메모"], []);
        Sheet(workbook, ChapterSheetNames.Choices, ["인덱스", "대본", "메모"], []);

        workbook.SaveAs(path);

        return path;
    }

    private static void Sheet(
        XLWorkbook workbook, string name, string[] headers, string?[][] rows)
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
