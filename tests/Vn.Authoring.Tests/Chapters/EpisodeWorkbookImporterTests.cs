using ClosedXML.Excel;
using Vn.Authoring.Chapters;
using Vn.Authoring.Chapters.Import;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// R-D — 대본 워크북 → 대사노드의 <b>한 번뿐인</b> 관문
/// (<c>docs/work-orders/tool-owns-workbooks-orders.md</c> §5).
///
/// <b>이 묶음이 지키는 것은 "한 번"과 "전부 아니면 전무"다.</b> 동기화 시절에는 워크북이
/// 바뀔 때마다 다시 읽고 맞췄으므로 절반만 들어와도 다음 회차가 고쳐 줬다. 임포트는 다음
/// 회차가 없다 — 절반만 들어온 프로젝트는 무엇이 원본인지 사람이 알 수 없게 만든다.
/// </summary>
public sealed class EpisodeWorkbookImporterTests : IDisposable
{
    private static readonly GameDefinition Definition = GameDefinition.Parse("""
        { "speakers": [ { "name": "라루", "characterId": "laru" },
                        { "name": "윌로", "characterId": "willo" } ] }
        """)!;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-episode-import", Guid.NewGuid().ToString("N"));

    public EpisodeWorkbookImporterTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    // ── 들여온다 ────────────────────────────────────────────────────────────

    [Fact]
    public void 대본이_대사노드로_선다()
    {
        World world = Build("ep01", "ep02");
        WriteScript("ep01", ("10", "윌로", "복도는 조용했다."), ("20", "라루", "같이 갈까?"));
        WriteScript("ep02", ("10", "윌로", "문이 열렸다."));

        EpisodeImport import = Run(world);

        Assert.True(import.Applied, Say(import));
        Assert.Equal(["ep01", "ep02"], import.Entries.Select(entry => entry.EpisodeId));

        Assert.Equal(
            ["복도는 조용했다.", "같이 갈까?"],
            TextOf(world, import.Entries[0]));
    }

    [Fact]
    public void 대본이_없는_에피소드는_건너뛴다()
    {
        // 아직 대본을 안 쓴 에피소드다 — 오류가 아니다.
        World world = Build("ep01", "ep02");
        WriteScript("ep01", ("10", "윌로", "한 줄"));

        EpisodeImport import = Run(world);

        Assert.True(import.Applied, Say(import));
        Assert.Equal(["ep01"], import.Entries.Select(entry => entry.EpisodeId));
    }

    [Fact]
    public void 구판_워크북은_이행하고_그_사실을_알린다()
    {
        World world = Build("ep01");
        WriteLegacyScriptWithBlock("ep01");

        EpisodeImport import = Run(world);

        Assert.True(import.Applied, Say(import));
        Assert.Contains(import.Notices, notice => notice.Contains("걷었습니다", StringComparison.Ordinal));

        // 블록 안에 있던 대사는 남는다 — 사라지는 글이 없어야 한다.
        Assert.Equal(["첫 줄", "조건 안", "끝 줄"], TextOf(world, import.Entries[0]));
    }

    // ── ⛔ 부분 성공하지 않는다 (§5.2) ─────────────────────────────────────

    [Fact]
    public void 하나가_깨지면_아무것도_안_들어온다()
    {
        // ⛔ 이것이 이 클래스가 동기화와 갈라지는 지점이다. 절반만 들여오면 사람은
        //    "어디까지 들어왔나"를 파일과 프로젝트를 대조해 가며 알아내야 한다.
        World world = Build("ep01", "ep02");
        WriteScript("ep01", ("10", "윌로", "멀쩡한 줄"));
        WriteScript("ep02", ("10", "라루", "첫 줄"), ("10", "윌로", "같은 번호를 단 줄"));

        EpisodeImport import = Run(world);

        Assert.False(import.Applied);
        Assert.Empty(import.Entries);
        Assert.Contains(import.Errors, item => item.Message.Contains("중복", StringComparison.Ordinal));

        // 멀쩡했던 ep01조차 프로젝트에 서지 않았다 — 그것이 "전무"의 뜻이다.
        Assert.Empty(world.Editor.Project.EnumerateNodes().OfType<DialogueNode>());
    }

    [Fact]
    public void 조건_블록을_적은_대본도_통째로_막는다()
    {
        // v15 관문(EpisodeConditionBlockRetired)이 임포트에서도 그대로 선다.
        World world = Build("ep01");
        WriteScript("ep01", ("10", "윌로", "첫 줄"), ("20", null, "IF"));

        EpisodeImport import = Run(world);

        Assert.False(import.Applied);
        Assert.Contains(import.Errors, item =>
            item.Code == ChapterDiagnosticCode.EpisodeConditionBlockRetired);
    }

    // ── 한 번뿐이다 ────────────────────────────────────────────────────────

    [Fact]
    public void 들여온_뒤_워크북을_고쳐도_프로젝트는_안_변한다()
    {
        // ⛔ §5.2의 불변식. 임포트 뒤 그 프로젝트는 다시 읽지 않는다 — 이것이 뒤집기의
        //    전부다. 감시자가 사라진 자리를 이 테스트가 지킨다.
        World world = Build("ep01");
        WriteScript("ep01", ("10", "윌로", "들여온 줄"));

        EpisodeImport import = Run(world);
        Assert.True(import.Applied, Say(import));

        WriteScript("ep01", ("10", "윌로", "엑셀에서 나중에 고친 줄"));

        Assert.Equal(["들여온 줄"], TextOf(world, import.Entries[0]));
    }

    [Fact]
    public void 같은_에피소드를_두_번_들여와도_노드가_하나다()
    {
        World world = Build("ep01");
        WriteScript("ep01", ("10", "윌로", "한 줄"));

        Assert.True(Run(world).Applied);
        Assert.True(Run(world).Applied);

        Assert.Single(world.Editor.Project.EnumerateNodes().OfType<DialogueNode>());
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private sealed record World(ProjectEditor Editor, string FileId, ChapterGraphModel Chapter);

    private string EpisodesFolder => Path.Combine(_directory, "episodes", "ch01");

    private World Build(params string[] episodeIds)
    {
        var project = new StoryProject();
        var file = new StoryFile("sf_ch01", "ch01", "story/ch01.vnstory.json");
        project.Files.Add(file);

        int next = 0;
        var editor = new ProjectEditor(project, newLineId: () => $"ln_new_{++next:D3}");

        var chapter = new ChapterGraphModel(
            chapterId: "ch01",
            sourcePath: "chapters/ch01.xlsx",
            episodes: [.. episodeIds.Select((id, index) => new ChapterEpisode(
                EpisodeId: id,
                Title: id,
                Index: string.Empty,
                DialogueEntry: id,
                X: index,
                Y: 0,
                Memo: null,
                SourceRow: index + 2))],
            edges: [],
            conditions: [],
            stats: [],
            fixtures: [],
            diagnostics: []);

        Directory.CreateDirectory(EpisodesFolder);

        return new World(editor, file.Id, chapter);
    }

    private EpisodeImport Run(World world) => EpisodeWorkbookImporter.Run(
        world.Editor, Definition, world.FileId, EpisodesFolder, world.Chapter);

    private void WriteScript(string episodeId, params (string? Index, string? Speaker, string? Text)[] rows)
    {
        string path = Path.Combine(EpisodesFolder, episodeId + ".xlsx");

        using var book = new XLWorkbook();
        IXLWorksheet sheet = book.AddWorksheet("대본");

        string[] headers = ["인덱스", "LineId", "화자", "내용"];

        for (int column = 0; column < headers.Length; column++)
        {
            sheet.Cell(1, column + 1).SetValue(headers[column]);
        }

        for (int row = 0; row < rows.Length; row++)
        {
            (string? index, string? speaker, string? text) = rows[row];

            Set(sheet, row + 2, 1, index);
            Set(sheet, row + 2, 3, speaker);
            Set(sheet, row + 2, 4, text);
        }

        book.SaveAs(path);
    }

    /// <summary>v14 6열 · 조건 블록 포함 — 이행기가 걷어야 할 모양.</summary>
    private void WriteLegacyScriptWithBlock(string episodeId)
    {
        string path = Path.Combine(EpisodesFolder, episodeId + ".xlsx");

        using var book = new XLWorkbook();
        IXLWorksheet sheet = book.AddWorksheet("대본");

        string?[][] rows =
        [
            ["유형", "조건라벨", "인덱스", "LineId", "화자", "내용"],
            [null, null, "10", "ln_0001", "윌로", "첫 줄"],
            ["IF", "신뢰높음", null, null, null, null],
            [null, null, "40", "ln_0002", "라루", "조건 안"],
            ["ENDIF", null, null, null, null, null],
            [null, null, "90", "ln_0003", "윌로", "끝 줄"]
        ];

        for (int row = 0; row < rows.Length; row++)
        {
            for (int column = 0; column < rows[row].Length; column++)
            {
                Set(sheet, row + 1, column + 1, rows[row][column]);
            }
        }

        book.SaveAs(path);
    }

    private static void Set(IXLWorksheet sheet, int row, int column, string? value)
    {
        if (value is { Length: > 0 })
        {
            sheet.Cell(row, column).SetValue(value);
        }
    }

    /// <summary>그 노드의 대사 본문 — 로케일이 갖는다.</summary>
    private static IEnumerable<string> TextOf(World world, EpisodeImportEntry entry)
    {
        var node = (DialogueNode)world.Editor.Project.FindNode(entry.DialogueNodeId)!;
        ScriptDocument script = world.Editor.Project.FindScript(node.ScriptId!)!;
        ScriptLocale primary = script.Locales.Single(locale => locale.Locale == script.PrimaryLocale);

        return script.ActiveLines.Select(line => primary.Find(line.Id).Text);
    }

    private static string Say(EpisodeImport import) =>
        string.Join(" / ", import.Diagnostics.Select(item => item.Message));
}
