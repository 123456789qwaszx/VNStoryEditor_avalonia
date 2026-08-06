using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Vn.App.Services;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;
using Vn.Authoring.Script;

namespace Vn.App.Views;

/// <summary>
/// 대사 노드 하나를 위에서 아래로 편집한다.
///
/// 카드의 들여쓰기와 색은 <see cref="ConditionFlowResolver"/>가 계산한 갈래에서만 나온다.
/// 화면이 조건 상태를 따로 들고 있지 않으므로, 조건 드롭다운을 한 번 바꾸면
/// 그 아래 줄들의 표시가 전부 알아서 따라온다.
///
/// <b>화자와 대사를 입력하면 대본이 바뀐다.</b> 이 노드는 본문을 소유하지 않는다.
/// 화면에 보이는 것은 대본과 이 노드의 조건 데이터를 합친 투영이다.
///
/// 글자 편집은 카드를 다시 만들지 않는다. 다시 만들면 편집 중이던 칸이 사라진다.
/// </summary>
public partial class DialogueNodeEditor : UserControl
{
    private static readonly SolidColorBrush StageSelectionBrush = new(Color.FromArgb(200, 37, 99, 235));

    private AuthoringSession? _session;
    private string? _nodeId;
    private bool _building;
    private RenderedDocument? _previewDocument;
    private readonly IReadOnlyList<OutputPreset> _outputPresets = OutputPresetCatalog.All;
    private OutputPreset _selectedOutputPreset = OutputPresetCatalog.RuntimeFull;
    private string? _selectedLineId;
    private readonly Dictionary<string, Border> _stageLineCards = new(StringComparer.Ordinal);

    /// <summary>갈래 시작 줄(라벨·조건) → 블록/갈래 (W36-a). 클릭하면 그 갈래가 선택으로 남는다.</summary>
    private readonly Dictionary<string, (BranchFlow.Block Block, int BranchIndex)> _branchStarts =
        new(StringComparer.Ordinal);

    /// <summary>MainWindow가 꽂아 주는 공유 하단 무대 프리뷰.</summary>
    internal MiniStagePreview? StagePreview { get; set; }

    public DialogueNodeEditor()
    {
        InitializeComponent();

        NameBox.LostFocus += (_, _) => CommitName();
        AddLineButton.Click += (_, _) => AddLine();
        PublishButton.Click += (_, _) => Publish();
        ApplyScenarioButton.Click += (_, _) => UiGuard.Run(_session, "텍스트 반영", ApplyScenario);
        ExportNodeButton.Click += async (_, _) => await ExportNodeAsync(csv: false);
        ExportNodeCsvButton.Click += async (_, _) => await ExportNodeAsync(csv: true);

        DefaultExitCheck.IsCheckedChanged += (_, _) => OnDefaultExitToggled();
        DefaultExitCombo.SelectionChanged += (_, _) => OnDefaultExitSelected();
        EditorTabs.SelectionChanged += (_, _) => RefreshPreview();

        PreviewPresetCombo.ItemsSource = _outputPresets
            .Select(preset => preset.DisplayName)
            .ToArray();
        PreviewPresetCombo.SelectedIndex = 0;
        PreviewPresetCombo.SelectionChanged += (_, _) => OnPreviewPresetSelected();

        // 목록 아래 빈 공간 우클릭 = 맨 아래 줄 추가 (W49). 카드 위 우클릭은 카드가
        // 먼저 받아 Handled로 막고, 텍스트 칸은 기본 편집 메뉴의 자리다.
        LineScroll.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(LineScroll).Properties.IsRightButtonPressed &&
                (args.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) is null)
            {
                ShowEmptyAreaFlyout();
            }
        };
    }

    /// <summary>빈 공간의 줄 메뉴 (W49) — 여기서의 추가는 언제나 맨 아래, 삭제는 대상이 없어 잠긴다.</summary>
    private void ShowEmptyAreaFlyout()
    {
        if (_session?.Project.FindDialogue(_nodeId) is null)
        {
            return;
        }

        var panel = new StackPanel { Spacing = 2, MinWidth = 150 };
        var flyout = new Flyout { Content = panel };

        var add = new Button
        {
            Content = "맨 아래에 줄 추가",
            FontSize = 11,
            Padding = new Thickness(10, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent
        };
        add.Click += (_, _) =>
        {
            flyout.Hide();
            UiGuard.Run(_session, "줄 추가", AddLineAtEnd);
        };
        panel.Children.Add(add);

        panel.Children.Add(new Button
        {
            Content = "이 줄 삭제",
            FontSize = 11,
            Padding = new Thickness(10, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            IsEnabled = false // 빈 공간에는 지울 줄이 없다 — 줄 카드에서 우클릭
        });

        flyout.ShowAt(LineScroll, showAtPointer: true);
    }

    private void AddLineAtEnd()
    {
        if (_session is null || _session.Project.FindDialogue(_nodeId) is not { } node)
        {
            return;
        }

        string scriptId = node.ScriptId ?? _session.Editor.EnsureDialogueScript(node.Id).Id;
        ScriptLine line = _session.Editor.InsertScriptLine(scriptId); // 인덱스 없음 = 맨 끝
        SelectStageLine(line.Id);
    }

    internal void Attach(AuthoringSession session)
    {
        _session = session;
    }

    /// <summary>어떤 노드를 편집할지 정하고 화면을 만든다.</summary>
    internal void Show(string? nodeId)
    {
        if (!string.Equals(_nodeId, nodeId, StringComparison.Ordinal))
        {
            _selectedLineId = null;
        }

        _nodeId = nodeId;
        Rebuild();
    }

    internal string? NodeId => _nodeId;

    /// <summary>현재 Preview가 보관하는 원본 매핑 Segment. 문자열 표시와 별개로 유지한다.</summary>
    internal RenderedDocument? PreviewDocument => _previewDocument;

    internal OutputPresetId SelectedOutputPresetId => _selectedOutputPreset.Id;

    internal void SelectOutputPreset(OutputPresetId presetId)
    {
        int index = -1;

        for (int itemIndex = 0; itemIndex < _outputPresets.Count; itemIndex++)
        {
            if (_outputPresets[itemIndex].Id == presetId)
            {
                index = itemIndex;
                break;
            }
        }

        if (index < 0 || index == PreviewPresetCombo.SelectedIndex)
        {
            return;
        }

        PreviewPresetCombo.SelectedIndex = index;
    }

    internal void Rebuild()
    {
        if (_session is null || _session.Project.FindDialogue(_nodeId) is not { } node)
        {
            LineHost.Children.Clear();
            ResultHost.Children.Clear();
            ClearPreview();
            return;
        }

        _building = true;

        try
        {
            NameBox.Text = node.Name;

            BuildScriptSummary(node);

            DialogueScript script = DialogueScriptResolver.Resolve(_session.Project, node);
            DialogueFlow flow = ConditionFlowResolver.Resolve(
                node,
                script,
                _session.Project,
                _session.Definition);

            LineHost.Children.Clear();
            _stageLineCards.Clear();

            if (_selectedLineId is null ||
                flow.Lines.All(item => !string.Equals(item.Line.LineId, _selectedLineId, StringComparison.Ordinal)))
            {
                _selectedLineId = flow.Lines.FirstOrDefault()?.Line.LineId;
            }

            if (flow.Lines.Count > 0)
            {
                // 목록 위에 한 번만 나오는 얇은 열 헤더 — 카드에는 열 이름이 없다.
                LineHost.Children.Add(BuildColumnHeader());
            }

            // 갈래 트리 상태 (W36-a) — 헤더 문구용. 카드별 흐림은 RefreshBranchStates가 맡는다.
            BranchFlow.Analysis<DialogueLine> branchAnalysis =
                AnalyzeBranches(script, SimulateBranches(node, script).Effective);
            var blockOfLine = new Dictionary<string, BranchFlow.Block>(StringComparer.Ordinal);

            foreach (BranchFlow.Block block in branchAnalysis.Blocks)
            {
                foreach (BranchFlow.Branch branchStart in block.Branches)
                {
                    blockOfLine[branchStart.LineId] = block;
                }
            }

            // 선택 블록은 조건과 달리 들여쓰기·색이 아니라 블록 전체를 감싸는 박스로 보여 준다.
            // 옵션 라벨과 본문 카드가 같은 박스 안에 순서대로 쌓인다.
            StackPanel? choiceBox = null;
            int choiceChain = -1;

            foreach (ResolvedLine line in flow.Lines)
            {
                bool inChoice = line.Branch is { IsChoice: true };

                if (inChoice && (choiceBox is null || line.Branch!.ChainIndex != choiceChain))
                {
                    choiceBox = new StackPanel { Spacing = 3 };
                    choiceChain = line.Branch!.ChainIndex;

                    // 지금 프리뷰가 접는 갈래를 헤더에 그대로 쓴다 (W36-a).
                    string headerText = "선택지 — 미선택 (문서 순서 근사)";

                    if (blockOfLine.TryGetValue(line.Line.LineId, out BranchFlow.Block? headerBlock) &&
                        headerBlock is { SelectedBranch: { } taken } &&
                        taken >= 0 && taken < headerBlock.Branches.Count)
                    {
                        string label = headerBlock.Branches[taken].Label;
                        headerText = $"선택지 — ▶ {(label.Length > 18 ? label[..18] + "…" : label)}";
                    }

                    choiceBox.Children.Add(new TextBlock
                    {
                        Text = headerText,
                        FontSize = 10,
                        FontWeight = FontWeight.Bold,
                        Opacity = 0.75
                    });

                    LineHost.Children.Add(new Border
                    {
                        Padding = new Thickness(6, 4),
                        CornerRadius = new CornerRadius(8),
                        BorderThickness = new Thickness(2),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(150, 217, 119, 6)),
                        Background = new SolidColorBrush(Color.FromArgb(14, 217, 119, 6)),
                        Child = choiceBox
                    });
                }
                else if (!inChoice)
                {
                    choiceBox = null;
                    choiceChain = -1;
                }

                Control card = WrapForStageSelection(BuildCard(node, script, line), line.Line.LineId);

                if (inChoice && choiceBox is not null)
                {
                    choiceBox.Children.Add(card);
                }
                else
                {
                    LineHost.Children.Add(card);
                }
            }

            if (flow.Lines.Count == 0)
            {
                LineHost.Children.Add(new TextBlock
                {
                    Text = "'줄 추가'를 누르면 바로 타이핑할 수 있습니다. 긴 대본은 Script Preview의 붙여넣기가 맡습니다.",
                    Opacity = 0.6,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            AddOrphanCards(node, script);
            BuildDefaultExit(node);
            BuildResults(node);
            RefreshExportState(node);
            ShowProblems(flow);
            RefreshBranchStates(node);
            RefreshPreviewCore(node);
        }
        finally
        {
            _building = false;
        }
    }

    /// <summary>
    /// 조건 값 시뮬 (W36-b) — 수동 선택 위에 "값이 이렇다면 이 갈래"의 자동 판정을 얹는다.
    /// 시작값 = 등록 초기값 + 세션 시뮬 오버라이드. 식은 노드 조건·게임 정의 조건에서 찾는다.
    /// </summary>
    private ConditionSimulation.Result SimulateBranches(DialogueNode node, DialogueScript script)
    {
        var initial = RegisteredVariables(node)
            .Select(assignment => (
                assignment.Variable,
                Value: _session!.SimulationValues.TryGetValue(assignment.Variable, out string? overridden)
                    ? overridden
                    : assignment.Value))
            .ToList();

        return ConditionSimulation.Decide(
            script.Lines,
            line => line.Transition?.Kind,
            line => line.LineId,
            line => line.Transition?.ConditionId is { } conditionId
                ? _session!.Project.FindCondition(conditionId)?.Expression
                    ?? _session.Definition.Conditions.FirstOrDefault(condition =>
                        string.Equals(condition.Id, conditionId, StringComparison.Ordinal))?.Expression
                : null,
            line => line.Sets.Select(operation => (operation.Variable, operation.Operator, operation.Value)),
            initial,
            _session!.BranchSelection);
    }

    /// <summary>대본 라인의 갈래 분석 — 프리뷰 폴드와 같은 유효 선택(수동+자동)·커서 기준(사본 금지).</summary>
    private BranchFlow.Analysis<DialogueLine> AnalyzeBranches(DialogueScript script, StageBranchSelection effective)
    {
        return BranchFlow.Analyze(
            script.Lines,
            line => line.Transition?.Kind,
            line => line.LineId,
            line => line.Text,
            effective,
            _selectedLineId);
    }

    /// <summary>
    /// 갈래 트리 상태 갱신 (W36-a) — 선택되지 않은 갈래의 카드는 흐려진다(프리뷰에 접히지
    /// 않는 줄이 밝게 남으면 화면이 거짓말을 한다). 미선택(근사) 갈래는 기존 밝기 그대로다.
    /// 카드 재생성 없이 흐림만 바꾸므로 커서 이동마다 불러도 포커스를 잃지 않는다.
    /// </summary>
    private void RefreshBranchStates(DialogueNode node)
    {
        if (_session is null)
        {
            return;
        }

        DialogueScript script = DialogueScriptResolver.Resolve(_session.Project, node);
        BranchFlow.Analysis<DialogueLine> analysis =
            AnalyzeBranches(script, SimulateBranches(node, script).Effective);

        _branchStarts.Clear();

        foreach (BranchFlow.Block block in analysis.Blocks)
        {
            for (int branchIndex = 0; branchIndex < block.Branches.Count; branchIndex++)
            {
                _branchStarts[block.Branches[branchIndex].LineId] = (block, branchIndex);
            }
        }

        foreach (BranchFlow.AnalyzedLine<DialogueLine> line in analysis.Lines)
        {
            if (!_stageLineCards.TryGetValue(line.Source.LineId, out Border? card) || card is null)
            {
                continue;
            }

            bool dimmed = !line.Taken && !line.Unresolved;
            card.Opacity = dimmed ? 0.42 : 1;
            ToolTip.SetTip(
                card,
                dimmed
                    ? "선택되지 않은 갈래 — 프리뷰·재생에 접히지 않습니다. 갈래 시작 줄(라벨·조건)을 클릭하면 이 갈래를 봅니다."
                    : null);
        }
    }

    /// <summary>
    /// 편집 컨트롤을 다시 만들지 않고 읽기 전용 Preview만 재합성한다.
    /// 화자·대사를 입력하는 동안 내용 변경이 연속으로 와도 포커스를 잃지 않는다.
    /// </summary>
    internal void RefreshPreview()
    {
        if (_session is null || _session.Project.FindDialogue(_nodeId) is not { } node)
        {
            ClearPreview();
            return;
        }

        RefreshPreviewCore(node);
    }

    private void RefreshPreviewCore(DialogueNode node)
    {
        _previewDocument = WorkingDialoguePreview.ComposePreset(
            _session!.Project,
            node.Id,
            _selectedOutputPreset,
            _session.Definition);

        bool scenarioEditable = _selectedOutputPreset.Id == OutputPresetId.ScenarioOnly;

        // ScenarioOnly는 편집면이다 (X12a). 사용자가 그 안에서 쓰는 중이면 라이브 미러가
        // 붙여넣던 텍스트를 덮어쓰지 않는다 — 반영 버튼이 정식 반영 경로다.
        if (!(scenarioEditable && PreviewBox.IsFocused))
        {
            PreviewBox.Text = DocumentPreviewFormatter.Format(_previewDocument);
        }

        PreviewBox.IsReadOnly = !scenarioEditable;
        ApplyScenarioButton.IsVisible = scenarioEditable;
        ScenarioApplyHintText.Text = scenarioEditable
            ? "붙여넣거나 고친 뒤 [텍스트 반영] — 기존 줄의 LineId는 보존됩니다(diff 기반)."
            : string.Empty;

        PreviewSummaryText.Text =
            $"{_selectedOutputPreset.DisplayName} · {_previewDocument.Segments.Count}개 Segment · " +
            (scenarioEditable ? "편집 가능 — 저장 상태와 라이브 동기화" : "작업 중 미리 보기 (읽기 전용 라이브 미러)");

        UpdateStagePreview(node);
    }

    private string? _pendingDeleteText;

    /// <summary>
    /// ScenarioOnly 텍스트를 라인으로 반영한다 (X12a). 전량 재생성 없음 —
    /// diff는 ScriptSynchronizer, 삭제는 같은 텍스트로 한 번 더 눌러 확인한다.
    /// </summary>
    private void ApplyScenario()
    {
        if (_session is null || _session.Project.FindDialogue(_nodeId) is not { } node)
        {
            return;
        }

        string text = PreviewBox.Text ?? string.Empty;
        bool confirmDeletes = string.Equals(_pendingDeleteText, text, StringComparison.Ordinal);

        ScenarioPasteOutcome outcome = _session.Editor.ApplyScenarioText(
            node.Id, text, _session.Definition, confirmDeletes);

        ScenarioProblemsText.IsVisible = outcome.Problems.Count > 0;
        ScenarioProblemsText.Text = string.Join(
            Environment.NewLine,
            outcome.Problems.Select(problem => $"• {problem}"));

        if (outcome.NeedsDeleteConfirmation && !confirmDeletes)
        {
            _pendingDeleteText = text;
            _session.SetStatus($"{outcome.Summary()} — 같은 텍스트로 [텍스트 반영]을 한 번 더 누르면 적용합니다.");
            return;
        }

        _pendingDeleteText = null;
        _session.SetStatus(outcome.Applied
            ? $"텍스트를 반영했습니다. {outcome.Summary()}" +
              (outcome.Problems.Count > 0 ? $" · 확인할 항목 {outcome.Problems.Count}개" : string.Empty)
            : outcome.Summary());
    }

    /// <summary>클릭한 라인이 무대 프리뷰의 기준이 되도록 왼쪽 강조 띠로 감싼다.</summary>
    private Control WrapForStageSelection(Control card, string lineId)
    {
        var wrapper = new Border
        {
            // Transparent는 보이지 않지만 히트테스트는 된다 — 배경 없는 일반 대사 카드의
            // 빈 부분을 클릭해도 선택이 되게 한다(글자 위만 반응하던 문제 수정, W53).
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = string.Equals(lineId, _selectedLineId, StringComparison.Ordinal)
                ? StageSelectionBrush
                : Brushes.Transparent,
            Padding = new Thickness(4, 0, 0, 0),
            Child = card
        };

        wrapper.AddHandler(
            PointerPressedEvent,
            (_, args) =>
            {
                SelectStageLine(lineId);

                // 우클릭 = 줄 추가/삭제 바로가기 (W47) — ＋ 플라이아웃을 거치지 않는 짧은 길.
                // 텍스트 입력 칸 위에서는 비켜선다 (W49) — 잘라내기/복사/붙여넣기 기본
                // 메뉴가 그 자리의 주인이다.
                if (args.GetCurrentPoint(wrapper).Properties.IsRightButtonPressed &&
                    (args.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) is null)
                {
                    ShowLineContextFlyout(wrapper, lineId);
                    args.Handled = true; // 아래 빈 공간 메뉴(W49)가 겹쳐 뜨지 않게
                }
            },
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        _stageLineCards[lineId] = wrapper;

        return wrapper;
    }

    /// <summary>줄 카드 우클릭 메뉴 (W47) — 추가·삭제는 ＋ 플라이아웃과 같은 편집 경로 하나를 쓴다.</summary>
    private void ShowLineContextFlyout(Control anchor, string lineId)
    {
        if (_session?.Project.FindDialogue(_nodeId) is not { } node || node.ScriptId is not { } scriptId)
        {
            return;
        }

        var panel = new StackPanel { Spacing = 2, MinWidth = 140 };

        Button Item(string label, string tip)
        {
            var button = new Button
            {
                Content = label,
                FontSize = 11,
                Padding = new Thickness(10, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent
            };
            ToolTip.SetTip(button, tip);
            panel.Children.Add(button);
            return button;
        }

        var flyout = new Flyout { Content = panel };

        Button add = Item("아래에 줄 추가", "이 줄 바로 아래에 새 줄을 끼워 넣습니다.");
        add.Click += (_, _) =>
        {
            flyout.Hide();
            UiGuard.Run(_session, "줄 추가", AddLine); // 선택된 줄 아래에 — 위의 SelectStageLine이 이미 골랐다
        };

        Button remove = Item("이 줄 삭제", "대본에서 이 줄을 뺍니다. LineId는 은퇴 상태로 남습니다.");
        remove.Click += (_, _) =>
        {
            flyout.Hide();
            UiGuard.Run(_session, "줄 삭제", () => _session!.Editor.RetireScriptLine(scriptId, lineId));
        };

        flyout.ShowAt(anchor);
    }

    private void SelectStageLine(string lineId)
    {
        if (string.Equals(_selectedLineId, lineId, StringComparison.Ordinal))
        {
            return;
        }

        _selectedLineId = lineId;

        // 갈래 시작 줄(라벨·조건) 클릭 = 그 갈래 선택 (W36-a) — 커서를 옮겨도 유지된다.
        if (_session is not null &&
            _branchStarts.TryGetValue(lineId, out (BranchFlow.Block Block, int BranchIndex) start))
        {
            if (start.Block.IsChoice)
            {
                _session.BranchSelection.SelectChoice(start.Block.BlockLineId, lineId);
            }
            else
            {
                _session.BranchSelection.SelectCondition(start.Block.BlockLineId, start.BranchIndex);
            }
        }

        foreach ((string cardLineId, Border card) in _stageLineCards)
        {
            card.BorderBrush = string.Equals(cardLineId, lineId, StringComparison.Ordinal)
                ? StageSelectionBrush
                : Brushes.Transparent;
        }

        if (_session?.Project.FindDialogue(_nodeId) is { } node)
        {
            RefreshBranchStates(node); // 커서 덮어쓰기·새 선택이 흐림 상태를 바꾼다
            UpdateStagePreview(node);
        }
    }

    /// <summary>
    /// 공급된 연출(내보내기와 같은 <see cref="NodeExportResolver"/> 규칙으로 찾는다)을
    /// 선택 라인까지 접어 하단 무대 프리뷰에 민다. 공급이 없으면 화자만 표시한다.
    /// </summary>
    private void UpdateStagePreview(DialogueNode node)
    {
        if (StagePreview is null || _session is null)
        {
            return;
        }

        DialogueScript script = DialogueScriptResolver.Resolve(_session.Project, node);
        DialogueLine? selected = script.Lines.FirstOrDefault(line =>
                string.Equals(line.LineId, _selectedLineId, StringComparison.Ordinal))
            ?? script.Lines.FirstOrDefault();

        string contextLabel = $"대사: {node.Name}";
        NodeExport export = NodeExportResolver.Resolve(_session.Project, node.Id);

        int index = selected is null
            ? -1
            : script.Lines.ToList().FindIndex(item =>
                string.Equals(item.LineId, selected.LineId, StringComparison.Ordinal));

        // 조건 값 시뮬 (W36-b): 수동 선택 + 값 기반 자동 판정을 합친 유효 선택을 쓴다.
        ConditionSimulation.Result simulation = SimulateBranches(node, script);

        // 스탯 HUD (X3): 작업 중 문서 기준 — (시뮬 시작값 반영) 초기값 + 선택 갈래 기준의
        // set 누적 (W35 — 문서 순서 근사 은퇴. 선택 상태는 발행 쪽과 같은 LineId 키를 쓴다).
        BranchFlow.Analysis<DialogueLine> scriptAnalysis = BranchFlow.Analyze(
            script.Lines,
            line => line.Transition?.Kind,
            line => line.LineId,
            line => line.Text,
            simulation.Effective,
            selected?.LineId);

        var setsUpToLine = new List<(string Variable, SetOperatorKind Operator, string Value)>();

        foreach (BranchFlow.AnalyzedLine<DialogueLine> item in scriptAnalysis.Lines)
        {
            if (item.Taken || item.Unresolved)
            {
                setsUpToLine.AddRange(item.Source.Sets.Select(operation =>
                    (operation.Variable, operation.Operator, operation.Value)));
            }

            if (selected is not null &&
                string.Equals(item.Source.LineId, selected.LineId, StringComparison.Ordinal))
            {
                break;
            }
        }

        IReadOnlyList<StatFold.StatValue> stats = StatFold.Fold(
            RegisteredVariables(node).Select(assignment => (
                assignment.Variable,
                _session.SimulationValues.TryGetValue(assignment.Variable, out string? overridden)
                    ? overridden
                    : assignment.Value)),
            setsUpToLine);

        // 선택 라인이 옵션 라벨이면 그 블록의 버튼 묶음이 대사창을 대신한다.
        IReadOnlyList<BranchFlow.Block> scriptBlocks = BranchFlow.PassedBlocks(
            scriptAnalysis, line => line.LineId, selected?.LineId);

        IReadOnlyList<StageChoiceOption>? choices = StageChoiceOptions.Build(
            ChoiceOptionBundle.At(
                script.Lines,
                index,
                line => line.Transition?.Kind,
                line => line.Text),
            optionIndex => script.Lines[optionIndex].LineId,
            scriptBlocks);

        if (export.Presentation is null || export.Dialogue is null)
        {
            StagePreview.Show(new MiniStagePreviewRequest(
                contextLabel,
                MiniStageState.Empty,
                HasPresentation: false,
                selected?.LineId,
                selected?.Speaker,
                selected?.Text,
                LineIndex: index,
                LineCount: script.Lines.Count,
                Stats: stats,
                ChoiceOptions: choices,
                BranchBlocks: scriptBlocks,
                AutoBranchBlocks: simulation.AutoBlocks.ToArray()));
            return;
        }

        // 연출은 자신이 읽은 발행본 기준으로 접는다. 지금 편집 중인 줄이 그 발행본에
        // 없다면(발행 후 추가된 줄 등) 그 사실을 숨기지 않고 알린다.
        string? notice = selected is not null && !export.Dialogue.ContainsLine(selected.LineId)
            ? "이 줄은 공급된 연출이 읽은 발행본에 없습니다. 문서 전체 기준 상태를 표시합니다."
            : null;

        // 갈래 인식 (W35) — 유효 선택(수동+자동)된 갈래의 라인만 접는다. 미결정 블록은 근사 + 표시.
        BranchAwareLines.Result branch = BranchAwareLines.UpTo(
            export.Dialogue, export.Presentation.Bindings, selected?.LineId, simulation.Effective);

        CoreStageFoldResult fold = CoreStageFold.Fold(
            PresentationCommandCatalog.For(_session.Definition),
            export.Presentation.SetupCommands,
            branch.FoldLines,
            _session.TuningLibrary.Tuning);

        StagePreview.Show(new MiniStagePreviewRequest(
            contextLabel,
            fold.State,
            HasPresentation: true,
            selected?.LineId,
            selected?.Speaker,
            selected?.Text,
            notice,
            LineIndex: index,
            LineCount: script.Lines.Count,
            // 여기 보이는 것은 공급된 발행 결과다 — 발행은 불변이므로 직접 조작을 잠근다.
            EditContext: new StageEditContext(
                export.Presentation.SourceNodeId,
                selected?.LineId,
                DisabledReason: "공급된 발행 결과를 보고 있습니다. 작업 중 연출을 편집하려면 연출 노드를 여세요."),
            Stats: stats,
            ChoiceOptions: choices,
            CoreState: fold.CoreState,
            BranchBlocks: scriptBlocks,
            // 전이(W33): 이 라인으로 넘어가는 시간 = 라인 커맨드 duration의 최댓값.
            TransitionSeconds: StageTransitions.SecondsFor(
                PresentationCommandCatalog.For(_session.Definition),
                export.Presentation.Bindings.FirstOrDefault(item =>
                    string.Equals(item.LineId, selected?.LineId, StringComparison.Ordinal))?.Commands),
            // 소리 표시(W34-b): 정지 프레임에 없는 오디오를 ♪ 칩으로.
            AudioCues: StageAudioCues.Of(
                PresentationCommandCatalog.For(_session.Definition),
                export.Presentation.Bindings.FirstOrDefault(item =>
                    string.Equals(item.LineId, selected?.LineId, StringComparison.Ordinal))?.Commands),
            AutoBranchBlocks: simulation.AutoBlocks.ToArray()));
    }

    /// <summary>
    /// 프리뷰에서 갈래 선택이 바뀌었다 (W47) — 카드 흐림과 무대 프리뷰만 갱신한다.
    /// 카드를 다시 만들지 않아 입력 포커스를 잃지 않는다.
    /// </summary>
    internal void RefreshBranchView()
    {
        if (_session?.Project.FindDialogue(_nodeId) is { } node)
        {
            RefreshBranchStates(node);
            UpdateStagePreview(node);
        }
    }

    /// <summary>프리뷰 창의 이전/다음. 선택은 이 편집기의 것 하나뿐이다.
    /// 재생 중에는 타는 경로만 걷는다 (W39) — 안 타는 갈래 라인은 건너뛰고,
    /// 경로 끝에서는 실행 출구를 따라 다음 노드로 넘어간다.</summary>
    internal void MoveStageLine(int delta)
    {
        if (_session?.Project.FindDialogue(_nodeId) is not { } node)
        {
            return;
        }

        DialogueScript script = DialogueScriptResolver.Resolve(_session.Project, node);

        if (script.Lines.Count == 0)
        {
            return;
        }

        if (StagePreview?.Playback.IsPlaying == true && TryMoveAlongPath(node, script, delta))
        {
            return;
        }

        int index = script.Lines.ToList().FindIndex(item =>
            string.Equals(item.LineId, _selectedLineId, StringComparison.Ordinal));
        int next = Math.Clamp((index < 0 ? 0 : index) + delta, 0, script.Lines.Count - 1);

        SelectStageLine(script.Lines[next].LineId);
    }

    /// <summary>재생 경로 이동 (W39). 현재 라인이 경로 밖(출구 뒤 등)이면 false —
    /// 문서 순서 이동으로 계속해 조용히 버리지 않는다.</summary>
    private bool TryMoveAlongPath(DialogueNode node, DialogueScript script, int delta)
    {
        PlaybackPath.Result path = TracePlaybackPath(node, script);
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

    /// <summary>이 노드의 재생 경로 — 유효 갈래 선택(수동+자동) 기준, 출구는 그래프의 것.</summary>
    private PlaybackPath.Result TracePlaybackPath(DialogueNode node, DialogueScript script)
    {
        return PlaybackPath.Trace(
            script.Lines,
            line => line.Transition?.Kind,
            line => line.LineId,
            line => node.BranchExits.TryGetValue(line.LineId, out string? exit) ? exit : null,
            node.DefaultExitTargetNodeId,
            SimulateBranches(node, script).Effective,
            _selectedLineId);
    }

    /// <summary>문서 끝 도달 (W39) — 실행 출구를 따라 다음 노드로 전환하면 true.</summary>
    internal bool TryExitPlaybackNode()
    {
        if (_session?.Project.FindDialogue(_nodeId) is not { } node)
        {
            return false;
        }

        DialogueScript script = DialogueScriptResolver.Resolve(_session.Project, node);
        return TryEnterNextNode(TracePlaybackPath(node, script).ExitTargetNodeId);
    }

    /// <summary>
    /// 실행 출구의 대사 노드로 재생을 넘긴다 (W39). 노드 선택이 바뀌면 편집기가 그 노드를
    /// 열고 첫 라인 프리뷰를 밀어 재생이 이어진다. 대사 노드가 아니면 알리고 멈춘다.
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

        if (string.Equals(next.Id, _nodeId, StringComparison.Ordinal))
        {
            // 자기 자신으로 돌아오는 출구 — 선택 변경 이벤트가 없으므로 첫 라인을 직접 고른다.
            if (DialogueScriptResolver.Resolve(_session.Project, next).Lines.FirstOrDefault() is not { } first)
            {
                return false;
            }

            StagePreview.Playback.OnNodeSwitch();
            _selectedLineId = null;
            SelectStageLine(first.LineId);
            return true;
        }

        StagePreview.Playback.OnNodeSwitch();
        _session.Select(next.Id);
        return true;
    }

    private void OnPreviewPresetSelected()
    {
        if (_building ||
            PreviewPresetCombo.SelectedIndex < 0 ||
            PreviewPresetCombo.SelectedIndex >= _outputPresets.Count)
        {
            return;
        }

        OutputPreset selected = _outputPresets[PreviewPresetCombo.SelectedIndex];

        if (_selectedOutputPreset.Id == selected.Id)
        {
            return;
        }

        _selectedOutputPreset = selected;
        RefreshPreview();
    }

    private void ClearPreview()
    {
        _previewDocument = null;
        PreviewBox.Text = string.Empty;
        PreviewSummaryText.Text = "DialogueNode를 선택하면 작업 중 상태를 선택한 출력 프리셋으로 펼쳐 보여 줍니다.";
        StagePreview?.Show(null);
    }

    // ── 대본 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 대본은 노드가 생성될 때 함께 만들어진다 (X4, D-3). 여기는 표시만 한다 —
    /// 가져오기·대본 선택 진입점은 제거됐고, 긴 외부 대본은 X12의 붙여넣기 경로가 맡는다.
    /// </summary>
    private void BuildScriptSummary(DialogueNode node)
    {
        ScriptDocument? current = _session!.Project.FindScript(node.ScriptId);

        ScriptSummaryText.Text = current is null
            ? "대본이 아직 없습니다. '줄 추가'를 누르면 전용 대본이 함께 만들어집니다."
            : $"{current.ActiveLines.Count()}줄 · {current.PrimaryLocale}" +
              (current.SourcePath is null ? string.Empty : $" · {Path.GetFileName(current.SourcePath)}");
    }

    private void AddLine()
    {
        if (_session is null || _session.Project.FindDialogue(_nodeId) is not { } node)
        {
            return;
        }

        // 가져오기로 만들었던 옛 프로젝트의 대본 없는 노드도 여기서 처음 쓸 수 있게 된다.
        string scriptId = node.ScriptId ?? _session.Editor.EnsureDialogueScript(node.Id).Id;

        // 선택한 줄이 있으면 그 바로 아래에 끼워 넣는다. 없으면 맨 끝, 비어 있으면 첫 줄.
        int? at = null;

        if (_selectedLineId is not null && _session.Project.FindScript(scriptId) is { } document)
        {
            int index = document.Lines.FindIndex(line =>
                string.Equals(line.Id, _selectedLineId, StringComparison.Ordinal));

            if (index >= 0)
            {
                at = index + 1;
            }
        }

        ScriptLine line = _session.Editor.InsertScriptLine(scriptId, at);

        // 새 줄이 새 선택이다 — 프리뷰 기준도 함께 옮긴다.
        SelectStageLine(line.Id);
    }

    // ── 카드 ────────────────────────────────────────────────────────────────
    //
    // 고밀도 개편: 평범한 대사는 딱 한 줄(34~42px)이다. 조건·선택·Set은 메인 행 위의
    // 태그 레일, 출구는 아래의 출구 레일로만 나타난다 — 태그가 없으면 레일도 없다.
    // 태그는 표시가 아니라 편집 진입점이다: 누르면 기존 편집 컨트롤이 Flyout으로 열린다.

    /// <summary>헤더와 모든 카드가 공유하는 열 구성 — Index | LineId | Character | 대사 | ＋.</summary>
    private const string RowColumns = "30,64,96,*,28";

    /// <summary>목록 위에 한 번만 나오는 얇은 열 헤더.</summary>
    private Control BuildColumnHeader()
    {
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(RowColumns),
            // 카드 왼쪽의 선택 띠(3) + 여백(4) + 카드 안쪽 여백(6)만큼 맞춰 들어간다.
            Margin = new Thickness(13, 0, 0, 2)
        };

        void Add(string text, int column)
        {
            var block = new TextBlock { Text = text, FontSize = 9, Opacity = 0.45 };
            Grid.SetColumn(block, column);
            header.Children.Add(block);
        }

        Add("#", 0);
        Add("LineId", 1);
        Add("화자", 2);
        Add("대사", 3);
        return header;
    }

    private Control BuildCard(DialogueNode node, DialogueScript script, ResolvedLine resolved)
    {
        ConditionBranch? branch = resolved.Branch;
        int palette = branch?.PaletteIndex ?? -1;
        bool isLabel = resolved.Line.Transition?.OpensOption == true;

        var body = new StackPanel { Spacing = 1 };

        if (BuildTagRail(node, resolved) is { } rail)
        {
            body.Children.Add(rail);
        }

        body.Children.Add(BuildMainRow(node, script, resolved));

        if (resolved.IsBranchExit && branch is not null)
        {
            body.Children.Add(BuildExitRail(node, branch));
        }

        if (isLabel)
        {
            // 라벨은 대사가 아니라 플레이어가 누르는 버튼이다 (X10) —
            // 채워진 배경으로 분기 대사와 한눈에 갈린다.
            return new Border
            {
                Padding = new Thickness(6, 3),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Color.FromArgb(170, 217, 119, 6)),
                Background = new SolidColorBrush(Color.FromArgb(45, 217, 119, 6)),
                Child = body
            };
        }

        if (branch is { IsChoice: true })
        {
            // 선택 후 분기 대사는 일반 대사 줄과 똑같이 생겼다(화자 편집 포함).
            // 라벨 아래로 들여쓰기만 되어 소속을 보여 준다.
            return new Border
            {
                Margin = new Thickness(18, 0, 0, 0),
                Padding = new Thickness(6, 3),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
                Child = body
            };
        }

        return new Border
        {
            // 조건 안이면 오른쪽으로 한 단계 들어가고 그 조건 팔레트의 색을 쓴다.
            Margin = new Thickness(resolved.Depth * 20, 0, 0, 0),
            Padding = new Thickness(6, 3),
            CornerRadius = new CornerRadius(4),
            BorderThickness = branch is null
                ? new Thickness(1)
                : new Thickness(3, 1, 1, 1),
            BorderBrush = branch is null
                ? new SolidColorBrush(Color.FromArgb(60, 128, 128, 128))
                : BranchPalette.Accent(palette),
            Background = resolved.IsBranchExit
                ? BranchPalette.ExitBackground(palette)
                : branch is null
                    ? null
                    : BranchPalette.Background(palette),
            Child = body
        };
    }

    /// <summary>Index | LineId | 화자 | 대사 | ＋ 의 메인 행. 높이는 한 줄로 고정이다.</summary>
    private Control BuildMainRow(DialogueNode node, DialogueScript script, ResolvedLine resolved)
    {
        // 선택지 라벨은 화자가 없는 버튼 텍스트다 (X9) — 화자 칸에는 ▶만 놓는다.
        bool isLabel = resolved.Line.Transition?.OpensOption == true;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions(RowColumns) };
        string revisionTip = $"{resolved.Line.LineId} · rev {resolved.Line.Revision}";

        var index = new TextBlock
        {
            Text = resolved.Index.ToString(),
            FontSize = 10,
            Opacity = 0.45,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(index, revisionTip);
        Grid.SetColumn(index, 0);
        row.Children.Add(index);

        var lineId = new TextBlock
        {
            Text = resolved.Line.LineId,
            FontSize = 9,
            Opacity = 0.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        ToolTip.SetTip(lineId, revisionTip);
        Grid.SetColumn(lineId, 1);
        row.Children.Add(lineId);

        // 긴 대사가 카드 높이를 늘리지 않는다 — 칸 안에서 가로로 흐른다.
        var text = new TextBox
        {
            Text = resolved.Line.Text,
            PlaceholderText = isLabel ? "선택지 라벨 (버튼 텍스트)" : "대사",
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap,
            FontSize = 12,
            MinHeight = 26,
            Padding = new Thickness(6, 3),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };

        AutoCompleteBox? speaker = null;
        string scriptId = script.ScriptId ?? string.Empty;

        // 글자가 바뀔 때마다 대본에 넣되, 그것이 카드 목록을 다시 만들지는 않는다.
        // 편집기가 내용 변경과 구조 변경을 구분해서 알리기 때문에 가능하다.
        void Commit()
        {
            if (!_building && scriptId.Length > 0)
            {
                _session!.Editor.SetScriptLineText(
                    scriptId,
                    resolved.Line.LineId,
                    speaker?.Text ?? resolved.Line.Speaker,
                    text.Text ?? string.Empty,
                    script.Locale);
            }
        }

        if (isLabel)
        {
            var icon = new TextBlock
            {
                Text = "▶",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(217, 119, 6)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(icon, "선택지 버튼 텍스트 — 대사가 아닙니다.");
            Grid.SetColumn(icon, 2);
            row.Children.Add(icon);
        }
        else
        {
            // 화자는 등록 목록(game.definition speakers)의 드롭다운 + 자유 입력 겸용이다 (X5).
            speaker = new AutoCompleteBox
            {
                Text = resolved.Line.Speaker,
                PlaceholderText = "화자",
                FontSize = 11,
                MinHeight = 26,
                ItemsSource = _session!.Definition.Speakers
                    .Select(item => item.Name)
                    .Where(item => item.Length > 0)
                    .ToList(),
                FilterMode = AutoCompleteFilterMode.Contains,
                MinimumPrefixLength = 0
            };
            speaker.TextChanged += (_, _) => Commit();

            // ▾ = 등록 화자 전체 목록 (W40) — 자동완성은 타이핑해야 열리니 클릭 한 번 길을 따로 둔다.
            AutoCompleteBox speakerBox = speaker;
            var pick = new Button
            {
                Content = "▾",
                FontSize = 8,
                Padding = new Thickness(3, 0),
                MinWidth = 16,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(1, 0, 0, 0)
            };
            ToolTip.SetTip(pick, "등록된 화자 목록에서 선택");
            pick.Click += (_, _) => ShowSpeakerFlyout(pick, speakerBox);

            var speakerCell = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Margin = new Thickness(4, 0, 0, 0)
            };
            Grid.SetColumn(speaker, 0);
            Grid.SetColumn(pick, 1);
            speakerCell.Children.Add(speaker);
            speakerCell.Children.Add(pick);
            Grid.SetColumn(speakerCell, 2);
            row.Children.Add(speakerCell);
        }

        text.TextChanged += (_, _) => Commit();
        Grid.SetColumn(text, 3);
        row.Children.Add(text);

        Button plus = BuildPlusButton(node, script, resolved);
        Grid.SetColumn(plus, 4);
        row.Children.Add(plus);

        return row;
    }

    /// <summary>
    /// 등록 화자 드롭다운 (W40). 항목 클릭 = 그 이름이 화자 칸에 들어간다 —
    /// TextChanged가 대본에 반영하므로 자유 입력과 같은 길 하나를 쓴다.
    /// </summary>
    private void ShowSpeakerFlyout(Button anchor, AutoCompleteBox speaker)
    {
        List<string> names = _session!.Definition.Speakers
            .Select(item => item.Name)
            .Where(item => item.Length > 0)
            .ToList();

        if (names.Count == 0)
        {
            _session.SetStatus("game.definition에 등록된 화자가 없습니다. 화자 칸에 직접 입력하세요.");
            return;
        }

        var panel = new StackPanel();
        var flyout = new Flyout
        {
            Content = new ScrollViewer { MaxHeight = 240, Content = panel },
            Placement = PlacementMode.Bottom
        };

        foreach (string name in names)
        {
            var item = new Button
            {
                Content = name,
                FontSize = 11,
                Padding = new Thickness(10, 4),
                MinWidth = 120,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent
            };
            item.Click += (_, _) =>
            {
                speaker.Text = name;
                flyout.Hide();
            };
            panel.Children.Add(item);
        }

        flyout.ShowAt(anchor);
    }

    // ── 태그 레일 ───────────────────────────────────────────────────────────

    /// <summary>
    /// 조건 전환·Set이 있는 줄에만 만드는 메타데이터 레일. 태그가 없으면 null —
    /// 그 줄은 추가 높이를 전혀 갖지 않는다.
    /// </summary>
    private Control? BuildTagRail(DialogueNode node, ResolvedLine resolved)
    {
        var rail = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(94, 0, 0, 1)
        };

        if (resolved.Line.Transition is not null)
        {
            rail.Children.Add(BuildTransitionTag(node, resolved));
        }

        IReadOnlyList<SetOperation> sets = resolved.Line.Sets;

        if (sets.Count > 0)
        {
            IReadOnlyList<VariableAssignment> registered = RegisteredVariables(node);

            for (int operationIndex = 0; operationIndex < sets.Count; operationIndex++)
            {
                rail.Children.Add(BuildSetTag(
                    node, resolved.Line.LineId, registered, operationIndex, sets[operationIndex]));
            }
        }

        return rail.Children.Count > 0 ? rail : null;
    }

    private static Button TagButton(string text, IBrush background)
    {
        return new Button
        {
            Content = new TextBlock
            {
                Text = text,
                FontSize = 9,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White
            },
            Background = background,
            Padding = new Thickness(6, 1),
            CornerRadius = new CornerRadius(3),
            MinHeight = 0,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private string ConditionLabelOf(DialogueNode node, string conditionId)
    {
        AvailableConditionCatalog available = AvailableConditionResolver.Resolve(
            _session!.Project, node.Id, _session.Definition);
        AvailableCondition? condition = available.Find(conditionId);
        AvailableCondition? known = condition ?? AvailableConditionResolver.FindKnown(
            _session.Project, _session.Definition, conditionId);

        return condition is not null
            ? condition.DisplayName
            : known is not null
                ? AvailableConditionResolver.UnavailableLabel(known, conditionId)
                : "알 수 없는 조건";
    }

    /// <summary>이 줄의 전환 태그. 누르면 기존 조건 드롭다운이 Flyout으로 열린다.</summary>
    private Control BuildTransitionTag(DialogueNode node, ResolvedLine resolved)
    {
        var neutral = new SolidColorBrush(Color.FromArgb(160, 107, 114, 128));
        var amber = new SolidColorBrush(Color.FromArgb(220, 217, 119, 6));

        (string label, IBrush background) = resolved.Line.Transition!.Kind switch
        {
            ConditionTransitionKind.BeginIf or ConditionTransitionKind.BeginElseIf =>
                (ConditionLabelOf(node, resolved.Branch?.ConditionId ?? string.Empty),
                    resolved.Branch is { } branch ? BranchPalette.Accent(branch.PaletteIndex) : neutral),
            ConditionTransitionKind.BeginChoice or ConditionTransitionKind.BeginNextOption =>
                ($"선택 {(resolved.Branch?.BranchIndexInChain ?? 0) + 1}", (IBrush)amber),
            ConditionTransitionKind.EndChoice => ("선택지 끝", neutral),
            _ => ("조건 종료", neutral)
        };

        Button tag = TagButton(label, background);
        ToolTip.SetTip(tag, "누르면 이 줄의 조건·선택 전환을 고칩니다.");

        tag.Click += (_, _) =>
        {
            var panel = new StackPanel { Spacing = 4, MinWidth = 220 };
            panel.Children.Add(new TextBlock
            {
                Text = "이 줄의 조건 / 선택 전환",
                FontSize = 10,
                Opacity = 0.6
            });
            panel.Children.Add(BuildConditionBox(node, resolved));
            new Flyout { Content = panel }.ShowAt(tag);
        };

        return tag;
    }

    /// <summary>set 태그 하나. 누르면 기존 변수·연산자·값 편집 행이 Flyout으로 열린다.</summary>
    private Control BuildSetTag(
        DialogueNode node,
        string lineId,
        IReadOnlyList<VariableAssignment> registered,
        int operationIndex,
        SetOperation operation)
    {
        Button tag = TagButton(
            $"set {operation.Variable} {SetOperators.Symbol(operation.Operator)} {operation.Value}",
            new SolidColorBrush(Color.FromArgb(200, 37, 99, 235)));
        ToolTip.SetTip(tag, "누르면 이 <<set>>을 고치거나 지웁니다.");

        tag.Click += (_, _) =>
        {
            var panel = new StackPanel { Spacing = 4, MinWidth = 300 };
            panel.Children.Add(new TextBlock
            {
                Text = "이 줄에 도달했을 때 실행할 <<set>>",
                FontSize = 10,
                Opacity = 0.6
            });
            panel.Children.Add(BuildSetOperationRow(node, lineId, registered, operationIndex, operation));
            new Flyout { Content = panel }.ShowAt(tag);
        };

        return tag;
    }

    // ── ＋ (메타데이터 추가) ─────────────────────────────────────────────────

    /// <summary>
    /// 행 오른쪽의 ＋ — 줄 추가가 아니라 <b>이 줄에</b> 조건·선택지·Set·출구를 더하는
    /// 버튼이다. 항목은 전부 기존 데이터 구조와 기존 편집 컨트롤을 그대로 쓴다.
    /// </summary>
    private Button BuildPlusButton(DialogueNode node, DialogueScript script, ResolvedLine resolved)
    {
        var plus = new Button
        {
            Content = "＋",
            FontSize = 11,
            Padding = new Thickness(5, 1),
            MinHeight = 0,
            Margin = new Thickness(2, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(plus, "이 줄에 조건·선택지·Set·출구를 더하거나 고칩니다.");
        plus.Click += (_, _) => OpenLineMetaFlyout(plus, node, script, resolved);
        return plus;
    }

    private void OpenLineMetaFlyout(
        Control anchor,
        DialogueNode node,
        DialogueScript script,
        ResolvedLine resolved)
    {
        var panel = new StackPanel { Spacing = 4, MinWidth = 250 };

        void Section(string title)
        {
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 10,
                Opacity = 0.6,
                Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 6, 0, 0)
            });
        }

        // 조건 추가/변경·선택지 시작 — 무엇이 가능한지는 기존 ConditionChoices가 정한다.
        Section("조건 / 선택지");
        panel.Children.Add(BuildConditionBox(node, resolved));

        Section("Set");
        IReadOnlyList<VariableAssignment> registered = RegisteredVariables(node);
        var addSet = new Button { Content = "+ set", FontSize = 10, Padding = new Thickness(7, 2) };
        ToolTip.SetTip(addSet, "이 줄에 도달했을 때 실행할 <<set>>을 더합니다.");

        addSet.Click += (_, _) =>
        {
            if (_building)
            {
                return;
            }

            List<SetOperation> next = CurrentSets(node, resolved.Line.LineId);
            next.Add(new SetOperation
            {
                Variable = registered.FirstOrDefault()?.Variable ?? string.Empty,
                Operator = SetOperatorKind.Add,
                Value = "1"
            });
            _session!.Editor.SetLineSetOperations(node.Id, resolved.Line.LineId, next);
        };

        if (registered.Count == 0 && resolved.Line.Sets.Count == 0)
        {
            var setRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            setRow.Children.Add(addSet);
            setRow.Children.Add(new TextBlock
            {
                Text = "설정노드를 연결하고 변수를 등록하면 드롭다운으로 고릅니다.",
                FontSize = 10,
                Opacity = 0.55,
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(setRow);
        }
        else
        {
            panel.Children.Add(addSet);
        }

        // 출구는 선택·조건 갈래의 마지막 줄에서만 매달 수 있다.
        if (resolved.Branch is { } branch && resolved.Index == branch.LastLineIndex)
        {
            Section("출구 (이 갈래 끝에서 점프)");
            panel.Children.Add(BuildExitEditor(node, branch));
        }

        // ▲▼✕ 행 버튼이 사라진 자리의 대체 진입점 — 행에서는 치웠지만 기능은 남긴다.
        Section("줄");
        string scriptId = script.ScriptId ?? string.Empty;
        string lineId = resolved.Line.LineId;
        var lineRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        lineRow.Children.Add(SmallButton("▲ 위로", () => _session!.Editor.MoveScriptLine(scriptId, lineId, -1)));
        lineRow.Children.Add(SmallButton("▼ 아래로", () => _session!.Editor.MoveScriptLine(scriptId, lineId, 1)));
        Button remove = SmallButton("✕ 삭제", () => _session!.Editor.RetireScriptLine(scriptId, lineId));
        ToolTip.SetTip(remove, "대본에서 이 줄을 뺍니다. LineId는 은퇴 상태로 남습니다.");
        lineRow.Children.Add(remove);
        panel.Children.Add(lineRow);

        new Flyout { Content = panel }.ShowAt(anchor);
    }

    // ── 출구 레일 ───────────────────────────────────────────────────────────

    /// <summary>
    /// 갈래 출구가 있는 줄 아래의 출구 레일. 표시만 하면 기능이 퇴보한다 —
    /// 누르면 출구 편집이 열린다.
    /// </summary>
    private Control BuildExitRail(DialogueNode node, ConditionBranch branch)
    {
        StoryNode? target = _session!.Project.FindNode(branch.ExitTargetNodeId);

        var button = new Button
        {
            Content = new TextBlock
            {
                Text = $"→ 분기 이후: {target?.Name ?? "(사라진 노드)"}",
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = BranchPalette.Accent(branch.PaletteIndex)
            },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2, 0),
            MinHeight = 0,
            Margin = new Thickness(92, 0, 0, 0)
        };
        ToolTip.SetTip(button, "누르면 이 갈래의 출구를 고칩니다.");

        button.Click += (_, _) =>
        {
            var panel = new StackPanel { Spacing = 4, MinWidth = 240 };
            panel.Children.Add(new TextBlock
            {
                Text = "출구 (이 갈래 끝에서 점프)",
                FontSize = 10,
                Opacity = 0.6
            });
            panel.Children.Add(BuildExitEditor(node, branch));
            new Flyout { Content = panel }.ShowAt(button);
        };

        return button;
    }

    /// <summary>갈래 출구 편집 — 그래프의 분기 간선과 같은 SetExitTarget 하나를 부른다.</summary>
    private Control BuildExitEditor(DialogueNode node, ConditionBranch branch)
    {
        List<StoryNode> targets = _session!.Project.EnumerateNodes()
            .Where(other => !string.Equals(other.Id, node.Id, StringComparison.Ordinal))
            .Where(other => other is not PresentationNode)
            .ToList();

        var combo = new ComboBox
        {
            ItemsSource = targets.Select(candidate => candidate.Name).ToList(),
            SelectedIndex = targets.FindIndex(candidate =>
                string.Equals(candidate.Id, branch.ExitTargetNodeId, StringComparison.Ordinal)),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "대상 노드"
        };

        combo.SelectionChanged += (_, _) =>
        {
            if (!_building && combo.SelectedIndex >= 0 && combo.SelectedIndex < targets.Count)
            {
                _session.Editor.SetExitTarget(
                    node.Id, ExitPortKind.Branch, branch.OpenLineId, targets[combo.SelectedIndex].Id);
            }
        };

        var clear = new Button
        {
            Content = "해제",
            FontSize = 10,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        clear.Click += (_, _) =>
            _session!.Editor.SetExitTarget(node.Id, ExitPortKind.Branch, branch.OpenLineId, null);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(combo, 0);
        Grid.SetColumn(clear, 1);
        row.Children.Add(combo);
        row.Children.Add(clear);
        return row;
    }

    /// <summary>
    /// 조건 드롭다운. "이 줄이 조건에 포함되는가"가 아니라
    /// "이 줄에서 조건 흐름을 바꿀 것인가"를 고르는 자리다.
    /// 무엇을 보여 줄지는 <see cref="ConditionChoices"/>가 정한다.
    /// </summary>
    private ComboBox BuildConditionBox(DialogueNode node, ResolvedLine resolved)
    {
        IReadOnlyList<ConditionChoice> choices = ConditionChoices.For(
            resolved.PrecedingBranch,
            node,
            _session!.Project,
            _session.Definition,
            resolved.Line.Transition);

        var box = new ComboBox
        {
            Margin = new Thickness(8, 0),
            FontSize = 11,
            MinWidth = 150,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = choices.Select(choice => choice.Label).ToList(),
            SelectedIndex = IndexOfChoice(choices, ConditionChoices.Current(choices, resolved.Line.Transition))
        };

        box.SelectionChanged += (_, _) =>
        {
            if (_building || box.SelectedIndex < 0 || box.SelectedIndex >= choices.Count)
            {
                return;
            }

            ConditionChoice picked = choices[box.SelectedIndex];

            if (picked == ConditionChoices.Current(choices, resolved.Line.Transition))
            {
                return;
            }

            _session.Editor.SetLineTransition(node.Id, resolved.Line.LineId, picked.ToTransition());
        };

        return box;
    }

    /// <summary>
    /// 이 대사 노드가 쓸 수 있는 변수 — 연결된 설정노드(Settings link)의 등록 목록이다.
    /// 조건 드롭다운과 같은 해석기(<see cref="ConnectedSetNodeResolver"/>)를 지난다.
    /// </summary>
    private IReadOnlyList<VariableAssignment> RegisteredVariables(DialogueNode node)
    {
        return ConnectedSetNodeResolver.Resolve(_session!.Project, node.Id)
            .SelectMany(connected => connected.Node.Assignments)
            .Where(assignment => assignment.Variable.Length > 0)
            .GroupBy(assignment => assignment.Variable, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    private List<SetOperation> CurrentSets(DialogueNode node, string lineId)
    {
        return node.LineExtensions
            .FirstOrDefault(extension => string.Equals(extension.LineId, lineId, StringComparison.Ordinal))
            ?.SetOperations.Select(operation => operation.Clone()).ToList() ?? new List<SetOperation>();
    }

    /// <summary>
    /// set 편집 (X6) — 타이핑 대신 등록 변수 드롭다운 + 슬라이더.
    /// 슬라이더 범위는 설정노드의 변수별 등록(기본 -5~+5)이고 편의일 뿐이라
    /// 옆의 직접 입력으로 범위 밖 값도 넣을 수 있다. 저장되는 것은 값 문자열
    /// 그대로이므로 <c>&lt;&lt;set&gt;&gt;</c> 출력은 바이트 단위로 불변이다.
    /// 고밀도 개편 후에는 태그 Flyout 안에서 열린다.
    /// </summary>
    private Control BuildSetOperationRow(
        DialogueNode node,
        string lineId,
        IReadOnlyList<VariableAssignment> registered,
        int operationIndex,
        SetOperation operation)
    {
        // 드롭다운에는 등록 변수만 나온다 (X6 수용). 이미 적혀 있는 미등록 변수는
        // 그 행에서만 '(미등록)'으로 보인다 — 조용히 지우지 않는다.
        var choices = registered.Select(item => (item.Variable, Label: item.Variable)).ToList();
        VariableAssignment? registration = registered.FirstOrDefault(item =>
            string.Equals(item.Variable, operation.Variable, StringComparison.Ordinal));

        if (registration is null && operation.Variable.Length > 0)
        {
            choices.Insert(0, (operation.Variable, Label: $"{operation.Variable} (미등록)"));
        }

        void Commit(Action<SetOperation> mutate)
        {
            if (_building)
            {
                return;
            }

            List<SetOperation> next = CurrentSets(node, lineId);

            if (operationIndex < next.Count)
            {
                mutate(next[operationIndex]);
                _session!.Editor.SetLineSetOperations(node.Id, lineId, next);
            }
        }

        var variableBox = new ComboBox
        {
            ItemsSource = choices.Select(choice => choice.Label).ToList(),
            SelectedIndex = choices.FindIndex(choice =>
                string.Equals(choice.Variable, operation.Variable, StringComparison.Ordinal)),
            FontSize = 11,
            MinWidth = 110,
            PlaceholderText = choices.Count == 0 ? "등록 변수 없음" : "변수"
        };

        variableBox.SelectionChanged += (_, _) =>
        {
            if (variableBox.SelectedIndex >= 0 && variableBox.SelectedIndex < choices.Count)
            {
                Commit(target => target.Variable = choices[variableBox.SelectedIndex].Variable);
            }
        };

        string[] operators = ["=", "+=", "-="];
        var operatorBox = new ComboBox
        {
            ItemsSource = operators,
            SelectedIndex = Array.IndexOf(operators, SetOperators.Symbol(operation.Operator)),
            FontSize = 11,
            Margin = new Thickness(4, 0, 0, 0)
        };

        operatorBox.SelectionChanged += (_, _) =>
        {
            if (operatorBox.SelectedIndex >= 0)
            {
                Commit(target => target.Operator = SetOperators.Parse(operators[operatorBox.SelectedIndex]));
            }
        };

        // Bool 플래그는 수치 슬라이더가 아니라 On/Off 토글이다 (X7).
        // 저장 값은 Yarn 문법 그대로 true/false 문자열 — 출력 불변.
        bool isBool = registration?.IsBool == true ||
            _session!.Definition.Variables.Any(spec =>
                string.Equals(spec.Name, operation.Variable, StringComparison.Ordinal) &&
                string.Equals(spec.Type, "bool", StringComparison.OrdinalIgnoreCase));

        if (isBool)
        {
            var toggle = new CheckBox
            {
                IsChecked = string.Equals(operation.Value, "true", StringComparison.OrdinalIgnoreCase),
                Content = string.Equals(operation.Value, "true", StringComparison.OrdinalIgnoreCase) ? "On" : "Off",
                Margin = new Thickness(6, 0, 0, 0),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };

            toggle.IsCheckedChanged += (_, _) =>
            {
                if (!_building)
                {
                    toggle.Content = toggle.IsChecked == true ? "On" : "Off";
                    Commit(target => target.Value = toggle.IsChecked == true ? "true" : "false");
                }
            };

            var removeBool = new Button
            {
                Content = "✕",
                FontSize = 10,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            removeBool.Click += (_, _) =>
            {
                if (!_building)
                {
                    List<SetOperation> nextOps = CurrentSets(node, lineId);

                    if (operationIndex < nextOps.Count)
                    {
                        nextOps.RemoveAt(operationIndex);
                        _session!.Editor.SetLineSetOperations(node.Id, lineId, nextOps);
                    }
                }
            };

            var boolRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto") };
            Grid.SetColumn(variableBox, 0);
            Grid.SetColumn(operatorBox, 1);
            Grid.SetColumn(toggle, 2);
            Grid.SetColumn(removeBool, 3);
            boolRow.Children.Add(variableBox);
            boolRow.Children.Add(operatorBox);
            boolRow.Children.Add(toggle);
            boolRow.Children.Add(removeBool);
            return boolRow;
        }

        double min = registration?.EffectiveSliderMin ?? VariableAssignment.DefaultSliderMin;
        double max = registration?.EffectiveSliderMax ?? VariableAssignment.DefaultSliderMax;
        bool syncing = false;

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(6, 0, 0, 0),
            MinWidth = 90,
            VerticalAlignment = VerticalAlignment.Center
        };

        var valueBox = new TextBox
        {
            Text = operation.Value,
            FontSize = 11,
            Width = 56,
            Margin = new Thickness(4, 0, 0, 0)
        };
        ToolTip.SetTip(valueBox, $"직접 입력 — 슬라이더 범위({FormatNumber(min)}~{FormatNumber(max)}) 밖 값도 됩니다.");

        if (double.TryParse(
                operation.Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double numeric))
        {
            slider.Value = Math.Clamp(numeric, min, max);
        }

        // 드래그 중에는 숫자 표시만 따라오고, 커밋은 놓는 순간 한 번이다 —
        // 커밋이 편집 행을 다시 만들므로(Structure) 틱마다 커밋하면 드래그가 끊긴다.
        slider.ValueChanged += (_, args) =>
        {
            if (!syncing && !_building)
            {
                syncing = true;
                valueBox.Text = FormatNumber(args.NewValue);
                syncing = false;
            }
        };

        slider.PointerCaptureLost += (_, _) =>
        {
            if (!syncing && !_building)
            {
                syncing = true;
                string formatted = FormatNumber(slider.Value);
                valueBox.Text = formatted;
                Commit(target => target.Value = formatted);
                syncing = false;
            }
        };

        valueBox.LostFocus += (_, _) =>
        {
            if (syncing || _building)
            {
                return;
            }

            syncing = true;
            string text = valueBox.Text ?? string.Empty;

            if (double.TryParse(
                    text,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double typed))
            {
                slider.Value = Math.Clamp(typed, min, max); // 슬라이더는 편의 — 값은 그대로 저장
            }

            Commit(target => target.Value = text);
            syncing = false;
        };

        var remove = new Button
        {
            Content = "✕",
            FontSize = 10,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        remove.Click += (_, _) =>
        {
            if (_building)
            {
                return;
            }

            List<SetOperation> next = CurrentSets(node, lineId);

            if (operationIndex < next.Count)
            {
                next.RemoveAt(operationIndex);
                _session!.Editor.SetLineSetOperations(node.Id, lineId, next);
            }
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto") };
        Grid.SetColumn(variableBox, 0);
        Grid.SetColumn(operatorBox, 1);
        Grid.SetColumn(slider, 2);
        Grid.SetColumn(valueBox, 3);
        Grid.SetColumn(remove, 4);
        row.Children.Add(variableBox);
        row.Children.Add(operatorBox);
        row.Children.Add(slider);
        row.Children.Add(valueBox);
        row.Children.Add(remove);

        return row;
    }

    private static string FormatNumber(double value) =>
        value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// 대본에서 사라진 줄에 남은 조건 데이터. 자동으로 지우지 않으므로 눈에 보여야 한다.
    /// </summary>
    private void AddOrphanCards(DialogueNode node, DialogueScript script)
    {
        IReadOnlyList<OrphanLineExtension> orphans = script.Orphans
            .Where(orphan => !orphan.Extension.IsEmpty)
            .ToArray();

        if (orphans.Count == 0)
        {
            return;
        }

        LineHost.Children.Add(new TextBlock
        {
            Text = "대본에서 사라진 줄에 남은 조건",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 10, 0, 0)
        });

        foreach (OrphanLineExtension orphan in orphans)
        {
            var content = new StackPanel { Spacing = 3 };

            content.Children.Add(new TextBlock
            {
                Text = orphan.Extension.LineId,
                FontSize = 11,
                Opacity = 0.7
            });

            content.Children.Add(new TextBlock
            {
                Text = orphan.LastKnownText is { } text
                    ? $"마지막 내용: {text.Speaker}: {text.Text}"
                    : "이 대본에 없는 LineId입니다.",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });

            content.Children.Add(SmallButton(
                "조건 지우기",
                () => _session!.Editor.SetLineTransition(node.Id, orphan.Extension.LineId, null)));

            LineHost.Children.Add(new Border
            {
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(20, 220, 38, 38)),
                Child = content
            });
        }
    }

    // ── 발행 ────────────────────────────────────────────────────────────────

    private void BuildResults(DialogueNode node)
    {
        ResultHost.Children.Clear();

        DialogueDraft draft = _session!.Editor.InspectDialoguePublish(node.Id, _session.Definition);
        PublishButton.IsEnabled = draft.CanPublish;
        PublishStatusText.Text = draft.CanPublish
            ? "발행할 수 있습니다."
            : draft.BlockingSummary();

        foreach (DialogueResult result in _session.Project.Results.DialogueResultsOf(node.Id).Reverse())
        {
            var content = new StackPanel { Spacing = 2 };

            content.Children.Add(new TextBlock
            {
                Text = $"v{result.Identity.Version} · {result.Lines.Count}줄 · {result.Locale}",
                FontWeight = FontWeight.SemiBold
            });

            content.Children.Add(new TextBlock
            {
                Text = $"{result.Identity.ResultId} · {result.Identity.ContentHash[..19]}…",
                FontSize = 10,
                Opacity = 0.6
            });

            content.Children.Add(new TextBlock
            {
                Text = result.PublishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                FontSize = 10,
                Opacity = 0.6
            });

            ResultHost.Children.Add(new Border
            {
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(45, 128, 128, 128)),
                Child = content
            });
        }

        if (ResultHost.Children.Count == 0)
        {
            ResultHost.Children.Add(new TextBlock
            {
                Text = "아직 발행한 결과가 없습니다.",
                Opacity = 0.6
            });
        }
    }

    private void Publish()
    {
        if (_session is null || _nodeId is null)
        {
            return;
        }

        try
        {
            PublishOutcome<DialogueResult> outcome = _session.Editor.PublishDialogue(
                _nodeId,
                _session.Definition);

            _session.SetStatus(outcome.Created
                ? $"{outcome.Result.Identity.Label}을 발행했습니다."
                : $"내용이 같아 {outcome.Result.Identity.Label}을 그대로 사용합니다.");
        }
        catch (PublishRejectedException exception)
        {
            _session.SetStatus(exception.Message.Replace(Environment.NewLine, " ", StringComparison.Ordinal));
        }
    }

    // ── 기본 출구 ───────────────────────────────────────────────────────────

    private void BuildDefaultExit(DialogueNode node)
    {
        List<StoryNode> targets = _session!.Project.EnumerateNodes()
            .Where(other => !string.Equals(other.Id, node.Id, StringComparison.Ordinal))
            .Where(other => other is not PresentationNode)
            .ToList();

        DefaultExitCombo.ItemsSource = targets.Select(target => target.Name).ToList();
        DefaultExitCombo.Tag = targets;

        bool connected = node.DefaultExitTargetNodeId is not null;
        DefaultExitCheck.IsChecked = connected;
        DefaultExitCombo.IsEnabled = connected;

        DefaultExitCombo.SelectedIndex = connected
            ? targets.FindIndex(target => string.Equals(target.Id, node.DefaultExitTargetNodeId, StringComparison.Ordinal))
            : -1;
    }

    private void OnDefaultExitToggled()
    {
        if (_building || _session is null || _nodeId is null)
        {
            return;
        }

        DefaultExitCombo.IsEnabled = DefaultExitCheck.IsChecked == true;

        if (DefaultExitCheck.IsChecked != true)
        {
            _session.Editor.SetExitTarget(_nodeId, ExitPortKind.Default, null, null);
        }
    }

    private void OnDefaultExitSelected()
    {
        if (_building ||
            _session is null ||
            _nodeId is null ||
            DefaultExitCombo.Tag is not List<StoryNode> targets ||
            DefaultExitCombo.SelectedIndex < 0 ||
            DefaultExitCombo.SelectedIndex >= targets.Count)
        {
            return;
        }

        _session.Editor.SetExitTarget(
            _nodeId,
            ExitPortKind.Default,
            null,
            targets[DefaultExitCombo.SelectedIndex].Id);
    }

    // ── 그 밖 ───────────────────────────────────────────────────────────────

    private void ShowProblems(DialogueFlow flow)
    {
        if (flow.Problems.Count == 0)
        {
            ProblemText.IsVisible = false;
            return;
        }

        ProblemText.IsVisible = true;
        ProblemText.Text = string.Join(
            Environment.NewLine,
            flow.Problems.Select(problem => "• " + problem.Message).Distinct());
    }

    private void CommitName()
    {
        if (!_building && _session is not null && _nodeId is not null)
        {
            _session.Editor.RenameNode(_nodeId, NameBox.Text ?? string.Empty);
        }
    }

    private static int IndexOfChoice(IReadOnlyList<ConditionChoice> choices, ConditionChoice choice)
    {
        for (int index = 0; index < choices.Count; index++)
        {
            if (choices[index] == choice)
            {
                return index;
            }
        }

        return 0;
    }

    // ── 노드 단위 내보내기 ──────────────────────────────────────────────────

    /// <summary>
    /// 이 대사 노드의 내보내기 짝(연출 공급 연결에서 계산) 상태를 보여 준다.
    /// </summary>
    private void RefreshExportState(DialogueNode node)
    {
        // 발행은 게이트가 아니다 (D-2). 내보내기 상태는 라이브 합성과 같은 계산이다.
        LiveComposition composition = LiveNodeComposer.Compose(
            _session!.Project, node.Id, _session.Definition, DateTimeOffset.UtcNow);

        ExportNodeButton.IsEnabled = composition.CanWrite;
        ExportNodeCsvButton.IsEnabled = composition.CanWrite;

        if (!composition.CanWrite)
        {
            ExportPairText.Text = string.Join(" / ", composition.BlockingProblems);
            return;
        }

        string pair = composition.WorkingPresentation is not null
            ? "현재 대사 + 공급된 연출 (작업 중 상태)"
            : "현재 대사 (연출 공급 없음)";
        string warnings = composition.Warnings.Count > 0
            ? " · " + string.Join(" / ", composition.Warnings)
            : string.Empty;
        ExportPairText.Text = pair + warnings;
    }

    /// <summary>이 노드 하나만 폴더로 내보낸다. 전체·라이브 출력과 같은 길(LiveNodeComposer)을 지난다.</summary>
    private async Task ExportNodeAsync(bool csv)
    {
        if (_session is null || _nodeId is null)
        {
            return;
        }

        try
        {
            // 선택한 양식만 산출된다 (X13) — 노드 단위 내보내기도 같은 선택을 따른다.
            if (csv && !_session.Project.ExportFormats.AnyCsv)
            {
                _session.SetStatus("양식 선택에서 CSV가 전부 꺼져 있습니다. [양식…]에서 켜세요.");
                return;
            }

            if (!csv && !_session.Project.ExportFormats.YarnTrio)
            {
                _session.SetStatus("양식 선택에서 Yarn 트리오가 꺼져 있습니다. [양식…]에서 켜세요.");
                return;
            }

            // 발행은 게이트가 아니다 (D-2) — 라이브 출력과 같은 합성(현재 작업 상태 Freeze)이다.
            LiveComposition composition = LiveNodeComposer.Compose(
                _session.Project, _nodeId, _session.Definition, DateTimeOffset.UtcNow);

            if (!composition.CanWrite)
            {
                _session.SetStatus($"내보낼 수 없습니다. {string.Join(" / ", composition.BlockingProblems)}");
                return;
            }

            IStorageProvider? storage = TopLevel.GetTopLevel(this)?.StorageProvider;

            if (storage is null || !storage.CanPickFolder)
            {
                _session.SetStatus("이 환경에서는 폴더 선택 창을 열 수 없습니다.");
                return;
            }

            IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = csv ? "이 노드의 CSV를 내보낼 폴더" : "이 노드의 .yarn을 내보낼 폴더",
                    AllowMultiple = false
                });

            if (folders.Count == 0)
            {
                return;
            }

            IReadOnlyList<string> written;

            if (csv)
            {
                written = CsvBundleExporter.WriteTo(
                    CsvBundleExporter.Export(
                        composition.WorkingDialogue!,
                        composition.WorkingPresentation,
                        _session.Project,
                        _session.Definition),
                    folders[0].Path.LocalPath,
                    _session.Project.ExportFormats);
            }
            else
            {
                written = YarnBundleEmitter.WriteBundles(
                    new[] { composition.Bundle! },
                    folders[0].Path.LocalPath);
            }

            _session.SetStatus(
                $"{written.Count}개 파일을 내보냈습니다: " +
                string.Join(", ", written.Select(System.IO.Path.GetFileName)));
        }
        catch (Exception exception)
        {
            _session.SetStatus($"내보내기에 실패했습니다. {exception.Message}");
        }
    }

    private static Button SmallButton(string glyph, Action action)
    {
        var button = new Button
        {
            Content = glyph,
            FontSize = 10,
            Padding = new Thickness(6, 2),
            Margin = new Thickness(2, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        button.Click += (_, _) => action();
        return button;
    }
}
