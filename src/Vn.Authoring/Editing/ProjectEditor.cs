using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Script;
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

    /// <summary>
    /// 다른 프로젝트의 <c>.vnstory.json</c> 하나를 이 프로젝트의 파일(판)로 들여온다 (W51).
    ///
    /// 대사 노드가 참조하는 대본은 관례 위치(<c>../script/&lt;scriptId&gt;.vnscript.json</c>)에서
    /// 함께 들여오고, 못 찾으면 경고로 남긴다(조용히 버리지 않는다 — 규칙 14).
    /// Id가 이미 이 프로젝트에 있으면(같은 파일 재수입) 명확히 거부한다 — Id를 다시
    /// 발급하면 노드 간 연결·LineId 계약이 소리 없이 끊어진다.
    /// 다른 파일의 노드를 가리키던 실행 출구는 대상이 없으면 흐름 해석기가 알린다.
    /// </summary>
    public StoryFileImportOutcome ImportStoryFile(string storyJsonPath)
    {
        string fullPath = Path.GetFullPath(storyJsonPath);
        StoryFile imported = StoryFileJson.Read(File.ReadAllText(fullPath));

        if (Project.FindFile(imported.Id) is not null)
        {
            throw new InvalidOperationException(
                $"파일 '{imported.Name}'({imported.Id})은 이미 이 프로젝트에 있습니다 — 같은 파일을 두 번 가져올 수 없습니다.");
        }

        foreach (StoryNode node in imported.Nodes)
        {
            if (Project.FindNode(node.Id) is not null)
            {
                throw new InvalidOperationException(
                    $"노드 '{node.Name}'({node.Id})이 이미 이 프로젝트에 있습니다 — 같은 파일을 두 번 가져올 수 없습니다.");
            }
        }

        // 저장 시 이 프로젝트의 story/ 관례 자리로 다시 쓰인다 — 원본 파일은 건드리지 않는다.
        imported.RelativePath = ProjectStore.DefaultRelativePath(imported.Id);

        // 딸린 대본 — 원본 프로젝트의 관례 자리(story/의 형제 script/)에서 찾는다.
        string? sourceRoot = Path.GetDirectoryName(Path.GetDirectoryName(fullPath));
        var scripts = new List<ScriptDocument>();
        var warnings = new List<string>();

        foreach (DialogueNode dialogue in imported.Nodes.OfType<DialogueNode>())
        {
            if (dialogue.ScriptId is not { } scriptId || Project.FindScript(scriptId) is not null ||
                scripts.Any(script => string.Equals(script.Id, scriptId, StringComparison.Ordinal)))
            {
                continue;
            }

            string? scriptPath = sourceRoot is null
                ? null
                : Path.Combine(sourceRoot, ProjectStore.DefaultScriptPath(scriptId).Replace('/', Path.DirectorySeparatorChar));

            if (scriptPath is not null && File.Exists(scriptPath))
            {
                scripts.Add(ScriptDocumentJson.Read(File.ReadAllText(scriptPath)));
            }
            else
            {
                warnings.Add(
                    $"'{dialogue.Name}'의 대본({scriptId})을 찾지 못했습니다 — 원본 프로젝트의 script 폴더가 옆에 있어야 합니다.");
            }
        }

        Mutate(() =>
        {
            foreach (ScriptDocument script in scripts)
            {
                Project.Scripts.Add(script);
            }

            Project.Files.Add(imported);
        });

        return new StoryFileImportOutcome(imported, scripts.Count, warnings);
    }

    /// <summary>
    /// 시나리오 파일(판)을 통째로 제거한다 (W61). 안의 노드가 맺던 연결·출구는
    /// 노드 삭제와 같은 규칙 하나(<see cref="RemoveReferencesToNode"/>)로 정리된다.
    /// 대본·발행 결과는 지우지 않는다 — 발행본은 불변이고, 대본은 지우면 고아 연출의
    /// 사유를 물을 수 없게 된다. 한 번의 되돌리기로 통째로 복구된다.
    /// </summary>
    public void RemoveStoryFile(string fileId)
    {
        StoryFile? file = Project.FindFile(fileId);

        if (file is null)
        {
            return;
        }

        if (Project.Files.Count <= 1)
        {
            throw new InvalidOperationException(
                "마지막 시나리오 파일은 제거할 수 없습니다 — 새 노드가 갈 곳이 없어집니다.");
        }

        Mutate(() =>
        {
            Project.Files.Remove(file);

            foreach (StoryNode node in file.Nodes)
            {
                RemoveReferencesToNode(node.Id);
            }

            if (file.Nodes.Any(node =>
                string.Equals(Project.StartNodeId, node.Id, StringComparison.Ordinal)))
            {
                Project.StartNodeId = Project.EnumerateNodes()
                    .FirstOrDefault(candidate => candidate is not PresentationNode)
                    ?.Id;
            }
        });
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

    /// <summary>
    /// 대사 노드를 만든다. scriptId를 주지 않으면 <b>전용 대본을 함께 만들어 잇는다</b> (X4, D-3)
    /// — 노드는 생성 즉시 편집 대상이고, 첫 빈 줄까지 준비되어 바로 타이핑할 수 있다.
    /// 노드·대본·첫 줄이 한 번의 편집이라 되돌리기 한 번에 함께 사라진다.
    /// </summary>
    public DialogueNode AddDialogueNode(
        string fileId,
        double x = 0,
        double y = 0,
        string? name = null,
        string? scriptId = null)
    {
        if (scriptId is null)
        {
            StoryFile file = RequireFile(fileId);
            var created = new DialogueNode(name: name ?? NextName("장면"))
            {
                Layout = new NodeLayout { X = x, Y = y }
            };

            if (Project.FindNode(created.Id) is not null)
            {
                throw new InvalidOperationException($"노드 Id '{created.Id}'가 프로젝트 안에서 중복됩니다.");
            }

            var script = new ScriptDocument(name: $"{created.Name} 대본");
            var firstLine = new ScriptLine(_newLineId());
            created.ScriptId = script.Id;

            Mutate(() =>
            {
                script.Lines.Add(firstLine);
                script.RequireLocale(script.PrimaryLocale).Entries[firstLine.Id] = LocalizedLine.Empty;
                Project.Scripts.Add(script);
                file.Nodes.Add(created);
                Project.StartNodeId ??= created.Id;
            });

            return created;
        }

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

    public CommandSupplyNode AddCommandSupplyNode(
        string fileId,
        double x = 0,
        double y = 0,
        string? name = null)
    {
        var node = new CommandSupplyNode(name: name ?? NextName("연출 공급"))
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
    public void SetLineTransition(string nodeId, string lineId, LineConditionTransition? transition) =>
        SetLineTransitions(nodeId, lineId, transition is null ? [] : [transition]);

    /// <summary>
    /// 이 줄 앞의 전환들을 통째로 바꾼다 (2026-08-17) — 목록 순서가 곧 일어나는 순서다.
    /// 빈 목록이면 전환이 없는 줄이다. 대본에 없는 LineId를 주면 아무것도 하지 않는다.
    /// </summary>
    public void SetLineTransitions(
        string nodeId, string lineId, IReadOnlyList<LineConditionTransition> transitions)
    {
        ArgumentNullException.ThrowIfNull(transitions);

        DialogueNode node = RequireDialogue(nodeId);

        if (Project.FindScript(node.ScriptId)?.FindLine(lineId) is not { IsRetired: false })
        {
            return;
        }

        DialogueLineExtension? existing = node.FindExtension(lineId);

        if ((existing?.Transitions.Count ?? 0) == 0 && transitions.Count == 0)
        {
            return;
        }

        // 옵션 라벨 전환은 안정 Id를 가져야 한다 — 아래 루프가 그 자리에서 잇는다.
        var resolved = new List<LineConditionTransition>(transitions.Count);

        foreach (LineConditionTransition item in transitions)
        {
            resolved.Add(item is { OpensOption: true, OptionId: null }
                ? new LineConditionTransition(
                    item.Kind,
                    optionId: existing?.Transitions.FirstOrDefault(candidate => candidate.OpensOption)?.OptionId
                        ?? Identifier.Option())
                : item);
        }

        Mutate(() =>
        {
            DialogueLineExtension extension = existing ?? new DialogueLineExtension(lineId);

            if (existing is null)
            {
                node.LineExtensions.Add(extension);
            }

            extension.Transitions.Clear();
            extension.Transitions.AddRange(resolved);

            // 순서가 중요하다. 먼저 주인 없는 출구를 버리고, 그다음에 빈 항목을 버린다.
            // 반대로 하면 아직 출구가 매달려 있어 빈 항목이 남는다.
            PruneBranchExits(node);
            PruneLineExtensions(node);
        });
    }

    private void SetLineTransitionLegacy(string nodeId, string lineId, LineConditionTransition? transition)
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

        // 옵션 라벨 전환은 안정 Id를 가져야 한다. 이미 이 줄이 옵션이었다면 그 Id를 잇는다 —
        // 라벨 전환 종류를 바꿨다고 정체성이 바뀌면 순서 경고(C3)가 성립하지 않는다.
        if (transition is { OpensOption: true, OptionId: null })
        {
            transition = new LineConditionTransition(
                transition.Kind,
                optionId: existing?.Transition?.OptionId ?? Identifier.Option());
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
        SetExitTarget(port.NodeId, port.Kind, port.ExitKey, targetNodeId);
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

        if (node is not DialogueNode)
        {
            // 실행 출구는 DialogueNode만 가진다. SetNode·CommandSupplyNode는 공급자이고
            // PresentationNode는 결과 소비자다 — 어느 쪽도 실행 흐름을 정하지 않는다.
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

        // 선택지 출구(v9)는 문구가 열쇠다 — 대본의 줄에 매이지 않는다.
        Dictionary<string, string> exits =
            kind == ExitPortKind.Choice ? dialogue.ChoiceExits : dialogue.BranchExits;

        Mutate(() =>
        {
            if (targetNodeId is null)
            {
                exits.Remove(branchOpenLineId);
            }
            else
            {
                exits[branchOpenLineId] = targetNodeId;
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

    /// <summary>
    /// CommandSupplyNode가 PresentationNode에 커맨드 범주와 프리셋을 공급하는 link를 만든다.
    /// Settings link와 동형이다. 같은 소스와 대상의 링크가 이미 있으면 그것을 돌려준다.
    /// </summary>
    public NodeLink AddCommandSupplyLink(string supplyNodeId, string presentationNodeId)
    {
        if (Project.FindNode(supplyNodeId) is not CommandSupplyNode)
        {
            throw new InvalidOperationException($"'{supplyNodeId}'는 연출 공급 노드가 아닙니다.");
        }

        if (Project.FindNode(presentationNodeId) is not PresentationNode)
        {
            throw new InvalidOperationException($"'{presentationNodeId}'는 연출 노드가 아닙니다.");
        }

        NodeLink? existing = Project.Links.FirstOrDefault(link =>
            link.Kind == NodeLinkKind.CommandSupply &&
            string.Equals(link.SourceNodeId, supplyNodeId, StringComparison.Ordinal) &&
            string.Equals(link.TargetNodeId, presentationNodeId, StringComparison.Ordinal));

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
                link.Kind == NodeLinkKind.CommandSupply &&
                string.Equals(link.TargetNodeId, presentationNodeId, StringComparison.Ordinal))
            .Select(link => link.Order)
            .DefaultIfEmpty(-1)
            .Max() + 1;

        var link = new NodeLink(
            kind: NodeLinkKind.CommandSupply,
            sourceNodeId: supplyNodeId,
            targetNodeId: presentationNodeId)
        {
            Order = nextOrder
        };

        Mutate(ProjectChangeKind.Connections, () => Project.Links.Add(link));
        return link;
    }

    /// <summary>
    /// 이 연출 노드가 발행한 결과를 공급할 대사 노드를 정한다. null이면 연결을 끊는다.
    ///
    /// 짝은 한 쌍이다 — 연출 하나가 여러 대사에, 대사 하나가 여러 연출에 걸리면
    /// 내보내기가 어느 짝을 골라야 할지 답할 수 없으므로 양쪽의 기존 공급을 걷어낸다.
    /// </summary>
    public NodeLink? SetPresentationSupplyTarget(string presentationNodeId, string? dialogueNodeId)
    {
        if (Project.FindPresentation(presentationNodeId) is null)
        {
            throw new InvalidOperationException($"'{presentationNodeId}'는 연출 노드가 아닙니다.");
        }

        if (dialogueNodeId is not null && Project.FindDialogue(dialogueNodeId) is null)
        {
            throw new InvalidOperationException($"'{dialogueNodeId}'는 대사 노드가 아닙니다.");
        }

        NodeLink? existing = Project.Links.FirstOrDefault(link =>
            link.Kind == NodeLinkKind.PresentationSupply &&
            string.Equals(link.SourceNodeId, presentationNodeId, StringComparison.Ordinal));

        if (dialogueNodeId is null && existing is null)
        {
            return null;
        }

        if (existing is not null &&
            string.Equals(existing.TargetNodeId, dialogueNodeId, StringComparison.Ordinal))
        {
            if (!existing.IsEnabled)
            {
                Mutate(ProjectChangeKind.Connections, () => existing.IsEnabled = true);
            }

            return existing;
        }

        NodeLink? created = null;

        Mutate(ProjectChangeKind.Connections, () =>
        {
            Project.Links.RemoveAll(link =>
                link.Kind == NodeLinkKind.PresentationSupply &&
                (string.Equals(link.SourceNodeId, presentationNodeId, StringComparison.Ordinal) ||
                 (dialogueNodeId is not null &&
                  string.Equals(link.TargetNodeId, dialogueNodeId, StringComparison.Ordinal))));

            if (dialogueNodeId is not null)
            {
                created = new NodeLink(
                    kind: NodeLinkKind.PresentationSupply,
                    sourceNodeId: presentationNodeId,
                    targetNodeId: dialogueNodeId);
                Project.Links.Add(created);
            }
        });

        return created;
    }

    // ── 연출 공급 노드 ──────────────────────────────────────────────────────

    /// <summary>공급 범주 집합을 통째로 바꾼다. 어떤 묶음을 무엇이라 부를지는 데이터다.</summary>
    public void SetSupplyCategories(string supplyNodeId, IReadOnlyList<string> categoryIds)
    {
        CommandSupplyNode node = RequireSupply(supplyNodeId);

        if (node.Categories.SequenceEqual(categoryIds, StringComparer.Ordinal))
        {
            return;
        }

        Mutate(ProjectChangeKind.PresentationContent, () =>
        {
            node.Categories.Clear();
            node.Categories.AddRange(categoryIds);
        });
    }

    public CommandPreset AddCommandPreset(
        string supplyNodeId,
        string commandDefinitionId,
        string? displayName = null,
        IReadOnlyDictionary<string, string>? argumentValues = null,
        string? note = null)
    {
        CommandSupplyNode node = RequireSupply(supplyNodeId);
        var preset = new CommandPreset
        {
            DisplayName = displayName ?? string.Empty,
            CommandDefinitionId = commandDefinitionId,
            Note = note
        };

        if (argumentValues is not null)
        {
            foreach ((string key, string value) in argumentValues)
            {
                preset.ArgumentValues[key] = value;
            }
        }

        Mutate(ProjectChangeKind.PresentationContent, () => node.Presets.Add(preset));
        return preset;
    }

    public void UpdateCommandPreset(
        string supplyNodeId,
        string presetId,
        string? displayName = null,
        string? commandDefinitionId = null,
        IReadOnlyDictionary<string, string>? argumentValues = null,
        string? note = null)
    {
        CommandSupplyNode node = RequireSupply(supplyNodeId);

        if (node.FindPreset(presetId) is not { } preset)
        {
            return;
        }

        Mutate(ProjectChangeKind.PresentationContent, () =>
        {
            preset.DisplayName = displayName ?? preset.DisplayName;
            preset.CommandDefinitionId = commandDefinitionId ?? preset.CommandDefinitionId;
            preset.Note = note ?? preset.Note;

            if (argumentValues is not null)
            {
                preset.ArgumentValues.Clear();

                foreach ((string key, string value) in argumentValues)
                {
                    preset.ArgumentValues[key] = value;
                }
            }
        });
    }

    /// <summary>
    /// 프리셋을 지운다. 이 프리셋을 참조하던 연출 binding은 건드리지 않는다 —
    /// 발행 검증이 "프리셋을 찾을 수 없음"으로 알린다. 말없이 다른 값으로 갈아 끼우지 않는다.
    /// </summary>
    public void RemoveCommandPreset(string supplyNodeId, string presetId)
    {
        CommandSupplyNode node = RequireSupply(supplyNodeId);

        if (node.FindPreset(presetId) is { } preset)
        {
            Mutate(ProjectChangeKind.PresentationContent, () => node.Presets.Remove(preset));
        }
    }

    private CommandSupplyNode RequireSupply(string nodeId)
    {
        return Project.FindNode(nodeId) as CommandSupplyNode
            ?? throw new InvalidOperationException($"'{nodeId}'는 연출 공급 노드가 아닙니다.");
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
        string? note = null,
        string? presetId = null)
    {
        PresentationNode node = RequirePresentation(presentationNodeId);
        PresentationLineBinding? existingBinding = node.Bindings.FirstOrDefault(item =>
            string.Equals(item.LineId, lineId, StringComparison.Ordinal));
        PresentationLineBinding binding = existingBinding ?? new PresentationLineBinding(lineId);
        var command = new PresentationCommandInstance(definitionId: definitionId)
        {
            Note = note,
            PresetId = presetId
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
            RecordRecentCommand(definitionId);
        });

        return command;
    }

    /// <summary>
    /// 갤러리의 "최근" 섹션 재료. 추가와 같은 mutate 안에서 움직이므로 별도 undo 단계를
    /// 만들지 않고, 되돌리면 최근 목록도 함께 돌아간다.
    /// </summary>
    private void RecordRecentCommand(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
        {
            return;
        }

        Project.RecentCommandIds.RemoveAll(id => string.Equals(id, definitionId, StringComparison.Ordinal));
        Project.RecentCommandIds.Insert(0, definitionId);

        if (Project.RecentCommandIds.Count > StoryProject.MaxRecentCommands)
        {
            Project.RecentCommandIds.RemoveRange(
                StoryProject.MaxRecentCommands,
                Project.RecentCommandIds.Count - StoryProject.MaxRecentCommands);
        }
    }

    /// <summary>
    /// LineId 없는 노드 수준 Setup 커맨드를 목록 맨 뒤에 붙인다.
    /// 장면 준비(슬롯·캐스팅·배경 스폰·리셋)는 대사 줄이 아니라 노드에 속한다.
    /// </summary>
    public PresentationCommandInstance AddPresentationSetupCommand(
        string presentationNodeId,
        string definitionId,
        IReadOnlyDictionary<string, string>? arguments = null,
        string? note = null,
        string? presetId = null)
    {
        PresentationNode node = RequirePresentation(presentationNodeId);
        var command = new PresentationCommandInstance(definitionId: definitionId)
        {
            Note = note,
            PresetId = presetId
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
            node.SetupCommands.Add(command);
            RecordRecentCommand(definitionId);
        });
        return command;
    }

    /// <summary>
    /// 커맨드 여러 개를 한 라인에 <b>한 번의 편집으로</b> 붙인다. 캐스팅 시퀀스처럼
    /// 하나의 조작이 커맨드 여러 개가 되는 경우다 — 되돌리기 한 번에 전부 원복되어야
    /// "조작 하나 = 편집 하나"가 지켜진다. 커맨드는 매크로가 아니라 개별로 저장된다.
    /// </summary>
    public IReadOnlyList<PresentationCommandInstance> AddPresentationCommands(
        string presentationNodeId,
        string lineId,
        IReadOnlyList<(string DefinitionId, IReadOnlyDictionary<string, string> Arguments)> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        PresentationNode node = RequirePresentation(presentationNodeId);
        PresentationLineBinding? existingBinding = node.Bindings.FirstOrDefault(item =>
            string.Equals(item.LineId, lineId, StringComparison.Ordinal));
        PresentationLineBinding binding = existingBinding ?? new PresentationLineBinding(lineId);

        var created = new List<PresentationCommandInstance>(commands.Count);

        foreach ((string definitionId, IReadOnlyDictionary<string, string> arguments) in commands)
        {
            var command = new PresentationCommandInstance(definitionId: definitionId);

            foreach ((string key, string value) in arguments)
            {
                command.Arguments[key] = value;
            }

            created.Add(command);
        }

        Mutate(ProjectChangeKind.PresentationContent, () =>
        {
            if (existingBinding is null)
            {
                node.Bindings.Add(binding);
            }

            foreach (PresentationCommandInstance command in created)
            {
                binding.Commands.Add(command);
                RecordRecentCommand(command.DefinitionId);
            }
        });

        return created;
    }

    /// <summary>
    /// 커맨드의 인자 여러 개를 <b>한 번의 편집으로</b> 덮어쓴다(주지 않은 키는 유지).
    /// 직접 조작의 "같은 종류는 수정"이 이 통로를 지난다 — 인자마다 undo 단계가
    /// 쪼개지면 되돌리기 한 번으로 조작 하나가 원복되지 않는다.
    /// </summary>
    public void UpdatePresentationCommandArguments(
        string presentationNodeId,
        string commandId,
        IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        PresentationNode node = RequirePresentation(presentationNodeId);
        PresentationCommandInstance command = FindPresentationCommand(node, commandId)
            ?? throw new InvalidOperationException($"커맨드 '{commandId}'를 찾을 수 없습니다.");

        bool same = arguments.All(pair =>
            command.Arguments.TryGetValue(pair.Key, out string? current) &&
            string.Equals(current, pair.Value, StringComparison.Ordinal));

        if (same)
        {
            return;
        }

        Mutate(ProjectChangeKind.PresentationContent, () =>
        {
            foreach ((string key, string value) in arguments)
            {
                command.Arguments[key] = value;
            }
        });
    }

    /// <summary>
    /// 커맨드 인자 하나를 바꾼다. 빈 값은 인자를 지워 카탈로그 기본값으로 되돌린다.
    /// 칩 편집·직접 조작이 전부 이 통로를 지난다.
    /// </summary>
    public void SetPresentationCommandArgument(
        string presentationNodeId,
        string commandId,
        string argumentName,
        string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(argumentName);
        PresentationNode node = RequirePresentation(presentationNodeId);
        PresentationCommandInstance command = FindPresentationCommand(node, commandId)
            ?? throw new InvalidOperationException($"커맨드 '{commandId}'를 찾을 수 없습니다.");

        string? next = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        string? current = command.Arguments.TryGetValue(argumentName, out string? existing) ? existing : null;

        if (string.Equals(current, next, StringComparison.Ordinal))
        {
            return;
        }

        Mutate(ProjectChangeKind.PresentationContent, () =>
        {
            if (next is null)
            {
                command.Arguments.Remove(argumentName);
            }
            else
            {
                command.Arguments[argumentName] = next;
            }
        });
    }

    private static PresentationCommandInstance? FindPresentationCommand(
        PresentationNode node,
        string commandId)
    {
        return node.SetupCommands
            .Concat(node.Bindings.SelectMany(binding => binding.Commands))
            .FirstOrDefault(command => string.Equals(command.Id, commandId, StringComparison.Ordinal));
    }

    /// <summary>
    /// 라인 하나의 인라인 동기화 마커 목록을 통째로 바꾼다. 문자 오프셋 순으로 정렬해 둔다.
    /// 빈 목록이면 마커 없는 기존 동작이다.
    /// </summary>
    public void SetPresentationLineMarkers(
        string presentationNodeId,
        string lineId,
        IReadOnlyList<PresentationLineMarker>? markers)
    {
        PresentationNode node = RequirePresentation(presentationNodeId);
        PresentationLineBinding? existing = node.Bindings.FirstOrDefault(item =>
            string.Equals(item.LineId, lineId, StringComparison.Ordinal));
        bool empty = markers is null || markers.Count == 0;

        if ((existing is null || existing.Markers.Count == 0) && empty)
        {
            return;
        }

        Mutate(ProjectChangeKind.PresentationContent, () =>
        {
            PresentationLineBinding binding = existing ?? new PresentationLineBinding(lineId);

            if (existing is null)
            {
                node.Bindings.Add(binding);
            }

            binding.Markers.Clear();

            if (markers is not null)
            {
                binding.Markers.AddRange(markers
                    .OrderBy(marker => marker.CharacterOffset)
                    .ThenBy(marker => marker.FirstCommandIndex)
                    .Select(marker => marker.Clone()));
            }

            if (binding.Commands.Count == 0 && binding.Markers.Count == 0)
            {
                node.Bindings.Remove(binding);
            }
        });
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
        IReadOnlyDictionary<string, string>? defaultArguments = null,
        string? presetId = null)
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
        bool samePreset = string.Equals(command.PresetId, presetId, StringComparison.Ordinal);
        bool sameArguments = defaultArguments is null ||
            (command.Arguments.Count == defaultArguments.Count &&
             command.Arguments.All(pair =>
                 defaultArguments.TryGetValue(pair.Key, out string? value) &&
                 string.Equals(value, pair.Value, StringComparison.Ordinal)));

        if (sameDefinition && samePreset && sameArguments)
        {
            return;
        }

        Mutate(ProjectChangeKind.PresentationContent, () =>
        {
            command.DefinitionId = definitionId;
            command.PresetId = presetId;

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

    /// <summary>
    /// 그 판(챕터)의 <b>설정 노드</b>를 보장한다 (2026-08-17 소유자: "조건 노드라기보다는
    /// 챕터별로 자동으로 저장되는 컨트롤러에 가까운 것 같아. 챕터 하나에 저런 설정을 담아두는
    /// 노드가 상시로 뜨는거지").
    ///
    /// 공급 범위가 판 전체가 된 뒤로 설정노드는 <b>여러 개일 이유가 없다</b> — 어차피 다 같은
    /// 곳에 미치므로 나눠 놓으면 "어디에 적었더라"만 생긴다. 그래서 챕터당 하나가 자동으로
    /// 서고, 작가는 그 안을 채울 뿐 노드를 만들거나 지우지 않는다.
    /// </summary>
    /// <returns>그 판의 설정 노드. 이미 있으면 그대로 돌려준다(파일을 건드리지 않는다).</returns>
    public SetNode EnsureChapterSettingsNode(string fileId)
    {
        StoryFile file = RequireFile(fileId);

        // 챕터 조건 공급 노드(A계층 배관)는 세지 않는다 — 그건 기획자 자료를 나르는 자리다.
        SetNode? existing = file.Nodes.OfType<SetNode>()
            .FirstOrDefault(node => !Chapters.EpisodeSyncService.IsConditionSupplyNode(node, file));

        return existing ?? AddSetNode(fileId, name: ChapterSettingsNodeName(file.Name));
    }

    /// <summary>챕터 설정 노드의 이름 규약. 판 이름 = 챕터 Id다.</summary>
    public static string ChapterSettingsNodeName(string chapterId) => $"{chapterId} 설정";

    /// <summary>
    /// 작가가 더한 화자 목록을 통째로 정한다 (2026-08-17) — <b>`game.definition.json`에는
    /// 쓰지 않는다</b>. 정의 파일은 기획자 전용이고, 이건 프로젝트(작가 소유)에 산다.
    /// 이름이 빈 항목은 버린다 — 드롭다운에 빈 줄이 서면 고를 수도 없다.
    /// </summary>
    public void SetWriterSpeakers(IEnumerable<WriterSpeaker> speakers)
    {
        ArgumentNullException.ThrowIfNull(speakers);

        // 이름이 빈 줄도 그대로 둔다 (2026-08-17 소유자 보고 — "화자 추가를 눌렀는데
        // 아무 일도 안 일어나"). 여기서 걸러 내면 <b>방금 만든 빈 줄</b>이 첫 저장에
        // 휩쓸려 사라진다: 화면에 줄이 서고, 아무 칸이나 건드리는 순간 그 줄이 없어졌다.
        // 빈 줄은 "아직 안 쓴 자리"이지 잘못이 아니다 — 파일로 나갈 때만 턴다
        // (<see cref="Serialization.ProjectManifestJson"/>).
        List<WriterSpeaker> next = speakers
            .Select(speaker => speaker.Clone())
            .ToList();

        bool same = Project.WriterSpeakers.Count == next.Count;

        for (int index = 0; same && index < next.Count; index++)
        {
            same = string.Equals(Project.WriterSpeakers[index].Name, next[index].Name, StringComparison.Ordinal) &&
                   string.Equals(Project.WriterSpeakers[index].CharacterId, next[index].CharacterId,
                       StringComparison.Ordinal);
        }

        if (same)
        {
            return;
        }

        Mutate(() =>
        {
            Project.WriterSpeakers.Clear();
            Project.WriterSpeakers.AddRange(next);
        });
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
                string.Equals(current.Value, replacement.Value, StringComparison.Ordinal) &&
                string.Equals(current.Type, replacement.Type, StringComparison.Ordinal) &&
                Nullable.Equals(current.SliderMin, replacement.SliderMin) &&
                Nullable.Equals(current.SliderMax, replacement.SliderMax);
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

    // ── 프로젝트 설정 ──────────────────────────────────────────────────────

    /// <summary>
    /// 프리뷰 에셋 루트 두 곳을 바꾼다. null은 미설정이다.
    /// 프로젝트 이동 내성을 위해 상대 경로를 권장하지만 강제하지는 않는다 —
    /// 에셋이 다른 드라이브에 있으면 상대 경로가 존재하지 않는다.
    /// </summary>
    public void SetAssetRoots(
        string? backgroundsPath,
        string? portraitsPath,
        string? bgmPath = null,
        string? sfxPath = null)
    {
        string? backgrounds = AssetRootSettings.NormalizePath(backgroundsPath);
        string? portraits = AssetRootSettings.NormalizePath(portraitsPath);
        // 오디오 루트 (W59) — 넘기지 않으면(null) 기존 값을 유지한다: 배경/초상화만
        // 바꾸는 기존 호출(폴더 지정 UI)이 오디오 설정을 지우면 안 된다.
        string? bgm = AssetRootSettings.NormalizePath(bgmPath) ?? Project.AssetRoots.BgmPath;
        string? sfx = AssetRootSettings.NormalizePath(sfxPath) ?? Project.AssetRoots.SfxPath;

        if (string.Equals(Project.AssetRoots.BackgroundsPath, backgrounds, StringComparison.Ordinal) &&
            string.Equals(Project.AssetRoots.PortraitsPath, portraits, StringComparison.Ordinal) &&
            string.Equals(Project.AssetRoots.BgmPath, bgm, StringComparison.Ordinal) &&
            string.Equals(Project.AssetRoots.SfxPath, sfx, StringComparison.Ordinal))
        {
            return;
        }

        Mutate(ProjectChangeKind.Content, () =>
        {
            Project.AssetRoots.BackgroundsPath = backgrounds;
            Project.AssetRoots.PortraitsPath = portraits;
            Project.AssetRoots.BgmPath = bgm;
            Project.AssetRoots.SfxPath = sfx;
        });
    }

    /// <summary>라이브 출력 폴더를 바꾼다 (X12c). null은 라이브 출력 없음이다.</summary>
    public void SetOutputPath(string? outputPath)
    {
        string? next = AssetRootSettings.NormalizePath(outputPath);

        if (string.Equals(Project.OutputPath, next, StringComparison.Ordinal))
        {
            return;
        }

        Mutate(ProjectChangeKind.Content, () => Project.OutputPath = next);
    }

    /// <summary>내보내기 양식 선택을 바꾼다 (X13). 프로젝트에 저장되고 되돌릴 수 있다.</summary>
    public void SetExportFormats(ExportFormatSelection formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ExportFormatSelection current = Project.ExportFormats;

        if (current.YarnTrio == formats.YarnTrio &&
            current.ScriptCsv == formats.ScriptCsv &&
            current.ReviewCsv == formats.ReviewCsv &&
            current.DirectionCsv == formats.DirectionCsv)
        {
            return;
        }

        ExportFormatSelection next = formats.Clone();

        Mutate(ProjectChangeKind.Content, () =>
        {
            Project.ExportFormats.YarnTrio = next.YarnTrio;
            Project.ExportFormats.ScriptCsv = next.ScriptCsv;
            Project.ExportFormats.ReviewCsv = next.ReviewCsv;
            Project.ExportFormats.DirectionCsv = next.DirectionCsv;
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

            foreach (string key in dialogue.ChoiceExits
                         .Where(pair => string.Equals(pair.Value, nodeId, StringComparison.Ordinal))
                         .Select(pair => pair.Key)
                         .ToList())
            {
                dialogue.ChoiceExits.Remove(key);
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
