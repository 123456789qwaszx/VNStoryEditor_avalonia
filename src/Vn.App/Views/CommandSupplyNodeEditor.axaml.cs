using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Vn.App.Services;
using Vn.Authoring.Definition;
using Vn.Authoring.Model;

namespace Vn.App.Views;

/// <summary>
/// 연출 공급 노드 하나를 편집한다 — 공급 범주 체크, 프리셋 목록, 연출 노드 연결.
///
/// 그래프 드래그 없이 여기서 연결을 만든다. 연결의 실제 저장은 언제나
/// <c>ProjectEditor.AddCommandSupplyLink</c> / <c>SetLinkEnabled</c>를 지난다.
/// </summary>
public partial class CommandSupplyNodeEditor : UserControl
{
    private AuthoringSession? _session;
    private string? _nodeId;
    private bool _building;

    public CommandSupplyNodeEditor()
    {
        InitializeComponent();

        NameBox.LostFocus += (_, _) =>
        {
            if (!_building && _session is not null && _nodeId is not null)
            {
                _session.Editor.RenameNode(_nodeId, NameBox.Text ?? string.Empty);
            }
        };

        AddPresetButton.Click += (_, _) => AddPreset();
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
        if (_session is null ||
            _session.Project.FindNode(_nodeId) is not CommandSupplyNode supply)
        {
            CategoryHost.Children.Clear();
            PresetHost.Children.Clear();
            LinkHost.Children.Clear();
            return;
        }

        _building = true;

        try
        {
            NameBox.Text = supply.Name;
            PresentationCommandCatalog catalog = PresentationCommandCatalog.For(_session.Definition);

            BuildCategoryList(supply, catalog);
            BuildPresetList(supply, catalog);
            BuildLinkList(supply);
        }
        finally
        {
            _building = false;
        }
    }

    private void BuildCategoryList(CommandSupplyNode supply, PresentationCommandCatalog catalog)
    {
        CategoryHost.Children.Clear();

        foreach (PresentationCategoryDefinition category in catalog.Categories)
        {
            string categoryId = category.Id;
            var check = new CheckBox
            {
                Content = $"{category.DisplayName} ({categoryId})",
                FontSize = 12,
                IsChecked = supply.Categories.Contains(categoryId, StringComparer.Ordinal)
            };

            check.IsCheckedChanged += (_, _) =>
            {
                if (_building || _session is null)
                {
                    return;
                }

                List<string> next = supply.Categories.Where(id => id != categoryId).ToList();

                if (check.IsChecked == true)
                {
                    next.Add(categoryId);
                }

                _session.Editor.SetSupplyCategories(supply.Id, next);
            };

            CategoryHost.Children.Add(check);
        }
    }

    private void BuildPresetList(CommandSupplyNode supply, PresentationCommandCatalog catalog)
    {
        PresetHost.Children.Clear();
        IReadOnlyList<PresentationCommandDefinition> choices = catalog.Definitions;

        foreach (CommandPreset preset in supply.Presets)
        {
            PresetHost.Children.Add(BuildPresetCard(supply, preset, catalog, choices));
        }

        AddPresetButton.IsEnabled = choices.Count > 0;
    }

    private Control BuildPresetCard(
        CommandSupplyNode supply,
        CommandPreset preset,
        PresentationCommandCatalog catalog,
        IReadOnlyList<PresentationCommandDefinition> choices)
    {
        var content = new StackPanel { Spacing = 4 };

        var name = new TextBox
        {
            Text = preset.DisplayName,
            PlaceholderText = "프리셋 이름 (예: 폴짝 점프)",
            FontSize = 12
        };
        name.LostFocus += (_, _) =>
        {
            if (!_building && _session is not null)
            {
                _session.Editor.UpdateCommandPreset(supply.Id, preset.Id, displayName: name.Text ?? string.Empty);
            }
        };

        var combo = new ComboBox
        {
            ItemsSource = choices
                .Select(item =>
                    $"{catalog.FindCategory(item.CategoryId)?.DisplayName ?? item.CategoryId} · {item.DisplayName}")
                .ToArray(),
            SelectedIndex = FindIndex(choices, preset.CommandDefinitionId),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (!_building && _session is not null &&
                combo.SelectedIndex >= 0 && combo.SelectedIndex < choices.Count)
            {
                _session.Editor.UpdateCommandPreset(
                    supply.Id,
                    preset.Id,
                    commandDefinitionId: choices[combo.SelectedIndex].Id);
            }
        };

        var arguments = new TextBox
        {
            Text = string.Join(
                Environment.NewLine,
                preset.ArgumentValues.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key} = {pair.Value}")),
            PlaceholderText = "인자 — 한 줄에 하나씩: 이름 = 값",
            AcceptsReturn = true,
            FontSize = 11
        };
        arguments.LostFocus += (_, _) =>
        {
            if (_building || _session is null)
            {
                return;
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (string raw in (arguments.Text ?? string.Empty).Split('\n'))
            {
                string[] parts = raw.Split('=', 2);

                if (parts.Length == 2 && parts[0].Trim().Length > 0)
                {
                    values[parts[0].Trim()] = parts[1].Trim();
                }
            }

            _session.Editor.UpdateCommandPreset(supply.Id, preset.Id, argumentValues: values);
        };

        var remove = new Button
        {
            Content = "✕",
            FontSize = 10,
            Padding = new Thickness(6, 2),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        remove.Click += (_, _) =>
        {
            if (!_building && _session is not null)
            {
                _session.Editor.RemoveCommandPreset(supply.Id, preset.Id);
            }
        };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(name, 0);
        Grid.SetColumn(remove, 1);
        header.Children.Add(name);
        header.Children.Add(remove);

        content.Children.Add(header);
        content.Children.Add(combo);
        content.Children.Add(arguments);

        return new Border
        {
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Color.FromArgb(45, 128, 128, 128)),
            BorderThickness = new Thickness(1),
            Child = content
        };
    }

    private void BuildLinkList(CommandSupplyNode supply)
    {
        LinkHost.Children.Clear();

        List<PresentationNode> presentations = _session!.Project.EnumerateNodes()
            .OfType<PresentationNode>()
            .ToList();

        if (presentations.Count == 0)
        {
            LinkHost.Children.Add(new TextBlock
            {
                Text = "연출 노드가 없습니다.",
                FontSize = 11,
                Opacity = 0.55
            });
            return;
        }

        foreach (PresentationNode presentation in presentations)
        {
            NodeLink? link = _session.Project.Links.FirstOrDefault(item =>
                item.Kind == NodeLinkKind.CommandSupply &&
                string.Equals(item.SourceNodeId, supply.Id, StringComparison.Ordinal) &&
                string.Equals(item.TargetNodeId, presentation.Id, StringComparison.Ordinal));

            var check = new CheckBox
            {
                Content = presentation.Name,
                FontSize = 12,
                IsChecked = link is { IsEnabled: true }
            };

            check.IsCheckedChanged += (_, _) =>
            {
                if (_building || _session is null)
                {
                    return;
                }

                if (check.IsChecked == true)
                {
                    _session.Editor.AddCommandSupplyLink(supply.Id, presentation.Id);
                }
                else if (link is not null)
                {
                    _session.Editor.SetLinkEnabled(link.Id, enabled: false);
                }
            };

            LinkHost.Children.Add(check);
        }
    }

    private void AddPreset()
    {
        if (_building || _session is null || _nodeId is null)
        {
            return;
        }

        PresentationCommandCatalog catalog = PresentationCommandCatalog.For(_session.Definition);

        if (catalog.Definitions.Count == 0)
        {
            return;
        }

        PresentationCommandDefinition definition = catalog.Definitions[0];
        _session.Editor.AddCommandPreset(
            _nodeId,
            definition.Id,
            displayName: definition.DisplayName,
            argumentValues: definition.DefaultArgumentValues());
    }

    private static int FindIndex(
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
