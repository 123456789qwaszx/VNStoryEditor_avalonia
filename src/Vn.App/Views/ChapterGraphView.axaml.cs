using System.Diagnostics;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using IoPath = System.IO.Path;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vn.App.Services;
using Vn.Authoring.Chapters;

namespace Vn.App.Views;

/// <summary>
/// 챕터·에피소드 그래프 뷰 (G4·G5). <b>별도 화면이고 기존 대사·연출 그래프는 손대지 않는다</b> (G-1).
///
/// <b>편집은 전부 엑셀 셀 쓰기다 (G-2 v2).</b> 위치·관계의 소유자는 여전히 엑셀이고, 이 화면의
/// 드래그·패널·[＋ 분기]는 <see cref="ChapterWorkbookWriter"/>를 거쳐 해당 셀만 고친다.
/// 저장 감시(G5)가 다시 읽어 화면이 따라온다 — 화면 상태가 진실이 되는 순간은 없다.
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
    /// 워크북을 여는 손. 기본은 OS 기본 연결이고, 화면 없는 검증이 실제 엑셀을
    /// 띄우지 않도록 갈아끼울 수 있다.
    /// </summary>
    internal Action<string> OpenWorkbookFile { get; set; } = path =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    /// <summary>
    /// .xlsx 기본 앱의 실행 파일을 묻는 손. OS 연결을 맹신하면 엑셀 없는 기계에서
    /// 엉뚱한 앱(실사례: 챗지피티)이 뜨므로, 열기 전에 이걸로 확인한다.
    /// </summary>
    internal Func<string?> WorkbookHandlerProbe { get; set; } =
        SpreadsheetAssociation.ResolveXlsxHandler;

    /// <summary>스프레드시트 앱이 없을 때의 대안 — 탐색기에서 파일을 선택해 보여 준다.</summary>
    internal Action<string> RevealInFolder { get; set; } = path =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
        {
            UseShellExecute = true
        });

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
        EdgeApplyButton.Click += (_, _) => UiGuard.Run(_session, "간선 저장", ApplyEdgeFromPanel);
        EdgeDeleteButton.Click += (_, _) => UiGuard.Run(_session, "간선 삭제", DeleteSelectedEdge);

        // 빈 판 클릭 = 선택 해제. 카드·간선·라벨 핸들러가 각자 누름을 소비하므로(e.Handled)
        // 여기까지 흘러오는 왼쪽 누름은 진짜 빈 공간뿐이다.
        GraphCanvas.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(GraphCanvas).Properties.IsLeftButtonPressed)
            {
                SelectEpisode(null);
            }
        };
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

        string target = EpisodeLibrary.PathFor(folder, episodeId);
        string? handler = WorkbookHandlerProbe();

        if (!SpreadsheetAssociation.IsSpreadsheetHandler(handler))
        {
            // 편집할 수 없는 앱에 워크북을 던지지 않는다 — 폴더에서 보여 주고 사유를 말한다.
            RevealInFolder(target);
            _session?.SetStatus(handler is null
                ? ".xlsx에 연결된 앱이 없어 폴더에서 파일을 보여 줍니다. 엑셀이나 LibreOffice Calc로 열어 주세요."
                : $".xlsx의 기본 앱({IoPath.GetFileName(handler)})이 스프레드시트가 아니라서 폴더에서 보여 줍니다. " +
                  "우클릭 → 연결 프로그램에서 스프레드시트 앱을 고르세요.");
            return;
        }

        OpenWorkbookFile(target);
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
        _cardById.Clear();
        _cardBase.Clear();
        _lineByEdge.Clear();
        _lineBase.Clear();

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

        // 배치는 깊이 레이아웃이 소유한다 (v3) — 열 = 깊이, 드래그 없음.
        // 흐름(간선)이 바뀌면 자리가 저절로 따라온다.
        _placed = ChapterBranchPlanner.Layout(model)
            .ToDictionary(
                pair => pair.Key,
                pair => (X: pair.Value.X + CanvasMargin, Y: pair.Value.Y + CanvasMargin),
                StringComparer.Ordinal);

        GraphCanvas.Width = _placed.Count == 0
            ? CanvasMargin * 2
            : _placed.Values.Max(position => position.X) + CardWidth + CanvasMargin;
        GraphCanvas.Height = _placed.Count == 0
            ? CanvasMargin * 2
            : _placed.Values.Max(position => position.Y) + CardHeight + CanvasMargin;

        RefreshFixtureCombo(model);
        (IReadOnlySet<string> path, IReadOnlySet<(string, string)> pathEdges) = WalkSelectedFixture(model);

        // 간선을 먼저 그려야 노드 카드 아래로 깔린다.
        foreach (ChapterEdge edge in model.Edges)
        {
            DrawEdge(edge, onPath: pathEdges.Contains((edge.FromEpisodeId, edge.ToEpisodeId)));
        }

        foreach (ChapterEpisode episode in model.Episodes)
        {
            DrawEpisode(model, episode, onPath: path.Contains(episode.EpisodeId));
        }

        DrawDiagnostics(model);
        ApplySelectionVisuals();
        RefreshPropertyPanel(preserveTyping: true);
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

    private void DrawEdge(ChapterEdge edge, bool onPath)
    {
        if (!_placed.TryGetValue(edge.FromEpisodeId, out (double X, double Y) fromPos) ||
            !_placed.TryGetValue(edge.ToEpisodeId, out (double X, double Y) toPos))
        {
            // 끝점이 없는 간선은 그리지 않는다. 이미 오류로 보고돼 있고, 허공에 매다는 편이 나쁘다.
            return;
        }

        (double x1, double y1) = (fromPos.X + (CardWidth / 2), fromPos.Y + (CardHeight / 2));
        (double x2, double y2) = (toPos.X + (CardWidth / 2), toPos.Y + (CardHeight / 2));

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

        // 간선의 정체를 시각 요소에 남긴다. 화면 없는 렌더 검증(Gate A)이 "무엇이 그려졌는지"를
        // 색·좌표로 역추론하지 않고 이름으로 확인할 수 있어야 한다.
        line.Tag = EdgeTag(edge);
        GraphCanvas.Children.Add(line);

        // 선택 강조는 제자리에서 입힌다(ApplySelectionVisuals) — 여기서는 기본 모습만 등록한다.
        string fromId = edge.FromEpisodeId;
        string toId = edge.ToEpisodeId;
        _lineByEdge[(fromId, toId)] = line;
        _lineBase[(fromId, toId)] = (line.Stroke, line.StrokeThickness);

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

        hit.PointerPressed += (_, e) =>
        {
            e.Handled = true; // 캔버스(빈 공간 = 선택 해제)까지 흘러가면 곧바로 풀려 버린다
            UiGuard.Run(_session, "간선 선택", () => SelectEdgeKey(fromId, toId));
        };
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
            Child = new TextBlock { Text = label, FontSize = 10, Opacity = 0.85 },
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };

        // 라벨은 간선의 한가운데 — 사람이 간선을 누르려고 겨누는 바로 그 자리 — 를 덮는다.
        // 히트 선보다 나중에 그려져 클릭을 삼키므로, 라벨 클릭도 간선 선택으로 잇는다.
        text.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            UiGuard.Run(_session, "간선 선택", () => SelectEdgeKey(fromId, toId));
        };

        text.Measure(Size.Infinity);
        Canvas.SetLeft(text, ((x1 + x2) / 2) - (text.DesiredSize.Width / 2));
        Canvas.SetTop(text, ((y1 + y2) / 2) - (text.DesiredSize.Height / 2));
        GraphCanvas.Children.Add(text);
    }

    private void DrawEpisode(ChapterGraphModel model, ChapterEpisode episode, bool onPath)
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

        // 클릭 = 선택(속성 패널) · 더블클릭 = 에피소드 엑셀 열기. 드래그는 없다(v3) —
        // 배치는 깊이 레이아웃이 소유하고, 흐름을 바꾸면 자리가 따라온다.
        WireCardInteraction(card, episode);
        card.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);

        // 선택 강조는 제자리에서 입힌다(ApplySelectionVisuals) — 여기서는 기본 모습만 등록한다.
        _cardById[episode.EpisodeId] = card;
        _cardBase[episode.EpisodeId] = (card.BorderBrush, card.BorderThickness);

        (double x, double y) = _placed[episode.EpisodeId];
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

    // ── 편집 (G-2 v2 → v3: 배치는 깊이 레이아웃 소유, 드래그 없음) ──────────

    private string? _selectedEpisodeId;

    /// <summary>이번 그리기의 캔버스 배치 — <see cref="ChapterBranchPlanner.Layout"/> + 여백.</summary>
    private IReadOnlyDictionary<string, (double X, double Y)> _placed =
        new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);

    /// <summary>선택된 챕터의 워크북 경로. 편집이 쓰는 대상이다.</summary>
    private string? SelectedChapterPath =>
        _entries.FirstOrDefault(item => item.ChapterId == _selectedChapterId)?.Path;

    private ChapterGraphModel? SelectedModel =>
        _entries.FirstOrDefault(item => item.ChapterId == _selectedChapterId)?.Model;

    private void WireCardInteraction(Border card, ChapterEpisode episode)
    {
        card.PointerPressed += (_, e) =>
        {
            // 오른쪽·가운데 단추로는 선택하지 않는다.
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed)
            {
                return;
            }

            e.Handled = true; // 캔버스(빈 공간 = 선택 해제)로 흘러가면 방금 한 선택이 풀린다
            SelectEpisode(episode.EpisodeId);
        };

        card.DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            UiGuard.Run(_session, "에피소드 열기", () => OpenEpisode(episode.EpisodeId));
        };
    }

    /// <summary>선택은 노드 아니면 간선 하나다 — 패널이 무엇을 편집하는지 애매하면 안 된다.</summary>
    private (string From, string To)? _selectedEdgeKey;

    // 선택 강조는 다시 그리지 않고 제자리에서 바꾼다. 클릭 핸들러 안에서 캔버스를 다시 만들면
    // 방금 누른 카드가 파괴되어 더블클릭(둘째 탭이 다른 인스턴스에 떨어짐)과 드래그(캡처가
    // 죽은 카드에 걸림)가 죽는다 — 실사용에서 "클릭이 안 먹힌다"로 나타났던 결함이다.
    private readonly Dictionary<string, Border> _cardById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (IBrush? Brush, Thickness Thickness)> _cardBase =
        new(StringComparer.Ordinal);
    private readonly Dictionary<(string, string), Line> _lineByEdge = new();
    private readonly Dictionary<(string, string), (IBrush? Stroke, double Thickness)> _lineBase = new();


    internal void SelectEpisode(string? episodeId)
    {
        _selectedEpisodeId = episodeId;
        _selectedEdgeKey = null;
        ShowEditTabForSelection(episodeId is not null);
        ApplySelectionVisuals();
        RefreshPropertyPanel();
    }

    internal void SelectEdgeKey(string fromEpisodeId, string toEpisodeId)
    {
        _selectedEdgeKey = (fromEpisodeId, toEpisodeId);
        _selectedEpisodeId = null;
        ShowEditTabForSelection(true);
        ApplySelectionVisuals();
        RefreshPropertyPanel();
    }

    /// <summary>
    /// 무언가를 고르면 편집 탭으로 옮긴다 — 판에서 노드를 눌렀는데 오른쪽이 그대로면
    /// "클릭이 안 먹힌다"가 된다. 선택을 푸는 쪽(빈 판 클릭)은 탭을 건드리지 않는다:
    /// 조건을 보다가 판을 정리하려고 빈 곳을 누른 사람을 조건 탭 밖으로 끌어내지 않는다.
    /// </summary>
    private void ShowEditTabForSelection(bool selected)
    {
        if (selected)
        {
            RightTabs.SelectedItem = EditTab;
        }
    }

    /// <summary>모든 카드·간선을 기본 모습으로 되돌리고 선택된 것만 파랗게 강조한다.</summary>
    private void ApplySelectionVisuals()
    {
        foreach ((string id, Border card) in _cardById)
        {
            (IBrush? brush, Thickness thickness) = _cardBase[id];
            card.BorderBrush = brush;
            card.BorderThickness = thickness;
        }

        foreach (((string, string) key, Line line) in _lineByEdge)
        {
            (IBrush? stroke, double thickness) = _lineBase[key];
            line.Stroke = stroke;
            line.StrokeThickness = thickness;
        }

        if (_selectedEpisodeId is { } episodeId && _cardById.TryGetValue(episodeId, out Border? selected))
        {
            selected.BorderBrush = new SolidColorBrush(Color.Parse("#3D7BD9"));
            selected.BorderThickness = new Thickness(2.4);
        }

        if (_selectedEdgeKey is { } edgeKey && _lineByEdge.TryGetValue(edgeKey, out Line? line2))
        {
            line2.Stroke = new SolidColorBrush(Color.Parse("#3D7BD9"));
            line2.StrokeThickness = 3.4;
        }
    }

    /// <summary>편집 칸을 마지막으로 채운 선택. 같은 선택이면 다시 채우지 않는다.</summary>
    private (string? Episode, (string From, string To)? Edge) _panelFilledFor;

    /// <summary>
    /// 선택된 에피소드(또는 간선)의 현재 값으로 패널을 채운다. 원천은 언제나 방금 읽은 모델이다.
    ///
    /// <b>편집 칸은 선택이 바뀔 때만 채운다.</b> 저장 감시는 언제든 울리는데(엑셀이 파일을
    /// 건드리기만 해도) 그때마다 칸을 모델 값으로 되돌리면, 적어 두고 아직 [적용]하지 않은
    /// 글자가 조용히 사라진다 — 사람 눈에는 "적용을 눌러도 안 바뀐다"로 보인다.
    /// 포커스가 어디 있는지로 판정하던 이전 방식은 콤보·체크박스를 만진 순간 뚫렸다.
    /// </summary>
    /// <param name="preserveTyping">저장 감시가 부른 다시 그리기라면 참.</param>
    private void RefreshPropertyPanel(bool preserveTyping = false)
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

        (string? Episode, (string From, string To)? Edge) selection =
            (episode?.EpisodeId,
                edge is null ? null : (edge.FromEpisodeId, edge.ToEpisodeId));

        bool fill = !preserveTyping || selection != _panelFilledFor;
        _panelFilledFor = selection;

        RefreshConditionList(model);

        if (model is not null && edge is not null)
        {
            RefreshEdgePanel(model, edge, fill);
        }

        if (model is null || episode is null)
        {
            return;
        }

        if (fill)
        {
            IdBox.Text = episode.EpisodeId;
            TitleBox.Text = episode.Title;
            EndingKeyBox.Text = episode.EndingKey ?? string.Empty;
        }

        // 목록(ItemsSource)은 언제나 새로 고친다 — 조건·에피소드가 늘었을 수 있다.
        // 다만 고른 값은 채울 때가 아니면 사람이 고른 것을 지킨다.
        var labels = new List<string> { "(없음)" };
        labels.AddRange(model.Conditions.Select(condition => condition.Label));

        SetItems(VisibleCombo, labels,
            fill ? episode.VisibleConditionLabel ?? "(없음)" : VisibleCombo.SelectedItem as string);
        SetItems(UnlockCombo, new List<string>(labels),
            fill ? episode.UnlockConditionLabel ?? "(없음)" : UnlockCombo.SelectedItem as string);

        SetItems(EdgeTargetCombo,
            model.Episodes
                .Where(candidate => candidate.EpisodeId != episode.EpisodeId)
                .Select(candidate => candidate.EpisodeId)
                .ToList(),
            EdgeTargetCombo.SelectedItem as string); // 도착 고르기는 언제나 사람 소유

        RefreshEdgeList(model, episode);
    }

    /// <summary>목록을 갈되 고른 값은 지킨다(그 값이 새 목록에 남아 있다면).</summary>
    private static void SetItems(ComboBox combo, IReadOnlyList<string> items, string? selected)
    {
        combo.ItemsSource = items;
        combo.SelectedItem = selected is not null && items.Contains(selected) ? selected : null;
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
                // 배경 없는 TextBlock은 글자 획 위만 클릭된다 — 행 전체가 눌리게 깔아 둔다.
                Background = Brushes.Transparent,
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

    /// <summary>
    /// 간선 편집 패널 — "이 분기는 언제 보이고 언제 열리는가"를 채운다.
    /// <paramref name="fill"/>이 거짓이면 사람이 적던 값을 지킨다(위 설명 참조).
    /// </summary>
    private void RefreshEdgePanel(ChapterGraphModel model, ChapterEdge edge, bool fill)
    {
        EdgeFromToPanel.Children.Clear();
        EdgeFromToPanel.Children.Add(EpisodeLink(model, edge.FromEpisodeId));
        EdgeFromToPanel.Children.Add(new TextBlock
        {
            Text = "→",
            FontSize = 11,
            Opacity = 0.5,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        EdgeFromToPanel.Children.Add(EpisodeLink(model, edge.ToEpisodeId));

        if (fill)
        {
            EdgeLabelEditBox.Text = edge.OptionLabel ?? string.Empty;
            EdgeHideCheck.IsChecked = edge.HideWhenLocked;
            EdgeLockedMsgBox.Text = edge.LockedMessage ?? string.Empty;
        }

        var labels = new List<string> { "(없음)" };
        labels.AddRange(model.Conditions.Select(condition => condition.Label));

        SetItems(EdgeConditionCombo, labels,
            fill ? edge.ConditionLabel ?? "(없음)" : EdgeConditionCombo.SelectedItem as string);
    }

    /// <summary>
    /// 간선 패널에서 그 끝의 에피소드로 건너뛰는 고리. 누르면 그 에피소드가 선택되어
    /// 속성 패널(Id [개명]·제목)이 열린다 — 간선을 보다가 에피소드 이름을 고치려 할 때
    /// 노드를 다시 찾아 클릭하지 않아도 된다.
    /// </summary>
    private Control EpisodeLink(ChapterGraphModel model, string episodeId)
    {
        string title = model.FindEpisode(episodeId)?.Title ?? string.Empty;

        var link = new TextBlock
        {
            Text = title.Length == 0 || string.Equals(title, episodeId, StringComparison.Ordinal)
                ? episodeId
                : $"{episodeId} ({title})",
            FontSize = 11,
            TextDecorations = TextDecorations.Underline,
            Foreground = new SolidColorBrush(Color.Parse("#3D7BD9")),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Background = Brushes.Transparent,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };

        ToolTip.SetTip(link, "이 에피소드를 선택합니다 — 이름·제목은 거기서 고칩니다.");

        link.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            UiGuard.Run(_session, "에피소드 선택", () => SelectEpisode(episodeId));
        };

        return link;
    }

    /// <summary>간선 패널의 [적용]. 바뀐 필드만 셀에 쓴다.</summary>
    internal void ApplyEdgeFromPanel()
    {
        if (_selectedEdgeKey is not { } key || SelectedChapterPath is not { } path ||
            SelectedModel?.Edges.FirstOrDefault(candidate =>
                candidate.FromEpisodeId == key.From && candidate.ToEpisodeId == key.To) is not { } edge)
        {
            // 조용한 무동작 금지 — 사람에게는 "눌러도 아무 일이 없다"가 된다.
            _session?.SetStatus("간선을 다시 골라 주세요. 선택이 풀렸거나 그 간선이 사라졌습니다.");
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
                // 배경 없는 TextBlock은 글자 획 위만 클릭된다 — 행 전체가 눌리게 깔아 둔다.
                Background = Brushes.Transparent,
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
            _session?.SetStatus("에피소드를 다시 골라 주세요. 선택이 풀렸거나 그 에피소드가 사라졌습니다.");
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

        // 인덱스·종류·대사엔트리·메모·도달불가 허용은 패널에서 뺐다(v3 — 흐름 저작에 필요한
        // 최소만). 그 열들은 엑셀에서 여전히 고칠 수 있고, 대사엔트리는 생성 시 EpisodeId로
        // 자동이다.
        ChapterWriteResult result = ChapterWorkbookWriter.UpdateEpisode(
            path,
            episode.EpisodeId,
            title: Changed(TitleBox.Text, episode.Title),
            visibleConditionLabel: Changed(visible, episode.VisibleConditionLabel ?? string.Empty),
            unlockConditionLabel: Changed(unlock, episode.UnlockConditionLabel ?? string.Empty),
            endingKey: Changed(EndingKeyBox.Text, episode.EndingKey ?? string.Empty));

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

    /// <summary>
    /// [＋ 에피소드] — 에피소드가 선택돼 있으면 <b>그 자식으로</b> 만들어 간선까지 잇는다
    /// (자리 = 부모 깊이 + 1 열, v3 소유자 지시). 선택이 없으면 홀로 선 노드다.
    /// Id는 자동 발명하지 않되 빈 워크북을 부를 수는 없으니, 겹치지 않는 자리표시 Id를 주고
    /// 사람이 패널에서 [개명]으로 정하게 한다.
    /// </summary>
    internal void AddEpisodeFromToolbar()
    {
        if (SelectedChapterPath is not { } path || SelectedModel is not { } model)
        {
            _session?.SetStatus("챕터를 먼저 선택해 주세요.");
            return;
        }

        int number = 1;
        while (model.FindEpisode($"new{number:D2}") is not null)
        {
            number++;
        }

        string episodeId = $"new{number:D2}";
        string? parent = _selectedEpisodeId is { } id && model.FindEpisode(id) is not null ? id : null;

        ChapterWriteResult result;

        if (parent is not null)
        {
            (double x, double y) = ChapterBranchPlanner.SuggestPlacement(model, parent);
            result = ChapterWorkbookWriter.AddNextEpisode(path, parent, episodeId, "새 에피소드", x, y);
        }
        else
        {
            double x = model.Episodes.Count == 0 ? 0 : model.Episodes.Max(episode => episode.X) + 220;
            result = ChapterWorkbookWriter.AddEpisode(path, episodeId, "새 에피소드", x, 0);
        }

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
