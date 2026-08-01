using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Vn.App.Services;
using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;

namespace Vn.App.Views;

/// <summary>
/// 대사 노드 하나를 위에서 아래로 편집한다.
///
/// 카드의 들여쓰기와 색은 <see cref="ConditionFlowResolver"/>가 계산한 갈래에서만 나온다.
/// 화면이 조건 상태를 따로 들고 있지 않으므로, 조건 드롭다운을 한 번 바꾸면
/// 그 아래 줄들의 표시가 전부 알아서 따라온다.
///
/// 글자 편집은 카드를 다시 만들지 않는다. 다시 만들면 편집 중이던 칸이 사라진다.
/// 그래서 <see cref="ProjectChangeKind.Content"/>일 때는 목록을 그대로 둔다.
/// </summary>
public partial class DialogueNodeEditor : UserControl
{
    private AuthoringSession? _session;
    private string? _nodeId;
    private bool _building;

    public DialogueNodeEditor()
    {
        InitializeComponent();

        NameBox.LostFocus += (_, _) => CommitName();
        AddLineButton.Click += (_, _) =>
        {
            if (_session is not null && _nodeId is not null)
            {
                _session.Editor.AddLine(_nodeId);
            }
        };

        DefaultExitCheck.IsCheckedChanged += (_, _) => OnDefaultExitToggled();
        DefaultExitCombo.SelectionChanged += (_, _) => OnDefaultExitSelected();
    }

    internal void Attach(AuthoringSession session)
    {
        _session = session;
    }

    /// <summary>어떤 노드를 편집할지 정하고 화면을 만든다.</summary>
    internal void Show(string? nodeId)
    {
        _nodeId = nodeId;
        Rebuild();
    }

    internal string? NodeId => _nodeId;

    internal void Rebuild()
    {
        if (_session is null || _session.Project.FindDialogue(_nodeId) is not { } node)
        {
            LineHost.Children.Clear();
            return;
        }

        _building = true;

        try
        {
            NameBox.Text = node.Name;

            DialogueFlow flow = ConditionFlowResolver.Resolve(node, _session.Project);

            LineHost.Children.Clear();

            foreach (ResolvedLine line in flow.Lines)
            {
                LineHost.Children.Add(BuildCard(node, line, flow));
            }

            BuildDefaultExit(node);
            ShowProblems(flow);
        }
        finally
        {
            _building = false;
        }
    }

    // ── 카드 ────────────────────────────────────────────────────────────────

    private Control BuildCard(DialogueNode node, ResolvedLine resolved, DialogueFlow flow)
    {
        ConditionBranch? branch = resolved.Branch;
        int palette = branch?.PaletteIndex ?? -1;

        var body = new StackPanel { Spacing = 6 };

        body.Children.Add(BuildHeader(node, resolved, flow));
        body.Children.Add(BuildTextRow(node, resolved));

        if (resolved.IsBranchExit && branch is not null)
        {
            body.Children.Add(BuildExitBadge(branch));
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

    private Control BuildHeader(DialogueNode node, ResolvedLine resolved, DialogueFlow flow)
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

        Grid.SetColumn(index, 0);
        header.Children.Add(index);

        // 지금 어느 갈래인지는 색이 아니라 글자로도 반드시 알 수 있어야 한다.
        if (resolved.Branch is { } branch)
        {
            ConditionDefinition? condition = _session!.Project.FindCondition(branch.ConditionId);

            var label = new Border
            {
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(6, 1),
                CornerRadius = new CornerRadius(3),
                Background = BranchPalette.Accent(branch.PaletteIndex),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = condition is null
                        ? "알 수 없는 조건"
                        : ConditionChoices.DisplayName(condition),
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

        Button up = SmallButton("▲", () => _session!.Editor.MoveLine(node.Id, resolved.Line.Id, -1));
        Button down = SmallButton("▼", () => _session!.Editor.MoveLine(node.Id, resolved.Line.Id, 1));
        Button remove = SmallButton("✕", () => _session!.Editor.RemoveLine(node.Id, resolved.Line.Id));

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
        IReadOnlyList<ConditionChoice> choices =
            ConditionChoices.For(resolved.PrecedingBranch, _session!.Project);

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

            _session.Editor.SetLineTransition(node.Id, resolved.Line.Id, picked.ToTransition());
        };

        return box;
    }

    private Control BuildTextRow(DialogueNode node, ResolvedLine resolved)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*") };

        var speaker = new TextBox
        {
            Text = resolved.Line.Speaker,
            PlaceholderText = "화자",
            FontWeight = FontWeight.SemiBold,
            FontSize = 12
        };

        var text = new TextBox
        {
            Text = resolved.Line.Text,
            PlaceholderText = "대사",
            Margin = new Thickness(6, 0, 0, 0),
            AcceptsReturn = false,
            TextWrapping = TextWrapping.Wrap
        };

        // 글자가 바뀔 때마다 모델에 넣되, 그것이 카드 목록을 다시 만들지는 않는다.
        // 편집기가 내용 변경과 구조 변경을 구분해서 알리기 때문에 가능하다.
        void Commit()
        {
            if (!_building)
            {
                _session!.Editor.SetLineText(
                    node.Id,
                    resolved.Line.Id,
                    speaker.Text ?? string.Empty,
                    text.Text ?? string.Empty);
            }
        }

        speaker.TextChanged += (_, _) => Commit();
        text.TextChanged += (_, _) => Commit();

        Grid.SetColumn(speaker, 0);
        Grid.SetColumn(text, 1);
        row.Children.Add(speaker);
        row.Children.Add(text);

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

    // ── 기본 출구 ───────────────────────────────────────────────────────────

    private void BuildDefaultExit(DialogueNode node)
    {
        List<StoryNode> targets = _session!.Project.EnumerateNodes()
            .Where(other => !string.Equals(other.Id, node.Id, StringComparison.Ordinal))
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
