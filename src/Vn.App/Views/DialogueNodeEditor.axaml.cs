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
    /// 지금 보는 노드가 엑셀노드인가 (<see cref="DialogueNode.ExcelEpisodeId"/>).
    /// 참이면 본문·화자·줄 구성은 읽기 전용이다 — 원본이 엑셀이라, 여기서 고쳐도 다음
    /// 동기화가 되돌린다. 고쳐지는 척하다 증발하는 것이 가장 나쁜 화면이다.
    /// 출구·연출·발행은 툴 소유 그대로다.
    /// </summary>
    private bool _excelOwned;

    /// <summary>
    /// 이 노드의 대사 잠금을 사람이 <b>지금</b> 풀어 뒀는가 (2026-08-24 소유자: "때때로는
    /// 이곳에서도 대사를 편집하는게 편하다").
    ///
    /// ⚠ <b>노드마다·매번 다시 잠긴다</b> (<see cref="Rebuild"/>에서 초기화). 열어 둔 것을
    /// 잊고 다음 노드에서 무심코 고치는 길을 없애려는 것이다 — 원본은 여전히 엑셀이고,
    /// 여는 것은 잠깐의 예외여야 한다.
    ///
    /// ⛔ 이 값은 <b>화면만</b> 연다. 열린 칸에서 고친 글은 노드가 아니라
    /// <see cref="EpisodeLineEditor"/>를 지나 <b>엑셀 셀로</b> 나간다 — 노드만 고치면
    /// 다음 동기화가 지운다(`EpisodeLineEditorTests.노드만_고치면_다음_동기화가_지운다`).
    ///
    /// ⚠ <b>깃발이 아니라 노드 Id를 든다.</b> 참·거짓으로 들면 다시 그릴 때마다 초기화할지
    /// 말지를 매번 정해야 하고(Rebuild는 노드를 옮길 때도 칸을 고칠 때도 돈다), 한 번
    /// 틀리면 <b>남의 노드가 열린 채로 뜬다</b>. Id로 들면 노드가 바뀌는 순간 저절로 닫힌다.
    /// </summary>
    private string? _excelUnlockedNodeId;

    /// <summary>엑셀노드인데 사람이 이 노드의 잠금을 풀어 둔 상태.</summary>
    private bool ExcelTextUnlocked =>
        _excelOwned &&
        _nodeId is not null &&
        string.Equals(_excelUnlockedNodeId, _nodeId, StringComparison.Ordinal);

    /// <summary>본문·화자를 지금 고칠 수 있는가 — 자유 노드는 늘, 엑셀노드는 풀었을 때만.</summary>
    private bool TextEditable => !_excelOwned || ExcelTextUnlocked;

    /// <summary>갈래 시작 줄(라벨·조건) → 블록/갈래 (W36-a). 클릭하면 그 갈래가 선택으로 남는다.</summary>
    private readonly Dictionary<string, (BranchFlow.Block Block, int BranchIndex)> _branchStarts =
        new(StringComparer.Ordinal);

    public DialogueNodeEditor()
    {
        InitializeComponent();

        NameBox.LostFocus += (_, _) => CommitName();
        AddLineButton.Click += (_, _) => AddLine();

        // 잠금을 풀고 잠근다 — 카드를 다시 만들어야 칸의 읽기 전용이 따라온다.
        ExcelTextLockToggle.IsCheckedChanged += (_, _) => UiGuard.Run(_session, "대사 잠금", () =>
        {
            if (_building)
            {
                return; // Rebuild가 모양을 맞추는 중이다 — 사람이 누른 것이 아니다.
            }

            _excelUnlockedNodeId = ExcelTextLockToggle.IsChecked == true ? _nodeId : null;

            if (ExcelTextUnlocked)
            {
                _session?.SetStatus(
                    "이 노드의 대사를 엽니다 — 고친 글은 에피소드 엑셀에 바로 써집니다. " +
                    "줄을 더하고 지우는 것은 여전히 엑셀에서 합니다.");
            }

            Rebuild(); // 칸의 읽기 전용은 카드를 다시 만들어야 따라온다
        });
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
    /// 대사 잠금 토글의 모양 (2026-08-24). <b>엑셀노드에서만 보인다</b> — 자유 노드는 늘
    /// 열려 있어 잠글 것이 없다.
    ///
    /// ⛔ <b>되쓸 자리를 못 찾으면 열리지 않는다.</b> 그 줄이 어느 워크북 어느 인덱스에서
    /// 왔는지 모르면 고친 글이 갈 곳이 없고, 그러면 다음 동기화까지만 사는 거짓말이 된다.
    /// 그래서 <see cref="EpisodeLineEditor.Locate"/>가 답을 못 내면 토글을 잠가 두고
    /// <b>이유를 말한다</b> — 잠긴 채 아무 말도 없으면 "이 기능이 없다"로 읽힌다.
    /// </summary>
    private void RefreshExcelTextLockToggle(DialogueNode node)
    {
        ExcelTextLockToggle.IsVisible = _excelOwned;

        if (!_excelOwned)
        {
            _excelUnlockedNodeId = null;
            return;
        }

        // 줄 하나라도 되쓸 자리가 있으면 연다 — 첫 줄로 대표해서 묻는다.
        bool writable = _session is { } session &&
            FirstWritableLineTarget(session, node) is not null;

        ExcelTextLockToggle.IsEnabled = writable;
        ExcelTextLockToggle.IsChecked = ExcelTextUnlocked;
        ExcelTextLockToggle.Content = ExcelTextUnlocked ? "✏ 대사 편집 중" : "🔒 대사 잠김";

        ToolTip.SetTip(ExcelTextLockToggle, !writable
            ? "이 노드의 대사를 되쓸 엑셀 자리를 찾지 못했습니다 — 프로젝트를 저장했는지, " +
              "그 에피소드의 대본 파일이 있는지 확인해 주세요."
            : ExcelTextUnlocked
                ? "잠그면 다시 읽기 전용이 됩니다. 여는 동안 고친 글은 에피소드 엑셀 셀에 " +
                  "바로 써집니다 — 화자와 내용만 열립니다."
                : "이 대본의 원본은 에피소드 엑셀입니다. 풀면 화자·내용을 여기서도 고칠 수 " +
                  "있고, 고친 값은 그 엑셀 셀에 바로 저장됩니다.\n" +
                  "줄 추가·삭제·조건 블록은 엑셀에서 합니다.");
    }

    /// <summary>
    /// 이 노드에서 되쓸 수 있는 줄이 하나라도 있는가 — 토글을 열지 말지의 근거다.
    /// </summary>
    private EpisodeLineTarget? FirstWritableLineTarget(AuthoringSession session, DialogueNode node)
    {
        foreach (string lineId in node.ExcelLineMap.Values)
        {
            if (EpisodeLineEditor.Locate(session.Project, session.ProjectPath, node, lineId)
                is { } target)
            {
                return target;
            }
        }

        return null;
    }

    /// <summary>
    /// 고친 화자·내용을 <b>엑셀 셀로 내보내고</b>, 성공했을 때만 노드도 맞춘다 (2026-08-24).
    ///
    /// ⛔ 순서가 곧 규칙이다. 노드를 먼저 고치면, 엑셀이 그 파일을 잡고 있어 쓰기가 거부된
    /// 순간 화면과 파일이 다른 말을 하고 <b>다음 동기화가 사람이 방금 쓴 글을 지운다.</b>
    /// </summary>
    /// <returns>노드에도 반영해도 되는가.</returns>
    private bool WriteLineToWorkbook(string lineId, string? speaker, string? text)
    {
        if (_session is not { } session ||
            session.Project.FindDialogue(_nodeId) is not { } node)
        {
            return false;
        }

        if (EpisodeLineEditor.Locate(session.Project, session.ProjectPath, node, lineId)
            is not { } target)
        {
            session.SetStatus(
                "이 줄을 되쓸 엑셀 자리를 찾지 못했습니다 — 고친 글을 저장하지 않았습니다.");
            return false;
        }

        ChapterWriteResult result = EpisodeLineEditor.Write(target, speaker, text);

        if (!result.Written)
        {
            // 침묵 금지 (규율 1) — 안 써졌다는 사실이 반드시 사람에게 닿아야 한다.
            session.SetStatus(result.Failure!);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 엑셀노드의 <b>구조</b> 편집 시도를 막고, 어디서 고치는지 말한다. 참이면 호출자는
    /// 그냥 돌아간다.
    ///
    /// ⚠ <b>대사 잠금을 풀어도 이 문은 안 열린다</b> (2026-08-24). 풀리는 것은 화자·내용
    /// 두 칸뿐이고, 줄을 더하고 지우고 옮기는 것·조건 블록은 엑셀 소유 그대로다 — 그쪽은
    /// 인덱스 재배치가 얽혀 있어, 열면 표의 구조를 두 곳이 갖게 된다.
    /// </summary>
    private bool BlockIfExcelOwned(string what)
    {
        if (!_excelOwned)
        {
            return false;
        }

        _session?.SetStatus(
            $"{what}은(는) 엑셀에서 합니다 — 이 대본의 원본은 에피소드 엑셀입니다. " +
            "챕터 그래프에서 노드를 더블클릭하면 열립니다.");
        return true;
    }

    /// <summary>
    /// 화자·내용 편집 시도를 막는다 — 묻는 것은 "엑셀노드인가"가 아니라 <b>"지금 잠겨
    /// 있는가"</b>다 (2026-08-24). 잠금을 푼 엑셀노드에서는 통과시킨다.
    /// </summary>
    private bool BlockIfTextLocked(string what)
    {
        if (TextEditable)
        {
            return false;
        }

        _session?.SetStatus(
            $"{what}은(는) 지금 읽기 전용입니다 — 이 대본의 원본은 에피소드 엑셀입니다. " +
            "위 [🔒 대사 잠김]을 풀면 여기서도 고칠 수 있습니다.");
        return true;
    }

    // 엑셀노드 안내 띠("📄 엑셀노드 — 원본: 에피소드 …")는 2026-08-22에 사라졌다 (소유자).
    // 대사 카드가 이미 잠겨 있고 손대려 하면 상태줄이 같은 말을 한다(BlockIfExcelOwned) —
    // 목록 맨 위에 늘 서 있던 세 줄은 그 사실을 매번 다시 알리는 소음이었다.

    /// <summary>빈 공간의 줄 메뉴 (W49) — 여기서의 추가는 언제나 맨 아래, 삭제는 대상이 없어 잠긴다.</summary>
    private void ShowEmptyAreaFlyout()
    {
        if (BlockIfExcelOwned("줄 추가") || _session?.Project.FindDialogue(_nodeId) is null)
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
        if (BlockIfExcelOwned("줄 추가") ||
            _session is null || _session.Project.FindDialogue(_nodeId) is not { } node)
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
            _excelOwned = node.ExcelEpisodeId is not null;

            NameBox.Text = node.Name;
            // 엑셀노드의 이름은 챕터 `대사엔트리`가 원천이다 — 여기서 바꾸면 다음 동기화가
            // 같은 이름의 노드를 못 찾아 빈 노드를 새로 만든다.
            //
            // ⚠ 이름은 <b>잠금을 풀어도 안 열린다.</b> 푼 것은 대사 두 칸(화자·내용)이지
            // 노드의 신원이 아니다 — 이름은 챕터 워크북 소유라 되쓸 자리도 여기가 아니다.
            NameBox.IsReadOnly = _excelOwned;

            // 줄 추가·텍스트 반영도 마찬가지다 — 표의 구조는 엑셀 소유 그대로다.
            AddLineButton.IsEnabled = !_excelOwned;
            ApplyScenarioButton.IsEnabled = !_excelOwned;

            RefreshExcelTextLockToggle(node);

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

                Control card = WrapForLineSelection(BuildCard(node, script, line), line.Line.LineId);

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
        var initial = RegisteredVariables(node)
            .Select(assignment => (assignment.Variable, Value: assignment.Value))
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
        if (BlockIfExcelOwned("줄 추가·삭제") ||
            _session?.Project.FindDialogue(_nodeId) is not { } node || node.ScriptId is not { } scriptId)
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
        if (BlockIfExcelOwned("줄 추가") ||
            _session is null || _session.Project.FindDialogue(_nodeId) is not { } node)
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
            // 엑셀노드의 본문은 기본이 읽기 전용 — 원본은 엑셀이다. 위 [🔒 대사 잠김]을
            // 풀면 열리고, 그때 고친 글은 노드가 아니라 엑셀 셀로 나간다(2026-08-24).
            IsReadOnly = !TextEditable,
            // 캐럿조차 안 선다 — 읽기 전용이어도 클릭이 먹으면 "열려 있는 것처럼 보인다"(실사용 보고).
            IsHitTestVisible = TextEditable
        };

        if (!TextEditable)
        {
            text.Opacity = 0.8;
            ToolTip.SetTip(row, "본문은 엑셀에서 고칩니다 — 위 [🔒 대사 잠김]을 풀면 " +
                "여기서도 고칠 수 있고, 고친 값은 그 엑셀 셀에 바로 저장됩니다.");
        }

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

            // 엑셀노드는 <b>엑셀이 먼저다.</b> 셀에 못 쓰면 노드도 안 고친다 — 안 그러면
            // 화면과 파일이 다른 말을 하고 다음 동기화가 사람의 글을 지운다.
            if (_excelOwned &&
                !WriteLineToWorkbook(resolved.Line.LineId, nextSpeaker, nextText))
            {
                return;
            }

            _session!.Editor.SetScriptLineText(
                scriptId, resolved.Line.LineId, nextSpeaker, nextText, script.Locale);
        }

        // ⚠ 엑셀노드에서는 <b>초점을 잃을 때만</b> 낸다 (2026-08-24). 자판 하나마다 워크북을
        // 열면 엑셀 파일을 쉼 없이 두드리고, 그 사이 들어온 파일 사건이 칸을 다시 채워
        // 쓰던 글을 끊는다 — 챕터 그래프가 2026-08-17에 같은 자리에서 배운 것이다.
        // 자유 노드의 대본은 프로젝트 안의 값이라 예전처럼 글자마다 낸다.
        void Wire(Control field)
        {
            if (_excelOwned)
            {
                field.LostFocus += (_, _) => Commit();
                return;
            }

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
                IsEnabled = TextEditable // 화자도 본문과 함께 엑셀 소유다 — 같이 열리고 닫힌다
            };
            Wire(speaker);

            // 후보는 포커스 때마다 다시 읽는다 (W56) — 방금 등록한 화자가 카드 재생성
            // 없이도 자동완성에 바로 나온다.
            speaker.GotFocus += (_, _) => speaker.ItemsSource = SpeakerNames();

            // ▾ = 등록 화자 전체 목록 (W40) — 자동완성은 타이핑해야 열리니 클릭 한 번 길을 따로 둔다.
            //
            // ⚠ 엑셀노드에서는 이 단추도 잠긴다 (2026-08-23 소유자 보고: "대사편집에서 화자가
            // 선택가능하고, 실제 반영은 안되더라도 표기상으로 바꿔지는 것처럼 보이는데").
            // 화자 칸은 이미 잠겨 있었지만 <b>▾는 살아 있어서</b>, 거기서 고른 이름이 잠긴
            // 칸에 써졌다(프로그램이 넣는 값은 IsEnabled를 안 본다). 다음 동기화가 엑셀의
            // 값으로 되돌리므로 <b>고쳐진 척하다 증발하는</b> 가장 나쁜 화면이었다.
            AutoCompleteBox speakerBox = speaker;
            var pick = new Button
            {
                Content = "▾",
                FontSize = 8,
                Padding = new Thickness(3, 0),
                MinWidth = 16,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(1, 0, 0, 0),
                IsEnabled = TextEditable
            };
            ToolTip.SetTip(pick, TextEditable
                ? "등록된 화자 목록에서 선택"
                : "화자는 엑셀에서 고칩니다 — 위 [🔒 대사 잠김]을 풀면 여기서도 고를 수 있습니다.");
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
        // 단추가 이미 잠겨 있지만 가드를 여기에도 둔다 — 문이 둘이면 빗장도 둘이어야 한다.
        // ⚠ 묻는 것은 "엑셀노드인가"가 아니라 <b>"지금 잠겨 있는가"</b>다 — 잠금을 푼
        // 엑셀노드에서는 여기서도 골라야 한다(2026-08-24).
        if (BlockIfTextLocked("화자"))
        {
            return;
        }

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

        // 엑셀노드 — 조건·선택지의 원본은 엑셀(E열·CHOICE/OPTION 행)이다. 정보는 보이되
        // 편집 플라이아웃은 열리지 않는다. 여기서 고친 것은 다음 동기화가 되돌리기 때문이다.
        if (_excelOwned)
        {
            Control chip = TagChip(label, background);
            ToolTip.SetTip(chip, "조건·선택지는 엑셀에서 고칩니다 — E열(조건라벨)·CHOICE/OPTION 행.");
            return chip;
        }

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

    /// <summary>set 태그 하나. 누르면 아이템·능력·연산자·값 편집 행이 Flyout으로 열린다.</summary>
    private Control BuildSetTag(
        DialogueNode node,
        string lineId,
        IReadOnlyList<VariableAssignment> registered,
        int operationIndex,
        SetOperation operation)
    {
        string summary = $"set {operation.Variable} {SetOperators.Symbol(operation.Operator)} {operation.Value}";
        var blue = new SolidColorBrush(Color.FromArgb(200, 37, 99, 235));

        // 엑셀노드 — 스탯 조작의 주인은 챕터 간선의 스탯변화다(2026-08-14 폐지 결정).
        // 보이되 고쳐지지 않는다.
        if (_excelOwned)
        {
            Control chip = TagChip(summary, blue);
            ToolTip.SetTip(chip, "스탯변화는 챕터 그래프의 간선에서 고칩니다 — 간선 시트 C열 (예: trust +3).");
            return chip;
        }

        Button tag = TagButton(summary, blue);
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

        if (_excelOwned)
        {
            // 엑셀노드 — 조건·선택지·Set의 원본은 엑셀이다. 이 메뉴에는 툴 소유인 출구만 남는다.
            panel.Children.Add(new TextBlock
            {
                Text = "📄 엑셀노드 — 조건·선택지·스탯변화는 엑셀에서 고칩니다.",
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
                    Text = "챕터 설정 노드에 아이템·능력을 더하면 드롭다운으로 고릅니다.",
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
        }

        // 출구는 선택·조건 갈래의 마지막 줄에서만 매달 수 있다.
        //
        // ⚠ 엑셀노드에서는 <b>어디로 가는지만</b> 말한다 (2026-08-23 소유자) — 잇고 떼는
        // 자리는 연출 그래프 카드의 IF 갈래 포트 하나다. 레일(→ 분기 이후)과 여기가 같은
        // `SetExitTarget`을 부르는 둘째·셋째 문이었다.
        if (resolved.Branch is { } branch && resolved.Index == branch.LastLineIndex)
        {
            Section("출구 (이 갈래 끝에서 점프)");

            if (_excelOwned)
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
        // 엑셀노드에서는 줄 구성이 엑셀 소유라 아예 내놓지 않는다(각 동작에 가드도 있다).
        if (_excelOwned)
        {
            new Flyout { Content = panel }.ShowAt(anchor);
            return;
        }

        Section("줄");
        string scriptId = script.ScriptId ?? string.Empty;
        string lineId = resolved.Line.LineId;
        var lineRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        lineRow.Children.Add(SmallButton("▲ 위로", () =>
        {
            if (!BlockIfExcelOwned("줄 이동"))
            {
                _session!.Editor.MoveScriptLine(scriptId, lineId, -1);
            }
        }));
        lineRow.Children.Add(SmallButton("▼ 아래로", () =>
        {
            if (!BlockIfExcelOwned("줄 이동"))
            {
                _session!.Editor.MoveScriptLine(scriptId, lineId, 1);
            }
        }));
        Button remove = SmallButton("✕ 삭제", () =>
        {
            if (!BlockIfExcelOwned("줄 삭제"))
            {
                _session!.Editor.RetireScriptLine(scriptId, lineId);
            }
        });
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

        if (_excelOwned)
        {
            // 단추가 아니라 글이다 — 잠김이 모양으로 보인다(엑셀노드 태그 칩과 같은 규칙).
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
            if (BlockIfExcelOwned("갈래 출구 배선"))
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

    /// <summary>
    /// 이 대사 노드가 쓸 수 있는 아이템·능력 — 이 챕터(판) 설정 노드의 등록 목록이다.
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
    /// set 편집 (X6) — 타이핑 대신 등록 아이템·능력 드롭다운 + 슬라이더.
    /// 슬라이더 범위는 설정노드의 항목별 등록(기본 -5~+5)이고 편의일 뿐이라
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
        // 드롭다운에는 등록된 아이템·능력만 나온다 (X6 수용). 이미 적혀 있는 미등록 이름은
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
            PlaceholderText = choices.Count == 0 ? "등록된 아이템·능력 없음" : "아이템·능력"
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

        // 능력(보유)은 수치 슬라이더가 아니라 On/Off 토글이다 (X7).
        // 저장 값은 Yarn 문법 그대로 true/false 문자열 — 출력 불변.
        //
        // 종류는 <b>이 챕터의 등록</b>만 본다 (2026-08-17 소유자) — 정의 파일을 뒤지던
        // 폴백은 뺐다. 기획자 스탯은 작가에게 노출되어서는 안 되는 자료라, 종류를 알아내는
        // 길로도 쓰지 않는다.
        bool isBool = registration?.IsBool == true;

        // 능력에는 부호가 없다 (2026-08-17 소유자: "지금 능력은 On, Off인데도 부호가 있는데,
        // 부호를 없애던지 혹은 =로 고정") — On/Off에 `+=`가 설 자리가 없다. 콤보를 감추고
        // 값을 `=`로 못 박는다: 고를 것이 하나뿐이면 고르게 하지 않는다.
        if (isBool)
        {
            operatorBox.IsVisible = false;

            if (operation.Operator != SetOperatorKind.Assign)
            {
                Commit(target => target.Operator = SetOperatorKind.Assign);
            }
        }

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
    // `DefaultExitTargetNodeId`·`EffectiveDefaultExit`·발행 점프 모두 손대지 않았다.
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
        .Where(other => other is not DialogueNode { ExcelEpisodeId: not null })
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
        if (_excelOwned)
        {
            return; // 이름의 원천은 챕터 `대사엔트리` — 개명은 챕터 그래프의 [이름] 칸에서.
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
