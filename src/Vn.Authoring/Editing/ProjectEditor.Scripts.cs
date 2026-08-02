using Vn.Authoring.Model;
using Vn.Authoring.Script;

namespace Vn.Authoring.Editing;

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

        Mutate(() => line.IsRetired = true);
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
