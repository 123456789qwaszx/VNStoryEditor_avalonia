using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Vn.Authoring.Flow;
using Vn.Authoring.Results;

namespace Vn.App.Views;

/// <summary>
/// 선택 라인 디테일 편집 (2026-08-20 소유자: "우측 연출 편집 공간을 터미널 아래로 —
/// 노드의 모든 라인을 훑는 게 아니라 <b>지금 선택한 LineId의 연출만</b> 디테일하게").
///
/// 터미널이 시나리오 전체의 지도라면 여기는 현재 라인의 작업대다: 그 라인의 커맨드가
/// 줄줄이 서고, 점(●) = 상세조절 · 더블클릭 = 텍스트 편집 · ✕ = 삭제 · 아래 입력줄 = 추가.
/// 신호만 낸다 — 실행은 호스트(프리뷰 패널)가 편집 통로로 소화한다.
/// </summary>
internal sealed class LineDetailPanel : UserControl
{
    private readonly TextBlock _header = new()
    {
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
        TextWrapping = TextWrapping.Wrap
    };

    private readonly StackPanel _commands = new() { Spacing = 2 };
    private readonly TextBox _addInput = new()
    {
        FontSize = 11,
        FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
        PlaceholderText = "커맨드 추가 — 예: move_by c1 +2u 0u 12fr (Enter)",
        Padding = new Thickness(4, 2)
    };

    private string? _lineId;

    public event Action<PresentationResultCommand>? CommandDotClicked;

    public event Action<PresentationResultCommand, string>? CommandTextEdited;

    public event Action<PresentationResultCommand>? CommandRemoveRequested;

    /// <summary>입력줄 확정 — 이 라인에 이 텍스트의 커맨드를 추가해 달라.</summary>
    public event Action<string, string>? CommandAddRequested;

    public LineDetailPanel()
    {
        _addInput.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter && _lineId is { } lineId &&
                _addInput.Text is { Length: > 0 } text)
            {
                CommandAddRequested?.Invoke(lineId, text);
                _addInput.Text = string.Empty;
                args.Handled = true;
            }
        };

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(22, 25, 32)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    _header,
                    new ScrollViewer
                    {
                        MaxHeight = 180,
                        HorizontalScrollBarVisibility =
                            Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        Content = _commands
                    },
                    _addInput
                }
            }
        };
    }

    /// <summary>
    /// 선택 라인의 것만 싣는다 — <paramref name="rows"/>에서 그 LineId의 커맨드 행을 걸러 쓴다
    /// (터미널과 같은 원천 하나, 별도 모델 없음).
    /// </summary>
    public void Show(
        IReadOnlyList<PresentationScriptRow>? rows, string? selectedLineId, bool editable)
    {
        _commands.Children.Clear();
        _lineId = selectedLineId;

        if (rows is null || selectedLineId is null)
        {
            IsVisible = false;
            return;
        }

        IsVisible = true;

        PresentationScriptRow? dialogue = rows.FirstOrDefault(row =>
            row.Kind == PresentationScriptRowKind.Dialogue &&
            string.Equals(row.LineId, selectedLineId, StringComparison.Ordinal));
        _header.Text = dialogue is null ? selectedLineId : $"▸ {dialogue.Text}";

        var lineCommands = rows
            .Where(row => row.Kind == PresentationScriptRowKind.Command &&
                          string.Equals(row.LineId, selectedLineId, StringComparison.Ordinal))
            .ToArray();

        if (lineCommands.Length == 0)
        {
            _commands.Children.Add(new TextBlock
            {
                Text = "이 라인에는 연출이 없습니다 — 아래 입력줄이나 무대 직접 조작으로 답니다.",
                FontSize = 10,
                Opacity = 0.55,
                Foreground = Brushes.Gainsboro,
                TextWrapping = TextWrapping.Wrap
            });
        }

        foreach (PresentationScriptRow row in lineCommands)
        {
            _commands.Children.Add(BuildCommandRow(row, editable));
        }

        _addInput.IsVisible = editable;
    }

    private Control BuildCommandRow(PresentationScriptRow row, bool editable)
    {
        var dot = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = new SolidColorBrush(editable
                ? Color.FromArgb(230, 250, 204, 21)
                : Color.FromArgb(90, 148, 163, 184)),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 6, 0)
        };

        var text = new TextBlock
        {
            Text = row.Text,
            FontSize = 11,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
            Foreground = new SolidColorBrush(Color.FromRgb(125, 207, 252)),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        var remove = new Button
        {
            Content = "✕",
            FontSize = 9,
            Padding = new Thickness(4, 0),
            IsVisible = editable,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        ToolTip.SetTip(remove, "이 커맨드를 지웁니다.");

        var layout = new DockPanel();
        DockPanel.SetDock(dot, Dock.Left);
        DockPanel.SetDock(remove, Dock.Right);
        layout.Children.Add(dot);
        layout.Children.Add(remove);
        layout.Children.Add(text);

        var host = new Border { Background = Brushes.Transparent, Child = layout };

        if (!editable || row.Command is not { } command)
        {
            return host;
        }

        dot.Cursor = new Cursor(StandardCursorType.Hand);
        remove.Click += (_, _) => CommandRemoveRequested?.Invoke(command);
        ToolTip.SetTip(host, "점 = 상세조절 · 더블클릭 = 텍스트 편집 · ✕ = 삭제");

        host.PointerPressed += (_, args) =>
        {
            Point position = args.GetPosition(host);

            if (args.ClickCount >= 2)
            {
                BeginInlineEdit(layout, text, command);
                args.Handled = true;
            }
            else if (position.X <= 16)
            {
                CommandDotClicked?.Invoke(command);
                args.Handled = true;
            }
        };

        return host;
    }

    private void BeginInlineEdit(DockPanel layout, TextBlock text, PresentationResultCommand command)
    {
        var input = new TextBox
        {
            Text = text.Text,
            FontSize = 11,
            FontFamily = text.FontFamily,
            MinWidth = 200,
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
