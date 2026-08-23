using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 연출 그래프에서 고친 대사가 <b>엑셀 셀까지 간다</b> (2026-08-24 소유자: "때때로는
/// 이곳에서도 대사를 편집하는게 편하다").
///
/// <b>이 파일이 지키는 것은 단 하나다</b> — 고친 글이 <em>다음 동기화에 살아남는가</em>.
/// 잠금만 풀고 노드만 고치면 <see cref="EpisodeSyncService.Sync"/>가 워크북 값으로
/// 덮어써서 글이 증발한다. 그래서 왕복 테스트(고친다 → 다시 동기화한다 → 그대로인가)가
/// 이 기능의 유일한 진짜 증명이고, 나머지는 그 주위의 가드다.
/// </summary>
public sealed class EpisodeLineEditorTests : IDisposable
{
    private static readonly GameDefinition Definition = GameDefinition.Parse("""
        { "speakers": [ { "name": "라루", "characterId": "laru" }, { "name": "윌로", "characterId": "willo" } ] }
        """)!;

    private const string ChapterId = "ch05";
    private const string EpisodeId = "main05.02";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "vn-episode-line-editor", Guid.NewGuid().ToString("N"));

    public EpisodeLineEditorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    private string ProjectPath => Path.Combine(_root, "예제.vnproject.json");

    // ── 이 기능의 이유 ──────────────────────────────────────────────────────

    [Fact]
    public void 여기서_고친_대사가_다음_동기화에도_살아남는다()
    {
        // ⛔ 이것이 무너지면 기능 전체가 거짓말이다 — 고쳐지는 척하다 증발하는 화면.
        World world = Build();

        string lineId = FirstLineId(world);
        EpisodeLineTarget target = Locate(world, lineId)!;

        Assert.True(EpisodeLineEditor.Write(target, "윌로", "여기서 고친 대사").Written);

        // 동기화는 워크북을 다시 읽어 노드에 밀어 넣는다. 우리가 워크북을 고쳤으므로
        // 되돌릴 것이 없다 — 오히려 이 동기화가 고친 글을 노드로 실어 온다.
        Sync(world);

        LocalizedLine line = LineOf(world, lineId);

        Assert.Equal("여기서 고친 대사", line.Text);
        Assert.Equal("윌로", line.Speaker);
    }

    [Fact]
    public void 노드만_고치면_다음_동기화가_지운다()
    {
        // ⚠ <b>이것이 되쓰기가 있어야 하는 이유다.</b> 잠금을 풀고 노드만 고치는 길은
        // 여기서 증발한다 — 위 테스트가 그저 "워크북을 고치면 워크북 값이 온다"는
        // 동어반복이 아니라는 것도 이 테스트가 보인다.
        //
        // 이 줄이 깨지는 날은 동기화가 덮어쓰기를 그만둔 날이고, 그러면 되쓰기의 근거도
        // 다시 봐야 한다.
        World world = Build();

        string lineId = FirstLineId(world);
        string original = LineOf(world, lineId).Text;

        world.Editor.SetScriptLineText(
            world.Node.ScriptId!, lineId, "윌로", "노드에만 적은 글", locale: null);

        Assert.Equal("노드에만 적은 글", LineOf(world, lineId).Text);

        Sync(world);

        Assert.Equal(original, LineOf(world, lineId).Text);
    }

    [Fact]
    public void 워크북_셀_자체가_바뀐다()
    {
        // 노드만 보면 "동기화가 아직 안 돈 것"과 구분이 안 된다 — 파일을 직접 본다.
        World world = Build();

        string lineId = FirstLineId(world);
        EpisodeLineTarget target = Locate(world, lineId)!;

        EpisodeLineEditor.Write(target, "윌로", "셀에 적힌 글");

        EpisodeRow row = EpisodeWorkbookReader.Read(world.WorkbookPath).Rows
            .Single(candidate => candidate.Index == target.Index);

        Assert.Equal("셀에 적힌 글", row.Text);
        Assert.Equal("윌로", row.Speaker);
    }

    // ── 되쓸 곳이 없으면 열지 않는다 ────────────────────────────────────────

    [Fact]
    public void 엑셀노드가_아니면_자리를_못_찾는다()
    {
        // 자유 노드는 애초에 툴 소유라 이 길을 지날 이유가 없다.
        World world = Build();
        DialogueNode free = world.Editor.AddDialogueNode(world.FileId, name: "자유 씬");

        Assert.Null(EpisodeLineEditor.Locate(
            world.Editor.Project, ProjectPath, free, "ln_0001"));
    }

    [Fact]
    public void 신원이_없는_줄은_자리를_못_찾는다()
    {
        // 워크북에서 실려 오지 않은 줄 — 되쓸 칸이 없다. 여기서 null이 아니면
        // 엉뚱한 행을 덮는다.
        World world = Build();

        Assert.Null(Locate(world, "ln_없는줄"));
    }

    [Fact]
    public void 프로젝트를_아직_저장하지_않았으면_자리를_못_찾는다()
    {
        // 대본 폴더는 매니페스트 옆에 산다 — 매니페스트가 없으면 폴더도 없다.
        World world = Build();

        Assert.Null(EpisodeLineEditor.Locate(
            world.Editor.Project, projectPath: null, world.Node, FirstLineId(world)));
    }

    [Fact]
    public void 같은_이름의_에피소드라도_제_챕터의_워크북을_짚는다()
    {
        // 챕터마다 같은 EpisodeId를 따로 쓸 수 있다 (2026-08-16) — 챕터를 거치지 않고
        // 파일을 찾으면 남의 챕터 원고를 고친다.
        World world = Build();

        string otherFolder = Path.Combine(_root, "episodes", "ch09");
        Directory.CreateDirectory(otherFolder);
        File.Copy(SamplePath, Path.Combine(otherFolder, EpisodeId + ".xlsx"));

        EpisodeLineTarget target = Locate(world, FirstLineId(world))!;

        Assert.Equal(
            Path.GetFullPath(world.WorkbookPath),
            Path.GetFullPath(target.WorkbookPath));
    }

    // ── 열어 준 적 없는 문 ──────────────────────────────────────────────────

    [Fact]
    public void 조건_블록_행에는_쓰지_않는다()
    {
        // 화자·내용 두 칸만 열었다. IF·ENDIF 행에 글을 쓰면 리더가 그 블록을 다르게 읽는다.
        World world = Build();

        EpisodeRow block = EpisodeWorkbookReader.Read(world.WorkbookPath).Rows
            .First(row => row.Kind is not EpisodeRowKind.Dialogue);

        ChapterWriteResult result =
            EpisodeWorkbookWriter.SetLine(world.WorkbookPath, block.Index, "라루", "밀어 넣기");

        Assert.False(result.Written);
        Assert.Contains("조건 블록은 엑셀에서 고칩니다", result.Failure);
    }

    [Fact]
    public void 없는_인덱스에는_쓰지_않고_사유를_말한다()
    {
        // 엑셀에서 그 줄이 지워졌을 수 있다 — 조용히 아무 데나 쓰는 것이 최악이다.
        World world = Build();

        ChapterWriteResult result =
            EpisodeWorkbookWriter.SetLine(world.WorkbookPath, 999_999, "라루", "허공");

        Assert.False(result.Written);
        Assert.Contains("999999", result.Failure!.Replace(",", string.Empty));
    }

    [Fact]
    public void 엑셀이_잡고_있으면_쓰지_않고_그_사실을_말한다()
    {
        // 챕터 워크북과 같은 규칙이다 — 반쯤 쓴 워크북은 없다.
        World world = Build();
        EpisodeLineTarget target = Locate(world, FirstLineId(world))!;

        ChapterWriteResult result;

        using (new FileStream(
                   world.WorkbookPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = EpisodeLineEditor.Write(target, "라루", "못 들어간다");
        }

        Assert.False(result.Written);
        Assert.Contains("엑셀이", result.Failure);

        // 그리고 파일은 그대로다.
        EpisodeRow row = EpisodeWorkbookReader.Read(world.WorkbookPath).Rows
            .Single(candidate => candidate.Index == target.Index);

        Assert.NotEqual("못 들어간다", row.Text);
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private sealed record World(
        ProjectEditor Editor, string FileId, ChapterGraphModel Chapter,
        string WorkbookPath, DialogueNode Node);

    /// <summary>챕터 하나 · 그 판 · 그 에피소드의 대본 — 그리고 한 번 동기화해 둔 상태.</summary>
    private World Build()
    {
        var project = new StoryProject();

        // ⚠ 판 이름 = ChapterId (챕터=판 1:1, G-1 v2). Locate가 이 이름으로 대본 폴더를 찾는다.
        var file = new StoryFile("sf_board", ChapterId, "story/ch05.vnstory.json");
        project.Files.Add(file);

        int next = 0;
        var editor = new ProjectEditor(project, newLineId: () => $"ln_new_{++next:D3}");

        string folder = Path.Combine(_root, "episodes", ChapterId);
        Directory.CreateDirectory(folder);

        string workbook = Path.Combine(folder, EpisodeId + ".xlsx");
        File.Copy(SamplePath, workbook);

        ChapterGraphModel chapter = ChapterWorkbookReader.Read(SamplePath);

        EpisodeSyncReport report =
            EpisodeSyncService.Sync(editor, Definition, file.Id, workbook, chapter);

        Assert.True(report.Applied, string.Join(" / ", report.Problems));

        var node = (DialogueNode)editor.Project.FindNode(report.DialogueNodeId)!;

        return new World(editor, file.Id, chapter, workbook, node);
    }

    private void Sync(World world) => EpisodeSyncService.Sync(
        world.Editor, Definition, world.FileId, world.WorkbookPath, world.Chapter);

    private EpisodeLineTarget? Locate(World world, string lineId) =>
        EpisodeLineEditor.Locate(world.Editor.Project, ProjectPath, world.Node, lineId);

    /// <summary>워크북에서 실려 온 첫 줄 — 신원이 있어야 되쓸 자리가 있다.</summary>
    private static string FirstLineId(World world) => world.Node.ExcelLineMap
        .OrderBy(pair => pair.Key)
        .Select(pair => pair.Value)
        .First(lineId => world.Editor.Project.FindScript(world.Node.ScriptId!)!.ActiveLines
            .Any(line => string.Equals(line.Id, lineId, StringComparison.Ordinal)));

    /// <summary>
    /// 그 줄의 화자·대사. <b>본문은 줄이 아니라 로케일이 갖는다</b> — LineId를 키로 쓰는
    /// 덕분에 언어를 더해도 논리가 가리키는 Id가 안 바뀐다(`ScriptDocument` 주석).
    /// </summary>
    private static LocalizedLine LineOf(World world, string lineId)
    {
        ScriptDocument script = world.Editor.Project.FindScript(world.Node.ScriptId!)!;

        Assert.Contains(script.ActiveLines, line =>
            string.Equals(line.Id, lineId, StringComparison.Ordinal));

        return script.Locales
            .Single(locale => locale.Locale == script.PrimaryLocale)
            .Find(lineId);
    }
}
