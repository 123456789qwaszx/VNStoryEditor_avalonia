using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Vn.App.Services;
using Vn.Authoring.Chapters;
using Vn.Authoring.Graph;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.App.Views;

/// <summary>
/// GraphProjection을 그리는 저작 화면.
///
/// StoryProject를 직접 순회하지 않는다. 공식 원본과 workspace의 파일 펼침 상태를
/// <see cref="GraphProjectionBuilder"/>에 전달하고, 펼쳐진 노드는 NodeCard로, 접힌 파일은
/// FileProxy로 받은 결과만 그린다. 파일을 접어도 실제 연결 target은 NodeId로 유지되고
/// endpoint만 프록시의 해당 노드 행으로 바뀐다.
/// </summary>
public partial class GraphEditorView : UserControl
{
    private const double CardWidth = 210;
    private const double HeaderHeight = 46;
    private const double PortRowHeight = 24;
    private const double CardPadding = 8;
    private const double PortRadius = 6;

    private const double ProxyWidth = 250;
    private const double ProxyHeaderHeight = 50;
    private const double ProxyRowHeight = 28;
    private const double ProxyPortRadius = 5;

    private readonly List<NodeCard> _cards = new();
    private readonly List<FileProxyVisual> _proxies = new();
    private readonly List<EdgeVisual> _edges = new();

    /// <summary>챕터(판) 박스 — 펼친 판의 노드들을 감싸는 배경 프레임 + 이름표 (2단계 무대 3번).</summary>
    private readonly List<Control> _frames = new();

    /// <summary>챕터 간선의 철도 배선 (T1) — 미러 선·분기점·선택지 칩.</summary>
    private readonly List<Control> _railVisuals = new();

    /// <summary>
    /// 챕터 그래프 뷰가 읽은 챕터 목록 (T1). 이 뷰는 챕터 워크북을 직접 읽지 않는다 —
    /// 챕터 모델의 원천은 하나여야 감시·재시도 규칙이 두 벌이 되지 않는다.
    /// </summary>
    private IReadOnlyList<ChapterEntry> _chapters = Array.Empty<ChapterEntry>();

    internal void SupplyChapters(IReadOnlyList<ChapterEntry> entries)
    {
        _chapters = entries;

        if (_projection is not null)
        {
            DrawChapterRails();
        }
    }

    private AuthoringSession? _session;
    private GraphProjection? _projection;

    private NodeCard? _draggingCard;
    private Point _dragOffset;

    private GraphOutputPortProjection? _connectingFrom;
    private Line? _connectingLine;

    private EdgeVisual? _selectedEdge;
    private bool _updatingFilter;

    // ── 그래프 내비게이션 (W40) ────────────────────────────────────────────
    private const double MinZoom = 0.1;  // 큰 판(W41)에서도 전체 조망이 가능하게
    private const double MaxZoom = 2.0;

    // 판 크기 (W41) — 캔버스 크기의 유일한 정의. 미니맵과 같은 3:2 비율을 유지해야
    // 미니맵이 왜곡 없이 축소된다. Canvas는 좌표 공간일 뿐이라 크기 자체는 비용이
    // 없다 — 비용은 노드 수에서 나온다(가상화는 파일 판 백로그).
    private const double CanvasWidth = 12000;
    private const double CanvasHeight = 8000;
    private const double MinimapWidth = 180;   // 캔버스와 같은 3:2 비율
    private const double MinimapHeight = 120;

    private double _zoom = 1;
    private bool _panning;
    private Point _panStart;
    private Vector _panStartOffset;
    private bool _minimapDragging;
    private Rectangle? _minimapViewport;
    private string? _followedNodeId; // 선택이 바뀐 순간에만 화면이 따라간다 (GB-4)

    // 판별 뷰 상태 (GB-1) — 판(활성 파일)마다 보던 자리·배율을 기억한다.
    // 뷰 상태라 저장하지 않는다(원칙 E) — 세션 안에서만 산다.
    private readonly Dictionary<string, (Vector Offset, double Zoom)> _boardViews = new(StringComparer.Ordinal);
    private string? _viewedFileId;

    // 접힌 파일 드래그 (W52) — 프록시 위치는 안 노드들의 평균이므로,
    // 끌기는 파일 안 노드 전부를 같은 delta로 옮기는 것이다(펼치면 그 자리에 있다).
    private string? _draggingProxyFileId;
    private Point _proxyDragStart;
    private readonly Dictionary<string, Point> _proxyStartPositions = new(StringComparer.Ordinal);

    // 범위 선택 (W40) — 좌클릭 드래그로 잡은 노드 무리는 한 번에 움직인다.
    private Rectangle? _rubberBand;
    private Point _rubberStart;
    private readonly HashSet<string> _multiSelected = new(StringComparer.Ordinal);
    private bool _draggingGroup;
    private Point _groupDragStart;
    private readonly Dictionary<string, Point> _groupStartPositions = new(StringComparer.Ordinal);

    /// <summary>토글 상태로 만든 필터. 거르는 것은 화면이 아니라 투영이다.</summary>
    private GraphFilter CurrentFilter => new(
        ShowDialogue: FilterDialogueCheck.IsChecked == true,
        ShowSet: FilterSetCheck.IsChecked == true,
        ShowPresentation: FilterPresentationCheck.IsChecked == true,
        ShowCommandSupply: FilterSupplyCheck.IsChecked == true,
        ShowResultConnections: FilterResultCheck.IsChecked == true);

    /// <summary>DialogueNode만 남기는 흐름 보기.</summary>
    private void ApplyFlowOnlyFilter()
    {
        _updatingFilter = true;

        try
        {
            FilterDialogueCheck.IsChecked = true;
            FilterSetCheck.IsChecked = false;
            FilterPresentationCheck.IsChecked = false;
            FilterSupplyCheck.IsChecked = false;
            FilterResultCheck.IsChecked = false;
        }
        finally
        {
            _updatingFilter = false;
        }

        Rebuild();
    }

    public GraphEditorView()
    {
        InitializeComponent();

        // 판 크기는 상수 한 곳이 정한다 (W41) — 미니맵 배율과 어긋날 길을 없앤다.
        GraphCanvas.Width = CanvasWidth;
        GraphCanvas.Height = CanvasHeight;

        AddDialogueButton.Click += (_, _) => AddNode(GraphNodeKind.Dialogue);
        AddSetButton.Click += (_, _) => AddNode(GraphNodeKind.Set);
        AddPresentationButton.Click += (_, _) => AddNode(GraphNodeKind.Presentation);
        AddSupplyButton.Click += (_, _) => AddNode(GraphNodeKind.CommandSupply);
        DeleteNodeButton.Click += (_, _) => DeleteSelectedNode();

        foreach (CheckBox check in new[]
                 {
                     FilterDialogueCheck, FilterSetCheck, FilterPresentationCheck,
                     FilterSupplyCheck, FilterResultCheck
                 })
        {
            check.IsCheckedChanged += (_, _) =>
            {
                if (!_updatingFilter)
                {
                    Rebuild();
                }
            };
        }

        FlowOnlyButton.Click += (_, _) => ApplyFlowOnlyFilter();

        GraphCanvas.PointerMoved += OnCanvasPointerMoved;
        GraphCanvas.PointerReleased += OnCanvasPointerReleased;
        GraphCanvas.PointerPressed += OnCanvasPointerPressed;

        // 그래프 내비게이션 (W40) — Ctrl+휠 줌·중간 버튼 팬은 스크롤보다 먼저 가로챈다.
        GraphScroll.AddHandler(PointerWheelChangedEvent, OnGraphWheel, RoutingStrategies.Tunnel);
        GraphScroll.AddHandler(PointerPressedEvent, OnGraphPanPressed, RoutingStrategies.Tunnel);
        GraphScroll.AddHandler(PointerMovedEvent, OnGraphPanMoved, RoutingStrategies.Tunnel);
        GraphScroll.AddHandler(PointerReleasedEvent, OnGraphPanReleased, RoutingStrategies.Tunnel);
        GraphScroll.ScrollChanged += (_, _) => RefreshMinimapViewport();
        MinimapCanvas.PointerPressed += OnMinimapPressed;
        MinimapCanvas.PointerMoved += OnMinimapMoved;
        MinimapCanvas.PointerReleased += (_, _) => _minimapDragging = false;

        // 전체 보기·배율 프리셋 (GB-4). 프리셋은 화면 중앙 기준으로 배율만 바꾼다.
        FitAllButton.Click += (_, _) => FitAll();
        Zoom50Button.Click += (_, _) => ApplyZoom(0.5, null);
        Zoom100Button.Click += (_, _) => ApplyZoom(1, null);
        Zoom150Button.Click += (_, _) => ApplyZoom(1.5, null);
    }

    internal void Attach(AuthoringSession session)
    {
        _session = session;
    }

    // ── 그리기 ──────────────────────────────────────────────────────────────

    internal void Rebuild()
    {
        if (_session is null)
        {
            return;
        }

        _projection = GraphProjectionBuilder.Build(
            _session.Project,
            _session.ExpandedFileIds,
            _session.Definition,
            CurrentFilter);

        GraphCanvas.Children.Clear();
        _cards.Clear();
        _proxies.Clear();
        _edges.Clear();
        _frames.Clear();
        _selectedEdge = null;

        foreach (GraphItemProjection item in _projection.Items)
        {
            switch (item)
            {
                case ExpandedNodeProjection node:
                    _cards.Add(BuildCard(node));
                    break;

                case CollapsedFileProjection file:
                    _proxies.Add(BuildFileProxy(file));
                    break;
            }
        }

        DrawChapterFrames();

        AddDialogueButton.IsEnabled = _session.ActiveFileId is not null;
        AddSetButton.IsEnabled = _session.ActiveFileId is not null;
        AddPresentationButton.IsEnabled = _session.ActiveFileId is not null;

        DrawEdges();
        HighlightSelection();
        RefreshMinimap();
        HandleBoardSwitch();
    }

    /// <summary>
    /// 챕터(판) 박스 (2단계 무대 3번, PDF 3·9장) — 펼친 판마다 노드들을 감싸는 프레임과
    /// 이름표를 카드 <b>뒤에</b> 깐다. 작가가 "이 노드가 어느 챕터 소속인지"를 보고 붙일
    /// 곳을 정하는 무대다. 히트 대상이 아니라서 클릭·드래그·포트에 아무 영향이 없다.
    /// </summary>
    private void DrawChapterFrames()
    {
        foreach (Control frame in _frames)
        {
            GraphCanvas.Children.Remove(frame);
        }

        _frames.Clear();

        if (_session is null || _projection is null)
        {
            return;
        }

        foreach (IGrouping<string, ExpandedNodeProjection> group in _projection.Items
                     .OfType<ExpandedNodeProjection>()
                     .GroupBy(item => item.FileId, StringComparer.Ordinal))
        {
            Rect? bounds = null;

            foreach (ExpandedNodeProjection node in group)
            {
                if (FindCard(node.NodeId) is not { } card)
                {
                    continue;
                }

                var rect = new Rect(node.Position.X, node.Position.Y, CardWidth, CardHeightOf(card));
                bounds = bounds is { } current ? current.Union(rect) : rect;
            }

            if (bounds is not { } area)
            {
                continue;
            }

            // 위쪽은 이름표 자리까지 여유를 둔다.
            area = area.Inflate(new Thickness(24, 46, 24, 24));

            bool isActive = string.Equals(group.Key, _session.ActiveFileId, StringComparison.Ordinal);
            string chapterName = _session.Project.Files
                .FirstOrDefault(file => string.Equals(file.Id, group.Key, StringComparison.Ordinal))
                ?.Name ?? group.Key;

            var frame = new Border
            {
                Width = area.Width,
                Height = area.Height,
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(isActive ? 1.6 : 1),
                BorderBrush = new SolidColorBrush(
                    isActive ? Color.FromArgb(150, 61, 123, 217) : Color.FromArgb(70, 128, 128, 128)),
                Background = new SolidColorBrush(
                    isActive ? Color.FromArgb(12, 61, 123, 217) : Color.FromArgb(8, 128, 128, 128)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(frame, area.X);
            Canvas.SetTop(frame, area.Y);

            var label = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3),
                Background = new SolidColorBrush(
                    isActive ? Color.FromArgb(220, 61, 123, 217) : Color.FromArgb(160, 107, 114, 128)),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock
                {
                    Text = $"챕터 {chapterName} ⌄",
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.White
                }
            };
            ToolTip.SetTip(label, "클릭하면 이 챕터를 표 형태로 접습니다. 접힌 표를 더블클릭하면 다시 펼칩니다.");

            // 이름표만 히트 대상이다(프레임은 아님) — 클릭 = 접기 (PDF 9장 "체크해제하면 표형태").
            string frameFileId = group.Key;
            label.PointerPressed += (_, args) =>
            {
                args.Handled = true;
                _session?.SetFileExpanded(frameFileId, expanded: false);
            };

            Canvas.SetLeft(label, area.X + 12);
            Canvas.SetTop(label, area.Y + 10);

            _frames.Add(frame);
            _frames.Add(label);
        }

        // 카드·간선보다 뒤에 깔리도록 맨 앞 인덱스에 순서대로 끼운다.
        for (int index = 0; index < _frames.Count; index++)
        {
            GraphCanvas.Children.Insert(index, _frames[index]);
        }

        DrawChapterRails();
    }

    // ── 챕터 간선 철도 배선 (T1) ────────────────────────────────────────────

    private static readonly IBrush RailBrush = new SolidColorBrush(Color.FromArgb(170, 120, 120, 120));
    private static readonly IBrush RailChipBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x6A, 0x14));
    private const double RailTrunkInset = 30;   // 출발 카드 왼쪽에서 줄기까지
    private const double RailBranchGap = 44;    // 가지 사이 세로 간격
    private const double RailFirstDrop = 36;    // 카드 아래 첫 가지까지

    /// <summary>
    /// 챕터 간선을 철도 노선처럼 (T1, 소유자 그림) — 출발 카드에서 수직 줄기가 내려오고,
    /// 단일 분기점에서 가지들이 갈라져 도착 카드로 들어간다. 선택은 항상 에피소드 끝이므로
    /// (CHOICE는 파일 맨 끝 0~1개) 분기점은 하나다 — Line 비율 같은 것은 존재하지 않는다.
    /// 기본 칩은 문구만(T-D4 — 수치는 챕터 그래프의 책임), 클릭하면 읽기 전용 상세.
    /// </summary>
    private void DrawChapterRails()
    {
        foreach (Control visual in _railVisuals)
        {
            GraphCanvas.Children.Remove(visual);
        }

        _railVisuals.Clear();

        if (_session is null || _projection is null || _chapters.Count == 0)
        {
            return;
        }

        foreach (IGrouping<string, ExpandedNodeProjection> group in _projection.Items
                     .OfType<ExpandedNodeProjection>()
                     .GroupBy(item => item.FileId, StringComparer.Ordinal))
        {
            string boardName = _session.Project.Files
                .FirstOrDefault(file => string.Equals(file.Id, group.Key, StringComparison.Ordinal))
                ?.Name ?? string.Empty;

            ChapterGraphModel? chapter = _chapters
                .FirstOrDefault(entry => string.Equals(entry.ChapterId, boardName, StringComparison.Ordinal))
                ?.Model;

            if (chapter is null)
            {
                continue; // 챕터 판이 아니다 — 일반 프로젝트는 아무 변화 없다.
            }

            // 에피소드 Id → 카드 자리. 아직 동기화 전인 에피소드는 없는 것으로 둔다 —
            // 없는 노드로 선을 그으면 거짓말이다.
            var spots = new Dictionary<string, Rect>(StringComparer.Ordinal);

            foreach (ExpandedNodeProjection node in group)
            {
                if (_session.Project.FindNode(node.NodeId) is DialogueNode { ExcelEpisodeId: { } episodeId } &&
                    FindCard(node.NodeId) is { } card)
                {
                    spots[episodeId] = new Rect(
                        node.Position.X, node.Position.Y, CardWidth, CardHeightOf(card));
                }
            }

            foreach (IGrouping<string, ChapterEdge> fromGroup in chapter.Edges
                         .Where(edge => spots.ContainsKey(edge.FromEpisodeId) &&
                                        spots.ContainsKey(edge.ToEpisodeId))
                         .GroupBy(edge => edge.FromEpisodeId, StringComparer.Ordinal))
            {
                DrawRailsFrom(spots[fromGroup.Key], fromGroup.ToList(), spots);
            }
        }

        // 프레임 뒤·카드 앞 — 프레임들 바로 다음 인덱스에 끼운다.
        for (int index = 0; index < _railVisuals.Count; index++)
        {
            GraphCanvas.Children.Insert(_frames.Count + index, _railVisuals[index]);
        }
    }

    /// <summary>한 출발 카드의 줄기와 가지들. 시트 순서 = 가지 순서(위→아래).</summary>
    private void DrawRailsFrom(Rect source, IReadOnlyList<ChapterEdge> edges, IReadOnlyDictionary<string, Rect> spots)
    {
        double trunkX = source.X + RailTrunkInset;
        double branchY = source.Bottom + RailFirstDrop;

        foreach (ChapterEdge edge in edges)
        {
            DrawRailBranch(edge, trunkX, branchY, spots[edge.ToEpisodeId]);
            branchY += RailBranchGap;
        }

        // 수직 줄기 — 카드 바닥에서 마지막 가지까지.
        _railVisuals.Add(RailLine(trunkX, source.Bottom, trunkX, branchY - RailBranchGap));
    }

    private void DrawRailBranch(ChapterEdge edge, double trunkX, double y, Rect target)
    {
        double cursorX = trunkX;

        if (!edge.IsPlainAdvance)
        {
            // ● 문구 칩 — 기본 표시는 문구뿐이다(T-D4). 클릭하면 읽기 전용 상세.
            var chip = new TextBlock
            {
                Text = $"● {edge.OptionLabel}",
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = RailChipBrush,
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            ToolTip.SetTip(chip, "선택지 — 누르면 조건·스탯변화를 보여 줍니다(읽기 전용, 편집은 챕터 그래프).");

            chip.Measure(Size.Infinity);
            double chipWidth = chip.DesiredSize.Width;

            _railVisuals.Add(RailLine(trunkX, y, trunkX + 10, y));
            Canvas.SetLeft(chip, trunkX + 14);
            Canvas.SetTop(chip, y - chip.DesiredSize.Height / 2);

            ChapterEdge captured = edge;
            chip.PointerPressed += (_, args) =>
            {
                args.Handled = true;
                ShowRailDetail(chip, captured);
            };

            _railVisuals.Add(chip);
            cursorX = trunkX + 14 + chipWidth + 6;
        }

        // 도착 카드로 — 같은 높이면 왼쪽으로 진입(▶), 위·아래면 카드 가운데로 꺾어 진입(▲▼).
        if (y >= target.Y && y <= target.Bottom)
        {
            _railVisuals.Add(RailLine(cursorX, y, target.X - 10, y));
            _railVisuals.Add(RailArrow(target.X - 9, y, pointRight: true));
        }
        else
        {
            double midX = target.X + CardWidth / 2;
            _railVisuals.Add(RailLine(cursorX, y, midX, y));

            bool targetAbove = target.Bottom < y;
            double endY = targetAbove ? target.Bottom + 10 : target.Y - 10;
            _railVisuals.Add(RailLine(midX, y, midX, endY));
            _railVisuals.Add(RailArrow(midX, targetAbove ? endY - 1 : endY + 1, pointRight: false, pointUp: targetAbove));
        }
    }

    /// <summary>읽기 전용 상세 — 배선·편집은 T2의 몫이고, 값 편집은 언제나 챕터 그래프/엑셀이다.</summary>
    private void ShowRailDetail(Control anchor, ChapterEdge edge)
    {
        var panel = new StackPanel { Spacing = 3, MinWidth = 200 };

        void Line(string text, bool bold = false, double opacity = 1) => panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            Opacity = opacity,
            TextWrapping = TextWrapping.Wrap
        });

        Line(edge.OptionLabel ?? "(진행)", bold: true);
        Line($"{edge.FromEpisodeId} → {edge.ToEpisodeId}", opacity: 0.7);

        if (!string.IsNullOrWhiteSpace(edge.ConditionLabel))
        {
            Line($"조건: {edge.ConditionLabel}", opacity: 0.85);
        }

        if (edge.StatChanges.Count > 0)
        {
            Line("스탯변화: " + string.Join("; ", edge.StatChanges
                .Select(delta => $"{delta.Key} {(delta.Amount >= 0 ? "+" : "")}{delta.Amount}")), opacity: 0.85);
        }

        if (edge.HideWhenLocked)
        {
            Line("잠기면 숨김", opacity: 0.7);
        }

        if (!string.IsNullOrWhiteSpace(edge.LockedMessage))
        {
            Line($"잠금 안내: {edge.LockedMessage}", opacity: 0.7);
        }

        Line("편집은 챕터 그래프에서 · 씬 배선은 T2에서 열립니다.", opacity: 0.5);

        new Flyout { Content = panel }.ShowAt(anchor);
    }

    private static Line RailLine(double x1, double y1, double x2, double y2) => new()
    {
        StartPoint = new Point(x1, y1),
        EndPoint = new Point(x2, y2),
        Stroke = RailBrush,
        StrokeThickness = 1.4,
        IsHitTestVisible = false
    };

    private static Polygon RailArrow(double x, double y, bool pointRight, bool pointUp = false)
    {
        var arrow = new Polygon
        {
            Fill = RailBrush,
            IsHitTestVisible = false,
            Points = pointRight
                ? [new Point(0, -4), new Point(7, 0), new Point(0, 4)]
                : pointUp
                    ? [new Point(-4, 0), new Point(0, -7), new Point(4, 0)]
                    : [new Point(-4, 0), new Point(0, 7), new Point(4, 0)]
        };

        Canvas.SetLeft(arrow, x);
        Canvas.SetTop(arrow, y);
        return arrow;
    }

    /// <summary>
    /// 판 전환 (GB-1) — 활성 파일이 바뀌었으면 떠나는 판의 자리·배율을 기억하고,
    /// 새 판은 기억해 둔 자리로(처음이면 전체 보기로) 돌아간다.
    /// </summary>
    private void HandleBoardSwitch()
    {
        string? active = _session?.ActiveFileId;

        if (string.Equals(active, _viewedFileId, StringComparison.Ordinal))
        {
            return;
        }

        if (_viewedFileId is { } previous)
        {
            _boardViews[previous] = (GraphScroll.Offset, _zoom);
        }

        _viewedFileId = active;

        void Restore()
        {
            if (active is not null &&
                _boardViews.TryGetValue(active, out (Vector Offset, double Zoom) view))
            {
                SetZoom(view.Zoom);
                GraphScroll.Offset = view.Offset;
                RefreshMinimapViewport();
            }
            else if (active is not null)
            {
                FitFile(active); // 처음 여는 챕터는 그 챕터 박스가 들어오게 (PDF 9장 "그 챕터로 이동")
            }
            else
            {
                FitAll();
            }
        }

        if (GraphScroll.Viewport.Width > 0)
        {
            Restore();
        }
        else
        {
            // 앱 시작 직후 첫 레이아웃 전 — 뷰포트 크기가 잡힌 다음 턴에 자리를 잡는다.
            Avalonia.Threading.Dispatcher.UIThread.Post(Restore);
        }
    }

    /// <summary>좌표만 바뀌었을 때. projection을 다시 계산하되 컨트롤은 유지한다.</summary>
    internal void RefreshPositions()
    {
        if (_session is null)
        {
            return;
        }

        _projection = GraphProjectionBuilder.Build(
            _session.Project,
            _session.ExpandedFileIds,
            _session.Definition,
            CurrentFilter);

        foreach (ExpandedNodeProjection node in _projection.Items.OfType<ExpandedNodeProjection>())
        {
            NodeCard? card = FindCard(node.NodeId);

            if (card is not null)
            {
                Canvas.SetLeft(card.Visual, node.Position.X);
                Canvas.SetTop(card.Visual, node.Position.Y);
            }
        }

        foreach (CollapsedFileProjection file in _projection.Items.OfType<CollapsedFileProjection>())
        {
            FileProxyVisual? proxy = FindProxy(file.FileId);

            if (proxy is not null)
            {
                Canvas.SetLeft(proxy.Visual, file.Position.X);
                Canvas.SetTop(proxy.Visual, file.Position.Y);
            }
        }

        foreach (EdgeVisual edge in _edges)
        {
            GraphConnectionProjection? current = _projection.Connections.FirstOrDefault(
                item => string.Equals(item.Key, edge.Connection.Key, StringComparison.Ordinal));

            if (current is not null)
            {
                edge.Connection = current;
                PositionEdge(edge);
            }
        }

        // 노드가 움직이면 그 노드를 감싸는 챕터 박스도 따라 늘고 준다.
        DrawChapterFrames();

        RefreshMinimap();
    }

    internal void HighlightSelection()
    {
        foreach (NodeCard card in _cards)
        {
            bool selected = string.Equals(card.NodeId, _session?.SelectedNodeId, StringComparison.Ordinal);
            bool grouped = _multiSelected.Contains(card.NodeId); // 범위 선택 무리 (W40)

            card.Visual.BorderBrush = selected || grouped
                ? new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB))
                : new SolidColorBrush(Color.FromArgb(90, 128, 128, 128));

            card.Visual.BorderThickness = new Thickness(selected || grouped ? 2 : 1);
        }

        foreach (FileProxyVisual proxy in _proxies)
        {
            foreach (ProxyNodeRow row in proxy.Rows)
            {
                bool selected = string.Equals(row.NodeId, _session?.SelectedNodeId, StringComparison.Ordinal);
                row.Visual.Background = selected
                    ? new SolidColorBrush(Color.FromArgb(42, 37, 99, 235))
                    : Brushes.Transparent;
            }
        }

        // 선택이 실제로 바뀐 순간에만 따라간다 (GB-4) — 같은 선택의 재하이라이트
        // (편집·재빌드)에 화면이 끼어들면 스크롤해 둔 자리를 빼앗는다.
        if (!string.Equals(_session?.SelectedNodeId, _followedNodeId, StringComparison.Ordinal))
        {
            // 노드 삭제의 대체 선택(사라진 노드 → 첫 노드)은 사용자가 고른 게 아니다 —
            // 화면을 옮기지 않는다 (소유자 지시 2026-08-06).
            bool deletionFallback = _followedNodeId is not null &&
                _session?.Project.FindNode(_followedNodeId) is null;

            _followedNodeId = _session?.SelectedNodeId;

            if (!deletionFallback)
            {
                ScrollToSelected();
            }
        }
    }

    private NodeCard BuildCard(ExpandedNodeProjection node)
    {
        var body = new StackPanel { Spacing = 0 };

        body.Children.Add(new TextBlock
        {
            Text = node.NodeName,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        body.Children.Add(new TextBlock
        {
            Text = node.Badge is null
                ? NodeKindLabel(node.NodeKind)
                : $"{NodeKindLabel(node.NodeKind)} · {node.Badge}",
            FontSize = 10,
            Opacity = 0.6,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 1, 0, 6)
        });

        var card = new Border
        {
            Width = CardWidth,
            Padding = new Thickness(CardPadding),
            CornerRadius = new CornerRadius(8),
            Background = node.NodeKind switch
            {
                GraphNodeKind.Set => new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF)),
                GraphNodeKind.Presentation => new SolidColorBrush(Color.FromRgb(0xFA, 0xF5, 0xFF)),
                GraphNodeKind.CommandSupply => new SolidColorBrush(Color.FromRgb(0xF0, 0xFD, 0xF4)),
                _ => new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))
            },
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 128, 128, 128)),
            Child = body,
            Tag = node.NodeId
        };

        var visual = new NodeCard(node.NodeId, node.NodeKind, card, node.OutputPorts);

        for (int index = 0; index < node.OutputPorts.Count; index++)
        {
            body.Children.Add(BuildPortRow(node.OutputPorts[index], visual, index));
        }

        Canvas.SetLeft(card, node.Position.X);
        Canvas.SetTop(card, node.Position.Y);
        GraphCanvas.Children.Add(card);

        card.PointerPressed += (_, args) => OnCardPressed(visual, args);
        return visual;
    }

    private FileProxyVisual BuildFileProxy(CollapsedFileProjection file)
    {
        var content = new StackPanel { Spacing = 0 };

        var headerDot = new Ellipse
        {
            Width = 11,
            Height = 11,
            Fill = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var headerText = new StackPanel
        {
            Spacing = 1,
            Children =
            {
                new TextBlock
                {
                    Text = file.FileName,
                    FontWeight = FontWeight.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                new TextBlock
                {
                    Text = $"접힘 · {file.Nodes.Count}개 노드 · {file.RelativePath}",
                    FontSize = 9,
                    Opacity = 0.6,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            }
        };

        var header = new Grid
        {
            Height = ProxyHeaderHeight,
            Margin = new Thickness(10, 0),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(headerDot, 0);
        Grid.SetColumn(headerText, 1);
        header.Children.Add(headerDot);
        header.Children.Add(headerText);
        content.Children.Add(header);

        var rows = new List<ProxyNodeRow>();

        if (file.Nodes.Count == 0)
        {
            content.Children.Add(new Border
            {
                Height = ProxyRowHeight,
                Child = new TextBlock
                {
                    Text = "빈 파일",
                    Margin = new Thickness(22, 0),
                    FontSize = 10,
                    Opacity = 0.5,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
        }
        else
        {
            for (int index = 0; index < file.Nodes.Count; index++)
            {
                CollapsedNodeEntry entry = file.Nodes[index];
                ProxyNodeRow row = BuildProxyRow(entry, index);
                rows.Add(row);
                content.Children.Add(row.Visual);
            }
        }

        var visual = new Border
        {
            Width = ProxyWidth,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
            BorderThickness = new Thickness(1),
            ClipToBounds = false,
            Child = content,
            Tag = file.FileId
        };

        Canvas.SetLeft(visual, file.Position.X);
        Canvas.SetTop(visual, file.Position.Y);
        GraphCanvas.Children.Add(visual);

        // 접힌 파일도 끌어 옮긴다 (W52). 노드 행은 자기 클릭을 Handled로 막으므로
        // 여기 오는 좌클릭은 헤더·테두리다. 빈 파일 프록시는 옮길 내용이 없다.
        visual.Cursor = new Cursor(StandardCursorType.SizeAll);
        visual.PointerPressed += (_, args) =>
        {
            if (_session is null ||
                !args.GetCurrentPoint(visual).Properties.IsLeftButtonPressed ||
                _session.Project.FindFile(file.FileId) is not { Nodes.Count: > 0 } storyFile)
            {
                return;
            }

            _draggingProxyFileId = file.FileId;
            _proxyDragStart = args.GetPosition(GraphCanvas);
            _proxyStartPositions.Clear();

            foreach (StoryNode node in storyFile.Nodes)
            {
                _proxyStartPositions[node.Id] = new Point(node.Layout.X, node.Layout.Y);
            }

            args.Handled = true;
        };

        // 접힌 표를 더블클릭 = 펼치기 — 이름표 클릭(접기)의 짝이다.
        ToolTip.SetTip(visual, "더블클릭하면 이 챕터를 펼칩니다.");
        visual.DoubleTapped += (_, args) =>
        {
            args.Handled = true;
            _draggingProxyFileId = null; // 둘째 누름이 시작한 끌기 상태를 걷는다
            _session?.SetFileExpanded(file.FileId, expanded: true);
        };

        return new FileProxyVisual(file.FileId, visual, rows);
    }

    private ProxyNodeRow BuildProxyRow(CollapsedNodeEntry entry, int index)
    {
        IBrush dotBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));

        var input = new Ellipse
        {
            Width = ProxyPortRadius * 2,
            Height = ProxyPortRadius * 2,
            Margin = new Thickness(-ProxyPortRadius, 0, 7, 0),
            Fill = entry.IncomingCount > 0 ? dotBrush : Brushes.Transparent,
            Stroke = dotBrush,
            StrokeThickness = 1.5,
            VerticalAlignment = VerticalAlignment.Center
        };

        var name = new TextBlock
        {
            Text = entry.NodeName,
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        var kind = new TextBlock
        {
            Text = NodeKindLabel(entry.NodeKind),
            FontSize = 9,
            Opacity = 0.5,
            Margin = new Thickness(6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var output = new Ellipse
        {
            Width = ProxyPortRadius * 2,
            Height = ProxyPortRadius * 2,
            Margin = new Thickness(7, 0, -ProxyPortRadius, 0),
            Fill = entry.OutgoingCount > 0 ? dotBrush : Brushes.Transparent,
            Stroke = dotBrush,
            StrokeThickness = 1.5,
            VerticalAlignment = VerticalAlignment.Center
        };

        var grid = new Grid
        {
            Height = ProxyRowHeight,
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto")
        };
        Grid.SetColumn(input, 0);
        Grid.SetColumn(name, 1);
        Grid.SetColumn(kind, 2);
        Grid.SetColumn(output, 3);
        grid.Children.Add(input);
        grid.Children.Add(name);
        grid.Children.Add(kind);
        grid.Children.Add(output);

        var row = new Border
        {
            Height = ProxyRowHeight,
            Padding = new Thickness(0, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(35, 107, 114, 128)),
            BorderThickness = new Thickness(0, index == 0 ? 1 : 0, 0, 1),
            Child = grid,
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = entry.NodeId
        };

        row.PointerPressed += (_, args) =>
        {
            _session?.Select(entry.NodeId);
            HighlightSelection();
            args.Handled = true;
        };

        return new ProxyNodeRow(entry.NodeId, entry.NodeKind, index, row);
    }

    private Control BuildPortRow(GraphOutputPortProjection port, NodeCard card, int index)
    {
        bool branch = port.Kind == GraphOutputPortKind.ExecutionBranch;
        bool settings = port.Kind == GraphOutputPortKind.Settings;
        bool presentation = port.Kind == GraphOutputPortKind.PublishedResult;
        IBrush settingsBrush = new SolidColorBrush(Color.FromRgb(0x0F, 0x76, 0x6E));
        IBrush presentationBrush = new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED));

        var label = new TextBlock
        {
            Text = port.Label,
            FontSize = 10,
            Opacity = port.Kind == GraphOutputPortKind.ExecutionDefault ? 0.6 : 1,
            Foreground = branch
                ? BranchPalette.Accent(port.PaletteIndex)
                : settings
                    ? settingsBrush
                    : presentation
                        ? presentationBrush
                        : null,
            FontWeight = branch || settings || presentation ? FontWeight.SemiBold : FontWeight.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        IBrush portBrush = branch
            ? BranchPalette.Accent(port.PaletteIndex)
            : settings
                ? settingsBrush
                : presentation
                    ? presentationBrush
                    : Brushes.DimGray;

        var knob = new Ellipse
        {
            Width = PortRadius * 2,
            Height = PortRadius * 2,
            Margin = new Thickness(6, 0, -CardPadding - PortRadius, 0),
            Fill = port.IsConnected ? portBrush : Brushes.Transparent,
            Stroke = portBrush,
            StrokeThickness = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        knob.PointerPressed += (_, args) => OnPortPressed(port, card, index, args);

        var row = new Grid
        {
            Height = PortRowHeight,
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };

        Grid.SetColumn(label, 0);
        Grid.SetColumn(knob, 1);
        row.Children.Add(label);
        row.Children.Add(knob);

        return row;
    }

    private void DrawEdges()
    {
        if (_projection is null)
        {
            return;
        }

        foreach (GraphConnectionProjection connection in _projection.Connections)
        {
            AddEdge(connection);
        }
    }

    private void AddEdge(GraphConnectionProjection connection)
    {
        if (!TryEndpointAnchor(connection.Source, out _) ||
            !TryEndpointAnchor(connection.Target, out _))
        {
            return;
        }

        IBrush stroke = ConnectionBrush(connection);
        var path = new ShapePath
        {
            Stroke = stroke,
            StrokeThickness = 2,
            StrokeDashArray = connection.Kind switch
            {
                GraphConnectionKind.Settings => new AvaloniaList<double> { 5, 3 },
                GraphConnectionKind.ResultSnapshot => new AvaloniaList<double> { 2, 3 },
                _ => null
            }
        };

        bool showLabel = connection.Kind is
            GraphConnectionKind.ExecutionBranch or
            GraphConnectionKind.Settings or
            GraphConnectionKind.ResultSnapshot;
        var label = new Border
        {
            Padding = new Thickness(5, 1),
            CornerRadius = new CornerRadius(3),
            Background = showLabel ? stroke : Brushes.Transparent,
            IsVisible = showLabel,
            Child = new TextBlock
            {
                Text = connection.Label,
                FontSize = 9,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White
            }
        };

        var edge = new EdgeVisual(connection, path, label);

        // 좌클릭 = 선택(하단에 정보), 우클릭 = 삭제 (W47 — 툴바의 간선 삭제 버튼을 대신한다).
        void OnEdgePressed(PointerPressedEventArgs args, Control source)
        {
            SelectEdge(edge);

            if (args.GetCurrentPoint(source).Properties.IsRightButtonPressed)
            {
                DeleteSelectedEdge();
            }

            args.Handled = true;
        }

        path.PointerPressed += (_, args) => OnEdgePressed(args, path);
        label.PointerPressed += (_, args) => OnEdgePressed(args, label);

        GraphCanvas.Children.Insert(0, path);
        GraphCanvas.Children.Add(label);
        _edges.Add(edge);

        PositionEdge(edge);
    }

    private void PositionEdge(EdgeVisual edge)
    {
        if (!TryEndpointAnchor(edge.Connection.Source, out Point from) ||
            !TryEndpointAnchor(edge.Connection.Target, out Point to))
        {
            edge.Path.IsVisible = false;
            edge.Label.IsVisible = false;
            return;
        }

        edge.Path.IsVisible = true;
        bool showLabel = edge.Connection.Kind is
            GraphConnectionKind.ExecutionBranch or
            GraphConnectionKind.Settings or
            GraphConnectionKind.ResultSnapshot;
        edge.Label.IsVisible = showLabel;

        IReadOnlyList<GraphPosition> route = OrthogonalEdgeRouter.Route(
            new GraphPosition(from.X, from.Y),
            new GraphPosition(to.X, to.Y));

        var geometry = new StreamGeometry();

        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(ToPoint(route[0]), isFilled: false);

            for (int index = 1; index < route.Count; index++)
            {
                context.LineTo(ToPoint(route[index]));
            }

            context.EndFigure(isClosed: false);
        }

        edge.Path.Data = geometry;

        GraphPosition labelPoint = new(
            (route[1].X + route[2].X) / 2,
            (route[1].Y + route[2].Y) / 2);

        edge.Label.Measure(Size.Infinity);
        Canvas.SetLeft(edge.Label, labelPoint.X - (edge.Label.DesiredSize.Width / 2));
        Canvas.SetTop(edge.Label, labelPoint.Y - (edge.Label.DesiredSize.Height / 2));
    }

    private bool TryEndpointAnchor(GraphEndpointProjection endpoint, out Point point)
    {
        switch (endpoint.Kind)
        {
            case GraphEndpointKind.ExpandedNodeOutput:
            {
                NodeCard? card = FindCard(endpoint.NodeId);
                int portIndex = card?.PortIndex(endpoint.PortKey) ?? -1;

                if (card is not null && portIndex >= 0)
                {
                    point = PortAnchor(card, portIndex);
                    return true;
                }

                break;
            }

            case GraphEndpointKind.ExpandedNodeInput:
            {
                NodeCard? card = FindCard(endpoint.NodeId);

                if (card is not null)
                {
                    point = InputAnchor(card);
                    return true;
                }

                break;
            }

            case GraphEndpointKind.CollapsedFileNodeOutput:
            case GraphEndpointKind.CollapsedFileNodeInput:
            {
                FileProxyVisual? proxy = FindProxy(endpoint.FileId);

                if (proxy is not null && endpoint.ProxyRowIndex is { } rowIndex)
                {
                    point = ProxyRowAnchor(
                        proxy,
                        rowIndex,
                        output: endpoint.Kind == GraphEndpointKind.CollapsedFileNodeOutput);
                    return true;
                }

                break;
            }
        }

        point = default;
        return false;
    }

    /// <summary>펼쳐진 노드 카드의 출력 포트 좌표.</summary>
    private static Point PortAnchor(NodeCard card, int portIndex)
    {
        double x = Canvas.GetLeft(card.Visual) + CardWidth;
        double y = Canvas.GetTop(card.Visual)
            + CardPadding + HeaderHeight
            + (portIndex * PortRowHeight) + (PortRowHeight / 2);

        return new Point(x, y);
    }

    private static Point InputAnchor(NodeCard card)
    {
        return new Point(
            Canvas.GetLeft(card.Visual),
            Canvas.GetTop(card.Visual) + CardPadding + (HeaderHeight / 2));
    }

    private static Point ProxyRowAnchor(FileProxyVisual proxy, int rowIndex, bool output)
    {
        double x = Canvas.GetLeft(proxy.Visual) + (output ? ProxyWidth : 0);
        double y = Canvas.GetTop(proxy.Visual)
            + ProxyHeaderHeight
            + (rowIndex * ProxyRowHeight)
            + (ProxyRowHeight / 2);

        return new Point(x, y);
    }

    // ── 그래프 내비게이션 (W40): 줌·팬·미니맵 ─────────────────────────────

    private void OnGraphWheel(object? sender, PointerWheelEventArgs args)
    {
        if (!args.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return; // 그냥 휠은 기존 스크롤 그대로
        }

        double factor = args.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        ApplyZoom(_zoom * factor, args.GetPosition(GraphScroll));
        args.Handled = true;
    }

    /// <summary>배율 적용 — anchor(뷰포트 좌표) 아래의 내용이 그 자리에 남게 오프셋을 맞춘다.</summary>
    private void ApplyZoom(double zoom, Point? anchor)
    {
        Point pivot = anchor ?? new Point(
            GraphScroll.Viewport.Width / 2, GraphScroll.Viewport.Height / 2);

        // 지금 pivot 아래에 있는 캔버스 좌표.
        var content = new Point(
            (GraphScroll.Offset.X + pivot.X) / _zoom,
            (GraphScroll.Offset.Y + pivot.Y) / _zoom);

        SetZoom(zoom);
        GraphScroll.Offset = new Vector(
            Math.Max(0, (content.X * _zoom) - pivot.X),
            Math.Max(0, (content.Y * _zoom) - pivot.Y));
        RefreshMinimapViewport();
    }

    /// <summary>배율의 변환·표시만 — 오프셋은 호출자가 자기 규칙(고정점·중앙)으로 정한다.</summary>
    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        ZoomHost.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        ZoomText.Text = $"{Math.Round(_zoom * 100)}%";
        ZoomHost.UpdateLayout(); // 새 크기를 알아야 오프셋 상한이 맞는다
    }

    /// <summary>
    /// 특정 챕터(판)로 이동 — 그 판의 노드들이 여백을 두고 들어오는 배율과 위치.
    /// 왼쪽 챕터 목록을 클릭했을 때 여러 챕터 박스 사이에서 그 챕터를 눈앞에 가져온다.
    /// </summary>
    private void FitFile(string fileId)
    {
        if (GraphScroll.Viewport.Width <= 0 || GraphScroll.Viewport.Height <= 0)
        {
            return;
        }

        Rect? bounds = null;

        foreach (ExpandedNodeProjection node in (_projection?.Items ?? [])
                     .OfType<ExpandedNodeProjection>()
                     .Where(item => string.Equals(item.FileId, fileId, StringComparison.Ordinal)))
        {
            if (FindCard(node.NodeId) is not { } card)
            {
                continue;
            }

            var rect = new Rect(node.Position.X, node.Position.Y, CardWidth, CardHeightOf(card));
            bounds = bounds is { } current ? current.Union(rect) : rect;
        }

        if (bounds is not { } area)
        {
            FitAll(); // 빈 판이거나 접혀 있다 — 전체 보기가 차선이다
            return;
        }

        area = area.Inflate(80);
        SetZoom(Math.Min(
            1.0,
            Math.Min(GraphScroll.Viewport.Width / area.Width, GraphScroll.Viewport.Height / area.Height)));
        GraphScroll.Offset = new Vector(
            Math.Max(0, (area.Center.X * _zoom) - (GraphScroll.Viewport.Width / 2)),
            Math.Max(0, (area.Center.Y * _zoom) - (GraphScroll.Viewport.Height / 2)));
        RefreshMinimapViewport();
    }

    /// <summary>전체 보기 (GB-4) — 노드·프록시 전부가 여백을 두고 들어오는 배율과 위치.</summary>
    private void FitAll()
    {
        if (GraphScroll.Viewport.Width <= 0 || GraphScroll.Viewport.Height <= 0)
        {
            return; // 레이아웃 전 — 맞출 화면이 아직 없다
        }

        Rect? bounds = null;

        foreach (NodeCard card in _cards)
        {
            var rect = new Rect(
                Canvas.GetLeft(card.Visual), Canvas.GetTop(card.Visual),
                CardWidth, CardHeightOf(card));
            bounds = bounds is { } current ? current.Union(rect) : rect;
        }

        foreach (FileProxyVisual proxy in _proxies)
        {
            var rect = new Rect(
                Canvas.GetLeft(proxy.Visual), Canvas.GetTop(proxy.Visual),
                ProxyWidth, ProxyHeaderHeight + (Math.Max(1, proxy.Rows.Count) * ProxyRowHeight));
            bounds = bounds is { } current ? current.Union(rect) : rect;
        }

        if (bounds is not { } all)
        {
            ApplyZoom(1, null); // 빈 판 — 기본 배율로 돌아간다
            GraphScroll.Offset = default;
            return;
        }

        all = all.Inflate(80); // 가장자리 여백
        SetZoom(Math.Min(
            1.0, // 노드가 적다고 확대까지 하지는 않는다
            Math.Min(GraphScroll.Viewport.Width / all.Width, GraphScroll.Viewport.Height / all.Height)));
        GraphScroll.Offset = new Vector(
            Math.Max(0, (all.Center.X * _zoom) - (GraphScroll.Viewport.Width / 2)),
            Math.Max(0, (all.Center.Y * _zoom) - (GraphScroll.Viewport.Height / 2)));
        RefreshMinimapViewport();
    }

    /// <summary>
    /// 선택 노드로 화면 이동 (GB-4) — 이미 보이는 노드에는 끼어들지 않는다.
    /// 접힌 파일 안의 노드면 그 프록시로 간다.
    /// </summary>
    private void ScrollToSelected()
    {
        if (_session?.SelectedNodeId is not { } nodeId)
        {
            return;
        }

        Control? target = FindCard(nodeId)?.Visual;
        target ??= _proxies.FirstOrDefault(proxy => proxy.Rows.Any(row =>
            string.Equals(row.NodeId, nodeId, StringComparison.Ordinal)))?.Visual;

        if (target is null)
        {
            return;
        }

        double left = Canvas.GetLeft(target);
        double top = Canvas.GetTop(target);
        double width = target.Bounds.Width > 0 ? target.Bounds.Width : CardWidth;
        double height = target.Bounds.Height > 0 ? target.Bounds.Height : 160;

        var viewRect = new Rect(
            GraphScroll.Offset.X / _zoom,
            GraphScroll.Offset.Y / _zoom,
            GraphScroll.Viewport.Width / _zoom,
            GraphScroll.Viewport.Height / _zoom);

        if (viewRect.Intersects(new Rect(left, top, width, height)))
        {
            return;
        }

        GraphScroll.Offset = new Vector(
            Math.Max(0, ((left + (width / 2)) * _zoom) - (GraphScroll.Viewport.Width / 2)),
            Math.Max(0, ((top + (height / 2)) * _zoom) - (GraphScroll.Viewport.Height / 2)));
    }

    private void OnGraphPanPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(GraphScroll).Properties.IsMiddleButtonPressed)
        {
            return;
        }

        _panning = true;
        _panStart = args.GetPosition(GraphScroll);
        _panStartOffset = GraphScroll.Offset;
        args.Pointer.Capture(GraphScroll);
        args.Handled = true;
    }

    private void OnGraphPanMoved(object? sender, PointerEventArgs args)
    {
        if (!_panning)
        {
            return;
        }

        Point now = args.GetPosition(GraphScroll);
        GraphScroll.Offset = new Vector(
            Math.Max(0, _panStartOffset.X - (now.X - _panStart.X)),
            Math.Max(0, _panStartOffset.Y - (now.Y - _panStart.Y)));
        args.Handled = true;
    }

    private void OnGraphPanReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (_panning)
        {
            _panning = false;
            args.Pointer.Capture(null);
            args.Handled = true;
        }
    }

    /// <summary>미니맵 전체 다시 그리기 — 카드·프록시를 축소 사각형으로, 위에 뷰포트 틀.</summary>
    private void RefreshMinimap()
    {
        MinimapCanvas.Children.Clear();

        foreach (NodeCard card in _cards)
        {
            AddMinimapRect(
                Canvas.GetLeft(card.Visual),
                Canvas.GetTop(card.Visual),
                CardWidth,
                CardHeightOf(card),
                card.NodeKind switch
                {
                    GraphNodeKind.Set => Color.FromRgb(0x3B, 0x82, 0xF6),
                    GraphNodeKind.Presentation => Color.FromRgb(0x8B, 0x5C, 0xF6),
                    GraphNodeKind.CommandSupply => Color.FromRgb(0x22, 0xC5, 0x5E),
                    _ => Color.FromRgb(0x9C, 0xA3, 0xAF)
                });
        }

        foreach (FileProxyVisual proxy in _proxies)
        {
            AddMinimapRect(
                Canvas.GetLeft(proxy.Visual),
                Canvas.GetTop(proxy.Visual),
                ProxyWidth,
                ProxyHeaderHeight + (Math.Max(1, proxy.Rows.Count) * ProxyRowHeight),
                Color.FromRgb(0x6B, 0x72, 0x80));
        }

        _minimapViewport = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
            StrokeThickness = 1,
            Fill = new SolidColorBrush(Color.FromArgb(20, 37, 99, 235)),
            IsHitTestVisible = false
        };
        MinimapCanvas.Children.Add(_minimapViewport);
        RefreshMinimapViewport();
    }

    private void AddMinimapRect(double x, double y, double width, double height, Color color)
    {
        var rect = new Rectangle
        {
            Width = Math.Max(2, width * (MinimapWidth / CanvasWidth)),
            Height = Math.Max(2, height * (MinimapHeight / CanvasHeight)),
            Fill = new SolidColorBrush(Color.FromArgb(170, color.R, color.G, color.B)),
            RadiusX = 1,
            RadiusY = 1,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(rect, x * (MinimapWidth / CanvasWidth));
        Canvas.SetTop(rect, y * (MinimapHeight / CanvasHeight));
        MinimapCanvas.Children.Add(rect);
    }

    /// <summary>뷰포트 틀만 옮긴다 — 스크롤·줌마다 불리므로 전체를 다시 그리지 않는다.</summary>
    private void RefreshMinimapViewport()
    {
        if (_minimapViewport is null)
        {
            return;
        }

        double scaleX = MinimapWidth / CanvasWidth;
        double scaleY = MinimapHeight / CanvasHeight;

        double left = GraphScroll.Offset.X / _zoom * scaleX;
        double top = GraphScroll.Offset.Y / _zoom * scaleY;
        double width = Math.Min(MinimapWidth, GraphScroll.Viewport.Width / _zoom * scaleX);
        double height = Math.Min(MinimapHeight, GraphScroll.Viewport.Height / _zoom * scaleY);

        Canvas.SetLeft(_minimapViewport, Math.Clamp(left, 0, MinimapWidth));
        Canvas.SetTop(_minimapViewport, Math.Clamp(top, 0, MinimapHeight));
        _minimapViewport.Width = width;
        _minimapViewport.Height = height;
    }

    private void OnMinimapPressed(object? sender, PointerPressedEventArgs args)
    {
        _minimapDragging = true;
        MoveViewportTo(args.GetPosition(MinimapCanvas));
        args.Pointer.Capture(MinimapCanvas);
        args.Handled = true;
    }

    private void OnMinimapMoved(object? sender, PointerEventArgs args)
    {
        if (_minimapDragging)
        {
            MoveViewportTo(args.GetPosition(MinimapCanvas));
            args.Handled = true;
        }
    }

    /// <summary>미니맵의 한 점이 뷰포트 중앙에 오도록 스크롤을 옮긴다.</summary>
    private void MoveViewportTo(Point minimapPoint)
    {
        double contentX = minimapPoint.X / (MinimapWidth / CanvasWidth);
        double contentY = minimapPoint.Y / (MinimapHeight / CanvasHeight);

        GraphScroll.Offset = new Vector(
            Math.Max(0, (contentX * _zoom) - (GraphScroll.Viewport.Width / 2)),
            Math.Max(0, (contentY * _zoom) - (GraphScroll.Viewport.Height / 2)));
    }

    private double CardHeightOf(NodeCard card)
    {
        return card.Visual.Bounds.Height > 0
            ? card.Visual.Bounds.Height
            : HeaderHeight + (card.Ports.Count * PortRowHeight) + (CardPadding * 2);
    }

    // ── 조작 ────────────────────────────────────────────────────────────────

    private void OnCardPressed(NodeCard card, PointerPressedEventArgs args)
    {
        _session?.Select(card.NodeId);

        // 범위 선택된 카드를 잡으면 무리가 함께 움직인다 (W40).
        if (_multiSelected.Contains(card.NodeId) && _multiSelected.Count > 1)
        {
            _draggingGroup = true;
            _groupDragStart = args.GetPosition(GraphCanvas);
            _groupStartPositions.Clear();

            foreach (NodeCard member in _cards)
            {
                if (_multiSelected.Contains(member.NodeId))
                {
                    _groupStartPositions[member.NodeId] = new Point(
                        Canvas.GetLeft(member.Visual), Canvas.GetTop(member.Visual));
                }
            }

            HighlightSelection();
            args.Handled = true;
            return;
        }

        _multiSelected.Clear(); // 무리 밖 카드를 잡으면 단일 선택으로 돌아간다
        HighlightSelection();

        _draggingCard = card;
        Point position = args.GetPosition(GraphCanvas);
        _dragOffset = new Point(
            position.X - Canvas.GetLeft(card.Visual),
            position.Y - Canvas.GetTop(card.Visual));

        args.Handled = true;
    }

    private void OnPortPressed(
        GraphOutputPortProjection port,
        NodeCard card,
        int index,
        PointerPressedEventArgs args)
    {
        _connectingFrom = port;

        Point start = PortAnchor(card, index);
        _connectingLine = new Line
        {
            StartPoint = start,
            EndPoint = start,
            Stroke = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
            StrokeThickness = 2,
            StrokeDashArray = new AvaloniaList<double> { 4, 3 }
        };

        GraphCanvas.Children.Add(_connectingLine);
        HintText.Text = port.Kind switch
        {
            GraphOutputPortKind.Settings =>
                "조건을 공급할 대사 노드 또는 접힌 파일의 대사 행 위에서 놓으세요.",
            GraphOutputPortKind.PublishedResult =>
                "이 대사 노드의 최신 발행 결과를 읽을 연출 노드 위에서 놓으세요.",
            _ =>
                "연결할 실행 노드 또는 접힌 파일의 실행 노드 행 위에서 놓으세요. 빈 곳에 놓으면 실행 연결이 끊어집니다."
        };

        args.Handled = true;
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (args.Source is not Canvas)
        {
            return;
        }

        SelectEdge(null);

        // 빈 곳 좌클릭 = 범위 선택 시작 (W40). 이전 무리는 새 범위가 대신한다.
        if (args.GetCurrentPoint(GraphCanvas).Properties.IsLeftButtonPressed)
        {
            _multiSelected.Clear();
            HighlightSelection();

            _rubberStart = args.GetPosition(GraphCanvas);
            _rubberBand = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
                StrokeThickness = 1,
                StrokeDashArray = new AvaloniaList<double> { 4, 3 },
                Fill = new SolidColorBrush(Color.FromArgb(28, 37, 99, 235)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(_rubberBand, _rubberStart.X);
            Canvas.SetTop(_rubberBand, _rubberStart.Y);
            GraphCanvas.Children.Add(_rubberBand);
            args.Pointer.Capture(GraphCanvas);
        }
    }

    private Rect RubberRect(Point current)
    {
        return new Rect(
            new Point(Math.Min(_rubberStart.X, current.X), Math.Min(_rubberStart.Y, current.Y)),
            new Point(Math.Max(_rubberStart.X, current.X), Math.Max(_rubberStart.Y, current.Y)));
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs args)
    {
        Point position = args.GetPosition(GraphCanvas);

        if (_rubberBand is not null)
        {
            Rect rect = RubberRect(position);
            Canvas.SetLeft(_rubberBand, rect.X);
            Canvas.SetTop(_rubberBand, rect.Y);
            _rubberBand.Width = rect.Width;
            _rubberBand.Height = rect.Height;
            return;
        }

        if (_connectingLine is not null)
        {
            _connectingLine.EndPoint = position;
            return;
        }

        if (_session is null)
        {
            return;
        }

        if (_draggingGroup)
        {
            double deltaX = position.X - _groupDragStart.X;
            double deltaY = position.Y - _groupDragStart.Y;

            foreach ((string nodeId, Point start) in _groupStartPositions)
            {
                _session.Editor.MoveNode(
                    nodeId,
                    ClampNodeX(start.X + deltaX),
                    ClampNodeY(start.Y + deltaY));
            }

            return;
        }

        if (_draggingProxyFileId is not null)
        {
            double deltaX = position.X - _proxyDragStart.X;
            double deltaY = position.Y - _proxyDragStart.Y;

            foreach ((string nodeId, Point start) in _proxyStartPositions)
            {
                _session.Editor.MoveNode(
                    nodeId,
                    ClampNodeX(start.X + deltaX),
                    ClampNodeY(start.Y + deltaY));
            }

            return;
        }

        if (_draggingCard is null)
        {
            return;
        }

        _session.Editor.MoveNode(
            _draggingCard.NodeId,
            ClampNodeX(position.X - _dragOffset.X),
            ClampNodeY(position.Y - _dragOffset.Y));
    }

    // 판 경계 클램프 (GB-4) — 카드가 스크롤이 닿지 않는 바깥으로 끌려 나가지 않는다.
    private static double ClampNodeX(double x) => Math.Clamp(x, 0, CanvasWidth - CardWidth);

    private static double ClampNodeY(double y) => Math.Clamp(y, 0, CanvasHeight - 240);

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        _draggingCard = null;
        _draggingGroup = false;
        _draggingProxyFileId = null;

        // 범위 선택 확정 (W40) — 사각형에 걸친 카드 전부가 무리가 된다.
        if (_rubberBand is not null)
        {
            Rect rect = RubberRect(args.GetPosition(GraphCanvas));
            GraphCanvas.Children.Remove(_rubberBand);
            _rubberBand = null;
            args.Pointer.Capture(null);

            _multiSelected.Clear();

            foreach (NodeCard card in _cards)
            {
                var bounds = new Rect(
                    Canvas.GetLeft(card.Visual),
                    Canvas.GetTop(card.Visual),
                    CardWidth,
                    CardHeightOf(card));

                if (rect.Intersects(bounds))
                {
                    _multiSelected.Add(card.NodeId);
                }
            }

            HighlightSelection();
            return;
        }

        if (_connectingFrom is null)
        {
            return;
        }

        GraphOutputPortProjection port = _connectingFrom;
        _connectingFrom = null;

        if (_connectingLine is not null)
        {
            GraphCanvas.Children.Remove(_connectingLine);
            _connectingLine = null;
        }

        HintText.Text = "포트(●)를 끌어 실제 노드나 FileProxy의 노드 행에 놓으면 연결됩니다.";

        GraphNodeHit? dropped = NodeAt(args.GetPosition(GraphCanvas));

        if (port.Kind == GraphOutputPortKind.Settings)
        {
            HandleSupplyDrop(port, dropped);
            return;
        }

        if (port.Kind == GraphOutputPortKind.PublishedResult)
        {
            AttachLatestResult(port.NodeId, dropped);
            return;
        }

        if (dropped is { NodeKind: GraphNodeKind.Presentation })
        {
            _session?.SetStatus("PresentationNode는 실행 출구의 대상이 될 수 없습니다.");
            return;
        }

        string? target = dropped is null || string.Equals(dropped.NodeId, port.NodeId, StringComparison.Ordinal)
            ? null
            : dropped.NodeId;

        if (port.ExecutionPort is not null)
        {
            _session?.Editor.SetExitTarget(port.ExecutionPort, target);
        }
    }

    /// <summary>
    /// 공급 포트(조건·커맨드·연출 — 전부 비실행 연결)를 놓았을 때.
    /// 어떤 연결이 되는지는 <b>포트를 소유한 노드의 종류</b>가 정한다.
    /// 잘못된 대상은 상태 표시줄로 알리기만 한다 — 드래그 한 번에 툴이 죽으면 안 된다.
    /// </summary>
    private void HandleSupplyDrop(GraphOutputPortProjection port, GraphNodeHit? dropped)
    {
        if (_session is null ||
            dropped is null ||
            string.Equals(dropped.NodeId, port.NodeId, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            switch (_session.Project.FindNode(port.NodeId))
            {
                case SetNode when dropped.NodeKind == GraphNodeKind.Dialogue:
                    _session.Editor.AddSettingsLink(port.NodeId, dropped.NodeId);
                    _session.SetStatus("조건 공급을 연결했습니다.");
                    break;

                case SetNode:
                    _session.SetStatus("조건 공급은 대사 노드에만 연결할 수 있습니다.");
                    break;

                case CommandSupplyNode when dropped.NodeKind == GraphNodeKind.Presentation:
                    _session.Editor.AddCommandSupplyLink(port.NodeId, dropped.NodeId);
                    _session.SetStatus("커맨드 공급을 연결했습니다.");
                    break;

                case CommandSupplyNode:
                    _session.SetStatus("커맨드 공급은 연출 노드에만 연결할 수 있습니다.");
                    break;

                case PresentationNode when dropped.NodeKind == GraphNodeKind.Dialogue:
                    _session.Editor.SetPresentationSupplyTarget(port.NodeId, dropped.NodeId);
                    _session.SetStatus("연출 공급을 연결했습니다. 내보내기가 이 짝을 사용합니다.");
                    break;

                case PresentationNode:
                    _session.SetStatus("연출 공급은 대사 노드에만 연결할 수 있습니다.");
                    break;
            }
        }
        catch (InvalidOperationException exception)
        {
            _session.SetStatus(exception.Message);
        }
    }

    /// <summary>
    /// 대사 노드의 발행 결과 포트를 연출 노드에 끌어다 놓았을 때.
    ///
    /// <b>최신 버전을 명시적으로 고정한다.</b> "이 노드의 최신"으로 저장하면 다음 발행 때
    /// 연출가가 모르는 사이에 발밑의 대사가 바뀐다. 다른 버전으로 옮기는 것은 연출 편집기의
    /// 버전 목록에서 한다.
    /// </summary>
    private void AttachLatestResult(string dialogueNodeId, GraphNodeHit? dropped)
    {
        if (_session is null)
        {
            return;
        }

        if (dropped is null)
        {
            _session.SetStatus("연출 노드 위에 놓아야 입력 결과가 연결됩니다.");
            return;
        }

        if (dropped.NodeKind != GraphNodeKind.Presentation)
        {
            _session.SetStatus("발행 결과는 연출 노드만 읽을 수 있습니다.");
            return;
        }

        DialogueResult? latest = _session.Project.Results
            .DialogueResultsOf(dialogueNodeId)
            .LastOrDefault();

        if (latest is null)
        {
            _session.SetStatus(
                "이 대사 노드는 아직 발행된 결과가 없습니다. 대사 편집기에서 먼저 발행하세요.");
            return;
        }

        try
        {
            _session.Editor.SetPresentationSource(
                dropped.NodeId,
                latest.Identity.ResultId,
                latest.Identity.Version);
            _session.SetStatus($"연출이 '{latest.SourceNodeName} v{latest.Identity.Version}'을 읽습니다.");
        }
        catch (InvalidOperationException exception)
        {
            _session.SetStatus(exception.Message);
        }
    }

    private GraphNodeHit? NodeAt(Point point)
    {
        foreach (NodeCard card in _cards)
        {
            double left = Canvas.GetLeft(card.Visual);
            double top = Canvas.GetTop(card.Visual);
            double height = card.Visual.Bounds.Height > 0
                ? card.Visual.Bounds.Height
                : HeaderHeight + (card.Ports.Count * PortRowHeight) + (CardPadding * 2);

            if (point.X >= left && point.X <= left + CardWidth &&
                point.Y >= top && point.Y <= top + height)
            {
                return new GraphNodeHit(card.NodeId, card.NodeKind);
            }
        }

        foreach (FileProxyVisual proxy in _proxies)
        {
            double left = Canvas.GetLeft(proxy.Visual);
            double top = Canvas.GetTop(proxy.Visual);

            if (point.X < left || point.X > left + ProxyWidth || point.Y < top + ProxyHeaderHeight)
            {
                continue;
            }

            int rowIndex = (int)((point.Y - top - ProxyHeaderHeight) / ProxyRowHeight);

            if (rowIndex >= 0 && rowIndex < proxy.Rows.Count)
            {
                ProxyNodeRow row = proxy.Rows[rowIndex];
                return new GraphNodeHit(row.NodeId, row.NodeKind);
            }
        }

        return null;
    }

    private void SelectEdge(EdgeVisual? edge)
    {
        foreach (EdgeVisual item in _edges)
        {
            item.Path.StrokeThickness = 2;
        }

        _selectedEdge = edge;

        if (edge is null)
        {
            return;
        }

        edge.Path.StrokeThickness = 4;

        string sourceName = FindNodeName(edge.Connection.SourceNodeId) ?? edge.Connection.SourceNodeId;
        string targetName = FindNodeName(edge.Connection.TargetNodeId) ?? edge.Connection.TargetNodeId;
        string kind = edge.Connection.Kind switch
        {
            GraphConnectionKind.Settings => "조건 공급",
            GraphConnectionKind.ResultSnapshot => "발행 결과 입력",
            GraphConnectionKind.ExecutionBranch => $"조건 '{edge.Connection.Label}'",
            _ => "기본 출구"
        };

        HintText.Text = $"{sourceName} — {kind} → {targetName} · 우클릭으로 삭제";
    }

    private void DeleteSelectedEdge()
    {
        if (_selectedEdge is null || _session is null)
        {
            return;
        }

        if (_selectedEdge.Connection.LinkId is { } linkId)
        {
            _session.Editor.RemoveLink(linkId);
        }
        else if (_selectedEdge.Connection.ExecutionPort is { } exit)
        {
            _session.Editor.SetExitTarget(exit, null);
        }
    }

    private void AddNode(GraphNodeKind kind)
    {
        if (_session is null)
        {
            return;
        }

        if (_session.ActiveFileId is not { } fileId)
        {
            _session.SetStatus("새 노드를 추가할 StoryFile이 없습니다.");
            return;
        }

        StoryFile activeFile = _session.ActiveFile
            ?? throw new InvalidOperationException($"현재 StoryFile '{fileId}'를 찾을 수 없습니다.");

        // 지금 보고 있는 곳에 만든다 (GB-4) — 큰 판에서 "만들었는데 안 보임"을 없앤다.
        // 연속 생성은 계단으로 밀려 정확히 겹치지 않는다.
        int fileNodeCount = activeFile.Nodes.Count;
        double stagger = (fileNodeCount % 5) * 26;
        double x = ClampNodeX(
            ((GraphScroll.Offset.X + (GraphScroll.Viewport.Width / 2)) / _zoom) - (CardWidth / 2) + stagger);
        double y = ClampNodeY(
            ((GraphScroll.Offset.Y + (GraphScroll.Viewport.Height / 2)) / _zoom) - 90 + stagger);

        StoryNode node = kind switch
        {
            GraphNodeKind.Dialogue => _session.Editor.AddDialogueNode(fileId, x, y),
            GraphNodeKind.Set => _session.Editor.AddSetNode(fileId, x, y),
            GraphNodeKind.Presentation => _session.Editor.AddPresentationNode(fileId, x, y),
            GraphNodeKind.CommandSupply => _session.Editor.AddCommandSupplyNode(fileId, x, y),
            _ => throw new NotSupportedException($"지원하지 않는 그래프 노드 종류 '{kind}'입니다.")
        };

        _session.Select(node.Id);
    }

    private void DeleteSelectedNode()
    {
        if (_session?.SelectedNodeId is { } nodeId)
        {
            _session.Editor.RemoveNode(nodeId);
        }
    }

    private NodeCard? FindCard(string nodeId)
    {
        return _cards.FirstOrDefault(item => string.Equals(item.NodeId, nodeId, StringComparison.Ordinal));
    }

    private FileProxyVisual? FindProxy(string fileId)
    {
        return _proxies.FirstOrDefault(item => string.Equals(item.FileId, fileId, StringComparison.Ordinal));
    }

    private string? FindNodeName(string nodeId)
    {
        ExpandedNodeProjection? expanded = _projection?.Items
            .OfType<ExpandedNodeProjection>()
            .FirstOrDefault(item => string.Equals(item.NodeId, nodeId, StringComparison.Ordinal));

        if (expanded is not null)
        {
            return expanded.NodeName;
        }

        return _projection?.Items
            .OfType<CollapsedFileProjection>()
            .SelectMany(file => file.Nodes)
            .FirstOrDefault(item => string.Equals(item.NodeId, nodeId, StringComparison.Ordinal))
            ?.NodeName;
    }

    private static IBrush ConnectionBrush(GraphConnectionProjection connection)
    {
        return connection.Kind switch
        {
            GraphConnectionKind.Settings => new SolidColorBrush(Color.FromRgb(0x0F, 0x76, 0x6E)),
            GraphConnectionKind.ResultSnapshot => new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)),
            GraphConnectionKind.ExecutionBranch => BranchPalette.Accent(connection.PaletteIndex),
            _ => new SolidColorBrush(Color.FromArgb(150, 100, 100, 100))
        };
    }

    private static string NodeKindLabel(GraphNodeKind kind)
    {
        return kind switch
        {
            GraphNodeKind.Set => "설정",
            GraphNodeKind.Presentation => "연출",
            GraphNodeKind.CommandSupply => "연출 공급",
            _ => "대사"
        };
    }

    private static Point ToPoint(GraphPosition position) => new(position.X, position.Y);

    private sealed class NodeCard
    {
        public NodeCard(
            string nodeId,
            GraphNodeKind nodeKind,
            Border visual,
            IReadOnlyList<GraphOutputPortProjection> ports)
        {
            NodeId = nodeId;
            NodeKind = nodeKind;
            Visual = visual;
            Ports = ports;
        }

        public string NodeId { get; }
        public GraphNodeKind NodeKind { get; }
        public Border Visual { get; }
        public IReadOnlyList<GraphOutputPortProjection> Ports { get; }

        public int PortIndex(string? key)
        {
            return key is null
                ? -1
                : Ports.ToList().FindIndex(port => string.Equals(port.Key, key, StringComparison.Ordinal));
        }
    }

    private sealed class FileProxyVisual
    {
        public FileProxyVisual(string fileId, Border visual, IReadOnlyList<ProxyNodeRow> rows)
        {
            FileId = fileId;
            Visual = visual;
            Rows = rows;
        }

        public string FileId { get; }
        public Border Visual { get; }
        public IReadOnlyList<ProxyNodeRow> Rows { get; }
    }

    private sealed record ProxyNodeRow(
        string NodeId,
        GraphNodeKind NodeKind,
        int Index,
        Border Visual);

    private sealed record GraphNodeHit(string NodeId, GraphNodeKind NodeKind);

    private sealed class EdgeVisual
    {
        public EdgeVisual(GraphConnectionProjection connection, ShapePath path, Border label)
        {
            Connection = connection;
            Path = path;
            Label = label;
        }

        public GraphConnectionProjection Connection { get; set; }
        public ShapePath Path { get; }
        public Border Label { get; }
    }
}
