using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Vn.Authoring.Chapters;

namespace Vn.App.Views;

/// <summary>
/// `스탯변화` 줄 편집기 — <c>[스탯 ▾] [＋/－ ▾] [수치] [✕]</c> (2026-08-17 소유자:
/// "엑셀에서 정의해둔 스탯을 드롭다운으로 선택하고 부호와 값을 넣는거야").
///
/// 대사노드의 Set 편집기와 같은 감각이다 — 사람은 <b>고르고 누르지</b> 문법을 외우지
/// 않는다. 원천은 여전히 챕터 엑셀 `간선` 시트의 `스탯변화` 칸이고, 이 편집기는 그
/// 칸에 들어갈 글(<c>trust +1; anger -1</c>)을 만드는 손일 뿐이다 — 새 저장소가 아니다.
///
/// <b>줄은 자리가 아니라 물건이다.</b> 처음엔 줄을 목록의 <i>번호</i>로 잡았는데, 다시
/// 그릴 때마다 그 번호가 낡아 엉뚱한 줄을 고치거나 없는 줄을 짚었다(테스트가 둘 다 잡음).
/// 지금은 줄 하나가 <see cref="Row"/> 하나이고, 사건 처리기는 그 물건을 잡는다 — 목록에서
/// 빠진 줄의 처리기가 뒤늦게 울어도 고아 물건을 고칠 뿐 남의 줄을 건드리지 않는다.
///
/// 수치는 <b>낼 때 컨트롤에서 읽는다.</b> 글자마다 모델을 좇으면 사건 순서에 기대게 되고,
/// 그 기대가 깨지면 사람이 친 값이 조용히 사라진다(실제로 그렇게 깨졌다).
/// </summary>
internal sealed class StatChangeEditor : StackPanel
{
    /// <summary>줄 하나. 컨트롤이 서 있으면 값의 원본은 컨트롤이고, 씨앗은 그리기 전 상태다.</summary>
    private sealed class Row
    {
        public required string Key { get; set; }

        /// <summary>다시 그리기 전에 거둬 둔 값 — 컨트롤이 없을 때의 원본.</summary>
        public required int Seed { get; set; }

        public ComboBox? Sign { get; set; }
        public TextBox? Amount { get; set; }
    }

    private readonly List<Row> _rows = [];
    private IReadOnlyList<ChapterStat> _stats = [];
    private bool _rendering;

    public StatChangeEditor() => Spacing = 3;

    /// <summary>
    /// 사람이 값을 고쳤다. <see cref="Load"/>·다시 그리기로는 울리지 않는다 — 화면을
    /// 채우는 일이 저장을 부르면 아무것도 안 고쳤는데 파일이 써진다.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>읽기 전용(엑셀에서만 편집)이면 드롭다운·단추가 잠긴다.</summary>
    public bool Editable { get; set; } = true;

    /// <summary>이 챕터의 스탯 목록과 지금 값으로 편집기를 채운다.</summary>
    public void Load(IReadOnlyList<ChapterStat> stats, IReadOnlyList<StatDelta> deltas)
    {
        _stats = stats;
        _rows.Clear();

        // 스탯 시트에서 사라진 키도 지우지 않는다 — 사람이 적어 둔 값을 툴이 말없이
        // 없애면 안 된다(리더가 오류로 지목하는 쪽이 옳다).
        foreach (StatDelta delta in deltas)
        {
            _rows.Add(new Row { Key = delta.Key, Seed = delta.Amount });
        }

        Render();
    }

    /// <summary>시트에 그대로 들어갈 글 — <c>trust +1; anger -1</c>.</summary>
    public string ToSheetText()
    {
        Harvest();

        return string.Join("; ", _rows
            .Select(row => $"{row.Key} {(row.Seed >= 0 ? "+" : "-")}{Math.Abs(row.Seed)}"));
    }

    /// <summary>컨트롤에 있는 값을 씨앗으로 거둔다 — 다시 그리기·내보내기 직전에 부른다.</summary>
    private void Harvest()
    {
        foreach (Row row in _rows)
        {
            if (row.Sign is null)
            {
                continue;
            }

            int sign = row.Sign.SelectedIndex == 1 ? -1 : 1;

            // bool 스탯은 수치칸이 없다 — 부호가 곧 켬(+1)·끔(−1)이다.
            int magnitude = row.Amount is { IsVisible: true } box
                ? int.TryParse(
                    box.Text, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int parsed) ? parsed : 0
                : 1;

            row.Seed = sign * magnitude;
        }
    }

    private void Render()
    {
        _rendering = true;

        try
        {
            Children.Clear();

            foreach (Row row in _rows)
            {
                row.Sign = null;
                row.Amount = null;
            }

            if (_stats.Count == 0)
            {
                Children.Add(Hint("`스탯` 시트에 스탯이 없습니다 — 엑셀에서 먼저 선언하세요."));
                return;
            }

            if (_rows.Count == 0)
            {
                Children.Add(Hint("이 길을 건너도 스탯이 변하지 않습니다."));
            }

            foreach (Row row in _rows)
            {
                Children.Add(BuildRow(row));
            }

            var add = new Button
            {
                Content = "＋ 스탯변화",
                FontSize = 10,
                Padding = new Thickness(7, 1),
                HorizontalAlignment = HorizontalAlignment.Left,
                IsEnabled = Editable,
                [ToolTip.TipProperty] =
                    "이 길을 건너는 순간 1회 반영됩니다. 스탯은 챕터 엑셀 `스탯` 시트에서 선언한 것만 고를 수 있습니다."
            };

            add.Click += (_, _) =>
            {
                Harvest();

                // 아직 안 쓴 스탯을 먼저 권한다 — 같은 키를 두 줄에 적는 실수가 흔하다.
                ChapterStat pick = _stats.FirstOrDefault(stat =>
                    _rows.All(row => row.Key != stat.Key)) ?? _stats[0];

                _rows.Add(new Row { Key = pick.Key, Seed = 1 });
                Render();
                Changed?.Invoke(this, EventArgs.Empty);
            };

            Children.Add(add);
        }
        finally
        {
            _rendering = false;
        }
    }

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 10,
        Opacity = 0.5,
        TextWrapping = TextWrapping.Wrap
    };

    private Control BuildRow(Row row)
    {
        ChapterStat? stat = _stats.FirstOrDefault(candidate => candidate.Key == row.Key);
        bool isBool = stat?.Type == ChapterStatType.Bool;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };

        var statCombo = new ComboBox
        {
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = Editable,
            ItemsSource = _stats.Select(Display).ToList(),
            SelectedItem = stat is null ? null : Display(stat),
            PlaceholderText = row.Key // 시트에서 사라진 키도 무엇이었는지는 보인다
        };
        statCombo.SelectionChanged += (_, _) =>
        {
            if (_rendering || statCombo.SelectedIndex < 0)
            {
                return;
            }

            ChapterStat picked = _stats[statCombo.SelectedIndex];

            if (picked.Key == row.Key)
            {
                return;
            }

            Harvest();
            row.Key = picked.Key;

            // 종류가 바뀌면 수치를 물려받지 않는다 — bool 줄에 +5가 남는 사고를 여기서 끊는다.
            if (picked.Type == ChapterStatType.Bool)
            {
                row.Seed = row.Seed >= 0 ? 1 : -1;
            }

            Render();
            Changed?.Invoke(this, EventArgs.Empty);
        };
        Grid.SetColumn(statCombo, 0);
        grid.Children.Add(statCombo);

        // bool 스탯은 부호가 곧 켬/끔이다 — `+1`이라 적어 두면 무슨 뜻인지 알 수 없다.
        var signCombo = new ComboBox
        {
            FontSize = 11,
            MinWidth = isBool ? 74 : 54,
            Margin = new Thickness(4, 0, 0, 0),
            IsEnabled = Editable,
            ItemsSource = isBool ? new[] { "켬", "끔" } : ["＋", "－"],
            SelectedIndex = row.Seed >= 0 ? 0 : 1
        };
        signCombo.SelectionChanged += (_, _) =>
        {
            if (!_rendering)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };
        Grid.SetColumn(signCombo, 1);
        grid.Children.Add(signCombo);

        var amountBox = new TextBox
        {
            FontSize = 11,
            Width = 56,
            Margin = new Thickness(4, 0, 0, 0),
            IsEnabled = Editable,
            IsVisible = !isBool,
            Text = Math.Abs(row.Seed).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        // 초점을 잃을 때만 낸다 — 자판 하나마다 워크북을 열면 쓰던 숫자가 끊긴다.
        amountBox.LostFocus += (_, _) =>
        {
            if (!_rendering)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };
        amountBox.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                e.Handled = true;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };
        Grid.SetColumn(amountBox, 2);
        grid.Children.Add(amountBox);

        row.Sign = signCombo;
        row.Amount = amountBox;

        var remove = new Button
        {
            Content = "✕",
            FontSize = 10,
            Padding = new Thickness(5, 1),
            Margin = new Thickness(4, 0, 0, 0),
            IsEnabled = Editable,
            [ToolTip.TipProperty] = "이 줄을 지웁니다."
        };
        remove.Click += (_, _) =>
        {
            Harvest();
            _rows.Remove(row);
            Render();
            Changed?.Invoke(this, EventArgs.Empty);
        };
        Grid.SetColumn(remove, 3);
        grid.Children.Add(remove);

        return grid;
    }

    /// <summary>드롭다운에 보이는 이름 — 표시명이 있으면 그것, 없으면 키.</summary>
    private static string Display(ChapterStat stat) =>
        string.IsNullOrWhiteSpace(stat.DisplayName) || stat.DisplayName == stat.Key
            ? stat.Key
            : $"{stat.DisplayName} ({stat.Key})";
}
