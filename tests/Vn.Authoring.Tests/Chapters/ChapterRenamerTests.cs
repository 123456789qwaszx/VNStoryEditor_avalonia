using ClosedXML.Excel;
using Vn.Authoring.Chapters;
using Vn.Authoring.Chapters.Import;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>챕터 개명은 이름을 지고 있는 것 넷을 함께 옮긴다</b> (2026-08-24 소유자 보고:
/// "챕터의 이름을 바꾸니까, 에피소드들의 대화가 없다고 나오고, 엑셀도 새로운게 열리네.
/// 또 연출그래프에서도 조건노드가 이전의 챕터를 받고 있어").
///
/// 옮겨야 하는 넷: <c>chapters/{Id}.xlsx</c> · <c>episodes/{Id}/</c> · 판 이름 ·
/// 조건 공급 노드 <c>챕터 {Id} 조건</c>. 2026-10-29부터는 <b>다섯</b> — <c>Project.Chapters</c>의
/// Id가 먼저다(`docs/plans/excel-boundary.md` C2). 모델 셋(Id·판·배관)은 되돌리기 한 칸이고,
/// 파일 둘은 산출물이라 못 옮겨도 모델을 되돌리지 않는다.
///
/// ⚠ <b>이 파일의 첫 두 테스트가 신고 그 자체다.</b> 나머지는 고치면서 새로 생긴 사고
/// 자리(옛 파일이 남는 경우·이름 충돌)를 막는다.
/// </summary>
public sealed class ChapterRenamerTests : IDisposable
{
    private const string OldId = "ch05";
    private const string NewId = "ch05_다시";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "vn-chapter-rename", Guid.NewGuid().ToString("N"));

    public ChapterRenamerTests() => Directory.CreateDirectory(_root);

    // ⛔ 정적 캐시를 지우지 않는다 — 나란히 도는 다른 클래스의 것까지 지운다. 열쇠가
    // 내용 해시라 지울 이유도 없다(파일을 쓰면 저절로 빗나간다).
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ── 신고 ────────────────────────────────────────────────────────────────

    [Fact]
    public void 대본_폴더가_함께_옮겨진다()
    {
        // ⛔ 이것이 "에피소드들의 대화가 없다"의 정체다. 대본은 2026-08-16부터 챕터별
        //    하위 폴더에 사는데 개명이 워크북 파일만 옮겼다 — 새 이름의 폴더가 비어 있으니
        //    툴은 대사가 없다고 말하고, 노드를 더블클릭하면 <b>빈 워크북을 새로 만들어</b>
        //    열었다. 원고는 옛 폴더에 그대로 살아 있는데도.
        World world = Build();

        Assert.True(ChapterRenamer.Rename(world.Editor, world.ProjectPath, OldId, NewId).Renamed);

        string? folder = EpisodeLibrary.FolderFor(world.ProjectPath, NewId);

        Assert.NotNull(EpisodeLibrary.FindExisting(folder!, "main05.02"));
        Assert.False(Directory.Exists(EpisodeLibrary.FolderFor(world.ProjectPath, OldId)!));

        // 원고가 그대로다 — 옮긴 것이지 새로 만든 것이 아니다.
        EpisodeWorkbookModel model = EpisodeWorkbookReader.Read(
            EpisodeLibrary.FindExisting(folder!, "main05.02")!);

        Assert.Equal("복도는 조용했다", model.Rows.Single(row => row.IsLine).Text);
    }

    [Fact]
    public void 조건_공급_노드의_이름이_따라간다()
    {
        // ⛔ "연출그래프에서도 조건노드가 이전의 챕터를 받고 있어" — 옛 이름의 배관이 판에
        //    남으면 다음 동기화가 새 이름으로 <b>하나 더</b> 만들어 조건이 둘로 갈린다.
        World world = Build();

        Assert.True(ChapterRenamer.Rename(world.Editor, world.ProjectPath, OldId, NewId).Renamed);

        List<string> supplies = world.Editor.Project.EnumerateNodes().OfType<SetNode>()
            .Where(node => ChapterBoardSupply.IsConditionSupplyNodeName(node.Name))
            .Select(node => node.Name)
            .ToList();

        Assert.Equal([ChapterBoardSupply.ConditionSupplyNodeName(NewId)], supplies);
    }

    [Fact]
    public void 개명_뒤_동기화가_배관을_하나_더_만들지_않는다()
    {
        // 위 둘을 이어 붙인 자리 — 이름만 맞춰 두고 동기화가 또 만들면 고친 것이 아니다.
        World world = Build();

        ChapterRenamer.Rename(world.Editor, world.ProjectPath, OldId, NewId);

        ChapterGraphModel chapter = ChapterWorkbookReader.Read(
            Path.Combine(ChapterLibrary.FolderFor(world.ProjectPath)!, NewId + ".xlsx"));

        EpisodeWorkbookImporter.Run(
            world.Editor, GameDefinition.Empty, world.FileId,
            EpisodeLibrary.FolderFor(world.ProjectPath, NewId)!, chapter);

        ChapterBoardSupply.SupplyChapterConditionsToBoard(
            world.Editor, GameDefinition.Empty, world.FileId, chapter);

        Assert.Single(
            world.Editor.Project.EnumerateNodes().OfType<SetNode>(),
            node => ChapterBoardSupply.IsConditionSupplyNodeName(node.Name));
    }

    [Fact]
    public void 판_이름도_따라간다()
    {
        // 챕터 = 판 1:1 (G-1 v2). 판 이름이 곧 챕터 Id라, 어긋나면 배관 판정도 흔들린다.
        World world = Build();

        ChapterRenamer.Rename(world.Editor, world.ProjectPath, OldId, NewId);

        Assert.Contains(world.Editor.Project.Files, file => file.Name == NewId);
        Assert.DoesNotContain(world.Editor.Project.Files, file => file.Name == OldId);
    }

    [Fact]
    public void 챕터_모델의_Id가_바뀐다()
    {
        // ⛔ 2026-10-29까지 여기가 빠져 있었다 — 파일·판·배관은 새 이름인데 Project.Chapters는
        //    옛 이름이라, 저장하고 다시 열면 챕터 목록은 옛 이름이고 판만 새 이름이었다.
        World world = Build();

        Assert.True(ChapterRenamer.Rename(world.Editor, world.ProjectPath, OldId, NewId).Renamed);

        Assert.NotNull(world.Editor.FindChapter(NewId));
        Assert.Null(world.Editor.FindChapter(OldId));
        Assert.Contains(world.Editor.FindChapter(NewId)!.Episodes, episode => episode.EpisodeId == "main05.02");
    }

    [Fact]
    public void 개명은_되돌리기_한_칸이다()
    {
        // 사용자 동작 하나 = 되돌리기 한 번. Id·판·배관이 각각 한 칸이면 Ctrl+Z 한 번에 셋이
        // 어긋난 채 멈춘다.
        World world = Build();

        ChapterRenamer.Rename(world.Editor, world.ProjectPath, OldId, NewId);
        world.Editor.Undo();

        Assert.NotNull(world.Editor.FindChapter(OldId));
        Assert.Null(world.Editor.FindChapter(NewId));
        Assert.Contains(world.Editor.Project.Files, file => file.Name == OldId);
        Assert.Contains(
            world.Editor.Project.EnumerateNodes().OfType<SetNode>(),
            node => node.Name == ChapterBoardSupply.ConditionSupplyNodeName(OldId));
    }

    [Fact]
    public void 프로젝트에_없는_챕터는_개명하지_않는다()
    {
        World world = Build();

        ChapterRenamer.Result result =
            ChapterRenamer.Rename(world.Editor, world.ProjectPath, "없는챕터", NewId);

        Assert.False(result.Renamed);
        Assert.Contains("프로젝트에 없습니다", result.Failure);
    }

    // ── 고치면서 생긴 사고 자리 ─────────────────────────────────────────────

    [Fact]
    public void 대본_폴더를_못_옮겨도_모델은_바뀌고_옛_파일은_사유와_함께_남는다()
    {
        // 파일은 산출물이다 — 프로젝트에서 다시 낸다. 엑셀이 잡고 있어 못 옮긴 것은 개명의
        // 실패가 아니라 "그 파일을 지금 옮기지 못했다"이고, 옛 파일은 사람이 치운다.
        // (2026-10-29 전에는 모델까지 되돌렸다 — 원본이 파일이던 시절의 규칙이다.)
        World world = Build();

        string script = EpisodeLibrary.FindExisting(
            EpisodeLibrary.FolderFor(world.ProjectPath, OldId)!, "main05.02")!;

        ChapterRenamer.Result result;

        // 엑셀이 대본을 잡고 있는 상태 — 폴더가 안 옮겨진다.
        using (new FileStream(script, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = ChapterRenamer.Rename(world.Editor, world.ProjectPath, OldId, NewId);
        }

        Assert.True(result.Renamed);
        Assert.False(result.EpisodesMoved);
        Assert.Contains("대본 폴더를 옮기지 못했습니다", result.FilesNotMoved);

        // 모델은 새 이름이다 — 챕터도, 판도.
        Assert.NotNull(world.Editor.FindChapter(NewId));
        Assert.Contains(world.Editor.Project.Files, file => file.Name == NewId);

        // 워크북은 옮겨졌고(잠긴 건 대본이다), 옛 대본 폴더는 그대로 남아 있다 — 지우지 않는다.
        Assert.True(File.Exists(Path.Combine(ChapterLibrary.FolderFor(world.ProjectPath)!, NewId + ".xlsx")));
        Assert.True(Directory.Exists(EpisodeLibrary.FolderFor(world.ProjectPath, OldId)!));
    }

    [Fact]
    public void 새_이름의_대본_폴더가_이미_있으면_옮기지_않고_말한다()
    {
        // 덮어쓰면 남의 원고가 사라진다 — 그 폴더는 건드리지 않고, 모델만 바꾸고 사유를 말한다.
        World world = Build();
        Directory.CreateDirectory(EpisodeLibrary.FolderFor(world.ProjectPath, NewId)!);

        ChapterRenamer.Result result =
            ChapterRenamer.Rename(world.Editor, world.ProjectPath, OldId, NewId);

        Assert.True(result.Renamed);
        Assert.False(result.EpisodesMoved);
        Assert.Contains("이미 있어", result.FilesNotMoved);
        Assert.NotNull(world.Editor.FindChapter(NewId));

        // 옛 폴더도 새 폴더도 그대로다.
        Assert.True(Directory.Exists(EpisodeLibrary.FolderFor(world.ProjectPath, OldId)!));
        Assert.True(Directory.Exists(EpisodeLibrary.FolderFor(world.ProjectPath, NewId)!));
    }

    [Fact]
    public void 아직_저장하지_않은_프로젝트도_챕터를_개명한다()
    {
        // 원본은 프로젝트다 — 옮길 파일이 아직 없을 뿐, 개명을 막을 이유가 아니다.
        World world = Build();

        ChapterRenamer.Result result = ChapterRenamer.Rename(world.Editor, null, OldId, NewId);

        Assert.True(result.Renamed);
        Assert.Null(result.FilesNotMoved);
        Assert.NotNull(world.Editor.FindChapter(NewId));
    }

    [Fact]
    public void 대본_폴더가_아직_없어도_개명은_된다()
    {
        // 에피소드를 하나도 안 쓴 챕터 — 옮길 폴더가 없는 것은 잘못이 아니다.
        World world = Build();
        Directory.Delete(EpisodeLibrary.FolderFor(world.ProjectPath, OldId)!, recursive: true);

        ChapterRenamer.Result result =
            ChapterRenamer.Rename(world.Editor, world.ProjectPath, OldId, NewId);

        Assert.True(result.Renamed);
        Assert.False(result.EpisodesMoved);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private sealed record World(ProjectEditor Editor, string ProjectPath, string FileId);

    /// <summary>챕터 워크북 + 대본 하나 + 판 + 조건 배관이 선 프로젝트.</summary>
    private World Build()
    {
        string projectPath = Path.Combine(_root, "예제.vnproject.json");

        var project = new StoryProject { Title = "개명" };
        var board = new StoryFile("sf_ch05", OldId, "story/ch05.vnstory.json");
        project.Files.Add(board);
        ProjectStore.Save(projectPath, project);

        var editor = new ProjectEditor(project);

        // 챕터 워크북 — 조건 하나(배관이 설 근거)와 에피소드 하나.
        string chapters = ChapterLibrary.FolderFor(projectPath)!;
        Directory.CreateDirectory(chapters);
        WriteChapter(Path.Combine(chapters, OldId + ".xlsx"));

        // 대본 — 그 조건을 쓰는 IF 하나와 대사 한 줄.
        string episodes = EpisodeLibrary.FolderFor(projectPath, OldId)!;
        Directory.CreateDirectory(episodes);
        WriteScript(Path.Combine(episodes, "main05.02.xlsx"));

        ChapterGraphModel chapter = ChapterWorkbookReader.Read(Path.Combine(chapters, OldId + ".xlsx"));

        // 원본은 프로젝트다 — 챕터 모델이 있어야 개명할 것이 있다.
        editor.EnsureChapter(OldId);
        editor.AddEpisode(OldId, "main05.02", title: "조용한 복도", 0, 0);

        EpisodeWorkbookImporter.Run(
            editor, GameDefinition.Empty, board.Id, episodes, chapter);

        // v15 — 조건 공급 노드를 세우는 것은 <b>챕터 쪽</b>이다. 예전에는 대본의 `조건라벨`을
        // 거두며 Sync가 함께 세웠는데 그 칸이 폐지됐다(EpisodeSyncRunner가 부르는 순서 그대로).
        ChapterBoardSupply.SupplyChapterConditionsToBoard(
            editor, GameDefinition.Empty, board.Id, chapter);

        // 배관이 실제로 섰는지 확인하고 시작한다 — 없으면 위 두 테스트가 헛돈다.
        Assert.Single(
            editor.Project.EnumerateNodes().OfType<SetNode>(),
            node => ChapterBoardSupply.IsConditionSupplyNodeName(node.Name));

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
            ["인덱스", "LineId", "화자", "내용"],
            [
                ["10", null, "윌로", "복도는 조용했다"]
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
