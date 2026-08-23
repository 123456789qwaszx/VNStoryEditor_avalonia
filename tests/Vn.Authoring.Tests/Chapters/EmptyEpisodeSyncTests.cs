using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 대사가 한 줄도 없는 에피소드도 <b>작가의 판에 노드로 선다</b> (2026-08-17 소유자 보고:
/// "엑셀을 만들더라도 시나리오 그래프에 반영되게 하려면 최소한 한 줄의 대사는 있어야 해.
/// 이게 진짜 엄청 헷갈려").
///
/// 예전에는 노드를 만들기 <b>전에</b> 되돌아갔다 — 기획자가 에피소드를 만들어도 작가의
/// 판에는 아무것도 없었고, 작가는 무엇을 기다려야 하는지 알 수 없었다.
/// </summary>
public sealed class EmptyEpisodeSyncTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-empty-episode", Guid.NewGuid().ToString("N"));

    public EmptyEpisodeSyncTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    [Fact]
    public void 빈_대본도_노드가_서고_배지에는_세지_않는다()
    {
        var project = new StoryProject();
        var file = new StoryFile("sf_sync", "테스트", "story/sync.vnstory.json");
        project.Files.Add(file);
        var editor = new ProjectEditor(project);
        ChapterGraphModel chapter = ChapterWorkbookReader.Read(SamplePath);

        // 툴이 새 에피소드에 만들어 주는 것과 같은 빈 워크북 — 머리글만 있고 대사가 없다.
        Assert.True(EpisodeLibrary.EnsureWorkbook(_directory, "main05.02"));
        string workbook = EpisodeLibrary.FindExisting(_directory, "main05.02")!;

        EpisodeSyncReport report = EpisodeSyncService.Sync(
            editor, GameDefinition.Empty, file.Id, workbook, chapter);

        // 노드가 선다 — 작가가 "여기에 쓰면 된다"를 볼 수 있다.
        Assert.NotNull(report.DialogueNodeId);
        DialogueNode node = Assert.Single(project.EnumerateNodes().OfType<DialogueNode>());
        Assert.Equal(report.DialogueNodeId, node.Id);
        Assert.Equal("main05.02", node.ExcelEpisodeId); // 본문은 엑셀 소유라는 표식도 선다

        // 아직 쓰지 않은 것은 잘못이 아니다 — 거부도 경고도 아니고 배지에 세지 않는다.
        Assert.False(report.Applied);
        Assert.True(report.NotYetWritten);
        Assert.False(report.HasErrors);
        Assert.Equal(0, report.RejectionCount);
    }

    [Fact]
    public void 대사를_적으면_같은_노드에_반영된다()
    {
        // 빈 채로 선 노드가 나중에 대사를 받는다 — 노드가 새로 생기지 않는다(신원 유지).
        var project = new StoryProject();
        var file = new StoryFile("sf_sync", "테스트", "story/sync.vnstory.json");
        project.Files.Add(file);
        var editor = new ProjectEditor(project);
        ChapterGraphModel chapter = ChapterWorkbookReader.Read(SamplePath);

        EpisodeLibrary.EnsureWorkbook(_directory, "main05.02");
        string workbook = EpisodeLibrary.FindExisting(_directory, "main05.02")!;

        string firstNodeId = EpisodeSyncService
            .Sync(editor, GameDefinition.Empty, file.Id, workbook, chapter).DialogueNodeId!;

        using (var book = new ClosedXML.Excel.XLWorkbook(workbook))
        {
            ClosedXML.Excel.IXLWorksheet sheet = book.Worksheet("대본");
            sheet.Cell(2, 3).SetValue(10);   // v14 — 인덱스는 C열
            sheet.Cell(2, 5).SetValue("윌로");
            sheet.Cell(2, 6).SetValue("이제 한 줄 있다");
            book.Save();
        }

        EpisodeSyncReport second = EpisodeSyncService.Sync(
            editor, GameDefinition.Empty, file.Id, workbook, chapter);

        Assert.True(second.Applied);
        Assert.Equal(firstNodeId, second.DialogueNodeId);
        Assert.Single(project.EnumerateNodes().OfType<DialogueNode>());
    }
}
