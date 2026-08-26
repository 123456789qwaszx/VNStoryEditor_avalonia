using ClosedXML.Excel;
using Vn.Authoring.Chapters;
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
/// 조건 공급 노드 <c>챕터 {Id} 조건</c>.
///
/// ⚠ <b>이 파일의 첫 두 테스트가 신고 그 자체다.</b> 나머지는 고치면서 새로 생긴 사고
/// 자리(반쯤 바뀐 챕터·이름 충돌)를 막는다.
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
            .Where(node => EpisodeSyncService.IsConditionSupplyNodeName(node.Name))
            .Select(node => node.Name)
            .ToList();

        Assert.Equal([EpisodeSyncService.ConditionSupplyNodeName(NewId)], supplies);
    }

    [Fact]
    public void 개명_뒤_동기화가_배관을_하나_더_만들지_않는다()
    {
        // 위 둘을 이어 붙인 자리 — 이름만 맞춰 두고 동기화가 또 만들면 고친 것이 아니다.
        World world = Build();

        ChapterRenamer.Rename(world.Editor, world.ProjectPath, OldId, NewId);

        ChapterGraphModel chapter = ChapterWorkbookReader.Read(
            Path.Combine(ChapterLibrary.FolderFor(world.ProjectPath)!, NewId + ".xlsx"));

        EpisodeSyncService.Sync(
            world.Editor, GameDefinition.Empty, world.FileId,
            EpisodeLibrary.FindExisting(EpisodeLibrary.FolderFor(world.ProjectPath, NewId)!, "main05.02")!,
            chapter);

        Assert.Single(
            world.Editor.Project.EnumerateNodes().OfType<SetNode>(),
            node => EpisodeSyncService.IsConditionSupplyNodeName(node.Name));
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

    // ── 고치면서 생긴 사고 자리 ─────────────────────────────────────────────

    [Fact]
    public void 대본_폴더를_못_옮기면_이름을_되돌린다()
    {
        // ⛔ 반쯤 바뀐 챕터가 가장 나쁘다 — 워크북만 새 이름이면 그 챕터는 "대사가 하나도
        //    없는 챕터"로 보이고, 노드를 열면 빈 워크북이 새로 생겨 원고가 둘로 갈린다.
        //    이 건의 원래 증상 그대로다.
        World world = Build();

        string script = EpisodeLibrary.FindExisting(
            EpisodeLibrary.FolderFor(world.ProjectPath, OldId)!, "main05.02")!;

        // 엑셀이 대본을 잡고 있는 상태 — 폴더가 안 옮겨진다.
        using (new FileStream(script, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            ChapterRenamer.Result result =
                ChapterRenamer.Rename(world.Editor, world.ProjectPath, OldId, NewId);

            Assert.False(result.Renamed);
            Assert.Contains("되돌렸습니다", result.Failure);
        }

        // 옛 이름 그대로 서 있다 — 워크북도, 대본 폴더도, 판도.
        Assert.True(File.Exists(Path.Combine(ChapterLibrary.FolderFor(world.ProjectPath)!, OldId + ".xlsx")));
        Assert.True(Directory.Exists(EpisodeLibrary.FolderFor(world.ProjectPath, OldId)!));
        Assert.Contains(world.Editor.Project.Files, file => file.Name == OldId);
    }

    [Fact]
    public void 새_이름의_대본_폴더가_이미_있으면_멈춘다()
    {
        // 덮어쓰면 남의 원고가 사라진다 — 옮기기 전에 본다.
        World world = Build();
        Directory.CreateDirectory(EpisodeLibrary.FolderFor(world.ProjectPath, NewId)!);

        ChapterRenamer.Result result =
            ChapterRenamer.Rename(world.Editor, world.ProjectPath, OldId, NewId);

        Assert.False(result.Renamed);
        Assert.Contains("이미 있습니다", result.Failure);

        // 아무것도 안 건드렸다 — 워크북 이름도 그대로다.
        Assert.True(File.Exists(Path.Combine(ChapterLibrary.FolderFor(world.ProjectPath)!, OldId + ".xlsx")));
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

        EpisodeSyncService.Sync(
            editor, GameDefinition.Empty, board.Id,
            Path.Combine(episodes, "main05.02.xlsx"), chapter);

        // 배관이 실제로 섰는지 확인하고 시작한다 — 없으면 위 두 테스트가 헛돈다.
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
