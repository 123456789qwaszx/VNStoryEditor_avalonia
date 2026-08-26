using ClosedXML.Excel;

using Vn.Authoring.Chapters;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 간선에 매달린 <b>자유 씬</b>이 계약의 <c>ViaNodeId</c>로 나가는가 (2026-08-24).
///
/// 배선의 원본은 워크북이 아니라 <b>연출 그래프</b>다 — 시나리오 작가가 엑셀노드의
/// 선택지 포트에 커스텀 대사 노드를 잇고, 그 값은 <see cref="DialogueNode.ChoiceExits"/>에
/// 산다. 챕터 구조(엑셀)는 한 글자도 안 바뀐다.
/// </summary>
public sealed class ChapterViaSceneTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-via-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 선택지에_매단_자유_씬이_ViaNodeId로_나간다()
    {
        ChapterGraphModel chapter = BuildChapter();

        // 연출 그래프: 엑셀노드 `시작`의 선택지 `믿는다`에 자유 씬을 매단다.
        var project = new StoryProject();
        var file = new StoryFile(name: "ch01");
        var excel = new DialogueNode(name: "시작") { ExcelEpisodeId = "시작" };
        var scene = new DialogueNode(name: "전이 어둠");

        excel.ChoiceExits["믿는다"] = scene.Id;
        file.Nodes.Add(excel);
        file.Nodes.Add(scene);
        project.Files.Add(file);

        ChapterExportResult result =
            ChapterProgressionExporter.Export(chapter, episodesFolder: null, project);

        Assert.False(result.Refused);

        Assert.Equal("전이_어둠", ViaOf(result.Json!, "시작", "믿는다"));

        // 매달지 않은 길은 그대로 빈 문자열 — 런타임이 곧장 도착 에피소드로 간다.
        Assert.Equal(string.Empty, ViaOf(result.Json!, "시작", "혼자 간다"));
    }

    [Fact]
    public void 이름에_공백이_있으면_이미터_규칙을_지난다()
    {
        // ⚠ 손으로 이으면 갈리는 자리다. 런타임은 이 글자로 YarnProject에서 노드를 찾고,
        // 산출된 .yarn 쪽 타이틀은 SanitizeNodeName을 지나 `장면 1` → `장면_1`이 된다.
        ChapterGraphModel chapter = BuildChapter();

        var project = new StoryProject();
        var file = new StoryFile(name: "ch01");
        var excel = new DialogueNode(name: "시작") { ExcelEpisodeId = "시작" };
        var scene = new DialogueNode(name: "장면 1");

        excel.ChoiceExits["믿는다"] = scene.Id;
        file.Nodes.Add(excel);
        file.Nodes.Add(scene);
        project.Files.Add(file);

        ChapterExportResult result =
            ChapterProgressionExporter.Export(chapter, episodesFolder: null, project);

        Assert.Equal("장면_1", ViaOf(result.Json!, "시작", "믿는다"));
    }

    [Fact]
    public void 프로젝트를_안_넘기면_전부_빈다()
    {
        // 챕터 모델만으로 부르는 자리(CLI·테스트)가 그대로 살아 있어야 한다.
        ChapterExportResult result =
            ChapterProgressionExporter.Export(BuildChapter(), episodesFolder: null);

        Assert.False(result.Refused);
        Assert.Equal(string.Empty, ViaOf(result.Json!, "시작", "믿는다"));
    }

    [Fact]
    public void 배선이_끊긴_자리는_조용히_비운다()
    {
        // 씬을 지웠는데 배선이 남은 경우. 없는 이름을 실으면 런타임의 사전 대조가
        // 재생 자체를 막는다 — 빈 채로 내고 곧장 도착 에피소드로 가는 편이 낫다.
        ChapterGraphModel chapter = BuildChapter();

        var project = new StoryProject();
        var file = new StoryFile(name: "ch01");
        var excel = new DialogueNode(name: "시작") { ExcelEpisodeId = "시작" };

        excel.ChoiceExits["믿는다"] = "지워진노드";
        file.Nodes.Add(excel);
        project.Files.Add(file);

        ChapterExportResult result =
            ChapterProgressionExporter.Export(chapter, episodesFolder: null, project);

        Assert.Equal(string.Empty, ViaOf(result.Json!, "시작", "믿는다"));
    }

    [Fact]
    public void 엑셀은_한_글자도_안_바뀐다()
    {
        // 이 기능의 요구 자체다 — 작가가 마음대로 떼고 붙여도 챕터 구조가 흔들리면 안 된다.
        string path = WriteWorkbook();
        byte[] before = File.ReadAllBytes(path);

        ChapterGraphModel chapter = ChapterWorkbookReader.Read(path);

        var project = new StoryProject();
        var file = new StoryFile(name: "ch01");
        var excel = new DialogueNode(name: "시작") { ExcelEpisodeId = "시작" };
        var scene = new DialogueNode(name: "전이 어둠");

        excel.ChoiceExits["믿는다"] = scene.Id;
        file.Nodes.Add(excel);
        file.Nodes.Add(scene);
        project.Files.Add(file);

        ChapterProgressionExporter.Export(chapter, episodesFolder: null, project);

        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void 비어_있는_연출_씬은_챕터_검증이_오류로_막는다()
    {
        // ⛔ 처음에는 경고로 두었다 — "빈 씬도 들어갔다 곧장 나오면 그만"이라고 봤다.
        //    그런데 Yarn 컴파일러가 <b>줄 없는 노드를 YarnProject에 아예 넣지 않는다</b>.
        //    그러면 진행 JSON이 부르는 이름이 저쪽에 없어 재생이 통째로 막힌다 —
        //    빈 씬 하나가 챕터 전체를 못 돌게 하므로 내보내기 전에 세워야 한다.
        ChapterGraphModel chapter = BuildChapter();

        var project = new StoryProject();
        var file = new StoryFile(name: chapter.ChapterId);
        var excel = new DialogueNode(name: "시작") { ExcelEpisodeId = "시작" };
        var scene = new DialogueNode(name: "빈 전이");   // 대본이 없다 = 재생할 줄이 없다

        excel.ChoiceExits["믿는다"] = scene.Id;
        file.Nodes.Add(excel);
        file.Nodes.Add(scene);
        project.Files.Add(file);

        ChapterValidationResult validation =
            ChapterValidator.Validate(chapter, episodesFolder: null, project);

        ChapterDiagnostic problem = Assert.Single(
            validation.All, item => item.Code == ChapterDiagnosticCode.ViaSceneEmpty);

        Assert.Equal(ChapterDiagnosticSeverity.Error, problem.Severity);
        Assert.Equal(ChapterSheetNames.Edges, problem.Sheet);
        Assert.Contains("빈 전이", problem.Message);
        Assert.Contains("재생할 줄이 하나도 없습니다", problem.Message);
        // "왜 치명적인가"(YarnProject 사정)는 문구에서 뺐다 (2026-08-26 소유자:
        // "굳이 말할 필요 없어") — 기획자에게는 고치는 법 한 문장이면 된다.
        Assert.DoesNotContain("YarnProject", problem.Message);
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
                ExcelEpisodeId = episode.EpisodeId
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
    public void 대사가_비어도_줄이_있으면_경고하지_않는다()
    {
        // 순수 연출 씬의 정상 모양이다 — 커맨드는 줄에 매달리므로 줄은 있어야 한다.
        // 본문이 빈 줄 하나하나는 이미터가 따로 짚는다.
        ChapterGraphModel chapter = BuildChapter();

        var project = new StoryProject();
        var file = new StoryFile(name: chapter.ChapterId);
        var excel = new DialogueNode(name: "시작") { ExcelEpisodeId = "시작" };
        project.Files.Add(file);
        file.Nodes.Add(excel);

        var script = new ScriptDocument(name: "전이 대본");
        script.Lines.Add(new ScriptLine("ln_via"));
        project.Scripts.Add(script);

        var scene = new DialogueNode(name: "전이") { ScriptId = script.Id };
        file.Nodes.Add(scene);
        excel.ChoiceExits["믿는다"] = scene.Id;

        ChapterValidationResult validation =
            ChapterValidator.Validate(chapter, episodesFolder: null, project);

        Assert.DoesNotContain(
            validation.All, item => item.Code == ChapterDiagnosticCode.ViaSceneEmpty);
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
        var excel = new DialogueNode(name: "Id를_바꿧음") { ExcelEpisodeId = "시작" };

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
    private static string ViaOf(string json, string fromEpisodeId, string choiceLabel)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);

        return document.RootElement
            .GetProperty("Nodes")
            .EnumerateArray()
            .Single(node => node.GetProperty("EpisodeId").GetString() == fromEpisodeId)
            .GetProperty("NextOptions")
            .EnumerateArray()
            .Single(option => option.GetProperty("ChoiceLabel").GetString() == choiceLabel)
            .GetProperty("ViaNodeId")
            .GetString()!;
    }

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
