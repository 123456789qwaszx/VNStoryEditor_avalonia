using ClosedXML.Excel;
using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// G5 — 에피소드 워크북 저장 → 대사노드 반영의 한 판. 파이프라인 ③→④→⑤가 실제로 이어지고,
/// 새 LineId가 프로젝트 신원 맵(<c>ExcelLineMap</c>)에 기록되며(v4 — 워크북은 불변),
/// 거부·삭제가 목록으로 보고된다.
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

        // 엑셀노드 표식 — 편집기가 이걸 보고 본문을 읽기 전용으로 잠근다.
        Assert.Equal("main05.02", node.ExcelEpisodeId);

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
        Assert.Empty(second.IssuedLineIds);
        Assert.Empty(second.Pruned);

        // 노드가 또 생기지 않았다 — 이름으로 찾아 재사용한다.
        Assert.Single(editor.Project.EnumerateNodes().OfType<DialogueNode>(),
            node => node.Name == "Story_ch05_02");
    }

    // ── 행 신원 (v4 — 워크북은 불변, 신원은 프로젝트가 갖는다) ──────────────

    [Fact]
    public void 새_행의_LineId가_프로젝트_신원_맵에_기록되고_워크북은_바이트_그대로다()
    {
        // 이 테스트가 v4의 수용 기준이다: 어떤 동기화 경로도 대본 파일을 건드리지 않는다.
        // 구글 드라이브 .xlsm 사건의 재발 방지 — writer가 하나면 형식이 뒤집힐 수 없다.
        (ProjectEditor editor, string fileId, ChapterGraphModel chapter) = BuildWorld();
        string workbook = WriteMinimalEpisode("ep_new.xlsx", lineIdForSecondRow: null);
        byte[] before = File.ReadAllBytes(workbook);

        EpisodeSyncReport report = EpisodeSyncService.Sync(
            editor, Definition, fileId, workbook, chapter);

        Assert.True(report.Applied, string.Join(" / ", report.Problems));
        string issued = Assert.Single(report.IssuedLineIds);

        // 워크북은 단 한 바이트도 바뀌지 않았다.
        Assert.Equal(before, File.ReadAllBytes(workbook));

        // 신원은 프로젝트가 갖는다 — 인덱스 20의 새 줄이 발급 ID로 매였다.
        DialogueNode node = (DialogueNode)editor.Project.FindNode(report.DialogueNodeId)!;
        Assert.Equal(issued, node.ExcelLineMap[20]);
        Assert.Equal("ln_0001", node.ExcelLineMap[10]); // B열 값은 이행 seed로 흡수됐다

        // 다음 동기화는 매핑으로 ID 매칭 — 변경 없음, 왕복이 닫혔다.
        EpisodeSyncReport after = EpisodeSyncService.Sync(editor, Definition, fileId, workbook, chapter);
        Assert.Empty(after.IssuedLineIds);
        Assert.Empty(after.Pruned);
    }

    [Fact]
    public void 대사를_고쳐도_인덱스가_같으면_신원이_유지된다()
    {
        // 신원의 키가 인덱스인 이유 — 작가가 가장 많이 하는 행동(대사 수정)에서
        // 연출 바인딩이 끊기면 안 된다.
        (ProjectEditor editor, string fileId, ChapterGraphModel chapter) = BuildWorld();
        string workbook = WriteMinimalEpisode("ep_edit.xlsx", lineIdForSecondRow: null);

        EpisodeSyncReport first = EpisodeSyncService.Sync(editor, Definition, fileId, workbook, chapter);
        string issued = Assert.Single(first.IssuedLineIds);

        // 시트에서 하듯 둘째 줄의 대사만 고친다 (B열은 아무도 안 쓴다).
        WriteRows(workbook,
        [
            ["인덱스", "LineId", "유형", "태그", "조건라벨", "IN", "OUT", "화자", "내용", "스탯변화", "메모"],
            ["10", "ln_0001", null, null, null, null, null, "라루", "첫 줄", null, null],
            ["20", null, null, null, null, null, null, "윌로", "고친 둘째 줄", null, null]
        ]);

        EpisodeSyncReport second = EpisodeSyncService.Sync(editor, Definition, fileId, workbook, chapter);

        Assert.True(second.Applied, string.Join(" / ", second.Problems));
        Assert.Empty(second.IssuedLineIds); // 새 ID가 발급되지 않았다 — 같은 줄의 수정이다

        DialogueNode node = (DialogueNode)editor.Project.FindNode(second.DialogueNodeId)!;
        Assert.Equal(issued, node.ExcelLineMap[20]); // 신원 유지
    }

    [Fact]
    public void 공백_있는_미등록_화자는_합쳐지기_전에_경고한다()
    {
        // 실사례 — 화자 칸에 문장을 적자 대사와 합쳐져 지문이 됐는데, 아무도 왜인지
        // 말해 주지 않았다. 조용한 병합이 가장 나쁘다.
        (ProjectEditor editor, string fileId, ChapterGraphModel chapter) = BuildWorld();

        string workbook = Path.Combine(_directory, "ep_space.xlsx");
        WriteRows(workbook,
        [
            ["인덱스", "LineId", "유형", "태그", "조건라벨", "IN", "OUT", "화자", "내용", "스탯변화", "메모"],
            ["10", null, null, null, null, null, null, "3시 13에 고쳤는데", "3시 10분으로 되있네", null, null]
        ]);

        EpisodeSyncReport report = EpisodeSyncService.Sync(editor, Definition, fileId, workbook, chapter);

        Assert.Contains(report.Diagnostics, item =>
            item.Severity == ChapterDiagnosticSeverity.Warning &&
            item.Message.Contains("합쳐져 지문이 됩니다"));
    }

    [Fact]
    public void 워크북이_읽기_공유_잠금이어도_동기화가_끝까지_된다()
    {
        // v4에서는 쓸 것이 없으므로 "되쓰기 실패"라는 상태 자체가 없다.
        (ProjectEditor editor, string fileId, ChapterGraphModel chapter) = BuildWorld();
        string workbook = WriteMinimalEpisode("ep_locked.xlsx", lineIdForSecondRow: null);

        using (new FileStream(workbook, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            EpisodeSyncReport report = EpisodeSyncService.Sync(
                editor, Definition, fileId, workbook, chapter);

            Assert.True(report.Applied);
            Assert.Single(report.IssuedLineIds);
            Assert.Equal(0, report.RejectionCount);
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

    [Fact]
    public void 선택지로_끝나는_에피소드가_발행되고_옵션_출구가_점프로_나간다()
    {
        // 2단계 포트 규칙 (2026-08-14 소유자 승인) — 엑셀이 선택지를 선언하고, 각 옵션의
        // 도착은 작가가 보드에서 잇는다. 안 이은 옵션 = 에피소드 종료. 이 규칙으로
        // Gate B의 반칸("선택지로 끝나는 견본은 발행 거부")이 닫힌다.
        (ProjectEditor editor, string fileId, ChapterGraphModel chapter) = BuildWorld();
        string workbook = CopySampleAs("main05.02.xlsx");

        EpisodeSyncReport report = EpisodeSyncService.Sync(editor, Definition, fileId, workbook, chapter);
        Assert.True(report.Applied, string.Join(" / ", report.Problems));

        DialogueNode node = (DialogueNode)editor.Project.FindNode(report.DialogueNodeId)!;

        // 견본은 선택지로 끝난다 — 옵션 줄이 실제로 있다.
        List<DialogueLineExtension> options = node.LineExtensions
            .Where(extension => extension.Transition?.OpensOption == true)
            .ToList();
        Assert.NotEmpty(options);

        // 첫 옵션만 작가의 곁가지로 잇는다. 나머지는 안 잇는다(= 그 자리에서 에피소드 종료).
        DialogueNode side = editor.AddDialogueNode(fileId, name: "곁가지_창고");
        editor.SetScriptLineText(
            editor.EnsureDialogueScript(side.Id).Id,
            editor.Project.FindScript(side.ScriptId)!.ActiveLines.First().Id,
            "라루", "여긴 창고야.");
        editor.SetExitTarget(node.Id, Vn.Authoring.Flow.ExitPortKind.Branch, options[0].LineId, side.Id);

        // 발행이 더는 거부되지 않는다 — 열린 채 끝난 블록은 알림일 뿐이다.
        Assert.DoesNotContain(
            editor.InspectDialoguePublish(node.Id, Definition).Problems,
            problem => problem.IsBlocking);

        Vn.Authoring.Results.DialogueResult published =
            editor.PublishDialogue(node.Id, Definition).Result;
        Vn.Authoring.Results.DialogueResult sidePublished =
            editor.PublishDialogue(side.Id, Definition).Result;

        // 이미터 → 실컴파일. 곁가지도 함께 내보내야 점프 대상이 실재한다.
        Vn.Authoring.Rendering.YarnBundle bundle =
            Vn.Authoring.Rendering.YarnBundleEmitter.Emit(published, project: editor.Project);
        Vn.Authoring.Rendering.YarnBundle sideBundle =
            Vn.Authoring.Rendering.YarnBundleEmitter.Emit(sidePublished, project: editor.Project);

        string compileDirectory = Path.Combine(_directory, "compile-trailing");
        Directory.CreateDirectory(compileDirectory);
        Vn.Authoring.Rendering.YarnBundleEmitter.WriteBundles([bundle, sideBundle], compileDirectory);

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

        // 이어진 옵션은 곁가지로 점프한다 — 산출 Yarn에 실재해야 한다.
        string yarn = string.Join("\n", Directory.EnumerateFiles(compileDirectory, "*.yarn")
            .Select(File.ReadAllText));
        Assert.Contains("곁가지_창고", yarn);
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

        // §3.2의 9열 머리글 (2026-08-14 개정 — 스탯변화·메모 폐지)이 그대로 있고,
        // 리더가 이 워크북을 읽을 수 있다.
        Assert.Equal("인덱스", sheet.Cell(1, 1).GetString());
        Assert.Equal("내용", sheet.Cell(1, 9).GetString());
        Assert.Equal(string.Empty, sheet.Cell(1, 10).GetString());
        Assert.Equal(10, sheet.Cell(2, 1).GetDouble());

        // 시트 보호는 없다 (v4) — 툴이 이 파일을 쓰지 않으므로 지킬 셀이 없고,
        // 외부 편집기(구글 시트)가 재저장할 때 깨질 것도 하나 줄었다.
        Assert.False(sheet.Protection.IsProtected);

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
