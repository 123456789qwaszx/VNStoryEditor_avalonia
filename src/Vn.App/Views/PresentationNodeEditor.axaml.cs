using Avalonia;
using Avalonia.Controls;
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
    private AuthoringSession? _session;
    private string? _nodeId;
    private bool _building;
    private AvailablePresentationCommands? _available;

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
        PublishButton.Click += (_, _) => Publish();
    }

    internal void Attach(AuthoringSession session) => _session = session;

    internal string? NodeId => _nodeId;

    internal void Show(string? nodeId)
    {
        _nodeId = nodeId;
        Rebuild();
    }

    internal void Rebuild()
    {
        if (_session is null || _session.Project.FindPresentation(_nodeId) is not { } presentation)
        {
            LineHost.Children.Clear();
            return;
        }

        _building = true;

        try
        {
            NameBox.Text = presentation.Name;
            LineHost.Children.Clear();

            BuildSourcePicker(presentation);

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
        }
        finally
        {
            _building = false;
        }
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

        IReadOnlyList<PresentationCommandDefinition> choices = catalog.Definitions;

        foreach (PresentationCommandInstance command in presentation.SetupCommands)
        {
            content.Children.Add(BuildSetupRow(presentation, command, catalog, choices));
        }

        var add = new Button
        {
            Content = "+ Setup 커맨드",
            FontSize = 11,
            Padding = new Thickness(8, 3),
            IsEnabled = choices.Count > 0
        };

        add.Click += (_, _) =>
        {
            if (!_building && _session is not null && choices.Count > 0)
            {
                PresentationCommandDefinition definition = choices[0];
                _session.Editor.AddPresentationSetupCommand(
                    presentation.Id,
                    definition.Id,
                    definition.DefaultArgumentValues());
            }
        };

        content.Children.Add(add);

        return new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(7),
            BorderBrush = new SolidColorBrush(Color.FromArgb(50, 37, 99, 235)),
            BorderThickness = new Thickness(1),
            Child = content
        };
    }

    private Control BuildSetupRow(
        PresentationNode presentation,
        PresentationCommandInstance command,
        PresentationCommandCatalog catalog,
        IReadOnlyList<PresentationCommandDefinition> choices)
    {
        var combo = new ComboBox
        {
            ItemsSource = choices
                .Select(item =>
                    $"{catalog.FindCategory(item.CategoryId)?.DisplayName ?? item.CategoryId} · {item.DisplayName}")
                .ToArray(),
            SelectedIndex = FindDefinitionIndex(choices, command.DefinitionId),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        combo.SelectionChanged += (_, _) =>
        {
            if (_building || _session is null || combo.SelectedIndex < 0 || combo.SelectedIndex >= choices.Count)
            {
                return;
            }

            PresentationCommandDefinition definition = choices[combo.SelectedIndex];
            _session.Editor.SetPresentationCommandDefinition(
                presentation.Id,
                command.Id,
                definition.Id,
                definition.DefaultArgumentValues());
        };

        Button up = SetupButton("▲", () =>
            _session!.Editor.MovePresentationSetupCommand(presentation.Id, command.Id, -1));
        Button down = SetupButton("▼", () =>
            _session!.Editor.MovePresentationSetupCommand(presentation.Id, command.Id, 1));
        Button remove = SetupButton("✕", () =>
            _session!.Editor.RemovePresentationCommand(presentation.Id, command.Id));

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };
        Grid.SetColumn(combo, 0);
        Grid.SetColumn(up, 1);
        Grid.SetColumn(down, 2);
        Grid.SetColumn(remove, 3);
        row.Children.Add(combo);
        row.Children.Add(up);
        row.Children.Add(down);
        row.Children.Add(remove);
        return row;
    }

    private Button SetupButton(string glyph, Action action)
    {
        var button = new Button
        {
            Content = glyph,
            FontSize = 10,
            Padding = new Thickness(6, 2),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
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

        foreach (PresentationCategoryDefinition category in _available?.Categories ?? catalog.Categories)
        {
            content.Children.Add(BuildCategoryEditor(presentation, line.LineId, category, catalog));
        }

        return new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(7),
            BorderBrush = new SolidColorBrush(Color.FromArgb(35, 128, 128, 128)),
            BorderThickness = new Thickness(1),
            Child = content
        };
    }

    /// <summary>드롭다운 항목 하나 — 카탈로그 정의 또는 공급 노드의 프리셋.</summary>
    private sealed record CommandChoice(
        string Label,
        string DefinitionId,
        string? PresetId,
        IReadOnlyDictionary<string, string> Arguments);

    private Control BuildCategoryEditor(
        PresentationNode presentation,
        string lineId,
        PresentationCategoryDefinition category,
        PresentationCommandCatalog catalog)
    {
        var choices = new List<CommandChoice>();

        // 프리셋이 먼저다 — 값이 세팅된 "정확한 연출종류"가 원시 커맨드보다 우선 후보다.
        foreach (AvailablePreset preset in _available?.PresetsFor(category.Id)
                     ?? (IReadOnlyList<AvailablePreset>)Array.Empty<AvailablePreset>())
        {
            choices.Add(new CommandChoice(
                $"★ {preset.DisplayName}",
                preset.Preset.CommandDefinitionId,
                preset.Preset.Id,
                new Dictionary<string, string>(StringComparer.Ordinal)));
        }

        foreach (PresentationCommandDefinition definition in catalog.For(category.Id))
        {
            choices.Add(new CommandChoice(
                definition.DisplayName,
                definition.Id,
                PresetId: null,
                definition.DefaultArgumentValues()));
        }

        PresentationLineBinding? binding = presentation.FindBinding(lineId);
        PresentationCommandInstance? command = binding?.Commands.FirstOrDefault(item =>
            string.Equals(
                catalog.Find(item.DefinitionId)?.CategoryId,
                category.Id,
                StringComparison.Ordinal));

        var enabled = new CheckBox
        {
            Content = category.DisplayName,
            IsChecked = command?.IsEnabled == true,
            IsEnabled = choices.Count > 0,
            MinWidth = 128,
            VerticalAlignment = VerticalAlignment.Center
        };

        var combo = new ComboBox
        {
            ItemsSource = choices.Select(item => item.Label).ToArray(),
            SelectedIndex = command is null
                ? (choices.Count > 0 ? 0 : -1)
                : FindChoiceIndex(choices, command),
            IsEnabled = enabled.IsChecked == true && choices.Count > 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = choices.Count == 0 ? "명령 정의 없음" : "명령 선택"
        };

        enabled.IsCheckedChanged += (_, _) =>
        {
            if (_building || _session is null || choices.Count == 0)
            {
                return;
            }

            combo.IsEnabled = enabled.IsChecked == true;

            if (command is null && enabled.IsChecked == true)
            {
                int index = Math.Clamp(combo.SelectedIndex, 0, choices.Count - 1);
                CommandChoice choice = choices[index];
                command = _session.Editor.AddPresentationCommand(
                    presentation.Id,
                    lineId,
                    choice.DefinitionId,
                    choice.Arguments,
                    presetId: choice.PresetId);
            }
            else if (command is not null)
            {
                _session.Editor.SetPresentationCommandEnabled(
                    presentation.Id,
                    command.Id,
                    enabled.IsChecked == true);
            }
        };

        combo.SelectionChanged += (_, _) =>
        {
            if (_building || _session is null || combo.SelectedIndex < 0 || combo.SelectedIndex >= choices.Count)
            {
                return;
            }

            CommandChoice choice = choices[combo.SelectedIndex];

            if (command is null)
            {
                if (enabled.IsChecked == true)
                {
                    command = _session.Editor.AddPresentationCommand(
                        presentation.Id,
                        lineId,
                        choice.DefinitionId,
                        choice.Arguments,
                        presetId: choice.PresetId);
                }
            }
            else
            {
                _session.Editor.SetPresentationCommandDefinition(
                    presentation.Id,
                    command.Id,
                    choice.DefinitionId,
                    choice.Arguments,
                    choice.PresetId);
            }
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(enabled, 0);
        Grid.SetColumn(combo, 1);
        combo.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(enabled);
        row.Children.Add(combo);
        return row;
    }

    private static int FindChoiceIndex(IReadOnlyList<CommandChoice> choices, PresentationCommandInstance command)
    {
        for (int index = 0; index < choices.Count; index++)
        {
            CommandChoice choice = choices[index];

            bool matches = command.PresetId is not null
                ? string.Equals(choice.PresetId, command.PresetId, StringComparison.Ordinal)
                : choice.PresetId is null &&
                  string.Equals(choice.DefinitionId, command.DefinitionId, StringComparison.Ordinal);

            if (matches)
            {
                return index;
            }
        }

        return choices.Count > 0 ? 0 : -1;
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

    private static int FindDefinitionIndex(
        IReadOnlyList<PresentationCommandDefinition> definitions,
        string definitionId)
    {
        for (int index = 0; index < definitions.Count; index++)
        {
            if (string.Equals(definitions[index].Id, definitionId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return definitions.Count > 0 ? 0 : -1;
    }
}
