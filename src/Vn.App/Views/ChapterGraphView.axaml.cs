using System.Diagnostics;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using IoPath = System.IO.Path;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Vn.App.Services;
using Vn.Authoring.Chapters;

namespace Vn.App.Views;

/// <summary>
/// 챕터·에피소드 그래프 뷰 (G4·G5). <b>별도 화면이고 기존 대사·연출 그래프는 손대지 않는다</b> (G-1).
///
/// <b>그래프는 읽기 전용이다 — 구조적으로.</b> 드래그 핸들러가 없고, 위치·관계의 소유자는
/// 엑셀이다(G-2). 이 화면이 파일에 쓰는 것은 둘뿐이며 그래프와 무관하다: 없는 에피소드
/// 워크북의 생성(<see cref="EpisodeLibrary"/>)과 동기화의 LineId 되쓰기(B열) — 둘 다 노드
/// 클릭·저장 감시(G5)의 일이다.
///
/// 오류가 있어도 읽힌 데까지 그린다. 빈 화면 + "오류"보다, 그려진 그래프 옆에 무엇이 어디서
/// 잘못됐는지 세워 두는 편이 고칠 자리를 알려 준다(규칙 14).
/// </summary>
public partial class ChapterGraphView : UserControl
{
    private const double CardWidth = 190;
    private const double CardHeight = 74;
    private const double CanvasMargin = 60;

    private readonly List<ChapterEntry> _entries = new();
    private readonly List<EpisodeSyncReport> _syncReports = new();

    /// <summary>선택된 챕터의 구조 검증 + 도달성 증명 결과 (G7). 워크북을 읽을 때 갱신된다.</summary>
    private ChapterValidationResult? _validation;

    private AuthoringSession? _session;
    private ChapterFolderWatcher? _watcher;
    private ChapterFolderWatcher? _episodeWatcher;
    private string? _selectedChapterId;
    private bool _updatingCombo;

    /// <summary>
    /// 워크북을 여는 손. 기본은 OS 기본 연결(엑셀)이고, 화면 없는 검증이 실제 엑셀을
    /// 띄우지 않도록 갈아끼울 수 있다.
    /// </summary>
    internal Action<string> OpenWorkbookFile { get; set; } = path =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    /// <summary>챕터 목록이 다시 읽혔다. 왼쪽 패널(MainWindow)이 이걸 듣고 목록을 다시 그린다.</summary>
    internal event Action<IReadOnlyList<ChapterEntry>>? EntriesReloaded;

    /// <summary>폴더를 다시 훑고 감시를 다시 건다. 새 챕터를 만든 직후(폴더가 방금 생겼을 수 있다) 부른다.</summary>
    internal void RefreshFromDisk() => WatchAndReload();

    /// <summary>왼쪽 패널의 챕터 클릭이 이 뷰의 선택을 바꾼다.</summary>
    internal void SelectChapter(string chapterId)
    {
        if (string.Equals(_selectedChapterId, chapterId, StringComparison.Ordinal))
        {
            return;
        }

        _selectedChapterId = chapterId;
        _updatingCombo = true;
        ChapterCombo.SelectedItem = chapterId;
        _updatingCombo = false;
        Validate();
        Draw();
    }

    public ChapterGraphView()
    {
        InitializeComponent();

        ReloadButton.Click += (_, _) => UiGuard.Run(_session, "챕터 다시 읽기", Reload);
        OpenFolderButton.Click += (_, _) => UiGuard.Run(_session, "챕터 폴더 열기", OpenFolder);
        ExportButton.Click += (_, _) => UiGuard.Run(_session, "챕터 내보내기", () => Export());

        ChapterCombo.SelectionChanged += (_, _) =>
        {
            if (_updatingCombo)
            {
                return;
            }

            _selectedChapterId = ChapterCombo.SelectedItem as string;
            Draw();
        };

        // 픽스처 전환 → 경로 하이라이트가 바뀐다 (G6).
        FixtureCombo.SelectionChanged += (_, _) =>
        {
            if (_updatingFixtureCombo)
            {
                return;
            }

            string? picked = FixtureCombo.SelectedItem as string;
            _selectedFixture = picked == "(끄기)" ? null : picked;
            Draw();
        };

        // 편집 (G-2 v2) — 전부 엑셀 셀에 써지고, 저장 감시가 다시 읽어 화면이 따라온다.
        ApplyButton.Click += (_, _) => UiGuard.Run(_session, "속성 저장", ApplySelectedProperties);
        RenameButton.Click += (_, _) => UiGuard.Run(_session, "에피소드 개명", RenameSelectedEpisode);
        AddEdgeButton.Click += (_, _) => UiGuard.Run(_session, "기존 에피소드 연결", AddEdgeFromPanel);
        DeleteEpisodeButton.Click += (_, _) => UiGuard.Run(_session, "에피소드 삭제", DeleteSelectedEpisode);
        AddEpisodeButton.Click += (_, _) => UiGuard.Run(_session, "에피소드 추가", AddEpisodeFromToolbar);
        SaveConditionButton.Click += (_, _) => UiGuard.Run(_session, "조건 저장", SaveConditionFromPanel);
        CreateNextButton.Click += (_, _) => UiGuard.Run(_session, "다음 에피소드 추가", CreateNextEpisodeFromPanel);
        EdgeApplyButton.Click += (_, _) => UiGuard.Run(_session, "간선 저장", ApplyEdgeFromPanel);
        EdgeDeleteButton.Click += (_, _) => UiGuard.Run(_session, "간선 삭제", DeleteSelectedEdge);
    }

    internal void Attach(AuthoringSession session)
    {
        _session = session;
        session.Changed += (_, _) => Dispatcher.UIThread.Post(WatchAndReload);
        WatchAndReload();
    }

    // ── 읽기 ────────────────────────────────────────────────────────────────

    /// <summary>프로젝트가 바뀌면 감시 대상 폴더도 바뀐다.</summary>
    private void WatchAndReload()
    {
        string? folder = ChapterLibrary.FolderFor(_session?.ProjectPath);
        string? episodes = EpisodeLibrary.FolderFor(_session?.ProjectPath);

        if (!string.Equals(_watcher?.Folder, folder, StringComparison.OrdinalIgnoreCase))
        {
            StartWatching(folder);
        }

        if (!string.Equals(_episodeWatcher?.Folder, episodes, StringComparison.OrdinalIgnoreCase))
        {
            StartWatchingEpisodes(episodes);
        }

        Reload();
    }

    /// <summary>
    /// 엑셀 저장 → 뷰 즉시 갱신 (Gate A). 감시·디바운스는 <see cref="ChapterFolderWatcher"/>가
    /// 하고, 여기서는 그 알림을 UI 스레드로 옮겨 다시 그리기만 한다.
    /// </summary>
    private void StartWatching(string? folder)
    {
        _watcher?.Dispose();
        _watcher = null;

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }

        // 알림은 워커 스레드에서 온다 — 화면을 만지기 전에 반드시 UI 스레드로 건너간다.
        _watcher = new ChapterFolderWatcher(
            folder,
            () => Dispatcher.UIThread.Post(() => UiGuard.Run(_session, "챕터 워크북 반영", Reload)));
    }

    /// <summary>
    /// 에피소드 저장 → 대사노드 반영 (G5). 챕터 감시와 같은 감시자를 episodes/에 하나 더 둔다.
    /// </summary>
    private void StartWatchingEpisodes(string? folder)
    {
        _episodeWatcher?.Dispose();
        _episodeWatcher = null;

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }

        _episodeWatcher = new ChapterFolderWatcher(
            folder,
            () => Dispatcher.UIThread.Post(() => UiGuard.Run(_session, "에피소드 반영", SyncEpisodes)));
    }

    /// <summary>
    /// 선택된 챕터의 에피소드 워크북 전부를 대사노드로 반영한다.
    ///
    /// 감시자는 어느 파일이 바뀌었는지 말하지 않으므로(저장 한 번이 이벤트 여러 개라 어차피
    /// 뭉개진다) 전부 다시 돈다 — 바뀌지 않은 워크북은 "변경 없음"으로 끝나 비용이 잔잔하다.
    /// </summary>
    internal void SyncEpisodes()
    {
        _syncReports.Clear();

        if (_session is null)
        {
            return;
        }

        string? folder = EpisodeLibrary.FolderFor(_session.ProjectPath);
        ChapterEntry? entry = _entries.FirstOrDefault(item => item.ChapterId == _selectedChapterId);

        if (folder is null || !Directory.Exists(folder) || entry?.Model is null)
        {
            return;
        }

        // 새 노드가 들어갈 판 = 그 챕터의 판 (챕터=판 1:1, G-1 v2). 없으면 만든다 —
        // 왼쪽 챕터 목록 클릭과 같은 규칙 하나를 쓴다.
        string fileId = _session.EnsureChapterBoard(entry.ChapterId);

        foreach (ChapterEpisode episode in entry.Model.Episodes)
        {
            string path = EpisodeLibrary.PathFor(folder, episode.EpisodeId);

            if (!File.Exists(path))
            {
                continue;
            }

            _syncReports.Add(EpisodeSyncService.Sync(
                _session.Editor,
                _session.Definition,
                fileId,
                path,
                entry.Model));
        }

        // 에피소드가 바뀌면 스탯 증감량도 바뀐다 — 도달성을 다시 증명한다.
        Validate();
        Draw();

        int rejected = _syncReports.Sum(report => report.RejectionCount);
        _session.SetStatus(rejected == 0
            ? $"에피소드 {_syncReports.Count}개를 반영했습니다."
            : $"에피소드 {_syncReports.Count}개 중 거부·경고 {rejected}건 — 아래 검증 보고를 확인하세요.");
    }

    /// <summary>
    /// 노드 클릭 → 에피소드 엑셀 열기 (G5). 워크북이 없으면 §3.2 규격대로 만들어서 연다 —
    /// 기획자가 머리글 11개를 손으로 칠 이유가 없다.
    /// </summary>
    internal void OpenEpisode(string episodeId)
    {
        string? folder = EpisodeLibrary.FolderFor(_session?.ProjectPath);

        if (folder is null)
        {
            _session?.SetStatus("프로젝트를 먼저 저장해야 에피소드 폴더 자리가 정해집니다.");
            return;
        }

        if (EpisodeLibrary.EnsureWorkbook(folder, episodeId))
        {
            _session?.SetStatus($"에피소드 워크북을 새로 만들었습니다: {EpisodeLibrary.PathFor(folder, episodeId)}");
            StartWatchingEpisodes(folder);
        }

        OpenWorkbookFile(EpisodeLibrary.PathFor(folder, episodeId));
    }

    private void Reload()
    {
        _entries.Clear();
        _entries.AddRange(ChapterLibrary.Load(
            ChapterLibrary.FolderFor(_session?.ProjectPath),
            _session?.Definition));

        _updatingCombo = true;
        ChapterCombo.ItemsSource = _entries.Select(entry => entry.ChapterId).ToList();

        if (_selectedChapterId is null ||
            _entries.All(entry => entry.ChapterId != _selectedChapterId))
        {
            _selectedChapterId = _entries.FirstOrDefault()?.ChapterId;
        }

        ChapterCombo.SelectedItem = _selectedChapterId;
        _updatingCombo = false;

        Validate();
        Draw();
        EntriesReloaded?.Invoke(_entries);
    }

    /// <summary>
    /// 구조 검증 + 도달성 증명 (G7). 워크북을 읽을 때마다 한 번 돌려 두고 화면은 그 결과를
    /// 그리기만 한다 — 픽스처를 바꿀 때마다 상태공간을 다시 훑을 이유가 없다.
    /// </summary>
    private void Validate()
    {
        _validation = null;

        ChapterEntry? entry = _entries.FirstOrDefault(item => item.ChapterId == _selectedChapterId);

        if (entry?.Model is null)
        {
            return;
        }

        _validation = ChapterValidator.Validate(
            entry.Model, EpisodeLibrary.FolderFor(_session?.ProjectPath));
    }

    private void OpenFolder()
    {
        string? folder = ChapterLibrary.FolderFor(_session?.ProjectPath);

        if (folder is null)
        {
            _session?.SetStatus("프로젝트를 먼저 저장해야 챕터 폴더 자리가 정해집니다.");
            return;
        }

        if (!Directory.Exists(folder))
        {
            // 폴더를 대신 만들지 않는다 — 이 레이어에서 파일을 만드는 쪽은 언제나 사람이다.
            _session?.SetStatus($"챕터 폴더가 없습니다: {folder}");
            return;
        }

        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    /// <summary>런타임 수입물이 나가는 자리. 에셋과 같은 규약 폴더 방식이다 — 매니페스트 없음.</summary>
    public const string ExportFolderName = "exported";

    /// <summary>
    /// G8 — 런타임 수입용 JSON을 쓴다. <b>검증을 통과해야만 나간다</b>(Gate C 3번) —
    /// 오류가 있으면 파일을 만들지 않고 사유를 보고 패널에 세운다. 쓰레기가 런타임으로
    /// 넘어가는 것보다 내보내기가 실패하는 편이 낫다.
    /// </summary>
    internal string? Export()
    {
        ChapterEntry? entry = _entries.FirstOrDefault(item => item.ChapterId == _selectedChapterId);

        if (entry?.Model is null || _session?.ProjectPath is null)
        {
            _session?.SetStatus("내보낼 챕터가 없습니다.");
            return null;
        }

        ChapterExportResult result = ChapterProgressionExporter.Export(
            entry.Model, EpisodeLibrary.FolderFor(_session.ProjectPath));

        // 거부 사유도 화면에 세운다 — 상태줄 한 줄로는 무엇이 왜인지 알 수 없다.
        _validation = result.Validation;
        Draw();

        if (result.Refused)
        {
            int errors = result.Validation.All
                .Count(item => item.Severity == ChapterDiagnosticSeverity.Error);

            DiagnosticsExpander.IsExpanded = true;
            _session.SetStatus(
                $"내보내기를 거부했습니다 — 검증 오류 {errors}건. 아래 검증 보고를 확인하세요.");

            return null;
        }

        string folder = IoPath.Combine(
            IoPath.GetDirectoryName(IoPath.GetFullPath(_session.ProjectPath))!, ExportFolderName);

        Directory.CreateDirectory(folder);
        string path = IoPath.Combine(folder, entry.ChapterId + ".progression.json");
        File.WriteAllText(path, result.Json!, new System.Text.UTF8Encoding(false));

        _session.SetStatus($"내보냈습니다: {path}");
        return path;
    }

    // ── 그리기 ──────────────────────────────────────────────────────────────

    private void Draw()
    {
        GraphCanvas.Children.Clear();
        DiagnosticsPanel.Children.Clear();

        // 그릴 것이 없으면 판도 없다. 이걸 지우지 않으면 큰 챕터를 보다가 못 읽는 챕터로
        // 넘어갔을 때 텅 빈 캔버스가 이전 크기 그대로 남아 스크롤만 넓어진다.
        GraphCanvas.Width = 0;
        GraphCanvas.Height = 0;

        ChapterEntry? entry = _entries.FirstOrDefault(item => item.ChapterId == _selectedChapterId);

        if (entry is null)
        {
            string? folder = ChapterLibrary.FolderFor(_session?.ProjectPath);

            EmptyText.IsVisible = true;
            EmptyText.Text = folder is null
                ? "프로젝트를 저장하면 그 옆의 chapters 폴더에서 챕터 워크북을 읽습니다."
                : $"챕터 워크북이 없습니다.\n{folder} 에 {{ChapterId}}.xlsx 를 넣으면 여기에 그려집니다.";

            DiagnosticsExpander.Header = "검증 보고";
            return;
        }

        EmptyText.IsVisible = false;

        if (entry.Model is null)
        {
            EmptyText.IsVisible = true;
            EmptyText.Text = $"'{entry.ChapterId}'을 읽지 못했습니다.\n{entry.OpenFailure}";
            DiagnosticsExpander.Header = "검증 보고 — 읽기 실패";
            return;
        }

        ChapterGraphModel model = entry.Model;
        var layout = ChapterGraphLayout.For(model.Episodes, CardWidth, CardHeight, CanvasMargin);
        _layout = layout; // 드래그 역산(캔버스 → 엑셀 좌표)의 근거

        GraphCanvas.Width = layout.Width;
        GraphCanvas.Height = layout.Height;

        RefreshFixtureCombo(model);
        (IReadOnlySet<string> path, IReadOnlySet<(string, string)> pathEdges) = WalkSelectedFixture(model);

        // 간선을 먼저 그려야 노드 카드 아래로 깔린다.
        foreach (ChapterEdge edge in model.Edges)
        {
            DrawEdge(model, layout, edge,
                onPath: pathEdges.Contains((edge.FromEpisodeId, edge.ToEpisodeId)));
        }

        foreach (ChapterEpisode episode in model.Episodes)
        {
            DrawEpisode(model, layout, episode, onPath: path.Contains(episode.EpisodeId));
        }

        DrawDiagnostics(model);
        RefreshPropertyPanel();
    }

    // ── 픽스처 (G6) ─────────────────────────────────────────────────────────

    private string? _selectedFixture;
    private bool _fixtureInitialized;
    private bool _updatingFixtureCombo;

    private void RefreshFixtureCombo(ChapterGraphModel model)
    {
        var names = new List<string> { "(끄기)" };
        names.AddRange(model.Fixtures.Select(fixture => fixture.Name));

        _updatingFixtureCombo = true;
        FixtureCombo.ItemsSource = names;

        // 처음 한 번만 `활성` 픽스처를 기본으로 고른다 — 시트가 고른 것을 화면이 존중하되,
        // 사람이 (끄기)를 골랐다면 그 선택이 이긴다.
        if (!_fixtureInitialized)
        {
            _selectedFixture = model.Fixtures.FirstOrDefault(fixture => fixture.IsActive)?.Name;
            _fixtureInitialized = true;
        }

        FixtureCombo.SelectedItem = _selectedFixture is not null && names.Contains(_selectedFixture)
            ? _selectedFixture
            : "(끄기)";
        _updatingFixtureCombo = false;
    }

    /// <summary>선택된 픽스처로 한 판을 걸어 경로(노드·간선 집합)를 얻는다.</summary>
    private (IReadOnlySet<string> Nodes, IReadOnlySet<(string, string)> Edges) WalkSelectedFixture(
        ChapterGraphModel model)
    {
        var empty = (new HashSet<string>(StringComparer.Ordinal),
            new HashSet<(string, string)>());

        FixtureStopText.Text = string.Empty;

        ChapterFixture? fixture = model.Fixtures.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, _selectedFixture, StringComparison.Ordinal));

        if (fixture is null)
        {
            return empty;
        }

        FixtureWalkResult walk = ChapterFixtureWalker.Walk(model, fixture);

        if (walk.StoppedBecause is not null)
        {
            FixtureStopText.Text = walk.StoppedBecause;
        }

        var nodes = walk.EpisodeIds.ToHashSet(StringComparer.Ordinal);
        var edges = new HashSet<(string, string)>();

        for (int index = 0; index + 1 < walk.EpisodeIds.Count; index++)
        {
            edges.Add((walk.EpisodeIds[index], walk.EpisodeIds[index + 1]));
        }

        return (nodes, edges);
    }

    private void DrawEdge(
        ChapterGraphModel model, ChapterGraphLayout layout, ChapterEdge edge, bool onPath)
    {
        ChapterEpisode? from = model.FindEpisode(edge.FromEpisodeId);
        ChapterEpisode? to = model.FindEpisode(edge.ToEpisodeId);

        if (from is null || to is null)
        {
            // 끝점이 없는 간선은 그리지 않는다. 이미 오류로 보고돼 있고, 허공에 매다는 편이 나쁘다.
            return;
        }

        (double x1, double y1) = layout.Center(from, CardWidth, CardHeight);
        (double x2, double y2) = layout.Center(to, CardWidth, CardHeight);

        var line = new Line
        {
            StartPoint = new Point(x1, y1),
            EndPoint = new Point(x2, y2),
            Stroke = edge.ConditionLabel is null
                ? new SolidColorBrush(Color.Parse("#8894A0"))
                : new SolidColorBrush(Color.Parse("#C08A3E")),
            StrokeThickness = 1.6
        };

        if (edge.HideWhenLocked)
        {
            // 잠기면 숨는 간선은 존재 자체가 조건부다 — 실선으로 그리면 없는 길을 약속하게 된다.
            line.StrokeDashArray = new AvaloniaList<double> { 4, 3 };
        }

        if (onPath)
        {
            // 픽스처가 실제로 지나가는 간선 (G6). 굵고 초록이다.
            line.Stroke = new SolidColorBrush(Color.Parse("#3E9B57"));
            line.StrokeThickness = 3.2;
        }

        bool isSelected = _selectedEdgeKey is { } key &&
            key.From == edge.FromEpisodeId && key.To == edge.ToEpisodeId;

        if (isSelected)
        {
            line.Stroke = new SolidColorBrush(Color.Parse("#3D7BD9"));
            line.StrokeThickness = 3.4;
        }

        // 간선의 정체를 시각 요소에 남긴다. 화면 없는 렌더 검증(Gate A)이 "무엇이 그려졌는지"를
        // 색·좌표로 역추론하지 않고 이름으로 확인할 수 있어야 한다.
        line.Tag = EdgeTag(edge);
        GraphCanvas.Children.Add(line);

        // 1.6px 실선은 사람이 못 누른다 — 보이지 않는 굵은 히트 선을 위에 겹친다.
        // Transparent는 히트 대상이다(null과 다르다). 카드가 나중에 그려져 그 위를 덮으므로
        // 간선의 가운데 구간만 클릭된다 — 그게 맞다.
        var hit = new Line
        {
            StartPoint = line.StartPoint,
            EndPoint = line.EndPoint,
            Stroke = Brushes.Transparent,
            StrokeThickness = 14,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };

        string fromId = edge.FromEpisodeId;
        string toId = edge.ToEpisodeId;
        hit.PointerPressed += (_, _) =>
            UiGuard.Run(_session, "간선 선택", () => SelectEdgeKey(fromId, toId));
        GraphCanvas.Children.Add(hit);

        string label = string.Join(" · ", new[]
        {
            edge.OptionLabel,
            edge.ConditionLabel is null ? null : $"[{edge.ConditionLabel}]"
        }.Where(part => !string.IsNullOrEmpty(part)));

        if (label.Length == 0)
        {
            return;
        }

        var text = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#F0F4F6F8")),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1),
            Child = new TextBlock { Text = label, FontSize = 10, Opacity = 0.85 }
        };

        text.Measure(Size.Infinity);
        Canvas.SetLeft(text, ((x1 + x2) / 2) - (text.DesiredSize.Width / 2));
        Canvas.SetTop(text, ((y1 + y2) / 2) - (text.DesiredSize.Height / 2));
        GraphCanvas.Children.Add(text);
    }

    private void DrawEpisode(
        ChapterGraphModel model, ChapterGraphLayout layout, ChapterEpisode episode, bool onPath)
    {
        // 워크북의 오류든 도달 불가든 노드에는 같은 ⚠로 선다 — 기획자에게는 둘 다 "고칠 것"이다.
        bool hasError = model.EpisodeHasError(episode) || IsUnreachable(episode);

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

        // 시작 에피소드(첫 행)가 곧 Root다 — 도달성의 씨앗이자 분기 저작의 출발점.
        if (ReferenceEquals(model.StartEpisode, episode))
        {
            var root = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#3D7BD9")),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 0),
                Child = new TextBlock
                {
                    Text = "ROOT",
                    FontSize = 9,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White
                }
            };
            ToolTip.SetTip(root, "시작 에피소드 — `에피소드` 시트의 첫 행입니다. 도달성 증명과 내보내기의 StartEpisodeId가 됩니다.");
            header.Children.Add(root);
        }

        if (episode.HasGate)
        {
            var lockMark = new TextBlock { Text = "🔒", FontSize = 11 };
            ToolTip.SetTip(lockMark, GateSummary(episode));
            header.Children.Add(lockMark);
        }

        if (episode.IsEnding)
        {
            header.Children.Add(new TextBlock { Text = "★", FontSize = 11, Foreground = Brushes.Goldenrod });
        }

        if (hasError)
        {
            header.Children.Add(new TextBlock { Text = "⚠", FontSize = 11, Foreground = Brushes.IndianRed });
        }

        header.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(episode.Title) ? episode.EpisodeId : episode.Title,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var body = new StackPanel { Spacing = 1 };
        body.Children.Add(header);
        body.Children.Add(new TextBlock
        {
            Text = episode.EpisodeId,
            FontSize = 10,
            Opacity = 0.6,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        body.Children.Add(new TextBlock
        {
            Text = $"{episode.Kind} · {episode.Index} · {episode.DialogueEntry}",
            FontSize = 9,
            Opacity = 0.45,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var card = new Border
        {
            Width = CardWidth,
            Height = CardHeight,
            Padding = new Thickness(9, 7),
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(hasError ? 2 : 1),
            BorderBrush = hasError
                ? Brushes.IndianRed
                : episode.IsEnding
                    ? new SolidColorBrush(Color.Parse("#C09A3E"))
                    : new SolidColorBrush(Color.Parse("#7F8A96")),
            Background = new SolidColorBrush(Color.Parse("#FAFBFCFD")),
            Child = body,
            // 노드 카드임을 EpisodeId로 표시한다. 간선 라벨도 Border라서, 표식이 없으면
            // 검증이 카드와 라벨을 구별하지 못한다.
            Tag = episode.EpisodeId
        };

        ToolTip.SetTip(card, Tooltip(episode));

        if (episode.HasGate)
        {
            card.BorderThickness = new Thickness(1.6);
            card.BorderBrush = new SolidColorBrush(Color.Parse("#C08A3E"));
        }

        if (onPath)
        {
            // 픽스처 경로 위의 노드 (G6). 간선과 같은 초록으로 묶인다. 오류 테두리보다는 뒤다 —
            // 경로에 있어도 깨진 건 깨진 것이다.
            if (!hasError)
            {
                card.BorderThickness = new Thickness(2.4);
                card.BorderBrush = new SolidColorBrush(Color.Parse("#3E9B57"));
            }

            card.Background = new SolidColorBrush(Color.Parse("#F0EDF7EF"));
        }

        // 클릭 = 선택(속성 패널) · 드래그 = 이동(놓으면 엑셀 X·Y에 저장, G-2 v2) ·
        // 더블클릭 = 에피소드 엑셀 열기. 기존 시나리오 그래프와 같은 문법이다.
        WireCardInteraction(card, episode);
        card.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);

        if (string.Equals(episode.EpisodeId, _selectedEpisodeId, StringComparison.Ordinal))
        {
            card.BorderBrush = new SolidColorBrush(Color.Parse("#3D7BD9"));
            card.BorderThickness = new Thickness(2.4);
        }

        (double x, double y) = layout.Place(episode);
        Canvas.SetLeft(card, x);
        Canvas.SetTop(card, y);
        GraphCanvas.Children.Add(card);
    }

    /// <summary>
    /// 도달성 증명이 "닿을 수 없다"고 한 에피소드인가 (G7). `도달불가 허용`이 켜진 노드는
    /// 의도된 것이므로 오류 표식을 달지 않는다 — 대신 그 사실이 진단 목록에 알림으로 선다(D3).
    /// </summary>
    private bool IsUnreachable(ChapterEpisode episode) =>
        _validation is { } validation &&
        !validation.Reachability.ReachableEpisodeIds.Contains(episode.EpisodeId) &&
        !episode.AllowUnreachable;

    // ── 편집 (G-2 v2) ───────────────────────────────────────────────────────

    private string? _selectedEpisodeId;
    private ChapterGraphLayout? _layout;
    private Border? _dragCard;
    private ChapterEpisode? _dragEpisode;
    private Point _dragPointerStart;
    private (double Left, double Top) _dragCardStart;
    private bool _dragMoved;

    /// <summary>선택된 챕터의 워크북 경로. 편집이 쓰는 대상이다.</summary>
    private string? SelectedChapterPath =>
        _entries.FirstOrDefault(item => item.ChapterId == _selectedChapterId)?.Path;

    private ChapterGraphModel? SelectedModel =>
        _entries.FirstOrDefault(item => item.ChapterId == _selectedChapterId)?.Model;

    private void WireCardInteraction(Border card, ChapterEpisode episode)
    {
        card.PointerPressed += (_, e) =>
        {
            SelectEpisode(episode.EpisodeId);

            _dragCard = card;
            _dragEpisode = episode;
            _dragPointerStart = e.GetPosition(GraphCanvas);
            _dragCardStart = (Canvas.GetLeft(card), Canvas.GetTop(card));
            _dragMoved = false;
            e.Pointer.Capture(card);
        };

        card.PointerMoved += (_, e) =>
        {
            if (!ReferenceEquals(_dragCard, card))
            {
                return;
            }

            Point now = e.GetPosition(GraphCanvas);
            Vector delta = now - _dragPointerStart;

            // 손떨림을 드래그로 세지 않는다 — 4px 문턱.
            if (!_dragMoved && Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4)
            {
                return;
            }

            _dragMoved = true;
            Canvas.SetLeft(card, _dragCardStart.Left + delta.X);
            Canvas.SetTop(card, _dragCardStart.Top + delta.Y);
        };

        card.PointerReleased += (_, e) =>
        {
            if (!ReferenceEquals(_dragCard, card))
            {
                return;
            }

            Border dragged = _dragCard;
            ChapterEpisode target = _dragEpisode!;
            _dragCard = null;
            _dragEpisode = null;
            e.Pointer.Capture(null);

            if (_dragMoved)
            {
                UiGuard.Run(_session, "노드 위치 저장", () =>
                    CommitNodePosition(target.EpisodeId, Canvas.GetLeft(dragged), Canvas.GetTop(dragged)));
            }
        };

        card.DoubleTapped += (_, _) =>
            UiGuard.Run(_session, "에피소드 열기", () => OpenEpisode(episode.EpisodeId));
    }

    /// <summary>
    /// 드래그로 놓은 캔버스 좌표를 엑셀 좌표로 되돌려(배치의 평행이동 역산) 그 행의 X·Y 셀에
    /// 쓴다. 저장이 끝나면 폴더 감시가 다시 읽어 그래프·간선이 따라온다.
    /// </summary>
    internal void CommitNodePosition(string episodeId, double canvasX, double canvasY)
    {
        if (SelectedChapterPath is not { } path || _layout is not { } layout)
        {
            return;
        }

        ChapterWriteResult result = ChapterWorkbookWriter.SetEpisodePosition(
            path, episodeId, canvasX - layout.OffsetX, canvasY - layout.OffsetY);

        _session?.SetStatus(result.Written
            ? $"'{episodeId}' 위치를 엑셀에 저장했습니다."
            : result.Failure!);
    }

    /// <summary>선택은 노드 아니면 간선 하나다 — 패널이 무엇을 편집하는지 애매하면 안 된다.</summary>
    private (string From, string To)? _selectedEdgeKey;

    internal void SelectEpisode(string? episodeId)
    {
        _selectedEpisodeId = episodeId;
        _selectedEdgeKey = null;
        Draw(); // Draw 끝에서 패널이 채워진다
    }

    internal void SelectEdgeKey(string fromEpisodeId, string toEpisodeId)
    {
        _selectedEdgeKey = (fromEpisodeId, toEpisodeId);
        _selectedEpisodeId = null;
        Draw();
    }

    /// <summary>선택된 에피소드(또는 간선)의 현재 값으로 패널을 채운다. 원천은 언제나 방금 읽은 모델이다.</summary>
    private void RefreshPropertyPanel()
    {
        ChapterGraphModel? model = SelectedModel;
        ChapterEpisode? episode = model?.FindEpisode(_selectedEpisodeId ?? string.Empty);
        ChapterEdge? edge = _selectedEdgeKey is { } key
            ? model?.Edges.FirstOrDefault(candidate =>
                candidate.FromEpisodeId == key.From && candidate.ToEpisodeId == key.To)
            : null;

        PropertyPanel.IsVisible = episode is not null;
        EdgePanel.IsVisible = edge is not null;
        NoSelectionText.IsVisible = episode is null && edge is null;

        RefreshConditionList(model);

        if (model is not null && edge is not null)
        {
            RefreshEdgePanel(model, edge);
        }

        if (model is null || episode is null)
        {
            return;
        }

        IdBox.Text = episode.EpisodeId;
        TitleBox.Text = episode.Title;
        IndexBox.Text = episode.Index;
        EntryBox.Text = episode.DialogueEntry;
        EndingKeyBox.Text = episode.EndingKey ?? string.Empty;
        MemoBox.Text = episode.Memo ?? string.Empty;
        AllowUnreachableCheck.IsChecked = episode.AllowUnreachable;

        KindCombo.ItemsSource = new[] { "Main", "Attachment" };
        KindCombo.SelectedItem =
            string.Equals(episode.Kind, "Attachment", StringComparison.OrdinalIgnoreCase)
                ? "Attachment"
                : "Main";

        var labels = new List<string> { "(없음)" };
        labels.AddRange(model.Conditions.Select(condition => condition.Label));
        VisibleCombo.ItemsSource = labels;
        UnlockCombo.ItemsSource = new List<string>(labels);
        VisibleCombo.SelectedItem = episode.VisibleConditionLabel ?? "(없음)";
        UnlockCombo.SelectedItem = episode.UnlockConditionLabel ?? "(없음)";

        EdgeTargetCombo.ItemsSource = model.Episodes
            .Where(candidate => candidate.EpisodeId != episode.EpisodeId)
            .Select(candidate => candidate.EpisodeId)
            .ToList();

        RefreshEdgeList(model, episode);
    }

    private void RefreshEdgeList(ChapterGraphModel model, ChapterEpisode episode)
    {
        EdgeListPanel.Children.Clear();

        foreach (ChapterEdge edge in model.Edges.Where(candidate =>
                     string.Equals(candidate.FromEpisodeId, episode.EpisodeId, StringComparison.Ordinal)))
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

            string label = edge.IsPlainAdvance ? "(일반 진행)" : edge.OptionLabel!;
            string condition = edge.ConditionLabel is null ? string.Empty : $"  [{edge.ConditionLabel}]";
            var text = new TextBlock
            {
                Text = $"→ {edge.ToEpisodeId}  {label}{condition}",
                FontSize = 11,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            ToolTip.SetTip(text, "클릭하면 이 간선의 조건·해금을 편집합니다.");

            string from = edge.FromEpisodeId;
            string to = edge.ToEpisodeId;
            text.PointerPressed += (_, _) =>
                UiGuard.Run(_session, "간선 선택", () => SelectEdgeKey(from, to));
            row.Children.Add(text);

            var remove = new Button { Content = "✕", FontSize = 10, Padding = new Thickness(5, 1) };
            Grid.SetColumn(remove, 1);
            remove.Click += (_, _) => UiGuard.Run(_session, "간선 삭제", () =>
                Report(ChapterWorkbookWriter.RemoveEdge(SelectedChapterPath!, from, to),
                    $"간선 {from}→{to}을 지웠습니다."));
            row.Children.Add(remove);

            EdgeListPanel.Children.Add(row);
        }
    }

    /// <summary>간선 편집 패널 — "이 분기는 언제 보이고 언제 열리는가"를 채운다.</summary>
    private void RefreshEdgePanel(ChapterGraphModel model, ChapterEdge edge)
    {
        EdgeFromToText.Text = $"{edge.FromEpisodeId} → {edge.ToEpisodeId}";
        EdgeLabelEditBox.Text = edge.OptionLabel ?? string.Empty;
        EdgeHideCheck.IsChecked = edge.HideWhenLocked;
        EdgeLockedMsgBox.Text = edge.LockedMessage ?? string.Empty;

        var labels = new List<string> { "(없음)" };
        labels.AddRange(model.Conditions.Select(condition => condition.Label));
        EdgeConditionCombo.ItemsSource = labels;
        EdgeConditionCombo.SelectedItem = edge.ConditionLabel ?? "(없음)";
    }

    /// <summary>간선 패널의 [적용]. 바뀐 필드만 셀에 쓴다.</summary>
    internal void ApplyEdgeFromPanel()
    {
        if (_selectedEdgeKey is not { } key || SelectedChapterPath is not { } path ||
            SelectedModel?.Edges.FirstOrDefault(candidate =>
                candidate.FromEpisodeId == key.From && candidate.ToEpisodeId == key.To) is not { } edge)
        {
            return;
        }

        string? Changed(string? boxValue, string? current)
        {
            string value = boxValue?.Trim() ?? string.Empty;
            return string.Equals(value, current ?? string.Empty, StringComparison.Ordinal) ? null : value;
        }

        string? condition = EdgeConditionCombo.SelectedItem as string == "(없음)"
            ? string.Empty
            : EdgeConditionCombo.SelectedItem as string;

        ChapterWriteResult result = ChapterWorkbookWriter.UpdateEdge(
            path, key.From, key.To,
            optionLabel: Changed(EdgeLabelEditBox.Text, edge.OptionLabel ?? string.Empty),
            conditionLabel: Changed(condition, edge.ConditionLabel ?? string.Empty),
            hideWhenLocked: EdgeHideCheck.IsChecked == edge.HideWhenLocked ? null : EdgeHideCheck.IsChecked,
            lockedMessage: Changed(EdgeLockedMsgBox.Text, edge.LockedMessage ?? string.Empty));

        Report(result, $"간선 {key.From}→{key.To}을 저장했습니다.");
    }

    internal void DeleteSelectedEdge()
    {
        if (_selectedEdgeKey is not { } key || SelectedChapterPath is not { } path)
        {
            return;
        }

        ChapterWriteResult result = ChapterWorkbookWriter.RemoveEdge(path, key.From, key.To);

        if (result.Written)
        {
            _selectedEdgeKey = null;
        }

        Report(result, $"간선 {key.From}→{key.To}을 지웠습니다.");
    }

    /// <summary>
    /// [＋ 분기] — 분기 저작의 핵심 동작. Id 하나로 새 에피소드가 만들어져 부모 오른쪽에
    /// 순서대로 서고 간선이 이어진다. 부모 선택은 유지된다 — 분기를 연달아 추가하는 흐름이라서다.
    /// </summary>
    internal void CreateNextEpisodeFromPanel()
    {
        if (_selectedEpisodeId is not { } parent || SelectedChapterPath is not { } path)
        {
            return;
        }

        string newId = NewNextIdBox.Text?.Trim() ?? string.Empty;

        if (newId.Length == 0)
        {
            _session?.SetStatus("새 분기의 Id를 적어 주세요.");
            return;
        }

        string? label = string.IsNullOrWhiteSpace(NewNextLabelBox.Text)
            ? null
            : NewNextLabelBox.Text.Trim();

        ChapterWriteResult result = ChapterWorkbookWriter.AddNextEpisode(
            path, parent, newId, title: newId, optionLabel: label);

        if (result.Written)
        {
            NewNextIdBox.Text = string.Empty;
            NewNextLabelBox.Text = string.Empty;
        }

        Report(result, $"'{newId}'를 만들어 {parent}의 다음으로 이었습니다.");
    }

    private void RefreshConditionList(ChapterGraphModel? model)
    {
        ConditionListPanel.Children.Clear();

        foreach (ChapterCondition condition in model?.Conditions ?? Enumerable.Empty<ChapterCondition>())
        {
            var line = new TextBlock
            {
                Text = $"{condition.Label} = {condition.Expression}",
                FontSize = 10,
                Opacity = 0.75,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };

            // 클릭하면 편집 칸으로 올라온다 — 라벨이 같으면 [조건 추가/수정]이 수정이 된다.
            line.PointerPressed += (_, _) =>
            {
                ConditionLabelBox.Text = condition.Label;
                ConditionExprBox.Text = condition.Expression;
            };

            ConditionListPanel.Children.Add(line);
        }
    }

    /// <summary>속성 패널의 [적용]. 모델의 현재 값과 다른 필드만 셀에 쓴다.</summary>
    internal void ApplySelectedProperties()
    {
        ChapterGraphModel? model = SelectedModel;
        ChapterEpisode? episode = model?.FindEpisode(_selectedEpisodeId ?? string.Empty);

        if (model is null || episode is null || SelectedChapterPath is not { } path)
        {
            return;
        }

        string? Changed(string? boxValue, string? current)
        {
            string value = boxValue?.Trim() ?? string.Empty;
            return string.Equals(value, current ?? string.Empty, StringComparison.Ordinal) ? null : value;
        }

        string? visible = VisibleCombo.SelectedItem as string == "(없음)"
            ? string.Empty
            : VisibleCombo.SelectedItem as string;
        string? unlock = UnlockCombo.SelectedItem as string == "(없음)"
            ? string.Empty
            : UnlockCombo.SelectedItem as string;

        ChapterWriteResult result = ChapterWorkbookWriter.UpdateEpisode(
            path,
            episode.EpisodeId,
            title: Changed(TitleBox.Text, episode.Title),
            index: Changed(IndexBox.Text, episode.Index),
            kind: Changed(KindCombo.SelectedItem as string, episode.Kind),
            dialogueEntry: Changed(EntryBox.Text, episode.DialogueEntry),
            visibleConditionLabel: Changed(visible, episode.VisibleConditionLabel ?? string.Empty),
            unlockConditionLabel: Changed(unlock, episode.UnlockConditionLabel ?? string.Empty),
            endingKey: Changed(EndingKeyBox.Text, episode.EndingKey ?? string.Empty),
            memo: Changed(MemoBox.Text, episode.Memo ?? string.Empty),
            allowUnreachable: AllowUnreachableCheck.IsChecked == episode.AllowUnreachable
                ? null
                : AllowUnreachableCheck.IsChecked);

        Report(result, $"'{episode.EpisodeId}' 속성을 엑셀에 저장했습니다.");
    }

    internal void RenameSelectedEpisode()
    {
        string oldId = _selectedEpisodeId ?? string.Empty;
        string newId = IdBox.Text?.Trim() ?? string.Empty;

        if (oldId.Length == 0 || newId.Length == 0 ||
            string.Equals(oldId, newId, StringComparison.Ordinal) ||
            SelectedChapterPath is not { } path)
        {
            return;
        }

        ChapterWriteResult result = ChapterWorkbookWriter.RenameEpisode(path, oldId, newId);

        if (result.Written)
        {
            _selectedEpisodeId = newId;
        }

        Report(result, $"'{oldId}' → '{newId}' 개명했습니다. 간선·픽스처 참조가 함께 따라갔습니다.");
    }

    internal void AddEdgeFromPanel()
    {
        if (_selectedEpisodeId is not { } from ||
            EdgeTargetCombo.SelectedItem is not string to ||
            SelectedChapterPath is not { } path)
        {
            _session?.SetStatus("간선을 추가하려면 도착 에피소드를 골라 주세요.");
            return;
        }

        string? label = string.IsNullOrWhiteSpace(EdgeLabelBox.Text) ? null : EdgeLabelBox.Text.Trim();

        Report(ChapterWorkbookWriter.AddEdge(path, from, to, optionLabel: label),
            $"간선 {from}→{to}을 더했습니다.");
        EdgeLabelBox.Text = string.Empty;
    }

    internal void DeleteSelectedEpisode()
    {
        if (_selectedEpisodeId is not { } episodeId || SelectedChapterPath is not { } path)
        {
            return;
        }

        ChapterWriteResult result = ChapterWorkbookWriter.RemoveEpisode(path, episodeId);

        if (result.Written)
        {
            _selectedEpisodeId = null;
        }

        Report(result, $"'{episodeId}' 행과 그 간선·픽스처 참조를 지웠습니다. 에피소드 엑셀 파일은 그대로입니다.");
    }

    internal void AddEpisodeFromToolbar()
    {
        if (SelectedChapterPath is not { } path || SelectedModel is not { } model)
        {
            _session?.SetStatus("챕터를 먼저 선택해 주세요.");
            return;
        }

        // Id는 자동 발명하지 않되 빈 워크북을 부를 수는 없으니, 겹치지 않는 자리표시 Id를 주고
        // 사람이 패널에서 [개명]으로 정하게 한다. 위치는 가장 오른쪽 노드 옆이다.
        int number = 1;
        while (model.FindEpisode($"new{number:D2}") is not null)
        {
            number++;
        }

        string episodeId = $"new{number:D2}";
        double x = model.Episodes.Count == 0 ? 0 : model.Episodes.Max(episode => episode.X) + 220;

        ChapterWriteResult result = ChapterWorkbookWriter.AddEpisode(path, episodeId, "새 에피소드", x, 0);

        if (result.Written)
        {
            _selectedEpisodeId = episodeId;
        }

        Report(result, $"'{episodeId}' 행을 더했습니다. Id와 대사엔트리를 패널에서 채워 주세요.");
    }

    internal void SaveConditionFromPanel()
    {
        string label = ConditionLabelBox.Text?.Trim() ?? string.Empty;
        string expression = ConditionExprBox.Text?.Trim() ?? string.Empty;

        if (label.Length == 0 || expression.Length == 0 || SelectedChapterPath is not { } path)
        {
            _session?.SetStatus("조건은 라벨과 식이 모두 있어야 합니다.");
            return;
        }

        bool exists = SelectedModel?.FindCondition(label) is not null;

        ChapterWriteResult result = exists
            ? ChapterWorkbookWriter.UpdateCondition(path, label, expression)
            : ChapterWorkbookWriter.AddCondition(path, label, expression);

        Report(result, exists ? $"조건 '{label}'을 고쳤습니다." : $"조건 '{label}'을 더했습니다.");
    }

    /// <summary>쓰기 결과를 상태줄로. 성공이면 감시가 다시 읽어 화면이 따라온다.</summary>
    private void Report(ChapterWriteResult result, string success) =>
        _session?.SetStatus(result.Written ? success : result.Failure!);

    /// <summary>간선 하나를 가리키는 표식. 화면 검증이 이 이름으로 간선을 찾는다.</summary>
    internal static string EdgeTag(ChapterEdge edge) =>
        $"{edge.FromEpisodeId}→{edge.ToEpisodeId}";

    private static string GateSummary(ChapterEpisode episode) => string.Join(" · ", new[]
    {
        episode.VisibleConditionLabel is null ? null : $"표시: {episode.VisibleConditionLabel}",
        episode.UnlockConditionLabel is null ? null : $"해금: {episode.UnlockConditionLabel}"
    }.Where(part => part is not null));

    private static string Tooltip(ChapterEpisode episode)
    {
        var lines = new List<string>
        {
            $"{episode.EpisodeId} ({episode.SourceRow}행)",
            $"대사엔트리: {episode.DialogueEntry}",
            $"위치: X={episode.X:0.##} Y={episode.Y:0.##}"
        };

        string gate = GateSummary(episode);

        if (gate.Length > 0)
        {
            lines.Add(gate);
        }

        if (episode.IsEnding)
        {
            lines.Add($"엔딩키: {episode.EndingKey}");
        }

        if (!string.IsNullOrWhiteSpace(episode.Memo))
        {
            lines.Add($"메모: {episode.Memo}");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// 오류·경고·정보를 심각도 순으로, 그 뒤에 에피소드 동기화 보고를. 각 줄이 파일·시트·행·열을
    /// 그대로 말하고, 머리글의 배지가 거부 건수를 든다(G5).
    /// </summary>
    private void DrawDiagnostics(ChapterGraphModel model)
    {
        // 워크북 읽기 진단 + 검증기(구조·교차·도달성) 진단을 한 목록으로 본다.
        // 같은 진단이 두 곳에서 나올 수 있으므로(리더가 낸 것을 검증기가 다시 담는다) 겹은 지운다.
        List<ChapterDiagnostic> all = model.Diagnostics
            .Concat(_validation?.All ?? Enumerable.Empty<ChapterDiagnostic>())
            .Distinct()
            .ToList();

        int errors = all.Count(item => item.Severity == ChapterDiagnosticSeverity.Error);
        int warnings = all.Count(item => item.Severity == ChapterDiagnosticSeverity.Warning);
        int rejected = _syncReports.Sum(report => report.RejectionCount);

        string header = errors + warnings == 0
            ? $"검증 보고 — 오류 없음 (알림 {all.Count}건)"
            : $"검증 보고 — 오류 {errors} · 경고 {warnings}";

        if (_syncReports.Count > 0)
        {
            header += rejected == 0
                ? $" · 동기화 {_syncReports.Count}건 반영"
                : $" · 동기화 거부·경고 {rejected}건";
        }

        DiagnosticsExpander.Header = header;
        DiagnosticsExpander.IsExpanded = errors > 0 || rejected > 0;

        // 탐색이 상한에서 멈췄으면 "도달 불가"가 단정이 아니라는 사실을 먼저 말한다.
        if (_validation is { Reachability.ExplorationComplete: false })
        {
            DiagnosticsPanel.Children.Add(DiagnosticLine(
                "도달성 탐색이 상한에서 중단됐습니다 — 아래의 도달 불가는 단정이 아니라 " +
                "'경로를 찾지 못했다'입니다.", Brushes.DarkGoldenrod, dim: false, bold: true));
        }

        if (all.Count == 0 && _syncReports.Count == 0)
        {
            DiagnosticsPanel.Children.Add(new TextBlock
            {
                Text = "보고할 것이 없습니다.",
                FontSize = 11,
                Opacity = 0.55
            });

            return;
        }

        foreach (ChapterDiagnostic diagnostic in all
                     .OrderByDescending(item => item.Severity)
                     .ThenBy(item => item.Sheet, StringComparer.Ordinal)
                     .ThenBy(item => item.Row ?? 0))
        {
            DiagnosticsPanel.Children.Add(DiagnosticLine(
                diagnostic.Describe(),
                diagnostic.Severity switch
                {
                    ChapterDiagnosticSeverity.Error => Brushes.IndianRed,
                    ChapterDiagnosticSeverity.Warning => Brushes.DarkGoldenrod,
                    _ => null
                },
                dim: diagnostic.Severity == ChapterDiagnosticSeverity.Info));
        }

        DrawSyncReports();
    }

    /// <summary>
    /// 에피소드 동기화 결과. 거부·삭제·함께 접힌 논리를 <b>목록으로</b> 보인다 —
    /// 조용한 무반영이 최악이다(G3-1·G3-2).
    /// </summary>
    private void DrawSyncReports()
    {
        foreach (EpisodeSyncReport report in _syncReports)
        {
            string summary = report.Applied
                ? $"에피소드 {report.EpisodeId} — 반영됨"
                : $"에피소드 {report.EpisodeId} — 반영 거부";

            DiagnosticsPanel.Children.Add(DiagnosticLine(
                summary, report.Applied ? null : Brushes.IndianRed, dim: false, bold: true));

            foreach (string problem in report.Problems)
            {
                DiagnosticsPanel.Children.Add(DiagnosticLine($"  {problem}", Brushes.IndianRed, dim: false));
            }

            foreach (ChapterDiagnostic diagnostic in report.Diagnostics
                         .Where(item => item.Severity != ChapterDiagnosticSeverity.Info))
            {
                DiagnosticsPanel.Children.Add(DiagnosticLine(
                    $"  {diagnostic.Describe()}",
                    diagnostic.Severity == ChapterDiagnosticSeverity.Error
                        ? Brushes.IndianRed
                        : Brushes.DarkGoldenrod,
                    dim: false));
            }

            foreach (EpisodePrunedLogic pruned in report.Pruned)
            {
                DiagnosticsPanel.Children.Add(DiagnosticLine(
                    $"  {pruned.Describe()}", Brushes.DarkGoldenrod, dim: false));
            }

            if (report.WrittenBackLineIds.Count > 0)
            {
                DiagnosticsPanel.Children.Add(DiagnosticLine(
                    $"  새 LineId {report.WrittenBackLineIds.Count}개를 워크북에 되썼습니다.",
                    null, dim: true));
            }

            if (report.WriteBackFailure is not null)
            {
                DiagnosticsPanel.Children.Add(DiagnosticLine(
                    $"  {report.WriteBackFailure}", Brushes.DarkGoldenrod, dim: false));
            }
        }
    }

    private static TextBlock DiagnosticLine(
        string text, IBrush? foreground, bool dim, bool bold = false) => new()
    {
        Text = text,
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Foreground = foreground,
        FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        Opacity = dim ? 0.6 : 1
    };
}
