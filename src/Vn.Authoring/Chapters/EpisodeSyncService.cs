using ClosedXML.Excel;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Chapters;

/// <summary>행 하나가 지워지며 함께 접힌 대사 논리 (G3-2). 합계가 아니라 행별이다.</summary>
/// <param name="LineId">지워진 줄의 신원.</param>
public sealed record EpisodePrunedLogic(
    string LineId,
    int Transitions,
    int SetOperations,
    int BranchExits)
{
    public string Describe() =>
        $"{LineId} 행과 함께 접힘 — 조건·선택 전환 {Transitions}개 · set {SetOperations}개 · " +
        $"갈래 출구 {BranchExits}개";
}

/// <summary>
/// 에피소드 워크북 하나의 동기화 결과. 배지·보고 패널이 이걸 그대로 그린다 —
/// 적용했으면 무엇이 어떻게 이어졌는지, 안 했으면 왜인지가 전부 담긴다.
/// </summary>
/// <param name="IssuedLineIds">
/// 새 줄에 발급되어 프로젝트 신원 맵(<c>ExcelLineMap</c>)에 기록된 LineId들 (v4 —
/// 워크북에는 아무것도 쓰지 않는다. 대본 파일의 writer는 사람뿐이다).
/// </param>
/// <param name="NotYetWritten">
/// 대사를 아직 한 줄도 쓰지 않은 워크북인가. <b>거부가 아니다</b> — 툴이 방금 만들어 준 빈
/// 표에 대고 "반영 거부"라고 말하면, 아무것도 잘못하지 않은 사람이 고칠 것을 찾게 된다.
/// </param>
public sealed record EpisodeSyncReport(
    string EpisodeId,
    string WorkbookPath,
    string? DialogueNodeId,
    bool Applied,
    IReadOnlyList<ChapterDiagnostic> Diagnostics,
    IReadOnlyList<string> Problems,
    IReadOnlyList<EpisodePrunedLogic> Pruned,
    IReadOnlyList<string> IssuedLineIds,
    bool NotYetWritten = false)
{
    public bool HasErrors =>
        !NotYetWritten &&
        (!Applied ||
         Problems.Count > 0 ||
         Diagnostics.Any(item => item.Severity == ChapterDiagnosticSeverity.Error));

    /// <summary>배지에 올릴 거부·경고 건수. 아직 안 쓴 워크북은 세지 않는다.</summary>
    public int RejectionCount =>
        NotYetWritten
            ? 0
            : Problems.Count +
              Diagnostics.Count(item => item.Severity == ChapterDiagnosticSeverity.Error);
}

/// <summary>
/// 에피소드 워크북 저장 → 대사노드 반영의 한 판 (G5). 파이프라인 ③→④→⑤를 한 번에 지난다.
///
/// <b>새 경로가 아니다.</b> 평평화(G2)가 만든 텍스트를 X12의
/// <c>ScenarioTextParser → ScriptSynchronizer.Plan → ApplyScenarioText</c>에 먹일 뿐이다.
/// 이 클래스가 하는 일은 그 앞뒤 — 워크북을 읽고, 결과를 보고하고, 새 LineId를 B열에 되쓰는 것.
/// </summary>
public static class EpisodeSyncService
{
    public static EpisodeSyncReport Sync(
        ProjectEditor editor,
        GameDefinition definition,
        string fileId,
        string workbookPath,
        ChapterGraphModel? chapter)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);

        string episodeId = Path.GetFileNameWithoutExtension(workbookPath);

        // 챕터 `화자` 시트의 등록을 정의의 speakers에 합쳐서 본다 (2026-08-16) — 화자 판정
        // (공백 있는 이름을 화자로 볼지)과 미등록 경고가 같은 등록부 하나를 봐야 한다.
        // 여기서만 합치고 파일(game.definition.json)에는 쓰지 않는다 — 초상화 매핑의
        // 정본은 그대로 정의 파일이다.
        definition = AugmentWithChapterSpeakers(definition, chapter);

        // 챕터가 없으면 라벨 검사는 빈 목록으로 돈다 — 조건라벨이 전부 미정의 오류가 되므로
        // 조용히 통과하는 일은 없다.
        string[] labels = chapter?.Conditions.Select(condition => condition.Label).ToArray()
            ?? Array.Empty<string>();

        EpisodeWorkbookModel model;

        try
        {
            model = EpisodeWorkbookReader.Read(workbookPath, labels);
        }
        catch (XlsxReadException exception)
        {
            return Refused(episodeId, workbookPath, Array.Empty<ChapterDiagnostic>(),
                [$"워크북을 읽지 못했습니다: {exception.Message}"]);
        }

        var conditions = (chapter?.Conditions ?? Array.Empty<ChapterCondition>() as IReadOnlyList<ChapterCondition>)
            .ToDictionary(condition => condition.Label, condition => condition, StringComparer.Ordinal);

        // 행 신원의 원천은 프로젝트의 ExcelLineMap이다(v4). 노드가 아직 없으면(첫 동기화)
        // 매핑도 없다 — B열 값이 이행 seed로 쓰인다. 여기서 노드를 만들지는 않는다:
        // 깨진·빈 워크북이 노드를 남기면 안 되기 때문이다.
        IReadOnlyDictionary<int, string>? identity =
            FindNode(editor, NodeNameFor(episodeId, chapter))?.ExcelLineMap;

        EpisodeFlattenResult flattened = EpisodeFlattener.Flatten(model, conditions, identity);

        var diagnostics = model.Diagnostics.Concat(flattened.Diagnostics).ToList();

        // 공백 있는 미등록 화자는 파서가 산문으로 보아 대사와 합쳐진다("화자와 내용이
        // 합쳐진다" — 실사례). 등록된 이름이면 공백이 있어도 통과하므로, 여기 걸리는 것은
        // 오타이거나 아직 등록 안 된 이름이다 — 조용히 합쳐지기 전에 말해 준다.
        foreach (EpisodeRow row in model.Rows)
        {
            if (row.Speaker.Any(char.IsWhiteSpace) &&
                definition.FindSpeakerCharacterId(row.Speaker) is null)
            {
                diagnostics.Add(new ChapterDiagnostic(
                    ChapterDiagnosticSeverity.Warning,
                    ChapterDiagnosticCode.ColumnHeaderUnexpected,
                    workbookPath, model.SheetName, row.SourceRow, "H",
                    $"화자 '{row.Speaker}'에 공백이 있는데 등록된 화자가 아니라서, 대사와 " +
                    "합쳐져 지문이 됩니다. 화자 칸에는 이름만 적거나 챕터의 `화자` 시트 또는 " +
                    "game.definition.json의 speakers에 그 이름을 등록해 주세요."));
            }
        }

        // 아직 대사를 한 줄도 쓰지 않았다면 반영할 것이 없다 — 거부가 아니라 "아직"이다.
        // 툴이 방금 만들어 준 빈 워크북이 스스로 거부당하는 일은 없어야 한다.
        if (!model.HasErrors && !flattened.HasErrors && flattened.Text.Trim().Length == 0)
        {
            return new EpisodeSyncReport(
                episodeId, workbookPath, DialogueNodeId: null,
                Applied: false,
                diagnostics,
                Array.Empty<string>(),
                Array.Empty<EpisodePrunedLogic>(),
                Array.Empty<string>(),
                NotYetWritten: true);
        }

        if (model.HasErrors || flattened.HasErrors)
        {
            // 구조가 깨진 표를 대사노드에 밀어 넣지 않는다. 무엇이 왜 거부됐는지는 진단에 전부 있다.
            return Refused(episodeId, workbookPath, diagnostics,
                ["검증 오류가 있어 반영하지 않았습니다. 아래 목록을 고치고 다시 저장해 주세요."]);
        }

        DialogueNode node = FindOrCreateNode(editor, fileId, episodeId, chapter);

        // 엑셀노드 표식 — 이 노드의 본문은 엑셀 소유다. 편집기가 이 값을 보고 읽기 전용으로
        // 잠근다(툴에서 고친 본문은 다음 동기화가 되돌리므로, 고쳐지는 척하는 화면이 더 나쁘다).
        node.ExcelEpisodeId = episodeId;

        EnsureConditionSupply(editor, definition, fileId, node, model, chapter);

        // G3-2 — 지워질 줄이 소유하던 논리를 세려면 지우기 전의 모습이 필요하다.
        Dictionary<string, DialogueLineExtension> extensionsBefore = node.LineExtensions
            .ToDictionary(extension => extension.LineId, extension => extension, StringComparer.Ordinal);
        HashSet<string> exitsBefore = node.BranchExits.Keys.ToHashSet(StringComparer.Ordinal);

        // 엑셀 경로에서 행 삭제는 기획자의 명시적 행동이다 — 확인 대화가 아니라 보고가 맞다.
        ScenarioPasteOutcome outcome = editor.ApplyScenarioText(
            node.Id, flattened.Text, definition, confirmDeletes: true);

        if (!outcome.Applied)
        {
            return new EpisodeSyncReport(
                episodeId, workbookPath, node.Id,
                Applied: false,
                diagnostics,
                outcome.Problems.Concat([outcome.Summary()]).Distinct().ToList(),
                Array.Empty<EpisodePrunedLogic>(),
                Array.Empty<string>());
        }

        IReadOnlyList<EpisodePrunedLogic> pruned =
            CollectPruned(outcome.Plan!, extensionsBefore, exitsBefore);

        IReadOnlyList<string> issued = RecordLineIdentity(node, model, flattened, outcome.Plan!);

        return new EpisodeSyncReport(
            episodeId, workbookPath, node.Id,
            Applied: true,
            diagnostics,
            outcome.Problems,
            pruned,
            issued);
    }

    /// <summary>
    /// 행 신원 기록 (v4) — <b>워크북에는 아무것도 쓰지 않는다.</b> 유효 ID 전부(기존 유지 +
    /// 방금 발급 + B열 seed)를 인덱스에 매어 노드의 <see cref="DialogueNode.ExcelLineMap"/>에
    /// 다시 적는다. 매핑은 대사 줄 상태와 같은 저장 단위(프로젝트)로 함께 커밋·롤백된다.
    /// </summary>
    private static IReadOnlyList<string> RecordLineIdentity(
        DialogueNode node,
        EpisodeWorkbookModel model,
        EpisodeFlattenResult flattened,
        ScriptSyncPlan plan)
    {
        // 새로 발급된 ID — 산출 위치로 맞물린다(Inserted의 NewIndex = 산출 줄 번호).
        Dictionary<int, string> assigned = plan.Entries
            .Where(entry => entry.Kind == ScriptSyncKind.Inserted &&
                            entry.NewIndex is not null &&
                            entry.LineId is not null)
            .ToDictionary(entry => entry.NewIndex!.Value, entry => entry.LineId!);

        var issued = new List<string>();
        var next = new Dictionary<int, string>();

        for (int position = 0; position < flattened.Lines.Count; position++)
        {
            EpisodeFlattenedLine line = flattened.Lines[position];

            if (line.LineId is { } keptId)
            {
                next[line.Index] = keptId;
            }
            else if (assigned.TryGetValue(position, out string? newId))
            {
                next[line.Index] = newId;
                issued.Add(newId);
            }
        }

        // 산출에 실리지 않는 행(CHOICE 등)의 신원은 유지한다 — 행이 아직 존재한다면.
        HashSet<int> liveIndexes = model.Rows.Select(row => row.Index).ToHashSet();

        foreach ((int index, string lineId) in node.ExcelLineMap)
        {
            if (!next.ContainsKey(index) && liveIndexes.Contains(index))
            {
                next[index] = lineId;
            }
        }

        if (!next.OrderBy(pair => pair.Key).SequenceEqual(node.ExcelLineMap.OrderBy(pair => pair.Key)))
        {
            node.ExcelLineMap.Clear();

            foreach ((int index, string lineId) in next)
            {
                node.ExcelLineMap[index] = lineId;
            }
        }

        return issued;
    }

    /// <summary>이 에피소드가 반영될 노드의 이름 — 챕터의 `대사엔트리`, 없으면 EpisodeId.</summary>
    private static string NodeNameFor(string episodeId, ChapterGraphModel? chapter) =>
        chapter?.FindEpisode(episodeId)?.DialogueEntry is { Length: > 0 } entry
            ? entry
            : episodeId;

    private static DialogueNode? FindNode(ProjectEditor editor, string name) =>
        editor.Project.EnumerateNodes()
            .OfType<DialogueNode>()
            .FirstOrDefault(node => string.Equals(node.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// 반영 대상 대사노드. 이름의 원천은 챕터 `에피소드` 시트의 `대사엔트리`다 — 런타임이
    /// 재생할 엔트리와 툴의 노드가 같은 이름을 쓴다. 챕터에 없는 에피소드면 EpisodeId를 쓴다.
    /// </summary>
    private static DialogueNode FindOrCreateNode(
        ProjectEditor editor, string fileId, string episodeId, ChapterGraphModel? chapter)
    {
        string name = NodeNameFor(episodeId, chapter);

        if (FindNode(editor, name) is { } existing)
        {
            return existing;
        }

        DialogueNode created = editor.AddDialogueNode(fileId, name: name);

        // 노드 생성이 자동으로 채우는 첫 빈 줄을 은퇴시킨다. 워크북의 줄들은 전부 ID를 실어
        // 오는데 이 줄만 신원 없는 고아로 남아, 동기화가 "이 빈 줄이 지워진 것인지 어느 줄로
        // 고쳐진 것인지"를 확신하지 못하고 통째 거부한다. 손으로 타이핑을 시작하는 노드에는
        // 필요한 줄이지만, 엑셀이 채우는 노드에는 처음부터 설 자리가 없다.
        foreach (ScriptLine line in editor.Project.FindScript(created.ScriptId)!
                     .ActiveLines.ToList())
        {
            editor.RetireScriptLine(created.ScriptId!, line.Id);
        }

        return created;
    }

    /// <summary>
    /// 챕터 조건 공급 설정노드의 이름 규약 — 이 이름이 곧 신원이다. 동기화(생성·dedupe)와
    /// 그래프 프로젝션(작가 화면에서 숨김)이 같은 규칙 하나를 쓴다.
    /// </summary>
    public static string ConditionSupplyNodeName(string chapterId) => $"챕터 {chapterId} 조건";

    /// <summary>
    /// 챕터 `조건` 시트의 라벨↔식을 설정노드로 만들어 대사노드에 잇는다 (G3 —
    /// "조건 행 → <c>ConditionDefinition{Name=라벨, Expression=식}</c> 생성·dedupe(식 기준)").
    ///
    /// <c>ApplyScenarioText</c>는 <c>&lt;&lt;if 식&gt;&gt;</c>을 노드가 쓸 수 있는 조건과 <b>정확 일치</b>로만
    /// 역조회한다(보정 금지). 챕터가 원천인 조건이 노드 곁에 없으면 그 조회가 전부 빗나가므로,
    /// 반영 전에 여기서 공급한다. 이미 같은 식이 보이면 만들지 않는다 — 식 기준 dedupe.
    /// </summary>
    private static void EnsureConditionSupply(
        ProjectEditor editor,
        GameDefinition definition,
        string fileId,
        DialogueNode node,
        EpisodeWorkbookModel model,
        ChapterGraphModel? chapter)
    {
        if (chapter is null)
        {
            return;
        }

        // 노드에 다는 식은 Yarn 형태다 — <<if>>가 정확 일치로 역조회하는 대상이자, 게임이
        // 실제로 평가할 식이다. 번역 불가(cleared: 등)는 평평화가 이미 오류로 알렸다.
        List<(string Label, string Yarn)> used = model.Rows
            .Where(row => row.ConditionLabel is not null)
            .Select(row => chapter.FindCondition(row.ConditionLabel!))
            .Where(condition => condition is not null)
            .DistinctBy(condition => condition!.Label, StringComparer.Ordinal)
            .Select(condition => (condition!.Label, ConditionYarnTranslator.Translate(condition)))
            .Where(pair => pair.Item2.IsTranslatable)
            .Select(pair => (pair.Label, pair.Item2.Yarn!))
            .ToList();

        if (used.Count == 0)
        {
            return;
        }

        string supplyName = ConditionSupplyNodeName(chapter.ChapterId);

        SetNode? supply = editor.Project.EnumerateNodes().OfType<SetNode>()
            .FirstOrDefault(candidate => string.Equals(candidate.Name, supplyName, StringComparison.Ordinal));

        supply ??= editor.AddSetNode(fileId, name: supplyName);
        editor.AddSettingsLink(supply.Id, node.Id);

        // 링크까지 이은 뒤의 실제 가용 목록과 대조한다 — 정의 파일의 전역 조건이나 다른
        // 설정노드가 이미 같은 식을 주고 있으면 여기서 또 만들지 않는다.
        Flow.AvailableConditionCatalog available =
            Flow.AvailableConditionResolver.Resolve(editor.Project, node.Id, definition);

        HashSet<string> knownExpressions = available.Conditions
            .Select(condition => condition.Expression.Trim())
            .ToHashSet(StringComparer.Ordinal);

        foreach ((string label, string yarn) in used)
        {
            if (knownExpressions.Add(yarn))
            {
                editor.AddCondition(supply.Id, label, yarn);
            }
        }
    }

    /// <summary>
    /// 챕터의 조건 <b>전부</b>를 그 판의 모든 대사 노드가 쓸 수 있게 공급한다 (2단계 4번).
    /// 작가의 자유 노드가 조건 드롭다운에서 챕터 라벨(A 계층)을 바로 고르게 하는 자리다 —
    /// 설정노드를 손으로 찾아 잇는 절차가 없어야 "개발자를 안 부르고" 조건을 건다.
    /// 링크·조건 모두 멱등이다(있으면 다시 만들지 않는다).
    /// </summary>
    public static void SupplyChapterConditionsToBoard(
        ProjectEditor editor,
        GameDefinition definition,
        string fileId,
        ChapterGraphModel chapter)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(chapter);

        List<(string Label, string Yarn)> translatable = chapter.Conditions
            .Select(condition => (condition.Label, Translated: ConditionYarnTranslator.Translate(condition)))
            .Where(pair => pair.Translated.IsTranslatable)
            .Select(pair => (pair.Label, pair.Translated.Yarn!))
            .ToList();

        if (translatable.Count == 0)
        {
            return;
        }

        string supplyName = ConditionSupplyNodeName(chapter.ChapterId);

        SetNode? supply = editor.Project.EnumerateNodes().OfType<SetNode>()
            .FirstOrDefault(candidate => string.Equals(candidate.Name, supplyName, StringComparison.Ordinal));

        supply ??= editor.AddSetNode(fileId, name: supplyName);

        HashSet<string> known = supply.Conditions
            .Select(condition => condition.Expression.Trim())
            .ToHashSet(StringComparer.Ordinal);

        foreach ((string label, string yarn) in translatable)
        {
            if (known.Add(yarn))
            {
                editor.AddCondition(supply.Id, label, yarn);
            }
        }

        StoryFile? file = editor.Project.Files.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, fileId, StringComparison.Ordinal));

        foreach (DialogueNode dialogue in (file?.Nodes ?? []).OfType<DialogueNode>())
        {
            editor.AddSettingsLink(supply.Id, dialogue.Id);
        }
    }

    /// <summary>
    /// 가드레일 (2단계 4번) — 작가의 자유 노드가 Tier 2 스탯(A 계층)에 <c>&lt;&lt;set&gt;&gt;</c>을
    /// 걸면 경고한다. 대사 중의 스탯 직접 조작은 설계에서 통째로 뺐다(2026-08-14 — J열 폐지):
    /// 세이브/로드 복귀·도달성 증명이 못 보는 값 변화가 대본에 숨는다.
    /// 막지는 않는다(재생은 되니까), 크게 말한다.
    /// </summary>
    public static IReadOnlyList<ChapterDiagnostic> WarnFreeNodeStatWrites(
        ProjectEditor editor,
        string fileId,
        ChapterGraphModel chapter)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(chapter);

        HashSet<string> statKeys = chapter.Stats
            .Select(stat => stat.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (statKeys.Count == 0)
        {
            return Array.Empty<ChapterDiagnostic>();
        }

        StoryFile? file = editor.Project.Files.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, fileId, StringComparison.Ordinal));

        var warnings = new List<ChapterDiagnostic>();

        foreach (DialogueNode dialogue in (file?.Nodes ?? []).OfType<DialogueNode>())
        {
            if (dialogue.ExcelEpisodeId is not null)
            {
                continue; // 엑셀노드의 본문은 동기화 산출물 — set이 애초에 못 붙는다
            }

            foreach (DialogueLineExtension extension in dialogue.LineExtensions)
            {
                foreach (SetOperation operation in extension.SetOperations)
                {
                    string variable = operation.Variable.TrimStart('$');

                    if (statKeys.Contains(variable))
                    {
                        warnings.Add(new ChapterDiagnostic(
                            ChapterDiagnosticSeverity.Warning,
                            ChapterDiagnosticCode.StatKeyUnknown,
                            chapter.SourcePath, null, null, null,
                            $"자유 노드 '{dialogue.Name}'이 스탯 '{variable}'을 set으로 바꿉니다 — " +
                            "대사 중의 스탯 직접 조작은 설계에서 뺐습니다(세이브/로드·도달성 증명이 " +
                            "못 봅니다). 로컬 변수를 쓰세요. 수치 조정 방식은 별도로 정해집니다."));
                    }
                }
            }
        }

        return warnings;
    }

    /// <summary>
    /// 가드레일 — 판 위 노드의 출구(기본·갈래)가 엑셀노드를 가리키면 경고 (소유자 결정
    /// 2026-08-14). 에피소드 사이의 흐름은 챕터 간선이 소유하므로, 이 점프는 챕터 장부
    /// (표시/해금 검사·에피소드 끝 스탯 환산·cleared 기록)를 전부 지나친다. 게다가 Yarn
    /// 점프는 노드 처음부터 다시 재생하므로 같은 에피소드로의 "복귀"도 함정이다.
    /// 편집기는 후보에서 빼지만, 이미 있는 연결(옛 프로젝트)은 막지 않고 크게 말한다.
    /// </summary>
    public static IReadOnlyList<ChapterDiagnostic> WarnExitsIntoExcelNodes(
        ProjectEditor editor,
        string fileId,
        ChapterGraphModel chapter)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(chapter);

        Dictionary<string, DialogueNode> excelNodes = editor.Project.EnumerateNodes()
            .OfType<DialogueNode>()
            .Where(node => node.ExcelEpisodeId is not null)
            .ToDictionary(node => node.Id, node => node, StringComparer.Ordinal);

        if (excelNodes.Count == 0)
        {
            return Array.Empty<ChapterDiagnostic>();
        }

        StoryFile? file = editor.Project.Files.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, fileId, StringComparison.Ordinal));

        var warnings = new List<ChapterDiagnostic>();

        foreach (DialogueNode dialogue in (file?.Nodes ?? []).OfType<DialogueNode>())
        {
            void Check(string? targetId, string exitKind)
            {
                if (targetId is not null && excelNodes.TryGetValue(targetId, out DialogueNode? target))
                {
                    warnings.Add(new ChapterDiagnostic(
                        ChapterDiagnosticSeverity.Warning,
                        ChapterDiagnosticCode.ExitIntoExcelNode,
                        chapter.SourcePath, null, null, null,
                        $"노드 '{dialogue.Name}'의 {exitKind}가 엑셀노드 '{target.Name}'을 가리킵니다 — " +
                        "에피소드 사이의 흐름은 챕터 간선(기획자)이 정합니다. 이 점프는 표시/해금·" +
                        "cleared 기록을 지나치고, 도착 노드를 처음부터 다시 재생합니다. 출구를 " +
                        "자유 노드로 바꾸거나 비워 주세요(비우면 에피소드 종료)."));
                }
            }

            Check(dialogue.DefaultExitTargetNodeId, "기본 출구");

            foreach ((_, string targetId) in dialogue.BranchExits)
            {
                Check(targetId, "갈래 출구");
            }
        }

        return warnings;
    }

    private static IReadOnlyList<EpisodePrunedLogic> CollectPruned(
        ScriptSyncPlan plan,
        IReadOnlyDictionary<string, DialogueLineExtension> extensionsBefore,
        IReadOnlyCollection<string> exitsBefore)
    {
        var pruned = new List<EpisodePrunedLogic>();

        foreach (ScriptSyncEntry entry in plan.Entries
                     .Where(item => item.Kind == ScriptSyncKind.Deleted && item.LineId is not null))
        {
            string lineId = entry.LineId!;
            extensionsBefore.TryGetValue(lineId, out DialogueLineExtension? extension);
            int exits = exitsBefore.Contains(lineId) ? 1 : 0;
            int transitions = extension?.Transition is null ? 0 : 1;
            int sets = extension?.SetOperations.Count ?? 0;

            if (transitions + sets + exits > 0)
            {
                pruned.Add(new EpisodePrunedLogic(lineId, transitions, sets, exits));
            }
        }

        return pruned;
    }

    /// <summary>
    /// 챕터 화자를 정의의 speakers에 합친 <b>메모리 사본</b>. 정의에 이미 있는 이름은
    /// 그대로 두고(초상화 매핑 우선), 챕터에만 있는 이름을 뒤에 더한다. 합칠 것이 없으면
    /// 원본을 그대로 돌려준다.
    /// </summary>
    private static GameDefinition AugmentWithChapterSpeakers(
        GameDefinition definition, ChapterGraphModel? chapter)
    {
        if (chapter is null || chapter.Speakers.Count == 0)
        {
            return definition;
        }

        List<SpeakerSpec> additions = chapter.Speakers
            .Where(speaker => !definition.Speakers.Any(existing =>
                string.Equals(existing.Name, speaker.Name, StringComparison.Ordinal)))
            .Select(speaker => new SpeakerSpec
            {
                Name = speaker.Name,
                CharacterId = speaker.CharacterId ?? string.Empty
            })
            .ToList();

        if (additions.Count == 0)
        {
            return definition;
        }

        return new GameDefinition
        {
            Variables = definition.Variables,
            Events = definition.Events,
            Conditions = definition.Conditions,
            PresentationCommandCategories = definition.PresentationCommandCategories,
            PresentationCommands = definition.PresentationCommands,
            Speakers = definition.Speakers.Concat(additions).ToList(),
            Preview = definition.Preview,
            RuntimeTuningPath = definition.RuntimeTuningPath
        };
    }

    // ── LineId 되쓰기 ───────────────────────────────────────────────────────

    private static EpisodeSyncReport Refused(
        string episodeId,
        string workbookPath,
        IReadOnlyList<ChapterDiagnostic> diagnostics,
        IReadOnlyList<string> problems) =>
        new(episodeId, workbookPath, DialogueNodeId: null,
            Applied: false, diagnostics, problems,
            Array.Empty<EpisodePrunedLogic>(), Array.Empty<string>());
}
