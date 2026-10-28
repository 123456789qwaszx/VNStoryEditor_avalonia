using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Vn.App.Services;
using Vn.Authoring.Chapters;
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
    private static readonly SolidColorBrush LineSelectionBrush = new(Color.FromArgb(200, 37, 99, 235));

    private AuthoringSession? _session;
    private string? _nodeId;
    private bool _building;
    private RenderedDocument? _previewDocument;
    private readonly IReadOnlyList<OutputPreset> _outputPresets = OutputPresetCatalog.All;
    private OutputPreset _selectedOutputPreset = OutputPresetCatalog.RuntimeFull;
    private string? _selectedLineId;
    private readonly Dictionary<string, Border> _lineCards = new(StringComparer.Ordinal);

    /// <summary>
    /// 지금 보는 노드가 <b>챕터의 에피소드</b>인가 (<see cref="DialogueNode.MarkedEpisodeId"/>).
    ///
    /// ⛔ <b>이것은 더 이상 "엑셀이 소유한다"는 뜻이 아니다</b> (2026-09-16 · R-E · §6.4).
    /// 옛 이름은 <c>_excelOwned</c>였고 참이면 본문·화자·줄 구성이 통째로 읽기 전용이었다 —
    /// 여기서 고쳐도 <b>다음 동기화가 되돌린다</b>는 것이 이유였는데, 그 동기화가 R-D에서
    /// 철거됐다. 대본의 원본은 이제 프로젝트다. <b>그래서 대본은 전부 열렸다.</b>
    ///
    /// 남은 뜻은 좁다 — <b>챕터 워크북이 쥔 것</b>만이다. 셋뿐이고, 셋 다 대본이 아니다:
    /// ① 노드 이름(챕터 `대사엔트리`가 원천 — R-F에서 뒤집힌다)
    /// ② 스탯변화(챕터 간선의 것 — 2026-08-14 결정, 엑셀과 무관)
    /// ③ 갈래 출구 배선(연출 그래프 카드의 IF 포트 하나 — 2026-08-23 결정, 문은 하나)
    /// </summary>
    private bool _chapterEpisode;

    /// <summary>갈래 시작 줄(라벨·조건) → 블록/갈래 (W36-a). 클릭하면 그 갈래가 선택으로 남는다.</summary>
    private readonly Dictionary<string, (BranchFlow.Block Block, int BranchIndex)> _branchStarts =
        new(StringComparer.Ordinal);

    public DialogueNodeEditor()
    {
        InitializeComponent();

        NameBox.LostFocus += (_, _) => CommitName();
        AddLineButton.Click += (_, _) => AddLine();

        ApplyScenarioButton.Click += (_, _) => UiGuard.Run(_session, "텍스트 반영", ApplyScenario);

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

    /// <summary>
    /// <b>챕터가 쥔</b> 것을 여기서 고치려는 시도를 막고, 어디서 고치는지 말한다. 참이면
    /// 호출자는 그냥 돌아간다.
    ///
    /// ⛔ 옛 <c>BlockIfExcelOwned</c>가 막던 것 중 <b>대본에 속한 것은 전부 열렸다</b>
    /// (줄 추가·삭제·이동, 화자·내용, 텍스트 반영, 조건 태그 — R-E · §6.4). 남은 문은
    /// 챕터 흐름 하나뿐이고, 그것이 닫혀 있는 이유는 엑셀이 아니라 <b>문이 둘이면 안 되기</b>
    /// 때문이다: 갈래 출구는 연출 그래프 카드의 IF 포트에서만 잇고 뗀다(2026-08-23 소유자).
    /// </summary>
    private bool BlockIfChapterFlow(string what)
    {
        if (!_chapterEpisode)
        {
            return false;
        }

        _session?.SetStatus(
            $"{what}은(는) 연출 그래프 카드의 IF 갈래 포트에서 합니다 — 잇고 떼는 문은 하나입니다.");
        return true;
    }

    // 엑셀노드 안내 띠("📄 엑셀노드 — 원본: 에피소드 …")는 2026-08-22에 사라졌다 (소유자).
    // 그 시절 카드는 잠겨 있었고 띠는 그 사실을 매번 다시 알리는 소음이었다 — 2026-09-16에
    // 잠금 자체가 사라지면서 띠가 말하던 사실도 함께 사라졌다.

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

        panel.Children.Add(MenuSeparator());

        panel.Children.Add(new Button
        {
            Content = "삭제",
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
        SelectLine(line.Id);
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
            ClearPreview();
            return;
        }

        _building = true;

        try
        {
            _chapterEpisode = node.MarkedEpisodeId is not null;

            NameBox.Text = node.Name;

            // ⚠ 이름만은 아직 잠긴다 — 원천이 챕터 `대사엔트리`이고, 그 워크북의 주인은
            // 아직 기획자다(R-F에서 뒤집힌다). 개명은 [챕터 그래프]의 [이름] 칸에서 하고,
            // 그쪽은 워크북과 노드를 함께 간다. 여기서 갈면 둘이 어긋난다.
            //
            // ⛔ [줄 추가]·[텍스트 반영]을 함께 잠그던 두 줄은 2026-09-16에 사라졌다
            //    (R-E · §6.4) — 대본의 원본이 프로젝트가 됐으므로 잠글 것이 없다.
            NameBox.IsReadOnly = _chapterEpisode;

            BuildScriptSummary(node);

            DialogueScript script = DialogueScriptResolver.Resolve(_session.Project, node);
            DialogueFlow flow = ConditionFlowResolver.Resolve(
                node,
                script,
                _session.Project,
                _session.Definition);

            LineHost.Children.Clear();
            _lineCards.Clear();

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

                    // 지금 선택된 갈래를 헤더에 그대로 쓴다 (W36-a).
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
                        // 조건 갈래 안 선택지(W54)는 박스째 조건 깊이만큼 들어간다.
                        Margin = new Thickness((line.Depth - 1) * 20, 0, 0, 0),
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

                // 「조건 분기」 카드는 <b>줄 사이에</b> 낀다 (R7 P-5 · 2026-09-17 소유자) —
                // 그 줄에 붙은 것이 아니라 그 줄 <b>앞에서</b> 갈라진다는 표식이다.
                Control? marker = BuildBranchMarkerCard(node, line.Line.LineId);

                Control card = WrapForLineSelection(BuildCard(node, script, line), line.Line.LineId);

                if (inChoice && choiceBox is not null)
                {
                    if (marker is not null)
                    {
                        choiceBox.Children.Add(marker);
                    }

                    choiceBox.Children.Add(card);
                }
                else
                {
                    if (marker is not null)
                    {
                        LineHost.Children.Add(marker);
                    }

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
    /// 시작값 = 등록 초기값. 식은 노드 조건·게임 정의 조건에서 찾는다.
    /// </summary>
    private ConditionSimulation.Result SimulateBranches(DialogueNode node, DialogueScript script)
    {
        // ⛔ 작가 변수가 없어졌다 (2026-09-17) — 시뮬이 들고 시작할 값이 없다.
        //    조건이 묻는 것은 이제 챕터 스탯뿐이고, 그것은 판이 아니라 진행이 든다.
        var initial = new List<(string Variable, string Value)>();

        return ConditionSimulation.Decide(
            script.Lines,
            line => line.Transition?.Kind,
            line => line.LineId,
            line => line.Transition?.ConditionId is { } conditionId
                ? _session!.Project.FindCondition(conditionId)?.Expression
                    ?? _session.Definition.Conditions.FirstOrDefault(condition =>
                        string.Equals(condition.Id, conditionId, StringComparison.Ordinal))?.Expression
                : null,
            _ => Array.Empty<(string, SetOperatorKind, string)>(),
            initial,
            _session!.BranchSelection);
    }

    /// <summary>대본 라인의 갈래 분석 — 유효 선택(수동+자동)·커서 기준.</summary>
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
    /// 갈래 트리 상태 갱신 (W36-a) — 선택되지 않은 갈래의 카드는 흐려진다(실행에 안 타는
    /// 줄이 밝게 남으면 화면이 거짓말을 한다). 미선택(근사) 갈래는 기존 밝기 그대로다.
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
            if (!_lineCards.TryGetValue(line.Source.LineId, out Border? card) || card is null)
            {
                continue;
            }

            bool dimmed = !line.Taken && !line.Unresolved;
            card.Opacity = dimmed ? 0.42 : 1;
            ToolTip.SetTip(
                card,
                dimmed
                    ? "선택되지 않은 갈래 — 갈래 시작 줄(라벨·조건)을 클릭하면 이 갈래를 봅니다."
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

    /// <summary>클릭한 라인이 선택임을 왼쪽 강조 띠로 보인다 — 줄 추가 자리와 갈래 선택의 기준이다.</summary>
    private Control WrapForLineSelection(Control card, string lineId)
    {
        var wrapper = new Border
        {
            // Transparent는 보이지 않지만 히트테스트는 된다 — 배경 없는 일반 대사 카드의
            // 빈 부분을 클릭해도 선택이 되게 한다(글자 위만 반응하던 문제 수정, W53).
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = string.Equals(lineId, _selectedLineId, StringComparison.Ordinal)
                ? LineSelectionBrush
                : Brushes.Transparent,
            Padding = new Thickness(4, 0, 0, 0),
            Child = card
        };

        wrapper.AddHandler(
            PointerPressedEvent,
            (_, args) =>
            {
                SelectLine(lineId);

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
        _lineCards[lineId] = wrapper;

        return wrapper;
    }

    private static Control MenuSeparator() => new Border
    {
        Height = 1,
        Margin = new Thickness(4, 2),
        Background = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128))
    };

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
            UiGuard.Run(_session, "줄 추가", AddLine); // 선택된 줄 아래에 — 위의 SelectLine이 이미 골랐다
        };

        // 추가와 삭제 사이에 구분선 (소유자 지시) — 성격이 반대인 두 동작이 붙어 있으면 헷갈린다.
        panel.Children.Add(MenuSeparator());

        Button remove = Item("삭제", "대본에서 이 줄을 뺍니다. LineId는 은퇴 상태로 남습니다.");
        remove.Click += (_, _) =>
        {
            flyout.Hide();
            UiGuard.Run(_session, "줄 삭제", () => _session!.Editor.RetireScriptLine(scriptId, lineId));
        };

        // 마우스 지점에서 연다 (소유자 지시) — 카드 기준으로 뜨면 클릭한 곳과 멀어 헷갈린다.
        flyout.ShowAt(anchor, showAtPointer: true);
    }

    private void SelectLine(string lineId)
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

        foreach ((string cardLineId, Border card) in _lineCards)
        {
            card.BorderBrush = string.Equals(cardLineId, lineId, StringComparison.Ordinal)
                ? LineSelectionBrush
                : Brushes.Transparent;
        }

        if (_session?.Project.FindDialogue(_nodeId) is { } node)
        {
            RefreshBranchStates(node); // 커서 덮어쓰기·새 선택이 흐림 상태를 바꾼다
        }
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
    }

    // ── 대본 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 대본은 노드가 생성될 때 함께 만들어진다 (X4, D-3). 여기는 표시만 한다 —
    /// 가져오기·대본 선택 진입점은 제거됐고, 긴 외부 대본은 X12의 붙여넣기 경로가 맡는다.
    /// </summary>
    private void BuildScriptSummary(DialogueNode node)
    {
        ScriptDocument? current = _session!.Project.FindScript(node.ScriptId);

        // 머리글 오른쪽 한 줄에 든다 (2026-08-22) — 잘릴 수 있으므로 전문은 툴팁이 든다.
        string summary = current is null
            ? "대본 없음 — [줄 추가]로 만듭니다."
            : $"{current.ActiveLines.Count()}줄 · {current.PrimaryLocale}" +
              (current.SourcePath is null ? string.Empty : $" · {Path.GetFileName(current.SourcePath)}");

        ScriptSummaryText.Text = summary;
        ToolTip.SetTip(ScriptSummaryText, summary);
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

        // 새 줄이 새 선택이다.
        SelectLine(line.Id);
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
            Margin = new Thickness(4, 0, 0, 0),
            // ⛔ 본문을 읽기 전용으로 잠그던 자리다 (2026-08-24 ~ 2026-09-16). 원본이
            //    엑셀이던 시절의 규율이고, 이제 원본은 프로젝트다 — 늘 열려 있다.
        };

        AutoCompleteBox? speaker = null;
        string scriptId = script.ScriptId ?? string.Empty;

        // 글자가 바뀔 때마다 대본에 넣되, 그것이 카드 목록을 다시 만들지는 않는다.
        // 편집기가 내용 변경과 구조 변경을 구분해서 알리기 때문에 가능하다.
        void Commit()
        {
            if (_building || scriptId.Length == 0)
            {
                return;
            }

            string nextSpeaker = speaker?.Text ?? resolved.Line.Speaker;
            string nextText = text.Text ?? string.Empty;

            // ⛔ 여기 있던 "엑셀이 먼저다"는 2026-09-16에 걷혔다 (R-D). 고친 글을 <b>엑셀
            //    셀에 먼저 쓰고</b> 성공했을 때만 노드를 고치던 순서였는데, 그 순서의 이유가
            //    <i>"안 그러면 다음 동기화가 사람의 글을 지운다"</i>였다. 다시 읽는 동기화가
            //    없어졌으므로 지울 것도 없다 — 원본은 프로젝트다.
            _session!.Editor.SetScriptLineText(
                scriptId, resolved.Line.LineId, nextSpeaker, nextText, script.Locale);
        }

        // ⛔ 엑셀노드만 초점을 잃을 때 내던 갈래는 2026-09-16에 사라졌다 (R-E). 이유는
        //    "자판 하나마다 워크북 파일을 두드린다"였는데, 이제 글자는 프로젝트로 간다 —
        //    워크북은 [글 반영]/[텍스트 반영] 때 한 번 난다.
        void Wire(Control field)
        {
            if (field is TextBox box)
            {
                box.TextChanged += (_, _) => Commit();
            }
            else if (field is AutoCompleteBox auto)
            {
                auto.TextChanged += (_, _) => Commit();
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
                ItemsSource = SpeakerNames(),
                FilterMode = AutoCompleteFilterMode.Contains,
                MinimumPrefixLength = 0,
                // 화자도 본문과 함께 열렸다 (R-E · §6.4).
            };
            Wire(speaker);

            // 후보는 포커스 때마다 다시 읽는다 (W56) — 방금 등록한 화자가 카드 재생성
            // 없이도 자동완성에 바로 나온다.
            speaker.GotFocus += (_, _) => speaker.ItemsSource = SpeakerNames();

            // ▾ = 등록 화자 전체 목록 (W40) — 자동완성은 타이핑해야 열리니 클릭 한 번 길을 따로 둔다.
            //
            // ⛔ 엑셀노드에서 이 단추까지 잠그던 규율은 2026-09-16에 사라졌다 (R-E) —
            //    잠긴 칸이 없으니 "잠긴 칸에 값이 써지는" 사고도 없다.
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
            pick.Click += (_, _) => ShowSpeakerFlyout(pick, speakerBox, Commit);

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

        Wire(text);
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
    /// <param name="commit">
    /// 고른 이름을 실제로 내는 손 (2026-08-24). <b>있어야 한다</b> — 엑셀노드에서는 칸이
    /// 초점을 잃을 때만 저장하는데, ▾로 고르는 길에는 초점이 오간 적이 없어서 고른 이름이
    /// 칸에만 앉아 있다가 다음 다시 그리기에 지워진다.
    /// </param>
    private void ShowSpeakerFlyout(Button anchor, AutoCompleteBox speaker, Action commit)
    {
        List<string> candidates = SpeakerNames();

        if (candidates.Count == 0)
        {
            _session!.SetStatus(
                "등록된 화자가 없습니다 — 챕터 그래프의 [화자] 탭에서 더하면 여기 목록에 옵니다. " +
                "그때까지는 화자 칸에 직접 입력하세요.");
            return;
        }

        var panel = new StackPanel();
        var flyout = new Flyout
        {
            Content = new ScrollViewer { MaxHeight = 260, Content = panel },
            Placement = PlacementMode.Bottom
        };

        // 머리글이 없다 (2026-08-23 소유자: "기획등록 화자라고 표시되는 것도 치워줘").
        // 구역을 가르던 두 줄은 원천이 둘이던 시절의 것이고, 원천이 하나가 된 지금은
        // 이름 목록 위에 붙는 상표일 뿐이었다.
        foreach (string name in candidates)
        {
            var item = new Button
            {
                Content = name,
                FontSize = 11,
                Padding = new Thickness(10, 4),
                MinWidth = 140,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent
            };
            item.Click += (_, _) =>
            {
                speaker.Text = name;
                flyout.Hide();
                commit();
            };
            panel.Children.Add(item);
        }

        flyout.ShowAt(anchor);
    }

    /// <summary>
    /// 화자 후보 — <b>`game.definition.json`의 speakers 하나</b>가 원천이다 (2026-08-23
    /// 소유자: "화자는 툴 내부에서, 직접 정의해서 쓰는 게 맞는 것이였다"). 기획자가 챕터
    /// 그래프의 [화자] 탭에서 적고, 모든 챕터가 같은 목록을 본다.
    ///
    /// 출처를 갈라 세우던 두 구역(챕터 `화자` 시트 / 정의 파일)은 시트가 폐지되며 하나가
    /// 됐다. 여기 없는 이름도 화자 칸에 직접 적으면 그만이다 — 이 목록은 편의일 뿐이다.
    /// </summary>
    private List<string> SpeakerNames() =>
        _session?.Definition.Speakers
            .Select(item => item.Name)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];

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

        // ⛔ set 태그는 2026-09-17에 걷혔다 — 작가 변수가 없어져 줄에 달 값이 없다.

        return rail.Children.Count > 0 ? rail : null;
    }

    /// <summary>엑셀노드용 표시 전용 칩 — 태그 버튼과 같은 모양이되 누를 수 없다. 잠김이 모양으로 보인다.</summary>
    private static Border TagChip(string text, IBrush background) => new()
    {
        Child = new TextBlock
        {
            Text = text,
            FontSize = 9,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White
        },
        Background = background,
        Padding = new Thickness(6, 2),
        CornerRadius = new CornerRadius(3),
        Margin = new Thickness(0, 0, 4, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

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
            ? AvailableConditionResolver.LayeredLabel(condition)
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

        // ⛔ 엑셀노드에서 이 태그를 표시 전용 칩으로 내놓던 갈래는 2026-09-16에 사라졌다
        //    (R-E). 원본이 엑셀 E열이라 잠갔던 것인데, 대본 층의 조건은 R-C에서 규격
        //    자체가 은퇴했고(v15) 남은 것은 옛 프로젝트의 값이다 — 그 주인은 프로젝트다.
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

    // ── 「조건 분기」 카드 (R7 P-5) ──────────────────────────────────────────

    /// <summary>
    /// 그 줄 <b>앞</b>에 낀 「조건 분기」 카드. 표식이 없으면 <c>null</c>이다.
    ///
    /// ⛔ <b>내용이 없다.</b> 연출 그래프는 "어디서 갈라지는가"만 짚는다 (2026-09-17 소유자) —
    /// 구체적인 조건식도 선택지 문구도 여기서 정하지 않는다. 대본에는 <c>&lt;&lt;detour {노드}&gt;&gt;</c>
    /// 한 줄로 나가고, <c>&lt;&lt;if&gt;&gt;</c>가 없다.
    ///
    /// ⚠ 지우는 것은 <b>표식뿐</b>이다 — 다녀오던 에피소드는 판에 그대로 선다. 함께 지우면
    /// 거기 써 둔 대사가 한 번의 실수로 사라진다.
    /// </summary>
    private Control? BuildBranchMarkerCard(DialogueNode node, string lineId)
    {
        if (node.FindExtension(lineId)?.DetourTargetNodeId is not { Length: > 0 } targetId)
        {
            return null;
        }

        string name = _session?.Project.FindNode(targetId)?.Name ?? "(사라진 노드)";

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        row.Children.Add(new TextBlock
        {
            Text = "⑂ 조건 분기",
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });

        row.Children.Add(new TextBlock
        {
            Text = $"→ {name}",
            FontSize = 11,
            Opacity = 0.8,
            VerticalAlignment = VerticalAlignment.Center
        });

        row.Children.Add(new TextBlock
        {
            Text = "다녀왔다가 이 줄부터 이어집니다 — 조건은 다녀오는 곳의 첫머리에서 겁니다.",
            FontSize = 10,
            Opacity = 0.55,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        });

        Button remove = SmallButton("✕ 분기 제거", () =>
            _session!.Editor.SetExitTarget(node.Id, ExitPortKind.Detour, lineId, null));
        ToolTip.SetTip(remove, "표식만 지웁니다 — 다녀오던 에피소드는 판에 그대로 남습니다.");
        row.Children.Add(remove);

        return new Border
        {
            Tag = BranchMarkerTag(lineId),
            Margin = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(8, 4),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(Color.FromArgb(170, 124, 58, 237)),
            Background = new SolidColorBrush(Color.FromArgb(20, 124, 58, 237)),
            Child = row
        };
    }

    /// <summary>그 줄의 「조건 분기」 카드가 지는 표 — 화면 테스트가 이것으로 찾는다.</summary>
    internal static string BranchMarkerTag(string lineId) => $"branch:{lineId}";

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

        // ⭐ 「분기 추가」 (R7 P-5 · 2026-09-17 소유자) — <b>모든 노드에서</b> 선다.
        //
        // ⛔ 조건을 걸지 않는다. 이 줄 <b>앞</b>에 아무것도 없는 카드가 끼고, 연출 그래프의
        //    카드에 포트가 하나 뚫리며, 다녀올 에피소드가 함께 서서 이미 이어진 상태가 된다.
        //    조건이 성립하든 말든 다녀오고, 다녀온 곳이 제 첫머리에서 보고 아니면 곧바로
        //    돌아온다 — "어디서 갈라지는가"만 짚는 것이 연출 그래프의 몫이기 때문이다.
        Section("분기");

        if (resolved.Line.LineId is { Length: > 0 } markerLineId &&
            node.FindExtension(markerLineId)?.DetourTargetNodeId is { Length: > 0 })
        {
            panel.Children.Add(new TextBlock
            {
                Text = "이 자리에는 이미 분기가 있습니다 — 한 줄에 하나만 둘 수 있습니다.",
                FontSize = 10,
                Opacity = 0.6,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 250
            });
        }
        else
        {
            var addBranch = new Button
            {
                Content = "＋ 분기 추가",
                FontSize = 10,
                Padding = new Thickness(7, 2)
            };
            ToolTip.SetTip(
                addBranch,
                "이 줄 앞에서 갈라집니다. 다녀올 에피소드가 함께 서고 이미 이어진 상태입니다 — " +
                "조건은 그 에피소드의 첫머리에서 겁니다.");

            addBranch.Click += (_, _) =>
            {
                if (_building)
                {
                    return;
                }

                UiGuard.Run(_session!, "분기 추가", () =>
                    _session!.Editor.AddBranchMarker(node.Id, resolved.Line.LineId));
            };

            panel.Children.Add(addBranch);
        }

        if (_chapterEpisode)
        {
            // 조건·선택지는 열렸다(R-E). 남은 둘은 챕터의 것이다 — 간선의 스탯변화와 IF 포트.
            panel.Children.Add(new TextBlock
            {
                Text = "📄 챕터 에피소드 — 스탯변화는 간선에서, 갈래 출구는 카드의 IF 포트에서 고칩니다.",
                FontSize = 10,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 250
            });
        }
        else
        {
            // 조건 추가/변경·선택지 시작 — 무엇이 가능한지는 기존 ConditionChoices가 정한다.
            Section("조건 / 선택지");
            panel.Children.Add(BuildConditionBox(node, resolved));

            // ⛔ 「Set」 구역은 2026-09-17에 걷혔다 (소유자: *"작가 변수라는 개념 자체를
            //    지웁시다"*). 값이 변하는 자리는 이제 <b>챕터 간선의 `스탯변화`</b> 하나다.
        }

        // 출구는 선택·조건 갈래의 마지막 줄에서만 매달 수 있다.
        //
        // ⚠ 엑셀노드에서는 <b>어디로 가는지만</b> 말한다 (2026-08-23 소유자) — 잇고 떼는
        // 자리는 연출 그래프 카드의 IF 갈래 포트 하나다. 레일(→ 분기 이후)과 여기가 같은
        // `SetExitTarget`을 부르는 둘째·셋째 문이었다.
        if (resolved.Branch is { } branch && resolved.Index == branch.LastLineIndex)
        {
            Section("출구 (이 갈래 끝에서 점프)");

            if (_chapterEpisode)
            {
                StoryNode? wired = _session!.Project.FindNode(branch.ExitTargetNodeId);

                panel.Children.Add(new TextBlock
                {
                    Text = wired is null
                        ? "달린 씬이 없습니다 — 연출 그래프 카드의 IF 갈래 포트에서 답니다."
                        : $"'{wired.Name}' — 잇고 떼는 것은 연출 그래프 카드의 IF 갈래 포트에서 합니다.",
                    FontSize = 10,
                    Opacity = 0.7,
                    TextWrapping = TextWrapping.Wrap
                });
            }
            else
            {
                panel.Children.Add(BuildExitEditor(node, branch));
            }
        }

        // ▲▼✕ 행 버튼이 사라진 자리의 대체 진입점 — 행에서는 치웠지만 기능은 남긴다.
        // ⛔ 엑셀노드에서 이 구역을 통째로 안 내놓던 갈래는 2026-09-16에 사라졌다 (R-E) —
        //    줄 구성의 주인이 프로젝트가 됐으므로 모든 노드에서 선다.
        Section("줄");
        string scriptId = script.ScriptId ?? string.Empty;
        string lineId = resolved.Line.LineId;
        var lineRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        lineRow.Children.Add(SmallButton("▲ 위로", () =>
            _session!.Editor.MoveScriptLine(scriptId, lineId, -1)));
        lineRow.Children.Add(SmallButton("▼ 아래로", () =>
            _session!.Editor.MoveScriptLine(scriptId, lineId, 1)));
        Button remove = SmallButton("✕ 삭제", () =>
            _session!.Editor.RetireScriptLine(scriptId, lineId));
        ToolTip.SetTip(remove, "대본에서 이 줄을 뺍니다. LineId는 은퇴 상태로 남습니다.");
        lineRow.Children.Add(remove);
        panel.Children.Add(lineRow);

        new Flyout { Content = panel }.ShowAt(anchor);
    }

    // ── 출구 레일 ───────────────────────────────────────────────────────────

    /// <summary>
    /// 갈래 출구가 있는 줄 아래의 출구 레일. 표시만 하면 기능이 퇴보한다 —
    /// 누르면 출구 편집이 열린다.
    ///
    /// ⚠ <b>엑셀노드에서는 표시뿐이다</b> (2026-08-23 소유자: "이걸 클릭해서도 연결이 끊고
    /// 이어지는 기능이 있는데, 이 기능도 엑셀노드에서는 사용이 안되게 막아줘"). 배선을 잇고
    /// 떼는 자리는 <b>연출 그래프 카드의 IF 갈래 포트</b> 하나로 모은다 — 같은
    /// <c>SetExitTarget</c>을 부르는 문이 둘이면 어느 쪽이 정본인지 화면이 말하지 못한다.
    /// 어디로 가는지는 계속 보인다: 잠근 것은 고치는 길이지 정보가 아니다.
    /// </summary>
    private Control BuildExitRail(DialogueNode node, ConditionBranch branch)
    {
        StoryNode? target = _session!.Project.FindNode(branch.ExitTargetNodeId);

        var caption = new TextBlock
        {
            Text = $"→ 분기 이후: {target?.Name ?? "(사라진 노드)"}",
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Foreground = BranchPalette.Accent(branch.PaletteIndex)
        };

        if (_chapterEpisode)
        {
            // 단추가 아니라 글이다 — 잇고 떼는 문이 여기가 아님이 모양으로 보인다.
            caption.Margin = new Thickness(94, 0, 0, 0);
            caption.Opacity = 0.8;
            ToolTip.SetTip(caption,
                "이 갈래의 출구는 연출 그래프 카드의 IF 갈래 포트에서 잇고 뗍니다.");

            return caption;
        }

        var button = new Button
        {
            Content = caption,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2, 0),
            MinHeight = 0,
            Margin = new Thickness(92, 0, 0, 0)
        };
        ToolTip.SetTip(button, "누르면 이 갈래의 출구를 고칩니다.");

        button.Click += (_, _) =>
        {
            // 문이 둘이면 빗장도 둘이어야 한다 — 단추가 안 서더라도 여기서 다시 막는다.
            if (BlockIfChapterFlow("갈래 출구 배선"))
            {
                return;
            }

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
        List<StoryNode> targets = ExitTargets(node); // 엑셀노드 제외 — 기본 출구와 같은 규칙

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
                _session!.Editor.SetExitTarget(
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
            resolved.Line.Transition,
            // 조건 안 선택지(깊이 2)면 "선택지 끝 + 조건 종료"도 제시한다 (W55).
            choiceInsideCondition: resolved.PrecedingBranch is { IsChoice: true } && resolved.PrecedingDepth == 2);

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

    // ⛔ [발행] 탭과 BuildResults()는 2026-09-15에 사라졌다 (소유자: "저거 솔직히
    //    필요없어 보이는데"). 그 탭이 보여 주던 것은 발행 상태 문구와 발행 이력
    //    목록뿐이었고, 둘 다 [이 상태를 발행]이 2026-08-21에 자동이 된 뒤로는
    //    읽기 전용 현황판이었다 — 현황판은 볼 이유가 사라지면 지운다([편집 자료]
    //    현황판과 같은 결). ⚠ 발행 자체는 그대로다: 자동 발행
    //    (ProjectEditor.EnsurePresentationChannel)·Results 층·연출 바인딩·출력은
    //    한 줄도 안 건드렸다. 사라진 것은 그것을 들여다보던 창 하나다.
    //    (수동 발행 Publish()는 그보다 앞선 2026-08-21에 이미 없어졌다.)

    // ── 출구 후보 ───────────────────────────────────────────────────────────
    //
    // ⚠ [기본 출구] 편집 구역은 2026-08-22에 사라졌다 (소유자: "연출그래프에서 만질 일이
    // 없을 테니 그냥 없애도 될 것 같아") — 같은 값을 판의 레일 칩(`○ 진행`)이 이미
    // 편집한다(`GraphEditorView.ShowRailChip`). 한 값에 편집 창구가 둘이면 어느 쪽이
    // 최신인지 화면이 대답하지 못한다. <b>데이터·포트·출력은 그대로다</b> —
    // `DefaultExitTargetNodeId`·발행 점프 모두 손대지 않았다.
    // 남은 후보 계산은 갈래(detour) 출구가 쓴다.

    /// <summary>
    /// 출구 후보 — <b>엑셀노드는 뺀다</b> (소유자 결정 2026-08-14). 에피소드 사이의 흐름은
    /// 챕터 간선(기획자) 소유라, 자유 노드에서 엑셀노드로 점프하면 챕터 장부(표시/해금·
    /// 스탯 환산·cleared 기록)를 전부 지나친다. 같은 에피소드로의 "복귀"도 함정이다 —
    /// Yarn 점프는 노드 처음부터 다시 재생한다.
    /// </summary>
    /// <summary>검증용 손잡이 — 갈래 출구 콤보가 받는 후보 그대로.</summary>
    internal List<StoryNode> ExitTargetsProbe(string nodeId) =>
        ExitTargets((DialogueNode)_session!.Project.FindNode(nodeId)!);

    private List<StoryNode> ExitTargets(DialogueNode node) => _session!.Project.EnumerateNodes()
        .Where(other => !string.Equals(other.Id, node.Id, StringComparison.Ordinal))
        .Where(other => other is not PresentationNode)
        .Where(other => other is not DialogueNode { MarkedEpisodeId: not null })
        .ToList();

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
        if (_chapterEpisode)
        {
            return; // 이름의 원천은 챕터 `대사엔트리` — 개명은 챕터 그래프의 [이름] 칸에서(R-F까지).
        }

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

    // ⛔ 노드 단위 내보내기([이 노드 내보내기…]·[이 노드 CSV…])는 2026-09-15에
    //    [발행] 탭과 함께 사라졌다. 툴바의 [내보내기…]·[CSV 내보내기…]가 같은
    //    PickNodeExportsAsync로 노드를 골라 내보내므로 한 일에 창구가 둘이었다
    //    (MainWindow.OnExportClick·OnCsvExportClick). 계산 경로도 같았다 —
    //    LiveNodeComposer.Compose 하나를 양쪽이 지났다. 지운 것은 두 번째 창구뿐이다.

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
