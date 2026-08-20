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
/// - <b>Setup 구획과 라인 구획이 분리</b>돼 있다(다른 바탕 + 구분선)
/// - <b>좌클릭 한 번</b>이 커맨드 행 선택이다(그 라인도 함께 선택된다)
/// - 커맨드 행을 <b>드래그</b>하면 자리를 옮긴다 — 같은 라인 안·라인 사이·Setup까지 자유
/// - 왼쪽 <b>동그란 점</b>(Rider 브레이크포인트 감각)은 상세조절 — 텍스트·인자 편집은 아래 작업대의 몫이다
/// - 대사 행 클릭 = 그 라인 선택
///
/// 이 패널은 그리기와 신호뿐이다 — 편집의 실행(파싱·적용·이동·선택)은 호스트가 진다.
/// </summary>
internal sealed class PresentationScriptPanel : UserControl
{
    private readonly StackPanel _rows = new() { Spacing = 1 };
    private readonly ScrollViewer _scroll;
    private Border? _selectedGroup;
    private string? _selectedCommandId;

    /// <summary>드롭 자리 계산용 — 화면 순서 그대로의 행 메타. 커맨드 행과 대사 행만 담는다.</summary>
    private readonly List<(Border Host, PresentationScriptRow Row, string? TargetLineId, int InsertIndex)> _dropRows = new();

    private Border? _dropCandidate;
    private bool _dropBefore;

    // 드래그 제스처 상태.
    private PresentationResultCommand? _pressedCommand;
    private Border? _pressedHost;
    private Point _pressedAt;
    private bool _dragging;

    /// <summary>대사 행 클릭 또는 커맨드 선택 — 그 라인을 선택해 달라.</summary>
    public event Action<string>? LineClicked;

    /// <summary>Setup 구획 클릭 — 작업대에 Setup 편집을 열어 달라 (2026-08-21).</summary>
    public event Action? SetupClicked;

    /// <summary>
    /// 커맨드 점 클릭 (2026-08-21 소유자: "세부 디테일 대신에 키고 끄는기능") —
    /// 이 커맨드를 켜고 꺼 달라. bool은 지금 값(활성 여부)이다.
    /// </summary>
    public event Action<PresentationResultCommand, bool>? CommandToggleRequested;

    /// <summary>커맨드 선택 확정 — 아래 작업대(Inspector)를 이 커맨드로 바꿔 달라.</summary>
    public event Action<PresentationResultCommand>? CommandSelected;

    /// <summary>행 우측 X 클릭 — 이 커맨드를 제거해 달라 (2026-08-21).</summary>
    public event Action<PresentationResultCommand>? CommandRemoveRequested;

    /// <summary>드래그 이동 확정 — targetLineId(null=Setup)의 insertIndex 자리로 옮겨 달라.</summary>
    public event Action<PresentationResultCommand, string?, int>? CommandMoveRequested;

    /// <summary>지금 노란 띠가 선 커맨드 — 작업대 표시 동기화용. 없으면 null.</summary>
    internal string? SelectedCommandId => _selectedCommandId;

    public PresentationScriptPanel()
    {
        // 스크롤바가 행 위에 겹쳐 뜨므로 오른쪽·아래를 비워 둔다 (2026-08-21 소유자:
        // "두꺼운 바가 삭제 버튼을 가려버려서") — X가 바 밑에 깔리지 않는 자리다.
        _rows.Margin = new Thickness(0, 0, 16, 8);

        _scroll = new ScrollViewer
        {
            Content = _rows,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(17, 19, 24)), // 터미널 감각의 어두운 판
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 10), // 빽빽함 완화 (2026-08-21 소유자: "여백도 너무 빽빽")
            Child = _scroll
        };
    }

    public void Show(
        IReadOnlyList<PresentationScriptRow>? rows,
        string? selectedLineId,
        bool editable,
        bool setupSelected = false)
    {
        _rows.Children.Clear();
        _dropRows.Clear();
        _selectedGroup = null;
        _dropCandidate = null;
        _pressedCommand = null;
        _dragging = false;

        if (rows is null || rows.Count == 0)
        {
            IsVisible = false;
            return;
        }

        IsVisible = true;

        // 선택 커맨드가 지금 선택된 구획 밖이면 걷는다 — 라인이 넘어갔는데(재생·이동)
        // 다른 라인의 커맨드가 Inspector에 남아 있으면 화면이 거짓말한다.
        if (_selectedCommandId is not null && !rows.Any(row =>
                row.Kind == PresentationScriptRowKind.Command &&
                string.Equals(row.Command?.CommandId, _selectedCommandId, StringComparison.Ordinal) &&
                (row.LineId is null
                    ? setupSelected
                    : string.Equals(row.LineId, selectedLineId, StringComparison.Ordinal))))
        {
            _selectedCommandId = null;
        }

        // 라인 단위로 묶어 컨테이너 하나(= 하이라이트 박스 단위)로 만든다.
        // Setup 구획(LineId null)은 라인들과 분리된 제 공간을 갖는다(소유자 다듬기 1).
        int index = 0;

        while (index < rows.Count)
        {
            string? lineId = rows[index].LineId;
            bool isSetup = lineId is null;
            var group = new StackPanel { Spacing = 1 };
            int commandIndex = 0;

            if (!isSetup)
            {
                // 라인 박스 위쪽의 lineId 헤더 (2026-08-21 소유자: "-setup-과 동일하게") —
                // 대사 텍스트는 지금 자리(박스 아래쪽) 그대로다.
                group.Children.Add(BuildLineHeader(lineId!));
            }

            // 제거 X는 선택된 구획에서만 보인다 (2026-08-21 소유자: "특정 라인을
            // 클릭했을 때만 x가 보이도록") — 다른 라인은 읽기 화면처럼 고요하다.
            // 보이지 않을 때도 자리는 그대로 잡는다(아래) — 클릭마다 줄이 흔들리지 않게.
            bool showRemove = editable && (isSetup
                ? setupSelected
                : !setupSelected && string.Equals(lineId, selectedLineId, StringComparison.Ordinal));

            while (index < rows.Count &&
                   string.Equals(rows[index].LineId, lineId, StringComparison.Ordinal))
            {
                PresentationScriptRow row = rows[index];
                Control rowControl = BuildRow(row, editable, showRemove);
                group.Children.Add(rowControl);

                if (rowControl is Border host)
                {
                    if (row.Kind == PresentationScriptRowKind.Command)
                    {
                        _dropRows.Add((host, row, lineId, commandIndex));
                        commandIndex++;
                    }
                    else if (row.Kind == PresentationScriptRowKind.Dialogue)
                    {
                        // 대사 행 = 그 라인 커맨드 목록의 끝자리(대사 앞에 끼운다).
                        _dropRows.Add((host, row, lineId, commandIndex));
                    }
                }

                index++;
            }

            // 선택은 하나다 (2026-08-21 소유자: "하이라이트가 2군데에 잡혀") — Setup을
            // 고르면 라인 하이라이트는 꺼진다. 라인 선택 정보는 그대로 살아 있다가
            // Setup에서 벗어나면 다시 그 라인이 켜진다.
            bool selected = lineId is not null && !setupSelected &&
                string.Equals(lineId, selectedLineId, StringComparison.Ordinal);

            var container = new Border
            {
                Background = selected || (isSetup && setupSelected)
                    ? new SolidColorBrush(Color.FromArgb(60, 125, 211, 252)) // 반투명 박스
                    : isSetup
                        ? new SolidColorBrush(Color.FromArgb(70, 40, 44, 56)) // Setup 전용 바탕
                        : Brushes.Transparent,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 3),
                Margin = isSetup ? new Thickness(0, 0, 0, 4) : default,
                Child = group
            };

            if (selected)
            {
                _selectedGroup = container;
            }

            if (isSetup && editable)
            {
                // 빈 자리 클릭도 Setup 선택이다 — 행 자신이 소화한 클릭은 여기 안 온다.
                container.Cursor = new Cursor(StandardCursorType.Hand);
                container.PointerPressed += (_, args) =>
                {
                    _selectedCommandId = null;
                    SetupClicked?.Invoke();
                    args.Handled = true;
                };
            }

            _rows.Children.Add(container);

            if (isSetup)
            {
                // Setup과 라인 공간의 구분선.
                _rows.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(0, 2, 0, 6),
                    Background = new SolidColorBrush(Color.FromArgb(120, 90, 96, 110))
                });
            }
        }

        // 현재 구간이 보이게 — 렌더 뒤에 스크롤한다.
        if (_selectedGroup is { } target)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => target.BringIntoView(), Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    private Control BuildRow(PresentationScriptRow row, bool editable, bool showRemove)
    {
        return row.Kind switch
        {
            PresentationScriptRowKind.SectionHeader => BuildSectionHeader(row, editable),
            PresentationScriptRowKind.Command => BuildCommandRow(row, editable, showRemove),
            PresentationScriptRowKind.Actor => BuildActorRow(row),
            _ => BuildDialogueRow(row)
        };
    }

    /// <summary>
    /// 라인 단위 액터 선언 <c>&lt;&lt;actor @2 willow&gt;&gt;</c> — 읽기용 표시라
    /// 점·X·드래그가 없다. 커맨드와 다른 색으로 "선언"임을 알린다.
    /// </summary>
    private static TextBlock BuildActorRow(PresentationScriptRow row) => new()
    {
        Text = row.Text,
        FontSize = 11,
        FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
        Foreground = new SolidColorBrush(Color.FromRgb(134, 239, 172)),
        Opacity = 0.8,
        Margin = new Thickness(14, row.StartsGroup ? 6 : 0, 0, 0)
    };

    private TextBlock BuildSectionHeader(PresentationScriptRow row, bool editable)
    {
        var header = new TextBlock
        {
            Text = row.Text,
            FontSize = 10,
            Opacity = 0.5,
            Foreground = Brushes.Gainsboro,
            Margin = new Thickness(14, 4, 0, 2)
        };

        if (editable && row.LineId is null)
        {
            // Setup 헤더 클릭 = 작업대에 Setup 편집 (2026-08-21 소유자 지시).
            header.Cursor = new Cursor(StandardCursorType.Hand);
            header.PointerPressed += (_, args) =>
            {
                _selectedCommandId = null;
                SetupClicked?.Invoke();
                args.Handled = true;
            };
        }

        return header;
    }

    /// <summary>라인 박스 위쪽의 lineId 헤더 — Setup 헤더와 같은 결. 클릭 = 그 라인 선택.</summary>
    private TextBlock BuildLineHeader(string lineId)
    {
        var header = new TextBlock
        {
            Text = $"── {lineId} ──",
            FontSize = 10,
            Opacity = 0.5,
            Foreground = Brushes.Gainsboro,
            Margin = new Thickness(14, 4, 0, 2),
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        header.PointerPressed += (_, args) =>
        {
            _selectedCommandId = null;
            LineClicked?.Invoke(lineId);
            args.Handled = true;
        };

        return header;
    }

    private Border BuildDialogueRow(PresentationScriptRow row)
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
        host.Cursor = new Cursor(StandardCursorType.Hand);
        host.PointerPressed += (_, args) =>
        {
            if (row.LineId is { } lineId)
            {
                // 라인 선택 = 커맨드 선택 해제 — 작업대가 그 라인의 추가 입구로 돌아간다.
                _selectedCommandId = null;
                LineClicked?.Invoke(lineId);
                args.Handled = true;
            }
        };

        return host;
    }

    private Border BuildCommandRow(PresentationScriptRow row, bool editable, bool showRemove)
    {
        // Rider 브레이크포인트 감각의 점 — 켜고 끄기 토글 (2026-08-21 소유자:
        // "세부 디테일 대신에 키고 끄는기능"). 찬 점 = 켜짐, 빈 점 = 꺼짐.
        var dot = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = row.IsEnabled
                ? new SolidColorBrush(editable
                    ? Color.FromArgb(230, 250, 204, 21)
                    : Color.FromArgb(90, 148, 163, 184))
                : Brushes.Transparent,
            Stroke = row.IsEnabled
                ? null
                : new SolidColorBrush(Color.FromArgb(160, 250, 204, 21)),
            StrokeThickness = row.IsEnabled ? 0 : 1.5,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 4, 0)
        };

        bool isSelected = row.Command is { } own &&
            string.Equals(own.CommandId, _selectedCommandId, StringComparison.Ordinal);

        var text = new TextBlock
        {
            Text = row.Text,
            FontSize = 11,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
            Foreground = new SolidColorBrush(Color.FromRgb(125, 207, 252)),
            Opacity = row.IsEnabled ? 1.0 : 0.45, // 꺼진 커맨드는 흐리게 — 행은 남는다
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        var layout = new DockPanel { Margin = new Thickness(0, row.StartsGroup ? 6 : 0, 0, 0) };
        DockPanel.SetDock(dot, Dock.Left);
        layout.Children.Add(dot);
        layout.Children.Add(text);

        var host = new Border
        {
            Background = isSelected
                ? new SolidColorBrush(Color.FromArgb(70, 250, 204, 21)) // 선택 = 노란 띠
                : Brushes.Transparent,
            CornerRadius = new CornerRadius(2),
            Child = layout
        };

        if (!editable || row.Command is not { } command)
        {
            return host;
        }

        dot.Cursor = new Cursor(StandardCursorType.Hand);

        // 행 우측 끝의 제거 X (2026-08-21 소유자: "터미널 라인 우측 끝에 X", 같은 날:
        // "특정 라인을 클릭했을 때만") — 선택된 구획에서만 <b>보이되</b>, 안 보일 때도
        // 자리는 그대로 잡는다. 넣었다 뺐다 하면 클릭할 때마다 줄 높이가 흔들린다
        // (같은 날: "간격이 계속 바뀌면서 레이아웃이 이동하는데 … 어지럽고 피로해").
        var remove = new TextBlock
        {
            Text = "✕",
            FontSize = 11,
            Opacity = showRemove ? 0.35 : 0,
            IsHitTestVisible = showRemove, // 안 보이면 클릭도 행 선택으로 흘려보낸다
            Foreground = Brushes.Gainsboro,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 2, 0),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        remove.PointerPressed += (_, args) =>
        {
            CommandRemoveRequested?.Invoke(command);
            args.Handled = true;
        };
        DockPanel.SetDock(remove, Dock.Right);
        layout.Children.Insert(1, remove);

        host.PointerPressed += (_, args) =>
        {
            Point position = args.GetPosition(host);

            if (position.X <= 14)
            {
                // 점 = 켜고 끄기 — 선택을 건드리지 않는다.
                CommandToggleRequested?.Invoke(command, row.IsEnabled);
                args.Handled = true;
                return;
            }

            // 좌클릭 = 선택 후보 + 드래그 시작 후보 — 놓을 때까지 판정을 미룬다.
            _pressedCommand = command;
            _pressedHost = host;
            _pressedAt = args.GetPosition(this);
            _dragging = false;
            args.Pointer.Capture(host);
            args.Handled = true;
        };
        host.PointerMoved += (_, args) => OnRowPointerMoved(args);
        host.PointerReleased += (_, args) => OnRowPointerReleased(row, command, args);

        return host;
    }

    private void OnRowPointerMoved(PointerEventArgs args)
    {
        if (_pressedCommand is null)
        {
            return;
        }

        Point position = args.GetPosition(this);

        if (!_dragging && Math.Abs(position.Y - _pressedAt.Y) < 5 && Math.Abs(position.X - _pressedAt.X) < 5)
        {
            return;
        }

        _dragging = true;

        // 드롭 후보 — 포인터에 가장 가까운 커맨드/대사 행. 위 절반 = 그 앞, 아래 절반 = 그 뒤.
        ClearDropIndicator();

        foreach ((Border rowHost, PresentationScriptRow row, _, _) in _dropRows)
        {
            Rect bounds = rowHost.Bounds;
            Point topLeft = rowHost.TranslatePoint(default, this) ?? default;
            var screenBounds = new Rect(topLeft, bounds.Size);

            if (position.Y >= screenBounds.Top && position.Y <= screenBounds.Bottom)
            {
                _dropCandidate = rowHost;
                // 대사 행은 언제나 "그 앞"(= 커맨드 목록의 끝)이다.
                _dropBefore = row.Kind == PresentationScriptRowKind.Dialogue ||
                    position.Y < screenBounds.Center.Y;
                rowHost.BorderThickness = _dropBefore ? new Thickness(0, 2, 0, 0) : new Thickness(0, 0, 0, 2);
                rowHost.BorderBrush = new SolidColorBrush(Color.FromArgb(230, 250, 204, 21));
                break;
            }
        }
    }

    private void OnRowPointerReleased(
        PresentationScriptRow row, PresentationResultCommand command, PointerReleasedEventArgs args)
    {
        args.Pointer.Capture(null);

        if (_pressedCommand is null)
        {
            return;
        }

        bool wasDragging = _dragging;
        Border? candidate = _dropCandidate;
        bool before = _dropBefore;
        _pressedCommand = null;
        _pressedHost = null;
        _dragging = false;
        ClearDropIndicator();

        if (!wasDragging)
        {
            // 좌클릭 한 번 = 선택 (소유자 다듬기 2) — 그 라인(Setup 커맨드면 Setup 구획)도
            // 함께 고르고, 아래 작업대의 표시도 같은 커맨드로 맞춘다 (2026-08-21).
            _selectedCommandId = command.CommandId;
            CommandSelected?.Invoke(command);

            if (row.LineId is { } lineId)
            {
                LineClicked?.Invoke(lineId);
            }
            else
            {
                SetupClicked?.Invoke();
            }

            RefreshCommandSelection();
            return;
        }

        if (candidate is null)
        {
            return;
        }

        (_, PresentationScriptRow targetRow, string? targetLineId, int insertIndex) =
            _dropRows.First(entry => ReferenceEquals(entry.Host, candidate));

        int finalIndex = targetRow.Kind == PresentationScriptRowKind.Dialogue || before
            ? insertIndex
            : insertIndex + 1;

        // 같은 목록 안에서 앞으로 뽑혀 나가면 목표 인덱스가 하나 줄어든다 — 호스트가
        // 소스 위치를 모르므로 여기서 보정하지 않고 편집 통로(Remove 후 Insert)가 있는
        // 그대로 소화한다. 시각 결과는 표시가 다시 그려질 때 확정된다.
        CommandMoveRequested?.Invoke(command, targetLineId, finalIndex);
    }

    private void ClearDropIndicator()
    {
        if (_dropCandidate is { } candidate)
        {
            candidate.BorderThickness = default;
            candidate.BorderBrush = null;
        }

        _dropCandidate = null;
    }

    /// <summary>선택 띠만 다시 칠한다 — 전체 재구성 없이.</summary>
    private void RefreshCommandSelection()
    {
        foreach ((Border host, PresentationScriptRow row, _, _) in _dropRows)
        {
            if (row.Kind != PresentationScriptRowKind.Command)
            {
                continue;
            }

            bool selected = row.Command is { } command &&
                string.Equals(command.CommandId, _selectedCommandId, StringComparison.Ordinal);
            host.Background = selected
                ? new SolidColorBrush(Color.FromArgb(70, 250, 204, 21))
                : Brushes.Transparent;
        }
    }

}
