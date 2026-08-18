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
using Vn.Authoring.Flow;
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

        // 그래프 내비게이션 (W40) — 휠 줌·중간 버튼 팬은 스크롤보다 먼저 가로챈다.
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

            // 에피소드 Id → 엑셀노드·카드 자리, 그리고 판의 모든 노드 자리(체인 경유용).
            // 아직 동기화 전인 에피소드는 없는 것으로 둔다 — 없는 노드로 선을 그으면 거짓말이다.
            var spots = new Dictionary<string, (DialogueNode Node, Rect Rect)>(StringComparer.Ordinal);
            var nodeRects = new Dictionary<string, Rect>(StringComparer.Ordinal);
            var freeNodes = new List<DialogueNode>();

            foreach (ExpandedNodeProjection item in group)
            {
                if (FindCard(item.NodeId) is not { } card)
                {
                    continue;
                }

                var rect = new Rect(item.Position.X, item.Position.Y, CardWidth, CardHeightOf(card));
                nodeRects[item.NodeId] = rect;

                if (_session.Project.FindNode(item.NodeId) is DialogueNode dialogue)
                {
                    if (dialogue.ExcelEpisodeId is { } episodeId)
                    {
                        spots[episodeId] = (dialogue, rect);
                    }
                    else
                    {
                        freeNodes.Add(dialogue);
                    }
                }
            }

            // 도착 포트 (2026-08-15 소유자) — 여러 선택지가 같은 에피소드로 이어지는 일이
            // 많으므로, 들어오는 가지들은 도착 카드 앞의 접점 하나로 모인다.
            var arrivals = new Dictionary<string, List<(string From, string Label)>>(StringComparer.Ordinal);

            foreach ((string episodeId, (DialogueNode node, Rect rect)) in spots)
            {
                // 챕터에서 지운 에피소드의 노드가 판에 남아 있으면 레일을 긋지 않는다 —
                // 챕터 밖의 노드에 진행·종료를 그리는 것은 거짓말이다(동기화 보고의 몫).
                if (chapter.Episodes.All(episode =>
                        !string.Equals(episode.EpisodeId, episodeId, StringComparison.Ordinal)))
                {
                    continue;
                }

                List<ChapterEdge> edges = chapter.Edges
                    .Where(edge => string.Equals(edge.FromEpisodeId, episodeId, StringComparison.Ordinal))
                    .ToList();

                DrawRailsFrom(node, rect, edges, spots, nodeRects, freeNodes, arrivals);
            }

            foreach ((string toId, List<(string From, string Label)> incoming) in arrivals)
            {
                DrawArrivalPort(spots[toId].Rect, toId, incoming);
            }
        }

        // 프레임 뒤·카드 앞 — 프레임들 바로 다음 인덱스에 끼운다.
        for (int index = 0; index < _railVisuals.Count; index++)
        {
            GraphCanvas.Children.Insert(_frames.Count + index, _railVisuals[index]);
        }
    }

    /// <summary>
    /// 한 출발 카드의 줄기와 가지들 (T2, v9로 개정 2026-08-17).
    ///
    /// <b>가지의 주인은 챕터 `간선` 시트다</b> — 가지 하나 = 간선 하나이고 순서도 시트의 행
    /// 순서다(그것이 화면에 뜨는 순서다). 예전에는 대본의 OPTION 줄이 가지를 만들고 간선이
    /// 문구로 짝을 찾았는데, v9에서 대본에 OPTION이 없는 것이 정상이 되면서 모든 간선이
    /// "유령"으로 보였다(소유자 보고). 이제 그 개념 자체가 없다.
    ///
    /// 칩이 배선 진입점이다. 자유 씬은 <b>선택지 문구를 열쇠로</b> 매단다
    /// (<see cref="ExitPortKind.Choice"/>) — 대본의 줄에 매이지 않으므로 대본을 고쳐도
    /// 배선이 살아 있다. 구판 대본에 OPTION 줄이 남아 있고 문구가 같으면 <b>그 줄의 포트를
    /// 그대로 쓴다</b>(옛 배선을 잃지 않는다).
    ///
    /// 배선된 자유 씬이 있으면 가지는 <b>첫 씬의 입구까지만</b> 댄다 — 웹 속은 작가의 실행
    /// 배선이 유일한 선이고, 끝은 (진행) 합류선이 레인 끝으로 모은다.
    /// </summary>
    private void DrawRailsFrom(
        DialogueNode source,
        Rect sourceRect,
        IReadOnlyList<ChapterEdge> edges,
        IReadOnlyDictionary<string, (DialogueNode Node, Rect Rect)> spots,
        IReadOnlyDictionary<string, Rect> nodeRects,
        IReadOnlyList<DialogueNode> freeNodes,
        Dictionary<string, List<(string From, string Label)>> arrivals)
    {
        IReadOnlyList<ExitPort> ports = NodeConnections.PortsOf(source, _session!.Project, _session.Definition);
        List<ExitPort> optionPorts = ports.Where(port => port.IsChoice).ToList();
        ExitPort? defaultPort = ports.FirstOrDefault(port => port.Kind == ExitPortKind.Default);

        var branches = new List<(ChapterEdge? Edge, ExitPort? Port, Rect? Target)>();

        // 가지 순서 = <b>읽는 순서</b>: ① 문구 있는 길들(간선 시트 행 순서 그대로 — 그것이
        // 화면에 뜨는 순서다) ② 대본에만 남은 구판 OPTION 스텁 ③ 문구 없는 진행.
        // 진행이 맨 아래인 것은 소유자 보고("진행이 선택지 위에 서니 헷갈린다") 때문이고,
        // 보이지 않는 기본은 플레이어에게 버튼으로 안 뜨므로 선택지 순서를 왜곡하지도 않는다.
        void AddEdgeBranch(ChapterEdge edge)
        {
            // 도착 노드가 아직 동기화 전이어도 가지는 보인다 — 숨기면 선택지가 통째로
            // 사라져 그래프가 고장 난 것처럼 보인다(소유자 보고 2026-08-15). 없는 노드로
            // 선을 긋는 대신 "동기화 전" 표식에서 멈춘다.
            Rect? target = spots.TryGetValue(edge.ToEpisodeId, out (DialogueNode Node, Rect Rect) spot)
                ? spot.Rect
                : null;

            branches.Add((edge, PortFor(source, edge, optionPorts, defaultPort), target));
        }

        foreach (ChapterEdge edge in edges.Where(edge => !edge.IsPlainAdvance))
        {
            AddEdgeBranch(edge);
        }

        // 대본에만 남은 OPTION 줄(구판) — 짝할 간선이 없으니 그 길은 여기서 끝난다(Gate B).
        // 배선은 살아 있으므로 스텁으로 세워 둔다: 지우려면 대본에서 그 줄을 뺀다.
        foreach (ExitPort orphan in optionPorts.Where(port =>
                     !edges.Any(edge =>
                         string.Equals(edge.OptionLabel, port.ChoiceText, StringComparison.Ordinal))))
        {
            branches.Add((null, orphan, null));
        }

        foreach (ChapterEdge edge in edges.Where(edge => edge.IsPlainAdvance))
        {
            AddEdgeBranch(edge);
        }

        // 나가는 간선이 하나도 없는 에피소드(엔딩) — 기본 출구의 종료 스텁 (에필로그 자유 씬 자리).
        if (edges.Count == 0 && optionPorts.Count == 0 && defaultPort is not null)
        {
            branches.Add((null, defaultPort, null));
        }

        if (branches.Count == 0)
        {
            return;
        }

        double trunkX = sourceRect.X + RailTrunkInset;
        double branchY = sourceRect.Bottom + RailFirstDrop;

        foreach ((ChapterEdge? edge, ExitPort? port, Rect? target) in branches)
        {
            DrawRailBranch(source, edge, port, trunkX, branchY, target, nodeRects, freeNodes);

            if (edge is not null && target is not null)
            {
                // 도착 포트 장부 — 어느 에피소드의 어느 선택지가 여기로 들어오는가.
                if (!arrivals.TryGetValue(edge.ToEpisodeId, out List<(string From, string Label)>? list))
                {
                    arrivals[edge.ToEpisodeId] = list = new List<(string, string)>();
                }

                list.Add((source.ExcelEpisodeId ?? source.Name,
                    edge.IsPlainAdvance ? "(진행)" : edge.OptionLabel!));
            }

            branchY += RailBranchGap;
        }

        _railVisuals.Add(RailLine(trunkX, sourceRect.Bottom, trunkX, branchY - RailBranchGap));
    }

    /// <summary>
    /// 도착 포트 (2026-08-15 소유자) — 들어오는 가지들이 도착 카드 앞의 접점 하나로 모인다.
    /// 누르면 어느 에피소드의 어느 선택지들이 여기로 이어지는지 목록이 열린다(읽기 전용 —
    /// 잇고 끊는 곳은 출발 쪽 칩과 챕터 그래프다).
    /// </summary>
    private void DrawArrivalPort(Rect target, string episodeId, IReadOnlyList<(string From, string Label)> incoming)
    {
        double junctionX = target.X - 26;
        double junctionY = target.Y + target.Height / 2;

        _railVisuals.Add(RailLine(junctionX, junctionY, target.X - 10, junctionY));
        _railVisuals.Add(RailArrow(target.X - 9, junctionY, pointRight: true));

        var port = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = RailChipBrush,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(port, $"들어오는 길 {incoming.Count}개 — 누르면 목록이 열립니다.");
        Canvas.SetLeft(port, junctionX - 5);
        Canvas.SetTop(port, junctionY - 5);

        string captured = episodeId;
        var capturedIncoming = incoming.ToList();
        port.PointerPressed += (_, args) =>
        {
            args.Handled = true;

            var panel = new StackPanel { Spacing = 3, MinWidth = 200 };
            panel.Children.Add(new TextBlock
            {
                Text = $"{captured}로 들어오는 길",
                FontSize = 11,
                FontWeight = FontWeight.SemiBold
            });

            foreach ((string from, string label) in capturedIncoming)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"{from} · {label}",
                    FontSize = 11,
                    Opacity = 0.85
                });
            }

            panel.Children.Add(new TextBlock
            {
                Text = "잇고 끊는 곳은 출발 쪽 칩과 챕터 그래프입니다.",
                FontSize = 10,
                Opacity = 0.5,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 240
            });

            new Flyout { Content = panel }.ShowAt(port);
        };

        _railVisuals.Add(port);
    }

    /// <summary>
    /// 이 간선의 자유 씬이 매달릴 자리 (v9). 문구가 같은 구판 OPTION 줄이 대본에 있으면
    /// 그 줄의 포트를 그대로 쓰고(옛 배선 보존), 없으면 <b>문구를 열쇠로 한 선택지 포트</b>를
    /// 세운다. 문구 없는 길(보이지 않는 기본)은 노드의 기본 출구가 그 자리다.
    /// </summary>
    private ExitPort? PortFor(
        DialogueNode source,
        ChapterEdge edge,
        IReadOnlyList<ExitPort> optionPorts,
        ExitPort? defaultPort)
    {
        if (edge.IsPlainAdvance)
        {
            return defaultPort;
        }

        ExitPort? legacy = optionPorts.FirstOrDefault(port =>
            string.Equals(port.ChoiceText, edge.OptionLabel, StringComparison.Ordinal));

        if (legacy is not null)
        {
            return legacy;
        }

        return new ExitPort(
            ExitPortKind.Choice,
            source.Id,
            BranchOpenLineId: null,
            Label: edge.OptionLabel!,
            TargetNodeId: source.ChoiceExits.GetValueOrDefault(edge.OptionLabel!),
            PaletteIndex: -1,
            IsChoice: true,
            ChoiceText: edge.OptionLabel);
    }

    private void DrawRailBranch(
        DialogueNode source,
        ChapterEdge? edge,
        ExitPort? port,
        double trunkX,
        double y,
        Rect? target,
        IReadOnlyDictionary<string, Rect> nodeRects,
        IReadOnlyList<DialogueNode> freeNodes)
    {
        double cursorX = trunkX;

        string chipText = edge is { IsPlainAdvance: true } || (edge is null && port?.Kind == ExitPortKind.Default)
            ? "○ 진행"
            : $"● {edge?.OptionLabel ?? port?.ChoiceText}";

        var chip = new TextBlock
        {
            Text = chipText,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = RailChipBrush,
            Opacity = chipText == "○ 진행" ? 0.75 : 1,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(chip, "누르면 상세와 자유 씬 배선이 열립니다. 값 편집은 챕터 그래프에서.");

        chip.Measure(Size.Infinity);

        _railVisuals.Add(RailLine(trunkX, y, trunkX + 10, y));
        Canvas.SetLeft(chip, trunkX + 14);
        Canvas.SetTop(chip, y - chip.DesiredSize.Height / 2);

        DialogueNode capturedSource = source;
        ChapterEdge? capturedEdge = edge;
        ExitPort? capturedPort = port;
        chip.PointerPressed += (_, args) =>
        {
            args.Handled = true;
            ShowRailChip(chip, capturedSource, capturedEdge, capturedPort, freeNodes);
        };

        _railVisuals.Add(chip);
        cursorX = trunkX + 14 + chip.DesiredSize.Width + 6;

        // 배선된 자유 씬이 있으면 레일은 <b>첫 씬의 입구까지만</b> 댄다 (소유자 보고
        // 2026-08-15 — 레일이 체인을 관통해 그리니 작가의 실행 배선과 평행으로 겹쳐
        // 가독성이 나빴다). 웹 속의 유일한 선은 작가의 배선이고, 웹의 끝(기본 출구 없음
        // = 진행)은 합류선이 레인 끝으로 모은다.
        List<Rect> chain = port is null ? new List<Rect>() : ChainRects(port, nodeRects);
        bool web = chain.Count > 0;

        if (web)
        {
            (cursorX, y) = RouteInto(cursorX, y, chain[0]);

            // 스텁이 서야 한다면 웹 전체의 오른쪽 밖이다 — 합류선이 웹 속으로 되돌아
            // 들어오는 모양을 만들지 않는다.
            cursorX = Math.Max(cursorX, WebRight(port!, nodeRects, cursorX) + 12);
        }

        Point laneEnd;

        if (target is { } targetRect)
        {
            // 도착 포트로 모인다 (소유자 보고 — 접점만 있고 가지는 직접 붙던 결함).
            // 화살표는 포트의 몫이다 — 가지들은 접점까지만 간다.
            double junctionX = targetRect.X - 26;
            double junctionY = targetRect.Y + targetRect.Height / 2;

            if (!web)
            {
                _railVisuals.Add(RailLine(cursorX, y, junctionX, y));

                if (Math.Abs(junctionY - y) > 0.5)
                {
                    _railVisuals.Add(RailLine(junctionX, y, junctionX, junctionY));
                }
            }

            laneEnd = new Point(junctionX, junctionY);
        }
        else if (edge is not null)
        {
            // 동기화 전 스텁 — 간선은 있는데 도착 에피소드가 아직 이 판에 없다. 없는 노드로
            // 선을 긋는 대신 여기서 멈추고, 어디로 가는 길인지는 문구가 말한다.
            var pending = new TextBlock
            {
                Text = $"▢ {edge.ToEpisodeId} (동기화 전)",
                FontSize = 10,
                Opacity = 0.55,
                Foreground = RailChipBrush
            };
            ToolTip.SetTip(pending,
                $"도착 에피소드 '{edge.ToEpisodeId}'가 아직 이 판에 없습니다 — " +
                "챕터 그래프의 동기화로 노드를 만들면 여기로 이어집니다.");
            pending.Measure(Size.Infinity);

            _railVisuals.Add(RailLine(cursorX, y, cursorX + 18, y));
            Canvas.SetLeft(pending, cursorX + 22);
            Canvas.SetTop(pending, y - pending.DesiredSize.Height / 2);
            _railVisuals.Add(pending);

            laneEnd = new Point(cursorX + 18, y);
        }
        else
        {
            // 종료 스텁 — 이 길은 여기서 에피소드가 끝난다. 다음은 챕터가 정한다(없으면 엔딩).
            var stop = new TextBlock
            {
                Text = "⏹ 종료",
                FontSize = 10,
                Opacity = 0.6,
                IsHitTestVisible = false
            };
            stop.Measure(Size.Infinity);

            _railVisuals.Add(RailLine(cursorX, y, cursorX + 18, y));
            Canvas.SetLeft(stop, cursorX + 22);
            Canvas.SetTop(stop, y - stop.DesiredSize.Height / 2);
            _railVisuals.Add(stop);

            laneEnd = new Point(cursorX + 18, y);
        }

        // (진행) 합류 (2026-08-15 소유자) — 척추(기본 출구 체인) 밖으로 확장한 커스텀 웹
        // (선택지·조건 갈래로 이어진 자유 노드들)에서, 기본 출구가 빈 노드 = (진행)이다.
        // 그 출력들을 이 레인의 끝(도착 접점 또는 ⏹)으로 모아 그린다 — "어디로 돌아가는지"가
        // 눈에 보이고, 잇는 제스처는 출구를 비우는 것 하나다.
        if (port is not null)
        {
            DrawAdvanceReturns(port, laneEnd, nodeRects);
        }
    }

    /// <summary>
    /// 옵션 배선에서 닿는 자유 웹 전체(기본 출구 + 갈래 출구)를 걷고, (진행) 노드
    /// (기본 출구 없음)마다 레인 끝으로 합류선을 긋는다. 레일은 웹 속을 다시 긋지
    /// 않으므로(입구만 댄다) 척추 끝도 여기서 레인 끝에 닿는다.
    /// </summary>
    private void DrawAdvanceReturns(ExitPort port, Point laneEnd, IReadOnlyDictionary<string, Rect> nodeRects)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();

        if (port.TargetNodeId is { } first)
        {
            queue.Enqueue(first);
        }

        while (queue.Count > 0)
        {
            string id = queue.Dequeue();

            if (!visited.Add(id) ||
                _session!.Project.FindNode(id) is not DialogueNode { ExcelEpisodeId: null } node)
            {
                continue;
            }

            foreach (string next in node.BranchExits.Values)
            {
                queue.Enqueue(next);
            }

            if (node.DefaultExitTargetNodeId is { } defaultNext)
            {
                queue.Enqueue(defaultNext);
            }
            else if (nodeRects.TryGetValue(id, out Rect rect))
            {
                // (진행) — 오른쪽에서 나와 레인 끝으로 직교 합류.
                double outX = rect.Right + 6;
                double outY = rect.Y + rect.Height / 2;

                _railVisuals.Add(RailLine(outX, outY, laneEnd.X, outY));

                if (Math.Abs(laneEnd.Y - outY) > 0.5)
                {
                    _railVisuals.Add(RailLine(laneEnd.X, outY, laneEnd.X, laneEnd.Y));
                }
            }
        }
    }

    /// <summary>옵션 배선에서 닿는 자유 웹의 오른쪽 끝 X — 스텁을 웹 밖에 세우기 위한 값.</summary>
    private double WebRight(ExitPort port, IReadOnlyDictionary<string, Rect> nodeRects, double fallback)
    {
        double right = fallback;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();

        if (port.TargetNodeId is { } first)
        {
            queue.Enqueue(first);
        }

        while (queue.Count > 0)
        {
            string id = queue.Dequeue();

            if (!visited.Add(id) ||
                _session!.Project.FindNode(id) is not DialogueNode { ExcelEpisodeId: null } node)
            {
                continue;
            }

            if (nodeRects.TryGetValue(id, out Rect rect))
            {
                right = Math.Max(right, rect.Right);
            }

            foreach (string next in node.BranchExits.Values)
            {
                queue.Enqueue(next);
            }

            if (node.DefaultExitTargetNodeId is { } defaultNext)
            {
                queue.Enqueue(defaultNext);
            }
        }

        return right;
    }

    /// <summary>배선된 자유 씬 체인 — 첫 배선에서 출발해 기본 출구를 따라간다. 자유 노드만 잇는다.</summary>
    private List<Rect> ChainRects(ExitPort port, IReadOnlyDictionary<string, Rect> nodeRects)
    {
        var rects = new List<Rect>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? currentId = port.TargetNodeId;

        while (currentId is not null &&
               seen.Add(currentId) &&
               nodeRects.TryGetValue(currentId, out Rect rect) &&
               _session!.Project.FindNode(currentId) is DialogueNode { ExcelEpisodeId: null } free)
        {
            rects.Add(rect);
            currentId = free.DefaultExitTargetNodeId;
        }

        return rects;
    }

    /// <summary>클립(자유 씬) 카드로 들어갔다 오른쪽으로 나온다. 반환 = 다음 구간의 시작점.</summary>
    private (double X, double Y) RouteInto(double x, double y, Rect rect)
    {
        if (y >= rect.Y && y <= rect.Bottom)
        {
            _railVisuals.Add(RailLine(x, y, rect.X - 8, y));
            _railVisuals.Add(RailArrow(rect.X - 7, y, pointRight: true));
        }
        else
        {
            double midX = rect.X + rect.Width / 2;
            _railVisuals.Add(RailLine(x, y, midX, y));

            bool above = rect.Bottom < y;
            double endY = above ? rect.Bottom + 8 : rect.Y - 8;
            _railVisuals.Add(RailLine(midX, y, midX, endY));
            _railVisuals.Add(RailArrow(midX, above ? endY - 1 : endY + 1, pointRight: false, pointUp: above));
        }

        return (rect.Right + 6, rect.Y + rect.Height / 2);
    }

    /// <summary>
    /// 칩 클릭 — 상세(읽기 전용) + 자유 씬 배선 (T2). 배선의 데이터는 기존 그대로다:
    /// 옵션 칩 = <c>SetExitTarget(Branch, 옵션 줄)</c>, 진행 칩 = <c>SetExitTarget(Default)</c>.
    /// 간선 값(문구·조건·스탯변화)은 여기서 못 고친다 — 챕터 소유(T-R1).
    /// </summary>
    private void ShowRailChip(
        Control anchor,
        DialogueNode source,
        ChapterEdge? edge,
        ExitPort? port,
        IReadOnlyList<DialogueNode> freeNodes)
    {
        var panel = new StackPanel { Spacing = 3, MinWidth = 230 };

        void Row(string text, bool bold = false, double opacity = 1) => panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            Opacity = opacity,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 260
        });

        Row(edge?.OptionLabel ?? port?.ChoiceText ?? "(진행)", bold: true);

        if (edge is not null)
        {
            Row($"{edge.FromEpisodeId} → {edge.ToEpisodeId}", opacity: 0.7);

            if (!string.IsNullOrWhiteSpace(edge.ConditionLabel))
            {
                Row($"조건: {edge.ConditionLabel}", opacity: 0.85);
            }

            if (edge.StatChanges.Count > 0)
            {
                Row("스탯변화: " + string.Join("; ", edge.StatChanges
                    .Select(delta => $"{delta.Key} {(delta.Amount >= 0 ? "+" : "")}{delta.Amount}")), opacity: 0.85);
            }

            if (edge.HideWhenLocked)
            {
                Row("잠기면 숨김", opacity: 0.7);
            }

            if (!string.IsNullOrWhiteSpace(edge.LockedMessage))
            {
                Row($"잠금 안내: {edge.LockedMessage}", opacity: 0.7);
            }
        }
        else
        {
            // 대본에만 남은 구판 OPTION 줄 — 짝할 간선이 없다.
            Row("챕터에 이 선택지가 없습니다 — 대본에만 남은 옛 OPTION 줄입니다. " +
                "쓰려면 챕터 그래프에서 이 문구로 길을 내고, 아니면 대본에서 그 줄을 빼세요.",
                opacity: 0.6);
        }

        if (port is null)
        {
            new Flyout { Content = panel }.ShowAt(anchor);
            return;
        }

        // ── 자유 씬 배선 ──
        Row("이 길 위의 자유 씬", bold: true, opacity: 0.8);

        List<DialogueNode> candidates = freeNodes
            .Where(node => !string.Equals(node.Id, port.TargetNodeId, StringComparison.Ordinal))
            .ToList();

        var combo = new ComboBox
        {
            ItemsSource = candidates.Select(node => node.Name).ToList(),
            PlaceholderText = candidates.Count == 0 ? "판에 자유 노드가 없습니다" : "자유 씬 달기…",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = candidates.Count > 0
        };

        string sourceId = source.Id;
        ExitPortKind kind = port.Kind;
        // 선택지 포트(v9)의 열쇠는 문구, 갈래 포트는 여는 줄의 LineId다.
        string? exitKey = port.ExitKey;

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && combo.SelectedIndex < candidates.Count)
            {
                _session!.Editor.SetExitTarget(
                    sourceId, kind, exitKey, candidates[combo.SelectedIndex].Id);
            }
        };

        panel.Children.Add(combo);

        if (port.TargetNodeId is { } wired)
        {
            string wiredName = _session!.Project.FindNode(wired)?.Name ?? wired;
            var detach = new Button
            {
                Content = $"'{wiredName}' 떼기",
                FontSize = 10,
                Padding = new Thickness(8, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            detach.Click += (_, _) =>
                _session!.Editor.SetExitTarget(sourceId, kind, exitKey, null);
            panel.Children.Add(detach);
        }

        Row("씬이 끝나면(출구 없음) 에피소드가 끝납니다 — 간선이 있으면 그 길로, 없으면 챕터 진행 종료.", opacity: 0.5);

        if (port.Kind == ExitPortKind.Choice)
        {
            // 정직하게 — 배선은 저장되지만 내보내기가 아직 이 점프를 싣지 못한다.
            // 런타임 계약(Gate D)이 v9 전이 규칙과 함께 열려 있는 항목이다.
            Row("⚠ 이 배선은 저장되지만 아직 내보내기에 실리지 않습니다 — v9 전이 규칙과 함께 " +
                "런타임 계약(Gate D) 개정을 기다리는 중입니다.", opacity: 0.55);
        }

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
                : new SolidColorBrush(card.Style.Border);

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

    /// <summary>노드 종 하나의 시각 언어 — 등뼈(왼쪽 색 막대)·바탕·테두리·모서리·아이콘.</summary>
    private readonly record struct CardStyle(
        Color Accent, Color Background, Color Border, double Radius, string Icon);

    /// <summary>
    /// 종별 시각 체계 (2026-08-15 소유자 — "대본노드와 엑셀노드가 똑같이 생겨서 구분이 어렵다").
    /// 색 하나에 기대지 않고 세 채널을 겹친다: 등뼈 색 + 아이콘 + 카드 형태.
    /// 엑셀노드만 <b>각진 미색 서류</b>다(기획의 공식 문서, 본문 잠김) — 작가의 자유 씬은
    /// 둥근 흰 원고(✎)라서 섞여 있어도 한눈에 갈린다. 미니맵·접힌 목록도 같은 언어를 쓴다.
    /// </summary>
    private static CardStyle CardStyleFor(GraphNodeKind kind, bool excelOwned) => kind switch
    {
        GraphNodeKind.Dialogue when excelOwned => new CardStyle(
            Color.FromRgb(0xD9, 0x77, 0x06), Color.FromRgb(0xFB, 0xF6, 0xEA),
            Color.FromRgb(0xE3, 0xD5, 0xB7), 4, "📄"),
        GraphNodeKind.Dialogue => new CardStyle(
            Color.FromRgb(0x0D, 0x94, 0x88), Colors.White,
            Color.FromArgb(90, 128, 128, 128), 10, "✎"),
        GraphNodeKind.Set => new CardStyle(
            Color.FromRgb(0x3B, 0x82, 0xF6), Color.FromRgb(0xEF, 0xF6, 0xFF),
            Color.FromArgb(90, 128, 128, 128), 10, "⚙"),
        GraphNodeKind.Presentation => new CardStyle(
            Color.FromRgb(0x8B, 0x5C, 0xF6), Color.FromRgb(0xFA, 0xF5, 0xFF),
            Color.FromArgb(90, 128, 128, 128), 10, "🎬"),
        _ => new CardStyle(
            Color.FromRgb(0x22, 0xC5, 0x5E), Color.FromRgb(0xF0, 0xFD, 0xF4),
            Color.FromArgb(90, 128, 128, 128), 10, "🧰")
    };

    private bool IsExcelOwned(string nodeId) =>
        _session?.Project.FindNode(nodeId) is DialogueNode { ExcelEpisodeId: not null };

    private NodeCard BuildCard(ExpandedNodeProjection node)
    {
        CardStyle style = CardStyleFor(node.NodeKind, IsExcelOwned(node.NodeId));

        var body = new StackPanel { Spacing = 0 };

        var titleIcon = new TextBlock
        {
            Text = style.Icon,
            FontSize = 11,
            Foreground = new SolidColorBrush(style.Accent),
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var titleName = new TextBlock
        {
            Text = node.NodeName,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        var title = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(titleIcon, 0);
        Grid.SetColumn(titleName, 1);
        title.Children.Add(titleIcon);
        title.Children.Add(titleName);
        body.Children.Add(title);

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

        // 등뼈 — 카드 왼쪽의 종 색 막대. 선택 테두리(파랑 2px)와 채널이 겹치지 않는다.
        var spine = new Border
        {
            Width = 3,
            Background = new SolidColorBrush(style.Accent),
            CornerRadius = new CornerRadius(1.5),
            Margin = new Thickness(0, 1, 7, 1),
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(spine, 0);
        Grid.SetColumn(body, 1);
        layout.Children.Add(spine);
        layout.Children.Add(body);

        var card = new Border
        {
            Width = CardWidth,
            Padding = new Thickness(CardPadding),
            CornerRadius = new CornerRadius(style.Radius),
            Background = new SolidColorBrush(style.Background),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(style.Border),
            Child = layout,
            Tag = node.NodeId
        };

        var visual = new NodeCard(node.NodeId, node.NodeKind, card, node.OutputPorts, style);

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
            // 접힌 목록에도 종 아이콘 — 카드와 같은 시각 언어.
            Text = $"{CardStyleFor(entry.NodeKind, IsExcelOwned(entry.NodeId)).Icon} {entry.NodeName}",
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

    /// <summary>휠 — 누른 키와 무관하게 배율이다 (2026-08-18 팀장 미팅에서 Ctrl 요구가 빠졌다).</summary>
    private void OnGraphWheel(object? sender, PointerWheelEventArgs args)
    {
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
                card.Style.Accent); // 카드와 같은 시각 언어 — 미니맵에서도 엑셀·자유가 갈린다.
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
        if (_session?.SelectedNodeId is not { } nodeId)
        {
            return;
        }

        // 설정 노드는 챕터에 딸린 자리다 (2026-08-17) — 지우면 그 챕터의 조건·아이템·화자가
        // 통째로 사라지고, 어차피 다음에 판을 열 때 빈 채로 다시 선다.
        if (_session.Project.FindNode(nodeId) is SetNode)
        {
            _session.SetStatus(
                "설정 노드는 챕터마다 하나씩 있는 자리라 지우지 않습니다 — " +
                "안의 조건·아이템·능력은 노드를 열어 하나씩 지울 수 있습니다.");
            return;
        }

        _session.Editor.RemoveNode(nodeId);
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
            IReadOnlyList<GraphOutputPortProjection> ports,
            CardStyle style)
        {
            NodeId = nodeId;
            NodeKind = nodeKind;
            Visual = visual;
            Ports = ports;
            Style = style;
        }

        public string NodeId { get; }
        public GraphNodeKind NodeKind { get; }
        public Border Visual { get; }
        public IReadOnlyList<GraphOutputPortProjection> Ports { get; }
        public CardStyle Style { get; }

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
