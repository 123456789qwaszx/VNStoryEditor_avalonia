using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Chapters;
using Vn.Authoring.Editing;
using Vn.Authoring.Model;
using Vn.Authoring.Rendering;
using Vn.Authoring.Results;
using Vn.Authoring.Serialization;

namespace Vn.App;

/// <summary>
/// 앱 셸. 세션을 하나 만들고 그래프 화면과 노드 화면에 같은 것을 물려준다.
///
/// 두 화면은 서로를 모른다. 둘 다 <see cref="AuthoringSession"/>만 보고 있고,
/// 편집은 전부 <see cref="ProjectEditor"/>를 지난다. 그래서 여기에는 화면을 잇는 코드가 없다.
/// </summary>
public partial class MainWindow : Window
{
    private const string BaseTitle = "VnTool";

    private readonly AuthoringSession _session = new();

    /// <summary>검증용 손잡이 — 창이 쓰는 세션 하나. 테스트가 판을 짓고 선택을 옮긴다.</summary>
    internal AuthoringSession SessionProbe => _session;

    // ── 연출 그래프의 곁기둥 (2026-08-22 소유자 — 창의 우측 열에서 그 판 안으로 이사) ──
    //
    // ⚠ XAML이 아니라 여기서 만든다. 자리는 연출 그래프의 것이지만(SetSidePanel) 배선은
    // 셸의 것이다 — 이 편집기들은 세션·무대 프리뷰·발행·선택 전환과 얽혀 있어 뷰로
    // 옮기면 그 배선이 두 벌이 된다. 이름을 XAML 시절 그대로 둔 것은 의도다: 참조하는
    // 자리가 마흔 군데라 이름이 바뀌면 이사와 무관한 diff가 그만큼 생긴다.
    private readonly DialogueNodeEditor DialogueEditor = new();
    private readonly SetNodeEditor SetEditor = new();
    private readonly PresentationNodeEditor PresentationEditor = new();
    private readonly AssetExplorerView AssetExplorer = new() { MaxHeight = 300 };

    private readonly TextBlock EmptyText = new()
    {
        Text = "노드를 선택하면 여기서 편집합니다.",
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Opacity = 0.5
    };

    private LiveOutputService? _liveOutput;
    private readonly DispatcherTimer _autoSaveTimer;
    private bool _restoredRecent;
    /// <summary>왼쪽 챕터 목록의 데이터. 원천은 챕터 그래프 뷰의 읽기 하나다.</summary>
    private IReadOnlyList<ChapterEntry> _chapters = Array.Empty<ChapterEntry>();

    public MainWindow()
    {
        InitializeComponent();

        // 마지막 안전망 (공통 불변식 4). 개별 핸들러의 포획을 놓친 예외가 여기까지 오면
        // 로그와 상태줄에 남기고 앱은 계속 산다 — 클릭 하나가 미저장 원고를 끝내지 않는다.
        // 개별 경로의 포획을 대신하는 장치가 아니라, 새 코드가 실수했을 때의 바닥이다.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            UiGuard.Report(_session, "화면 동작", e.Exception);
        };

        // 오디오 실재생 (W62) — 재생 실패·미지원 보고는 상태줄 하나로 모이고,
        // 셸이 닫히면 소리도 함께 끝난다.
        AudioPreview.Problem = message => _session.SetStatus(message);
        Closed += (_, _) => AudioPreview.StopAll();

        // 곁기둥을 제 화면에 건다 — Attach보다 먼저여야 첫 그리기부터 자리가 서 있다.
        Graph.SetSidePanel(BuildSidePanel());

        Graph.Attach(_session);
        // 왼쪽 챕터 목록의 원천은 챕터 그래프 뷰가 읽은 목록 하나다 — 두 곳이 따로 읽으면
        // 감시·재시도 규칙이 두 벌이 된다. Attach 전에 구독해야 첫 읽기부터 받는다.
        ChapterGraph.EntriesReloaded += entries =>
        {
            _chapters = entries;
            // A계층 어휘(스탯 키·등록 화자)도 같은 읽기 하나에서 온다 — 작가 화면이
            // 계층을 가르는 근거다 (2026-08-17).
            _session.SupplyChapterVocabulary(entries);
            RebuildFileList();
            // 연출 그래프의 철도 배선(T1)도 같은 목록 하나를 본다 — 두 곳이 따로 읽지 않는다.
            Graph.SupplyChapters(entries);
        };
        // 위 드롭다운으로 골라도 왼쪽 목록의 강조가 따라온다 (2026-08-17 소유자 보고) —
        // 목록 클릭이 하던 일(판 활성 + 목록 다시 그리기)을 여기서도 똑같이 한다.
        ChapterGraph.ChapterSelected += chapterId => UiGuard.Run(_session, "챕터 선택", () =>
        {
            _session.SelectFile(_session.EnsureChapterBoard(chapterId));
            RebuildFileList();
        });
        MainTabs.SelectionChanged += (_, _) =>
        {
            ApplyTabChrome();
            EnterStageTab();
        };
        // XAML 기본은 펼침이고 SelectionChanged는 이미 정해진 선택에 오지 않는다 —
        // 여기서 한 번 맞춰 주지 않으면 첫 화면(챕터 그래프)만 어긋난다.
        ApplyTabChrome();

        ChapterGraph.Attach(_session);
        DialogueEditor.Attach(_session);
        SetEditor.Attach(_session);
        PresentationEditor.Attach(_session);
        StagePreview.Attach(_session);
        AssetExplorer.Attach(_session);
        _liveOutput = new LiveOutputService(_session);
        StagePreview.LineMoveRequested += delta =>
        {
            // 선택은 활성 편집기의 것 하나뿐이다 — 프리뷰 창은 그걸 움직일 뿐이다.
            if (PresentationEditor.IsVisible)
            {
                PresentationEditor.MoveStageLine(delta);
            }
            else if (DialogueEditor.IsVisible)
            {
                DialogueEditor.MoveStageLine(delta);
            }
        };
        StagePreview.LineSelectRequested += lineId =>
        {
            // 대본 패널의 대사 행 클릭 (2026-08-20) — 같은 규칙: 활성 편집기의 선택 하나.
            if (PresentationEditor.IsVisible)
            {
                PresentationEditor.SelectStageLineById(lineId);
            }
        };
        StagePreview.DetourResumeRequested += lineId =>
        {
            // detour 복귀 (2026-08-22) — 돌아간 노드의 편집기가 떠난 줄을 짚고 한 줄
            // 나아간다. 다녀온 detour는 경로에서 빠져 있으므로(PopDetour가 표시했다)
            // 그 한 걸음이 곧 "나머지 대본"의 첫 줄이다.
            if (PresentationEditor.IsVisible)
            {
                PresentationEditor.SelectStageLineById(lineId);
                PresentationEditor.MoveStageLine(1);
            }
            else if (DialogueEditor.IsVisible)
            {
                DialogueEditor.SelectStageLineById(lineId);
                DialogueEditor.MoveStageLine(1);
            }
        };
        StagePreview.SceneChosen += EnterPresentationChannel;
        // 작업대 = Inspector (2026-08-21 소유자: "점의 세부 조절창과 연출 편집창이
        // 합쳐지는 게 맞겠네") — 선택 커맨드 하나의 편집 행(연출 편집기) + 수치 조절
        // (무대 뷰)을 연출 편집기가 한 판으로 조합해 프리뷰 탭에 공급한다. 연출 추가는
        // 터미널 우클릭이 띄우는 콘솔이다(검색·종류별 탭·직접 입력 — 2026-08-21 소유자:
        // "콘솔을 띄운다는 느낌으로"). 대사 편집기 화면은 발행 결과 뷰라 작업대가 없다.
        StagePreview.CommandDetailProvider = commandId =>
            PresentationEditor.IsVisible
                ? PresentationEditor.BuildCommandInspector(commandId, StagePreview.BuildSceneInspector)
                : null;
        StagePreview.AddConsoleProvider = (lineId, setup, close) =>
            PresentationEditor.IsVisible
                ? PresentationEditor.BuildAddConsole(lineId, setup, close)
                : null;
        StagePreview.NodeExitRequested = () =>
        {
            // 문서 끝 도달 (W39) — 활성 편집기가 실행 출구를 따라 다음 노드로 전환하면
            // 재생이 이어진다. 전환된 노드의 편집기가 열리며 첫 라인 프리뷰를 민다.
            if (PresentationEditor.IsVisible)
            {
                return PresentationEditor.TryExitPlaybackNode();
            }

            return DialogueEditor.IsVisible && DialogueEditor.TryExitPlaybackNode();
        };
        StagePreview.ManipulationApplied += () =>
        {
            // 직접 조작은 연출 노드의 바인딩을 바꿨다 — 커맨드 행에 즉시 보여야 한다.
            if (PresentationEditor.IsVisible)
            {
                PresentationEditor.Rebuild();
            }
            else if (DialogueEditor.IsVisible)
            {
                // 대사 편집기에서의 프리뷰 선택지 클릭(갈래 선택)도 즉시 보여야 한다 (W47).
                DialogueEditor.RefreshBranchView();
            }
        };
        DialogueEditor.StagePreview = StagePreview;
        PresentationEditor.StagePreview = StagePreview;

        _session.Changed += OnSessionChanged;
        // 상태줄만 바뀐 경우는 셸 갱신 하나로 끝난다 (2026-08-18) — 판을 다시 그리거나
        // 워크북을 다시 읽을 이유가 없다. 그 구분이 없어서 노드가 늘수록 느려졌다.
        _session.StatusChanged += (_, _) =>
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                RefreshShell();
            }
            else
            {
                Dispatcher.UIThread.Post(RefreshShell);
            }
        };
        _session.SelectionChanged += OnSelectionChanged;
        _session.FileGraphStateChanged += OnFileGraphStateChanged;

        ChapterGraph.ChapterAddButton.Click += (_, _) => UiGuard.Run(_session, "새 챕터", ShowAddChapterFlyout);
        NewButton.Click += OnNewClick;
        OpenButton.Click += OnOpenClick;
        SaveButton.Click += OnSaveClick;
        SaveAsButton.Click += OnSaveAsClick;
        UndoButton.Click += (_, _) => _session.Editor.Undo();
        RedoButton.Click += (_, _) => _session.Editor.Redo();
        ExportButton.Click += OnExportClick;
        CsvExportButton.Click += OnCsvExportClick;
        ExportFormatsButton.Click += (_, _) =>
            UiGuard.Run(_session, "내보내기 양식 선택", ShowExportFormatsFlyout);
        OpenExportFolderButton.Click += (_, _) =>
            UiGuard.Run(_session, "내보낸 폴더 열기", OpenExportFolder);

        Opened += OnOpened;

        // 저장 단축키 (W49) — 어디에 포커스가 있어도 Ctrl+S가 저장이다.
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key == Avalonia.Input.Key.S &&
                args.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
            {
                args.Handled = true;
                OnSaveClick(this, new RoutedEventArgs());
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // 10분 자동 저장 (W49) — 저장 경로가 있고 바뀐 것이 있을 때만, 상태줄로 알린다.
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
        _autoSaveTimer.Tick += (_, _) => UiGuard.Run(_session, "자동 저장", () =>
        {
            if (_session.ProjectPath is not null && _session.IsDirty)
            {
                _session.Save();
                _session.SetStatus($"자동 저장했습니다 ({DateTime.Now:HH:mm})");
            }
        });
        _autoSaveTimer.Start();

        RebuildFileList();
        Graph.Rebuild();
        ShowSelectedNode();
        RefreshShell();
    }

    /// <summary>
    /// 고른 탭에 chrome을 맞춘다 — 챕터 그래프에서는 연출 계층의 우측 열과 Yarn/CSV
    /// 내보내기가 접힌다. 생성자와 탭 전환이 같은 계산 하나를 부른다: 시작 화면만
    /// 따로 세우면 두 벌이 되고, 두 벌은 반드시 어긋난다.
    /// </summary>
    /// <summary>
    /// 씬 하나를 연출할 수 있는 상태로 만든다 (2026-08-21) — 대사 발행 → 연출 노드 →
    /// 결과 연결 → 연출 발행까지 한 번에(멱등). 소유자: "미리 다 해둔 다음에 무대
    /// 프리뷰측에서 뭘 할지 고르도록".
    ///
    /// 부르는 곳이 둘이다 — <b>씬 선택기</b>와 <b>무대 프리뷰 탭에 들어오는 것</b>.
    /// 둘은 같은 뜻이라 같은 함수를 지난다(사본 금지).
    /// </summary>
    private void EnterPresentationChannel(string dialogueNodeId)
    {
        PresentationChannelOutcome outcome = _session.Editor.EnsurePresentationChannel(
            dialogueNodeId,
            _session.Definition);

        if (!outcome.Ready)
        {
            // 채널이 못 서도(대사 발행 불가 등) 씬 자체는 보여 준다 — 이유는 상태줄로.
            _session.SetStatus(outcome.Problem ?? "연출 채널을 세울 수 없습니다.");
            _session.Select(dialogueNodeId);
            return;
        }

        _session.Select(outcome.Presentation!.Id);
    }

    /// <summary>
    /// 무대 프리뷰 탭으로 들어왔다 (2026-08-22 소유자 보고: "연출 그래프에서 특정
    /// 에피소드노드를 클릭한채로 프리뷰에 들어올 시에는, 화면을 클릭해도 콘솔이 안 나옴").
    ///
    /// 원인은 화면이 아니라 <b>무엇을 보고 있는가</b>였다: 대사 노드가 선택돼 있으면
    /// 프리뷰가 <b>공급된 발행 결과</b>를 그리고, 발행본은 불변이라 직접 조작이 잠긴다
    /// (<c>DisabledReason</c>). 잠긴 화면에서는 무대를 눌러도 조절창이 열리지 않는다.
    ///
    /// 그래서 <b>탭에 들어오는 것을 씬 선택과 같은 뜻으로</b> 친다 — 연출의 입구는 이
    /// 판이고, 여기 들어왔다는 것은 그 씬을 연출하겠다는 것이다. 이미 채널이 서 있으면
    /// 멱등이라 아무 일도 안 일어난다.
    /// </summary>
    private void EnterStageTab()
    {
        if (!ReferenceEquals(MainTabs.SelectedItem, StageTabItem) ||
            _session.SelectedNode is not DialogueNode dialogue)
        {
            return;
        }

        UiGuard.Run(_session, "연출 채널", () => EnterPresentationChannel(dialogue.Id));
    }

    /// <summary>
    /// 탭마다 다른 상단 chrome. ⚠ <b>곁기둥을 접는 코드는 2026-08-22에 사라졌다</b> —
    /// 챕터 목록과 노드 편집기·에셋 탐색기가 각자 쓰는 화면 안으로 들어가면서
    /// "어느 탭에서 보이나"가 자리로 정해졌다. 여기 남은 것은 이름이 겹치는 단추들뿐이다:
    /// 챕터 툴바의 [내보내기](진행 JSON)와 상단 Yarn/CSV 내보내기가 한 화면에 둘이면
    /// 어느 쪽인지 헷갈린다.
    /// </summary>
    private void ApplyTabChrome()
    {
        bool chapterMode = ReferenceEquals(MainTabs.SelectedItem, ChapterTabItem);

        ExportButton.IsVisible = !chapterMode;

        // ⏸ [CSV 내보내기…]와 [양식…]은 <b>임시로</b> 내렸다 (2026-08-25 소유자).
        //    지우지 않고 접어 두는 이유: 뒤의 것(OnCsvExportClick·ShowExportFormatsFlyout·
        //    LiveOutputService의 양식 선택)은 그대로 살아 있고 라이브 출력이 계속 그
        //    선택을 따르므로, 코드를 걷으면 되살릴 때 두 벌이 된다. 되돌리는 것은
        //    아래 두 줄을 `!chapterMode`로 되돌리는 것뿐이다.
        CsvExportButton.IsVisible = false;
        ExportFormatsButton.IsVisible = false;
    }

    /// <summary>
    /// 연출 그래프의 곁기둥 한 판 — 위는 노드 편집기 넷이 겹쳐 선 자리(선택 하나만
    /// 보인다), 아래는 에셋 탐색기다. 탐색기는 제 머리에 접기 토글을 이미 갖고 있다.
    /// </summary>
    private Control BuildSidePanel()
    {
        var editors = new Panel
        {
            Children = { DialogueEditor, SetEditor, PresentationEditor, EmptyText }
        };

        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        Grid.SetRow(editors, 0);
        Grid.SetRow(AssetExplorer, 1);
        grid.Children.Add(editors);
        grid.Children.Add(AssetExplorer);
        return grid;
    }

    /// <summary>
    /// 최근 프로젝트 복원은 편의 기능이다. 실패해도 빈 창은 그대로 쓸 수 있어야 한다.
    /// 이 핸들러는 async void라 예외가 새면 잡아 줄 곳이 없고 곧장 프로세스를 죽인다.
    /// </summary>
    private void OnOpened(object? sender, EventArgs e)
    {
        if (_restoredRecent)
        {
            return;
        }

        _restoredRecent = true;

        try
        {
            if (AppSettingsService.LoadRecentProject() is { } recent)
            {
                _session.Open(recent);
            }
        }
        catch (Exception exception)
        {
            StartupLog.TryWrite("최근 프로젝트 복원", exception);
            _session.SetStatus(
                $"마지막에 열었던 프로젝트를 열지 못했습니다. '열기…'로 다시 시도해 주세요. ({exception.Message})");
        }
    }

    private void OnSessionChanged(object? sender, ProjectChangedEventArgs e)
    {
        void Apply()
        {
            ProjectRefreshPlan plan = ProjectRefreshPlanner.Plan(e.Kind, _session.SelectedNode);

            if (plan.RebuildGraph)
            {
                Graph.Rebuild();
            }
            else if (plan.RefreshGraphPositions)
            {
                // 좌표만 바뀌었다. 카드를 다시 만들면 드래그가 끊긴다.
                Graph.RefreshPositions();
            }

            if (plan.RebuildInspector)
            {
                RebuildInspector();
            }
            else if (plan.RefreshPreview)
            {
                // 화자·대사 내용 변경은 편집 컨트롤을 유지해야 하지만 Script Preview는
                // 현재 모델을 곧바로 반영해야 한다. Preview만 다시 합성한다.
                DialogueEditor.RefreshPreview();
            }

            if (plan.RefreshStagePreview)
            {
                PresentationEditor.RefreshStagePreview();
            }

            RefreshShell();
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        ShowSelectedNode();
        Graph.HighlightSelection();
        RefreshShell();
    }

    private void OnFileGraphStateChanged(object? sender, FileGraphStateChangedEventArgs e)
    {
        void Apply()
        {
            RebuildFileList();

            if (e.ExpandedFilesChanged)
            {
                Graph.Rebuild();
            }

            RefreshShell();
        }

        // 체크박스나 라디오 버튼의 RoutedEvent가 끝나기 전에 파일 목록을 통째로
        // 교체하면 클릭한 컨트롤을 자기 이벤트 안에서 제거하게 된다. 다음 UI turn에서 갱신한다.
        Dispatcher.UIThread.Post(Apply);
    }

    // ── 파일 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 새 챕터 (챕터 v2, G-1 v2) — chapters/ 폴더에 §3.1 규격 워크북을 만들고 그 판을 연다.
    /// 스탯 시트는 game.definition의 변수로 채운다. Id는 사람이 정한다(자동 발명 금지).
    /// </summary>
    private void ShowAddChapterFlyout()
    {
        var panel = new StackPanel { Spacing = 4, MinWidth = 220 };

        var name = new TextBox
        {
            PlaceholderText = "챕터 Id (예: ch06) — 파일 이름이 됩니다",
            FontSize = 11
        };
        panel.Children.Add(name);

        var flyout = new Flyout { Content = panel };

        var create = new Button { Content = "만들기", HorizontalAlignment = HorizontalAlignment.Stretch };
        create.Click += (_, _) => UiGuard.Run(_session, "새 챕터", () =>
        {
            string chapterId = name.Text?.Trim() ?? string.Empty;

            if (chapterId.Length == 0)
            {
                _session.SetStatus("챕터 Id를 적어 주세요.");
                return;
            }

            string? folder = ChapterLibrary.FolderFor(_session.ProjectPath);

            if (folder is null)
            {
                _session.SetStatus("프로젝트를 먼저 저장해야 챕터 폴더 자리가 정해집니다.");
                return;
            }

            var stats = _session.Definition.Variables
                .Select(variable => (variable.Name, variable.Name))
                .ToList();

            if (!ChapterWorkbookWriter.EnsureChapterWorkbook(folder, chapterId, stats))
            {
                _session.SetStatus($"챕터 '{chapterId}'가 이미 있습니다.");
                return;
            }

            _session.SelectFile(_session.EnsureChapterBoard(chapterId));
            ChapterGraph.RefreshFromDisk();
            ChapterGraph.SelectChapter(chapterId);
            flyout.Hide();
            _session.SetStatus($"챕터 '{chapterId}'를 만들었습니다: {System.IO.Path.Combine(folder, chapterId + ".xlsx")}");
        });
        panel.Children.Add(create);

        flyout.ShowAt(ChapterGraph.ChapterAddButton);
        name.Focus();
    }

    /// <summary>
    /// 왼쪽 챕터 목록 (챕터 v2). 챕터 클릭 = 그 챕터의 판을 활성으로 + 챕터 그래프 선택.
    /// 챕터가 없는 판은 목록에 없다 — 소유자 확정("완전 교체"). 기존 프로젝트는 챕터를
    /// 만들어 옮긴다.
    /// </summary>
    private void RebuildFileList()
    {
        {
            ChapterGraph.ChapterListHost.Children.Clear();

            foreach (ChapterEntry entry in _chapters)
            {
                string chapterId = entry.ChapterId;
                string? boardId = _session.Project.Files.FirstOrDefault(file =>
                    string.Equals(file.Name, chapterId, StringComparison.Ordinal))?.Id;
                bool isActive = boardId is not null &&
                    string.Equals(_session.ActiveFileId, boardId, StringComparison.Ordinal);

                string subtitle = entry.Model is null
                    ? "읽기 실패 — 검증 보고 참조"
                    : $"에피소드 {entry.Model.Episodes.Count}개" +
                      (entry.HasErrors ? " · ⚠ 오류" : string.Empty);

                var row = new Border
                {
                    Padding = new Thickness(8, 6),
                    CornerRadius = new CornerRadius(5),
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                    Background = isActive
                        ? new SolidColorBrush(Color.FromArgb(24, 37, 99, 235))
                        : Brushes.Transparent,
                    Child = new StackPanel
                    {
                        Spacing = 1,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = chapterId,
                                FontWeight = FontWeight.SemiBold,
                                TextTrimming = TextTrimming.CharacterEllipsis
                            },
                            new TextBlock
                            {
                                Text = subtitle,
                                FontSize = 10,
                                Opacity = 0.55,
                                Foreground = entry.HasErrors ? Brushes.IndianRed : null,
                                TextTrimming = TextTrimming.CharacterEllipsis
                            }
                        }
                    }
                };

                row.PointerPressed += (_, args) =>
                {
                    if (args.GetCurrentPoint(row).Properties.IsRightButtonPressed)
                    {
                        ShowChapterContextFlyout(row, chapterId);
                        args.Handled = true;
                        return;
                    }

                    // 한 번 클릭 = 선택만. 엑셀은 더블클릭으로 연다 (2026-08-16 소유자) —
                    // 챕터를 훑어보는 것과 편집하러 여는 것은 다른 동작이고, 클릭마다
                    // 엑셀이 뜨면 판을 옮겨 다닐 수가 없다. 노드(에피소드)와 같은 규칙이다.
                    UiGuard.Run(_session, "챕터 선택", () =>
                    {
                        _session.SelectFile(_session.EnsureChapterBoard(chapterId));
                        ChapterGraph.SelectChapter(chapterId);
                    });
                };

                row.DoubleTapped += (_, args) =>
                {
                    args.Handled = true;
                    UiGuard.Run(_session, "챕터 엑셀 열기",
                        () => ChapterGraph.OpenChapterWorkbook(chapterId));
                };

                ChapterGraph.ChapterListHost.Children.Add(row);
            }

            if (_chapters.Count == 0)
            {
                ChapterGraph.ChapterListHost.Children.Add(new TextBlock
                {
                    Text = "챕터가 없습니다. ＋로 만들거나 chapters/ 폴더에 워크북을 넣으세요.",
                    Margin = new Thickness(6, 10),
                    Opacity = 0.55,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }
    }

    /// <summary>
    /// 챕터 행 우클릭 — 이름 바꾸기와 <b>제거</b>. 둘 다 워크북·대본 폴더·판·조건 배관을
    /// 함께 옮기거나 걷는다(<see cref="ChapterRenamer"/> · <see cref="ChapterDeleter"/>).
    ///
    /// ⛔ 제거는 2026-08-25에 생겼다. 그전에는 "폴더에서 사람이 지우세요"였는데, 그 길은
    /// <b>절반만 지운다</b> — 파일은 사라져도 판과 노드가 프로젝트에 남아 이름이 겹치는
    /// 번들로 뒤늦게 터졌다. 사람이 손으로 못 지우는 쪽이 남는 것이 문제였다.
    /// </summary>
    private void ShowChapterContextFlyout(Control anchor, string chapterId)
    {
        var panel = new StackPanel { Spacing = 4, MinWidth = 220 };

        var name = new TextBox { Text = chapterId, FontSize = 11 };
        panel.Children.Add(name);

        var flyout = new Flyout { Content = panel };

        var rename = new Button { Content = "이름 바꾸기", HorizontalAlignment = HorizontalAlignment.Stretch };
        rename.Click += (_, _) => UiGuard.Run(_session, "챕터 이름 바꾸기", () =>
        {
            string newId = name.Text?.Trim() ?? string.Empty;

            if (newId.Length == 0 || string.Equals(newId, chapterId, StringComparison.Ordinal))
            {
                flyout.Hide();
                return;
            }

            // ⚠ 개명은 <b>네 자리를 함께</b> 옮기는 한 동작이다(워크북·대본 폴더·판·조건
            // 배관). 셸이 그중 둘만 알던 시절에 나머지 둘이 뒤처졌다 — 그 주인은 이제
            // ChapterRenamer 하나다(2026-08-24 소유자 보고).
            ChapterRenamer.Result result =
                ChapterRenamer.Rename(_session.Editor, _session.ProjectPath, chapterId, newId);

            if (!result.Renamed)
            {
                _session.SetStatus(result.Failure!);
                return;
            }

            ChapterGraph.RefreshFromDisk();
            ChapterGraph.SelectChapter(newId);
            flyout.Hide();

            // 무엇이 함께 옮겨졌는지 말한다 — 개명이 파일을 옮기는 일이라, 조용하면
            // 사람이 폴더를 열어 확인하게 된다.
            string carried = string.Join(" · ",
                new[]
                {
                    result.EpisodesMoved ? "대본 폴더" : null,
                    result.SupplyRenamed ? "조건 노드" : null
                }.Where(item => item is not null));

            _session.SetStatus(
                $"챕터 '{chapterId}' → '{newId}'로 바꿨습니다" +
                (carried.Length > 0 ? $"({carried}도 함께)." : ".") +
                (result.StaleExport is { } stale
                    ? $" ⚠ 옛 이름의 내보내기 '{stale}'가 남아 있습니다 — 지우고 다시 내보내 주세요."
                    : " 내보낸 JSON이 있었다면 다시 내보내 주세요."));
        });
        panel.Children.Add(rename);
        panel.Children.Add(MenuSeparator());
        panel.Children.Add(BuildChapterRemoveButton(flyout, chapterId));

        flyout.ShowAt(anchor);
        name.SelectAll();
        name.Focus();
    }

    /// <summary>
    /// [챕터 제거] — <b>두 번 눌러야 지워진다</b>. 첫 누름은 무엇이 함께 사라지는지를
    /// 그 자리에서 말하고, 그 문장을 읽은 손이 한 번 더 누른다.
    ///
    /// 확인 창을 따로 띄우지 않는 이유는 창이 사실을 <b>덮기</b> 때문이다 — 이 판에는
    /// 방금 읽던 챕터 이름과 이름 칸이 함께 있어서, 여기서 묻는 편이 무엇을 지우는지
    /// 더 분명하다. 되돌릴 자리(<c>.bak</c>·되돌리기)도 같은 문장에서 말한다.
    /// </summary>
    private Control BuildChapterRemoveButton(Flyout flyout, string chapterId)
    {
        var remove = new Button
        {
            Content = "챕터 제거…",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Foreground = new SolidColorBrush(Color.FromRgb(190, 60, 60))
        };

        var caution = new TextBlock
        {
            Text = $"'{chapterId}'의 대본과 연출 그래프의 노드가 함께 사라집니다. " +
                   "원고는 .bak으로 남고, 판은 되돌리기로 돌아옵니다.",
            FontSize = 10,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            Margin = new Thickness(0, 2, 0, 0)
        };

        bool armed = false;

        remove.Click += (_, _) =>
        {
            if (!armed)
            {
                armed = true;
                remove.Content = "정말 제거 — 한 번 더";
                caution.IsVisible = true;
                return;
            }

            UiGuard.Run(_session, "챕터 제거", () =>
            {
                ChapterDeleter.Result result =
                    ChapterDeleter.Delete(_session.Editor, _session.ProjectPath, chapterId);

                if (!result.Deleted)
                {
                    _session.SetStatus(result.Failure!);
                    return;
                }

                ChapterGraph.RefreshFromDisk();
                RefreshShell();
                flyout.Hide();

                // 되돌릴 자리를 <b>이름으로</b> 말한다 — "지웠습니다"만 오면 사람은 원고가
                // 사라진 줄 알고, 그 오해는 폴더를 열어 봐야만 풀린다.
                string kept = string.Join(" · ",
                    new[] { result.WorkbookBackup, result.EpisodesBackup }
                        .Where(item => item is not null));

                _session.SetStatus(
                    $"챕터 '{chapterId}'를 지웠습니다(연출 노드 {result.NodesRemoved}개도 함께)." +
                    (kept.Length > 0 ? $" 원고는 남겨 뒀습니다: {kept}" : string.Empty) +
                    (result.StaleExport is { } stale
                        ? $" ⚠ 내보낸 '{stale}'가 남아 있습니다 — 지우지 않으면 게임이 " +
                          "없는 챕터를 계속 싣습니다."
                        : string.Empty));
            });
        };

        return new StackPanel { Spacing = 2, Children = { remove, caution } };
    }

    private async void OnNewClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            _session.NewProject();

            // 새 프로젝트는 곧장 한 번 저장한다 (W49) — 저장돼야 프로젝트 폴더가 확정되고
            // 에셋 폴더·기본 튜닝 살림(W48)이 준비된다.
            await SaveAsAsync();

            if (_session.ProjectPath is null)
            {
                _session.SetStatus("저장을 건너뛰었습니다 — 저장하면 에셋 폴더와 기본 튜닝이 준비됩니다.");
            }
        }
        catch (Exception exception)
        {
            Report("새 프로젝트", exception);
        }
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            IStorageProvider? storage = GetTopLevel(this)?.StorageProvider;

            if (storage is null || !storage.CanOpen)
            {
                _session.SetStatus("이 환경에서는 파일 선택 창을 열 수 없습니다.");
                return;
            }

            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "VnTool 프로젝트 열기",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { ProjectFileType() }
                });

            if (files.Count > 0)
            {
                _session.Open(files[0].Path.LocalPath);
            }
        }
        catch (Exception exception)
        {
            Report("프로젝트 열기", exception);
        }
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_session.ProjectPath is null)
            {
                await SaveAsAsync();
                return;
            }

            _session.Save();
            RefreshShell();
        }
        catch (Exception exception)
        {
            Report("저장", exception);
        }
    }

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await SaveAsAsync();
        }
        catch (Exception exception)
        {
            Report("저장", exception);
        }
    }

    private async Task SaveAsAsync()
    {
        IStorageProvider? storage = GetTopLevel(this)?.StorageProvider;

        if (storage is null || !storage.CanSave)
        {
            _session.SetStatus("이 환경에서는 저장 창을 열 수 없습니다.");
            return;
        }

        IStorageFile? file = await storage.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "VnTool 프로젝트 저장",
                SuggestedFileName = "project" + ProjectManifestJson.FileExtension,
                FileTypeChoices = new[] { ProjectFileType() }
            });

        if (file is not null)
        {
            _session.Save(file.Path.LocalPath);
            RefreshShell();
        }
    }

    // ── 내보내기 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 대사 노드들을 골라 런타임 .yarn 트리오로 내보낸다. 짝이 되는 연출은 명시적 합성이
    /// 아니라 <see cref="NodeExportResolver"/>가 연출 공급 연결에서 계산한다.
    /// 선언 파일은 폴더당 하나만 나온다.
    /// </summary>
    /// <summary>
    /// 내보내기 양식 선택 (X13). 프로젝트에 저장되고, 내보내기 버튼들이 이 선택을 따른다.
    /// </summary>
    private void ShowExportFormatsFlyout()
    {
        var panel = new StackPanel { Spacing = 4, MinWidth = 180 };
        ExportFormatSelection current = _session.Project.ExportFormats;

        panel.Children.Add(new TextBlock
        {
            Text = "내보내기에서 산출할 양식",
            FontSize = 11,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        });

        void AddToggle(string label, bool value, Action<ExportFormatSelection, bool> assign)
        {
            var toggle = new CheckBox { Content = label, IsChecked = value, FontSize = 11 };

            toggle.IsCheckedChanged += (_, _) =>
            {
                ExportFormatSelection next = _session.Project.ExportFormats.Clone();
                assign(next, toggle.IsChecked == true);
                _session.Editor.SetExportFormats(next);
            };

            panel.Children.Add(toggle);
        }

        AddToggle("Yarn 트리오 (Story·Set·Pres + 선언)", current.YarnTrio, (f, v) => f.YarnTrio = v);
        AddToggle("Script CSV (번역·녹음)", current.ScriptCsv, (f, v) => f.ScriptCsv = v);
        AddToggle("Review CSV (기획 검수)", current.ReviewCsv, (f, v) => f.ReviewCsv = v);
        AddToggle("Direction CSV (연출 테이블)", current.DirectionCsv, (f, v) => f.DirectionCsv = v);

        // 라이브 출력 폴더 (X12c, D-1) — 지정하면 편집마다 자동 재합성·재저장된다.
        panel.Children.Add(new TextBlock
        {
            Text = $"라이브 출력: {_session.Project.OutputPath ?? "(미지정 — 자동 산출 없음)"}",
            FontSize = 10,
            Opacity = 0.7,
            Margin = new Avalonia.Thickness(0, 8, 0, 0),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = "클라우드 동기화 폴더는 쓰기 충돌이 날 수 있습니다.",
            FontSize = 9,
            Opacity = 0.5
        });

        var pickOutput = new Button { Content = "라이브 출력 폴더 지정…", FontSize = 11 };
        pickOutput.Click += async (_, _) => await UiGuard.RunAsync(_session, "출력 폴더 지정", async () =>
        {
            if (StorageProvider is not { CanPickFolder: true } storage)
            {
                return;
            }

            IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = "라이브 출력 폴더", AllowMultiple = false });

            if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } picked)
            {
                return;
            }

            string stored = picked;

            if (_session.ProjectPath is { } projectPath)
            {
                string relative = Path.GetRelativePath(Path.GetDirectoryName(projectPath) ?? projectPath, picked);

                if (!Path.IsPathRooted(relative))
                {
                    stored = relative;
                }
            }

            _session.Editor.SetOutputPath(stored);
            _liveOutput?.WriteNow();
        });
        panel.Children.Add(pickOutput);

        var clearOutput = new Button { Content = "라이브 출력 해제", FontSize = 11 };
        clearOutput.Click += (_, _) => _session.Editor.SetOutputPath(null);
        panel.Children.Add(clearOutput);

        AddOrphanOutputs(panel);

        new Flyout { Content = panel, Placement = PlacementMode.Bottom }.ShowAt(ExportFormatsButton);
    }

    /// <summary>
    /// 고아 출력 목록 (K1 ②안) — 노드를 지우거나 이름을 바꾸면 옛 파일이 출력 폴더에 남고,
    /// 유니티가 폴더를 통째로 읽으면 그 옛 대사가 재생된다. VnTool은 <b>지우지 않고 보여 준다</b>:
    /// 출력 폴더는 사용자의 폴더이지 도구의 소유물이 아니다.
    /// </summary>
    private void AddOrphanOutputs(StackPanel panel)
    {
        OrphanOutputScan scan = _liveOutput?.Orphans ?? OrphanOutputScan.Empty;

        if (scan.Orphans.Count == 0 && scan.Note is null)
        {
            return;
        }

        panel.Children.Add(new TextBlock
        {
            Text = $"낡은 산출 파일 {scan.Orphans.Count}개",
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            FontSize = 11,
            Margin = new Avalonia.Thickness(0, 10, 0, 0)
        });

        panel.Children.Add(new TextBlock
        {
            Text = "지금 프로젝트가 만들지 않는 파일입니다. VnTool은 지우지 않습니다 — "
                + "탐색기에서 직접 지우세요.",
            FontSize = 9,
            Opacity = 0.6,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        foreach (OrphanOutput orphan in scan.Orphans)
        {
            panel.Children.Add(new TextBlock
            {
                Text = orphan.Source == OrphanOutputSource.Recorded
                    ? $"• {orphan.FileName}"
                    : $"• {orphan.FileName} (기록 없음 — 이름 형식만 같습니다)",
                FontSize = 10,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
        }

        if (scan.Note is { } note)
        {
            panel.Children.Add(new TextBlock
            {
                Text = note,
                FontSize = 9,
                Opacity = 0.6,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
        }
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!_session.Project.ExportFormats.YarnTrio)
            {
                _session.SetStatus("양식 선택에서 Yarn 트리오가 꺼져 있습니다. [양식…]에서 켜세요.");
                return;
            }

            IReadOnlyList<LiveComposition> exports = await PickNodeExportsAsync();

            if (exports.Count == 0)
            {
                return;
            }

            List<YarnBundle> bundles = exports.Select(export => export.Bundle!).ToList();

            if (await PickExportFolderAsync(".yarn 트리오를 내보낼 폴더") is not { } folder)
            {
                return;
            }

            var written = new List<string>(YarnBundleEmitter.WriteBundles(bundles, folder));

            // 커스텀 곡선(W67 후속) — 번들이 @이름으로 참조하는 곡선을 런타임 스키마로
            // 함께 낸다. 곡선이 없으면 파일도 없다(런타임의 무음 0개 경로).
            if (YarnBundleEmitter.WriteCurves(_session.Project.EaseCurves, folder) is { } curvesPath)
            {
                written.Add(curvesPath);
            }

            int warnings = bundles.Sum(bundle => bundle.Problems.Count) +
                exports.Sum(export => export.Warnings.Count);
            _session.SetStatus(
                $"{written.Count}개 파일을 내보냈습니다: " +
                string.Join(", ", written.Select(Path.GetFileName)) +
                (warnings > 0 ? $" · 경고 {warnings}건" : string.Empty));
            RefreshShell();
        }
        catch (Exception exception)
        {
            Report("내보내기", exception);
        }
    }

    /// <summary>
    /// 진행 JSON이 나가는 <c>exported/</c>를 연다 (2026-08-25).
    ///
    /// 이 파일들은 <b>사람 손을 안 기다리고 저절로 나간다</b>(2026-08-17). 그래서 사람이
    /// 할 일은 만드는 것이 아니라 <b>가서 꺼내 오는 것</b>뿐인데, 그 자리를 몰라 못 꺼내
    /// 가는 것이 실제 걸림돌이었다 — 비개발자 작가에게 건네는 준비의 일부다.
    ///
    /// ⚠ 폴더를 <b>대신 만들지 않는다.</b> 없다는 것은 아직 한 챕터도 안 나갔다는 뜻이고,
    /// 빈 폴더를 열어 주면 "나갔는데 비었다"로 읽힌다 — 사유를 말하는 편이 낫다.
    /// </summary>
    private void OpenExportFolder()
    {
        if (_session?.ProjectPath is not { } projectPath)
        {
            _session?.SetStatus("프로젝트를 먼저 저장해야 내보낼 자리가 정해집니다.");
            return;
        }

        string folder = Path.Combine(
            Path.GetDirectoryName(projectPath)!, ChapterExportService.ExportFolderName);

        if (!Directory.Exists(folder))
        {
            _session.SetStatus(
                $"아직 나간 진행 JSON이 없습니다 — 검증을 통과한 챕터가 생기면 저절로 " +
                $"여기 나갑니다: {folder}");
            return;
        }

        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
    }

    /// <summary>
    /// 번역·녹음 / 기획 검수 / 연출 테이블 CSV 3종을 내보낸다.
    /// .yarn과 같은 입구(대사 노드 + 연출 공급 연결)를 쓰되, 파일 형식만 다르다.
    /// </summary>
    private async void OnCsvExportClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!_session.Project.ExportFormats.AnyCsv)
            {
                _session.SetStatus("양식 선택에서 CSV가 전부 꺼져 있습니다. [양식…]에서 켜세요.");
                return;
            }

            IReadOnlyList<LiveComposition> exports = await PickNodeExportsAsync();

            if (exports.Count == 0)
            {
                return;
            }

            if (await PickExportFolderAsync("CSV를 내보낼 폴더") is not { } folder)
            {
                return;
            }

            var written = new List<string>();

            foreach (LiveComposition export in exports)
            {
                CsvBundle bundle = CsvBundleExporter.Export(
                    export.WorkingDialogue!,
                    export.WorkingPresentation,
                    _session.Project,
                    _session.Definition);
                // 선택한 양식만 산출된다 (X13).
                written.AddRange(CsvBundleExporter.WriteTo(bundle, folder, _session.Project.ExportFormats));
            }

            _session.SetStatus(
                $"CSV {written.Count}개를 내보냈습니다: " +
                string.Join(", ", written.Select(Path.GetFileName)));
            RefreshShell();
        }
        catch (Exception exception)
        {
            Report("CSV 내보내기", exception);
        }
    }

    /// <summary>
    /// 내보낼 대사 노드를 고른다. 하나뿐이면 바로 그것을 쓴다.
    /// 내보낼 수 없는 노드(발행 없음 등)는 목록에서 이유와 함께 비활성으로 보인다.
    /// </summary>
    /// <summary>
    /// 내보낼 노드를 고른다. 발행은 게이트가 아니다 (D-2) — 합성은 라이브 출력과 같은
    /// <see cref="LiveNodeComposer"/>(현재 작업 상태의 Freeze)를 지나므로 바이트가 같다.
    /// </summary>
    private async Task<IReadOnlyList<LiveComposition>> PickNodeExportsAsync()
    {
        List<LiveComposition> candidates = _session.Project.EnumerateNodes()
            .OfType<DialogueNode>()
            .Select(node => LiveNodeComposer.Compose(
                _session.Project, node.Id, _session.Definition, DateTimeOffset.UtcNow))
            .ToList();

        List<LiveComposition> exportable = candidates.Where(item => item.CanWrite).ToList();

        if (exportable.Count == 0)
        {
            LiveComposition? firstBlocked = candidates.FirstOrDefault(item => item.BlockingProblems.Count > 0);
            _session.SetStatus(firstBlocked is null
                ? "내보낼 대사 노드가 없습니다."
                : $"내보낼 수 있는 노드가 없습니다 — {firstBlocked.DialogueNodeName}: {firstBlocked.BlockingProblems[0]}");
            return Array.Empty<LiveComposition>();
        }

        if (exportable.Count == 1)
        {
            return new[] { exportable[0] };
        }

        // 챕터를 실어 나른다 — 목록의 글줄과 거르개가 같은 값 하나를 본다.
        List<ExportPick> picks = exportable
            .Select(item => new ExportPick(item, ChapterLabelOf(item)))
            .OrderBy(pick => pick.Chapter, StringComparer.Ordinal)
            .ThenBy(pick => pick.Composition.DialogueNodeName, StringComparer.Ordinal)
            .ToList();

        var list = new ListBox { SelectionMode = SelectionMode.Multiple };

        // ⛔ 거르개 (2026-08-25 소유자: "내보낼 대사노드 선택하는 팝업창에 챕터 단위
        //    필터기능을 추가해"). 챕터가 여럿이면 목록이 수십 줄이 되는데, 사람이
        //    내보내려는 것은 <b>대개 한 챕터</b>다. 거르개가 없으면 남의 챕터를 하나씩
        //    풀어야 하고, 하나 빠뜨리면 그냥 같이 나간다 — 나간 뒤에는 안 보인다.
        var chapters = new List<string> { AllChapters };
        chapters.AddRange(picks.Select(pick => pick.Chapter).Distinct(StringComparer.Ordinal));

        var filter = new ComboBox
        {
            ItemsSource = chapters,
            SelectedIndex = 0,
            MinWidth = 180
        };

        // 고른 챕터만 남기고 <b>전부 고른 채로</b> 둔다 — 거르개는 "무엇을 볼까"가 아니라
        // "무엇을 낼까"이므로, 거른 뒤 다시 전체 선택을 시키면 두 번 시키는 것이 된다.
        void ApplyFilter()
        {
            string chosen = filter.SelectedItem as string ?? AllChapters;

            list.ItemsSource = string.Equals(chosen, AllChapters, StringComparison.Ordinal)
                ? picks
                : picks.Where(pick => string.Equals(pick.Chapter, chosen, StringComparison.Ordinal)).ToList();

            list.SelectAll();
        }

        filter.SelectionChanged += (_, _) => ApplyFilter();
        ApplyFilter();

        var confirm = new Button
        {
            Content = "내보내기",
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                new TextBlock { Text = "챕터", VerticalAlignment = VerticalAlignment.Center },
                filter
            }
        };

        DockPanel.SetDock(header, Dock.Top);

        var dialog = new Window
        {
            Title = "내보낼 대사 노드 선택 (여러 개 선택 가능)",
            Width = 520,
            Height = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel
            {
                Margin = new Thickness(12),
                Children =
                {
                    header,
                    Docked(confirm, Dock.Bottom),
                    new ScrollViewer { Content = list }
                }
            }
        };

        var picked = new List<LiveComposition>();

        // ⚠ 자리(index)가 아니라 <b>고른 것 자체</b>를 읽는다. 거르개가 목록을 갈아 끼우면
        //   자리는 원본 목록과 어긋나므로, 자리로 되짚으면 남의 노드를 내보낸다.
        confirm.Click += (_, _) =>
        {
            foreach (object? item in list.SelectedItems ?? Array.Empty<object>())
            {
                if (item is ExportPick pick)
                {
                    picked.Add(pick.Composition);
                }
            }

            dialog.Close();
        };

        await dialog.ShowDialog(this);
        return picked;
    }

    /// <summary>거르개의 "전부" 자리. 챕터 이름과 겹치지 않게 괄호를 쓴다.</summary>
    private const string AllChapters = "(전체)";

    /// <summary>
    /// 그 노드가 어느 챕터의 것인가. 판 이름이 곧 챕터다(챕터=판 1:1).
    /// 어디에도 안 속한 노드는 <b>숨기지 않고</b> 제 자리를 준다 — 안 보이면 못 고친다.
    /// </summary>
    private static string ChapterLabelOf(LiveComposition composition) =>
        composition.Bundle?.ChapterId is { Length: > 0 } chapter ? chapter : "(챕터 없음)";

    /// <summary>목록 한 줄. <c>ToString</c>이 곧 보이는 글줄이다.</summary>
    private sealed record ExportPick(LiveComposition Composition, string Chapter)
    {
        public override string ToString()
        {
            string pair = Composition.WorkingPresentation is not null
                ? "현재 대사 + 연출"
                : "현재 대사 (연출 공급 없음)";
            string warning = Composition.Warnings.Count > 0
                ? $" · 경고 {Composition.Warnings.Count}"
                : string.Empty;

            // 챕터를 <b>앞에</b> 세운다 — 거르개를 (전체)로 두고 훑을 때, 눈이 왼쪽 끝에서
            // 묶음을 읽는다. 뒤에 붙이면 노드 이름 길이에 따라 자리가 들쭉날쭉해진다.
            return $"[{Chapter}] {Composition.DialogueNodeName} · {pair}{warning}";
        }
    }

    private async Task<string?> PickExportFolderAsync(string title)
    {
        IStorageProvider? storage = GetTopLevel(this)?.StorageProvider;

        if (storage is null || !storage.CanPickFolder)
        {
            _session.SetStatus("이 환경에서는 폴더 선택 창을 열 수 없습니다.");
            return null;
        }

        IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            });

        return folders.Count == 0 ? null : folders[0].Path.LocalPath;
    }

    private static Control Docked(Control control, Dock dock)
    {
        control.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(control, dock);
        return control;
    }

    private static FilePickerFileType ProjectFileType()
    {
        return new FilePickerFileType("VnTool 프로젝트")
        {
            Patterns = new[] { "*.vnproject.json", "*.vnstory.json", "*.json" }
        };
    }

    // ── 화면 ────────────────────────────────────────────────────────────────



    private static Control MenuSeparator() => new Border
    {
        Height = 1,
        Margin = new Thickness(4, 2),
        Background = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128))
    };

    // [편집 자료] 현황판(RebuildResourcePanel)은 2026-08-21에 사라졌다 (소유자) —
    // 대본·발행 결과·연출 공급을 읽기 전용으로 나열하던 판인데, 발행·배선이 자동이
    // 된 뒤로는 볼 이유가 없다. 대본은 노드 카드가, 짝은 내보내기가 말한다.

    private void ShowSelectedNode()
    {
        StoryNode? node = _session.SelectedNode;

        DialogueEditor.IsVisible = node is DialogueNode;
        SetEditor.IsVisible = node is SetNode;
        PresentationEditor.IsVisible = node is PresentationNode;
        EmptyText.IsVisible = node is null;
        EmptyText.Text = "노드를 선택하면 여기서 편집합니다.";

        if (node is DialogueNode)
        {
            DialogueEditor.Show(node.Id);
        }
        else if (node is SetNode)
        {
            SetEditor.Show(node.Id);
        }
        else if (node is PresentationNode)
        {
            PresentationEditor.Show(node.Id);
        }

        // 무대 프리뷰는 대사·연출 편집기만 채운다. 다른 노드에서는 빈 상태로 돌린다.
        if (node is not DialogueNode && node is not PresentationNode)
        {
            StagePreview.Show(null);
        }

        // 씬 선택기가 현재 씬을 따라온다 — 연출 노드는 공급 대상(대사 노드)으로 옮겨 읽는다.
        StagePreview.SetCurrentScene(node switch
        {
            DialogueNode dialogue => dialogue.Id,
            PresentationNode presentation => _session.Project.Links.FirstOrDefault(link =>
                    link.Kind == NodeLinkKind.PresentationSupply &&
                    link.IsEnabled &&
                    string.Equals(link.SourceNodeId, presentation.Id, StringComparison.Ordinal))
                ?.TargetNodeId,
            _ => null
        });
    }

    /// <summary>
    /// 구조가 바뀌었을 때 오른쪽 화면을 다시 만든다.
    /// 선택된 노드가 그대로면 같은 노드를 다시 그리고, 바뀌었으면 그쪽으로 옮긴다.
    /// </summary>
    private void RebuildInspector()
    {
        StoryNode? node = _session.SelectedNode;

        if (node is DialogueNode && DialogueEditor.NodeId == node.Id)
        {
            DialogueEditor.Rebuild();
            return;
        }

        if (node is SetNode && SetEditor.NodeId == node.Id)
        {
            SetEditor.Rebuild();
            return;
        }

        if (node is PresentationNode && PresentationEditor.NodeId == node.Id)
        {
            PresentationEditor.Rebuild();
            return;
        }

        ShowSelectedNode();
    }

    private void RefreshShell()
    {
        string name = _session.ProjectPath is null
            ? _session.Project.Title
            : ProjectDisplayName(_session.ProjectPath);

        ProjectNameText.Text = name;
        ProjectPathText.Text = _session.ProjectPath ?? "저장되지 않음";
        ChapterGraph.ChapterSummaryText.Text = _session.ActiveFile is { } activeFile
            ? $"현재: {activeFile.Name}"
            : "현재 작업 파일 없음";
        StatusText.Text = _session.StatusMessage;

        bool dirty = _session.IsDirty;
        DirtyText.Text = dirty ? "● 저장되지 않은 변경" : string.Empty;

        UndoButton.IsEnabled = _session.Editor.CanUndo;
        RedoButton.IsEnabled = _session.Editor.CanRedo;

        Title = dirty ? $"* {BaseTitle} — {name}" : $"{BaseTitle} — {name}";
    }

    private static string ProjectDisplayName(string path)
    {
        string fileName = Path.GetFileName(path) ?? string.Empty;
        return fileName.EndsWith(ProjectManifestJson.FileExtension, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^ProjectManifestJson.FileExtension.Length]
            : Path.GetFileNameWithoutExtension(fileName) ?? fileName;
    }

    private void Report(string action, Exception exception)
    {
        StartupLog.TryWrite(action, exception);
        _session.SetStatus($"{action}하지 못했습니다. {exception.Message}");
        RefreshShell();
    }
}
