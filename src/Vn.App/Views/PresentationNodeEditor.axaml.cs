using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Vn.App.Services;
using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;

namespace Vn.App.Views;

/// <summary>
/// 연결된 DialogueNode의 줄을 읽기 전용으로 투영하고 LineId별 연출 Command를 편집한다.
/// Dialogue의 화자·대사는 복사하지 않으며 모든 변경은 PresentationNode의 binding에만 기록한다.
/// </summary>
public partial class PresentationNodeEditor : UserControl
{
    private static readonly PresentationCategory[] Categories =
    {
        PresentationCategory.Camera,
        PresentationCategory.ScreenEffect,
        PresentationCategory.CharacterActing
    };

    private AuthoringSession? _session;
    private string? _nodeId;
    private bool _building;

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
        if (_session is null || _session.Project.FindNode(_nodeId) is not PresentationNode presentation)
        {
            LineHost.Children.Clear();
            return;
        }

        _building = true;

        try
        {
            NameBox.Text = presentation.Name;
            LineHost.Children.Clear();

            DialogueNode? dialogue = PresentationBindingResolver.ResolveTarget(
                _session.Project,
                presentation.Id);
            PresentationCommandCatalog catalog = PresentationCommandCatalog.For(_session.Definition);

            if (dialogue is null)
            {
                TargetText.Text = "연결된 DialogueNode가 없습니다. 그래프의 연출 공급 포트를 DialogueNode에 연결하세요.";
            }
            else
            {
                TargetText.Text = $"대상: {dialogue.Name} · {dialogue.Lines.Count}개 LineBox";

                for (int index = 0; index < dialogue.Lines.Count; index++)
                {
                    LineHost.Children.Add(BuildLineCard(presentation, dialogue.Lines[index], index, catalog));
                }
            }

            IReadOnlyList<ResolvedPresentationBinding> orphaned = PresentationBindingResolver
                .Resolve(_session.Project, presentation)
                .Where(item => item.IsOrphan)
                .ToArray();

            if (orphaned.Count > 0)
            {
                LineHost.Children.Add(new TextBlock
                {
                    Text = "연결되지 않은 연출",
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 8, 0, 0)
                });

                foreach (ResolvedPresentationBinding orphan in orphaned)
                {
                    LineHost.Children.Add(BuildOrphanCard(orphan, catalog));
                }
            }
        }
        finally
        {
            _building = false;
        }
    }

    private Control BuildLineCard(
        PresentationNode presentation,
        LineBox line,
        int index,
        PresentationCommandCatalog catalog)
    {
        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(new TextBlock
        {
            Text = $"{index + 1}. {line.Id}",
            FontSize = 11,
            Opacity = 0.6
        });
        content.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(line.Speaker)
                ? line.Text
                : $"{line.Speaker}: {line.Text}",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeight.SemiBold
        });

        foreach (PresentationCategory category in Categories)
        {
            content.Children.Add(BuildCategoryEditor(presentation, line.Id, category, catalog));
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

    private Control BuildCategoryEditor(
        PresentationNode presentation,
        string lineId,
        PresentationCategory category,
        PresentationCommandCatalog catalog)
    {
        IReadOnlyList<PresentationCommandDefinition> choices = catalog.For(category);
        PresentationLineBinding? binding = presentation.Bindings.FirstOrDefault(item =>
            string.Equals(item.LineId, lineId, StringComparison.Ordinal));
        PresentationCommandInstance? command = binding?.Commands.FirstOrDefault(item =>
            catalog.Find(item.DefinitionId)?.Category == category);

        var enabled = new CheckBox
        {
            Content = CategoryName(category),
            IsChecked = command?.IsEnabled == true,
            IsEnabled = choices.Count > 0,
            MinWidth = 128,
            VerticalAlignment = VerticalAlignment.Center
        };

        var combo = new ComboBox
        {
            ItemsSource = choices.Select(item => item.DisplayName).ToArray(),
            SelectedIndex = command is null
                ? (choices.Count > 0 ? 0 : -1)
                : FindDefinitionIndex(choices, command.DefinitionId),
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
                PresentationCommandDefinition definition = choices[index];
                command = _session.Editor.AddPresentationCommand(
                    presentation.Id,
                    lineId,
                    definition.Id,
                    definition.DefaultArguments);
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

            PresentationCommandDefinition definition = choices[combo.SelectedIndex];

            if (command is null)
            {
                if (enabled.IsChecked == true)
                {
                    command = _session.Editor.AddPresentationCommand(
                        presentation.Id,
                        lineId,
                        definition.Id,
                        definition.DefaultArguments);
                }
            }
            else
            {
                _session.Editor.SetPresentationCommandDefinition(
                    presentation.Id,
                    command.Id,
                    definition.Id,
                    definition.DefaultArguments);
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

    private static string CategoryName(PresentationCategory category) => category switch
    {
        PresentationCategory.Camera => "Camera",
        PresentationCategory.ScreenEffect => "ScreenEffect",
        PresentationCategory.CharacterActing => "CharacterActing",
        _ => category.ToString()
    };
}
