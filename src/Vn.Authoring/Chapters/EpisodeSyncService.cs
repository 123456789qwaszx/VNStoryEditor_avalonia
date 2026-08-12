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
/// <param name="WrittenBackLineIds">워크북 B열에 되쓴 새 LineId들.</param>
/// <param name="WriteBackFailure">
/// 되쓰기를 못 한 사유. 엑셀이 파일을 잠그고 있으면 여기 남고, 워크북의 B열은 낡은 상태다 —
/// 다음 저장 때 다시 시도된다.
/// </param>
public sealed record EpisodeSyncReport(
    string EpisodeId,
    string WorkbookPath,
    string? DialogueNodeId,
    bool Applied,
    IReadOnlyList<ChapterDiagnostic> Diagnostics,
    IReadOnlyList<string> Problems,
    IReadOnlyList<EpisodePrunedLogic> Pruned,
    IReadOnlyList<string> WrittenBackLineIds,
    string? WriteBackFailure)
{
    public bool HasErrors =>
        !Applied ||
        Problems.Count > 0 ||
        Diagnostics.Any(item => item.Severity == ChapterDiagnosticSeverity.Error);

    /// <summary>배지에 올릴 거부·경고 건수.</summary>
    public int RejectionCount =>
        Problems.Count +
        Diagnostics.Count(item => item.Severity == ChapterDiagnosticSeverity.Error) +
        (WriteBackFailure is null ? 0 : 1);
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

        // 챕터가 없으면 라벨·스탯 검사는 빈 목록으로 돈다 — 조건라벨이 전부 미정의 오류가 되므로
        // 조용히 통과하는 일은 없다.
        string[] labels = chapter?.Conditions.Select(condition => condition.Label).ToArray()
            ?? Array.Empty<string>();
        string[] stats = chapter?.Stats.Select(stat => stat.Key).ToArray()
            ?? Array.Empty<string>();

        EpisodeWorkbookModel model;

        try
        {
            model = EpisodeWorkbookReader.Read(workbookPath, labels, stats);
        }
        catch (XlsxReadException exception)
        {
            return Refused(episodeId, workbookPath, Array.Empty<ChapterDiagnostic>(),
                [$"워크북을 읽지 못했습니다: {exception.Message}"]);
        }

        var conditions = (chapter?.Conditions ?? Array.Empty<ChapterCondition>() as IReadOnlyList<ChapterCondition>)
            .ToDictionary(condition => condition.Label, condition => condition, StringComparer.Ordinal);

        EpisodeFlattenResult flattened = EpisodeFlattener.Flatten(model, conditions);

        var diagnostics = model.Diagnostics.Concat(flattened.Diagnostics).ToList();

        if (model.HasErrors || flattened.HasErrors)
        {
            // 구조가 깨진 표를 대사노드에 밀어 넣지 않는다. 무엇이 왜 거부됐는지는 진단에 전부 있다.
            return Refused(episodeId, workbookPath, diagnostics,
                ["검증 오류가 있어 반영하지 않았습니다. 아래 목록을 고치고 다시 저장해 주세요."]);
        }

        DialogueNode node = FindOrCreateNode(editor, fileId, episodeId, chapter);
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
                Array.Empty<string>(),
                WriteBackFailure: null);
        }

        IReadOnlyList<EpisodePrunedLogic> pruned =
            CollectPruned(outcome.Plan!, extensionsBefore, exitsBefore);

        (IReadOnlyList<string> written, string? writeFailure) =
            WriteBackNewLineIds(workbookPath, model.SheetName, flattened, outcome.Plan!);

        return new EpisodeSyncReport(
            episodeId, workbookPath, node.Id,
            Applied: true,
            diagnostics,
            outcome.Problems,
            pruned,
            written,
            writeFailure);
    }

    /// <summary>
    /// 반영 대상 대사노드. 이름의 원천은 챕터 `에피소드` 시트의 `대사엔트리`다 — 런타임이
    /// 재생할 엔트리와 툴의 노드가 같은 이름을 쓴다. 챕터에 없는 에피소드면 EpisodeId를 쓴다.
    /// </summary>
    private static DialogueNode FindOrCreateNode(
        ProjectEditor editor, string fileId, string episodeId, ChapterGraphModel? chapter)
    {
        string name = chapter?.FindEpisode(episodeId)?.DialogueEntry is { Length: > 0 } entry
            ? entry
            : episodeId;

        DialogueNode? existing = editor.Project.EnumerateNodes()
            .OfType<DialogueNode>()
            .FirstOrDefault(node => string.Equals(node.Name, name, StringComparison.Ordinal));

        if (existing is not null)
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

        string supplyName = $"챕터 {chapter.ChapterId} 조건";

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

    // ── LineId 되쓰기 ───────────────────────────────────────────────────────

    /// <summary>
    /// 새로 발급된 LineId를 워크북 B열에 되쓴다. <b>B열 말고는 어떤 셀도 건드리지 않는다</b> —
    /// §3.2가 B열만 툴 소유로 정했다.
    ///
    /// 산출 줄 목록(<see cref="EpisodeFlattenResult.Lines"/>)과 동기화 계획의 새 줄 번호가
    /// 같은 텍스트에서 나왔으므로 자리로 맞물린다. 그 맞물림으로 "이 행의 새 ID"를 찾는다.
    ///
    /// 엑셀이 파일을 잠그고 있으면 쓰지 않고 사유를 남긴다 — 워크북은 낡은 상태가 되고,
    /// 기획자가 파일을 닫은 뒤의 다음 저장에서 다시 시도된다.
    /// </summary>
    private static (IReadOnlyList<string> Written, string? Failure) WriteBackNewLineIds(
        string workbookPath,
        string sheetName,
        EpisodeFlattenResult flattened,
        ScriptSyncPlan plan)
    {
        // 새 줄 번호 → 발급된 ID. Inserted만 — 나머지는 워크북이 이미 아는 신원이다.
        Dictionary<int, string> assigned = plan.Entries
            .Where(entry => entry.Kind == ScriptSyncKind.Inserted &&
                            entry.NewIndex is not null &&
                            entry.LineId is not null)
            .ToDictionary(entry => entry.NewIndex!.Value, entry => entry.LineId!);

        if (assigned.Count == 0)
        {
            return (Array.Empty<string>(), null);
        }

        var writes = new List<(int Row, string LineId)>();

        for (int position = 0; position < flattened.Lines.Count; position++)
        {
            EpisodeFlattenedLine line = flattened.Lines[position];

            if (line.LineId is null && assigned.TryGetValue(position, out string? newId))
            {
                writes.Add((line.SourceRow, newId));
            }
        }

        if (writes.Count == 0)
        {
            return (Array.Empty<string>(), null);
        }

        try
        {
            // 원본을 메모리로 복사해 연다. 이유 둘 — ① 경로 생성자는 실패 시 핸들을 놓지 않는다
            // (리더와 같은 이유). ② ClosedXML은 저장할 때 원본 스트림을 다시 읽으므로, 파일
            // 스트림을 살려 둔 채 같은 파일에 SaveAs를 하면 우리 자신의 읽기 핸들과 부딪친다.
            // 메모리 사본이면 원본 파일 핸들은 복사 순간에만 잡힌다.
            using var memory = new MemoryStream();

            using (var stream = new FileStream(
                       workbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                stream.CopyTo(memory);
            }

            memory.Position = 0;

            using var workbook = new XLWorkbook(memory);
            IXLWorksheet sheet = workbook.Worksheets.First(item =>
                string.Equals(item.Name, sheetName, StringComparison.Ordinal));

            foreach ((int row, string lineId) in writes)
            {
                sheet.Cell(row, 2).SetValue(lineId);
            }

            // 엑셀이 잠갔으면 여기서 깨끗하게 던지고 아래에서 사유가 된다.
            workbook.SaveAs(workbookPath);

            return (writes.Select(write => write.LineId).ToList(), null);
        }
        catch (Exception exception)
        {
            return (Array.Empty<string>(),
                $"새 LineId {writes.Count}개를 워크북에 되쓰지 못했습니다(파일이 잠겨 있을 수 있습니다): " +
                exception.Message + " — 워크북의 LineId 열은 낡은 상태이며 다음 저장 때 다시 시도됩니다.");
        }
    }

    private static EpisodeSyncReport Refused(
        string episodeId,
        string workbookPath,
        IReadOnlyList<ChapterDiagnostic> diagnostics,
        IReadOnlyList<string> problems) =>
        new(episodeId, workbookPath, DialogueNodeId: null,
            Applied: false, diagnostics, problems,
            Array.Empty<EpisodePrunedLogic>(), Array.Empty<string>(), WriteBackFailure: null);
}
