using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Vn.App.Services;
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
    private AuthoringSession? _session;
    private string? _nodeId;
    private bool _building;
    private RenderedDocument? _previewDocument;
    private readonly IReadOnlyList<OutputPreset> _outputPresets = OutputPresetCatalog.All;
    private OutputPreset _selectedOutputPreset = OutputPresetCatalog.RuntimeFull;

    public DialogueNodeEditor()
    {
        InitializeComponent();

        NameBox.LostFocus += (_, _) => CommitName();
        AddLineButton.Click += (_, _) => AddLine();
        ImportScriptButton.Click += OnImportScriptClick;
        ScriptCombo.SelectionChanged += (_, _) => OnScriptSelected();
        PublishButton.Click += (_, _) => Publish();

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

            foreach (ResolvedLine line in flow.Lines)
            {
                LineHost.Children.Add(BuildCard(node, script, line));
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
            AvailableConditionCatalog available = AvailableConditionResolver.Resolve(
                _session!.Project,
                node.Id,
                _session.Definition);
            AvailableCondition? condition = available.Find(branch.ConditionId);
            AvailableCondition? known = condition ?? AvailableConditionResolver.FindKnown(
                _session.Project,
                _session.Definition,
                branch.ConditionId);
            string conditionLabel = condition is not null
                ? condition.DisplayName
                : known is not null
                    ? AvailableConditionResolver.UnavailableLabel(known, branch.ConditionId)
                    : "알 수 없는 조건";

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

    private Control BuildTextRow(DialogueScript script, ResolvedLine resolved)
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
                    speaker.Text ?? string.Empty,
                    text.Text ?? string.Empty,
                    script.Locale);
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
