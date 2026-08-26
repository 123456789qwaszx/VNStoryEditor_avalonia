using ClosedXML.Excel;
using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>챕터 제거는 이름을 지고 있는 것 넷을 함께 걷는다</b> (2026-08-25 소유자: "챕터를
/// 만들 수는 있는데, 삭제할 방법이 없어 … 당연하지만, 그렇게 하면 연출그래프에 있던 것도
/// 모두 자동으로 제거되도록").
///
/// ⛔ 그전에는 "폴더에서 사람이 지우세요"였다. 그 길은 <b>절반만 지운다</b> — 파일은
/// 사라지는데 판과 노드는 프로젝트에 남아, 이름이 겹치는 번들과 "부르는 노드가
/// YarnProject에 없다"로 뒤늦게 터졌다. <b>사람이 손으로 못 지우는 쪽이 남는 것</b>이
/// 문제였으므로 여기서 재는 것도 그 남는 쪽이다.
/// </summary>
public sealed class ChapterDeleterTests : IDisposable
{
    private const string Doomed = "ch05";
    private const string Kept = "ch06";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "vn-chapter-delete", Guid.NewGuid().ToString("N"));

    public ChapterDeleterTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ── 요청 그 자체 ────────────────────────────────────────────────────────

    [Fact]
    public void 판과_그_위의_노드가_함께_걷힌다()
    {
        // "연출그래프에 있던 것도 모두 자동으로 제거되도록" — 에피소드 노드도, 조건 공급
        // 배관도 그 판에 서 있으므로 판이 걷히면 함께 간다.
        World world = Build();

        ChapterDeleter.Result result =
            ChapterDeleter.Delete(world.Editor, world.ProjectPath, Doomed);

        Assert.True(result.Deleted, result.Failure);
        Assert.True(result.NodesRemoved > 0);

        Assert.DoesNotContain(world.Editor.Project.Files, file =>
            string.Equals(file.Name, Doomed, StringComparison.Ordinal));

        // 배관이 프로젝트 어디에도 안 남는다 — 남으면 다음 동기화가 옛 챕터를 되살린다.
        Assert.DoesNotContain(
            world.Editor.Project.EnumerateNodes().OfType<SetNode>(),
            node => string.Equals(
                node.Name, EpisodeSyncService.ConditionSupplyNodeName(Doomed), StringComparison.Ordinal));

        // 남긴 챕터는 안 건드린다.
        Assert.Contains(world.Editor.Project.Files, file =>
            string.Equals(file.Name, Kept, StringComparison.Ordinal));
    }

    [Fact]
    public void 워크북과_대본이_bak으로_남는다()
    {
        // 챕터 하나는 몇 달치 원고일 수 있다. 되돌리기가 없는 저장소라, 지우는 종류의
        // 작업은 늘 직전 상태를 같은 이름의 `.bak`으로 남긴다.
        World world = Build();

        ChapterDeleter.Result result =
            ChapterDeleter.Delete(world.Editor, world.ProjectPath, Doomed);

        Assert.True(result.Deleted, result.Failure);

        string chapters = ChapterLibrary.FolderFor(world.ProjectPath)!;
        Assert.False(File.Exists(Path.Combine(chapters, Doomed + ".xlsx")));
        Assert.True(File.Exists(Path.Combine(chapters, Doomed + ".xlsx.bak")));

        string episodes = EpisodeLibrary.FolderFor(world.ProjectPath, Doomed)!;
        Assert.False(Directory.Exists(episodes));
        Assert.True(Directory.Exists(episodes + ".bak"));

        // 원고가 그대로다 — 민 것이지 지운 것이 아니다.
        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(
            Path.Combine(episodes + ".bak", "main05.02.xlsx"));

        Assert.Equal("복도는 조용했다", model.Rows.Single(row => row.IsLine).Text);

        // 무엇이 남았는지 이름으로 말한다 — "지웠습니다"만 오면 원고가 사라진 줄 안다.
        Assert.Equal(Doomed + ".xlsx.bak", result.WorkbookBackup);
        Assert.Equal(Doomed + ".bak", result.EpisodesBackup);
    }

    [Fact]
    public void 챕터_목록에서도_사라진다()
    {
        // 판만 걷고 워크북을 두면 다음에 폴더를 훑을 때 그 챕터가 <b>되살아난다</b>.
        World world = Build();

        ChapterDeleter.Delete(world.Editor, world.ProjectPath, Doomed);

        IReadOnlyList<ChapterEntry> chapters =
            ChapterLibrary.Load(ChapterLibrary.FolderFor(world.ProjectPath));

        Assert.Equal([Kept], chapters.Select(entry => entry.ChapterId).ToArray());
    }

    // ── 고치면서 생긴 사고 자리 ─────────────────────────────────────────────

    [Fact]
    public void 마지막_판은_지우지_않고_파일에도_손대지_않는다()
    {
        // 판이 하나뿐이면 새 노드가 갈 자리가 없어진다(ProjectEditor의 오랜 불변식).
        //
        // ⚠ 중요한 것은 <b>거절보다 그 시점</b>이다. 파일을 먼저 밀고 나서 거절하면
        //    워크북은 사라졌는데 판은 남는 — 이 기능이 고치려던 바로 그 상태가 된다.
        World world = Build(secondBoard: false);

        ChapterDeleter.Result result =
            ChapterDeleter.Delete(world.Editor, world.ProjectPath, Doomed);

        Assert.False(result.Deleted);
        Assert.Contains("마지막 판", result.Failure!, StringComparison.Ordinal);

        string chapters = ChapterLibrary.FolderFor(world.ProjectPath)!;
        Assert.True(File.Exists(Path.Combine(chapters, Doomed + ".xlsx")));
        Assert.True(Directory.Exists(EpisodeLibrary.FolderFor(world.ProjectPath, Doomed)!));
        Assert.Contains(world.Editor.Project.Files, file =>
            string.Equals(file.Name, Doomed, StringComparison.Ordinal));
    }

    [Fact]
    public void 대본_폴더를_못_치우면_워크북을_되돌린다()
    {
        // 반쯤 지운 챕터를 남기지 않는다. 워크북만 사라지면 원고는 폴더에 살아 있는데
        // 툴에서는 안 보인다 — 사람이 잃은 줄 알게 되는 상태다.
        World world = Build();

        string episodes = EpisodeLibrary.FolderFor(world.ProjectPath, Doomed)!;

        // 엑셀이 대본을 열고 있는 상황을 흉내 낸다 — 폴더가 안 옮겨진다.
        using (var _ = new FileStream(
                   Path.Combine(episodes, "main05.02.xlsx"),
                   FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ChapterDeleter.Result result =
                ChapterDeleter.Delete(world.Editor, world.ProjectPath, Doomed);

            Assert.False(result.Deleted);
        }

        string chapters = ChapterLibrary.FolderFor(world.ProjectPath)!;
        Assert.True(File.Exists(Path.Combine(chapters, Doomed + ".xlsx")));
        Assert.False(File.Exists(Path.Combine(chapters, Doomed + ".xlsx.bak")));
        Assert.Contains(world.Editor.Project.Files, file =>
            string.Equals(file.Name, Doomed, StringComparison.Ordinal));
    }

    [Fact]
    public void 내보낸_진행_JSON은_지우지_않고_이름을_말한다()
    {
        // 산출물이라도 남의 폴더(개발자가 가져가는 자리)에 있는 파일이다. 그냥 두면
        // 런타임이 없는 챕터를 계속 싣게 되므로, 지우는 대신 <b>이름을 말한다</b>.
        World world = Build();

        string export = ChapterExportService.ExportPathFor(world.ProjectPath, Doomed);
        Directory.CreateDirectory(Path.GetDirectoryName(export)!);
        File.WriteAllText(export, "{}");

        ChapterDeleter.Result result =
            ChapterDeleter.Delete(world.Editor, world.ProjectPath, Doomed);

        Assert.True(result.Deleted, result.Failure);
        Assert.Equal(Path.GetFileName(export), result.StaleExport);
        Assert.True(File.Exists(export));
    }

    [Fact]
    public void 지난번_bak이_있어도_원고_쪽을_집는다()
    {
        // ⛔ `ch05.xls*`는 지난번에 남긴 `ch05.xlsx.bak`도 잡고, 열거 순서는 정해져 있지
        //    않다. 그것을 집으면 `.bak.bak`을 만들고 원고는 그 자리에 남아 — 지웠다고
        //    말한 챕터가 <b>다음 새로고침에 되살아난다</b>. 목록이 곧 증거다.
        World world = Build();

        string chapters = ChapterLibrary.FolderFor(world.ProjectPath)!;
        File.WriteAllText(Path.Combine(chapters, Doomed + ".xlsx.bak"), "지난번 것");

        Assert.True(ChapterDeleter.Delete(world.Editor, world.ProjectPath, Doomed).Deleted);

        Assert.False(File.Exists(Path.Combine(chapters, Doomed + ".xlsx")));
        Assert.False(File.Exists(Path.Combine(chapters, Doomed + ".xlsx.bak.bak")));

        Assert.Equal(
            [Kept],
            ChapterLibrary.Load(chapters).Select(entry => entry.ChapterId).ToArray());
    }

    [Fact]
    public void 없는_챕터는_조용히_성공하지_않는다()
    {
        World world = Build();

        ChapterDeleter.Result result =
            ChapterDeleter.Delete(world.Editor, world.ProjectPath, "없는챕터");

        Assert.False(result.Deleted);
        Assert.Contains("없는챕터", result.Failure!, StringComparison.Ordinal);
    }

    // ── 무대 ────────────────────────────────────────────────────────────────

    private sealed record World(ProjectEditor Editor, string ProjectPath, string FileId);

    /// <summary>챕터 워크북 + 대본 하나 + 판 + 조건 배관이 선 프로젝트.</summary>
    /// <param name="secondBoard">
    /// 남길 챕터를 하나 더 세운다. 판이 하나뿐이면 제거가 <b>정당하게</b> 거절되므로,
    /// 지워지는 길을 재려면 둘이 있어야 한다.
    /// </param>
    private World Build(bool secondBoard = true)
    {
        string projectPath = Path.Combine(_root, "예제.vnproject.json");

        var project = new StoryProject { Title = "제거" };
        var board = new StoryFile("sf_ch05", Doomed, "story/ch05.vnstory.json");
        project.Files.Add(board);

        if (secondBoard)
        {
            project.Files.Add(new StoryFile("sf_ch06", Kept, "story/ch06.vnstory.json"));
        }

        ProjectStore.Save(projectPath, project);

        var editor = new ProjectEditor(project);

        string chapters = ChapterLibrary.FolderFor(projectPath)!;
        Directory.CreateDirectory(chapters);
        WriteChapter(Path.Combine(chapters, Doomed + ".xlsx"));

        if (secondBoard)
        {
            WriteChapter(Path.Combine(chapters, Kept + ".xlsx"));
        }

        string episodes = EpisodeLibrary.FolderFor(projectPath, Doomed)!;
        Directory.CreateDirectory(episodes);
        WriteScript(Path.Combine(episodes, "main05.02.xlsx"));

        ChapterGraphModel chapter = ChapterWorkbookReader.Read(Path.Combine(chapters, Doomed + ".xlsx"));

        EpisodeSyncService.Sync(
            editor, GameDefinition.Empty, board.Id,
            Path.Combine(episodes, "main05.02.xlsx"), chapter);

        // 배관이 실제로 섰는지 확인하고 시작한다 — 없으면 첫 테스트가 헛돈다.
        Assert.Single(
            editor.Project.EnumerateNodes().OfType<SetNode>(),
            node => EpisodeSyncService.IsConditionSupplyNodeName(node.Name));

        return new World(editor, projectPath, board.Id);
    }

    private static void WriteChapter(string path)
    {
        using var book = new XLWorkbook();

        Sheet(book, ChapterSheetNames.Episodes,
            ["EpisodeId", "대사엔트리", "제목", "이벤트키", "X", "Y"],
            [["main05.02", "장면_1", "조용한 복도", null, "0", "0"]]);

        Sheet(book, ChapterSheetNames.Edges,
            ["출발", "도착", "스탯변화", "선택지", "표시조건", "해금조건"], []);

        Sheet(book, ChapterSheetNames.Conditions,
            ["라벨", "스탯", "연산자", "값", "설명"],
            [["신뢰높음", "trust", ">=", "3", "라루를 신뢰"]]);

        Sheet(book, ChapterSheetNames.Stats,
            ["타입", "스탯키", "표시명", "초기값", "최소", "최대"],
            [[null, "trust", "신뢰", "0", "0", "5"]]);

        Sheet(book, ChapterSheetNames.Choices, ["인덱스", "대본", "메모"], []);

        book.SaveAs(path);
    }

    private static void WriteScript(string path)
    {
        using var book = new XLWorkbook();

        Sheet(book, "대본",
            ["유형", "조건라벨", "인덱스", "LineId", "화자", "내용"],
            [
                ["IF", "신뢰높음", null, null, null, null],
                [null, null, "10", null, "윌로", "복도는 조용했다"],
                ["ENDIF", null, null, null, null, null]
            ]);

        book.SaveAs(path);
    }

    private static void Sheet(XLWorkbook book, string name, string[] headers, string?[][] rows)
    {
        IXLWorksheet sheet = book.AddWorksheet(name);

        for (int column = 0; column < headers.Length; column++)
        {
            sheet.Cell(1, column + 1).SetValue(headers[column]);
        }

        for (int row = 0; row < rows.Length; row++)
        {
            for (int column = 0; column < rows[row].Length; column++)
            {
                if (rows[row][column] is { Length: > 0 } value)
                {
                    sheet.Cell(row + 2, column + 1).SetValue(value);
                }
            }
        }
    }
}
