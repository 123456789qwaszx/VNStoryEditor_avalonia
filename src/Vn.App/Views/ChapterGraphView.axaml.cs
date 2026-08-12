using System.Diagnostics;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
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

    public ChapterGraphView()
    {
        InitializeComponent();

        ReloadButton.Click += (_, _) => UiGuard.Run(_session, "챕터 다시 읽기", Reload);
        OpenFolderButton.Click += (_, _) => UiGuard.Run(_session, "챕터 폴더 열기", OpenFolder);

        ChapterCombo.SelectionChanged += (_, _) =>
        {
            if (_updatingCombo)
            {
                return;
            }

            _selectedChapterId = ChapterCombo.SelectedItem as string;
            Draw();
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

        // 새 노드가 들어갈 판. 활성 파일이 없으면 첫 파일, 그것도 없으면(빈 프로젝트) 반영할
        // 자리가 없으므로 멈추고 말한다 — 조용히 아무 데나 만들지 않는다.
        string? fileId = _session.ActiveFileId ?? _session.Project.Files.FirstOrDefault()?.Id;

        if (fileId is null)
        {
            _session.SetStatus("시나리오 파일이 없어 에피소드를 반영할 자리가 없습니다. 파일을 먼저 만들어 주세요.");
            return;
        }

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

        Draw();
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

        GraphCanvas.Width = layout.Width;
        GraphCanvas.Height = layout.Height;

        // 간선을 먼저 그려야 노드 카드 아래로 깔린다.
        foreach (ChapterEdge edge in model.Edges)
        {
            DrawEdge(model, layout, edge);
        }

        foreach (ChapterEpisode episode in model.Episodes)
        {
            DrawEpisode(model, layout, episode);
        }

        DrawDiagnostics(model);
        DrawFixtureSummary(model);
    }

    private void DrawEdge(ChapterGraphModel model, ChapterGraphLayout layout, ChapterEdge edge)
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

        // 간선의 정체를 시각 요소에 남긴다. 화면 없는 렌더 검증(Gate A)이 "무엇이 그려졌는지"를
        // 색·좌표로 역추론하지 않고 이름으로 확인할 수 있어야 한다.
        line.Tag = EdgeTag(edge);
        GraphCanvas.Children.Add(line);

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

    private void DrawEpisode(ChapterGraphModel model, ChapterGraphLayout layout, ChapterEpisode episode)
    {
        bool hasError = model.EpisodeHasError(episode);

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

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

        // 클릭 = 에피소드 엑셀 열기 (G5). 드래그가 아니다 — 이 뷰에 드래그는 없다.
        card.PointerPressed += (_, _) =>
            UiGuard.Run(_session, "에피소드 열기", () => OpenEpisode(episode.EpisodeId));
        card.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);

        (double x, double y) = layout.Place(episode);
        Canvas.SetLeft(card, x);
        Canvas.SetTop(card, y);
        GraphCanvas.Children.Add(card);
    }

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

    private void DrawFixtureSummary(ChapterGraphModel model)
    {
        ChapterFixture? active = model.Fixtures.FirstOrDefault(fixture => fixture.IsActive);

        PixtureText.Text = model.Fixtures.Count == 0
            ? string.Empty
            : active is null
                ? $"픽스처 {model.Fixtures.Count}개 (활성 없음)"
                : $"활성 픽스처: {active.Name}";
    }

    /// <summary>
    /// 오류·경고·정보를 심각도 순으로, 그 뒤에 에피소드 동기화 보고를. 각 줄이 파일·시트·행·열을
    /// 그대로 말하고, 머리글의 배지가 거부 건수를 든다(G5).
    /// </summary>
    private void DrawDiagnostics(ChapterGraphModel model)
    {
        int errors = model.Diagnostics.Count(item => item.Severity == ChapterDiagnosticSeverity.Error);
        int warnings = model.Diagnostics.Count(item => item.Severity == ChapterDiagnosticSeverity.Warning);
        int rejected = _syncReports.Sum(report => report.RejectionCount);

        string header = errors + warnings == 0
            ? $"검증 보고 — 오류 없음 (알림 {model.Diagnostics.Count}건)"
            : $"검증 보고 — 오류 {errors} · 경고 {warnings}";

        if (_syncReports.Count > 0)
        {
            header += rejected == 0
                ? $" · 동기화 {_syncReports.Count}건 반영"
                : $" · 동기화 거부·경고 {rejected}건";
        }

        DiagnosticsExpander.Header = header;
        DiagnosticsExpander.IsExpanded = errors > 0 || rejected > 0;

        if (model.Diagnostics.Count == 0 && _syncReports.Count == 0)
        {
            DiagnosticsPanel.Children.Add(new TextBlock
            {
                Text = "보고할 것이 없습니다.",
                FontSize = 11,
                Opacity = 0.55
            });

            return;
        }

        foreach (ChapterDiagnostic diagnostic in model.Diagnostics
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
