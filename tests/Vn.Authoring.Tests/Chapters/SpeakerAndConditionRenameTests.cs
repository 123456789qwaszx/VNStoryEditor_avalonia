using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// <b>조건·화자의 이름을 편집해도 연결이 이어진다</b> (2026-08-24 소유자: "조건이나 화자의
/// 이름을 편집한 경우도 마찬가지입니다").
///
/// 둘은 끊어지는 자리가 다르다.
/// <list type="bullet">
///   <item><b>조건</b> — 줄에 매달린 전환은 <c>ConditionId</c>로 잇는다. 그래서 라벨이 바뀌었을
///     때 조건을 <em>새로</em> 만들면 Id가 갈려 이미 매달린 갈래가 전부 고아가 된다. 같은 식이면
///     같은 조건이므로 <b>이름만</b> 갈아 끼운다.</item>
///   <item><b>화자</b> — 붙드는 것이 이름 문자열 하나뿐이다(<c>LocalizedLine.Speaker</c>).
///     등록부에서만 갈면 그 줄들이 전부 미등록이 된다. 2026-10-29부터 바꾸는 것은 <b>프로젝트의 줄</b>이고
///     대본 워크북은 산출물이라 <b>다시 낸다</b>(excel-boundary C4) — 셀을 고치지 않는다.</item>
/// </list>
/// </summary>
public sealed class SpeakerAndConditionRenameTests : IDisposable
{
    private static readonly GameDefinition Definition = GameDefinition.Parse("""
        { "speakers": [ { "name": "라루", "characterId": "laru" } ] }
        """)!;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "vn-rename-carries", Guid.NewGuid().ToString("N"));

    public SpeakerAndConditionRenameTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ── 조건 라벨 ───────────────────────────────────────────────────────────

    /// <summary>스탯 하나를 견주는 챕터 조건 하나짜리 모델. 라벨만 갈아 끼울 수 있다.</summary>
    private static ChapterGraphModel ChapterWith(string label) => new(
        chapterId: "ch01",
        sourcePath: "chapters/ch01.xlsx",
        episodes: [],
        edges: [],
        conditions: [
            new ChapterCondition(
                label,
                "trust >= 3",
                Description: null,
                Parsed: [new ConditionTerm(ConditionTermKind.StatComparison, "trust", ConditionComparison.AtLeast, 3)],
                IsValid: true,
                SourceRow: 2)
        ],
        stats: [],
        fixtures: [],
        diagnostics: []);

    [Fact]
    public void 조건_라벨을_바꾸면_이름만_따라가고_매달린_갈래는_산다()
    {
        // ⛔ 이것이 무너지면 라벨 한 글자에 그 조건을 쓰던 갈래가 전부 "알 수 없는 조건"이 된다.
        var project = new StoryProject();
        var file = new StoryFile("sf_ch01", "ch01", "story/ch01.vnstory.json");
        project.Files.Add(file);

        int next = 0;
        var editor = new ProjectEditor(project, newLineId: () => $"ln_{++next:D3}");

        ScriptDocument script = editor.AddScript("본문");
        ScriptLine line = editor.InsertScriptLine(script.Id);
        DialogueNode dialogue = editor.AddDialogueNode(file.Id, name: "본문", scriptId: script.Id);

        ChapterBoardSupply.SupplyChapterConditionsToBoard(
            editor, Definition, file.Id, ChapterWith("신뢰 높음"));

        SetNode supply = file.Nodes.OfType<SetNode>()
            .Single(node => ChapterBoardSupply.IsConditionSupplyNode(node, file));
        string conditionId = supply.Conditions.Single().Id;

        editor.SetLineTransitions(dialogue.Id, line.Id, [
            LineConditionTransition.BeginIf(conditionId),
            LineConditionTransition.EndIf()
        ]);

        // 기획자가 엑셀에서 라벨을 고쳤다 — 식은 그대로다.
        ChapterBoardSupply.SupplyChapterConditionsToBoard(
            editor, Definition, file.Id, ChapterWith("신뢰가 높다"));

        ConditionDefinition condition = Assert.Single(supply.Conditions);

        Assert.Equal(conditionId, condition.Id);          // 신원은 그대로 — 갈래가 산다
        Assert.Equal("신뢰가 높다", condition.Name);       // 이름은 따라간다
        Assert.Equal(
            conditionId,
            dialogue.FindExtension(line.Id)!.Transitions[0].ConditionId);
    }

    [Fact]
    public void 라벨이_그대로면_조건에_손대지_않는다()
    {
        // 같은 값을 다시 쓰면 되돌리기 기록이 쌓이고 화면이 다시 그려진다.
        var project = new StoryProject();
        var file = new StoryFile("sf_ch01", "ch01", "story/ch01.vnstory.json");
        project.Files.Add(file);
        var editor = new ProjectEditor(project);

        ChapterBoardSupply.SupplyChapterConditionsToBoard(
            editor, Definition, file.Id, ChapterWith("신뢰 높음"));

        long revision = editor.Revision;

        ChapterBoardSupply.SupplyChapterConditionsToBoard(
            editor, Definition, file.Id, ChapterWith("신뢰 높음"));

        Assert.Equal(revision, editor.Revision);
    }

    // ── 화자 ────────────────────────────────────────────────────────────────

    private sealed record World(
        ProjectEditor Editor, string ProjectPath, DialogueNode Node, ScriptDocument Script, ScriptLine Line, string WorkbookPath);

    /// <summary>
    /// 챕터 ch01 · 그 판의 에피소드 카드 하나 · 화자가 적힌 줄 하나 · 그 카드의 대본 워크북(산출물)이
    /// 한 번 나가 있는 프로젝트.
    /// </summary>
    private World BuildProject(string speaker, string episodeId = "main01.01")
    {
        string projectPath = Path.Combine(_root, "예제.vnproject.json");

        var editor = new ProjectEditor(new StoryProject());
        editor.EnsureChapter("ch01");
        string fileId = editor.EnsureChapterBoard("ch01");

        // 챕터 판에 세우면 에피소드도 함께 선다 — 이름이 곧 EpisodeId다.
        DialogueNode node = editor.AddDialogueNode(fileId, name: episodeId);
        ScriptDocument script = editor.Project.FindScript(node.ScriptId)!;
        ScriptLine line = script.ActiveLines.First();
        editor.SetScriptLineText(script.Id, line.Id, speaker, "문을 연다");

        string folder = EpisodeLibrary.FolderFor(projectPath, "ch01")!;
        Directory.CreateDirectory(folder);
        string workbook = EpisodeLibrary.PathFor(folder, episodeId);
        Assert.True(EpisodeScriptOutput.Write(editor.Project, node, workbook).Written);

        return new World(editor, projectPath, node, script, line, workbook);
    }

    private static string FirstSpeaker(string path) => EpisodeWorkbookReader.Read(path).Rows
        .First(row => row.Speaker.Length > 0)
        .Speaker;

    [Fact]
    public void 화자_개명이_프로젝트_줄과_대본_워크북에_함께_간다()
    {
        World world = BuildProject("라루");

        SpeakerRenameOutcome outcome = SpeakerRenamer.Rename(
            world.Editor, world.ProjectPath, "라루", "라루엘");

        Assert.Equal(1, outcome.ScriptLines);
        Assert.Equal(1, outcome.WorkbooksWritten);
        Assert.Empty(outcome.Deferred);

        Assert.Equal("라루엘", world.Script.Text(world.Line.Id, world.Script.PrimaryLocale).Speaker);
        Assert.Equal("라루엘", FirstSpeaker(world.WorkbookPath));   // 산출물이 프로젝트를 따라왔다
    }

    [Fact]
    public void 글자가_다른_이름은_건드리지_않고_파일에도_손대지_않는다()
    {
        // 추측 보정 금지 — 사람이 손으로 적은 다른 이름을 툴이 고쳐 주지 않는다. 그리고 안 바뀐
        // 파일을 다시 쓰지 않는다(내용 해시가 달라져 아무 일도 없었는데 감시가 깨어난다).
        World world = BuildProject("라루");
        byte[] before = File.ReadAllBytes(world.WorkbookPath);

        SpeakerRenameOutcome outcome = SpeakerRenamer.Rename(
            world.Editor, world.ProjectPath, "라루X", "누구");

        Assert.Equal(0, outcome.ScriptLines);
        Assert.Equal(0, outcome.WorkbooksWritten);
        Assert.Equal("라루", world.Script.Text(world.Line.Id, world.Script.PrimaryLocale).Speaker);
        Assert.Equal(before, File.ReadAllBytes(world.WorkbookPath));
    }

    [Fact]
    public void 엑셀이_잡고_있어도_프로젝트는_바뀌고_그_워크북은_미뤄진다()
    {
        // ⛔ 2026-10-29까지는 잠긴 대본이 하나라도 있으면 <b>시작도 안 했다</b> — 원본이 엑셀이던
        //    시절의 규칙이다. 원본이 프로젝트인 지금, 잠긴 것은 그 파일을 지금 못 쓰는 것뿐이다.
        World world = BuildProject("라루");

        SpeakerRenameOutcome outcome;

        using (new FileStream(world.WorkbookPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            outcome = SpeakerRenamer.Rename(world.Editor, world.ProjectPath, "라루", "라루엘");
        }

        Assert.Equal(1, outcome.ScriptLines);
        Assert.Equal(0, outcome.WorkbooksWritten);
        Assert.Contains("엑셀이", Assert.Single(outcome.Deferred));

        Assert.Equal("라루엘", world.Script.Text(world.Line.Id, world.Script.PrimaryLocale).Speaker);
        Assert.Equal("라루", FirstSpeaker(world.WorkbookPath));   // 옛 산출물 — 다음 출력이 따라잡는다
    }

    [Fact]
    public void 챕터_아닌_판의_줄도_바뀌지만_워크북은_없다()
    {
        // 작가의 낙서판(챕터 아닌 판)의 카드는 진행에 안 실리고 대본 워크북도 없다 — 줄만 바뀐다.
        var editor = new ProjectEditor(new StoryProject());
        string fileId = editor.AddStoryFile("낙서").Id;
        DialogueNode node = editor.AddDialogueNode(fileId, name: "메모");
        ScriptDocument script = editor.Project.FindScript(node.ScriptId)!;
        ScriptLine line = script.ActiveLines.First();
        editor.SetScriptLineText(script.Id, line.Id, "라루", "혼잣말");

        SpeakerRenameOutcome outcome = SpeakerRenamer.Rename(
            editor, Path.Combine(_root, "예제.vnproject.json"), "라루", "라루엘");

        Assert.Equal(1, outcome.ScriptLines);
        Assert.Equal(0, outcome.WorkbooksWritten);
        Assert.Empty(outcome.Deferred);
        Assert.Equal("라루엘", script.Text(line.Id, script.PrimaryLocale).Speaker);
    }
}
