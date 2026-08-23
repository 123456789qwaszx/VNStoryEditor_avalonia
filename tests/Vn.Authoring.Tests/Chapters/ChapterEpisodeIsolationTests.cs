using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 챕터별 대본 격리 (2026-08-16 소유자 보고) — "다른 챕터의 에피소드를 눌렀는데 기존 이름이
/// 같은 대본 엑셀이 열린다."
///
/// EpisodeId는 <b>챕터 안에서만</b> 유일하다. 두 축이 그 사실을 몰라 한 파일·한 노드를
/// 공유하고 있었다: ① 대본 파일 경로(<c>episodes/{Id}.xlsx</c>) ② 동기화의 대사노드 조회
/// (프로젝트 전체를 이름으로 훑음). 둘 다 챕터 범위로 좁혔다.
/// </summary>
public sealed class ChapterEpisodeIsolationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-episode-isolation", Guid.NewGuid().ToString("N"));

    public ChapterEpisodeIsolationTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string Root => Path.Combine(_directory, EpisodeLibrary.FolderName);

    [Fact]
    public void 같은_이름의_에피소드가_챕터마다_다른_파일을_갖는다()
    {
        string chapterA = Path.Combine(Root, "ch00");
        string chapterB = Path.Combine(Root, "ch01");

        Assert.True(EpisodeLibrary.EnsureWorkbook(chapterA, "new01"));
        Assert.True(EpisodeLibrary.EnsureWorkbook(chapterB, "new01")); // 두 번째도 정말 만들어진다

        string fileA = EpisodeLibrary.FindExisting(chapterA, "new01")!;
        string fileB = EpisodeLibrary.FindExisting(chapterB, "new01")!;

        Assert.NotEqual(fileA, fileB);

        // 한쪽 원고를 고쳐도 다른 쪽은 그대로다 — 이것이 보고된 버그의 핵심이다.
        using (var workbook = new ClosedXML.Excel.XLWorkbook(fileA))
        {
            workbook.Worksheet("대본").Cell(2, 6).SetValue("ch00의 대사");
            workbook.Save();
        }

        using var reread = new ClosedXML.Excel.XLWorkbook(fileB);
        Assert.Equal(string.Empty, reread.Worksheet("대본").Cell(2, 6).GetString());
    }

    [Fact]
    public void 챕터_폴더는_서로의_파일을_찾지_않는다()
    {
        string chapterA = Path.Combine(Root, "ch00");
        EpisodeLibrary.EnsureWorkbook(chapterA, "시작에피소드");

        // 다른 챕터 폴더에서는 그 이름이 없다 — 없으면 새로 만드는 것이 옳다.
        Assert.Null(EpisodeLibrary.FindExisting(Path.Combine(Root, "ch01"), "시작에피소드"));
    }

    // ── 구판 평면 파일 입양 ─────────────────────────────────────────────────

    [Fact]
    public void 주인이_하나면_평면_대본을_그_챕터_폴더로_옮긴다()
    {
        Directory.CreateDirectory(Root);
        EpisodeLibrary.EnsureWorkbook(Root, "old01"); // 구판 — 뿌리에 평평하게

        EpisodeLibrary.FlatAdoption adoption =
            EpisodeLibrary.AdoptFlatWorkbook(Root, "ch00", "old01", claimants: 1);

        Assert.True(adoption.Adopted);
        Assert.Null(adoption.Problem);
        Assert.NotNull(EpisodeLibrary.FindExisting(Path.Combine(Root, "ch00"), "old01"));
        Assert.Empty(Directory.GetFiles(Root, "old01.xlsx")); // 뿌리에는 안 남는다

        // 두 번째 부름은 할 일이 없다.
        Assert.False(EpisodeLibrary.AdoptFlatWorkbook(Root, "ch00", "old01", 1).Adopted);
    }

    [Fact]
    public void 여러_챕터가_같은_이름을_쓰면_옮기지_않고_말한다()
    {
        Directory.CreateDirectory(Root);
        EpisodeLibrary.EnsureWorkbook(Root, "겹친이름");

        EpisodeLibrary.FlatAdoption adoption =
            EpisodeLibrary.AdoptFlatWorkbook(Root, "ch00", "겹친이름", claimants: 2);

        Assert.False(adoption.Adopted);
        Assert.Contains("어느 챕터의 원고로 볼지", adoption.Problem);

        // 남의 원고를 가져가지 않는다 — 파일은 뿌리에 그대로 있다.
        Assert.Single(Directory.GetFiles(Root, "겹친이름.xlsx"));
    }

    // ── 대사노드 조회 ───────────────────────────────────────────────────────

    [Fact]
    public void 동기화는_그_챕터의_판_안에서만_노드를_찾는다()
    {
        // 두 판(챕터)에 같은 이름의 대사노드가 있어도, 동기화는 자기 판의 노드만 채운다.
        var project = new StoryProject();
        var boardA = new StoryFile("sf_a", "ch00", "story/a.vnstory.json");
        var boardB = new StoryFile("sf_b", "ch01", "story/b.vnstory.json");
        project.Files.Add(boardA);
        project.Files.Add(boardB);

        int next = 0;
        var editor = new ProjectEditor(project, newLineId: () => $"ln_{++next:D3}");

        // ch00 판에 'new01' 노드가 이미 서 있다(다른 챕터의 원고를 담은 노드).
        DialogueNode standing = editor.AddDialogueNode(boardA.Id, name: "new01");

        string workbook = Path.Combine(_directory, "new01.xlsx");
        WriteEpisode(workbook, "라루", "ch01의 대사다.");

        EpisodeSyncReport report = EpisodeSyncService.Sync(
            editor, GameDefinition.Empty, boardB.Id, workbook, chapter: null);

        Assert.True(report.Applied);

        // ch01 판에 새 노드가 섰고, ch00의 노드는 건드리지 않았다.
        DialogueNode created = (DialogueNode)project.FindNode(report.DialogueNodeId!)!;
        Assert.Equal(boardB.Id, project.FindFileContainingNode(created.Id)!.Id);
        Assert.NotEqual(standing.Id, created.Id);

        // ch00 노드에는 엑셀에서 온 줄이 하나도 없다(신원 맵이 비어 있다) — 남의 원고가
        // 쏟아지지 않았다는 뜻이다. 새 노드에는 그 줄이 있다.
        Assert.Empty(standing.ExcelLineMap);
        Assert.Equal(10, Assert.Single(created.ExcelLineMap).Key);
    }

    private static void WriteEpisode(string path, string speaker, string text)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        ClosedXML.Excel.IXLWorksheet sheet = workbook.AddWorksheet("대본");
        string[] headers = ["인덱스", "유형", "LineId", "조건라벨", "화자", "내용"];

        for (int column = 0; column < headers.Length; column++)
        {
            sheet.Cell(1, column + 1).SetValue(headers[column]);
        }

        sheet.Cell(2, 1).SetValue(10);
        sheet.Cell(2, 5).SetValue(speaker);
        sheet.Cell(2, 6).SetValue(text);

        workbook.SaveAs(path);
    }
}
