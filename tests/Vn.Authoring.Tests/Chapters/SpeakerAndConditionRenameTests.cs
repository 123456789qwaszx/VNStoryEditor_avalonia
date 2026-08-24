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
///   <item><b>화자</b> — 붙드는 것이 이름 문자열 하나뿐이다(대본 엑셀 E열 · <c>LocalizedLine</c>).
///     등록부에서만 갈면 그 줄들이 전부 미등록이 된다.</item>
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

    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

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

        EpisodeSyncService.SupplyChapterConditionsToBoard(
            editor, Definition, file.Id, ChapterWith("신뢰 높음"));

        SetNode supply = file.Nodes.OfType<SetNode>()
            .Single(node => EpisodeSyncService.IsConditionSupplyNode(node, file));
        string conditionId = supply.Conditions.Single().Id;

        editor.SetLineTransitions(dialogue.Id, line.Id, [
            LineConditionTransition.BeginIf(conditionId),
            LineConditionTransition.EndIf()
        ]);

        // 기획자가 엑셀에서 라벨을 고쳤다 — 식은 그대로다.
        EpisodeSyncService.SupplyChapterConditionsToBoard(
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

        EpisodeSyncService.SupplyChapterConditionsToBoard(
            editor, Definition, file.Id, ChapterWith("신뢰 높음"));

        long revision = editor.Revision;

        EpisodeSyncService.SupplyChapterConditionsToBoard(
            editor, Definition, file.Id, ChapterWith("신뢰 높음"));

        Assert.Equal(revision, editor.Revision);
    }

    // ── 화자 ────────────────────────────────────────────────────────────────

    private string CopySample(string name)
    {
        string folder = Path.Combine(_root, "episodes", "ch01");
        Directory.CreateDirectory(folder);

        string path = Path.Combine(folder, name + ".xlsx");
        File.Copy(SamplePath, path);

        return path;
    }

    private static string FirstSpeaker(string path) => EpisodeWorkbookReader.Read(path).Rows
        .First(row => row.Kind == EpisodeRowKind.Dialogue && row.Speaker.Length > 0)
        .Speaker;

    [Fact]
    public void 화자_개명이_대본_워크북의_화자_칸까지_간다()
    {
        string path = CopySample("main01.01");
        string speaker = FirstSpeaker(path);

        (ChapterWriteResult result, int changed) =
            EpisodeWorkbookWriter.RenameSpeaker(path, speaker, speaker + "엘");

        Assert.True(result.Written, result.Failure);
        Assert.True(changed > 0);

        IReadOnlyList<EpisodeRow> rows = EpisodeWorkbookReader.Read(path).Rows;

        Assert.DoesNotContain(rows, row => string.Equals(row.Speaker, speaker, StringComparison.Ordinal));
        Assert.Contains(rows, row => string.Equals(row.Speaker, speaker + "엘", StringComparison.Ordinal));
    }

    [Fact]
    public void 글자가_다른_이름은_건드리지_않는다()
    {
        // 추측 보정 금지 — 사람이 손으로 적은 다른 이름을 툴이 고쳐 주지 않는다.
        string path = CopySample("main01.01");
        string speaker = FirstSpeaker(path);

        (_, int changed) = EpisodeWorkbookWriter.RenameSpeaker(path, speaker + "X", "누구");

        Assert.Equal(0, changed);
        Assert.Equal(speaker, FirstSpeaker(path));
    }

    [Fact]
    public void 바꿀_것이_없으면_파일에_손대지_않는다()
    {
        // 안 바뀐 파일을 다시 쓰면 감시가 깨어나고 내용 해시가 달라져, 아무 일도 없었는데
        // 그 챕터의 대본을 전부 다시 읽는다 (성능 규칙 ⑥).
        string path = CopySample("main01.01");
        byte[] before = File.ReadAllBytes(path);

        (ChapterWriteResult result, int changed) =
            EpisodeWorkbookWriter.RenameSpeaker(path, "없는화자", "누구");

        Assert.True(result.Written);
        Assert.Equal(0, changed);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void 엑셀이_잡고_있으면_쓰지_않고_사유를_말한다()
    {
        string path = CopySample("main01.01");
        string speaker = FirstSpeaker(path);

        ChapterWriteResult result;
        int changed;

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            (result, changed) = EpisodeWorkbookWriter.RenameSpeaker(path, speaker, "못 들어간다");
        }

        Assert.False(result.Written);
        Assert.Equal(0, changed);
        Assert.Contains("엑셀이", result.Failure);
        Assert.Equal(speaker, FirstSpeaker(path));
    }

    // ── 한 판으로 돌리기 (SpeakerRenamer) ───────────────────────────────────

    private sealed record World(ProjectEditor Editor, string ProjectPath, IReadOnlyList<ChapterEntry> Chapters);

    /// <summary>챕터 하나 · 그 안의 대본 워크북들 — 챕터 시트가 그 에피소드들을 든다.</summary>
    private World BuildProject(params string[] episodeIds)
    {
        string projectPath = Path.Combine(_root, "예제.vnproject.json");

        var project = new StoryProject();
        project.Files.Add(new StoryFile("sf_ch01", "ch01", "story/ch01.vnstory.json"));
        var editor = new ProjectEditor(project);

        foreach (string episodeId in episodeIds)
        {
            CopySample(episodeId);
        }

        var chapter = new ChapterGraphModel(
            chapterId: "ch01",
            sourcePath: Path.Combine(_root, "chapters", "ch01.xlsx"),
            episodes: episodeIds
                .Select(id => new ChapterEpisode(
                    EpisodeId: id, Title: id, Index: "1", DialogueEntry: id,
                    X: 0, Y: 0, EndingKey: null, Memo: null, SourceRow: 2))
                .ToList(),
            edges: [],
            conditions: [],
            stats: [],
            fixtures: [],
            diagnostics: []);

        return new World(
            editor,
            projectPath,
            [new ChapterEntry("ch01", chapter.SourcePath, chapter, null)]);
    }

    [Fact]
    public void 한_판이_모든_대본과_프로젝트_줄을_함께_끌고_간다()
    {
        World world = BuildProject("main01.01", "main01.02");
        string speaker = FirstSpeaker(Path.Combine(_root, "episodes", "ch01", "main01.01.xlsx"));

        ScriptDocument script = world.Editor.AddScript("본문");
        ScriptLine line = world.Editor.InsertScriptLine(script.Id);
        world.Editor.SetScriptLineText(script.Id, line.Id, speaker, "문을 연다");

        SpeakerRenameOutcome outcome = SpeakerRenamer.Rename(
            world.Editor, world.ProjectPath, world.Chapters, speaker, speaker + "엘");

        Assert.True(outcome.Applied);
        Assert.Empty(outcome.Blocked);
        Assert.Equal(2, outcome.WorkbookFiles);
        Assert.True(outcome.WorkbookCells > 0);
        Assert.Equal(1, outcome.ScriptLines);

        Assert.Equal(speaker + "엘", FirstSpeaker(Path.Combine(_root, "episodes", "ch01", "main01.02.xlsx")));
        Assert.Equal(speaker + "엘", script.Text(line.Id, script.PrimaryLocale).Speaker);
    }

    [Fact]
    public void 못_읽은_챕터가_있으면_시작도_안_한다()
    {
        // 그 챕터의 에피소드 목록을 모르므로 대본이 몇 개 남았는지조차 말할 수 없다.
        // 조용히 건너뛰면 다음 동기화가 "미등록 화자"를 뿜을 때까지 아무도 모른다.
        World world = BuildProject("main01.01");
        string speaker = FirstSpeaker(Path.Combine(_root, "episodes", "ch01", "main01.01.xlsx"));

        var chapters = world.Chapters
            .Concat([new ChapterEntry("ch09", "chapters/ch09.xlsx", null, "엑셀이 잡고 있습니다.")])
            .ToList();

        SpeakerRenameOutcome outcome = SpeakerRenamer.Rename(
            world.Editor, world.ProjectPath, chapters, speaker, speaker + "엘");

        Assert.False(outcome.Applied);
        Assert.Contains("ch09", outcome.Blocked[0]);

        Assert.Equal(speaker, FirstSpeaker(Path.Combine(_root, "episodes", "ch01", "main01.01.xlsx")));
    }

    [Fact]
    public void 대본_하나라도_잠겨_있으면_시작도_안_한다()
    {
        // 2026-08-15 에피소드 개명이 배운 규칙 그대로 — 반쯤 개명된 프로젝트에는 되돌릴
        // 손잡이가 없다. 등록부는 부르는 쪽이 이 결과를 보고 갈지 말지 정한다.
        World world = BuildProject("main01.01", "main01.02");
        string speaker = FirstSpeaker(Path.Combine(_root, "episodes", "ch01", "main01.01.xlsx"));

        ScriptDocument script = world.Editor.AddScript("본문");
        ScriptLine line = world.Editor.InsertScriptLine(script.Id);
        world.Editor.SetScriptLineText(script.Id, line.Id, speaker, "문을 연다");

        SpeakerRenameOutcome outcome;

        using (new FileStream(
                   Path.Combine(_root, "episodes", "ch01", "main01.02.xlsx"),
                   FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            outcome = SpeakerRenamer.Rename(
                world.Editor, world.ProjectPath, world.Chapters, speaker, speaker + "엘");
        }

        Assert.False(outcome.Applied);
        Assert.NotEmpty(outcome.Blocked);
        Assert.Contains("엑셀이", outcome.Blocked[0]);

        // 그리고 <b>한 칸도</b> 안 바뀌었다 — 잠기지 않은 워크북도, 프로젝트 줄도.
        Assert.Equal(speaker, FirstSpeaker(Path.Combine(_root, "episodes", "ch01", "main01.01.xlsx")));
        Assert.Equal(speaker, script.Text(line.Id, script.PrimaryLocale).Speaker);
    }
}
