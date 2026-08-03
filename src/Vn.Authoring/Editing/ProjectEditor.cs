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
/// 되돌리기는 <see cref="ProjectSnapshotCodec"/>의 aggregate 스냅샷 방식이다.
/// 디스크의 manifest/StoryFile 배치와 무관하므로 물리 저장 구조가 바뀌어도 편집 기록은
/// 하나의 문자열로 안정적으로 왕복한다.
/// </summary>
public sealed partial class ProjectEditor
{
    private const int MaxHistory = 100;

    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<string> _newLineId;

    /// <param name="now">발행 시각의 출처. 테스트가 고정된 시각을 넣을 수 있게 주입한다.</param>
    /// <param name="newLineId">
    /// 새 LineId 발급기. 동기화 계획을 테스트에서 읽으려면 Id가 예측 가능해야 한다.
    /// </param>
    public ProjectEditor(
        StoryProject project,
        Func<DateTimeOffset>? now = null,
        Func<string>? newLineId = null)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _newLineId = newLineId ?? Identifier.Line;
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

    // ── 파일과 노드 ────────────────────────────────────────────────────────

    public StoryFile AddStoryFile(string? name = null)
    {
        var file = new StoryFile(name: name ?? NextFileName());

        Mutate(() => Project.Files.Add(file));
        return file;
    }

    public void RenameStoryFile(string fileId, string name)
    {
        if (Project.FindFile(fileId) is { } file && !string.Equals(file.Name, name, StringComparison.Ordinal))
        {
            Mutate(ProjectChangeKind.NodeMetadata, () => file.Name = name);
        }
    }

    /// <summary>
    /// 새 노드를 지정한 파일의 가장 아래에 붙인다.
    /// 노드 Id는 프로젝트 전체에서 유일해야 한다.
    /// </summary>
    public TNode AddNode<TNode>(string fileId, TNode node) where TNode : StoryNode
    {
        StoryFile file = RequireFile(fileId);

        if (Project.FindNode(node.Id) is not null)
        {
            throw new InvalidOperationException($"노드 Id '{node.Id}'가 프로젝트 안에서 중복됩니다.");
        }

        Mutate(() =>
        {
            file.Nodes.Add(node);

            // PresentationNode는 실행 흐름의 시작점이 아니다. 프로젝트에 연출 노드만 먼저
            // 만들어져도 이후 처음 추가되는 실행 노드가 정상적으로 시작 노드가 되어야 한다.
            if (node is not PresentationNode)
            {
                Project.StartNodeId ??= node.Id;
            }
        });

        return node;
    }

    public DialogueNode AddDialogueNode(
        string fileId,
        double x = 0,
        double y = 0,
        string? name = null,
        string? scriptId = null)
    {
        var node = new DialogueNode(name: name ?? NextName("장면"))
        {
            Layout = new NodeLayout { X = x, Y = y },
            ScriptId = scriptId
        };

        return AddNode(fileId, node);
    }

    public SetNode AddSetNode(
        string fileId,
        double x = 0,
        double y = 0,
        string? name = null)
    {
        var node = new SetNode(name: name ?? NextName("설정"))
        {
            Layout = new NodeLayout { X = x, Y = y }
        };

        return AddNode(fileId, node);
    }

    public PresentationNode AddPresentationNode(
        string fileId,
        double x = 0,
        double y = 0,
        string? name = null)
    {
        var node = new PresentationNode(name: name ?? NextName("연출"))
        {
            Layout = new NodeLayout { X = x, Y = y }
        };

        return AddNode(fileId, node);
    }

    public void RemoveNode(string nodeId)
    {
        StoryFile? owner = Project.FindFileContainingNode(nodeId);
        StoryNode? node = Project.FindNode(nodeId);

        if (owner is null || node is null)
        {
            return;
        }

        Mutate(() =>
        {
            owner.Nodes.Remove(node);
            RemoveReferencesToNode(nodeId);

            if (string.Equals(Project.StartNodeId, nodeId, StringComparison.Ordinal))
            {
                Project.StartNodeId = Project.EnumerateNodes()
                    .FirstOrDefault(candidate => candidate is not PresentationNode)
                    ?.Id;
            }
        });
    }

    /// <summary>
    /// 노드를 다른 StoryFile로 옮긴다. 같은 파일을 주면 그 파일 안에서 순서를 바꾼다.
    /// 노드의 Id와 그래프 좌표, 연결은 그대로 유지된다.
    /// </summary>
    public void MoveNodeToFile(string nodeId, string targetFileId, int? targetIndex = null)
    {
        StoryFile? source = Project.FindFileContainingNode(nodeId);
        StoryFile target = RequireFile(targetFileId);
        StoryNode? node = Project.FindNode(nodeId);

        if (source is null || node is null)
        {
            return;
        }

        int sourceIndex = source.Nodes.IndexOf(node);
        int requested = Math.Clamp(targetIndex ?? target.Nodes.Count, 0, target.Nodes.Count);
        int insertionIndex = requested;

        if (ReferenceEquals(source, target))
        {
            // targetIndex는 이동 전 목록을 기준으로 받는다. 앞의 항목을 제거하면 뒤쪽 위치가 하나 줄어든다.
            insertionIndex = requested > sourceIndex ? requested - 1 : requested;
            insertionIndex = Math.Clamp(insertionIndex, 0, source.Nodes.Count - 1);

            if (insertionIndex == sourceIndex)
            {
                return;
            }
        }

        Mutate(() =>
        {
            source.Nodes.Remove(node);
            int at = Math.Clamp(insertionIndex, 0, target.Nodes.Count);
            target.Nodes.Insert(at, node);
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

    // ── 줄에 얹는 대사 논리 ────────────────────────────────────────────────
    //
    // 줄 자체를 만들고 지우고 옮기는 명령은 여기 없다. 그것은 대본의 일이고
    // ProjectEditor.Scripts.cs에 있다. 여기서는 이미 있는 LineId에 논리를 붙인다.

    /// <summary>
    /// 이 줄에서 조건 흐름을 어떻게 바꿀지 정한다.
    /// 갈래를 더 이상 열지 않게 되면 거기 매달려 있던 조건 출구도 함께 사라진다.
    ///
    /// 대본에 없는 LineId를 주면 아무것도 하지 않는다. 조건은 존재하는 줄에만 붙는다.
    /// </summary>
    public void SetLineTransition(string nodeId, string lineId, LineConditionTransition? transition)
    {
        DialogueNode node = RequireDialogue(nodeId);

        if (Project.FindScript(node.ScriptId)?.FindLine(lineId) is not { IsRetired: false })
        {
            return;
        }

        DialogueLineExtension? existing = node.FindExtension(lineId);

        if (existing?.Transition is null && transition is null)
        {
            return;
        }

        Mutate(() =>
        {
            DialogueLineExtension extension = existing ?? new DialogueLineExtension(lineId);

            if (existing is null)
            {
                node.LineExtensions.Add(extension);
            }

            extension.Transition = transition;

            // 순서가 중요하다. 먼저 주인 없는 출구를 버리고, 그다음에 빈 항목을 버린다.
            // 반대로 하면 아직 출구가 매달려 있어 빈 항목이 남는다.
            PruneBranchExits(node);
            PruneLineExtensions(node);
        });
    }

    /// <summary>
    /// 이 줄에 도달했을 때 실행할 변수 변경 목록을 통째로 바꾼다. 목록 순서가 실행 순서다.
    /// 빈 목록이면 지운 것이다. 대본에 없는 LineId를 주면 아무것도 하지 않는다.
    /// </summary>
    public void SetLineSetOperations(
        string nodeId,
        string lineId,
        IReadOnlyList<SetOperation>? operations)
    {
        DialogueNode node = RequireDialogue(nodeId);

        if (Project.FindScript(node.ScriptId)?.FindLine(lineId) is not { IsRetired: false })
        {
            return;
        }

        DialogueLineExtension? existing = node.FindExtension(lineId);
        bool empty = operations is null || operations.Count == 0;

        if ((existing is null || existing.SetOperations.Count == 0) && empty)
        {
            return;
        }

        Mutate(() =>
        {
            DialogueLineExtension extension = existing ?? new DialogueLineExtension(lineId);

            if (existing is null)
            {
                node.LineExtensions.Add(extension);
            }

            extension.SetOperations.Clear();

            if (operations is not null)
            {
                // 호출자의 목록을 그대로 들고 있지 않는다. 밖에서 계속 고치는 목록을
                // 참조하면 되돌리기 스냅샷 밖에서 모델이 바뀐다.
                extension.SetOperations.AddRange(operations.Select(operation => operation.Clone()));
            }

            PruneLineExtensions(node);
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

        if (node is PresentationNode)
        {
            // PresentationNode는 실행 출구가 아니라 Presentation link만 가진다.
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

    // ── 설정 공급 연결 ──────────────────────────────────────────────────────

    /// <summary>
    /// SetNode가 DialogueNode에 조건과 assignment를 공급하는 Settings link를 만든다.
    /// 실행 순서는 바꾸지 않는다. 같은 소스와 대상의 링크가 이미 있으면 그것을 돌려준다.
    /// </summary>
    public NodeLink AddSettingsLink(string setNodeId, string dialogueNodeId)
    {
        if (Project.FindNode(setNodeId) is not SetNode)
        {
            throw new InvalidOperationException($"'{setNodeId}'는 설정 노드가 아닙니다.");
        }

        if (Project.FindNode(dialogueNodeId) is not DialogueNode)
        {
            throw new InvalidOperationException($"'{dialogueNodeId}'는 대사 노드가 아닙니다.");
        }

        NodeLink? existing = Project.Links.FirstOrDefault(link =>
            link.Kind == NodeLinkKind.Settings &&
            string.Equals(link.SourceNodeId, setNodeId, StringComparison.Ordinal) &&
            string.Equals(link.TargetNodeId, dialogueNodeId, StringComparison.Ordinal));

        if (existing is not null)
        {
            if (!existing.IsEnabled)
            {
                Mutate(ProjectChangeKind.Connections, () => existing.IsEnabled = true);
            }

            return existing;
        }

        int nextOrder = Project.Links
            .Where(link =>
                link.Kind == NodeLinkKind.Settings &&
                string.Equals(link.TargetNodeId, dialogueNodeId, StringComparison.Ordinal))
            .Select(link => link.Order)
            .DefaultIfEmpty(-1)
            .Max() + 1;

        var link = new NodeLink(
            kind: NodeLinkKind.Settings,
            sourceNodeId: setNodeId,
            targetNodeId: dialogueNodeId)
        {
            Order = nextOrder
        };

        Mutate(ProjectChangeKind.Connections, () => Project.Links.Add(link));
        return link;
    }

    public void RemoveLink(string linkId)
    {
        NodeLink? link = Project.FindLink(linkId);

        if (link is not null)
        {
            // Dialogue의 LineConditionTransition은 건드리지 않는다. 연결이 끊긴 뒤에는
            // ConditionFlowResolver가 사용할 수 없는 조건으로 표시한다.
            Mutate(ProjectChangeKind.Connections, () => Project.Links.Remove(link));
        }
    }

    public void SetLinkEnabled(string linkId, bool enabled)
    {
        NodeLink? link = Project.FindLink(linkId);

        if (link is not null && link.IsEnabled != enabled)
        {
            Mutate(ProjectChangeKind.Connections, () => link.IsEnabled = enabled);
        }
    }

    // ── Presentation command ────────────────────────────────────────────────

    public PresentationLineBinding AddPresentationBinding(
        string presentationNodeId,
        string lineId)
    {
        PresentationNode node = RequirePresentation(presentationNodeId);
        PresentationLineBinding? existing = node.Bindings.FirstOrDefault(binding =>
            string.Equals(binding.LineId, lineId, StringComparison.Ordinal));

        if (existing is not null)
        {
            return existing;
        }

        var binding = new PresentationLineBinding(lineId);
        Mutate(ProjectChangeKind.PresentationContent, () => node.Bindings.Add(binding));
        return binding;
    }

    public PresentationCommandInstance AddPresentationCommand(
        string presentationNodeId,
        string lineId,
        string definitionId,
        IReadOnlyDictionary<string, string>? arguments = null,
        string? note = null)
    {
        PresentationNode node = RequirePresentation(presentationNodeId);
        PresentationLineBinding? existingBinding = node.Bindings.FirstOrDefault(item =>
            string.Equals(item.LineId, lineId, StringComparison.Ordinal));
        PresentationLineBinding binding = existingBinding ?? new PresentationLineBinding(lineId);
        var command = new PresentationCommandInstance(definitionId: definitionId)
        {
            Note = note
        };

        if (arguments is not null)
        {
            foreach ((string key, string value) in arguments)
            {
                command.Arguments[key] = value;
            }
        }

        Mutate(ProjectChangeKind.PresentationContent, () =>
        {
            if (existingBinding is null)
            {
                node.Bindings.Add(binding);
            }

            binding.Commands.Add(command);
        });

        return command;
    }

    /// <summary>
    /// LineId 없는 노드 수준 Setup 커맨드를 목록 맨 뒤에 붙인다.
    /// 장면 준비(슬롯·캐스팅·배경 스폰·리셋)는 대사 줄이 아니라 노드에 속한다.
    /// </summary>
    public PresentationCommandInstance AddPresentationSetupCommand(
        string presentationNodeId,
        string definitionId,
        IReadOnlyDictionary<string, string>? arguments = null,
        string? note = null)
    {
        PresentationNode node = RequirePresentation(presentationNodeId);
        var command = new PresentationCommandInstance(definitionId: definitionId)
        {
            Note = note
        };

        if (arguments is not null)
        {
            foreach ((string key, string value) in arguments)
            {
                command.Arguments[key] = value;
            }
        }

        Mutate(ProjectChangeKind.PresentationContent, () => node.SetupCommands.Add(command));
        return command;
    }

    public void MovePresentationSetupCommand(
        string presentationNodeId,
        string commandId,
        int delta)
    {
        PresentationNode node = RequirePresentation(presentationNodeId);
        int from = node.SetupCommands.FindIndex(command =>
            string.Equals(command.Id, commandId, StringComparison.Ordinal));

        if (from < 0)
        {
            return;
        }

        int to = Math.Clamp(from + delta, 0, node.SetupCommands.Count - 1);

        if (to == from)
        {
            return;
        }

        Mutate(ProjectChangeKind.PresentationContent, () =>
        {
            PresentationCommandInstance command = node.SetupCommands[from];
            node.SetupCommands.RemoveAt(from);
            node.SetupCommands.Insert(to, command);
        });
    }

    public void MovePresentationCommand(
        string presentationNodeId,
        string commandId,
        int delta)
    {
        PresentationNode node = RequirePresentation(presentationNodeId);
        PresentationLineBinding? binding = node.Bindings.FirstOrDefault(item =>
            item.Commands.Any(command => string.Equals(command.Id, commandId, StringComparison.Ordinal)));

        if (binding is null)
        {
            return;
        }

        int from = binding.Commands.FindIndex(command =>
            string.Equals(command.Id, commandId, StringComparison.Ordinal));

        if (from < 0)
        {
            return;
        }

        int to = Math.Clamp(from + delta, 0, binding.Commands.Count - 1);

        if (from == to)
        {
            return;
        }

        Mutate(ProjectChangeKind.PresentationContent, () =>
        {
            PresentationCommandInstance command = binding.Commands[from];
            binding.Commands.RemoveAt(from);
            binding.Commands.Insert(to, command);
        });
    }

    public void RemovePresentationCommand(string presentationNodeId, string commandId)
    {
        PresentationNode node = RequirePresentation(presentationNodeId);

        if (node.SetupCommands.Any(command =>
                string.Equals(command.Id, commandId, StringComparison.Ordinal)))
        {
            Mutate(ProjectChangeKind.PresentationContent, () => node.SetupCommands.RemoveAll(
                command => string.Equals(command.Id, commandId, StringComparison.Ordinal)));
            return;
        }

        PresentationLineBinding? binding = node.Bindings.FirstOrDefault(item =>
            item.Commands.Any(command => string.Equals(command.Id, commandId, StringComparison.Ordinal)));

        if (binding is null)
        {
            return;
        }

        Mutate(ProjectChangeKind.PresentationContent, () => binding.Commands.RemoveAll(command =>
            string.Equals(command.Id, commandId, StringComparison.Ordinal)));
    }

    public void SetPresentationCommandEnabled(
        string presentationNodeId,
        string commandId,
        bool enabled)
    {
        PresentationNode node = RequirePresentation(presentationNodeId);
        PresentationCommandInstance? command = node.SetupCommands
            .Concat(node.Bindings.SelectMany(binding => binding.Commands))
            .FirstOrDefault(item => string.Equals(item.Id, commandId, StringComparison.Ordinal));

        if (command is not null && command.IsEnabled != enabled)
        {
            Mutate(ProjectChangeKind.PresentationContent, () => command.IsEnabled = enabled);
        }
    }

    public void SetPresentationCommandDefinition(
        string presentationNodeId,
        string commandId,
        string definitionId,
        IReadOnlyDictionary<string, string>? defaultArguments = null)
    {
        PresentationNode node = RequirePresentation(presentationNodeId);
        PresentationCommandInstance? command = node.SetupCommands
            .Concat(node.Bindings.SelectMany(binding => binding.Commands))
            .FirstOrDefault(item => string.Equals(item.Id, commandId, StringComparison.Ordinal));

        if (command is null)
        {
            return;
        }

        bool sameDefinition = string.Equals(command.DefinitionId, definitionId, StringComparison.Ordinal);
        bool sameArguments = defaultArguments is null ||
            (command.Arguments.Count == defaultArguments.Count &&
             command.Arguments.All(pair =>
                 defaultArguments.TryGetValue(pair.Key, out string? value) &&
                 string.Equals(value, pair.Value, StringComparison.Ordinal)));

        if (sameDefinition && sameArguments)
        {
            return;
        }

        Mutate(ProjectChangeKind.PresentationContent, () =>
        {
            command.DefinitionId = definitionId;

            if (defaultArguments is not null)
            {
                command.Arguments.Clear();
                foreach ((string key, string value) in defaultArguments)
                {
                    command.Arguments[key] = value;
                }
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
        SetNode? owner = Project.EnumerateNodes().OfType<SetNode>()
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

        _redo.Add(ProjectSnapshotCodec.Encode(Project));
        Restore(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Add(ProjectSnapshotCodec.Encode(Project));
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
        Project = ProjectSnapshotCodec.Decode(snapshot);
        Raise(ProjectChangeKind.Structure);
    }

    private void Mutate(Action change) => Mutate(ProjectChangeKind.Structure, change);

    private void Mutate(ProjectChangeKind kind, Action change)
    {
        _undo.Add(ProjectSnapshotCodec.Encode(Project));

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
    ///
    /// 대본에서 사라진 줄의 출구는 여기서 지우지 않는다. 그것은 <b>고아</b>이지 쓰레기가 아니다.
    /// 대본을 되돌리면 살아나야 하고, 그때까지는 진단으로 보인다.
    /// </summary>
    private static void PruneBranchExits(DialogueNode node)
    {
        HashSet<string> opening = node.LineExtensions
            .Where(extension => extension.Transition?.OpensBranch == true)
            .Select(extension => extension.LineId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string key in node.BranchExits.Keys.Where(key => !opening.Contains(key)).ToList())
        {
            node.BranchExits.Remove(key);
        }
    }

    /// <summary>아무것도 담지 않게 된 확장 항목을 버린다. 빈 항목은 파일만 시끄럽게 한다.</summary>
    private static void PruneLineExtensions(DialogueNode node)
    {
        node.LineExtensions.RemoveAll(extension =>
            extension.IsEmpty && !node.BranchExits.ContainsKey(extension.LineId));
    }

    private void RemoveReferencesToNode(string nodeId)
    {
        // 사라진 노드를 가리키던 출구를 남겨 두면 그래프에 끊어진 간선이 생긴다.
        Project.Links.RemoveAll(link =>
            string.Equals(link.SourceNodeId, nodeId, StringComparison.Ordinal) ||
            string.Equals(link.TargetNodeId, nodeId, StringComparison.Ordinal));

        foreach (StoryNode other in Project.EnumerateNodes())
        {
            if (string.Equals(other.DefaultExitTargetNodeId, nodeId, StringComparison.Ordinal))
            {
                other.DefaultExitTargetNodeId = null;
            }

            if (other is not DialogueNode dialogue)
            {
                continue;
            }

            foreach (string key in dialogue.BranchExits
                         .Where(pair => string.Equals(pair.Value, nodeId, StringComparison.Ordinal))
                         .Select(pair => pair.Key)
                         .ToList())
            {
                dialogue.BranchExits.Remove(key);
            }
        }
    }

    private StoryFile RequireFile(string fileId)
    {
        return Project.FindFile(fileId)
            ?? throw new InvalidOperationException($"StoryFile '{fileId}'를 찾을 수 없습니다.");
    }

    private DialogueNode RequireDialogue(string nodeId)
    {
        return Project.FindDialogue(nodeId)
            ?? throw new InvalidOperationException($"'{nodeId}'는 대사 노드가 아닙니다.");
    }

    private PresentationNode RequirePresentation(string nodeId)
    {
        return Project.FindNode(nodeId) as PresentationNode
            ?? throw new InvalidOperationException($"'{nodeId}'는 연출 노드가 아닙니다.");
    }

    private string NextFileName()
    {
        return $"파일 {Project.Files.Count + 1}";
    }

    private string NextName(string prefix)
    {
        int count = Project.EnumerateNodes().Count(node =>
            node.Name.StartsWith(prefix, StringComparison.Ordinal)) + 1;

        return $"{prefix} {count}";
    }
}
