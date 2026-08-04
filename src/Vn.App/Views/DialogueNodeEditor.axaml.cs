using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
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

    /// <summary>MainWindow가 꽂아 주는 공유 하단 무대 프리뷰.</summary>
    internal MiniStagePreview? StagePreview { get; set; }

    public DialogueNodeEditor()
    {
        InitializeComponent();

        NameBox.LostFocus += (_, _) => CommitName();
        AddLineButton.Click += (_, _) => AddLine();
        ImportScriptButton.Click += OnImportScriptClick;
        ScriptCombo.SelectionChanged += (_, _) => OnScriptSelected();
        PublishButton.Click += (_, _) => Publish();
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

            BuildScriptPicker(node);

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

            // 선택 블록은 조건과 달리 들여쓰기·색이 아니라 블록 전체를 감싸는 박스로 보여 준다.
            // 옵션 라벨과 본문 카드가 같은 박스 안에 순서대로 쌓인다.
            StackPanel? choiceBox = null;
            int choiceChain = -1;

            foreach (ResolvedLine line in flow.Lines)
            {
                bool inChoice = line.Branch is { IsChoice: true };

                if (inChoice && (choiceBox is null || line.Branch!.ChainIndex != choiceChain))
                {
                    choiceBox = new StackPanel { Spacing = 6 };
                    choiceChain = line.Branch!.ChainIndex;
                    choiceBox.Children.Add(new TextBlock
                    {
                        Text = "선택지",
                        FontSize = 11,
                        FontWeight = FontWeight.Bold,
                        Opacity = 0.75
                    });

                    LineHost.Children.Add(new Border
                    {
                        Padding = new Thickness(10),
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
                    Text = script.HasScript
                        ? "이 대본에는 아직 줄이 없습니다. '줄 추가'로 시작하거나 대본을 가져오세요."
                        : "대본을 고르거나 새로 가져오면 줄이 여기에 나타납니다.",
                    Opacity = 0.6,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            AddOrphanCards(node, script);
            BuildDefaultExit(node);
            BuildResults(node);
            RefreshExportState(node);
            ShowProblems(flow);
            RefreshPreviewCore(node);
        }
        finally
        {
            _building = false;
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
        PreviewBox.Text = DocumentPreviewFormatter.Format(_previewDocument);
        PreviewSummaryText.Text =
            $"{_selectedOutputPreset.DisplayName} · {_previewDocument.Segments.Count}개 Segment · " +
            "작업 중 미리 보기 (발행 결과가 아닙니다)";

        UpdateStagePreview(node);
    }

    /// <summary>클릭한 라인이 무대 프리뷰의 기준이 되도록 왼쪽 강조 띠로 감싼다.</summary>
    private Control WrapForStageSelection(Control card, string lineId)
    {
        var wrapper = new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = string.Equals(lineId, _selectedLineId, StringComparison.Ordinal)
                ? StageSelectionBrush
                : Brushes.Transparent,
            Padding = new Thickness(4, 0, 0, 0),
            Child = card
        };

        wrapper.AddHandler(
            PointerPressedEvent,
            (_, _) => SelectStageLine(lineId),
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        _stageLineCards[lineId] = wrapper;

        return wrapper;
    }

    private void SelectStageLine(string lineId)
    {
        if (string.Equals(_selectedLineId, lineId, StringComparison.Ordinal))
        {
            return;
        }

        _selectedLineId = lineId;

        foreach ((string cardLineId, Border card) in _stageLineCards)
        {
            card.BorderBrush = string.Equals(cardLineId, lineId, StringComparison.Ordinal)
                ? StageSelectionBrush
                : Brushes.Transparent;
        }

        if (_session?.Project.FindDialogue(_nodeId) is { } node)
        {
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

        // 스탯 HUD (X3): 작업 중 문서 기준 — 등록 초기값 + 선택 라인까지의 set 누적(문서 순서 근사).
        var setsUpToLine = new List<(string Variable, SetOperatorKind Operator, string Value)>();

        foreach (DialogueLine item in script.Lines)
        {
            setsUpToLine.AddRange(item.Sets.Select(operation =>
                (operation.Variable, operation.Operator, operation.Value)));

            if (selected is not null && string.Equals(item.LineId, selected.LineId, StringComparison.Ordinal))
            {
                break;
            }
        }

        IReadOnlyList<StatFold.StatValue> stats = StatFold.Fold(
            RegisteredVariables(node).Select(assignment => (assignment.Variable, assignment.Value)),
            setsUpToLine);

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
                Stats: stats));
            return;
        }

        // 연출은 자신이 읽은 발행본 기준으로 접는다. 지금 편집 중인 줄이 그 발행본에
        // 없다면(발행 후 추가된 줄 등) 그 사실을 숨기지 않고 알린다.
        string? notice = selected is not null && !export.Dialogue.ContainsLine(selected.LineId)
            ? "이 줄은 공급된 연출이 읽은 발행본에 없습니다. 문서 전체 기준 상태를 표시합니다."
            : null;

        MiniStageState state = MiniStageFold.Fold(
            PresentationCommandCatalog.For(_session.Definition),
            export.Presentation.SetupCommands,
            MiniStageFold.LinesUpTo(export.Dialogue, export.Presentation.Bindings, selected?.LineId));

        StagePreview.Show(new MiniStagePreviewRequest(
            contextLabel,
            state,
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
            Stats: stats));
    }

    /// <summary>프리뷰 창의 이전/다음. 선택은 이 편집기의 것 하나뿐이다.</summary>
    internal void MoveStageLine(int delta)
    {
        if (_session?.Project.FindDialogue(_nodeId) is not { } node)
        {
            return;
        }

        IReadOnlyList<DialogueLine> lines = DialogueScriptResolver.Resolve(_session.Project, node).Lines;

        if (lines.Count == 0)
        {
            return;
        }

        int index = lines.ToList().FindIndex(item =>
            string.Equals(item.LineId, _selectedLineId, StringComparison.Ordinal));
        int next = Math.Clamp((index < 0 ? 0 : index) + delta, 0, lines.Count - 1);

        SelectStageLine(lines[next].LineId);
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

    private void BuildScriptPicker(DialogueNode node)
    {
        List<ScriptDocument> scripts = _session!.Project.Scripts.ToList();

        ScriptCombo.ItemsSource = scripts.Select(script => script.Name).ToList();
        ScriptCombo.Tag = scripts;
        ScriptCombo.SelectedIndex = scripts.FindIndex(
            script => string.Equals(script.Id, node.ScriptId, StringComparison.Ordinal));

        ScriptDocument? current = _session.Project.FindScript(node.ScriptId);

        ScriptSummaryText.Text = current is null
            ? "이 장면이 읽을 대본이 없습니다. 화자와 대사는 대본이 소유합니다."
            : $"{current.ActiveLines.Count()}줄 · {current.PrimaryLocale} · " +
              $"동기화 {current.SourceRevision}회" +
              (current.SourcePath is null ? string.Empty : $" · {Path.GetFileName(current.SourcePath)}");

        AddLineButton.IsEnabled = current is not null;
        ImportScriptButton.IsEnabled = current is not null;
    }

    private void OnScriptSelected()
    {
        if (_building ||
            _session is null ||
            _nodeId is null ||
            ScriptCombo.Tag is not List<ScriptDocument> scripts ||
            ScriptCombo.SelectedIndex < 0 ||
            ScriptCombo.SelectedIndex >= scripts.Count)
        {
            return;
        }

        _session.Editor.SetDialogueScript(_nodeId, scripts[ScriptCombo.SelectedIndex].Id);
    }

    private void AddLine()
    {
        if (_session is null || _session.Project.FindDialogue(_nodeId) is not { ScriptId: { } scriptId })
        {
            return;
        }

        _session.Editor.InsertScriptLine(scriptId);
    }

    /// <summary>
    /// 작가의 평평한 대본 파일을 읽어 동기화한다.
    ///
    /// 계획을 먼저 세우고 확인이 필요한 항목이 있으면 <b>아무것도 바꾸지 않는다.</b>
    /// 도구가 대신 이어 붙이면 작가가 쓰지 않은 연출이 다른 대사에 붙는다.
    /// </summary>
    private async void OnImportScriptClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (_session is null ||
                _session.Project.FindDialogue(_nodeId) is not { ScriptId: { } scriptId })
            {
                return;
            }

            IStorageProvider? storage = TopLevel.GetTopLevel(this)?.StorageProvider;

            if (storage is null || !storage.CanOpen)
            {
                _session.SetStatus("이 환경에서는 파일 선택 창을 열 수 없습니다.");
                return;
            }

            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "평평한 대본 가져오기",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("대본 텍스트")
                        {
                            Patterns = new[] { "*.txt", "*.md", "*" }
                        }
                    }
                });

            if (files.Count == 0)
            {
                return;
            }

            string path = files[0].Path.LocalPath;
            string text = await File.ReadAllTextAsync(path, new UTF8Encoding(false));
            ScriptSyncPlan plan = _session.Editor.PlanScriptSync(scriptId, text, path);

            if (plan.HasConflicts)
            {
                _session.SetStatus(
                    $"확인이 필요해 대본을 반영하지 않았습니다. {plan.Summary()} — " +
                    (plan.Conflicts.First().Message ?? "확인 필요"));
                return;
            }

            _session.Editor.ApplyScriptSync(plan);

            string parseNote = plan.ParseProblems.Count == 0
                ? string.Empty
                : $" · 확인할 줄 {plan.ParseProblems.Count}개";
            _session.SetStatus($"{Path.GetFileName(path)}을 반영했습니다. {plan.Summary()}{parseNote}");
        }
        catch (Exception exception)
        {
            _session?.SetStatus($"대본을 가져오지 못했습니다. {exception.Message}");
        }
    }

    // ── 카드 ────────────────────────────────────────────────────────────────

    private Control BuildCard(DialogueNode node, DialogueScript script, ResolvedLine resolved)
    {
        ConditionBranch? branch = resolved.Branch;
        int palette = branch?.PaletteIndex ?? -1;

        var body = new StackPanel { Spacing = 6 };

        body.Children.Add(BuildHeader(node, script, resolved));
        body.Children.Add(BuildTextRow(script, resolved));
        body.Children.Add(BuildSetRow(node, resolved));

        if (resolved.IsBranchExit && branch is not null)
        {
            body.Children.Add(BuildExitBadge(branch));
        }

        if (branch is { IsChoice: true })
        {
            bool isLabel = resolved.Line.Transition?.OpensOption == true;

            if (isLabel)
            {
                // 라벨은 대사가 아니라 플레이어가 누르는 버튼이다 (X10).
                // 버튼처럼 그린다 — 아이콘 + 채워진 배경 + 둥근 모서리. 대사 줄과 한눈에 갈린다.
                var labelRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                var icon = new TextBlock
                {
                    Text = "▶",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(217, 119, 6)),
                    Margin = new Thickness(2, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                ToolTip.SetTip(icon, "선택지 버튼 텍스트 — 대사가 아닙니다.");
                Grid.SetColumn(icon, 0);
                Grid.SetColumn(body, 1);
                labelRow.Children.Add(icon);
                labelRow.Children.Add(body);

                return new Border
                {
                    Padding = new Thickness(10, 8),
                    CornerRadius = new CornerRadius(16),
                    BorderThickness = new Thickness(2),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(170, 217, 119, 6)),
                    Background = new SolidColorBrush(Color.FromArgb(45, 217, 119, 6)),
                    Child = labelRow
                };
            }

            // 선택 후 분기 대사는 일반 대사 줄과 똑같이 생겼다(화자 편집 포함).
            // 라벨 아래로 들여쓰기만 되어 소속을 보여 준다.
            return new Border
            {
                Margin = new Thickness(18, 0, 0, 0),
                Padding = new Thickness(10, 8),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
                Child = body
            };
        }

        var card = new Border
        {
            // 조건 안이면 오른쪽으로 한 단계 들어간다. 첫 버전의 깊이는 0 또는 1뿐이다.
            Margin = new Thickness(resolved.Depth * 28, 0, 0, 0),
            Padding = new Thickness(10, 8),
            CornerRadius = new CornerRadius(6),
            BorderThickness = branch is null
                ? new Thickness(1)
                : new Thickness(4, 1, 1, 1),
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

        return card;
    }

    private Control BuildHeader(DialogueNode node, DialogueScript script, ResolvedLine resolved)
    {
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto,Auto")
        };

        var index = new TextBlock
        {
            Text = resolved.Index.ToString(),
            FontSize = 11,
            Opacity = 0.5,
            MinWidth = 18,
            VerticalAlignment = VerticalAlignment.Center
        };

        ToolTip.SetTip(index, $"{resolved.Line.LineId} · rev {resolved.Line.Revision}");
        Grid.SetColumn(index, 0);
        header.Children.Add(index);

        // 지금 어느 갈래인지는 색이 아니라 글자로도 반드시 알 수 있어야 한다.
        if (resolved.Branch is { } branch)
        {
            string conditionLabel;

            if (branch.IsChoice)
            {
                conditionLabel = $"옵션 {branch.BranchIndexInChain + 1}";
            }
            else
            {
                AvailableConditionCatalog available = AvailableConditionResolver.Resolve(
                    _session!.Project,
                    node.Id,
                    _session.Definition);
                AvailableCondition? condition = available.Find(branch.ConditionId);
                AvailableCondition? known = condition ?? AvailableConditionResolver.FindKnown(
                    _session.Project,
                    _session.Definition,
                    branch.ConditionId);
                conditionLabel = condition is not null
                    ? condition.DisplayName
                    : known is not null
                        ? AvailableConditionResolver.UnavailableLabel(known, branch.ConditionId)
                        : "알 수 없는 조건";
            }

            var label = new Border
            {
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(6, 1),
                CornerRadius = new CornerRadius(3),
                Background = BranchPalette.Accent(branch.PaletteIndex),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = conditionLabel,
                    FontSize = 10,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White
                }
            };

            Grid.SetColumn(label, 1);
            header.Children.Add(label);
        }

        ComboBox conditionBox = BuildConditionBox(node, resolved);
        Grid.SetColumn(conditionBox, 2);
        header.Children.Add(conditionBox);

        string scriptId = script.ScriptId ?? string.Empty;
        string lineId = resolved.Line.LineId;

        Button up = SmallButton("▲", () => _session!.Editor.MoveScriptLine(scriptId, lineId, -1));
        Button down = SmallButton("▼", () => _session!.Editor.MoveScriptLine(scriptId, lineId, 1));
        Button remove = SmallButton("✕", () => _session!.Editor.RetireScriptLine(scriptId, lineId));
        ToolTip.SetTip(remove, "대본에서 이 줄을 뺍니다. LineId는 은퇴 상태로 남습니다.");

        Grid.SetColumn(up, 3);
        Grid.SetColumn(down, 4);
        Grid.SetColumn(remove, 5);
        header.Children.Add(up);
        header.Children.Add(down);
        header.Children.Add(remove);

        return header;
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
    /// 이 줄의 변수 변경. 한 줄에 하나씩 <c>변수 += 값</c> 형태로 적는다 (=, +=, -=).
    /// 발행하면 결과에 얼어붙고, 이미터가 Story의 <c>&lt;&lt;set&gt;&gt;</c>으로 낸다.
    /// </summary>
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
    /// </summary>
    private Control BuildSetRow(DialogueNode node, ResolvedLine resolved)
    {
        var host = new StackPanel { Spacing = 3 };
        IReadOnlyList<VariableAssignment> registered = RegisteredVariables(node);
        IReadOnlyList<SetOperation> sets = resolved.Line.Sets;

        for (int index = 0; index < sets.Count; index++)
        {
            host.Children.Add(BuildSetOperationRow(node, resolved.Line.LineId, registered, index, sets[index]));
        }

        var add = new Button { Content = "+ set", FontSize = 10, Padding = new Thickness(7, 2) };
        ToolTip.SetTip(add, "이 줄에 도달했을 때 실행할 <<set>>을 더합니다.");

        add.Click += (_, _) =>
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

        if (sets.Count == 0 && registered.Count == 0)
        {
            var hint = new TextBlock
            {
                Text = "설정노드를 연결하고 변수를 등록하면 여기서 드롭다운으로 고릅니다.",
                FontSize = 10,
                Opacity = 0.55,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            var addRow = new StackPanel { Orientation = Orientation.Horizontal };
            addRow.Children.Add(add);
            addRow.Children.Add(hint);
            host.Children.Add(addRow);
        }
        else
        {
            host.Children.Add(add);
        }

        return host;
    }

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

    private Control BuildTextRow(DialogueScript script, ResolvedLine resolved)
    {
        // 선택지 라벨은 화자가 없는 버튼 텍스트다 (X9). Speaker 입력 자체를 두지 않는다 —
        // 출력(`-> 라벨`)도 화자를 쓰지 않으므로 여기서 요구할 이유가 없다.
        bool isOptionLabel = resolved.Line.Transition?.OpensOption == true;

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(isOptionLabel ? "*" : "110,*")
        };

        AutoCompleteBox? speaker = null;

        var text = new TextBox
        {
            Text = resolved.Line.Text,
            PlaceholderText = isOptionLabel ? "선택지 라벨 (버튼 텍스트)" : "대사",
            AcceptsReturn = false,
            TextWrapping = TextWrapping.Wrap
        };

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

        if (isOptionLabel)
        {
            Grid.SetColumn(text, 0);
            row.Children.Add(text);
        }
        else
        {
            // 화자는 등록 목록(game.definition speakers)의 드롭다운 + 자유 입력 겸용이다 (X5).
            // 미등록 화자도 오류가 아니다 — 이름만 표시되는 기존 정책 그대로.
            speaker = new AutoCompleteBox
            {
                Text = resolved.Line.Speaker,
                PlaceholderText = "화자",
                FontSize = 12,
                ItemsSource = _session!.Definition.Speakers
                    .Select(item => item.Name)
                    .Where(item => item.Length > 0)
                    .ToList(),
                FilterMode = AutoCompleteFilterMode.Contains,
                MinimumPrefixLength = 0
            };
            speaker.TextChanged += (_, _) => Commit();
            text.Margin = new Thickness(6, 0, 0, 0);

            Grid.SetColumn(speaker, 0);
            Grid.SetColumn(text, 1);
            row.Children.Add(speaker);
            row.Children.Add(text);
        }

        text.TextChanged += (_, _) => Commit();

        return row;
    }

    /// <summary>
    /// 조건 출구 카드에 붙는 줄. 어느 조건에서 어디로 가는지 글자로 보여 준다.
    /// 색만으로는 "출구"라는 사실을 전달하지 않는다.
    /// </summary>
    private Control BuildExitBadge(ConditionBranch branch)
    {
        StoryNode? target = _session!.Project.FindNode(branch.ExitTargetNodeId);

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = "⇥",
                    FontWeight = FontWeight.Bold,
                    Foreground = BranchPalette.Accent(branch.PaletteIndex)
                },
                new TextBlock
                {
                    Text = $"분기 이동 → {target?.Name ?? "(사라진 노드)"}",
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold
                }
            }
        };
    }

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
        NodeExport export = NodeExportResolver.Resolve(_session!.Project, node.Id);

        ExportNodeButton.IsEnabled = export.CanExport;
        ExportNodeCsvButton.IsEnabled = export.CanExport;

        if (!export.CanExport)
        {
            ExportPairText.Text = export.ProblemSummary();
            return;
        }

        string pair = export.Presentation is { } presentation
            ? $"대사 v{export.Dialogue!.Identity.Version} + 연출 v{presentation.Identity.Version}"
            : $"대사 v{export.Dialogue!.Identity.Version} (연출 공급 없음)";
        string warnings = export.Problems.Count > 0
            ? " · " + export.ProblemSummary()
            : string.Empty;
        ExportPairText.Text = pair + warnings;
    }

    /// <summary>이 노드 하나만 폴더로 내보낸다. 전체 내보내기와 같은 길(NodeExportResolver)을 지난다.</summary>
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

            NodeExport export = NodeExportResolver.Resolve(_session.Project, _nodeId);

            if (!export.CanExport)
            {
                _session.SetStatus($"내보낼 수 없습니다. {export.ProblemSummary()}");
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
                        export.Dialogue!,
                        export.Presentation,
                        _session.Project,
                        _session.Definition),
                    folders[0].Path.LocalPath,
                    _session.Project.ExportFormats);
            }
            else
            {
                YarnBundle bundle = YarnBundleEmitter.Emit(
                    export.Dialogue!,
                    export.Presentation,
                    _session.Project,
                    _session.Definition);

                if (bundle.HasBlockingProblems)
                {
                    _session.SetStatus($"내보내지 못했습니다. {bundle.BlockingSummary()}");
                    return;
                }

                written = YarnBundleEmitter.WriteBundles(new[] { bundle }, folders[0].Path.LocalPath);
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
