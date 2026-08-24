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
    private readonly Border _frame;
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

    // ── 우클릭 메뉴 + 클립보드 (2026-08-21 소유자: "터미널에서 마우스 우클릭을 했을 때
    // 연출 추가 표시" + "복사 붙여넣기 기능도, ctrl+c/v/d 단축키도") ──────────────
    // 클립보드 자체는 호스트가 진다 — 이 패널은 그리기와 신호뿐이라는 규칙 그대로.

    /// <summary>[＋ 연출 추가] — 이 자리(lineId 또는 Setup)의 추가 UI를 작업대에 열어 달라.</summary>
    public event Action<string?, bool>? AddCommandRequested;

    /// <summary>[복사] / Ctrl+C — 이 커맨드를 클립보드에 담아 달라.</summary>
    public event Action<PresentationResultCommand>? CommandCopyRequested;

    /// <summary>[복제] / Ctrl+D — 이 커맨드를 바로 뒤에 복제해 달라.</summary>
    public event Action<PresentationResultCommand>? CommandDuplicateRequested;

    /// <summary>[붙여넣기] / Ctrl+V — 클립보드 커맨드를 이 자리(lineId 또는 Setup)에 붙여 달라.</summary>
    public event Action<string?, bool>? CommandPasteRequested;

    /// <summary>클립보드에 붙여넣을 커맨드가 있는가 — 메뉴 항목 활성의 근거. 호스트가 배선한다.</summary>
    public Func<bool>? HasClipboardCommand { get; set; }

    /// <summary>
    /// 행 우측 ★ 클릭 (2026-08-22) — 이 커맨드를 무대 조절창의 칩으로 담아 달라.
    /// 이미 원하는 값으로 맞춰 놓은 커맨드를 그대로 집는 것이 칩을 만드는 가장 짧은 길이다.
    /// </summary>
    public event Action<PresentationResultCommand>? CommandPinRequested;

    /// <summary>
    /// 담기 모드 (2026-08-22 소유자: "편집을 누르면 터미널쪽에서 무언가 추가할 수 있다는
    /// 표시가 되고, 그걸 실제로 클릭하면 자주쓰는 커맨드로 추가"). 조절창 [★ 자주 쓰는]
    /// 탭의 [편집]이 켠다.
    ///
    /// ⚠ <b>행마다 글리프를 바꾸지 않는다</b> (같은 날 2차: "X를 ★로 바꾸는 건 최악의
    /// 아이디어야 … 터미널 자체의 줄높이가 바뀌면서 꿈틀거리는게 조작감이 좋지 않아").
    /// 글자가 달라지면 글리프 높이가 달라지고 그것이 곧 줄 높이다. 대신 <b>판 전체가
    /// 활성 상태로 보이고</b>(테두리 + 바탕) <b>커맨드 행을 그냥 클릭하면 담긴다</b> —
    /// 테두리 두께는 평소에도 자리를 잡고 있으므로 켜고 꺼도 아무것도 안 움직인다.
    /// </summary>
    public bool PinMode { get; set; }

    /// <summary>
    /// 담기 모드에서 행 툴팁이 말할 <b>목적지</b> (2026-08-24 — 묶음 칩). 담긴 곳이
    /// 새 칩이냐 펼쳐 둔 칩이냐로 갈리므로, 손이 행 위에 있는 그 순간 그것을 말한다.
    /// 조절창이 정본이고(<c>StageSceneView.QuickPinTarget</c>) 여기는 그 문장을 받는다.
    /// </summary>
    public string? PinHint { get; set; }

    /// <summary>지금 노란 띠가 선 커맨드 — 작업대 표시 동기화용. 없으면 null.</summary>
    internal string? SelectedCommandId => _selectedCommandId;

    // 단축키·우클릭 판정에 쓰는 마지막 Show의 문맥.
    private string? _shownSelectedLineId;
    private bool _shownSetupSelected;
    private bool _shownEditable;

    public PresentationScriptPanel()
    {
        // 단축키(Ctrl+C/V/D)는 포커스가 이 패널 안에 있을 때만 듣는다 — 다른 곳의
        // 텍스트 편집을 가로채지 않는다. 행 클릭이 포커스를 이리로 가져온다.
        Focusable = true;

        // 스크롤바가 행 위에 겹쳐 뜨므로 오른쪽·아래를 비워 둔다 (2026-08-21 소유자:
        // "두꺼운 바가 삭제 버튼을 가려버려서") — X가 바 밑에 깔리지 않는 자리다.
        _rows.Margin = new Thickness(0, 0, 16, 8);

        _scroll = new ScrollViewer
        {
            Content = _rows,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

        // 테두리는 <b>늘</b> 2px 자리를 잡는다 (2026-08-22) — 담기 모드가 색만 칠하므로
        // 켜고 꺼도 안쪽 내용이 한 픽셀도 안 움직인다. 안쪽 여백은 그만큼 줄여 총합을 지킨다.
        _frame = new Border
        {
            Background = TerminalBackground, // 터미널 감각의 어두운 판
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent,
            Padding = new Thickness(10, 8), // 빽빽함 완화 (2026-08-21 소유자: "여백도 너무 빽빽")
            Child = _scroll
        };

        Content = _frame;
    }

    private static readonly SolidColorBrush TerminalBackground = new(Color.FromRgb(17, 19, 24));

    /// <summary>담기 모드의 바탕 — 같은 어둠에 노랑을 아주 조금 섞어 "켜져 있다"만 말한다.</summary>
    private static readonly SolidColorBrush TerminalArmedBackground = new(Color.FromRgb(26, 24, 17));

    private static readonly SolidColorBrush TerminalArmedBorder = new(Color.FromArgb(190, 250, 204, 21));

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
        _shownSelectedLineId = selectedLineId;
        _shownSetupSelected = setupSelected;
        _shownEditable = editable;

        // 판 전체가 "켜져 있다"를 말한다 — 테두리 색과 바탕만 바뀌고 자리는 그대로다.
        _frame.BorderBrush = PinMode ? TerminalArmedBorder : Brushes.Transparent;
        _frame.Background = PinMode ? TerminalArmedBackground : TerminalBackground;

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
                    if (TryShowContextMenu(container, args, null, setup: true, command: null))
                    {
                        return;
                    }

                    _selectedCommandId = null;
                    SetupClicked?.Invoke();
                    args.Handled = true;
                };
            }
            else if (!isSetup && editable && lineId is { } groupLineId)
            {
                // 라인 박스의 빈 자리 우클릭도 그 라인의 메뉴다 — 행 사이 틈에서도 열린다.
                container.PointerPressed += (_, args) =>
                    TryShowContextMenu(container, args, groupLineId, setup: false, command: null);
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
            if (TryShowContextMenu(header, args, lineId, setup: false, command: null))
            {
                return;
            }

            Focus();
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
            if (row.LineId is not { } lineId)
            {
                return;
            }

            if (TryShowContextMenu(host, args, lineId, setup: false, command: null))
            {
                return;
            }

            // 라인 선택 = 커맨드 선택 해제 — 작업대가 그 라인의 추가 입구로 돌아간다.
            Focus();
            _selectedCommandId = null;
            LineClicked?.Invoke(lineId);
            args.Handled = true;
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
        //
        // ⚠ 담기 모드에서도 이 글리프는 안 바뀐다 (2026-08-22 소유자) — ✕를 ★로 갈면
        // 글리프 높이가 달라져 줄이 꿈틀거린다. 담는 동안에는 숨을 뿐이다.
        var remove = new TextBlock
        {
            Text = "✕",
            FontSize = 11,
            Opacity = !PinMode && showRemove ? 0.35 : 0,
            IsHitTestVisible = !PinMode && showRemove, // 안 보이면 클릭도 행 선택으로 흘려보낸다
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

        if (PinMode)
        {
            // 담기 모드 = 행 자체가 단추다 (2026-08-22 소유자: "그냥 터미널 자체가 무언가
            // 활성화 된다는 느낌과 같이 특정 커맨드를 클릭했을 때 가져와지도록").
            // 바탕과 커서만 바뀐다 — 자리를 차지하는 것은 아무것도 안 늘어난다.
            host.Cursor = new Cursor(StandardCursorType.Hand);
            host.Background = new SolidColorBrush(Color.FromArgb(28, 250, 204, 21));
            ToolTip.SetTip(host, PinHint ?? "클릭하면 [★ 자주 쓰는]에 담습니다 (슬롯·시간 그대로).");

            host.PointerPressed += (_, args) =>
            {
                args.Handled = true;

                if (args.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
                {
                    CommandPinRequested?.Invoke(command);
                }
            };

            return host;
        }

        host.PointerPressed += (_, args) =>
        {
            // 우클릭 = 메뉴 — 그 커맨드를 먼저 선택해, 메뉴·단축키가 보이는 것과 같은
            // 대상을 잡게 한다(선택 없이 뜨면 [복사]가 무엇을 복사하는지 화면이 침묵한다).
            if (args.GetCurrentPoint(host).Properties.IsRightButtonPressed)
            {
                _selectedCommandId = command.CommandId;
                CommandSelected?.Invoke(command);
                RefreshCommandSelection();
                TryShowContextMenu(host, args, row.LineId, setup: row.LineId is null, command);
                return;
            }

            Point position = args.GetPosition(host);

            if (position.X <= 14)
            {
                // 점 = 켜고 끄기 — 선택을 건드리지 않는다.
                CommandToggleRequested?.Invoke(command, row.IsEnabled);
                args.Handled = true;
                return;
            }

            // 좌클릭 = 선택 후보 + 드래그 시작 후보 — 놓을 때까지 판정을 미룬다.
            Focus();
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
        if (PinMode)
        {
            return; // 담기 모드의 바탕은 "누를 수 있다"는 표시다 — 선택 띠가 덮으면 안 된다
        }

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

    // ── 우클릭 메뉴 + 단축키 (2026-08-21 소유자 지시) ────────────────────────

    /// <summary>
    /// 우클릭이면 그 자리의 메뉴를 열고 true. 좌클릭이면 아무것도 안 하고 false —
    /// 각 행 핸들러가 제 좌클릭 처리로 이어 간다. 편집 잠금 중에는 메뉴가 없다.
    /// </summary>
    private bool TryShowContextMenu(
        Control anchor,
        PointerPressedEventArgs args,
        string? lineId,
        bool setup,
        PresentationResultCommand? command)
    {
        if (!args.GetCurrentPoint(anchor).Properties.IsRightButtonPressed)
        {
            return false;
        }

        args.Handled = true;

        if (!_shownEditable)
        {
            return true; // 잠금 화면 — 우클릭을 소비만 한다(행 선택으로 흘리지 않는다)
        }

        Focus();

        var menu = new MenuFlyout();

        var add = new MenuItem { Header = "＋ 연출 추가" };
        add.Click += (_, _) => AddCommandRequested?.Invoke(lineId, setup);
        menu.Items.Add(add);

        if (command is not null)
        {
            var copy = new MenuItem
            {
                Header = "복사",
                InputGesture = new KeyGesture(Key.C, KeyModifiers.Control)
            };
            copy.Click += (_, _) => CommandCopyRequested?.Invoke(command);
            menu.Items.Add(copy);

            var duplicate = new MenuItem
            {
                Header = "복제",
                InputGesture = new KeyGesture(Key.D, KeyModifiers.Control)
            };
            duplicate.Click += (_, _) => CommandDuplicateRequested?.Invoke(command);
            menu.Items.Add(duplicate);
        }
        // [★ 자주 쓰는 데 담기]는 여기 없다 (2026-08-22) — 우클릭에서 담으면 조절창이
        // 닫힌 채라 담긴 결과가 화면 어디에도 안 보였다. 담기는 조절창 [편집]이 켜는
        // 행 우측 ★ 하나다: 판을 띄워 놓고 담고, 담기는 즉시 그 판에 선다.

        var paste = new MenuItem
        {
            Header = "붙여넣기",
            InputGesture = new KeyGesture(Key.V, KeyModifiers.Control),
            IsEnabled = HasClipboardCommand?.Invoke() == true
        };
        paste.Click += (_, _) => CommandPasteRequested?.Invoke(lineId, setup);
        menu.Items.Add(paste);

        menu.ShowAt(anchor, showAtPointer: true);
        return true;
    }

    /// <summary>지금 노란 띠가 선 커맨드의 결과 레코드 — 화면에 있는 행에서 찾는다.</summary>
    private PresentationResultCommand? SelectedCommand =>
        _selectedCommandId is null
            ? null
            : _dropRows.FirstOrDefault(entry =>
                entry.Row.Kind == PresentationScriptRowKind.Command &&
                string.Equals(entry.Row.Command?.CommandId, _selectedCommandId, StringComparison.Ordinal))
                .Row?.Command;

    /// <summary>선택 커맨드가 속한 자리, 없으면 지금 보고 있는 구획 — 붙여넣기의 목적지다.</summary>
    private (string? LineId, bool Setup) PasteTarget()
    {
        if (SelectedCommand is { } selected)
        {
            string? lineId = _dropRows
                .First(entry => ReferenceEquals(entry.Row.Command, selected))
                .TargetLineId;
            return (lineId, lineId is null);
        }

        return _shownSetupSelected ? (null, true) : (_shownSelectedLineId, false);
    }

    protected override void OnKeyDown(KeyEventArgs args)
    {
        base.OnKeyDown(args);

        if (args.Handled || !_shownEditable || !args.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        switch (args.Key)
        {
            case Key.C when SelectedCommand is { } copySource:
                CommandCopyRequested?.Invoke(copySource);
                args.Handled = true;
                break;

            case Key.D when SelectedCommand is { } duplicateSource:
                CommandDuplicateRequested?.Invoke(duplicateSource);
                args.Handled = true;
                break;

            case Key.V when HasClipboardCommand?.Invoke() == true:
                (string? lineId, bool setup) = PasteTarget();

                if (setup || lineId is not null)
                {
                    CommandPasteRequested?.Invoke(lineId, setup);
                    args.Handled = true;
                }

                break;
        }
    }
}
