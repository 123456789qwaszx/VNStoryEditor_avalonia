using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
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

    /// <summary>
    /// 카드를 <b>더블클릭</b>했다 — "이 노드를 무대에서 본다" (2026-08-25 소유자: "특정
    /// 노드를 더블 클릭했을 때, 무대프리뷰로 연결되도록. 더블클릭한 노드가 선택된채로").
    ///
    /// ⚠ 여기서 탭을 바꾸지 않는다. 이 판은 탭이 있다는 것도, 무대 프리뷰가 있다는 것도
    /// 모른다 — 두 화면은 서로를 모르고 <see cref="AuthoringSession"/> 하나만 본다(G-1).
    /// 무엇을 열지는 셸이 정한다.
    /// </summary>
    internal event Action<string>? NodeActivated;

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
        RefreshChapterCombo();

        if (_projection is not null)
        {
            DrawChapterRails();
        }
    }

    /// <summary>선택을 비추는 동안에는 <c>SelectionChanged</c>가 선택을 바꾸지 않게 한다.</summary>
    private bool _syncingChapter;

    /// <summary>
    /// 챕터 목록과 지금 활성인 판을 콤보에 비춘다 (2026-08-25).
    ///
    /// 목록의 원천은 <b>챕터 그래프가 읽은 하나</b>다(<see cref="SupplyChapters"/>) —
    /// 여기서 따로 읽으면 감시·재시도 규칙이 두 벌이 된다. 선택도 마찬가지로
    /// <c>ActiveFileId</c> 하나를 비출 뿐이라, 챕터 그래프에서 고른 것이 여기에도 보인다.
    /// </summary>
    private void RefreshChapterCombo()
    {
        _syncingChapter = true;

        try
        {
            string[] ids = _chapters.Select(entry => entry.ChapterId).ToArray();

            if (!ids.SequenceEqual(
                    (ChapterCombo.ItemsSource as IEnumerable<string>) ?? [], StringComparer.Ordinal))
            {
                ChapterCombo.ItemsSource = ids;
            }

            // 판 이름 = ChapterId (챕터=판 1:1) — 활성 판이 곧 고른 챕터다.
            ChapterCombo.SelectedItem = _session?.ActiveFile?.Name is { } name && ids.Contains(name)
                ? name
                : null;
        }
        finally
        {
            _syncingChapter = false;
        }
    }

    private AuthoringSession? _session;
    private GraphProjection? _projection;

    private NodeCard? _draggingCard;
    private Point _dragOffset;

    private GraphOutputPortProjection? _connectingFrom;
    private Line? _connectingLine;

    private EdgeVisual? _selectedEdge;

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

    /// <summary>
    /// 토글 상태로 만든 필터. 거르는 것은 화면이 아니라 투영이다.
    /// 연출·연출 공급 노드는 필터 이전에 배관으로 숨는다(2026-08-21) — 토글이 없다.
    /// </summary>
    private GraphFilter CurrentFilter => new(
        ShowDialogue: FilterDialogueCheck.IsChecked == true,
        ShowSet: FilterSetCheck.IsChecked == true);

    /// <summary>
    /// 우측 곁기둥의 내용을 셸이 물려준다 (2026-08-22) — 노드 편집기 묶음 + 에셋 탐색기.
    /// 자리는 이 판의 것이고(쓰는 화면이 여기뿐이다) 배선은 셸의 것이다: 편집기들이
    /// 세션·무대 프리뷰·발행과 얽혀 있어 이 뷰로 옮기면 그 배선이 두 벌이 된다.
    /// </summary>
    internal void SetSidePanel(Control content) => SideHost.Content = content;

    public GraphEditorView()
    {
        InitializeComponent();

        // 판 크기는 상수 한 곳이 정한다 (W41) — 미니맵 배율과 어긋날 길을 없앤다.
        GraphCanvas.Width = CanvasWidth;
        GraphCanvas.Height = CanvasHeight;

        AddDialogueButton.Click += (_, _) => AddNode(GraphNodeKind.Dialogue);
        DeleteNodeButton.Click += (_, _) => DeleteSelectedNode();

        // 고른 챕터의 판을 활성으로 — 그 뒤의 [+ 대사 노드]가 거기에 선다.
        // ⚠ 다시 그리기가 콤보를 맞출 때는 안 돈다(_syncingChapter) — 안 그러면
        //    선택을 비추는 일이 선택을 바꾸는 일이 되어 서로를 부른다.
        ChapterCombo.SelectionChanged += (_, _) =>
        {
            if (_syncingChapter || _session is null ||
                ChapterCombo.SelectedItem is not string chapterId)
            {
                return;
            }

            UiGuard.Run(_session, "챕터 고르기", () =>
                _session.SelectFile(_session.EnsureChapterBoard(chapterId)));
        };

        foreach (CheckBox check in new[] { FilterDialogueCheck, FilterSetCheck })
        {
            // 체크 하나가 곧 다시 그리기다 — 코드가 체크를 대신 눌러 주던 [흐름만]이
            // 사라지며(2026-08-22) 재진입을 막던 빗장도 함께 없어졌다.
            check.IsCheckedChanged += (_, _) => Rebuild();
        }

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

        // 활성 판이 바뀌면 콤보도 따라간다 — 챕터 그래프에서 고른 것이 여기에도 보인다.
        RefreshChapterCombo();

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
        SlotLabelBox = null;

        // 고른 간선이 사라졌으면 그 간선을 설명하던 줄도 사라져야 한다 — 안 그러면
        // 방금 지운 간선의 이름이 띠에 남는다(늘 서 있던 설명이 그 자리를 덮고 있어서
        // 여태 안 보였다).
        SetHint(null);

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

            // 챕터 안에 장면을 한 겹 더 (R6 S-3b) — 챕터 프레임 <b>뒤에</b> 더해야 그 위에
            // 깔린다(아래 Insert가 목록 순서대로 넣는다).
            DrawSceneFrames(group.Key, group);
        }

        // 카드·간선보다 뒤에 깔리도록 맨 앞 인덱스에 순서대로 끼운다.
        for (int index = 0; index < _frames.Count; index++)
        {
            GraphCanvas.Children.Insert(index, _frames[index]);
        }

        DrawChapterRails();
    }

    /// <summary>
    /// <b>장면 프레임</b> — 챕터 프레임 안에 한 겹 더 (R6 S-3b · <c>docs/plans/R6-explorer.md</c> §5).
    ///
    /// 같은 장면의 카드가 한 영역으로 보여야 작가가 <b>어디까지가 한 수명</b>인지 안다 —
    /// 장면 안에서는 모든 게 물릴 수 있고 장면이 끝나면 확정되기 때문이다.
    ///
    /// ⚠ <b>안 그리는 경우 셋</b>: 챕터가 아닌 판 · 장면ID를 하나도 안 적은 챕터 ·
    /// 장면이 하나뿐인 챕터. 앞의 둘은 접힌 판과 <b>같은 규칙</b>이고(규격 §2), 셋째는
    /// 챕터 프레임과 똑같은 자리에 겹쳐 그려 봐야 테두리만 두 겹이 되기 때문이다.
    ///
    /// ⚠ 히트 대상이 아니다 — 클릭·드래그·포트에 아무 영향이 없다(챕터 프레임과 같다).
    /// </summary>
    private void DrawSceneFrames(string fileId, IEnumerable<ExpandedNodeProjection> nodes)
    {
        if (_session?.Project.Files.FirstOrDefault(file =>
                string.Equals(file.Id, fileId, StringComparison.Ordinal)) is not { } board ||
            _session.Editor.FindChapter(board.Name) is not { } chapter)
        {
            return;
        }

        IReadOnlyList<ChapterScene> scenes = ChapterSceneGrouping.Of(
            chapter.ToGraphModel(chapter.ChapterId));

        if (scenes.Count < 2 || scenes.All(scene => scene.IsDefault))
        {
            return;
        }

        var sceneOf = new Dictionary<string, ChapterScene>(StringComparer.Ordinal);

        foreach (ChapterScene scene in scenes)
        {
            foreach (ChapterEpisode episode in scene.Episodes)
            {
                sceneOf[episode.EpisodeId] = scene;
            }
        }

        List<ExpandedNodeProjection> all = nodes.ToList();

        foreach (ChapterScene scene in scenes)
        {
            Rect? bounds = null;

            foreach (ExpandedNodeProjection node in all)
            {
                if (EpisodeOf(node) is not { } episodeId ||
                    !sceneOf.TryGetValue(episodeId, out ChapterScene? owner) ||
                    !ReferenceEquals(owner, scene) ||
                    FindCard(node.NodeId) is not { } card)
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

            // 챕터 프레임(24·46)보다 얕게 — 두 테두리가 붙어 보이지 않게 한다.
            area = area.Inflate(new Thickness(10, 26, 10, 10));

            _frames.Add(SceneFrame(area));
            _frames.Add(SceneLabel(area, scene));
        }
    }

    /// <summary>그 노드가 대신하는 에피소드 — 표식이 먼저고, 없으면 이름이다(대본 탭과 같은 규칙).</summary>
    private string? EpisodeOf(ExpandedNodeProjection node) =>
        _session?.Project.FindNode(node.NodeId) is DialogueNode dialogue
            ? dialogue.ExcelEpisodeId is { Length: > 0 } marked ? marked : dialogue.Name
            : null;

    private static Border SceneFrame(Rect area)
    {
        var frame = new Border
        {
            Width = area.Width,
            Height = area.Height,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 217, 119, 6)),
            Background = new SolidColorBrush(Color.FromArgb(10, 217, 119, 6)),
            IsHitTestVisible = false
        };

        Canvas.SetLeft(frame, area.X);
        Canvas.SetTop(frame, area.Y);

        return frame;
    }

    private static Border SceneLabel(Rect area, ChapterScene scene)
    {
        var label = new Border
        {
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 1),
            Background = new SolidColorBrush(Color.FromArgb(180, 217, 119, 6)),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                // ⚠ 장면이 확정·롤백의 경계라는 것이 이름표에서 읽혀야 한다.
                Text = scene.HasSplitEntry
                    ? $"장면 {scene.DisplayName} ⚠"
                    : $"장면 {scene.DisplayName}",
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White
            }
        };

        Canvas.SetLeft(label, area.X + 10);
        Canvas.SetTop(label, area.Y + 6);

        return label;
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

        if (_session is null || _projection is null)
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

            // ⛔ <b>살아 있는 프로젝트를 먼저 본다</b> (R7 · 2026-09-16). 예전에는 셸이 공급한
            //    <c>_chapters</c> 스냅샷만 봤는데, 이제 이 화면이 <b>간선을 직접 긋는다</b> —
            //    스냅샷은 셸이 다시 읽어 줄 때까지 낡아 있어 <b>방금 그은 길이 안 비친다</b>.
            //
            // ⚠ 스냅샷은 <b>뒷받침</b>으로 남는다: 프로젝트에 그 챕터가 없고 공급만 받은
            //    상태(셸이 아직 들여오기 전)에서도 철도는 보여야 한다. 둘 다 같은 값을
            //    말하므로 정본이 둘이 되는 것은 아니다 — 먼저 보는 쪽이 정해져 있다.
            ChapterGraphModel? chapter =
                _session.Editor.FindChapter(boardName)?.ToGraphModel(boardName, _session.Definition)
                ?? _chapters
                    .FirstOrDefault(entry =>
                        string.Equals(entry.ChapterId, boardName, StringComparison.Ordinal))
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

        // 이 에피소드의 선택지 칸들 (R7) — 가지마다 제 칸을 얹어 <b>철도 위에서</b> 고치고 잇는다.
        IReadOnlyList<GraphChoicePort> slots = SlotsOf(source.Id);

        var branches = new List<(ChapterEdge? Edge, ExitPort? Port, Rect? Target, GraphChoicePort? Slot)>();

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

            branches.Add((edge, PortFor(source, edge, optionPorts, defaultPort), target, SlotFor(slots, edge)));
        }

        foreach (ChapterEdge edge in edges.Where(edge => !edge.HasNoOptionLabel))
        {
            AddEdgeBranch(edge);
        }

        // ⛔ <b>대본에서 파생되던 가지는 걷었다</b> (R7 결정 ② · 2026-09-16 소유자).
        //    구판 OPTION 스텁과 "진행"(기본 출구) 스텁이 여기 섰는데, 선택지를 긋는 자리가
        //    이 화면이 된 지금은 <b>사람이 만들지 않은 점</b>이 섞여 혼란스럽다
        //    (소유자: "붉은 점 하나만 있을 때 자동으로 그 위로 '진행' 점이 생긴다").
        //    ⚠ 데이터는 그대로 산다 — 차후 「분기」 탭이 그것을 다시 화면에 올린다.

        foreach (ChapterEdge edge in edges.Where(edge => edge.HasNoOptionLabel))
        {
            AddEdgeBranch(edge);
        }

        // 빈 칸은 맨 아래에 — 이미 그은 길 다음에 "여기에 더 놓을 수 있다"가 온다.
        foreach (GraphChoicePort empty in slots.Where(item => item.IsEmpty))
        {
            branches.Add((null, null, null, empty));
        }

        if (branches.Count == 0)
        {
            return;
        }

        double trunkX = sourceRect.X + RailTrunkInset;
        double branchY = sourceRect.Bottom + RailFirstDrop;

        foreach ((ChapterEdge? edge, ExitPort? port, Rect? target, GraphChoicePort? slot) in branches)
        {
            DrawRailBranch(source, edge, port, trunkX, branchY, target, nodeRects, freeNodes, slot);

            if (edge is not null && target is not null)
            {
                // 도착 포트 장부 — 어느 에피소드의 어느 선택지가 여기로 들어오는가.
                if (!arrivals.TryGetValue(edge.ToEpisodeId, out List<(string From, string Label)>? list))
                {
                    arrivals[edge.ToEpisodeId] = list = new List<(string, string)>();
                }

                list.Add((source.ExcelEpisodeId ?? source.Name,
                    edge.HasNoOptionLabel ? "(진행)" : edge.OptionLabel!));
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
        if (edge.HasNoOptionLabel)
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

    /// <summary>
    /// <b>철도 가지 위의 선택지 칸</b> (R7 · 2026-09-16 소유자: "아래쪽으로 3개").
    ///
    /// 점 하나와 문구 하나. 점은 세 색으로 자리를 말하고(회색·붉은색·초록색), 문구를 누르면
    /// 그 자리에서 고치며, 점을 끌면 이어진다. 카드 <b>아래</b>에 서는 이유는 선택지가
    /// 실제로 뻗어 나가는 자리가 거기이기 때문이다 — 카드 안에 두면 이야기가 어디로
    /// 갈라지는지 한눈에 안 보인다.
    /// </summary>
    private Control SlotChip(GraphChoicePort slot, string nodeId)
    {
        IReadOnlyList<GraphChoicePort> ports = SlotsOf(nodeId);
        bool live = IsLive(slot, ports);
        bool auto = IsAutoSlot(slot, ports);
        string text = LabelOf(slot, ports);
        IBrush dot = SlotDot(slot, ports);

        var knob = new Ellipse
        {
            Width = PortRadius * 2,
            Height = PortRadius * 2,
            Fill = live ? dot : Brushes.Transparent,
            Stroke = dot,
            StrokeThickness = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            [ToolTip.TipProperty] = slot.IsEmpty
                ? live ? "끌어서 다음 에피소드에 놓으세요." : "선택지 문구를 적으면 끌 수 있습니다."
                : "이어져 있습니다. 빈 곳에 끌어다 놓으면 끊깁니다."
        };

        var label = new TextBlock
        {
            Text = auto ? "자동" : text.Length > 0 ? text : "＋ 선택지",
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = live ? dot : null,
            Opacity = live ? 1 : 0.45,
            FontStyle = text.Length > 0 || auto ? FontStyle.Normal : FontStyle.Italic,
            VerticalAlignment = VerticalAlignment.Center,

            // ⚠ 배경이 없으면 <b>눌림이 안 온다</b> — 투명한 TextBlock은 히트 대상이 아니다.
            //   2026-09-16에 "문구를 적을 칸이 안 열린다"의 진짜 원인이 이것이었다.
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Ibeam),
            [ToolTip.TipProperty] = auto
                ? "자동 길 — 문구 없이 다음으로 갑니다. 선택지 문구를 하나라도 적으면 이 자리가 닫힙니다."
                : "눌러서 선택지 문구를 적습니다. 적어야 끌어서 이을 수 있습니다."
        };

        if (!slot.IsAuto)
        {
            label.PointerPressed += (_, args) => UiGuard.Run(_session, "선택지 문구", () =>
            {
                args.Handled = true;
                _editingSlot = SlotKey(slot);
                Rebuild();
            });
        }

        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Background = Brushes.Transparent,

            // 어느 에피소드의 몇 번 칸인지 — 검증이 그 줄을 찾는 자리다.
            Tag = SlotKey(slot)
        };

        line.Children.Add(knob);

        line.Children.Add(string.Equals(_editingSlot, SlotKey(slot), StringComparison.Ordinal)
            ? SlotEditor(slot, ports)
            : label);

        // 꺼진 점을 누르면 <b>켠다</b>, 켜진 점을 누르면 <b>끈다</b> — 손짓 하나가 한 가지 뜻이다.
        knob.PointerPressed += (_, args) => UiGuard.Run(_session, "선택지", () =>
        {
            args.Handled = true;

            if (live)
            {
                BeginSlotDrag(slot, knob, nodeId);
                return;
            }

            // 꺼진 점을 누르면 <b>문구 칸을 연다</b> — 살리는 것은 적는 행위이지 누르는
            // 행위가 아니다(2026-09-17 소유자). 점도 문구도 같은 자리로 데려간다.
            _editingSlot = SlotKey(slot);
            Rebuild();
        });

        return line;
    }

    /// <summary>철도 가지의 옛 칩 — 아직 칸이 없는 가지(구판 OPTION 스텁·종료)가 쓴다.</summary>
    private Control LegacyChip(
        DialogueNode source, ChapterEdge? edge, ExitPort? port, IReadOnlyList<DialogueNode> freeNodes)
    {
        string chipText = edge is { HasNoOptionLabel: true } || (edge is null && port?.Kind == ExitPortKind.Default)
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

        chip.PointerPressed += (_, args) =>
        {
            args.Handled = true;
            ShowRailChip(chip, source, edge, port, freeNodes);
        };

        return chip;
    }

    /// <summary>그 에피소드의 노드가 낸 포트들 — 칸의 활성 판정이 이웃 칸을 봐야 한다.</summary>
    /// <summary>그 노드의 선택지 칸들 — 챕터 간선 순서 그대로.</summary>
    private IReadOnlyList<GraphChoicePort> SlotsOf(string nodeId) =>
        _projection?.Items.OfType<ExpandedNodeProjection>()
            .FirstOrDefault(node => string.Equals(node.NodeId, nodeId, StringComparison.Ordinal))
            ?.OutputPorts
            .Select(port => port.ChoicePort)
            .Where(choice => choice is not null)
            .Select(choice => choice!)
            .ToList() ?? [];

    private static GraphChoicePort? SlotFor(IReadOnlyList<GraphChoicePort> slots, ChapterEdge edge) =>
        slots.FirstOrDefault(slot =>
            string.Equals(slot.ToEpisodeId, edge.ToEpisodeId, StringComparison.Ordinal) &&
            string.Equals(slot.Label, edge.OptionLabel ?? string.Empty, StringComparison.Ordinal));

    /// <summary>철도 위의 점에서 끌기를 시작한다 — 선은 그 점에서 나간다.</summary>
    private void BeginSlotDrag(GraphChoicePort slot, Control knob, string nodeId)
    {
        _connectingFrom = new GraphOutputPortProjection(
            $"choice:{slot.Slot}", GraphOutputPortKind.Choice, nodeId,
            slot.Label, slot.Slot, !slot.IsEmpty, ExecutionPort: null, slot);

        Point start = knob.TranslatePoint(new Point(PortRadius, PortRadius), GraphCanvas)
            ?? new Point(0, 0);

        _connectingLine = new Line
        {
            StartPoint = start,
            EndPoint = start,
            Stroke = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
            StrokeThickness = 2,
            StrokeDashArray = new AvaloniaList<double> { 4, 3 }
        };

        GraphCanvas.Children.Add(_connectingLine);
        SetHint("이을 에피소드 노드 위에서 놓으세요. 빈 곳에 놓으면 길이 걷힙니다.");
    }

    private void DrawRailBranch(
        DialogueNode source,
        ChapterEdge? edge,
        ExitPort? port,
        double trunkX,
        double y,
        Rect? target,
        IReadOnlyDictionary<string, Rect> nodeRects,
        IReadOnlyList<DialogueNode> freeNodes,
        GraphChoicePort? slot = null)
    {
        double cursorX = trunkX;

        Control chip = slot is not null
            ? SlotChip(slot, source.Id)
            : LegacyChip(source, edge, port, freeNodes);

        chip.Measure(Size.Infinity);

        _railVisuals.Add(RailLine(trunkX, y, trunkX + 10, y));
        Canvas.SetLeft(chip, trunkX + 14);
        Canvas.SetTop(chip, y - chip.DesiredSize.Height / 2);

        _railVisuals.Add(chip);
        cursorX = trunkX + 14 + chip.DesiredSize.Width + 6;

        // 아직 안 이은 칸은 <b>여기서 끝</b>이다 — 갈 곳이 없으니 선도 없고 "종료"도 아니다
        // (2026-09-16 소유자: "연결되지 않은 상태라면 '종료'라는 글자가 표시됩니다").
        if (slot is { IsEmpty: true })
        {
            return;
        }

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

            // 커스텀 노드의 기본 출구는 죽었다 (2026-08-21) — EffectiveDefaultExit가
            // 늘 null이므로 배선된 씬은 전부 (진행) 합류다.
            if (node.EffectiveDefaultExit is { } defaultNext)
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

            if (node.EffectiveDefaultExit is { } defaultNext)
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
            // 커스텀→커스텀 배선이 죽어(2026-08-21) 체인은 첫 씬 하나로 끝난다.
            currentId = free.EffectiveDefaultExit;
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
    /// 챕터의 에피소드만 <b>각진 미색 서류</b>다 — 작가의 자유 씬은 둥근 흰 원고(✎)라서
    /// 섞여 있어도 한눈에 갈린다. 미니맵·접힌 목록도 같은 언어를 쓴다.
    ///
    /// ⚠ <b>모양이 잠김을 뜻하지는 않는다</b>(2026-09-16 · R-E). 서류 모양이던 시절에는
    /// 본문이 실제로 읽기 전용이었지만 이제 대본은 어느 노드든 열린다 — 이 모양이 말하는
    /// 것은 <b>소속</b>뿐이다: 챕터의 에피소드인가, 아니면 자유 씬인가.
    /// </summary>
    private static CardStyle CardStyleFor(GraphNodeKind kind, bool chapterEpisode) => kind switch
    {
        GraphNodeKind.Dialogue when chapterEpisode => new CardStyle(
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

    /// <summary>챕터의 에피소드인가 — 소속을 묻는 것이지 잠금을 묻는 것이 아니다.</summary>
    private bool IsChapterEpisode(string nodeId) =>
        _session?.Project.FindNode(nodeId) is DialogueNode { ExcelEpisodeId: not null };

    private NodeCard BuildCard(ExpandedNodeProjection node)
    {
        CardStyle style = CardStyleFor(node.NodeKind, IsChapterEpisode(node.NodeId));

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

        // ⛔ <b>선택지 칸은 카드 안에 없다</b> (2026-09-16 소유자: "아래쪽으로 3개").
        //    카드 아래 철도 가지에 선다 — 그 자리가 선택지가 실제로 뻗어 나가는 자리이고,
        //    카드 안에 두면 이야기가 어디로 갈라지는지 한눈에 안 보인다.
        IReadOnlyList<GraphOutputPortProjection> inCard = node.OutputPorts
            .Where(port => port.Kind != GraphOutputPortKind.Choice)
            .ToList();

        var visual = new NodeCard(node.NodeId, node.NodeKind, card, inCard, style);

        for (int index = 0; index < inCard.Count; index++)
        {
            body.Children.Add(BuildPortRow(inCard[index], visual, index));
        }

        Canvas.SetLeft(card, node.Position.X);
        Canvas.SetTop(card, node.Position.Y);
        GraphCanvas.Children.Add(card);

        card.PointerPressed += (_, args) => OnCardPressed(visual, args);

        // 더블클릭 = 이 노드를 무대에서 본다. 첫 누름이 이미 선택과 끌기를 시작해 두므로
        // (OnCardPressed) 여기서 할 일은 <b>그 끌기를 걷고</b> 셸에 알리는 것뿐이다 —
        // 안 걷으면 탭이 바뀐 뒤에도 카드가 마우스에 붙어 따라다닌다.
        card.DoubleTapped += (_, args) =>
        {
            _draggingCard = null;
            _draggingGroup = false;
            args.Handled = true;
            NodeActivated?.Invoke(visual.NodeId);
        };

        return visual;
    }

    private FileProxyVisual BuildFileProxy(CollapsedFileProjection file)
    {
        var content = new StackPanel { Spacing = 0 };

        // 화면에 실제로 선 행 수 — 장면 머리글을 포함한다. 프록시 높이와 간선 끝점이
        // 이 수를 쓴다(노드 행 수만 쓰면 머리글만큼 짧아진다).
        int visualRows = 0;

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
            // ⛔ <b>판 → 장면 → 노드</b> (R6 S-3). 장면 머리글도 <b>한 행을 차지한다</b> —
            //    간선 끝점의 Y가 `머리글높이 + 행번호 × 행높이`라, 안 세면 그 아래 노드의
            //    간선이 한 칸씩 위로 붙는다. 세는 규칙의 주인은 `CollapsedSceneGroup.HasHeader`다.
            foreach (CollapsedSceneGroup scene in file.Scenes)
            {
                if (scene.HasHeader)
                {
                    content.Children.Add(BuildProxySceneHeader(scene));
                    visualRows++;
                }

                foreach (CollapsedNodeEntry entry in scene.Entries)
                {
                    ProxyNodeRow row = BuildProxyRow(entry, visualRows++);
                    rows.Add(row);
                    content.Children.Add(row.Visual);
                }
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

        return new FileProxyVisual(file.FileId, visual, rows, Math.Max(1, visualRows));
    }

    /// <summary>
    /// 접힌 판 안의 <b>장면 머리글</b> 한 줄 (R6 S-3).
    ///
    /// ⚠ 누를 수 없다 — 장면은 담는 자리이고, 고르는 것은 노드다(대본 탭 트리와 같은 규율,
    /// <c>docs/plans/R6-explorer.md</c> §1). 여기서 누르면 판 끌기가 된다.
    /// </summary>
    private static Border BuildProxySceneHeader(CollapsedSceneGroup scene) =>
        new()
        {
            Height = ProxyRowHeight,
            Background = new SolidColorBrush(Color.FromArgb(26, 107, 114, 128)),
            Child = new TextBlock
            {
                Text = scene.SceneId.Length == 0 ? scene.DisplayName : "▸ " + scene.DisplayName,
                Margin = new Thickness(14, 0),
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center,
                [ToolTip.TipProperty] = scene.SceneId.Length == 0
                    ? "이 챕터의 진행에 안 실리는 노드들입니다 — 장면 경계가 없습니다."
                    : $"장면 '{scene.DisplayName}' — 같은 장면끼리 저장·롤백 구간을 공유합니다."
            }
        };

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
            Text = $"{CardStyleFor(entry.NodeKind, IsChapterEpisode(entry.NodeId)).Icon} {entry.NodeName}",
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

    // ── 선택지 슬롯 (R7 P-2) ────────────────────────────────────────────────

    /// <summary>
    /// <b>아직 안 이은 칸에 적어 둔 문구들</b> — 에피소드마다 <b>차례대로</b>. `null`은 아직
    /// 아무것도 안 적은 칸이다(= 꺼진 칸).
    ///
    /// ⛔ <b>자리 번호로 들지 않는다</b> (2026-09-16에 고쳤다). 간선이 하나 늘거나 줄면 빈 칸의
    /// 자리 번호가 통째로 밀리는데, 번호를 열쇠로 쓰면 적어 둔 문구가 <b>엉뚱한 칸에 붙거나
    /// 사라진다</b>. 차례로 들면 밀림이 저절로 맞는다 — 이은 길이 늘면 앞에서 하나 빠지고,
    /// 끊으면 그 문구가 앞에 하나 들어온다.
    ///
    /// ⚠ 저장하지 않는다. 간선은 도착이 있어야 존재하므로 도착 없는 문구를 담을 칸이 챕터에
    /// 없다(R6의 빈 장면과 같은 부류). 문구 자체는 적는 즉시 챕터의 선택지 사전에 배운다.
    /// </summary>
    private readonly Dictionary<string, List<string?>> _pending = new(StringComparer.Ordinal);

    /// <summary>그 칸이 <b>빈 칸들 중 몇 번째</b>인가. 이은 칸이면 음수다.</summary>
    private static int PendingAt(GraphChoicePort slot, IReadOnlyList<GraphChoicePort> slots) =>
        slot.Slot - slots.Count(item => !item.IsEmpty);

    private string? PendingText(GraphChoicePort slot, IReadOnlyList<GraphChoicePort> slots)
    {
        if (!_pending.TryGetValue(slot.FromEpisodeId, out List<string?>? list))
        {
            return null;
        }

        int at = PendingAt(slot, slots);

        return at >= 0 && at < list.Count ? list[at] : null;
    }

    private void SetPending(GraphChoicePort slot, IReadOnlyList<GraphChoicePort> slots, string? text)
    {
        int at = PendingAt(slot, slots);

        if (at < 0)
        {
            return;
        }

        if (!_pending.TryGetValue(slot.FromEpisodeId, out List<string?>? list))
        {
            _pending[slot.FromEpisodeId] = list = [];
        }

        while (list.Count <= at)
        {
            list.Add(null);
        }

        list[at] = text;

        // 꼬리의 꺼진 칸은 들고 있을 이유가 없다.
        while (list.Count > 0 && list[^1] is null)
        {
            list.RemoveAt(list.Count - 1);
        }
    }

    private static string SlotKey(GraphChoicePort choice) => choice.FromEpisodeId + "#" + choice.Slot;

    /// <summary>그 칸이 지금 지고 있는 문구 — 이은 것은 간선에서, 안 이은 것은 적어 둔 데서.</summary>
    private string LabelOf(GraphChoicePort choice, IReadOnlyList<GraphChoicePort> slots) =>
        choice.IsEmpty ? PendingText(choice, slots) ?? string.Empty : choice.Label;

    /// <summary>
    /// 살아난 칸인가 — <b>문구를 적으면</b> 살아나고 비우면 도로 꺼진다 (2026-09-17 소유자).
    ///
    /// ⚠ 점을 눌러 켜는 길도 잠깐 있었는데 소유자가 물렸다 — 켜기와 적기가 갈라져 있으면
    /// 손짓이 둘이 되고, 켜 두고 안 적은 칸이 <b>뜻 없이</b> 붉게 남는다.
    /// </summary>
    private bool IsArmed(GraphChoicePort choice, IReadOnlyList<GraphChoicePort> slots) =>
        choice.IsEmpty && PendingText(choice, slots) is not null;

    /// <summary>
    /// <b>자동 길 자리</b>인가 — 세 칸이 전부 꺼져 있을 때의 첫 칸 (R7 §2 ③-a).
    ///
    /// 자동 길은 문구가 없어야 하고(<c>AutoEdgeHasChoiceLabel</c>) 그 에피소드의 <b>유일한
    /// 간선</b>이어야 한다(<c>AutoEdgeHasSiblings</c>) — 그래서 이 조건이 정확히 자동 길이
    /// 설 수 있는 유일한 자리다. <b>화면이 규칙을 미리 지킨다.</b>
    /// </summary>
    private bool IsAutoSlot(GraphChoicePort choice, IReadOnlyList<GraphChoicePort> slots) =>
        choice.IsAuto ||
        (choice.IsEmpty && choice.Slot == 0 &&
         slots.All(other => other.IsEmpty && PendingText(other, slots) is null));

    /// <summary>
    /// 살아 있는 칸인가 — 이어졌거나, 켰거나, 자동 길 자리다.
    /// </summary>
    private bool IsLive(GraphChoicePort choice, IReadOnlyList<GraphChoicePort> slots) =>
        !choice.IsEmpty || IsArmed(choice, slots) || IsAutoSlot(choice, slots);

    /// <summary>지금 문구를 적고 있는 칸 — <b>테스트의 손잡이</b>이자 실제 입력칸이다.</summary>
    internal TextBox? SlotLabelBox { get; private set; }

    /// <summary>안 이은 활성 칸의 붉은 점.</summary>
    internal static readonly IBrush SlotLive = new SolidColorBrush(Color.FromRgb(0xD9, 0x3A, 0x3A));

    /// <summary>이어진 칸의 초록 점.</summary>
    internal static readonly IBrush SlotJoined = new SolidColorBrush(Color.FromRgb(0x1F, 0x9D, 0x55));

    /// <summary>아직 못 쓰는 칸의 회색 테두리.</summary>
    internal static readonly IBrush SlotAsleep = new SolidColorBrush(Color.FromArgb(120, 150, 150, 150));

    /// <summary>
    /// 그 칸의 색 — <b>세 자리를 한눈에</b> 가른다 (2026-09-16 소유자).
    ///
    /// | 텅 빈 회색 | 아직 꺼져 있다 — 눌러서 켠다 |
    /// | 붉은색 | 켜졌다 — 끌어다 놓을 수 있다 |
    /// | 초록색 | 이어졌다 |
    /// </summary>
    private IBrush SlotDot(GraphChoicePort choice, IReadOnlyList<GraphChoicePort> slots) =>
        !choice.IsEmpty ? SlotJoined : IsLive(choice, slots) ? SlotLive : SlotAsleep;

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

    /// <summary>지금 문구를 적고 있는 칸. 없으면 null.</summary>
    private string? _editingSlot;

    /// <summary>
    /// 문구 입력칸 — Enter 확정 · Esc 취소 · 딴 데를 누르면 취소(대본 탭 트리와 같은 손버릇).
    ///
    /// 이미 이어진 길이면 <b>그 간선의 문구를 고치고</b>, 아직 안 이었으면 적어만 둔다.
    /// 어느 쪽이든 <b>챕터의 선택지 사전에 배운다</b> — 문구의 주인은 챕터다(v9).
    /// </summary>
    private Control SlotEditor(GraphChoicePort choice, IReadOnlyList<GraphChoicePort> slots)
    {
        var box = new TextBox
        {
            Text = LabelOf(choice, slots),
            FontSize = 10,
            Padding = new Thickness(3, 0),
            MinHeight = 0,
            VerticalAlignment = VerticalAlignment.Center
        };

        void Commit(bool keep)
        {
            _editingSlot = null;
            SlotLabelBox = null;

            string wanted = box.Text?.Trim() ?? string.Empty;

            if (keep && !string.Equals(wanted, LabelOf(choice, slots), StringComparison.Ordinal))
            {
                Relabel(choice, slots, wanted);
            }

            Rebuild();
        }

        box.KeyDown += (_, args) => UiGuard.Run(_session, "선택지 문구", () =>
        {
            if (args.Key is Key.Enter or Key.Escape)
            {
                args.Handled = true;
                Commit(args.Key == Key.Enter);
            }
        });

        box.LostFocus += (_, _) => UiGuard.Run(_session, "선택지 문구", () =>
        {
            if (string.Equals(_editingSlot, SlotKey(choice), StringComparison.Ordinal))
            {
                Commit(keep: false);
            }
        });

        SlotLabelBox = box;
        return box;
    }

    private void Relabel(GraphChoicePort choice, IReadOnlyList<GraphChoicePort> slots, string wanted)
    {
        if (_session is null)
        {
            return;
        }

        if (choice.IsEmpty)
        {
            // 담을 간선이 아직 없다 — 화면이 들고, 문구만 챕터의 어휘집에 남긴다.
            // ⚠ <b>비우면 도로 꺼진다</b> (2026-09-17 소유자) — 문구가 곧 그 칸의 생사다.
            SetPending(choice, slots, wanted.Length > 0 ? wanted : null);

            if (wanted.Length > 0)
            {
                // ⚠ <b>이미 있으면 안 배운다.</b> 사전은 <b>어휘집</b>이라 같은 말이 두 번 들어갈
                //    자리가 없고, `AddChoiceLabel`은 [선택지 사전] 화면을 위해 중복을 거절한다 —
                //    여기서 그대로 부르면 <b>흔한 문구를 다시 쓰는 것만으로 실패한다</b>
                //    (2026-09-16에 테스트가 잡았다).
                if (!_session.Editor.FindChapter(choice.ChapterId)!.ChoiceOptions.Any(option =>
                        string.Equals(option.Text, wanted, StringComparison.Ordinal)))
                {
                    _session.Editor.AddChoiceLabel(choice.ChapterId, wanted);
                }
            }

            return;
        }

        // ⚠ 간선의 신원은 (출발, 도착, 문구)다 — 고치기 <b>전</b>의 문구로 찾아야 한다.
        _session.Editor.UpdateEdge(
            choice.ChapterId, choice.FromEpisodeId, choice.ToEpisodeId!,
            matchOptionLabel: choice.Label, optionLabel: wanted);
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
                ProxyWidth, ProxyHeaderHeight + (proxy.VisualRowCount * ProxyRowHeight));
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

        if (TargetVisualOf(nodeId) is not { } target)
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

        Center(target);
    }

    /// <summary>
    /// 노드 하나를 <b>화면 한가운데로</b> 가져온다 — 이미 보이더라도 옮긴다
    /// (2026-08-25 소유자: 무대에서 돌아올 때 "화면 중앙에 온 채").
    ///
    /// ⚠ <see cref="ScrollToSelected"/>와 <b>일부러 다르다</b>. 그쪽은 보이는 노드에 끼어들지
    /// 않는다 — 편집 중에 화면이 저 혼자 움직이면 스크롤해 둔 자리를 빼앗기 때문이다.
    /// 여기는 사람이 <b>판을 막 열어 눈이 아직 아무 데도 안 붙은</b> 순간이라 그 배려가
    /// 오히려 방해가 된다: "구석에 조금 보이니까 안 옮긴다"는 곧 못 찾는다는 뜻이다.
    ///
    /// 탭을 막 바꾼 순간에는 판도 카드도 <b>아직 재기 전</b>이라 크기가 0이다. 그때 계산하면
    /// 대체 크기로 어림잡게 되어 카드 높이의 절반쯤 어긋난다 — 가운데에 놓겠다고 해 놓고
    /// 어중간한 자리에 놓는 셈이라, 판과 카드 <b>둘 다</b> 재어진 다음으로 미룬다.
    /// </summary>
    internal void CenterOnNode(string nodeId) => CenterOnNode(nodeId, attempt: 0);

    /// <summary>재어지길 기다리는 횟수. 끝내 안 재어지면 어림값으로라도 옮긴다 — 영원히
    /// 미루면 아무 일도 안 일어나고, 그것이 가장 나쁘다(사람은 못 찾은 채 남는다).</summary>
    private const int CenterAttempts = 4;

    private void CenterOnNode(string nodeId, int attempt)
    {
        if (TargetVisualOf(nodeId) is not { } target)
        {
            return;
        }

        bool measured =
            GraphScroll.Viewport.Width > 0 && GraphScroll.Viewport.Height > 0 &&
            target.Bounds.Width > 0 && target.Bounds.Height > 0;

        if (!measured && attempt < CenterAttempts)
        {
            Dispatcher.UIThread.Post(
                () => CenterOnNode(nodeId, attempt + 1), DispatcherPriority.Loaded);
            return;
        }

        Center(target);
    }

    /// <summary>카드가 있으면 카드, 접힌 판 안이면 그 프록시 — 눈이 실제로 가야 할 것.</summary>
    private Control? TargetVisualOf(string nodeId) =>
        FindCard(nodeId)?.Visual
        ?? _proxies.FirstOrDefault(proxy => proxy.Rows.Any(row =>
            string.Equals(row.NodeId, nodeId, StringComparison.Ordinal)))?.Visual;

    private void Center(Control target)
    {
        double left = Canvas.GetLeft(target);
        double top = Canvas.GetTop(target);
        double width = target.Bounds.Width > 0 ? target.Bounds.Width : CardWidth;
        double height = target.Bounds.Height > 0 ? target.Bounds.Height : 160;

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
                ProxyHeaderHeight + (proxy.VisualRowCount * ProxyRowHeight),
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
        // 공급 포트 안내는 2026-08-22에 사라졌다 — 끌 수 있는 포트가 실행 출구뿐이다.
        SetHint("연결할 실행 노드 또는 접힌 파일의 실행 노드 행 위에서 놓으세요. " +
            "빈 곳에 놓으면 실행 연결이 끊어집니다.");

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

        // 끌기가 끝났으면 할 말도 끝났다. 예전에는 여기서 <b>조작법 설명으로 되돌렸는데</b>
        // (2026-08-24 폐기), 그것이 곧 늘 서 있던 그 문구였다.
        SetHint(null);

        GraphNodeHit? dropped = NodeAt(args.GetPosition(GraphCanvas));

        string? target = dropped is null || string.Equals(dropped.NodeId, port.NodeId, StringComparison.Ordinal)
            ? null
            : dropped.NodeId;

        if (port.ChoicePort is { } choice)
        {
            // ⚠ <b>UiGuard로 받는다.</b> 규칙 위반은 편집기가 예외로 거절하는데(신원 겹침 ·
            //    없는 에피소드), 놓임 처리기는 Avalonia가 부르는 자리라 그대로 터진다.
            //    거절은 상태줄로 가야 한다 — 이 저장소의 모든 편집 창구가 그 규약을 쓴다.
            UiGuard.Run(_session, "선택지 잇기", () => JoinChoice(choice, SlotsOf(port.NodeId), target));
            return;
        }

        if (port.ExecutionPort is not null)
        {
            _session?.Editor.SetExitTarget(port.ExecutionPort, target);
        }
    }

    /// <summary>
    /// <b>선택지를 잇거나 끊는다</b> (R7 P-3) — 이 손짓이 고치는 것은 <b>챕터 간선</b>이다.
    ///
    /// ⛔ 연출 층에 사본을 두지 않는다(<c>docs/plans/R7.md</c> §2 ①). 그래서 여기서 그은 길이
    /// [챕터 그래프]에도 그대로 보이고, 내보내기·도달성 증명·V1/V2 진단이 <b>하나도 안 바뀐
    /// 채</b> 그 값을 본다.
    /// </summary>
    private void JoinChoice(GraphChoicePort choice, IReadOnlyList<GraphChoicePort> slots, string? targetNodeId)
    {
        if (_session is null)
        {
            return;
        }

        if (targetNodeId is null)
        {
            Unjoin(choice);
            return;
        }

        if (EpisodeAt(targetNodeId) is not var (chapterId, episodeId))
        {
            _session.SetStatus("에피소드가 아닌 노드에는 선택지를 놓을 수 없습니다.");
            return;
        }

        // ⚠ 간선은 챕터 안의 길이다 — 두 챕터를 이을 수 없다. 조용히 무시하면 사람은
        //   손이 미끄러진 줄 안다.
        if (!string.Equals(chapterId, choice.ChapterId, StringComparison.Ordinal))
        {
            _session.SetStatus(
                $"'{chapterId}'는 다른 챕터입니다 — 선택지는 챕터 안에서만 잇습니다.");
            return;
        }

        string label = LabelOf(choice, slots);

        // 문구가 없는 채로 살아 있는 칸은 <b>자동 길</b> 자리다 (§2 ③-a).
        bool auto = choice.IsEmpty && label.Length == 0;

        // ⚠ <b>자동 길은 장면을 못 넘는다</b> — 장면 경계는 실제로 `Commit → Exit → Enter`라,
        //   묻지 않고 넘어가면 안 된다(`AutoEdgeCrossesScene`). 관문에서야 알면 되돌려야 하니
        //   여기서 먼저 말하고, <b>대신 할 수 있는 것</b>을 함께 말한다.
        //
        // ⚠ 장면ID를 아무 데도 안 적은 챕터에서는 에피소드마다 제 장면이라(`__scene_*`)
        //   자동 길이 아예 설 수 없다. 그것도 규칙 그대로다 — 숨기지 않고 말한다.
        if (auto && SceneOf(choice.ChapterId, choice.FromEpisodeId) is { } from &&
            SceneOf(choice.ChapterId, episodeId) is { } to &&
            !string.Equals(from, to, StringComparison.Ordinal))
        {
            _session.SetStatus(
                "자동 길은 같은 장면 안에서만 이어집니다 — 두 에피소드를 같은 장면에 두거나, " +
                "문구를 적어 선택지로 이어 주세요.");

            return;
        }

        if (!choice.IsEmpty)
        {
            // 이미 이어진 칸을 다른 노드로 옮긴 것이다 — 옛 길을 걷고 새로 긋는다.
            _session.Editor.RemoveEdge(
                choice.ChapterId, choice.FromEpisodeId, choice.ToEpisodeId!, choice.Label);
        }

        _session.Editor.AddEdge(
            choice.ChapterId, choice.FromEpisodeId, episodeId,
            optionLabel: label.Length > 0 ? label : null,
            auto: auto);

        // 그 칸의 적어 둔 문구는 이제 간선이 들고 있다 — 차례에서 하나 뺀다. 뒤엣것들이
        // 한 칸씩 앞으로 오는데, 간선이 하나 늘어 빈 칸도 한 칸씩 밀리므로 자리가 맞는다.
        if (_pending.TryGetValue(choice.FromEpisodeId, out List<string?>? pending) &&
            PendingAt(choice, slots) is var at and >= 0 && at < pending.Count)
        {
            pending.RemoveAt(at);
        }

        _session.SetStatus(auto
            ? $"'{choice.FromEpisodeId}' → '{episodeId}' 자동으로 이었습니다(문구 없이 지나갑니다)."
            : $"'{choice.FromEpisodeId}' → '{episodeId}' 선택지 '{label}'을 이었습니다.");

        Rebuild();
    }

    /// <summary>
    /// 빈 곳에 놓았다 — 이어져 있던 길을 걷는다.
    ///
    /// ⚠ <b>문구는 안 잃는다</b> (2026-09-16 소유자: "연결을 끊더라도 사라지지 않고 유지").
    /// 길을 끊는 것과 무엇을 물을지 정한 것은 다른 일이다 — 문구는 켜진 빈 칸으로 돌아와
    /// 다시 이을 수 있다. 칸이 하나 비므로 차례의 <b>맨 앞</b>에 들어간다.
    ///
    /// ⚠ 자동 길은 문구가 없으니 칸도 꺼진 채로 돌아온다.
    /// </summary>
    private void Unjoin(GraphChoicePort choice)
    {
        if (choice.IsEmpty)
        {
            return;
        }

        _session!.Editor.RemoveEdge(
            choice.ChapterId, choice.FromEpisodeId, choice.ToEpisodeId!, choice.Label);

        if (choice.Label.Length > 0)
        {
            if (!_pending.TryGetValue(choice.FromEpisodeId, out List<string?>? pending))
            {
                _pending[choice.FromEpisodeId] = pending = [];
            }

            pending.Insert(0, choice.Label);
        }

        _session.SetStatus(
            $"'{choice.FromEpisodeId}' → '{choice.ToEpisodeId}' 길을 걷었습니다." +
            (choice.Label.Length > 0 ? $" 문구 '{choice.Label}'은 칸에 남겨 뒀습니다." : string.Empty));

        Rebuild();
    }

    /// <summary>그 에피소드의 장면 — 빈 칸이면 <c>__scene_{에피소드}</c>로 퇴화한다.</summary>
    private string? SceneOf(string chapterId, string episodeId) =>
        _session?.Editor.FindChapter(chapterId)?.Episodes
            .FirstOrDefault(episode =>
                string.Equals(episode.EpisodeId, episodeId, StringComparison.Ordinal))
            ?.EffectiveSceneId;

    /// <summary>
    /// 그 노드가 선 자리 — (챕터, 에피소드). 에피소드가 아니면 null.
    ///
    /// ⚠ 판 이름이 챕터고 표식이 먼저고 없으면 이름이다 — 대본 탭·장면 묶기·슬롯 투영이
    /// 쓰는 그 규칙이다(새 규약을 만들지 않는다).
    /// </summary>
    private (string ChapterId, string EpisodeId)? EpisodeAt(string nodeId)
    {
        if (_session?.Project.FindFileContainingNode(nodeId) is not { } file ||
            _session.Project.FindNode(nodeId) is not DialogueNode dialogue ||
            _session.Editor.FindChapter(file.Name) is not { } chapter)
        {
            return null;
        }

        string episodeId = dialogue.ExcelEpisodeId is { Length: > 0 } marked ? marked : dialogue.Name;

        return chapter.Episodes.Any(episode =>
            string.Equals(episode.EpisodeId, episodeId, StringComparison.Ordinal))
            ? (chapter.ChapterId, episodeId)
            : null;
    }

    // AttachLatestResult(발행 결과 끌어 연결)는 2026-08-21에 사라졌다 — 연출 채널이
    // 자동으로 최신 발행본을 고정한다(ProjectEditor.EnsurePresentationChannel).
    // HandleSupplyDrop(조건 공급 끌어 연결)은 2026-08-22에 같은 길을 갔다 — 설정노드의
    // "이 챕터에 공급 중" 포트가 사라지면서 놓을 포트 자체가 없어졌다(공급 범위가 판
    // 전체라 이을 것이 없다). 링크 데이터와 AddSettingsLink는 그대로 산다.

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

            // ⚠ 행 번호는 <b>장면 머리글을 포함한</b> 화면 순서다 (R6 S-3) — 자리로 세지 않고
            //    각 행이 든 제 번호로 찾는다. 머리글을 누르면 아무 노드도 안 맞는다(맞다).
            if (proxy.Rows.FirstOrDefault(row => row.Index == rowIndex) is { } hit)
            {
                return new GraphNodeHit(hit.NodeId, hit.NodeKind);
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

        SetHint($"{sourceName} — {kind} → {targetName} · 우클릭으로 삭제");
    }

    /// <summary>
    /// 아래 띠에 한 줄 — <b>지금 벌어지는 일만</b> 말한다 (2026-08-24 소유자: "포트를 끌어
    /// 놓으면 연결, 빈곳좌클릭 드래그 범위선택 이런 설명문구도 제거").
    ///
    /// 늘 서 있던 조작법 설명은 폐지됐다. 손이 이미 아는 것을 매번 다시 일러 주면, 정작
    /// 무언가를 말해야 할 때 그 줄이 눈에 안 띈다 — 검증 보고에서 동기화 문구를 지운 것과
    /// 같은 선이다(같은 날).
    ///
    /// <b>null이면 띠가 접힌다.</b> 할 말 없는 빈 칸이 서 있으면 그것도 소음이다.
    /// 여닫는 문을 여기 하나로 둔 이유이기도 하다 — 문구만 지우고 띠를 안 접는 자리가
    /// 생기면 빈 줄이 남는다.
    /// </summary>
    private void SetHint(string? text)
    {
        HintText.Text = text ?? string.Empty;
        HintBar.IsVisible = !string.IsNullOrEmpty(text);
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

        // 이 판에서 사람이 만드는 노드는 대사·설정뿐이다 (2026-08-21) — 연출·연출 공급은
        // 배관이라 채널이 짓고(EnsurePresentationChannel), 설정노드는 챕터마다 자동으로 선다.
        StoryNode node = kind switch
        {
            GraphNodeKind.Dialogue => _session.Editor.AddDialogueNode(fileId, x, y),
            GraphNodeKind.Set => _session.Editor.AddSetNode(fileId, x, y),
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
        public FileProxyVisual(
            string fileId, Border visual, IReadOnlyList<ProxyNodeRow> rows, int visualRowCount)
        {
            FileId = fileId;
            Visual = visual;
            Rows = rows;
            VisualRowCount = visualRowCount;
        }

        public string FileId { get; }
        public Border Visual { get; }
        public IReadOnlyList<ProxyNodeRow> Rows { get; }

        /// <summary>
        /// 화면에 선 행 수 — <b>장면 머리글을 포함한다</b> (R6 S-3).
        ///
        /// ⚠ 높이·미니맵이 이것을 쓴다. <c>Rows.Count</c>(노드 행만)를 쓰면 머리글만큼
        /// 짧아져 아래 행이 상자 밖으로 삐져나온다.
        /// </summary>
        public int VisualRowCount { get; }
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
