using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Serialization;

namespace Vn.Authoring.Editing;

/// <summary>
/// 프로젝트를 바꾸는 유일한 통로.
///
/// 대사 화면과 그래프 화면은 서로 이야기하지 않는다. 둘 다 이 객체에 의도를 전달하고,
/// <see cref="Changed"/>를 듣고 다시 그린다. 화면끼리 직접 연결하면 화면 수가 늘어날 때마다
/// 연결 수가 제곱으로 늘고, 어느 화면이 최신인지 알 수 없게 된다.
///
/// 되돌리기는 스냅샷 방식이다. 편집 명령마다 역연산을 따로 만들면 명령이 늘어날수록
/// 짝이 맞지 않는 자리가 생긴다. 이 규모에서는 프로젝트 전체를 직렬화해 쌓는 편이
/// 더 단순하고 확실하다.
/// </summary>
public sealed class ProjectEditor
{
    private const int MaxHistory = 100;

    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();

    public ProjectEditor(StoryProject project)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
    }

    public StoryProject Project { get; private set; }

    /// <summary>
    /// 무엇이든 바뀌었을 때. 듣는 쪽은 현재 상태를 다시 읽는다.
    ///
    /// 바뀐 것이 구조인지 내용인지 함께 알린다. 화자·대사를 한 글자 칠 때마다 카드 목록을
    /// 통째로 다시 만들면 편집 중인 칸이 사라져 글을 쓸 수 없다. 종류를 알려 주면
    /// 화면이 "다시 그릴 것인가"를 스스로 판단할 수 있고, 그 판단이 화면마다 다를 수 있다.
    /// </summary>
    public event EventHandler<ProjectChangedEventArgs>? Changed;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    // ── 노드 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 새 노드를 만든다. 파일 순서의 <b>가장 아래</b>에 붙는다.
    /// 중간에 끼워 넣지 않는 이유는, 그래프에서의 위치와 파일 순서가 별개이기 때문이다.
    /// 시각적으로 위에 놓았다고 파일에서도 위로 가면 diff가 매번 크게 흔들린다.
    /// </summary>
    public TNode AddNode<TNode>(TNode node) where TNode : StoryNode
    {
        Mutate(() =>
        {
            Project.Nodes.Add(node);
            Project.StartNodeId ??= node.Id;
        });

        return node;
    }

    public DialogueNode AddDialogueNode(double x = 0, double y = 0, string? name = null)
    {
        var node = new DialogueNode(name: name ?? NextName("장면"))
        {
            Layout = new NodeLayout { X = x, Y = y }
        };

        // 빈 노드에서 바로 쓰기 시작할 수 있게 줄 하나를 둔다.
        node.Lines.Add(new LineBox());
        return AddNode(node);
    }

    public SetNode AddSetNode(double x = 0, double y = 0, string? name = null)
    {
        var node = new SetNode(name: name ?? NextName("설정"))
        {
            Layout = new NodeLayout { X = x, Y = y }
        };

        return AddNode(node);
    }

    public void RemoveNode(string nodeId)
    {
        StoryNode? node = Project.FindNode(nodeId);

        if (node is null)
        {
            return;
        }

        Mutate(() =>
        {
            Project.Nodes.Remove(node);

            // 사라진 노드를 가리키던 출구를 남겨 두면 그래프에 끊어진 간선이 생긴다.
            foreach (StoryNode other in Project.Nodes)
            {
                if (string.Equals(other.DefaultExitTargetNodeId, nodeId, StringComparison.Ordinal))
                {
                    other.DefaultExitTargetNodeId = null;
                }

                if (other is DialogueNode dialogue)
                {
                    foreach (string key in dialogue.BranchExits
                                 .Where(pair => string.Equals(pair.Value, nodeId, StringComparison.Ordinal))
                                 .Select(pair => pair.Key)
                                 .ToList())
                    {
                        dialogue.BranchExits.Remove(key);
                    }
                }
            }

            if (string.Equals(Project.StartNodeId, nodeId, StringComparison.Ordinal))
            {
                Project.StartNodeId = Project.Nodes.FirstOrDefault()?.Id;
            }
        });
    }

    public void RenameNode(string nodeId, string name)
    {
        if (Project.FindNode(nodeId) is { } node && !string.Equals(node.Name, name, StringComparison.Ordinal))
        {
            Mutate(ProjectChangeKind.NodeMetadata, () => node.Name = name);
        }
    }

    /// <summary>
    /// 그래프에서 노드를 끌어 놓은 결과. 되돌리기 기록을 남기지 않는다.
    /// 드래그 한 번에 수십 번 불리는 값이라 기록에 쌓으면 되돌리기가 좌표 이동으로 가득 찬다.
    /// </summary>
    public void MoveNode(string nodeId, double x, double y)
    {
        if (Project.FindNode(nodeId) is { } node)
        {
            node.Layout.X = x;
            node.Layout.Y = y;
            Raise(ProjectChangeKind.Layout);
        }
    }

    // ── 줄 ──────────────────────────────────────────────────────────────────

    public LineBox AddLine(string nodeId, int? index = null)
    {
        DialogueNode node = RequireDialogue(nodeId);
        var line = new LineBox();

        Mutate(() =>
        {
            int at = index ?? node.Lines.Count;
            node.Lines.Insert(Math.Clamp(at, 0, node.Lines.Count), line);
        });

        return line;
    }

    public void RemoveLine(string nodeId, string lineId)
    {
        DialogueNode node = RequireDialogue(nodeId);
        LineBox? line = node.Lines.FirstOrDefault(item => string.Equals(item.Id, lineId, StringComparison.Ordinal));

        if (line is null)
        {
            return;
        }

        Mutate(() =>
        {
            node.Lines.Remove(line);
            PruneBranchExits(node);
        });
    }

    /// <summary>
    /// 줄을 위아래로 옮긴다. 줄의 Id는 그대로이므로 그 줄이 열던 갈래와 출구가 함께 따라간다.
    /// </summary>
    public void MoveLine(string nodeId, string lineId, int delta)
    {
        DialogueNode node = RequireDialogue(nodeId);
        int from = node.Lines.FindIndex(item => string.Equals(item.Id, lineId, StringComparison.Ordinal));

        if (from < 0)
        {
            return;
        }

        int to = Math.Clamp(from + delta, 0, node.Lines.Count - 1);

        if (to == from)
        {
            return;
        }

        Mutate(() =>
        {
            LineBox line = node.Lines[from];
            node.Lines.RemoveAt(from);
            node.Lines.Insert(to, line);
        });
    }

    public void SetLineText(string nodeId, string lineId, string speaker, string text)
    {
        DialogueNode node = RequireDialogue(nodeId);
        LineBox? line = node.Lines.FirstOrDefault(item => string.Equals(item.Id, lineId, StringComparison.Ordinal));

        if (line is null ||
            (string.Equals(line.Speaker, speaker, StringComparison.Ordinal) &&
             string.Equals(line.Text, text, StringComparison.Ordinal)))
        {
            return;
        }

        // 글자만 바뀌는 편집이다. 화면은 이미 그 값을 보여 주고 있으므로 다시 만들지 않는다.
        Mutate(ProjectChangeKind.Content, () =>
        {
            line.Speaker = speaker;
            line.Text = text;
        });
    }

    /// <summary>
    /// 이 줄에서 조건 흐름을 어떻게 바꿀지 정한다.
    /// 갈래를 더 이상 열지 않게 되면 거기 매달려 있던 조건 출구도 함께 사라진다.
    /// </summary>
    public void SetLineTransition(string nodeId, string lineId, LineConditionTransition? transition)
    {
        DialogueNode node = RequireDialogue(nodeId);
        LineBox? line = node.Lines.FirstOrDefault(item => string.Equals(item.Id, lineId, StringComparison.Ordinal));

        if (line is null)
        {
            return;
        }

        Mutate(() =>
        {
            line.Transition = transition;
            PruneBranchExits(node);
        });
    }

    // ── 출구 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 포트 하나의 대상 노드를 정한다. null이면 연결을 끊는다.
    /// 그래프에서 간선을 끌든 노드 화면에서 드롭다운을 고르든 결국 이 하나를 부른다.
    /// </summary>
    public void SetExitTarget(ExitPort port, string? targetNodeId)
    {
        ArgumentNullException.ThrowIfNull(port);
        SetExitTarget(port.NodeId, port.Kind, port.BranchOpenLineId, targetNodeId);
    }

    public void SetExitTarget(
        string nodeId,
        ExitPortKind kind,
        string? branchOpenLineId,
        string? targetNodeId)
    {
        StoryNode? node = Project.FindNode(nodeId);

        if (node is null)
        {
            return;
        }

        if (targetNodeId is not null && Project.FindNode(targetNodeId) is null)
        {
            return;
        }

        if (kind == ExitPortKind.Default)
        {
            if (!string.Equals(node.DefaultExitTargetNodeId, targetNodeId, StringComparison.Ordinal))
            {
                Mutate(() => node.DefaultExitTargetNodeId = targetNodeId);
            }

            return;
        }

        if (node is not DialogueNode dialogue || branchOpenLineId is null)
        {
            return;
        }

        Mutate(() =>
        {
            if (targetNodeId is null)
            {
                dialogue.BranchExits.Remove(branchOpenLineId);
            }
            else
            {
                dialogue.BranchExits[branchOpenLineId] = targetNodeId;
            }
        });
    }

    // ── 조건 ────────────────────────────────────────────────────────────────

    public ConditionDefinition AddCondition(string setNodeId, string name, string expression)
    {
        var setNode = Project.FindNode(setNodeId) as SetNode
            ?? throw new InvalidOperationException($"'{setNodeId}'는 설정 노드가 아닙니다.");

        var condition = new ConditionDefinition { Name = name, Expression = expression };
        Mutate(() => setNode.Conditions.Add(condition));
        return condition;
    }

    public void UpdateCondition(string conditionId, string name, string expression)
    {
        ConditionDefinition? condition = Project.FindCondition(conditionId);

        if (condition is null ||
            (string.Equals(condition.Name, name, StringComparison.Ordinal) &&
             string.Equals(condition.Expression, expression, StringComparison.Ordinal)))
        {
            return;
        }

        // 조건 행의 개수나 순서는 그대로다. 현재 SetNode 입력 컨트롤을 다시 만들면
        // LostFocus로 커밋하는 순간 다음 클릭 대상이 사라진다. 대신 조건 이름을 사용하는
        // 그래프 라벨과 Dialogue 드롭다운만 새 값을 읽도록 별도 변경 종류를 알린다.
        Mutate(ProjectChangeKind.ConditionDefinition, () =>
        {
            condition.Name = name;
            condition.Expression = expression;
        });
    }

    /// <summary>
    /// 조건을 지운다. 그 조건을 쓰던 줄의 전환은 남겨 둔다.
    /// 지우면 작가가 쓴 갈래 구조가 조용히 무너지기 때문이다.
    /// 대신 <see cref="ConditionFlowResolver"/>가 "알 수 없는 조건"으로 알린다.
    /// </summary>
    public void RemoveCondition(string conditionId)
    {
        SetNode? owner = Project.Nodes.OfType<SetNode>()
            .FirstOrDefault(node => node.Conditions.Any(
                item => string.Equals(item.Id, conditionId, StringComparison.Ordinal)));

        if (owner is null)
        {
            return;
        }

        Mutate(() => owner.Conditions.RemoveAll(
            item => string.Equals(item.Id, conditionId, StringComparison.Ordinal)));
    }

    public void SetAssignments(string setNodeId, IEnumerable<VariableAssignment> assignments)
    {
        if (Project.FindNode(setNodeId) is not SetNode setNode)
        {
            return;
        }

        List<VariableAssignment> next = assignments.Select(item => item.Clone()).ToList();

        bool sameValues = setNode.Assignments.Count == next.Count;

        for (int index = 0; sameValues && index < next.Count; index++)
        {
            VariableAssignment current = setNode.Assignments[index];
            VariableAssignment replacement = next[index];

            sameValues =
                string.Equals(current.Variable, replacement.Variable, StringComparison.Ordinal) &&
                string.Equals(current.Value, replacement.Value, StringComparison.Ordinal);
        }

        if (sameValues)
        {
            return;
        }

        // 행 개수가 바뀌면 컨트롤 목록을 다시 만들어야 한다. 개수가 그대로인 채 변수명이나
        // 값만 바뀌었다면 현재 TextBox/AutoCompleteBox가 이미 그 값을 보여 주고 있으므로
        // 구조 재생성은 오히려 다음 클릭을 가로막는다.
        ProjectChangeKind kind = setNode.Assignments.Count == next.Count
            ? ProjectChangeKind.Content
            : ProjectChangeKind.Structure;

        Mutate(kind, () =>
        {
            setNode.Assignments.Clear();
            setNode.Assignments.AddRange(next);
        });
    }

    // ── 되돌리기 ────────────────────────────────────────────────────────────

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Add(ProjectJson.Write(Project));
        Restore(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Add(ProjectJson.Write(Project));
        Restore(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
    }

    /// <summary>파일에서 새로 읽었을 때처럼, 기록을 버리고 통째로 갈아 끼운다.</summary>
    public void Replace(StoryProject project)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
        _undo.Clear();
        _redo.Clear();
        Raise(ProjectChangeKind.Structure);
    }

    private void Restore(string snapshot)
    {
        Project = ProjectJson.Read(snapshot);
        Raise(ProjectChangeKind.Structure);
    }

    private void Mutate(Action change) => Mutate(ProjectChangeKind.Structure, change);

    private void Mutate(ProjectChangeKind kind, Action change)
    {
        _undo.Add(ProjectJson.Write(Project));

        if (_undo.Count > MaxHistory)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        change();
        Raise(kind);
    }

    private void Raise(ProjectChangeKind kind)
    {
        Changed?.Invoke(this, new ProjectChangedEventArgs(kind));
    }

    /// <summary>
    /// 더 이상 갈래를 열지 않는 줄에 매달린 조건 출구를 버린다.
    /// 남겨 두면 그래프에 주인 없는 간선이 생기고, 그 간선은 어느 화면에서도 지울 수 없다.
    /// </summary>
    private static void PruneBranchExits(DialogueNode node)
    {
        HashSet<string> opening = node.Lines
            .Where(line => line.Transition?.OpensBranch == true)
            .Select(line => line.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string key in node.BranchExits.Keys.Where(key => !opening.Contains(key)).ToList())
        {
            node.BranchExits.Remove(key);
        }
    }

    private DialogueNode RequireDialogue(string nodeId)
    {
        return Project.FindDialogue(nodeId)
            ?? throw new InvalidOperationException($"'{nodeId}'는 대사 노드가 아닙니다.");
    }

    private string NextName(string prefix)
    {
        int count = Project.Nodes.Count(node =>
            node.Name.StartsWith(prefix, StringComparison.Ordinal)) + 1;

        return $"{prefix} {count}";
    }
}
