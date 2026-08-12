using ClosedXML.Excel;
using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// G5 — 에피소드 워크북 저장 → 대사노드 반영의 한 판. 파이프라인 ③→④→⑤가 실제로 이어지고,
/// 새 LineId가 워크북 B열로 되돌아오며, 거부·삭제가 목록으로 보고된다.
/// </summary>
public sealed class EpisodeSyncServiceTests : IDisposable
{
    private static readonly GameDefinition Definition = GameDefinition.Parse("""
        { "speakers": [ { "name": "라루", "characterId": "laru" }, { "name": "윌로", "characterId": "willo" } ] }
        """)!;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "vn-episode-sync", Guid.NewGuid().ToString("N"));

    public EpisodeSyncServiceTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static string SamplePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "chapter-graph-sample.xlsx"));

    // ── 견본 왕복 ───────────────────────────────────────────────────────────

    [Fact]
    public void 견본_에피소드가_챕터의_대사엔트리_이름으로_노드가_된다()
    {
        (ProjectEditor editor, string fileId, ChapterGraphModel chapter) = BuildWorld();
        string workbook = CopySampleAs("main05.02.xlsx");

        EpisodeSyncReport report = EpisodeSyncService.Sync(
            editor, Definition, fileId, workbook, chapter);

        Assert.True(report.Applied, string.Join(" / ", report.Problems));
        Assert.Empty(report.Problems);

        // 노드 이름의 원천은 챕터 `에피소드` 시트의 `대사엔트리`다 — 런타임이 재생할 엔트리와 같다.
        DialogueNode node = Assert.IsType<DialogueNode>(editor.Project.FindNode(report.DialogueNodeId));
        Assert.Equal("Story_ch05_02", node.Name);

        // 워크북의 LineId(CHOICE 제외)가 전부 대본에 들어왔고 신원이 그대로다.
        Assert.Equal(
            ["ln_0001", "ln_0002", "ln_0100", "ln_0101", "ln_0102",
             "ln_0003", "ln_0004", "ln_0005", "ln_0007", "ln_0110", "ln_0111", "ln_0008"],
            editor.Project.FindScript(node.ScriptId!)!.ActiveLines.Select(line => line.Id));
    }

    [Fact]
    public void 같은_워크북을_두_번_동기화하면_두_번째는_변경이_없다()
    {
        (ProjectEditor editor, string fileId, ChapterGraphModel chapter) = BuildWorld();
        string workbook = CopySampleAs("main05.02.xlsx");

        EpisodeSyncService.Sync(editor, Definition, fileId, workbook, chapter);
        EpisodeSyncReport second = EpisodeSyncService.Sync(editor, Definition, fileId, workbook, chapter);

        Assert.True(second.Applied);
        Assert.Empty(second.WrittenBackLineIds);
        Assert.Empty(second.Pruned);

        // 노드가 또 생기지 않았다 — 이름으로 찾아 재사용한다.
        Assert.Single(editor.Project.EnumerateNodes().OfType<DialogueNode>(),
            node => node.Name == "Story_ch05_02");
    }

    // ── LineId 되쓰기 ───────────────────────────────────────────────────────

    [Fact]
    public void 새_행에_발급된_LineId가_워크북_B열에_되써진다()
    {
        (ProjectEditor editor, string fileId, ChapterGraphModel chapter) = BuildWorld();
        string workbook = WriteMinimalEpisode("ep_new.xlsx", lineIdForSecondRow: null);

        EpisodeSyncReport report = EpisodeSyncService.Sync(
            editor, Definition, fileId, workbook, chapter);

        Assert.True(report.Applied, string.Join(" / ", report.Problems));
        Assert.Null(report.WriteBackFailure);
        string issued = Assert.Single(report.WrittenBackLineIds);

        // 워크북을 다시 읽으면 B열에 새 신원이 있다 — 다음 왕복부터는 ID 매칭이다.
        EpisodeWorkbookModel reread = EpisodeWorkbookReader.Read(
            workbook, ["신뢰높음"], ["trust", "anger", "fatigue"]);

        Assert.Equal(issued, reread.FindByIndex(20)!.LineId);

        // 되쓰기 뒤의 동기화는 변경 없음이다 — 왕복이 닫혔다.
        EpisodeSyncReport after = EpisodeSyncService.Sync(editor, Definition, fileId, workbook, chapter);
        Assert.Empty(after.WrittenBackLineIds);
    }

    [Fact]
    public void 워크북이_잠겨_있으면_되쓰지_않고_사유를_남긴다()
    {
        (ProjectEditor editor, string fileId, ChapterGraphModel chapter) = BuildWorld();
        string workbook = WriteMinimalEpisode("ep_locked.xlsx", lineIdForSecondRow: null);

        using (new FileStream(workbook, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            EpisodeSyncReport report = EpisodeSyncService.Sync(
                editor, Definition, fileId, workbook, chapter);

            // 반영은 됐다 — 읽기는 공유 잠금과 공존한다. 못 한 것은 되쓰기뿐이고, 사유가 남는다.
            Assert.True(report.Applied);
            Assert.NotNull(report.WriteBackFailure);
            Assert.Contains("낡은 상태", report.WriteBackFailure);
            Assert.Empty(report.WrittenBackLineIds);
        }
    }

    // ── 거부·보고 ───────────────────────────────────────────────────────────

    [Fact]
    public void 검증_오류가_있으면_반영을_거부하고_원인을_남긴다()
    {
        (ProjectEditor editor, string fileId, ChapterGraphModel chapter) = BuildWorld();

        // IN이 가리키는 구간이 없다 — §3.3 규칙 1 위반.
        string workbook = Path.Combine(_directory, "broken.xlsx");
        WriteRows(workbook,
        [
            ["인덱스", "LineId", "유형", "태그", "조건라벨", "IN", "OUT", "화자", "내용", "스탯변화", "메모"],
            ["10", "ln_0001", null, null, null, null, null, "라루", "첫 줄", null, null],
            ["30", null, "IF", null, "신뢰높음", "900", null, null, null, null, null]
        ]);

        int nodesBefore = editor.Project.EnumerateNodes().Count();

        EpisodeSyncReport report = EpisodeSyncService.Sync(
            editor, Definition, fileId, workbook, chapter);

        Assert.False(report.Applied);
        Assert.Contains(report.Problems, problem => problem.Contains("검증 오류"));
        Assert.Contains(report.Diagnostics, item =>
            item.Severity == ChapterDiagnosticSeverity.Error &&
            item.Message.Contains("INPUT 태그가 없습니다"));
        Assert.True(report.RejectionCount > 0);

        // 깨진 표는 노드도 만들지 않는다.
        Assert.Equal(nodesBefore, editor.Project.EnumerateNodes().Count());
    }

    [Fact]
    public void 행이_지워지면_함께_접힌_논리가_행별로_보고된다()
    {
        // G3-2 — 조용한 무반영이 아니라, "이 행과 함께 무엇이 접혔는지"를 행별로 말한다.
        (ProjectEditor editor, string fileId, ChapterGraphModel chapter) = BuildWorld();
        string workbook = WriteMinimalEpisode("ep_prune.xlsx", lineIdForSecondRow: "ln_gone");

        EpisodeSyncReport first = EpisodeSyncService.Sync(editor, Definition, fileId, workbook, chapter);
        Assert.True(first.Applied);

        // 지워질 줄에 대사 논리를 단다 — set 하나.
        DialogueNode node = (DialogueNode)editor.Project.FindNode(first.DialogueNodeId)!;
        var extension = new DialogueLineExtension("ln_gone");
        extension.SetOperations.Add(new SetOperation
        {
            Variable = "mood",
            Operator = SetOperatorKind.Add,
            Value = "1"
        });
        node.LineExtensions.Add(extension);

        // 그 행을 워크북에서 지운다.
        WriteRows(workbook,
        [
            ["인덱스", "LineId", "유형", "태그", "조건라벨", "IN", "OUT", "화자", "내용", "스탯변화", "메모"],
            ["10", "ln_0001", null, null, null, null, null, "라루", "첫 줄", null, null]
        ]);

        EpisodeSyncReport second = EpisodeSyncService.Sync(editor, Definition, fileId, workbook, chapter);

        Assert.True(second.Applied);
        EpisodePrunedLogic pruned = Assert.Single(second.Pruned);
        Assert.Equal("ln_gone", pruned.LineId);
        Assert.Equal(1, pruned.SetOperations);
        Assert.Contains("set 1개", pruned.Describe());
    }

    // ── Gate B 1번 — 컴파일되는 텍스트 ──────────────────────────────────────

    [Fact]
    public void 엑셀_출처_노드가_발행되고_실제로_컴파일된다()
    {
        // 파이프라인 전 구간: 워크북 → 평평화 → 파서 → 대사노드 → 발행 → 이미터 → Yarn 컴파일러.
        // 조건 구간이 있는 에피소드다. 선택지로 끝나는 에피소드는 아직 발행할 수 없다 —
        // 툴 모델이 선택 블록을 닫는 후속 줄을 요구하는데 §3.4의 "모든 OUT=END"는 후속 줄이
        // 없다는 뜻이라, 그 간극은 소유자 결정 대상으로 run-log에 기록돼 있다.
        (ProjectEditor editor, string fileId, ChapterGraphModel chapter) = BuildWorld();

        string workbook = Path.Combine(_directory, "main05.02.xlsx");
        WriteRows(workbook,
        [
            ["인덱스", "LineId", "유형", "태그", "조건라벨", "IN", "OUT", "화자", "내용", "스탯변화", "메모"],
            ["10", "ln_0001", null, null, null, null, null, "윌로", "복도는 조용했다.", null, null],
            ["30", null, "IF", null, "신뢰높음", "900", null, null, null, null, null],
            ["40", "ln_0003", null, null, null, null, null, "라루", "왜 그런 표정이야?", null, null],
            ["900", "ln_0100", null, "INPUT", null, null, null, "윌로", "어머니가 같은 말을 했었다.", null, null],
            ["908", "ln_0102", null, "OUT", null, null, "40", "윌로", "아니. 처음 해.", null, null]
        ]);

        EpisodeSyncReport report = EpisodeSyncService.Sync(editor, Definition, fileId, workbook, chapter);
        Assert.True(report.Applied, string.Join(" / ", report.Problems));
        Assert.Empty(report.Problems);

        // 조건 갈래가 실제 조건으로 이어졌다 — 챕터 `조건` 시트가 설정노드로 공급됐다.
        DialogueNode node = (DialogueNode)editor.Project.FindNode(report.DialogueNodeId)!;
        Assert.Equal(
            Vn.Authoring.Model.ConditionTransitionKind.BeginIf,
            node.FindExtension("ln_0100")!.Transition!.Kind);
        Assert.Equal(
            Vn.Authoring.Model.ConditionTransitionKind.EndIf,
            node.FindExtension("ln_0003")!.Transition!.Kind);

        // 발행 → 이미터 → 실컴파일.
        Vn.Authoring.Results.DialogueResult published = editor.PublishDialogue(node.Id).Result;
        Vn.Authoring.Rendering.YarnBundle bundle = Vn.Authoring.Rendering.YarnBundleEmitter.Emit(
            published, project: editor.Project);

        string compileDirectory = Path.Combine(_directory, "compile");
        Directory.CreateDirectory(compileDirectory);
        Vn.Authoring.Rendering.YarnBundleEmitter.WriteBundles([bundle], compileDirectory);

        var utf8 = new System.Text.UTF8Encoding(false);
        File.WriteAllText(Path.Combine(compileDirectory, "Demo.yarnproject"),
            """
            {
              "projectFileVersion": 3,
              "baseLanguage": "ko",
              "sourceFiles": [ "**/*.yarn" ],
              "excludeFiles": []
            }
            """, utf8);
        // Tier 2 스탯은 게임 전역 변수다 — 실제 게임이 game.schema.json에 선언하는 것을 그대로 흉내낸다.
        // 브리지(U9)가 대화 전에 $trust를 심는 것과 같은 선언이다.
        File.WriteAllText(Path.Combine(compileDirectory, "game.schema.json"),
            """
            { "schemaVersion": 1,
              "variables": [
                { "id": "$trust", "type": "number" },
                { "id": "$anger", "type": "number" },
                { "id": "$fatigue", "type": "number" }
              ],
              "commands": [] }
            """, utf8);

        Vn.Core.Analysis.AnalysisReport compiled = new Vn.Core.VnProjectAnalyzer().Analyze(
            Path.Combine(compileDirectory, "Demo.yarnproject"),
            Path.Combine(compileDirectory, "game.schema.json"));

        var errors = compiled.Diagnostics
            .Where(item => item.Severity == Vn.Core.Diagnostics.DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0, "컴파일 오류: " + string.Join(
            Environment.NewLine,
            errors.Select(error => $"{error.Code} {error.FilePath}:{error.Line} {error.Message}")));
    }

    // ── 워크북 생성 (G5의 쓰기 절반) ────────────────────────────────────────

    [Fact]
    public void 없는_에피소드_워크북은_규격대로_생성된다()
    {
        string folder = Path.Combine(_directory, "episodes");

        Assert.True(EpisodeLibrary.EnsureWorkbook(folder, "main05.03"));
        Assert.False(EpisodeLibrary.EnsureWorkbook(folder, "main05.03")); // 두 번째는 그대로 둔다

        string path = EpisodeLibrary.PathFor(folder, "main05.03");

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);
        IXLWorksheet sheet = workbook.Worksheets.First();

        // §3.2의 11열 머리글이 그대로 있고, 리더가 이 워크북을 읽을 수 있다.
        Assert.Equal("인덱스", sheet.Cell(1, 1).GetString());
        Assert.Equal("메모", sheet.Cell(1, 11).GetString());
        Assert.Equal(10, sheet.Cell(2, 1).GetDouble());

        // LineId 열은 잠기고 나머지는 열려 있다 — B열만 툴 소유라는 §3.2 그대로.
        Assert.True(sheet.Protection.IsProtected);
        Assert.True(sheet.Cell(3, 2).Style.Protection.Locked);
        Assert.False(sheet.Cell(3, 9).Style.Protection.Locked);

        // 유형·태그 드롭다운이 걸려 있다.
        Assert.Equal(2, sheet.DataValidations.Count());
    }

    // ── 기반 ────────────────────────────────────────────────────────────────

    private (ProjectEditor Editor, string FileId, ChapterGraphModel Chapter) BuildWorld()
    {
        var project = new StoryProject();
        var file = new StoryFile("sf_sync", "테스트", "story/sync.vnstory.json");
        project.Files.Add(file);

        int next = 0;
        var editor = new ProjectEditor(project, newLineId: () => $"ln_new_{++next:D3}");

        ChapterGraphModel chapter = ChapterWorkbookReader.Read(SamplePath);

        return (editor, file.Id, chapter);
    }

    private string CopySampleAs(string fileName)
    {
        string path = Path.Combine(_directory, fileName);
        File.Copy(SamplePath, path);
        return path;
    }

    /// <summary>두 줄짜리 에피소드. 둘째 행의 LineId를 비워 두면 되쓰기 대상이 된다.</summary>
    private string WriteMinimalEpisode(string fileName, string? lineIdForSecondRow)
    {
        string path = Path.Combine(_directory, fileName);
        WriteRows(path,
        [
            ["인덱스", "LineId", "유형", "태그", "조건라벨", "IN", "OUT", "화자", "내용", "스탯변화", "메모"],
            ["10", "ln_0001", null, null, null, null, null, "라루", "첫 줄", null, null],
            ["20", lineIdForSecondRow, null, null, null, null, null, "윌로", "둘째 줄", null, null]
        ]);

        return path;
    }

    private static void WriteRows(string path, string?[][] rows)
    {
        using var workbook = new XLWorkbook();
        IXLWorksheet sheet = workbook.AddWorksheet("본문");

        for (int row = 0; row < rows.Length; row++)
        {
            for (int column = 0; column < rows[row].Length; column++)
            {
                if (rows[row][column] is { Length: > 0 } value)
                {
                    sheet.Cell(row + 1, column + 1).SetValue(value);
                }
            }
        }

        workbook.SaveAs(path);
    }
}
