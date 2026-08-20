using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Vn.App.Services;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.App.Views;

/// <summary>
/// <b>발행된 대사 결과 하나</b>를 읽기 전용으로 투영하고 LineId별 연출 Command를 편집한다.
///
/// 입력은 편집 중인 DialogueNode가 아니다. 편집 중인 노드를 읽으면 연출가가 작업하는 동안
/// 발밑의 대사가 바뀌고, 완성한 연출표가 어느 대사에 맞는 것인지 아무도 말할 수 없게 된다.
/// 모든 변경은 이 노드의 binding에만 기록한다.
/// </summary>
public partial class PresentationNodeEditor : UserControl
{
    private static readonly SolidColorBrush SelectedLineBrush = new(Color.FromArgb(160, 37, 99, 235));
    private static readonly SolidColorBrush NormalLineBrush = new(Color.FromArgb(35, 128, 128, 128));

    private AuthoringSession? _session;
    private string? _nodeId;
    private bool _building;
    private AvailablePresentationCommands? _available;
    private string? _selectedLineId;
    private readonly Dictionary<string, Border> _lineCards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MiniStageState> _foldCache = new(StringComparer.Ordinal);

    /// <summary>MainWindow가 꽂아 주는 공유 하단 무대 프리뷰.</summary>
    internal MiniStagePreview? StagePreview { get; set; }

    public PresentationNodeEditor()
    {
        InitializeComponent();

        NameBox.LostFocus += (_, _) =>
        {
            if (!_building && _session is not null && _nodeId is not null)
            {
                _session.Editor.RenameNode(_nodeId, NameBox.Text ?? string.Empty);
            }
        };

        SourceCombo.SelectionChanged += (_, _) => OnSourceSelected();
        SupplyCombo.SelectionChanged += (_, _) => OnSupplySelected();
        PublishButton.Click += (_, _) => Publish();
    }

    internal void Attach(AuthoringSession session) => _session = session;

    internal string? NodeId => _nodeId;

    internal void Show(string? nodeId)
    {
        if (!string.Equals(_nodeId, nodeId, StringComparison.Ordinal))
        {
            _selectedLineId = null;
        }

        _nodeId = nodeId;
        Rebuild();
    }

    internal void Rebuild()
    {
        if (_session is null || _session.Project.FindPresentation(_nodeId) is not { } presentation)
        {
            LineHost.Children.Clear();
            _lineCards.Clear();
            StagePreview?.Show(null);
            return;
        }

        _building = true;

        try
        {
            NameBox.Text = presentation.Name;
            LineHost.Children.Clear();
            _lineCards.Clear();
            _foldCache.Clear();

            BuildSourcePicker(presentation);
            BuildSupplyPicker(presentation);

            PresentationWorkspace workspace = PresentationBindingResolver.Resolve(
                _session.Project,
                presentation);

            // 드롭다운 범위는 연결된 공급 노드가 정한다. 없으면 전체 카탈로그 폴백이다.
            AvailablePresentationCommands available = AvailablePresentationCommandResolver.Resolve(
                _session.Project,
                presentation.Id,
                _session.Definition);
            PresentationCommandCatalog catalog = available.Catalog;
            _available = available;

            // Setup은 어느 줄에도 속하지 않는 장면 준비다. 대사 결과가 없어도 편집할 수 있다.
            LineHost.Children.Add(BuildSetupSection(presentation, catalog));

            if (workspace.Dialogue is not { } dialogue)
            {
                TargetText.Text = presentation.Source is { } missing
                    ? $"입력으로 지정한 대사 결과 '{missing.Label}'을 찾을 수 없습니다."
                    : "읽을 대사 결과가 없습니다. 대사 노드에서 먼저 발행한 뒤 위에서 고르세요.";
            }
            else
            {
                TargetText.Text =
                    $"{dialogue.SourceNodeName} · {dialogue.Identity.Label} · {dialogue.Lines.Count}줄 · " +
                    $"{dialogue.Locale}" +
                    (workspace.IsStale ? " · 내용 해시 불일치" : string.Empty);

                if (_selectedLineId is null || dialogue.FindLine(_selectedLineId) is null)
                {
                    _selectedLineId = dialogue.Lines.FirstOrDefault()?.LineId;
                }

                foreach (DialogueResultLine line in dialogue.Lines)
                {
                    LineHost.Children.Add(BuildLineCard(presentation, line, catalog));
                }
            }

            IReadOnlyList<ResolvedPresentationBinding> orphaned = workspace.Orphans.ToArray();

            if (orphaned.Count > 0)
            {
                LineHost.Children.Add(new TextBlock
                {
                    Text = "이 결과에 붙지 않는 연출",
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 8, 0, 0)
                });

                foreach (ResolvedPresentationBinding orphan in orphaned)
                {
                    LineHost.Children.Add(BuildOrphanCard(orphan, catalog));
                }
            }

            BuildPublishState(presentation);
            RefreshStagePreview();
        }
        finally
        {
            _building = false;
        }
    }

    /// <summary>
    /// 지금 편집 중인(발행 전) 상태를 선택 라인까지 접어 하단 무대 프리뷰에 민다.
    /// 커맨드 행 컨트롤은 그대로 두고 프리뷰만 갱신할 때도 이 진입점 하나를 쓴다.
    /// </summary>
    internal void RefreshStagePreview()
    {
        if (StagePreview is null || _session is null)
        {
            return;
        }

        if (_session.Project.FindPresentation(_nodeId) is not { } presentation)
        {
            StagePreview.Show(null);
            return;
        }

        // 프리셋 해석은 발행 Freeze와 같은 길을 지난다 — 프리뷰용 두 번째 해석 규칙을 만들지 않는다.
        PresentationDraft draft = _session.Editor.InspectPresentationPublish(presentation.Id);
        PresentationCommandCatalog catalog = AvailablePresentationCommandResolver
            .Resolve(_session.Project, presentation.Id, _session.Definition)
            .Catalog;
        PresentationWorkspace workspace = PresentationBindingResolver.Resolve(_session.Project, presentation);

        if (workspace.Dialogue is not { } dialogue)
        {
            CoreStageFoldResult setupFold = CoreStageFold.Fold(
                catalog,
                draft.SetupCommands,
                Array.Empty<MiniStageFoldLine>(),
                _session.TuningLibrary.Tuning);

            StagePreview.Show(new MiniStagePreviewRequest(
                $"연출: {presentation.Name}",
                setupFold.State,
                HasPresentation: true,
                SelectedLineId: null,
                SpeakerName: null,
                LineText: null,
                Notice: "읽을 대사 결과가 없어 Setup만 반영합니다.",
                EditContext: new StageEditContext(
                    presentation.Id,
                    LineId: null,
                    DisabledReason: "라인이 없어 직접 조작할 수 없습니다. 대사 결과를 먼저 고르세요."),
                CoreState: setupFold.CoreState));
            return;
        }

        DialogueResultLine? line = dialogue.FindLine(_selectedLineId) ?? dialogue.Lines.FirstOrDefault();

        // 조건 값 시뮬 (W36-b): 수동 선택 + 값 기반 자동 판정을 합친 유효 선택을 쓴다.
        ConditionSimulation.Result simulation = SimulateBranches(dialogue);

        // 갈래 인식 (W35) — 유효 선택된 갈래의 라인만 접는다. 미결정 블록은 근사 + 표시.
        BranchAwareLines.Result branch = BranchAwareLines.UpTo(
            dialogue, draft.Bindings, line?.LineId, simulation.Effective);

        PresentationResultBinding? lineBinding = draft.Bindings.FirstOrDefault(item =>
            string.Equals(item.LineId, line?.LineId, StringComparison.Ordinal));

        CoreStageFoldResult fold = CoreStageFold.Fold(
            catalog,
            draft.SetupCommands,
            branch.FoldLines,
            _session.TuningLibrary.Tuning);
        MiniStageState state = fold.State;

        int index = line is null
            ? -1
            : dialogue.Lines.ToList().FindIndex(item =>
                string.Equals(item.LineId, line.LineId, StringComparison.Ordinal));

        // 스탯 HUD (X3): (시뮬 시작값 반영) 발행 시점 설정값 + 유효 갈래 기준의 set 누적.
        IReadOnlyList<StatFold.StatValue> stats = StatFold.Fold(
            dialogue.Assignments.Select(assignment => (
                assignment.Variable,
                _session.SimulationValues.TryGetValue(assignment.Variable, out string? overridden)
                    ? overridden
                    : assignment.Value)),
            branch.TakenLines
                .SelectMany(item => item.Sets.Select(operation =>
                    (operation.Variable, operation.Operator, operation.Value)))
                .ToList());

        // 선택 라인이 옵션 라벨이면 그 블록의 버튼 묶음이 대사창을 대신한다 —
        // 작업 대본 쪽 프리뷰와 같은 판정 하나(ChoiceOptionBundle)를 지난다.
        IReadOnlyList<StageChoiceOption>? choices = StageChoiceOptions.Build(
            ChoiceOptionBundle.At(
                dialogue.Lines,
                index,
                resultLine => resultLine.Transition?.Kind,
                resultLine => resultLine.Text),
            optionIndex => dialogue.Lines[optionIndex].LineId,
            branch.Blocks);

        StagePreview.Show(new MiniStagePreviewRequest(
            $"연출: {presentation.Name}",
            state,
            HasPresentation: true,
            line?.LineId,
            line?.CharacterName,
            line?.Text,
            LineIndex: index,
            LineCount: dialogue.Lines.Count,
            // 작업 중 바인딩이므로 직접 조작이 열려 있다 — 조작은 이 라인의 편집이 된다.
            EditContext: new StageEditContext(presentation.Id, line?.LineId),
            Stats: stats,
            ChoiceOptions: choices,
            CoreState: fold.CoreState,
            BranchBlocks: branch.Blocks,
            // 전이(W33): 이 라인으로 넘어가는 시간 = 라인 커맨드 duration의 최댓값.
            TransitionSeconds: StageTransitions.SecondsFor(catalog, lineBinding?.Commands),
            // 소리 표시(W34-b): 정지 프레임에 없는 오디오를 ♪ 칩으로.
            AudioCues: StageAudioCues.Of(catalog, lineBinding?.Commands),
            AutoBranchBlocks: simulation.AutoBlocks.ToArray(),
            // 소리 실재생(W62): 재생이 이 라인에 도달하면 칩의 원본 커맨드가 울린다.
            AudioCommands: StageAudioCues.AudioOf(catalog, lineBinding?.Commands),
            // 이동 편집(W66): 모션 선언이 있는 커맨드만 무대에서 수치를 만질 수 있다.
            MotionCues: StageMotionCues.Of(
                catalog,
                draft.SetupCommands,
                branch.FoldLines,
                lineBinding?.Commands,
                _session.TuningLibrary.Tuning)));
    }

    /// <summary>
    /// 조건 값 시뮬 (W36-b) — 수동 선택 위에 "값이 이렇다면 이 갈래"의 자동 판정을 얹는다.
    /// 시작값 = 발행 시점 설정값 + 세션 시뮬 오버라이드. 프리뷰와 재생 경로가 같은 판정을 쓴다.
    /// </summary>
    private ConditionSimulation.Result SimulateBranches(DialogueResult dialogue)
    {
        return ConditionSimulation.Decide(
            dialogue.Lines,
            resultLine => resultLine.Transition?.Kind,
            resultLine => resultLine.LineId,
            resultLine => resultLine.Transition?.Expression,
            resultLine => resultLine.Sets.Select(operation =>
                (operation.Variable, operation.Operator, operation.Value)),
            dialogue.Assignments.Select(assignment => (
                assignment.Variable,
                _session!.SimulationValues.TryGetValue(assignment.Variable, out string? overridden)
                    ? overridden
                    : assignment.Value)),
            _session!.BranchSelection);
    }

    /// <summary>프리뷰 창의 이전/다음. 선택은 이 편집기의 것 하나뿐이다.
    /// 재생 중에는 타는 경로만 걷는다 (W39) — 안 타는 갈래 라인은 건너뛰고,
    /// 경로 끝에서는 실행 출구를 따라 다음 노드로 넘어간다.</summary>
    internal void MoveStageLine(int delta)
    {
        if (_session?.Project.FindPresentation(_nodeId) is not { } presentation)
        {
            return;
        }

        PresentationWorkspace workspace = PresentationBindingResolver.Resolve(_session.Project, presentation);

        if (workspace.Dialogue is not { } dialogue || dialogue.Lines.Count == 0)
        {
            return;
        }

        if (StagePreview?.Playback.IsPlaying == true && TryMoveAlongPath(dialogue, delta))
        {
            return;
        }

        int index = dialogue.Lines.ToList().FindIndex(item =>
            string.Equals(item.LineId, _selectedLineId, StringComparison.Ordinal));
        int next = Math.Clamp((index < 0 ? 0 : index) + delta, 0, dialogue.Lines.Count - 1);

        SelectStageLine(dialogue.Lines[next].LineId);
    }

    /// <summary>재생 경로 이동 (W39). 현재 라인이 경로 밖(출구 뒤 등)이면 false —
    /// 문서 순서 이동으로 계속해 조용히 버리지 않는다.</summary>
    private bool TryMoveAlongPath(DialogueResult dialogue, int delta)
    {
        PlaybackPath.Result path = TracePlaybackPath(dialogue);
        int at = path.LineIds.ToList().FindIndex(id =>
            string.Equals(id, _selectedLineId, StringComparison.Ordinal));

        if (at < 0)
        {
            return false;
        }

        int next = at + delta;

        if (next >= path.LineIds.Count)
        {
            if (!TryEnterNextNode(path.ExitTargetNodeId))
            {
                StagePreview!.Playback.StopAtEnd();
            }

            return true;
        }

        SelectStageLine(path.LineIds[Math.Max(next, 0)]);
        return true;
    }

    /// <summary>이 발행본의 재생 경로 — 여기서 보는 것이 발행 결과이므로 출구도 발행본의 것이다.</summary>
    private PlaybackPath.Result TracePlaybackPath(DialogueResult dialogue)
    {
        return PlaybackPath.Trace(
            dialogue.Lines,
            line => line.Transition?.Kind,
            line => line.LineId,
            line => line.BranchExitTargetNodeId,
            dialogue.DefaultExitTargetNodeId,
            SimulateBranches(dialogue).Effective,
            _selectedLineId);
    }

    /// <summary>문서 끝 도달 (W39) — 실행 출구를 따라 다음 노드로 전환하면 true.</summary>
    internal bool TryExitPlaybackNode()
    {
        if (_session?.Project.FindPresentation(_nodeId) is not { } presentation)
        {
            return false;
        }

        PresentationWorkspace workspace = PresentationBindingResolver.Resolve(_session.Project, presentation);

        if (workspace.Dialogue is not { } dialogue)
        {
            return false;
        }

        return TryEnterNextNode(TracePlaybackPath(dialogue).ExitTargetNodeId);
    }

    /// <summary>
    /// 실행 출구의 대사 노드로 재생을 넘긴다 (W39). 노드 선택이 바뀌면 대사 편집기가
    /// 그 노드를 열고 첫 라인 프리뷰를 밀어 재생이 이어진다. 대사 노드가 아니면 알리고 멈춘다.
    /// </summary>
    private bool TryEnterNextNode(string? targetNodeId)
    {
        if (_session is null || StagePreview is null || targetNodeId is null)
        {
            return false;
        }

        if (_session.Project.FindNode(targetNodeId) is not DialogueNode next)
        {
            _session.SetStatus("실행 출구가 가리키는 대사 노드를 찾지 못해 이어 재생을 멈췄습니다.");
            return false;
        }

        StagePreview.Playback.OnNodeSwitch();
        _session.Select(next.Id);
        return true;
    }

    private void SelectStageLine(string lineId)
    {
        if (string.Equals(_selectedLineId, lineId, StringComparison.Ordinal))
        {
            return;
        }

        _selectedLineId = lineId;

        foreach ((string cardLineId, Border card) in _lineCards)
        {
            card.BorderBrush = string.Equals(cardLineId, lineId, StringComparison.Ordinal)
                ? SelectedLineBrush
                : NormalLineBrush;
        }

        RefreshStagePreview();
    }

    /// <summary>
    /// 읽을 수 있는 대사 결과 목록. <b>버전을 하나하나 고르게 한다.</b>
    /// "최신"이라는 선택지를 두면 다음 발행 때 연출가 모르게 대사가 바뀐다.
    /// </summary>
    private void BuildSourcePicker(PresentationNode presentation)
    {
        List<DialogueResult> results = _session!.Project.Results.DialogueResults
            .OrderBy(result => result.SourceNodeName, StringComparer.Ordinal)
            .ThenBy(result => result.Identity.Version)
            .ToList();

        SourceCombo.ItemsSource = results
            .Select(result => $"{result.SourceNodeName} · v{result.Identity.Version} · {result.Lines.Count}줄")
            .ToList();
        SourceCombo.Tag = results;
        SourceCombo.SelectedIndex = presentation.Source is { } source
            ? results.FindIndex(result =>
                string.Equals(result.Identity.ResultId, source.ResultId, StringComparison.Ordinal) &&
                result.Identity.Version == source.Version)
            : -1;
        SourceCombo.IsEnabled = results.Count > 0;
        SourceCombo.PlaceholderText = results.Count == 0
            ? "발행된 대사 결과가 없습니다"
            : "발행된 대사 결과 선택";
    }

    /// <summary>
    /// 발행한 연출 결과를 어느 대사 노드에 공급할지. 내보내기는 이 연결로 짝을 찾는다.
    /// 첫 항목 "(공급 안 함)"이 연결 해제다.
    /// </summary>
    private void BuildSupplyPicker(PresentationNode presentation)
    {
        List<DialogueNode> dialogues = _session!.Project.EnumerateNodes()
            .OfType<DialogueNode>()
            .ToList();

        var labels = new List<string> { "(공급 안 함)" };
        labels.AddRange(dialogues.Select(dialogue => dialogue.Name));

        NodeLink? current = _session.Project.Links.FirstOrDefault(link =>
            link.Kind == NodeLinkKind.PresentationSupply &&
            link.IsEnabled &&
            string.Equals(link.SourceNodeId, presentation.Id, StringComparison.Ordinal));

        SupplyCombo.ItemsSource = labels;
        SupplyCombo.Tag = dialogues;
        SupplyCombo.SelectedIndex = current is null
            ? 0
            : dialogues.FindIndex(dialogue =>
                  string.Equals(dialogue.Id, current.TargetNodeId, StringComparison.Ordinal)) + 1;
        SupplyCombo.IsEnabled = dialogues.Count > 0;
    }

    private void OnSupplySelected()
    {
        if (_building ||
            _session is null ||
            _nodeId is null ||
            SupplyCombo.Tag is not List<DialogueNode> dialogues ||
            SupplyCombo.SelectedIndex < 0)
        {
            return;
        }

        try
        {
            _session.Editor.SetPresentationSupplyTarget(
                _nodeId,
                SupplyCombo.SelectedIndex == 0
                    ? null
                    : dialogues[SupplyCombo.SelectedIndex - 1].Id);
        }
        catch (InvalidOperationException exception)
        {
            _session.SetStatus(exception.Message);
        }
    }

    private void OnSourceSelected()
    {
        if (_building ||
            _session is null ||
            _nodeId is null ||
            SourceCombo.Tag is not List<DialogueResult> results ||
            SourceCombo.SelectedIndex < 0 ||
            SourceCombo.SelectedIndex >= results.Count)
        {
            return;
        }

        DialogueResult picked = results[SourceCombo.SelectedIndex];

        try
        {
            _session.Editor.SetPresentationSource(
                _nodeId,
                picked.Identity.ResultId,
                picked.Identity.Version);
        }
        catch (InvalidOperationException exception)
        {
            _session.SetStatus(exception.Message);
        }
    }

    private void BuildPublishState(PresentationNode presentation)
    {
        PresentationDraft draft = _session!.Editor.InspectPresentationPublish(presentation.Id);
        PublishButton.IsEnabled = draft.CanPublish;

        PresentationResult? latest = _session.Project.Results
            .PresentationResultsOf(presentation.Id)
            .LastOrDefault();

        PublishStatusText.Text = draft.CanPublish
            ? latest is null
                ? "아직 발행하지 않았습니다."
                : $"최신 발행: {latest.Identity.Label} · 대사 {latest.Source.Label}"
            : draft.BlockingSummary();
    }

    private void Publish()
    {
        if (_session is null || _nodeId is null)
        {
            return;
        }

        try
        {
            PublishOutcome<PresentationResult> outcome = _session.Editor.PublishPresentation(_nodeId);

            _session.SetStatus(outcome.Created
                ? $"{outcome.Result.Identity.Label}을 발행했습니다. 대사 {outcome.Result.Source.Label} 기준입니다."
                : $"내용이 같아 {outcome.Result.Identity.Label}을 그대로 사용합니다.");
        }
        catch (PublishRejectedException exception)
        {
            _session.SetStatus(exception.Message.Replace(Environment.NewLine, " ", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// LineId 없는 노드 수준 Setup 커맨드(슬롯·캐스팅·배경 스폰·리셋).
    /// 이미터에서 Set_ 노드 본문이 된다. 목록 순서가 곧 실행·출력 순서다.
    /// </summary>
    private Control BuildSetupSection(PresentationNode presentation, PresentationCommandCatalog catalog)
    {
        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(new TextBlock
        {
            Text = "Setup — 장면 준비 (Set 노드 본문)",
            FontWeight = FontWeight.SemiBold
        });

        foreach (PresentationCommandInstance command in presentation.SetupCommands)
        {
            content.Children.Add(BuildCommandRow(presentation, lineId: null, command, catalog));
        }

        content.Children.Add(BuildAddRow(presentation, lineId: null, catalog));

        return new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(7),
            BorderBrush = new SolidColorBrush(Color.FromArgb(50, 37, 99, 235)),
            BorderThickness = new Thickness(1),
            Child = content
        };
    }

    private Button SetupButton(string glyph, Action action)
    {
        var button = new Button
        {
            Content = glyph,
            FontSize = 10,
            Padding = new Thickness(6, 2),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top
        };

        button.Click += (_, _) =>
        {
            if (!_building && _session is not null)
            {
                action();
            }
        };

        return button;
    }

    private Control BuildLineCard(
        PresentationNode presentation,
        DialogueResultLine line,
        PresentationCommandCatalog catalog)
    {
        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(new TextBlock
        {
            Text = $"{line.Index + 1}. {line.LineId}",
            FontSize = 11,
            Opacity = 0.6
        });
        content.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(line.CharacterName)
                ? line.Text
                : $"{line.CharacterName}: {line.Text}",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeight.SemiBold
        });

        PresentationLineBinding? binding = presentation.FindBinding(line.LineId);

        foreach (PresentationCommandInstance command in binding?.Commands ?? Enumerable.Empty<PresentationCommandInstance>())
        {
            content.Children.Add(BuildCommandRow(presentation, line.LineId, command, catalog));
        }

        content.Children.Add(BuildAddRow(presentation, line.LineId, catalog));

        var card = new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(7),
            BorderBrush = string.Equals(line.LineId, _selectedLineId, StringComparison.Ordinal)
                ? SelectedLineBrush
                : NormalLineBrush,
            BorderThickness = new Thickness(1),
            Child = content
        };

        // 카드 어디를 만져도(내부 콤보 포함) 그 라인이 무대 프리뷰의 기준이 된다.
        card.AddHandler(
            PointerPressedEvent,
            (_, _) => SelectStageLine(line.LineId),
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        _lineCards[line.LineId] = card;

        return card;
    }

    // ── 커맨드 행 — 갤러리·칩·텍스트 입력 (W19) ───────────────────────────

    private static readonly FontFamily MonoFont = new("Consolas,Cascadia Mono,monospace");

    /// <summary>
    /// 커맨드 하나의 편집 행. 이름, 파라미터 칩, 그리고 항상 병기되는
    /// <c>&lt;&lt;…&gt;&gt;</c> 텍스트 — 어떤 방식으로 만들었든 결과는 텍스트로 보인다.
    /// 카탈로그 notes(함정)는 ⚠ 툴팁으로 그 자리에서 보인다.
    /// </summary>
    private Control BuildCommandRow(
        PresentationNode presentation,
        string? lineId,
        PresentationCommandInstance command,
        PresentationCommandCatalog catalog)
    {
        PresentationCommandDefinition? definition = catalog.Find(command.DefinitionId);

        var enabled = new CheckBox
        {
            IsChecked = command.IsEnabled,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(0)
        };

        enabled.IsCheckedChanged += (_, _) =>
        {
            if (!_building && _session is not null)
            {
                _session.Editor.SetPresentationCommandEnabled(
                    presentation.Id, command.Id, enabled.IsChecked == true);
            }
        };

        var body = new StackPanel { Spacing = 3 };

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        titleRow.Children.Add(new TextBlock
        {
            Text = definition?.DisplayName ?? command.DefinitionId,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (command.PresetId is { } presetId)
        {
            titleRow.Children.Add(new TextBlock
            {
                Text = $"★ {_available?.FindPreset(presetId)?.DisplayName ?? presetId}",
                FontSize = 10,
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        if (!string.IsNullOrWhiteSpace(definition?.Notes))
        {
            // 함정 노트 인라인 표출 — 이미 있는 데이터를 화면으로.
            var warning = new TextBlock
            {
                Text = "⚠",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(217, 119, 6)),
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(warning, definition.Notes);
            titleRow.Children.Add(warning);
        }

        body.Children.Add(titleRow);

        // 항상 병기되는 텍스트. 칩으로 값이 바뀌면 이 줄도 그 자리에서 따라온다.
        var commandTextBlock = new TextBlock
        {
            FontFamily = MonoFont,
            FontSize = 10,
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap
        };

        void RefreshCommandText() => commandTextBlock.Text =
            CommandText.Format(definition, command.DefinitionId, EffectiveArguments(command));

        if (definition is not null && definition.Parameters.Count > 0)
        {
            var chips = new WrapPanel { Orientation = Orientation.Horizontal };

            foreach (PresentationCommandParameter parameter in definition.Parameters)
            {
                chips.Children.Add(BuildArgumentChip(
                    presentation, lineId, command, parameter, RefreshCommandText));
            }

            body.Children.Add(chips);
        }

        RefreshCommandText();
        body.Children.Add(commandTextBlock);

        Button up = SetupButton("▲", () => MoveCommand(presentation, lineId, command, -1));
        Button down = SetupButton("▼", () => MoveCommand(presentation, lineId, command, 1));
        Button remove = SetupButton("✕", () =>
        {
            _session!.Editor.RemovePresentationCommand(presentation.Id, command.Id);
            Rebuild();
        });

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto") };
        enabled.Margin = new Thickness(0, 2, 6, 0);
        Grid.SetColumn(enabled, 0);
        Grid.SetColumn(body, 1);
        Grid.SetColumn(up, 2);
        Grid.SetColumn(down, 3);
        Grid.SetColumn(remove, 4);
        row.Children.Add(enabled);
        row.Children.Add(body);
        row.Children.Add(up);
        row.Children.Add(down);
        row.Children.Add(remove);
        return row;
    }

    private void MoveCommand(
        PresentationNode presentation,
        string? lineId,
        PresentationCommandInstance command,
        int delta)
    {
        if (lineId is null)
        {
            _session!.Editor.MovePresentationSetupCommand(presentation.Id, command.Id, delta);
        }
        else
        {
            _session!.Editor.MovePresentationCommand(presentation.Id, command.Id, delta);
        }

        Rebuild();
    }

    /// <summary>프리셋이 공급한 값 위에 인스턴스 인자가 덮인 유효 인자 — 발행 Freeze와 같은 방향.</summary>
    private IReadOnlyDictionary<string, string> EffectiveArguments(PresentationCommandInstance command)
    {
        var arguments = new Dictionary<string, string>(StringComparer.Ordinal);

        if (command.PresetId is { } presetId &&
            _available?.FindPreset(presetId) is { } preset)
        {
            foreach ((string key, string value) in preset.Preset.ArgumentValues)
            {
                arguments[key] = value;
            }
        }

        foreach ((string key, string value) in command.Arguments)
        {
            arguments[key] = value;
        }

        return arguments;
    }

    /// <summary>
    /// 파라미터 하나의 칩. 클릭하면 후보 목록 + 직접 입력이 열린다.
    /// 대상(slot/alias) 타입의 후보는 <b>그 라인까지 접은 무대 상태</b>에서 나오고,
    /// duration·direction 등 토큰 타입은 카탈로그 type 기준 후보다. 후보는 제약이 아니라
    /// 제안이다 — 직접 입력은 언제나 허용된다.
    /// </summary>
    private Control BuildArgumentChip(
        PresentationNode presentation,
        string? lineId,
        PresentationCommandInstance command,
        PresentationCommandParameter parameter,
        Action refreshCommandText)
    {
        var chip = new Button
        {
            FontSize = 10,
            Padding = new Thickness(6, 2),
            Margin = new Thickness(0, 0, 4, 3)
        };

        void RefreshChip()
        {
            string? value = EffectiveArguments(command).TryGetValue(parameter.Name, out string? current)
                ? current
                : null;
            chip.Content = value is null
                ? $"{parameter.Name}: ({parameter.Default ?? "미지정"})"
                : $"{parameter.Name}: {value}";
            chip.Opacity = value is null ? 0.65 : 1.0;
        }

        RefreshChip();

        chip.Click += (_, _) =>
        {
            var panel = new StackPanel { Spacing = 4, MinWidth = 180 };
            var flyout = new Flyout { Content = panel, Placement = PlacementMode.Bottom };

            void Commit(string? value)
            {
                _session!.Editor.SetPresentationCommandArgument(
                    presentation.Id, command.Id, parameter.Name, value);
                RefreshChip();
                refreshCommandText();
                RefreshStagePreview();
                flyout.Hide();
            }

            panel.Children.Add(new TextBlock
            {
                Text = $"{parameter.Name} ({parameter.Type})" + (parameter.Required ? " · 필수" : string.Empty),
                FontSize = 10,
                Opacity = 0.65
            });

            IEnumerable<string> candidates = ArgumentTokenCandidates.IsStageTargetType(parameter.Type)
                ? StageTargetCandidates(lineId)
                : ArgumentTokenCandidates.For(parameter.Type);

            foreach (string candidate in candidates)
            {
                var candidateButton = new Button
                {
                    Content = candidate,
                    FontSize = 11,
                    Padding = new Thickness(8, 2),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                string value = candidate.Split(' ')[0]; // "c1 (laru)" 표기에서 키만
                candidateButton.Click += (_, _) => Commit(value);
                panel.Children.Add(candidateButton);
            }

            var input = new TextBox
            {
                PlaceholderText = "직접 입력",
                FontSize = 11,
                Text = command.Arguments.TryGetValue(parameter.Name, out string? existing) ? existing : string.Empty
            };
            input.KeyDown += (_, args) =>
            {
                if (args.Key == Avalonia.Input.Key.Enter)
                {
                    Commit(input.Text);
                }
            };
            panel.Children.Add(input);

            if (parameter.Default is not null || !parameter.Required)
            {
                var reset = new Button
                {
                    Content = parameter.Default is null ? "지우기" : $"기본값 ({parameter.Default})",
                    FontSize = 10,
                    Padding = new Thickness(8, 2),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                reset.Click += (_, _) => Commit(null);
                panel.Children.Add(reset);
            }

            flyout.ShowAt(chip);
        };

        return chip;
    }

    /// <summary>그 라인까지 접은 무대의 대상 후보 — 별칭 먼저, 그다음 슬롯(캐스팅 병기).</summary>
    private IReadOnlyList<string> StageTargetCandidates(string? lineId)
    {
        MiniStageState state = FoldStateAt(lineId);
        var candidates = new List<string>();

        foreach ((string alias, string slotKey) in state.Aliases.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            candidates.Add($"{alias} ({slotKey})");
        }

        foreach ((string slotKey, MiniStageSlot slot) in state.Slots.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            candidates.Add(slot.CharacterId is null ? slotKey : $"{slotKey} ({slot.CharacterId})");
        }

        return candidates;
    }

    /// <summary>칩 후보용 폴드 상태. 라인별로 게으르게 접고 Rebuild마다 비운다.</summary>
    private MiniStageState FoldStateAt(string? lineId)
    {
        string key = lineId ?? string.Empty;

        if (_foldCache.TryGetValue(key, out MiniStageState? cached))
        {
            return cached;
        }

        MiniStageState state = MiniStageState.Empty;

        if (_session?.Project.FindPresentation(_nodeId) is { } presentation)
        {
            PresentationDraft draft = _session.Editor.InspectPresentationPublish(presentation.Id);
            PresentationCommandCatalog catalog = _available?.Catalog
                ?? PresentationCommandCatalog.For(_session.Definition);
            PresentationWorkspace workspace = PresentationBindingResolver.Resolve(_session.Project, presentation);

            state = workspace.Dialogue is { } dialogue && lineId is not null
                ? CoreStageFold.Fold(
                    catalog,
                    draft.SetupCommands,
                    BranchAwareLines.UpTo(dialogue, draft.Bindings, lineId, _session.BranchSelection).FoldLines,
                    _session.TuningLibrary.Tuning).State
                : CoreStageFold.Fold(
                    catalog,
                    draft.SetupCommands,
                    Array.Empty<MiniStageFoldLine>(),
                    _session.TuningLibrary.Tuning).State;
        }

        _foldCache[key] = state;
        return state;
    }

    // ── 갤러리와 텍스트 입력 ──────────────────────────────────────────────

    /// <summary>"연출 추가" 갤러리 버튼 + 텍스트 직접 입력 한 줄.</summary>
    private Control BuildAddRow(
        PresentationNode presentation,
        string? lineId,
        PresentationCommandCatalog catalog)
    {
        var gallery = new Button
        {
            Content = "+ 연출 추가",
            FontSize = 11,
            Padding = new Thickness(8, 3)
        };
        gallery.Click += (_, _) => ShowGallery(gallery, presentation, lineId, catalog);

        var error = new TextBlock
        {
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38)),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };

        var input = new TextBox
        {
            PlaceholderText = "<<커맨드 인자…>> 직접 입력 후 Enter",
            FontFamily = MonoFont,
            FontSize = 11
        };

        input.KeyDown += (_, args) =>
        {
            if (args.Key != Avalonia.Input.Key.Enter || _session is null)
            {
                return;
            }

            // 카탈로그 기준 파싱 — 틀리면 그 자리에서 이유를 말하고, 추측 보정은 없다.
            CommandTextParseResult parsed = CommandText.Parse(input.Text, catalog);

            if (!parsed.Success)
            {
                error.Text = parsed.Error;
                error.IsVisible = true;
                return;
            }

            if (_available is { } available && available.Categories.Count > 0 &&
                available.Categories.All(category =>
                    !string.Equals(category.Id, parsed.Definition!.CategoryId, StringComparison.Ordinal)))
            {
                error.Text = $"'{parsed.Definition!.OutputCommandName}'의 범주는 연결된 공급 노드의 범위 밖입니다.";
                error.IsVisible = true;
                return;
            }

            AddCommand(presentation, lineId, parsed.Definition!.Id, parsed.Arguments!);
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(gallery, 0);
        Grid.SetColumn(input, 1);
        input.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(gallery);
        row.Children.Add(input);

        var host = new StackPanel { Spacing = 3 };
        host.Children.Add(row);
        host.Children.Add(error);
        return host;
    }

    private void AddCommand(
        PresentationNode presentation,
        string? lineId,
        string definitionId,
        IReadOnlyDictionary<string, string> arguments,
        string? presetId = null)
    {
        if (_session is null)
        {
            return;
        }

        if (lineId is null)
        {
            _session.Editor.AddPresentationSetupCommand(presentation.Id, definitionId, arguments);
        }
        else
        {
            _session.Editor.AddPresentationCommand(
                presentation.Id, lineId, definitionId, arguments, presetId: presetId);
        }

        Rebuild();
    }

    private static readonly string[] IntensityOrder = ["미세", "가벼움", "보통", "강함"];

    /// <summary>
    /// 갤러리 팝업: ★프리셋 → 최근 사용(프로젝트 저장) → 카테고리(강도 부그룹) → 검색.
    /// 후보 범위는 <see cref="AvailablePresentationCommandResolver"/>가 정한 것 그대로다 — 사본 금지.
    /// </summary>
    private void ShowGallery(
        Control anchor,
        PresentationNode presentation,
        string? lineId,
        PresentationCommandCatalog catalog)
    {
        var list = new StackPanel { Spacing = 2 };
        var search = new TextBox { PlaceholderText = "이름 검색…", FontSize = 11 };
        var scroll = new ScrollViewer
        {
            Content = list,
            MaxHeight = 380,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };

        var panel = new StackPanel { Spacing = 6, Width = 340 };
        panel.Children.Add(search);
        panel.Children.Add(scroll);

        var flyout = new Flyout { Content = panel, Placement = PlacementMode.Bottom };

        void Fill()
        {
            list.Children.Clear();
            string query = search.Text?.Trim() ?? string.Empty;

            bool Matches(PresentationCommandDefinition definition) =>
                query.Length == 0 ||
                definition.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                definition.OutputCommandName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                definition.Id.Contains(query, StringComparison.OrdinalIgnoreCase);

            void Header(string text) => list.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Opacity = 0.6,
                Margin = new Thickness(0, 6, 0, 1)
            });

            void Item(PresentationCommandDefinition definition, string? presetId, string? presetName)
            {
                var title = new TextBlock
                {
                    Text = presetName is null ? definition.DisplayName : $"★ {presetName}",
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold
                };
                var subtitle = new TextBlock
                {
                    Text = $"<<{definition.OutputCommandName}>>" +
                        (definition.Notes is { } notes ? $" — {Summarize(notes)}" : string.Empty),
                    FontSize = 9,
                    Opacity = 0.6,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                var body = new StackPanel();
                body.Children.Add(title);
                body.Children.Add(subtitle);

                var item = new Button
                {
                    Content = body,
                    Padding = new Thickness(8, 3),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };

                item.Click += (_, _) =>
                {
                    flyout.Hide();
                    AddCommand(
                        presentation,
                        lineId,
                        definition.Id,
                        definition.DefaultArgumentValues(),
                        presetId);
                };

                list.Children.Add(item);
            }

            // ① 프리셋 — 값이 세팅된 "정확한 연출"이 언제나 먼저다.
            AvailablePreset[] presets = (_available?.Presets ?? Array.Empty<AvailablePreset>())
                .Where(preset => catalog.Find(preset.Preset.CommandDefinitionId) is { } definition &&
                    (Matches(definition) ||
                        preset.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            if (presets.Length > 0)
            {
                Header("프리셋");

                foreach (AvailablePreset preset in presets)
                {
                    Item(catalog.Find(preset.Preset.CommandDefinitionId)!, preset.Preset.Id, preset.DisplayName);
                }
            }

            // ② 최근 사용 — 프로젝트에 저장된 목록. 공급 범위 밖 정의는 걸러진다.
            PresentationCommandDefinition[] recents = _session!.Project.RecentCommandIds
                .Select(catalog.Find)
                .Where(definition => definition is not null && Matches(definition) && InScope(definition))
                .Cast<PresentationCommandDefinition>()
                .ToArray();

            if (recents.Length > 0)
            {
                Header("최근 사용");

                foreach (PresentationCommandDefinition definition in recents)
                {
                    Item(definition, null, null);
                }
            }

            // ③ 카테고리 — 액팅류는 강도(미세→강함) 부그룹으로.
            foreach (PresentationCategoryDefinition category in _available?.Categories ?? catalog.Categories)
            {
                PresentationCommandDefinition[] commands = catalog.For(category.Id)
                    .Where(Matches)
                    .ToArray();

                if (commands.Length == 0)
                {
                    continue;
                }

                Header(category.DisplayName);

                if (commands.Any(definition => definition.Intensity is not null))
                {
                    foreach (string intensity in IntensityOrder)
                    {
                        PresentationCommandDefinition[] group = commands
                            .Where(definition => string.Equals(definition.Intensity, intensity, StringComparison.Ordinal))
                            .ToArray();

                        if (group.Length == 0)
                        {
                            continue;
                        }

                        Header($"  {intensity}");

                        foreach (PresentationCommandDefinition definition in group)
                        {
                            Item(definition, null, null);
                        }
                    }

                    foreach (PresentationCommandDefinition definition in commands
                                 .Where(definition => definition.Intensity is null ||
                                     !IntensityOrder.Contains(definition.Intensity, StringComparer.Ordinal)))
                    {
                        Item(definition, null, null);
                    }
                }
                else
                {
                    foreach (PresentationCommandDefinition definition in commands)
                    {
                        Item(definition, null, null);
                    }
                }
            }

            if (list.Children.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = $"'{query}'에 맞는 커맨드가 없습니다.",
                    FontSize = 11,
                    Opacity = 0.6
                });
            }
        }

        bool InScope(PresentationCommandDefinition definition) =>
            _available is not { } available || available.Categories.Count == 0 ||
            available.Categories.Any(category =>
                string.Equals(category.Id, definition.CategoryId, StringComparison.Ordinal));

        search.TextChanged += (_, _) => Fill();
        Fill();
        flyout.ShowAt(anchor);
    }

    /// <summary>notes 요약 한 줄 — 첫 문장 또는 60자.</summary>
    private static string Summarize(string notes)
    {
        string firstLine = notes.Split('\n')[0].Trim();
        int sentenceEnd = firstLine.IndexOf(". ", StringComparison.Ordinal);

        if (sentenceEnd > 0)
        {
            firstLine = firstLine[..(sentenceEnd + 1)];
        }

        return firstLine.Length <= 60 ? firstLine : firstLine[..60] + "…";
    }

    private static Control BuildOrphanCard(
        ResolvedPresentationBinding orphan,
        PresentationCommandCatalog catalog)
    {
        string commands = orphan.Binding.Commands.Count == 0
            ? "명령 없음"
            : string.Join(", ", orphan.Binding.Commands.Select(command =>
                catalog.Find(command.DefinitionId)?.DisplayName ?? command.DefinitionId));

        return new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromArgb(20, 220, 38, 38)),
            Child = new TextBlock
            {
                Text = $"{orphan.Binding.LineId}\n{commands}",
                TextWrapping = TextWrapping.Wrap
            }
        };
    }
}
