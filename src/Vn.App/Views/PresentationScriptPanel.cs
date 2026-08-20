using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Vn.Authoring.Flow;
using Vn.Authoring.Results;

namespace Vn.App.Views;

/// <summary>
/// 연출 대본 텍스트 패널 (2026-08-20 소유자: "프리뷰 왼쪽에 텍스트 로그 터미널") —
/// 시나리오 전체가 텍스트로 미리 적혀 있고:
///
/// - <b>현재 라인의 구간</b>(그 라인의 커맨드들 + 대사 한 줄)은 반투명 배경 박스로 구분된다
/// - 커맨드 행 왼쪽의 <b>동그란 점</b>(Rider 브레이크포인트 감각)을 누르면 상세조절이 열린다
/// - 커맨드 행을 <b>더블클릭</b>하면 그 자리에서 텍스트로 고친다(Enter 적용 · Esc 취소)
/// - 대사 행 클릭 = 그 라인 선택
///
/// 이 패널은 그리기와 신호뿐이다 — 편집의 실행(파싱·적용·선택 이동)은 호스트가 진다.
/// </summary>
internal sealed class PresentationScriptPanel : UserControl
{
    private readonly StackPanel _rows = new() { Spacing = 1 };
    private readonly ScrollViewer _scroll;
    private Border? _selectedGroup;

    /// <summary>대사 행 클릭 — 그 라인을 선택해 달라.</summary>
    public event Action<string>? LineClicked;

    /// <summary>커맨드 점 클릭 — 이 커맨드의 상세조절을 열어 달라.</summary>
    public event Action<PresentationResultCommand>? CommandDotClicked;

    /// <summary>커맨드 인라인 편집 확정 — 이 커맨드를 이 텍스트로 고쳐 달라.</summary>
    public event Action<PresentationResultCommand, string>? CommandTextEdited;

    public PresentationScriptPanel()
    {
        _scroll = new ScrollViewer
        {
            Content = _rows,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(17, 19, 24)), // 터미널 감각의 어두운 판
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 4),
            Child = _scroll
        };
    }

    public void Show(IReadOnlyList<PresentationScriptRow>? rows, string? selectedLineId, bool editable)
    {
        _rows.Children.Clear();
        _selectedGroup = null;

        if (rows is null || rows.Count == 0)
        {
            IsVisible = false;
            return;
        }

        IsVisible = true;

        // 라인 단위로 묶어 컨테이너 하나(= 하이라이트 박스 단위)로 만든다.
        // Setup 구획(LineId null)도 한 묶음이다.
        int index = 0;

        while (index < rows.Count)
        {
            string? lineId = rows[index].LineId;
            var group = new StackPanel { Spacing = 1 };

            while (index < rows.Count &&
                   string.Equals(rows[index].LineId, lineId, StringComparison.Ordinal))
            {
                group.Children.Add(BuildRow(rows[index], editable));
                index++;
            }

            bool selected = lineId is not null &&
                string.Equals(lineId, selectedLineId, StringComparison.Ordinal);

            var container = new Border
            {
                Background = selected
                    ? new SolidColorBrush(Color.FromArgb(60, 125, 211, 252)) // 반투명 박스
                    : Brushes.Transparent,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(2, 2),
                Child = group
            };

            if (selected)
            {
                _selectedGroup = container;
            }

            _rows.Children.Add(container);
        }

        // 현재 구간이 보이게 — 렌더 뒤에 스크롤한다.
        if (_selectedGroup is { } target)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => target.BringIntoView(), Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    private Control BuildRow(PresentationScriptRow row, bool editable)
    {
        return row.Kind switch
        {
            PresentationScriptRowKind.SectionHeader => new TextBlock
            {
                Text = row.Text,
                FontSize = 10,
                Opacity = 0.5,
                Foreground = Brushes.Gainsboro,
                Margin = new Thickness(14, 4, 0, 2)
            },
            PresentationScriptRowKind.Command => BuildCommandRow(row, editable),
            _ => BuildDialogueRow(row)
        };
    }

    private Control BuildDialogueRow(PresentationScriptRow row)
    {
        var text = new TextBlock
        {
            Text = row.Text,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(14, 3, 4, 3)
        };

        var host = new Border { Background = Brushes.Transparent, Child = text };
        ToolTip.SetTip(host, "클릭하면 이 라인을 선택합니다.");
        host.Cursor = new Cursor(StandardCursorType.Hand);
        host.PointerPressed += (_, args) =>
        {
            if (row.LineId is { } lineId)
            {
                LineClicked?.Invoke(lineId);
                args.Handled = true;
            }
        };

        return host;
    }

    private Control BuildCommandRow(PresentationScriptRow row, bool editable)
    {
        // Rider 브레이크포인트 감각의 점 — 상세조절 입구.
        var dot = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = new SolidColorBrush(editable
                ? Color.FromArgb(230, 250, 204, 21)
                : Color.FromArgb(90, 148, 163, 184)),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 4, 0)
        };

        var text = new TextBlock
        {
            Text = row.Text,
            FontSize = 11,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
            Foreground = new SolidColorBrush(Color.FromRgb(125, 207, 252)),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        var layout = new DockPanel { Margin = new Thickness(0, row.StartsGroup ? 6 : 0, 0, 0) };
        DockPanel.SetDock(dot, Dock.Left);
        layout.Children.Add(dot);
        layout.Children.Add(text);

        var host = new Border { Background = Brushes.Transparent, Child = layout };

        if (!editable || row.Command is not { } command)
        {
            return host;
        }

        dot.Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(host, "점 = 상세조절 · 더블클릭 = 텍스트로 고치기");

        host.PointerPressed += (_, args) =>
        {
            Point position = args.GetPosition(host);

            if (args.ClickCount >= 2)
            {
                BeginInlineEdit(layout, text, command);
                args.Handled = true;
            }
            else if (position.X <= 14)
            {
                CommandDotClicked?.Invoke(command);
                args.Handled = true;
            }
        };

        return host;
    }

    /// <summary>텍스트 자리 편집 — Enter 적용, Esc/포커스 이탈 취소. 적용의 성패는 호스트가 알린다.</summary>
    private void BeginInlineEdit(DockPanel layout, TextBlock text, PresentationResultCommand command)
    {
        var input = new TextBox
        {
            Text = text.Text,
            FontSize = 11,
            FontFamily = text.FontFamily,
            MinWidth = 220,
            Padding = new Thickness(2),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        void EndEdit(bool apply)
        {
            if (!layout.Children.Contains(input))
            {
                return;
            }

            layout.Children.Remove(input);
            layout.Children.Add(text);

            if (apply && input.Text is { } edited &&
                !string.Equals(edited, text.Text, StringComparison.Ordinal))
            {
                CommandTextEdited?.Invoke(command, edited);
            }
        }

        input.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                EndEdit(apply: true);
                args.Handled = true;
            }
            else if (args.Key == Key.Escape)
            {
                EndEdit(apply: false);
                args.Handled = true;
            }
        };
        input.LostFocus += (_, _) => EndEdit(apply: false);

        layout.Children.Remove(text);
        layout.Children.Add(input);
        input.Focus();
        input.SelectAll();
    }
}
