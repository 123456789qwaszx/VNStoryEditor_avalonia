using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;
using Vn.Authoring.Script;

namespace Vn.Authoring.Editing;

/// <summary>
/// ScenarioOnly 붙여넣기 한 번의 결과 (X12a). 적용 여부와 이유가 전부 담긴다 —
/// 적용하지 않았다면 왜인지(충돌·삭제 확인 대기), 적용했다면 무엇이 어떻게 이어졌는지.
/// </summary>
public sealed record ScenarioPasteOutcome(
    ScenarioParseResult Parsed,
    ScriptSyncPlan? Plan,
    bool Applied,
    bool NeedsDeleteConfirmation,
    IReadOnlyList<string> Problems)
{
    public string Summary()
    {
        if (Plan is null)
        {
            return "반영할 대사 줄이 없습니다.";
        }

        if (!Applied)
        {
            return NeedsDeleteConfirmation
                ? $"삭제 {Plan.Count(ScriptSyncKind.Deleted)}줄이 있어 확인이 필요합니다. {Plan.Summary()}"
                : $"확인이 필요해 반영하지 않았습니다. {Plan.Summary()}";
        }

        return Plan.Summary();
    }
}

/// <summary>
/// 대본을 바꾸는 편집 명령.
///
/// <b>화자와 대사를 바꾸는 코드는 이 파일 안에만 있다.</b> DialogueNode를 지나는 경로는
/// 없다. 그래야 "한 줄을 고쳤을 때 권위 있는 데이터가 하나만 바뀐다"가 코드 구조로
/// 보장된다.
/// </summary>
public sealed partial class ProjectEditor
{
    public ScriptDocument AddScript(string? name = null)
    {
        var script = new ScriptDocument(name: name ?? NextScriptName());
        script.RequireLocale(script.PrimaryLocale);

        Mutate(() => Project.Scripts.Add(script));
        return script;
    }

    /// <summary>
    /// ScenarioOnly 텍스트를 대사 노드에 반영한다 (X12a). <b>전량 재생성은 없다</b> —
    /// 라인 매칭은 원본 대본 재동기화와 같은 <see cref="ScriptSynchronizer"/>를 지나므로
    /// 기존 라인의 LineId는 보존되고(불변식 1), 확신 없는 연결(Ambiguous)이 하나라도
    /// 있으면 아무것도 바꾸지 않는다. 삭제는 <paramref name="confirmDeletes"/>로 확인받는다.
    ///
    /// 조건 구조(<c>&lt;&lt;if&gt;&gt;</c>…)는 diff 뒤 해당 라인의 전환으로 반영하되, 식은 이 노드가
    /// 쓸 수 있는 조건과 <b>정확 일치</b>로만 역조회한다(보정 금지). 선택 전환은 파서
    /// 비범위이므로 건드리지 않는다. 해석 못 한 것은 전부 Problems로 남는다(규칙 14).
    /// </summary>
    public ScenarioPasteOutcome ApplyScenarioText(
        string dialogueNodeId,
        string text,
        GameDefinition definition,
        bool confirmDeletes = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        DialogueNode node = RequireDialogue(dialogueNodeId);
        ScriptDocument script = EnsureDialogueScript(node.Id);

        ScenarioParseResult parsed = ScenarioTextParser.Parse(text, definition);
        var problems = new List<string>(parsed.UnparsedLines);

        if (parsed.Lines.Count == 0)
        {
            return new ScenarioPasteOutcome(parsed, null, Applied: false, NeedsDeleteConfirmation: false, problems);
        }

        var parsedScript = new ParsedScript(
            parsed.Lines
                // 파서가 떼어낸 #line: 신원을 그대로 넘긴다. 여기서 버리면 동기화가 ID를 못 보고
                // 내용 추정으로 되돌아간다 — 엑셀 경로에서는 그게 통째 거부의 원인이 된다.
                .Select((line, index) =>
                    new ParsedScriptLine(index + 1, line.Speaker, line.Text, line.LineId))
                .ToList(),
            Array.Empty<ScriptParseProblem>(),
            Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty))));

        ScriptSyncPlan plan = ScriptSynchronizer.Plan(script, parsedScript, newLineId: _newLineId);

        if (plan.HasConflicts)
        {
            problems.AddRange(plan.Conflicts.Select(entry => entry.Message ?? "확인 필요"));
            return new ScenarioPasteOutcome(parsed, plan, Applied: false, NeedsDeleteConfirmation: false, problems);
        }

        if (plan.Count(ScriptSyncKind.Deleted) > 0 && !confirmDeletes)
        {
            return new ScenarioPasteOutcome(parsed, plan, Applied: false, NeedsDeleteConfirmation: true, problems);
        }

        ApplyScriptSync(plan);
        ApplyScenarioTransitions(node, parsed, plan, definition, problems);

        return new ScenarioPasteOutcome(parsed, plan, Applied: true, NeedsDeleteConfirmation: false, problems);
    }

    /// <summary>diff가 확정한 LineId 위로 조건 전환을 맞춘다. 선택 전환은 보호한다.</summary>
    private void ApplyScenarioTransitions(
        DialogueNode node,
        ScenarioParseResult parsed,
        ScriptSyncPlan plan,
        GameDefinition definition,
        List<string> problems)
    {
        AvailableConditionCatalog conditions = AvailableConditionResolver.Resolve(Project, node.Id, definition);

        // 새 순서 인덱스 → 확정 LineId (사라진 줄 항목은 NewIndex가 없다).
        Dictionary<int, string> lineIdByNewIndex = plan.Entries
            .Where(entry => entry is { NewIndex: not null, LineId: not null })
            .ToDictionary(entry => entry.NewIndex!.Value, entry => entry.LineId!);

        for (int index = 0; index < parsed.Lines.Count; index++)
        {
            if (!lineIdByNewIndex.TryGetValue(index, out string? lineId))
            {
                continue;
            }

            ScenarioStructureIntent? intent = parsed.Lines[index].Transition;
            LineConditionTransition? current = node.FindExtension(lineId)?.Transition;
            bool intentIsChoice = intent?.Kind
                is ConditionTransitionKind.BeginChoice
                or ConditionTransitionKind.BeginNextOption
                or ConditionTransitionKind.EndChoice;

            // 텍스트에 선택 전환이 안 보였다고 기존 것을 지우지 않는다 — 손으로 쓴 대본
            // 붙여넣기 한 번에 선택지 구조가 조용히 무너진다. 조건 전환을 얹으려는 것만 막는다.
            if (current?.IsChoiceKind == true && !intentIsChoice)
            {
                if (intent is not null)
                {
                    problems.Add(
                        $"'{parsed.Lines[index].Text}' 줄은 선택 전환을 갖고 있어 조건 전환을 얹을 수 없습니다.");
                }

                continue;
            }

            LineConditionTransition? desired = null;

            if (intent is not null)
            {
                switch (intent.Kind)
                {
                    case ConditionTransitionKind.EndIf:
                        desired = LineConditionTransition.EndIf();
                        break;

                    // 옵션 줄(->)이 파서를 지나 여기 온다 (G3-2). OptionId는 만들지 않는다 —
                    // 텍스트에는 옵션의 안정 식별자가 없고, 있던 것은 아래 비교가 보존한다.
                    case ConditionTransitionKind.BeginChoice:
                        desired = LineConditionTransition.BeginChoice();
                        break;

                    case ConditionTransitionKind.BeginNextOption:
                        desired = LineConditionTransition.BeginNextOption();
                        break;

                    case ConditionTransitionKind.EndChoice:
                        desired = LineConditionTransition.EndChoice();
                        break;

                    default:
                    {
                        AvailableCondition? condition = conditions.Conditions.FirstOrDefault(item =>
                            string.Equals(item.Expression.Trim(), intent.Expression, StringComparison.Ordinal));

                        if (condition is null)
                        {
                            // 비슷한 식을 추측해 잇지 않는다. 조건은 설정노드에서 먼저 만들어야 한다.
                            problems.Add(
                                $"<<{(intent.Kind == ConditionTransitionKind.BeginIf ? "if" : "elseif")} {intent.Expression}>> — " +
                                "이 노드가 쓸 수 있는 조건 중에 같은 식이 없습니다. 전환은 반영하지 않았습니다.");
                            continue;
                        }

                        desired = intent.Kind == ConditionTransitionKind.BeginIf
                            ? LineConditionTransition.BeginIf(condition.Id)
                            : LineConditionTransition.BeginElseIf(condition.Id);
                        break;
                    }
                }
            }

            // 선택 전환은 Kind만 비교한다 — 텍스트에는 옵션의 안정 식별자(op_)가 없으므로,
            // 같은 종류면 기존 OptionId를 그대로 둔다. 지우면 순서 변경 감지(C3 경고)가 죽는다.
            bool same = (current is null && desired is null) ||
                (current is not null && desired is not null &&
                 current.Kind == desired.Kind &&
                 (desired.IsChoiceKind ||
                  string.Equals(current.ConditionId, desired.ConditionId, StringComparison.Ordinal)));

            if (!same)
            {
                SetLineTransition(node.Id, lineId, desired);
            }
        }
    }

    /// <summary>
    /// 대본 없는 대사 노드(가져오기 시절의 옛 프로젝트)에 전용 대본을 만들어 잇는다 (X4).
    /// 이미 대본이 있으면 그대로 돌려준다.
    /// </summary>
    public ScriptDocument EnsureDialogueScript(string dialogueNodeId)
    {
        DialogueNode node = RequireDialogue(dialogueNodeId);

        if (Project.FindScript(node.ScriptId) is { } existing)
        {
            return existing;
        }

        var script = new ScriptDocument(name: $"{node.Name} 대본");
        script.RequireLocale(script.PrimaryLocale);

        Mutate(() =>
        {
            Project.Scripts.Add(script);
            node.ScriptId = script.Id;
        });

        return script;
    }

    public void RenameScript(string scriptId, string name)
    {
        if (Project.FindScript(scriptId) is { } script &&
            !string.Equals(script.Name, name, StringComparison.Ordinal))
        {
            Mutate(ProjectChangeKind.NodeMetadata, () => script.Name = name);
        }
    }

    /// <summary>
    /// 대본을 지운다. 그 대본을 읽던 DialogueNode의 조건 구조는 남겨 둔다.
    /// 지우면 작가가 만든 갈래가 조용히 무너지고, 대본을 다시 붙여도 되살아나지 않는다.
    /// </summary>
    public void RemoveScript(string scriptId)
    {
        if (Project.FindScript(scriptId) is not { } script)
        {
            return;
        }

        Mutate(() =>
        {
            Project.Scripts.Remove(script);

            foreach (DialogueNode node in Project.EnumerateNodes().OfType<DialogueNode>())
            {
                if (string.Equals(node.ScriptId, scriptId, StringComparison.Ordinal))
                {
                    node.ScriptId = null;
                }
            }
        });
    }

    /// <summary>
    /// 작가의 평평한 대본 텍스트를 읽어 동기화 계획을 세운다. 아무것도 바꾸지 않는다.
    /// 적용은 <see cref="ApplyScriptSync"/>가 한다.
    /// </summary>
    public ScriptSyncPlan PlanScriptSync(
        string scriptId,
        string rawText,
        string? sourcePath = null,
        string? locale = null)
    {
        ScriptDocument script = RequireScript(scriptId);
        return ScriptSynchronizer.Plan(
            script,
            ScriptParser.Parse(rawText),
            locale,
            sourcePath,
            _newLineId);
    }

    /// <summary>
    /// 계획을 적용한다. <see cref="ScriptSyncPlan.HasConflicts"/>면 아무것도 하지 않고 거부한다.
    /// 도구가 대신 고르는 순간 작가가 쓰지 않은 연출이 다른 대사에 붙는다.
    /// </summary>
    public void ApplyScriptSync(ScriptSyncPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        ScriptDocument script = RequireScript(plan.ScriptId);

        if (plan.HasConflicts)
        {
            throw new InvalidOperationException(
                "확인이 필요한 항목이 있어 대본을 동기화하지 않았습니다." + Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    plan.Conflicts.Select(entry => "• " + (entry.Message ?? entry.LineId))));
        }

        Mutate(() =>
        {
            ScriptLocale locale = script.RequireLocale(plan.Locale);
            var byId = script.Lines.ToDictionary(line => line.Id, StringComparer.Ordinal);
            var ordered = new List<ScriptLine>();
            var kept = new HashSet<string>(StringComparer.Ordinal);

            foreach (ScriptSyncEntry entry in plan.Entries.Where(item => item.NewIndex is not null))
            {
                string lineId = entry.LineId
                    ?? throw new InvalidOperationException("동기화 계획에 LineId 없는 항목이 있습니다.");

                if (!byId.TryGetValue(lineId, out ScriptLine? line))
                {
                    line = new ScriptLine(lineId);
                }

                if (entry.Kind == ScriptSyncKind.Modified)
                {
                    line.Revision++;
                }

                line.IsRetired = false;
                locale.Entries[lineId] = entry.Next ?? LocalizedLine.Empty;
                ordered.Add(line);
                kept.Add(lineId);
            }

            // 사라진 줄은 지우지 않고 목록 뒤에 은퇴 상태로 남긴다.
            // 본문도 남긴다. 고아 연출을 볼 때 그 줄이 무엇이었는지 알 수 있어야 한다.
            foreach (ScriptLine line in script.Lines.Where(line => !kept.Contains(line.Id)))
            {
                line.IsRetired = true;
                ordered.Add(line);
            }

            // 은퇴한 줄이 소유하던 대사 논리는 함께 접는다 (W47) — 카드가 없는 줄의
            // 조건은 지울 진입점조차 없는 유령이 된다.
            PruneRetiredLineLogic(script.Id, lineId => !kept.Contains(lineId));

            script.Lines.Clear();
            script.Lines.AddRange(ordered);
            script.SourceRevision++;
            script.SourceContentHash = plan.SourceContentHash;

            if (plan.SourcePath is not null)
            {
                script.SourcePath = plan.SourcePath;
            }
        });
    }

    /// <summary>대본에 빈 줄 하나를 끼워 넣는다. 새 LineId를 발급한다.</summary>
    public ScriptLine InsertScriptLine(string scriptId, int? index = null, string? locale = null)
    {
        ScriptDocument script = RequireScript(scriptId);
        var line = new ScriptLine(_newLineId());
        string targetLocale = locale ?? script.PrimaryLocale;

        Mutate(() =>
        {
            int at = index ?? script.Lines.Count;
            script.Lines.Insert(Math.Clamp(at, 0, script.Lines.Count), line);
            script.RequireLocale(targetLocale).Entries[line.Id] = LocalizedLine.Empty;
        });

        return line;
    }

    /// <summary>
    /// 대본에서 줄을 은퇴시킨다. 물리적으로 지우지 않는다.
    /// 지우면 그 LineId를 가리키던 연출이 왜 고아가 되었는지 물을 수조차 없게 된다.
    /// </summary>
    public void RetireScriptLine(string scriptId, string lineId)
    {
        ScriptDocument script = RequireScript(scriptId);

        if (script.FindLine(lineId) is not { IsRetired: false } line)
        {
            return;
        }

        Mutate(() =>
        {
            line.IsRetired = true;
            PruneRetiredLineLogic(scriptId, id => string.Equals(id, lineId, StringComparison.Ordinal));
        });
    }

    /// <summary>
    /// 줄이 대본에서 물러나면 그 줄이 소유하던 대사 논리(조건·선택 전환, set, 갈래 출구)도
    /// 접는다 (W47) — 줄 카드가 없으면 편집 진입점이 없어 지울 수도 없는 유령이 된다.
    /// 연출 바인딩은 남긴다: 고아 연출은 지우지 않고 해석기가 알리는 기존 정책 그대로다.
    /// </summary>
    private void PruneRetiredLineLogic(string scriptId, Func<string, bool> retired)
    {
        foreach (DialogueNode node in Project.EnumerateNodes().OfType<DialogueNode>()
            .Where(item => string.Equals(item.ScriptId, scriptId, StringComparison.Ordinal)))
        {
            node.LineExtensions.RemoveAll(extension => retired(extension.LineId));

            foreach (string exitLineId in node.BranchExits.Keys.Where(retired).ToList())
            {
                node.BranchExits.Remove(exitLineId);
            }
        }
    }

    public void MoveScriptLine(string scriptId, string lineId, int delta)
    {
        ScriptDocument script = RequireScript(scriptId);
        List<ScriptLine> active = script.ActiveLines.ToList();
        int from = active.FindIndex(line => string.Equals(line.Id, lineId, StringComparison.Ordinal));

        if (from < 0)
        {
            return;
        }

        int to = Math.Clamp(from + delta, 0, active.Count - 1);

        if (to == from)
        {
            return;
        }

        Mutate(() =>
        {
            ScriptLine line = active[from];
            active.RemoveAt(from);
            active.Insert(to, line);

            List<ScriptLine> retired = script.Lines.Where(item => item.IsRetired).ToList();
            script.Lines.Clear();
            script.Lines.AddRange(active);
            script.Lines.AddRange(retired);
        });
    }

    /// <summary>
    /// 화자와 대사를 바꾼다. <b>도구 안에서 대사 본문을 바꾸는 유일한 명령이다.</b>
    /// 내용만 바뀌므로 편집 컨트롤을 다시 만들지 않는다.
    /// </summary>
    public void SetScriptLineText(
        string scriptId,
        string lineId,
        string speaker,
        string text,
        string? locale = null)
    {
        ScriptDocument script = RequireScript(scriptId);

        if (script.FindLine(lineId) is not { } line)
        {
            return;
        }

        string targetLocale = locale ?? script.PrimaryLocale;
        LocalizedLine current = script.Text(lineId, targetLocale);
        var next = new LocalizedLine(speaker, text);

        if (current == next)
        {
            return;
        }

        Mutate(ProjectChangeKind.DialogueContent, () =>
        {
            script.RequireLocale(targetLocale).Entries[lineId] = next;

            // 기준 locale의 문구가 바뀌면 번역·녹음이 다시 봐야 한다는 뜻이다.
            if (string.Equals(targetLocale, script.PrimaryLocale, StringComparison.Ordinal))
            {
                line.Revision++;
            }
        });
    }

    /// <summary>이 대사 노드가 읽을 대본을 정한다. 조건 구조는 LineId 기준으로 그대로 남는다.</summary>
    public void SetDialogueScript(string dialogueNodeId, string? scriptId)
    {
        DialogueNode node = RequireDialogue(dialogueNodeId);

        if (scriptId is not null && Project.FindScript(scriptId) is null)
        {
            throw new InvalidOperationException($"대본 '{scriptId}'를 찾을 수 없습니다.");
        }

        if (string.Equals(node.ScriptId, scriptId, StringComparison.Ordinal))
        {
            return;
        }

        Mutate(() => node.ScriptId = scriptId);
    }

    private ScriptDocument RequireScript(string scriptId)
    {
        return Project.FindScript(scriptId)
            ?? throw new InvalidOperationException($"대본 '{scriptId}'를 찾을 수 없습니다.");
    }

    private string NextScriptName() => $"대본 {Project.Scripts.Count + 1}";
}
