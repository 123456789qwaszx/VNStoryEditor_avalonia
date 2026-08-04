using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Vn.App.Services;
using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;

namespace Vn.App.Views;

/// <summary>
/// 설정 노드를 편집한다. 조건을 만드는 유일한 자리다.
///
/// 변수 이름 후보는 <see cref="GameDefinition"/>이 공급한다. 이 화면은 게임의 변수를
/// 하나도 알지 못하고, 정의 파일이 없으면 후보 없이 작가가 직접 적는다.
/// 그래야 같은 도구를 다음 게임에 그대로 가져다 쓸 수 있다.
/// </summary>
public partial class SetNodeEditor : UserControl
{
    private AuthoringSession? _session;
    private string? _nodeId;
    private bool _building;

    public SetNodeEditor()
    {
        InitializeComponent();

        NameBox.LostFocus += (_, _) =>
        {
            if (!_building && _session is not null && _nodeId is not null)
            {
                _session.Editor.RenameNode(_nodeId, NameBox.Text ?? string.Empty);
            }
        };

        AddConditionButton.Click += (_, _) =>
        {
            if (_session is not null && _nodeId is not null)
            {
                _session.Editor.AddCondition(_nodeId, "새 조건", string.Empty);
            }
        };

        AddAssignmentButton.Click += (_, _) => AddAssignment();
    }

    internal void Attach(AuthoringSession session) => _session = session;

    internal void Show(string? nodeId)
    {
        _nodeId = nodeId;
        Rebuild();
    }

    internal string? NodeId => _nodeId;

    internal void Rebuild()
    {
        if (_session is null || _session.Project.FindNode(_nodeId) is not SetNode node)
        {
            ConditionHost.Children.Clear();
            AssignmentHost.Children.Clear();
            return;
        }

        _building = true;

        try
        {
            NameBox.Text = node.Name;

            ConditionHost.Children.Clear();

            foreach (ConditionDefinition condition in node.Conditions)
            {
                ConditionHost.Children.Add(BuildConditionRow(condition));
            }

            AssignmentHost.Children.Clear();

            for (int index = 0; index < node.Assignments.Count; index++)
            {
                AssignmentHost.Children.Add(BuildAssignmentRow(node, index));
            }

            VariableHintText.Text = _session.Definition.Variables.Count == 0
                ? $"{GameDefinition.FileName}이 없으면 변수 이름을 직접 적습니다."
                : $"{GameDefinition.FileName}이 제안하는 변수 {_session.Definition.Variables.Count}개를 쓸 수 있습니다.";
        }
        finally
        {
            _building = false;
        }
    }

    private Control BuildConditionRow(ConditionDefinition condition)
    {
        var name = new TextBox { Text = condition.Name, PlaceholderText = "작가가 읽을 이름", FontSize = 12 };

        var expression = new TextBox
        {
            Text = condition.Expression,
            PlaceholderText = "게임이 평가할 식",
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 12,
            FontFamily = new Avalonia.Media.FontFamily("Cascadia Mono,Consolas")
        };

        void Commit()
        {
            if (!_building)
            {
                _session!.Editor.UpdateCondition(
                    condition.Id,
                    name.Text ?? string.Empty,
                    expression.Text ?? string.Empty);
            }
        }

        name.LostFocus += (_, _) => Commit();
        expression.LostFocus += (_, _) => Commit();

        var remove = new Button { Content = "✕", FontSize = 10, Margin = new Thickness(6, 0, 0, 0) };
        remove.Click += (_, _) => _session!.Editor.RemoveCondition(condition.Id);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto") };
        Grid.SetColumn(name, 0);
        Grid.SetColumn(expression, 1);
        Grid.SetColumn(remove, 2);
        row.Children.Add(name);
        row.Children.Add(expression);
        row.Children.Add(remove);

        return row;
    }

    /// <summary>타입 드롭다운 항목 — X2가 만든 배열에 X7이 bool 한 줄을 더했다.</summary>
    private static readonly (string Type, string Label)[] VariableTypes =
    [
        (VariableAssignment.FloatType, "float (숫자)"),
        (VariableAssignment.BoolType, "bool (플래그)")
    ];

    private Control BuildAssignmentRow(SetNode node, int index)
    {
        VariableAssignment assignment = node.Assignments[index];

        // 값보다 타입이 먼저다 — 무엇을 담는 변수인지부터 정한다.
        var type = new ComboBox
        {
            ItemsSource = VariableTypes.Select(item => item.Label).ToList(),
            SelectedIndex = Math.Max(
                0,
                Array.FindIndex(VariableTypes, item =>
                    string.Equals(item.Type, assignment.Type, StringComparison.Ordinal))),
            FontSize = 11,
            MinWidth = 96,
            VerticalAlignment = VerticalAlignment.Center
        };

        var variable = new AutoCompleteBox
        {
            Text = assignment.Variable,
            PlaceholderText = "변수",
            FontSize = 12,
            Margin = new Thickness(6, 0, 0, 0),
            ItemsSource = _session!.Definition.Variables.Select(item => item.Name).ToList(),
            FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0
        };

        // Bool 플래그의 값은 수치가 아니라 On/Off다 (X7). 저장 값은 Yarn 문법 그대로
        // true/false 문자열이라 출력은 바뀌지 않는다.
        bool isBool = assignment.IsBool;

        var boolValue = new CheckBox
        {
            IsChecked = string.Equals(assignment.Value, "true", StringComparison.OrdinalIgnoreCase),
            Content = string.Equals(assignment.Value, "true", StringComparison.OrdinalIgnoreCase) ? "On" : "Off",
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 12,
            IsVisible = isBool,
            VerticalAlignment = VerticalAlignment.Center
        };

        var value = new TextBox
        {
            Text = assignment.Value,
            PlaceholderText = "초기값",
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 12,
            IsVisible = !isBool
        };

        // Set 편집 슬라이더의 변수별 범위 (X6). 비우면 기본 -5~+5다.
        var sliderMin = new TextBox
        {
            Text = assignment.SliderMin?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PlaceholderText = VariableAssignment.DefaultSliderMin.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 11,
            Width = 44
        };
        ToolTip.SetTip(sliderMin, "슬라이더 최솟값 — 편의 범위이며 직접 입력은 범위 밖도 됩니다.");

        var sliderMax = new TextBox
        {
            Text = assignment.SliderMax?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PlaceholderText = "+" + VariableAssignment.DefaultSliderMax.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Margin = new Thickness(2, 0, 0, 0),
            FontSize = 11,
            Width = 44
        };
        ToolTip.SetTip(sliderMax, "슬라이더 최댓값");

        static double? ParseRange(string? text) =>
            double.TryParse(
                text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double parsed)
                ? parsed
                : null;

        void Commit()
        {
            if (_building)
            {
                return;
            }

            string nextType = VariableTypes[Math.Max(0, type.SelectedIndex)].Type;
            bool nextIsBool = string.Equals(nextType, VariableAssignment.BoolType, StringComparison.Ordinal);

            List<VariableAssignment> next = node.Assignments.Select(item => item.Clone()).ToList();
            next[index] = new VariableAssignment
            {
                Variable = variable.Text ?? string.Empty,
                Value = nextIsBool
                    ? (boolValue.IsChecked == true ? "true" : "false")
                    : value.Text ?? string.Empty,
                Type = nextType,
                SliderMin = ParseRange(sliderMin.Text),
                SliderMax = ParseRange(sliderMax.Text)
            };

            _session!.Editor.SetAssignments(node.Id, next);
        }

        type.SelectionChanged += (_, _) => Commit();
        variable.LostFocus += (_, _) => Commit();
        value.LostFocus += (_, _) => Commit();
        boolValue.IsCheckedChanged += (_, _) =>
        {
            boolValue.Content = boolValue.IsChecked == true ? "On" : "Off";
            Commit();
        };
        sliderMin.LostFocus += (_, _) => Commit();
        sliderMax.LostFocus += (_, _) => Commit();

        var remove = new Button { Content = "✕", FontSize = 10, Margin = new Thickness(6, 0, 0, 0) };

        remove.Click += (_, _) =>
        {
            List<VariableAssignment> next = node.Assignments.Select(item => item.Clone()).ToList();
            next.RemoveAt(index);
            _session!.Editor.SetAssignments(node.Id, next);
        };

        // Bool 플래그에는 슬라이더 범위가 의미 없다.
        sliderMin.IsVisible = !isBool;
        sliderMax.IsVisible = !isBool;

        var valueHost = new Panel();
        valueHost.Children.Add(value);
        valueHost.Children.Add(boolValue);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,*,Auto,Auto,Auto") };
        Grid.SetColumn(type, 0);
        Grid.SetColumn(variable, 1);
        Grid.SetColumn(valueHost, 2);
        Grid.SetColumn(sliderMin, 3);
        Grid.SetColumn(sliderMax, 4);
        Grid.SetColumn(remove, 5);
        row.Children.Add(type);
        row.Children.Add(variable);
        row.Children.Add(valueHost);
        row.Children.Add(sliderMin);
        row.Children.Add(sliderMax);
        row.Children.Add(remove);

        return row;
    }

    private void AddAssignment()
    {
        if (_session?.Project.FindNode(_nodeId) is not SetNode node)
        {
            return;
        }

        List<VariableAssignment> next = node.Assignments.Select(item => item.Clone()).ToList();
        next.Add(new VariableAssignment());
        _session.Editor.SetAssignments(node.Id, next);
    }

}
