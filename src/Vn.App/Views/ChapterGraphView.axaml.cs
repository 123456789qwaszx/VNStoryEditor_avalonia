using System.Diagnostics;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using IoPath = System.IO.Path;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vn.App.Services;
using Vn.Authoring.Chapters;
using Vn.Authoring.Definition;

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

    /// <summary>판 수준 경고 (2단계 가드레일) — 자유 노드의 Tier 2 스탯 set 등.</summary>
    private readonly List<ChapterDiagnostic> _boardWarnings = new();

    /// <summary>선택된 챕터의 구조 검증 + 도달성 증명 결과 (G7). 워크북을 읽을 때 갱신된다.</summary>
    private ChapterValidationResult? _validation;

    /// <summary>`스탯변화` 줄 편집기 둘 — 간선 패널의 것과 에피소드 노드 폼 안의 것.</summary>
    private readonly StatChangeEditor _edgeStats = new();
    private readonly StatChangeEditor _formStats = new();

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

    /// <summary>
    /// 판을 실제로 다시 만든 횟수. <see cref="ValidationComputeCount"/>와 같은 종류의 창이다 —
    /// 그리기 자체는 싸지만, 다시 만드는 순간 <b>사람이 누르고 있던 카드가 사라진다</b>.
    /// </summary>
    internal int CanvasDrawCount { get; private set; }

    /// <summary>
    /// 챕터를 고른다 — <b>어디서 골랐든 이 길 하나를 지난다</b> (왼쪽 목록 클릭 · 위
    /// 드롭다운 · 코드).
    ///
    /// 2026-08-17 소유자 보고 둘의 뿌리가 여기였다. 드롭다운은 이 길을 안 지나고
    /// <c>Draw()</c>만 불렀다: ① 검증을 다시 안 돌려 <b>이전 챕터의 결과</b>로 그렸고 —
    /// 그래서 도달 불가 빨간 테두리가 남고 도착 스탯이 안 보였다(Ctrl+S가 세션을 흔들어
    /// 다시 읽히면 그제서야 맞았다) ② 판을 활성으로 바꾸지 않아 <b>왼쪽 챕터 목록의
    /// 강조가 따라오지 않았다</b>.
    /// </summary>
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

        // 고른 챕터의 선택은 이전 챕터의 것이 아니다 — 들고 있으면 없는 간선을 가리킨다.
        _selectedEpisodeId = null;
        _selectedEdgeKey = null;
        HideEdgeForm();

        // 잠금도 이전 챕터의 것이 아니다 (2026-08-24) — 엑셀이 A를 잡고 있을 때 B로
        // 옮기면 B는 잠기지 않은 채로 보여야 한다. ⚠ 그 물음을 여기 두지 않는다:
        // 바로 아래 Draw()의 마지막 줄이 이미 묻는다
        // (RefreshPropertyPanel → ApplyEditability → RefreshLockBanner). 한 줄 더 두면
        // 같은 물음이 두 곳에 산다. 대신 규칙을 테스트가 붙든다
        // (`ChapterEditGateTests.다른_챕터로_옮기면_그_챕터의_잠금을_다시_묻는다`) —
        // Draw()의 그 줄이 사라지면 여기가 아니라 그 테스트가 먼저 말한다.
        Validate();
        Draw();

        // 이 챕터의 대본이 자기 판의 노드로 서 있도록 따라잡는다 — 챕터를 처음 고르는
        // 순간이 곧 그 판을 처음 보는 순간이다.
        SyncEpisodes();

        // 그 챕터의 판을 활성으로 — 왼쪽 목록의 강조가 이 값을 본다.
        ChapterSelected?.Invoke(chapterId);
    }

    /// <summary>
    /// 챕터가 골라졌다. 셸(MainWindow)이 듣고 그 챕터의 판을 활성으로 바꾼다 — 판을
    /// 만드는 일은 셸의 몫이라(<c>EnsureChapterBoard</c>) 이 뷰가 직접 하지 않는다.
    /// </summary>
    internal event Action<string>? ChapterSelected;

    // ── 챕터 목록 자리 (2026-08-22 소유자 — 창 맨 왼쪽 열에서 이 기둥 위로 이사) ──────
    //
    // 자리만 이 뷰가 내주고 <b>내용은 셸(MainWindow)이 짓는다</b>. 목록의 클릭 하나가
    // 세션 전환(EnsureChapterBoard)·엑셀 열기·우클릭 메뉴와 얽혀 있어 여기로 옮기면
    // 같은 배선이 두 벌이 된다 — 그 사본이야말로 이 코드베이스가 가장 경계하는 것이다.

    /// <summary>챕터 줄이 쌓이는 자리.</summary>
    internal Panel ChapterListHost => ChapterListPanel;

    /// <summary>[＋] 새 챕터 — 셸이 클릭을 받고 플라이아웃의 과녁으로도 쓴다.</summary>
    internal Button ChapterAddButton => AddChapterButton;

    /// <summary>머리글 아래 한 줄 — 지금 활성인 판의 요약.</summary>
    internal TextBlock ChapterSummaryText => ChapterSummary;

    public ChapterGraphView()
    {
        InitializeComponent();

        // ⛔ [다시 읽기] 단추는 2026-08-24에 없앴다 (소유자: 한 번도 안 썼다). 감시자가
        // 붙은 뒤로 화면이 스스로 따라오므로 손잡이가 할 일이 남지 않았다. <see cref="Reload"/>
        // 자체는 그대로다 — 부르는 곳이 사람 손에서 감시자와 챕터 전환으로 옮겨갔을 뿐이다.
        OpenFolderButton.Click += (_, _) => UiGuard.Run(_session, "챕터 폴더 열기", OpenFolder);

        // [화자] 탭 — 프로젝트 전체의 캐스트 (2026-08-23). 엔터로도 더한다: 이름을 여럿
        // 적을 때 손이 마우스로 갔다 오지 않는다.
        SpeakerAddButton.Click += (_, _) => UiGuard.Run(_session, "화자 추가", AddSpeaker);
        SpeakerNameBox.KeyDown += (_, args) => CommitOnEnter(args, AddSpeaker);
        SpeakerCharacterIdBox.KeyDown += (_, args) => CommitOnEnter(args, AddSpeaker);

        ChapterCombo.SelectionChanged += (_, _) =>
        {
            if (_updatingCombo)
            {
                return;
            }

            // 드롭다운도 왼쪽 목록 클릭과 같은 길을 지난다 (2026-08-17) — 여기서
            // Draw()만 부르던 것이 "빨간 테두리가 안 사라진다"의 정체였다.
            if (ChapterCombo.SelectedItem is string picked)
            {
                UiGuard.Run(_session, "챕터 선택", () => SelectChapter(picked));
            }
        };


        // 편집 (G-2 v2) — 전부 엑셀 셀에 써지고, 저장 감시가 다시 읽어 화면이 따라온다.
        // 2026-08-16 소유자 — [개명]·[적용] 단추 폐지: Id는 Enter로 개명하고, 조건 콤보는
        // 고르는 순간 저장된다. 편집은 늘 열려 있고, 엑셀이 그 파일을 잡은 동안만 닫힌다.
        IdBox.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                e.Handled = true;
                UiGuard.Run(_session, "에피소드 개명", RenameSelectedEpisode);
            }
        };
        // 판 다루기 — 휠 확대·축소, 가운데 단추 끌어 이동. 휠은 <b>터널</b>로 받는다:
        // 스크롤뷰가 먼저 먹어 버리면 휠이 배율이 아니라 그냥 스크롤이 된다.
        GraphScroll.AddHandler(
            PointerWheelChangedEvent, OnGraphWheel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        GraphScroll.PointerPressed += OnGraphPanPressed;
        GraphScroll.PointerMoved += OnGraphPanMoved;
        GraphScroll.PointerReleased += OnGraphPanReleased;
        ZoomResetButton.Click += (_, _) => ApplyZoom(1, null);

        // 스탯변화 줄 편집기를 두 자리에 꽂는다 — 간선 패널과 에피소드 노드의 선택지 폼.
        EdgeStatsHost.Children.Add(_edgeStats);
        EdgeFormStatsHost.Children.Add(_formStats);

        // 관문은 v8에서 간선으로 옮겨 갔다 — 에피소드 콤보는 감춰져 있고 저장도 안 한다.
        AddNextEdgeButton.Click += (_, _) => UiGuard.Run(_session, "선택지 추가", AddChoiceSlotFromPanel);
        AddEdgeButton.Click += (_, _) => UiGuard.Run(_session, "간선 연결·수정", SubmitEdgeForm);
        DeleteEpisodeButton.Click += (_, _) => UiGuard.Run(_session, "에피소드 삭제", DeleteSelectedEpisode);
        AddEpisodeButton.Click += (_, _) => UiGuard.Run(_session, "에피소드 추가", AddEpisodeFromToolbar);
        EdgeDeleteButton.Click += (_, _) => UiGuard.Run(_session, "간선 삭제", DeleteSelectedEdge);

        // 간선 패널은 고르는 순간 저장된다 (2026-08-17 소유자: "굳이 적용을 누르지 않아도
        // 바로 반영되도록"). 에피소드 패널의 개명이 Enter로 확정되는 것과 같은 감각이다 —
        // [적용]이라는 문턱이 하나 더 있으면 "고쳤는데 왜 그대로지"가 생긴다.
        //
        // 글자 칸만 초점을 잃을 때 낸다. 자판 하나마다 워크북을 열면 엑셀 파일을 쉼 없이
        // 두드리고, 그 사이에 들어온 파일 사건이 칸을 다시 채워 쓰던 글을 끊는다.
        EdgeLabelEditBox.SelectionChanged += (_, _) => AutoSaveEdge();
        EdgeVisibleCombo.SelectionChanged += (_, _) => AutoSaveEdge();
        EdgeConditionCombo.SelectionChanged += (_, _) => AutoSaveEdge();
        EdgeHideCheck.IsCheckedChanged += (_, _) => AutoSaveEdge();
        EdgeLockedMsgBox.LostFocus += (_, _) => AutoSaveEdge();
        EdgeLockedMsgBox.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                e.Handled = true;
                AutoSaveEdge();
            }
        };
        _edgeStats.Changed += (_, _) => AutoSaveEdge();
        CopyDiagnosticsButton.Click += async (_, _) =>
            await UiGuard.RunAsync(_session, "보고 복사", CopyDiagnosticsAsync);

        // 대사 접기 — 토글이 곧 제목이다(상자 없이). 삼각형이 상태를 말한다
        // (▸ 접힘 · ▾ 펼침).
        DialogueToggle.IsCheckedChanged += (_, _) =>
        {
            bool open = DialogueToggle.IsChecked == true;
            DialoguePreviewText.IsVisible = open;
            DialogueToggle.Content = open ? "▾  대사" : "▸  대사";
        };

        // 펼친 채로 시작한다 (2026-08-24 소유자: "대사 미리보기는, 펼쳐둔 걸 디폴트로").
        // ⚠ XAML에서 IsChecked를 켜지 않는다 — 그러면 위 핸들러가 붙기 <b>전에</b> 켜져
        // 글자와 본문이 접힌 모양 그대로 남는다. 여기서 켜야 셋이 한 번에 맞는다.
        DialogueToggle.IsChecked = true;

        // 처음 보이는 탭은 [편집]이다 (2026-08-16 소유자) — 손이 가는 곳은 지금 고른
        // 하나이지 챕터 전체 표가 아니다. 그 뒤로는 사람이 고른 탭이 그대로 유지된다.
        RightTabs.SelectedItem = EditTab;

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
        DetachSession();   // 두 번 붙이면 구독도 감시도 두 벌이 된다

        _session = session;
        _sessionChanged = (_, _) => QueueReload();
        session.Changed += _sessionChanged;
        WatchAndReload();
    }

    /// <summary>세션 구독을 끊을 수 있도록 들고 있는다 — 람다를 바로 걸면 뗄 수가 없다.</summary>
    private EventHandler<Vn.Authoring.Editing.ProjectChangedEventArgs>? _sessionChanged;

    /// <summary>
    /// 세션에서 손을 뗀다 — 구독을 끊고 감시자를 닫는다.
    ///
    /// <b>왜 필요한가</b> (2026-08-18): 이 뷰는 붙기만 하고 떨어질 줄을 몰랐다. 앱에서는
    /// 뷰가 프로세스와 수명이 같아 티가 안 나지만, 테스트에서는 한 어셈블리가 <b>디스패처
    /// 하나</b>를 나눠 쓴다. 창을 안 닫고 끝낸 테스트의 뷰가 그대로 살아 있고, 그 감시자는
    /// 이미 지워진 임시 폴더를 본다.
    ///
    /// 그 다음이 나쁘다 — 250ms 디바운스가 <b>타이머 스레드에서</b> 깨어나 없어진 디스패처에
    /// <c>Post</c>를 하면 <c>NullReferenceException</c>이 나고, 그 자리는 잡을 사람이 없어
    /// <b>프로세스가 내려간다.</b> 죽는 순간 돌고 있던 테스트가 실패로 찍히므로 매번 다른
    /// 이름이 나왔고, 혼자 돌리면 통과했다.
    ///
    /// 앱에서는 뷰가 하나뿐이라 새는 일이 없지만, <b>끊을 수 있다는 것 자체가 규격</b>이다 —
    /// 붙이는 길만 있고 떼는 길이 없으면 수명은 언제나 우연에 맡겨진다.
    /// </summary>
    internal void DetachSession()
    {
        if (_session is not null && _sessionChanged is not null)
        {
            _session.Changed -= _sessionChanged;
        }

        _sessionChanged = null;
        _session = null;

        _watcher?.Dispose();
        _watcher = null;
        _episodeWatcher?.Dispose();
        _episodeWatcher = null;
    }

    /// <summary>이미 예약된 재읽기가 있는가. 한 번의 UI 차례에 하나만 돈다.</summary>
    private bool _reloadQueued;

    /// <summary>
    /// 재읽기 예약 — <b>몰려 오는 변경을 한 번으로 합친다</b> (2026-08-18).
    ///
    /// 동기화 한 번이 프로젝트 변경을 수십 개 낸다(에피소드마다 노드·줄이 붙는다).
    /// 예전에는 그 하나하나가 <see cref="WatchAndReload"/>를 예약했고, 그 한 번이
    /// 챕터 워크북 전부를 다시 열어 읽고 진행 JSON을 쓰고 판을 통째로 다시 그렸다.
    /// 노드 60개에서 <b>123번</b> 돌아 58초가 됐다 — 마지막 한 번 말고는 전부 버려질
    /// 그림이었다.
    ///
    /// 예약 표시는 실제로 돌기 직전에 내린다: 재읽기가 도는 동안 들어온 변경은
    /// 다음 차례를 새로 예약한다(놓치지 않는다).
    /// </summary>
    private void QueueReload()
    {
        if (_reloadQueued)
        {
            return;
        }

        _reloadQueued = true;

        Dispatcher.UIThread.Post(() =>
        {
            _reloadQueued = false;
            WatchAndReload();
        });
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

        bool episodesFolderChanged =
            !string.Equals(_episodeWatcher?.Folder, episodes, StringComparison.OrdinalIgnoreCase);

        if (episodesFolderChanged)
        {
            StartWatchingEpisodes(episodes);
        }

        Reload();

        // 켤 때 한 번은 밀린 저장을 따라잡는다 — 감시는 "저장 순간"만 잡으므로, 툴이 꺼진
        // 사이(시트·엑셀에서) 적힌 대사는 이게 없으면 영원히 안 불려온다. 폴더가 바뀐
        // 첫 판에만 돈다: 상태줄 갱신이 세션 Changed를 울려 여기로 되돌아와도(같은 폴더)
        // 다시 돌지 않아 맴돌이가 없다.
        //
        // <b>감시자가 못 붙었어도 돈다</b> (2026-08-17) — 대본 폴더가 아직 없으면 감시를
        // 걸 곳이 없어 `_episodeWatcher`가 null인데, 예전에는 그때 동기화까지 건너뛰었다.
        // 그래서 <b>첫 에피소드가 영영 대본을 못 받았다</b>: 폴더는 대본이 생겨야 나고
        // 대본은 동기화가 만드는데, 그 동기화가 폴더를 기다린 것이다(서로를 기다리는 매듭).
        // 이제 동기화가 첫 대본을 만들고, 그 김에 감시도 붙는다.
        //
        // ⚠ <b>에피소드 목록이 달라졌으면 폴더가 그대로여도 돈다</b> (2026-08-22 소유자
        // 보고: "챕터그래프에서 에피소드를 추가했는데 연출그래프에 반영이 안 돼 …
        // 더블클릭해서 엑셀을 열어야 그제야"). 노드를 세우는 것은 동기화뿐인데 그것이
        // <b>폴더가 바뀔 때만</b> 돌았다 — 그래서 엑셀 파일이 생겨 감시자가 우는 날에야
        // 노드가 섰다. 툴의 [＋ 에피소드]도, 엑셀에서 직접 더한 행도 같은 구멍이었다.
        if (episodesFolderChanged || EpisodeSetChanged())
        {
            SyncEpisodes();
        }
    }

    /// <summary>지난 동기화가 본 에피소드 목록의 지문 — 같은 목록이면 다시 돌지 않는다.</summary>
    private string? _syncedEpisodeSignature;

    /// <summary>
    /// 고른 챕터의 에피소드 <b>목록</b>이 지난 동기화 이후 달라졌는가. 확인하면서 기록한다.
    ///
    /// ⚠ 재읽기마다 무조건 동기화하지 않는 이유: <see cref="SyncEpisodes"/>는 화자·조건
    /// 어휘를 밀어 넣느라 <b>에피소드 워크북을 전부 열어 본다</b>. 저장 한 번마다 그 값을
    /// 치르면 §성능 규칙("고정은 시간이 아니라 일의 횟수로 건다")이 무너진다. 값을 부르는
    /// 것은 <b>달라진 목록</b>뿐이다 — 추가·삭제·개명이 곧 노드가 서고 지고 바뀌는 일이다.
    /// </summary>
    private bool EpisodeSetChanged()
    {
        ChapterEntry? entry = _entries.FirstOrDefault(item => item.ChapterId == _selectedChapterId);
        string signature = MakeEpisodeSignature(entry);

        if (string.Equals(signature, _syncedEpisodeSignature, StringComparison.Ordinal))
        {
            return false;
        }

        _syncedEpisodeSignature = signature;
        return true;
    }

    private string MakeEpisodeSignature(ChapterEntry? entry) =>
        entry?.Model is null
            ? $"{_selectedChapterId}:"
            : $"{_selectedChapterId}:" + string.Join(
                "|",
                entry.Model.Episodes
                    .Select(episode => episode.EpisodeId)
                    .OrderBy(id => id, StringComparer.Ordinal));

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
            () => Dispatcher.UIThread.Post(
                () => UiGuard.Run(_session, "챕터 워크북 반영", ReloadIfDiskChanged)),
            debounce: null,
            // 엑셀이 이 챕터를 <b>열거나 닫는</b> 순간 (2026-08-24). 저장이 아니므로 다시
            // 읽지 않는다 — 내용은 그대로다. 바뀐 것은 <b>툴이 쓸 수 있는가</b>뿐이라
            // 잠금만 다시 묻는다. 이 알림이 없으면 툴은 쓰기가 거부되고 나서야 알았다.
            //
            // ⚠ 이 알림이 <b>유일한 길은 아니다.</b> 엑셀이 잠금 파일을 지우는 것과 파일
            // 핸들을 놓는 것 사이에 틈이 있으면(디바운스 250ms로도 못 덮는 경우) 여기서
            // 물은 답이 아직 "잠김"일 수 있다. 그래도 갇히지 않는다: 그리기·패널 갱신이
            // 모두 RefreshLockBanner를 지나므로 다음 클릭 한 번이면 풀린다.
            onLockChanged: () => Dispatcher.UIThread.Post(
                () => UiGuard.Run(_session, "엑셀 잠금 확인", RefreshLockState)));
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
            () => Dispatcher.UIThread.Post(
                () => UiGuard.Run(_session, "에피소드 반영", SyncEpisodesIfDiskChanged)));
    }

    /// <summary>감시자가 챕터 폴더에서 깨울 때 도는 길. 테스트가 진짜 파일 사건을 기다리지 않고 이 자리를 친다.</summary>
    internal void ReloadIfDiskChanged() => IfDiskChanged(Reload);

    /// <summary>감시자가 대본 폴더에서 깨울 때 도는 길.</summary>
    internal void SyncEpisodesIfDiskChanged() => IfDiskChanged(SyncEpisodes);

    /// <summary>마지막으로 우리가 읽은 디스크의 지문. 감시자가 깨울 때 이것과 견준다.</summary>
    private string _diskFingerprint = string.Empty;

    /// <summary>
    /// <b>파일이 만져졌다는 신호가 곧 내용이 바뀌었다는 뜻은 아니다</b> (2026-08-18).
    ///
    /// 감시자는 <b>우리가 방금 쓴 저장도 똑같이 잡는다.</b> 툴이 쓴 자리는 이미 그 자리에서
    /// <see cref="QueueReload"/>로 화면을 맞췄으므로, 250ms 뒤 감시자가 들고 오는 것은
    /// <b>같은 그림을 한 번 더 그리라는 주문</b>이다. v11에서 챕터를 처음 열 때마다 `연출`
    /// 칸을 되쓰게 되면서 이 두 번째 그리기가 상시가 됐다.
    ///
    /// 그리기 자체는 싸다. 비싼 것은 <b>다시 만든다</b>는 사실이다 — 그 순간 사람이 누르고
    /// 있던 카드가 파괴되어 더블클릭의 둘째 탭이 다른 인스턴스에 떨어지고 드래그 캡처가
    /// 죽은 카드에 걸린다. 이 클래스의 클릭 테스트가 원래 못 박은 결함 그대로이고, 실제로
    /// 그 테스트가 <b>불규칙하게</b> 실패하고 있었다: 감시자가 250ms 뒤 아무 때나 끼어들어
    /// 눌린 손 밑에서 판을 갈아 치웠기 때문이다.
    ///
    /// 그래서 감시자가 깨울 때는 디스크의 지문을 먼저 본다. 남이 엑셀에서 저장한 것은
    /// 지문이 달라 그대로 통과하고, 우리가 쓴 것은 이미 반영돼 있어 조용히 끝난다.
    /// </summary>
    private void IfDiskChanged(Action work)
    {
        string now = DiskFingerprint();

        if (string.Equals(now, _diskFingerprint, StringComparison.Ordinal))
        {
            return;
        }

        work();
    }

    /// <summary>
    /// 감시 중인 두 폴더(`chapters/`·`episodes/`)에 있는 워크북 전부의 지문.
    ///
    /// 파일을 읽어 해싱한다 — 쓴 시각은 초 단위로 뭉개지는 파일 시스템이 있어(FAT·일부 SMB)
    /// "고쳤는데 안 바뀐 것으로 보이는" 쪽으로 틀린다. 화면이 낡은 채로 남는 실패는 여기서
    /// 가장 비싸므로, 값을 치르고 내용을 본다. 읽기는 파싱·증명·그리기보다 한참 싸다.
    ///
    /// <b>규칙은 <see cref="WorkbookFolderFingerprint"/>가 갖는다</b> — 2026-08-24에 여기서
    /// 나갔다. ⛔ 여기 있을 때 못 읽은 파일을 <c>'?'</c>라는 <em>상수</em>로 적고 있었고,
    /// 그래서 엑셀이 쥐고 있는 동안의 저장이 전부 묻혔다(소유자 보고: "엑셀을 닫으니까
    /// 그제서야 반영이 된다"). 화면 안에 있어서 화면 없이는 그 결함을 시험할 수 없었다.
    /// </summary>
    private string DiskFingerprint() => WorkbookFolderFingerprint.Of(
        ChapterLibrary.FolderFor(_session?.ProjectPath),
        EpisodeLibrary.FolderFor(_session?.ProjectPath));

    /// <summary>
    /// 선택된 챕터의 에피소드 워크북 전부를 대사노드로 반영한다.
    ///
    /// 감시자는 어느 파일이 바뀌었는지 말하지 않으므로(저장 한 번이 이벤트 여러 개라 어차피
    /// 뭉개진다) 전부 다시 돈다 — 바뀌지 않은 워크북은 "변경 없음"으로 끝나 비용이 잔잔하다.
    /// </summary>
    internal void SyncEpisodes()
    {
        _syncReports.Clear();
        _boardWarnings.Clear();

        if (_session is null)
        {
            return;
        }

        ChapterEntry? entry = _entries.FirstOrDefault(item => item.ChapterId == _selectedChapterId);

        // 이 목록으로 돌았다고 적어 둔다 — 뒤이은 재읽기가 같은 목록이면 다시 안 돈다.
        _syncedEpisodeSignature = MakeEpisodeSignature(entry);

        // ⛔ <b>고른 챕터만 돌면 안 된다</b> (2026-08-24 소유자 보고: "챕터그래프에서
        // 대사노드의 엑셀을 열어서 고칠 경우, 연출그래프의 동일한 엑셀노드에 반영이 안 되네").
        //
        // 연출 그래프는 <b>모든 판의 노드를 함께</b> 보여 준다. 그런데 반영은 고른 챕터
        // 하나만 돌았으므로, 다른 챕터의 대사노드는 그 챕터를 다시 고르기 전까지 영영
        // 낡은 글을 들고 있었다. 챕터가 둘 이상인 프로젝트에서는 늘 그랬다.
        //
        // ⚠ 이것이 <b>지금 감당되는</b> 이유: 같은 날 워크북 파싱을 내용 해시로 기억하게
        // 했다(`WorkbookParseCache`). 안 바뀐 대본은 해시만 재고 지나가므로, 챕터를 전부
        // 도는 값이 예전의 한 챕터보다 싸다. 그 전이었다면 이 고침은 못 했다.
        foreach (ChapterEntry other in SyncTargets(entry))
        {
            RunEpisodeSync(other);
        }

        // 아래는 <b>화면</b>의 몫이다 — 고른 챕터가 없어도 내보내기·검증·그리기는 돈다.
        AfterEpisodeSync();
    }

    /// <summary>
    /// 이번에 따라잡을 챕터들 — 고른 챕터와 <b>이미 판이 선</b> 챕터 전부.
    ///
    /// 판이 없는 챕터는 대사노드도 없으므로 낡을 것이 없다. 그런 챕터까지 돌면
    /// <see cref="ProjectEditor.EnsureChapterBoard"/>가 <b>판을 미리 만들어</b> 아무도
    /// 안 연 챕터의 판이 프로젝트에 쌓인다 — 고치려던 것보다 큰 변화다.
    /// </summary>
    private IEnumerable<ChapterEntry> SyncTargets(ChapterEntry? selected)
    {
        if (selected is not null)
        {
            yield return selected;
        }

        if (_session is null)
        {
            yield break;
        }

        foreach (ChapterEntry candidate in _entries)
        {
            if (candidate.ChapterId == selected?.ChapterId || candidate.Model is null)
            {
                continue;
            }

            // 판 이름 = ChapterId (챕터=판 1:1, G-1 v2).
            if (_session.Project.Files.Any(file =>
                    string.Equals(file.Name, candidate.ChapterId, StringComparison.Ordinal)))
            {
                yield return candidate;
            }
        }
    }

    /// <summary>챕터 하나를 반영하고 그 결과를 보고 더미에 쌓는다.</summary>
    private void RunEpisodeSync(ChapterEntry entry)
    {
        if (_session is null)
        {
            return;
        }

        // 화자·조건 드롭다운을 대본 워크북에 (2026-08-16 → 2026-08-23). 지문이 같으면
        // 파일을 하나도 열지 않으므로 매 동기화마다 불러도 값이 없다. ⚠ 반영보다 **앞**이다 —
        // 이미 있는 워크북이 새 어휘를 받은 뒤에 읽혀야 한다(새로 만드는 것은 만들 때 받는다).
        PushVocabularyToEpisodes();

        // 프로젝트가 실제로 바뀌었는지 재는 눈금 (2026-08-24 성능) — 아래 방송의 근거다.
        long revisionBefore = _session.Editor.Revision;

        // 순서와 정책은 저작이 갖는다 (2026-08-23에 이 파일에서 나갔다). 여기 남은 것은
        // **결과를 화면에 옮기는 일**뿐이다 — 상태줄·감시자·다시 그리기.
        EpisodeSyncRun run = EpisodeSyncRunner.Run(
            _session.Editor, _session.Definition, _session.ProjectPath, entry, _entries);

        _syncReports.AddRange(run.Reports);
        _boardWarnings.AddRange(run.BoardWarnings);

        foreach (string notice in run.Notices)
        {
            _session.SetStatus(notice);
        }

        if (run.WorkbooksCreated)
        {
            StartWatchingEpisodes(EpisodeLibrary.FolderFor(_session.ProjectPath));
        }

        // 무언가 <b>실제로 바뀌었으면</b> 열려 있는 편집 화면(줄 목록·그래프)을 다시
        // 만들게 알린다 — 대사 수정은 "타이핑 보호" 경로로 전달되어 화면이 옛 줄을 그대로
        // 들고 있었다(실사례).
        //
        // ⚠ 근거가 `run.Applied > 0`이었는데 <b>틀린 눈금이었다</b> (2026-08-24).
        // `Applied`는 "반영을 돌렸다"는 뜻이지 "뭔가 달라졌다"가 아니다 — 같은 워크북을
        // 두 번 돌려도 참이다(`EpisodeSyncServiceTests`가 그것을 못 박아 두었다).
        // 그래서 아무것도 안 바뀐 동기화가 <b>매번</b> 전체 다시 그리기를 방송했고,
        // 감시자가 250ms 뒤 깨어날 때마다 사람이 타이핑하던 칸이 파괴됐다.
        if (_session.Editor.Revision != revisionBefore)
        {
            _session.NotifyExternalScriptChange();
        }

        if (run.StatusMessage is { } message)
        {
            _session.SetStatus(message);
        }
    }

    /// <summary>
    /// 챕터를 <b>전부 돈 뒤</b> 한 번만 하는 일 — 내보내기·증명·그리기·지문.
    ///
    /// ⚠ 챕터마다 하면 안 된다. 내보내기와 증명은 <see cref="ChapterExportService"/>가
    /// 이미 전 챕터를 도는 일이고, 그리기는 화면 하나를 다시 만드는 일이다 — 챕터 수만큼
    /// 되풀이하면 그 값이 그대로 곱해진다.
    /// </summary>
    private void AfterEpisodeSync()
    {
        // ⚠ 반영이 판을 바꿨으면 **다시 내보낸다** (2026-08-23). `Reload()` 안의
        // `AutoExport()`는 동기화보다 먼저 돌므로, 엑셀에서 방금 더한 에피소드의 대사노드는
        // 그때 아직 없다 — 저작 관문(`DialogueEntryNodeMissing`)이 그것을 옳게 거부하고,
        // 여기서 다시 내지 않으면 **노드가 선 뒤에도 거부가 남는다.**
        //
        // 되돌이는 없다: 감시자는 `chapters/`와 `episodes/`를 보고 내보내기는 `exported/`에
        // 쓴다. 값도 잔잔하다 — 증명은 캐시가 받고, 글이 같은 파일은 다시 쓰지 않는다.
        AutoExport();

        // 에피소드가 바뀌면 스탯 증감량도 바뀐다 — 도달성을 다시 증명한다.
        Validate();
        Draw();

        // 동기화는 쓴다 — 첫 대본 워크북. 그 저장이 250ms 뒤 감시자로 되돌아오는데,
        // 화면은 이미 맞춰졌다. 여기서 지문을 찍어 그 되돌이를 끊는다.
        _diskFingerprint = DiskFingerprint();
    }

    // ── [화자] 탭 = 프로젝트의 캐스트 (2026-08-23) ──────────────────────────
    //
    // 소유자: "챕터 엑셀을 눌러보면, 엑셀 내 어떤 것에서도 화자를 사용하지 않는다 … 화자는
    // 툴 내부에서, 직접 정의해서 쓰는 게 맞는 것이였다." 확인해 보니 그대로였다 — `조건`
    // 시트는 간선의 표시조건·해금조건이 실제로 가리키지만 `화자` 시트는 어느 시트도, 검증도,
    // 도달성 증명도, 내보내기도 안 봤다. 툴이 대본 드롭다운에 쓰려고 남의 파일에 얹어 둔
    // 사전이었고, 그래서 <b>챕터마다 따로 적어야 하는 값</b>만 치렀다.
    //
    // 이제 등록 창구는 이 탭 하나이고 값은 `game.definition.json`의 speakers에 산다
    // (초상화 매핑과 같은 배열 — 화자의 집이 하나라는 뜻이다).

    /// <summary>[화자] 탭을 지금 정의 파일의 목록으로 다시 그린다.</summary>
    private void RebuildSpeakerTab()
    {
        SpeakerListPanel.Children.Clear();

        if (_session is null)
        {
            return;
        }

        IReadOnlyList<SpeakerSpec> speakers = _session.Definition.Speakers;

        if (speakers.Count == 0)
        {
            SpeakerListPanel.Children.Add(new TextBlock
            {
                Text = "아직 없습니다. 위에 이름을 적고 [＋ 추가]를 누르면 모든 챕터의 대본에서 " +
                       "그 화자를 고를 수 있습니다.",
                FontSize = 10,
                Opacity = 0.55,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        for (int index = 0; index < speakers.Count; index++)
        {
            SpeakerListPanel.Children.Add(BuildSpeakerRow(index, speakers[index]));
        }
    }

    /// <summary>
    /// 화자 한 줄 — `[이름][캐릭터키][✕]`. 고치면 그 자리에서 정의 파일에 저장된다
    /// ([적용] 없음 — 챕터 편집 탭과 같은 규칙, 2026-08-17 소유자).
    /// </summary>
    private Control BuildSpeakerRow(int index, SpeakerSpec speaker)
    {
        var name = new TextBox { Text = speaker.Name, FontSize = 12 };
        var characterId = new TextBox
        {
            Text = speaker.CharacterId,
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 12,
            PlaceholderText = "캐릭터키"
        };

        void Commit()
        {
            List<SpeakerSpec> edited = _session!.Definition.Speakers.ToList();

            if (index >= edited.Count)
            {
                return; // 그 사이 목록이 바뀌었다 — 다음 그리기가 정본이다.
            }

            string typed = (name.Text ?? string.Empty).Trim();

            // 이름을 지워 빈칸으로 만드는 것은 삭제가 아니다 — 삭제는 ✕ 하나뿐이다.
            // 빈 이름을 저장하면 목록에서 조용히 사라져 되돌릴 손잡이가 없어진다.
            if (typed.Length == 0)
            {
                name.Text = edited[index].Name;
                _session.SetStatus("이름은 비울 수 없습니다 — 지우려면 ✕를 누르세요.");
                return;
            }

            edited[index] = new SpeakerSpec
            {
                Name = typed,
                CharacterId = (characterId.Text ?? string.Empty).Trim()
            };

            SaveSpeakers(edited);
        }

        name.LostFocus += (_, _) => UiGuard.Run(_session, "화자 수정", Commit);
        characterId.LostFocus += (_, _) => UiGuard.Run(_session, "화자 수정", Commit);
        name.KeyDown += (_, args) => CommitOnEnter(args, Commit);
        characterId.KeyDown += (_, args) => CommitOnEnter(args, Commit);

        var remove = new Button
        {
            Content = "✕",
            FontSize = 11,
            Margin = new Thickness(6, 0, 0, 0),
            [ToolTip.TipProperty] = $"'{speaker.Name}'를 목록에서 지웁니다. " +
                                    "이미 대본에 적힌 이름은 그대로 남습니다(화자 칸은 자유 입력)."
        };
        remove.Click += (_, _) => UiGuard.Run(_session, "화자 삭제", () =>
        {
            List<SpeakerSpec> edited = _session!.Definition.Speakers.ToList();

            if (index < edited.Count)
            {
                edited.RemoveAt(index);
                SaveSpeakers(edited);
            }
        });

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto") };
        Grid.SetColumn(name, 0);
        Grid.SetColumn(characterId, 1);
        Grid.SetColumn(remove, 2);
        row.Children.Add(name);
        row.Children.Add(characterId);
        row.Children.Add(remove);

        return row;
    }

    private static void CommitOnEnter(Avalonia.Input.KeyEventArgs args, Action commit)
    {
        if (args.Key == Avalonia.Input.Key.Enter)
        {
            commit();
            args.Handled = true;
        }
    }

    /// <summary>폼의 한 줄을 목록 끝에 더한다. 같은 이름은 거절한다(목록이 신원이다).</summary>
    private void AddSpeaker()
    {
        if (_session is null)
        {
            return;
        }

        string name = (SpeakerNameBox.Text ?? string.Empty).Trim();

        if (name.Length == 0)
        {
            _session.SetStatus("화자 이름을 적어 주세요.");
            return;
        }

        List<SpeakerSpec> edited = _session.Definition.Speakers.ToList();

        if (edited.Any(item => string.Equals(item.Name, name, StringComparison.Ordinal)))
        {
            _session.SetStatus($"화자 '{name}'은 이미 있습니다.");
            return;
        }

        edited.Add(new SpeakerSpec
        {
            Name = name,
            CharacterId = (SpeakerCharacterIdBox.Text ?? string.Empty).Trim()
        });

        if (SaveSpeakers(edited))
        {
            SpeakerNameBox.Text = string.Empty;
            SpeakerCharacterIdBox.Text = string.Empty;
            SpeakerNameBox.Focus();
            _session.SetStatus($"화자 '{name}'을 더했습니다 — 모든 챕터의 대본에서 고를 수 있습니다.");
        }
    }

    /// <summary>
    /// 목록을 정의 파일에 통째로 쓰고, 그 결과를 <b>대본 워크북까지</b> 밀어 넣는다.
    ///
    /// 여기서 미는 것이 중요하다 — 저장만 하면 툴 화면은 새 목록을 보는데 엑셀 드롭다운은
    /// 옛 목록을 들고 있다. 그 어긋남이 2026-08-23 소유자 보고("엑셀에 화자가 동기화가 잘
    /// 안되네")의 절반이었다.
    /// </summary>
    private bool SaveSpeakers(IReadOnlyList<SpeakerSpec> speakers)
    {
        if (_session is null || !_session.SaveSpeakers(speakers))
        {
            return false;
        }

        RebuildSpeakerTab();
        PushVocabularyToEpisodes();
        _diskFingerprint = DiskFingerprint(); // 우리가 쓴 저장이 감시자로 되돌아오지 않게

        return true;
    }

    /// <summary>
    /// 구판 챕터 워크북의 `화자` 시트를 정의 파일로 옮기고 시트를 지운다 (2026-08-23 이행).
    ///
    /// <b>순서가 규격이다</b>: 시트를 지우는 데 성공한 뒤에 정의 파일에 저장한다. 반대로 하면
    /// 엑셀이 잡고 있어 못 지운 워크북이 다음 재읽기에서 <b>사람이 방금 지운 이름을 되살린다</b>.
    /// 지우기가 막히면 이번엔 아무것도 안 하고 다음 기회를 기다린다(원본은 `.bak`에 남는다).
    /// </summary>
    private void ImportLegacySpeakerSheets()
    {
        if (_session?.ProjectPath is null)
        {
            return;
        }

        List<SpeakerSpec> merged = _session.Definition.Speakers.ToList();
        bool changed = false;

        foreach (ChapterEntry entry in _entries)
        {
            if (entry.Model is not { HasSpeakerSheet: true } model)
            {
                continue;
            }

            if (ChapterWorkbookWriter.RemoveSpeakerSheet(entry.Path).Result.Failure is not null)
            {
                continue; // 잠겨 있다 — 다음 재읽기가 다시 시도한다.
            }

            foreach (ChapterSpeaker speaker in model.Speakers)
            {
                string name = speaker.Name.Trim();

                if (name.Length == 0 ||
                    merged.Any(item => string.Equals(item.Name, name, StringComparison.Ordinal)))
                {
                    continue;
                }

                merged.Add(new SpeakerSpec
                {
                    Name = name,
                    CharacterId = speaker.CharacterId?.Trim() ?? string.Empty
                });

                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        if (_session.SaveSpeakers(merged))
        {
            _session.SetStatus(
                $"챕터 엑셀의 `화자` 시트를 프로젝트 화자 목록으로 옮겼습니다({merged.Count}명) — " +
                "이제 [화자] 탭에서 편집합니다. 시트는 사라졌고 이전 상태는 .bak에 있습니다.");
        }
    }

    /// <summary>지난 밀기가 본 어휘의 지문 — 같으면 워크북을 하나도 열지 않는다.</summary>
    private string? _pushedVocabularySignature;

    /// <summary>
    /// 새 대본이 받을 화자 — 챕터를 가리지 않는 프로젝트 목록 하나다.
    /// 규칙은 <see cref="EpisodeSyncRunner.SpeakerNames"/>가 갖는다(동기화가 새 워크북을
    /// 만들 때 쓰는 것과 <b>같은 목록</b>이어야 한다).
    /// </summary>
    private List<string> ProjectSpeakerNames() =>
        _session is null ? [] : EpisodeSyncRunner.SpeakerNames(_session.Definition);

    /// <summary>
    /// 화자·조건 드롭다운을 <b>프로젝트의 모든 챕터</b>의 대본에 반영한다 (2026-08-23).
    ///
    /// 화자는 이제 프로젝트의 것이므로(→ [화자] 탭) 어느 챕터의 대본이든 <b>같은 이름
    /// 목록</b>을 받는다. 예전에는 고른 챕터의 폴더에 그 챕터 시트의 화자만 밀어서, 앱을
    /// 켜면 첫 챕터의 대본만 갱신됐다(2026-08-23 소유자 보고).
    /// ⚠ <b>조건 라벨은 챕터의 것</b>이라 그 챕터 것만 간다 — `조건` 시트는 그 챕터 스탯에
    /// 매인 이름이고, 화자와 달리 챕터가 실제로 쓴다(간선의 표시조건·해금조건).
    ///
    /// ⚠ <b>값이 큰 일이다</b> — 대본 워크북을 전부 열어 본다(§성능 규칙). 그래서 어휘의
    /// 지문을 들고 있다가 <b>달라진 순간에만</b> 한 바퀴 돈다. 밀다가 실패한 워크북이 있으면
    /// 지문을 적지 않는다: 엑셀이 잡고 있던 파일이 다음 기회를 얻어야 하고, 지문을 미리
    /// 적으면 그 워크북만 영영 낡은 목록을 들고 남는다.
    /// </summary>
    private void PushVocabularyToEpisodes()
    {
        if (_session?.ProjectPath is null)
        {
            return;
        }

        List<string> names = _session.Definition.Speakers
            .Select(speaker => speaker.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        string signature = MakeVocabularySignature(names);

        if (string.Equals(signature, _pushedVocabularySignature, StringComparison.Ordinal))
        {
            return;
        }

        var failures = new List<string>();
        int changed = 0;

        foreach (ChapterEntry entry in _entries)
        {
            if (entry.Model is not { } model ||
                EpisodeLibrary.FolderFor(_session.ProjectPath, entry.ChapterId) is not { } folder)
            {
                continue;
            }

            List<string> labels = model.Conditions.Select(condition => condition.Label).ToList();

            foreach (ChapterEpisode episode in model.Episodes)
            {
                EpisodeLibrary.VocabularyPush push =
                    EpisodeLibrary.PushVocabulary(folder, episode.EpisodeId, names, labels);

                if (push.Changed)
                {
                    changed++;
                }
                else if (push.Failure is { } failure)
                {
                    failures.Add(failure);
                }
            }
        }

        if (failures.Count == 0)
        {
            _pushedVocabularySignature = signature;
        }
        else
        {
            _session.SetStatus(failures[0] +
                (failures.Count > 1 ? $" (외 {failures.Count - 1}건)" : string.Empty));
        }

        if (changed > 0)
        {
            _session.SetStatus($"화자·조건 드롭다운을 대본 워크북 {changed}개에 반영했습니다.");
        }
    }

    /// <summary>
    /// 어휘의 지문 — 프로젝트 화자 이름들과 챕터마다의 (조건 라벨 · 에피소드 목록).
    /// 이 셋 중 하나라도 달라져야 워크북을 여는 값을 치른다.
    /// </summary>
    private string MakeVocabularySignature(IReadOnlyList<string> names)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(string.Join("|", names)).Append('\n');

        foreach (ChapterEntry entry in _entries)
        {
            builder.Append(entry.ChapterId).Append(':');

            if (entry.Model is { } model)
            {
                builder.Append(string.Join("|", model.Conditions.Select(item => item.Label)));
                builder.Append(':');
                builder.Append(string.Join("|", model.Episodes.Select(item => item.EpisodeId)));
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// 왼쪽 목록의 챕터 클릭 → 챕터 엑셀 열기 (2026-08-16 소유자 — 기본 동작이 "엑셀에서
    /// 만진다"이므로 클릭이 곧 편집 창구를 연다). 스프레드시트 앱이 없으면 폴더에서 보여 준다.
    /// </summary>
    internal void OpenChapterWorkbook(string chapterId)
    {
        string? folder = ChapterLibrary.FolderFor(_session?.ProjectPath);

        if (folder is null)
        {
            return;
        }

        string target = IoPath.Combine(folder, chapterId + ".xlsx");

        // .xlsm으로 개명된 챕터(구글 시트)도 그 파일이 원본이다.
        if (!File.Exists(target) && File.Exists(IoPath.Combine(folder, chapterId + ".xlsm")))
        {
            target = IoPath.Combine(folder, chapterId + ".xlsm");
        }

        if (!File.Exists(target))
        {
            return; // 목록에 있는데 파일이 없다 — 읽기 실패 챕터. 검증 보고가 사유를 든다.
        }

        if (!SpreadsheetAssociation.IsSpreadsheetHandler(WorkbookHandlerProbe()))
        {
            RevealInFolder(target);
            _session?.SetStatus(".xlsx에 연결된 스프레드시트 앱이 없어 폴더에서 파일을 보여 줍니다.");
            return;
        }

        OpenWorkbookFile(target);
    }

    /// <summary>
    /// 노드 클릭 → 에피소드 엑셀 열기 (G5). 워크북이 없으면 §3.2 규격대로 만들어서 연다 —
    /// 기획자가 머리글 11개를 손으로 칠 이유가 없다.
    /// </summary>
    internal void OpenEpisode(string episodeId)
    {
        // 대본은 그 챕터의 폴더에 산다 (2026-08-16) — 다른 챕터의 같은 이름과 섞이지 않는다.
        string? folder = SelectedEpisodesFolder;

        if (folder is null)
        {
            _session?.SetStatus("프로젝트를 먼저 저장하고 챕터를 골라야 대본 폴더 자리가 정해집니다.");
            return;
        }

        // .xlsx는 없는데 같은 이름의 .xlsm·.ods가 있다면, 사람이 쓴 원고가 그리로 옮겨 간
        // 것이다(구글 시트로 열어 저장하면 형식이 바뀌기도 한다). 그 위에 빈 워크북을 새로
        // 만들면 원고가 화면에서 사라진 것처럼 보인다 — 만들지 않고 그 파일을 열어 준다.
        if (EpisodeLibrary.FindExisting(folder, episodeId) is null &&
            EpisodeLibrary.FindOtherFormat(folder, episodeId) is { } other)
        {
            _session?.SetStatus(
                $"'{IoPath.GetFileName(other)}'가 있어 새 워크북을 만들지 않았습니다. " +
                "툴은 .xlsx만 읽습니다 — 그 파일을 .xlsx로 저장(또는 이름 변경)해 주세요.");
            OpenWorkbookFile(other);
            return;
        }

        if (EpisodeLibrary.EnsureWorkbook(
                folder,
                episodeId,
                ProjectSpeakerNames(),
                SelectedModel?.Conditions.Select(condition => condition.Label).ToList()))
        {
            _session?.SetStatus($"에피소드 워크북을 새로 만들었습니다: {EpisodeLibrary.PathFor(folder, episodeId)}");
            StartWatchingEpisodes(EpisodeLibrary.FolderFor(_session?.ProjectPath));
        }

        // 이미 있는 파일이면 그 파일을 연다 — 이름이 정규화만 다른 경우에도 같은 파일이다.
        string target = EpisodeLibrary.FindExisting(folder, episodeId)
            ?? EpisodeLibrary.PathFor(folder, episodeId);
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
        // 구판 워크북 규격 이행 (2026-08-16) — 필요 없는 파일에는 손대지 않으므로 매번
        // 불러도 쓰기는 구판을 처음 만난 그 한 번뿐이다. 실패(잠금)는 상태줄로 알리고
        // 리더가 구판 그대로 읽으며 머리글 경고를 세운다.
        if (ChapterLibrary.FolderFor(_session?.ProjectPath) is { } chapterFolder &&
            Directory.Exists(chapterFolder))
        {
            foreach (string workbook in Directory.EnumerateFiles(chapterFolder, "*.xlsx")
                         .Where(file => !IoPath.GetFileName(file).StartsWith("~$", StringComparison.Ordinal)))
            {
                ChapterWorkbookMigrator.MigrationResult migration =
                    ChapterWorkbookMigrator.Migrate(workbook);

                if (migration.Migrated)
                {
                    _session?.SetStatus(
                        $"'{IoPath.GetFileName(workbook)}'를 새 시트 규격으로 이행했습니다" +
                        "(이전 상태는 .bak). 엑셀이 열려 있었다면 닫았다 다시 열어 주세요.");
                }
                else if (migration.Failure is { } failure)
                {
                    _session?.SetStatus(failure);
                }
            }
        }

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

        // 구판 `화자` 시트 → 프로젝트 화자 목록 (2026-08-23 이행). 옮길 것이 없으면 아무
        // 파일도 안 만진다 — 시트가 남아 있는 워크북을 처음 만난 그 한 번뿐이다.
        ImportLegacySpeakerSheets();

        // 화자 목록이 바뀌는 순간은 [화자] 탭에서 저장한 순간이지만, 챕터가 늘거나 에피소드가
        // 생겨도 밀 곳이 늘어난다 — 지문이 같으면 워크북을 하나도 열지 않으므로 여기서 판다.
        PushVocabularyToEpisodes();
        RebuildSpeakerTab();

        AutoExport();        // 진행 JSON은 사람 손을 기다리지 않는다 (2026-08-17)
        Validate();
        Draw();              // 못 나갔으면 그 결론이 검증 보고 맨 위에 선다
        RefreshLockState(); // 엑셀을 열거나 닫으면 그 사실이 여기로 온다

        // 방금 본 디스크를 지문으로 남긴다 — 감시자가 깨울 때 이것과 견준다(IfDiskChanged).
        // 이행(.bak)이 위에서 파일을 만졌을 수도 있으므로 읽기가 끝난 지금 찍는다.
        _diskFingerprint = DiskFingerprint();

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

        _validation = ValidationFor(entry);
    }

    /// <summary>
    /// 증명과 내보내기 (2026-08-23에 이 파일에서 나갔다 — `Vn.Authoring.Chapters`).
    ///
    /// 캐시·지문·거부 장부가 전부 저쪽에 있다. 여기 남은 것은 <b>언제 부르나</b>뿐이고,
    /// 그것이 화면의 일이다. 규칙이 3,835줄 안에 살면 밖에서 보이지 않는다 — 실제로
    /// 물렸다: "동기화는 고른 챕터만, 내보내기는 전 챕터"라는 사실이 여기 묻혀 있어
    /// 저작 관문을 걸려던 시도가 뒤늦게 그것을 알았다.
    /// </summary>
    private readonly ChapterExportService _export = new();

    /// <summary>직전 내보내기에서 못 나간 챕터들.</summary>
    private ChapterExportRun _exportRun = ChapterExportRun.Empty;

    /// <summary>실제로 증명을 돌린 횟수 — 테스트가 "일을 몇 번 했는가"를 보는 창이다.</summary>
    internal int ValidationComputeCount => _export.ValidationComputeCount;

    private ChapterValidationResult ValidationFor(ChapterEntry entry) =>
        _export.ValidationFor(entry, _session?.ProjectPath, _session?.Project);
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
    /// <summary>
    /// 툴이 길을 놓을 때 넣어 주는 문구 (v12). 빈 문구는 오류이므로 무언가는 있어야 하고,
    /// 기획자가 바로 고쳐 쓸 수 있는 가장 짧은 말이다.
    /// </summary>
    internal const string DefaultOptionLabel = "계속";

    /// <summary>진행 JSON 폴더 이름. 정본은 <see cref="ChapterExportService"/>에 있다.</summary>
    public const string ExportFolderName = ChapterExportService.ExportFolderName;

    /// <summary>
    /// G8 — 런타임 수입용 JSON을 쓴다. <b>검증을 통과해야만 나간다</b>(Gate C 3번) —
    /// 오류가 있으면 파일을 만들지 않고 사유를 보고 패널에 세운다. 쓰레기가 런타임으로
    /// 넘어가는 것보다 내보내기가 실패하는 편이 낫다.
    /// </summary>
    // ── 판 다루기: 휠 확대·축소, 가운데 단추 끌어 이동 (2026-08-17 소유자) ──
    //
    // 연출 그래프(GraphEditorView)와 같은 손놀림·같은 산식이다. 판이 둘인데 다루는
    // 법이 다르면 손이 매번 헷갈린다.
    //
    // 2026-08-18 팀장 미팅에서 Ctrl 요구가 빠졌다 — 판 위에서 휠은 곧 배율이다.
    // 세로로 훑는 일은 가운데 단추 끌기가 맡는다.

    private const double MinZoom = 0.3;
    private const double MaxZoom = 2.5;

    private double _zoom = 1;
    private bool _panning;
    private Point _panStart;
    private Vector _panStartOffset;

    /// <summary>휠 — 누른 키와 무관하게 배율이다. 스크롤로 새지 않게 여기서 삼킨다.</summary>
    private void OnGraphWheel(object? sender, Avalonia.Input.PointerWheelEventArgs args)
    {
        ApplyZoom(_zoom * (args.Delta.Y > 0 ? 1.15 : 1 / 1.15), args.GetPosition(GraphScroll));
        args.Handled = true;
    }

    /// <summary>배율 적용 — 커서 아래의 내용이 그 자리에 남게 오프셋을 맞춘다.</summary>
    private void ApplyZoom(double zoom, Point? anchor)
    {
        Point pivot = anchor ?? new Point(
            GraphScroll.Viewport.Width / 2, GraphScroll.Viewport.Height / 2);

        // 지금 pivot 아래에 있는 캔버스 좌표.
        var content = new Point(
            (GraphScroll.Offset.X + pivot.X) / _zoom,
            (GraphScroll.Offset.Y + pivot.Y) / _zoom);

        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        ZoomHost.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        ZoomText.Text = $"{Math.Round(_zoom * 100)}%";
        ZoomResetButton.IsVisible = Math.Abs(_zoom - 1) > 0.001;
        ZoomHost.UpdateLayout(); // 새 크기를 알아야 오프셋 상한이 맞는다

        GraphScroll.Offset = new Vector(
            Math.Max(0, (content.X * _zoom) - pivot.X),
            Math.Max(0, (content.Y * _zoom) - pivot.Y));
    }

    private void OnGraphPanPressed(object? sender, Avalonia.Input.PointerPressedEventArgs args)
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

    private void OnGraphPanMoved(object? sender, Avalonia.Input.PointerEventArgs args)
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

    private void OnGraphPanReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs args)
    {
        if (!_panning)
        {
            return;
        }

        _panning = false;
        args.Pointer.Capture(null);
        args.Handled = true;
    }
    /// <summary>
    /// 다시 읽을 때마다 <b>모든</b> 챕터의 진행 JSON을 저절로 낸다. 규칙과 장부는
    /// <see cref="ChapterExportService"/>가 갖고, 여기는 <b>언제 부르나</b>만 정한다.
    ///
    /// 결과는 상태줄이 아니라 <b>검증 보고</b>에 세운다. 다시 읽기는 저장 직후에 오고
    /// 그 뒤로 동기화 보고까지 따라오므로, 상태줄에 얹으면 서로를 덮는다(실제로 덮였다).
    /// 못 나간 사유는 어차피 그 보고에 이미 서 있는 오류들이다 — 결론을 그 옆에 둔다.
    /// </summary>
    private void AutoExport() =>
        _exportRun = _export.ExportAll(_entries, _session?.ProjectPath, _session?.Project);

    /// <summary>검증 보고 맨 위에 세울 내보내기 결론 — 못 나갔을 때만 있다.</summary>
    private string? ExportNotice() => _exportRun.Notice;

    // ── 그리기 ──────────────────────────────────────────────────────────────

    private void Draw()
    {
        CanvasDrawCount++;

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


        // 에피소드별 보이는 선택지 = 문구가 붙은 나가는 간선들 (v9 — 길 하나가 곧 선택지
        // 하나). 포트 그리기와 간선 그리기가 같은 목록 하나를 본다.
        _optionsByEpisode.Clear();

        foreach (ChapterEpisode episode in model.Episodes)
        {
            // v12 (2026-08-24) — **나가는 길은 전부 포트를 받는다.** 예전에는 문구 없는
            // 간선("보이지 않는 기본")을 걸러 내고 카드 중앙에서 직행선으로 그렸는데,
            // 그 개념이 폐지됐다. 문구가 비어 있으면 그것은 종류가 아니라 **고칠 것**이고,
            // 판에서 그 자리를 보여 줘야 기획자가 고친다.
            _optionsByEpisode[episode.EpisodeId] = model.Edges
                .Where(edge =>
                    string.Equals(edge.FromEpisodeId, episode.EpisodeId, StringComparison.Ordinal))
                .ToList();
        }

        // 간선을 먼저 그려야 노드 카드 아래로 깔린다.
        DrawEpisodeRails(model);

        foreach (ChapterEpisode episode in model.Episodes)
        {
            DrawEpisode(model, episode);
        }

        DrawDiagnostics(model);
        ApplySelectionVisuals();
        RefreshPropertyPanel(preserveTyping: true);
    }

    // ⛔ 픽스처 (G6) — 2026-08-24에 화면에서 걷었다 (소유자: "꽤 오래 다뤘는데 단 한 번도
    // 안 썼어"). 여기 있던 것: `_selectedFixture`·`RefreshFixtureCombo`·`WalkSelectedFixture`,
    // 그리고 걸은 경로를 초록으로 칠하던 `onPath`/`pathEdges` 인자들.
    //
    // 콤보를 없애면 고를 길이 없고, 고를 수 없는 하이라이트는 켤 수도 끌 수도 없다 —
    // 시트의 `활성` 픽스처만 남겨 두면 견본 챕터가 초록으로 켜진 채 <b>끄는 손잡이 없이</b>
    // 서 있게 된다. 그래서 화면 쪽은 통째로 걷는 것이 옳다.
    //
    // ⚠ <b>엑셀의 `픽스처` 시트와 `ChapterFixtureWalker`는 안 지웠다</b> — 시트는 워크북
    // 규격이라 지우면 남의 파일이 깨지고, 걷기는 저작 계층의 순수 함수다(제 테스트가 있다).
    // 되살릴 때 필요한 것은 콤보 하나와 이 자리의 걷기 호출뿐이다.

    /// <summary>포트 줄 높이 — 카드가 선택지 수만큼 아래로 자란다.</summary>
    private const double PortRowHeight = 18;

    /// <summary>에피소드 → 보이는 선택지(문구 붙은 나가는 간선). Draw가 채우고 카드·간선이 함께 본다.</summary>
    private readonly Dictionary<string, List<ChapterEdge>> _optionsByEpisode = new(StringComparer.Ordinal);

    /// <summary>선택지 포트의 세로 자리 — 카드 그리기와 간선 그리기가 같은 산식을 쓴다.</summary>
    private static double PortY(double cardY, int index) =>
        cardY + CardHeight - 7 + index * PortRowHeight + PortRowHeight / 2;

    /// <summary>
    /// 간선 그리기의 갈림 (2026-08-15 소유자 개정 2) — <b>선택지 포트는 카드 오른쪽</b>이다.
    /// 연출 그래프의 조건 갈래 포트와 같은 문법: 카드 오른변에 포트가 뚫리고 각 포트에서
    /// 자기 간선이 나간다(선택지는 많아야 3개). 아래로 줄기를 빼는 철도 흉내는 폐기.
    /// 선택지 없는 에피소드는 기존 중앙 직행선 그대로.
    /// </summary>
    private void DrawEpisodeRails(ChapterGraphModel model)
    {
        foreach (ChapterEpisode episode in model.Episodes)
        {
            List<ChapterEdge> edges = model.Edges
                .Where(edge => string.Equals(edge.FromEpisodeId, episode.EpisodeId, StringComparison.Ordinal))
                .ToList();

            List<ChapterEdge> options = _optionsByEpisode.GetValueOrDefault(episode.EpisodeId) ?? [];

            if (!_placed.TryGetValue(episode.EpisodeId, out (double X, double Y) position))
            {
                // 자리를 못 잡은 카드 — 포트가 없으니 중앙 직행선으로라도 그린다.
                foreach (ChapterEdge edge in edges)
                {
                    DrawDirectEdge(edge);
                }

                continue;
            }

            for (int index = 0; index < options.Count; index++)
            {
                DrawPortEdge(options[index],
                    new Point(position.X + CardWidth + 5, PortY(position.Y, index)));
            }
        }
    }

    /// <summary>
    /// 포트에서 도착 카드로 <b>직선</b> 하나. 도착이 오른쪽이면 왼쪽 변으로, 왼쪽이면
    /// 오른쪽 변으로 들어간다(▶ ◀).
    ///
    /// 구간이 하나뿐이라 <b>히트 선이 간선 전체를 덮는다</b> — 예전에는 첫 가로 구간
    /// 위에서만 눌렸다.
    /// </summary>
    private void DrawPortEdge(ChapterEdge edge, Point port)
    {
        // 색이 말하는 것은 하나뿐이다 — 관문이 걸렸나(주황) 아닌가(회색). 픽스처 경로를
        // 초록으로 칠하던 갈래는 2026-08-24에 걷혔다.
        IBrush stroke = edge.ConditionLabel is null
            ? new SolidColorBrush(Color.Parse("#8894A0"))
            : new SolidColorBrush(Color.Parse("#C08A3E"));
        const double thickness = 1.6;

        var segments = new List<Line>();

        void Segment(double x1, double y1, double x2, double y2)
        {
            var line = new Line
            {
                StartPoint = new Point(x1, y1),
                EndPoint = new Point(x2, y2),
                Stroke = stroke,
                StrokeThickness = thickness,
                Tag = EdgeTag(edge),
                IsHitTestVisible = false
            };

            if (edge.HideWhenLocked)
            {
                line.StrokeDashArray = new AvaloniaList<double> { 4, 3 };
            }

            GraphCanvas.Children.Add(line);
            segments.Add(line);
        }

        if (!_placed.TryGetValue(edge.ToEpisodeId, out (double X, double Y) target))
        {
            return; // 없는 도착지는 구조 검증이 이미 잡았다.
        }

        var targetRect = new Rect(target.X, target.Y, CardWidth, CardHeight);

        // ⚠ 직선이다 (2026-08-23 소유자 보고: "선이 완전히 겹치다보니 어디로 이어지는지
        // 확인하기가 힘들어").
        //
        // 예전에는 직교 3구간(가로 → 세로 → 가로)으로 돌렸는데, 꺾이는 x가
        // `(port.X + target.X) / 2`라 **같은 열로 가는 간선들이 세로 구간을 공유**했다.
        // 도착도 언제나 카드 세로 중앙 한 점이라 마지막 가로 구간까지 겹쳤다. 결과적으로
        // 한 에피소드에서 나가는 길이 여럿이면 화면에서 한 줄로 보였다.
        //
        // 직선은 그 문제가 원천적으로 없다: 포트마다 y가 다르고 도착마다 위치가 다르므로
        // **기울기가 저절로 갈린다.** 겹치려면 두 간선의 출발과 도착이 모두 같아야 하는데,
        // 그러면 포트가 달라 출발점이 이미 다르다.
        bool targetIsRight = port.X <= targetRect.X;

        double entryX = targetIsRight ? targetRect.X - 8 : targetRect.Right + 8;
        double entryY = targetRect.Y + (CardHeight / 2);

        Segment(port.X, port.Y, entryX, entryY);

        // 화살촉은 진입하는 변에 둔다 — 뒤로 가는 길(도착이 왼쪽)이면 반대로 향한다.
        AddEdgeArrow(
            targetIsRight ? entryX + 1 : entryX - 1,
            entryY,
            stroke,
            pointRight: targetIsRight,
            pointLeft: !targetIsRight);

        RegisterEdgeLines(edge, segments, stroke, thickness);

        // 첫 가로 구간 위 히트 선 — 포트 밖에서도 간선을 누를 수 있다.
        var hit = new Line
        {
            StartPoint = segments[0].StartPoint,
            EndPoint = segments[0].EndPoint,
            Stroke = Brushes.Transparent,
            StrokeThickness = 12,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        string hitFrom = edge.FromEpisodeId;
        string hitTo = edge.ToEpisodeId;
        string hitLabel = EdgeLabelKey(edge);
        hit.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            UiGuard.Run(_session, "간선 선택", () => SelectEdgeKey(hitFrom, hitTo, hitLabel));
        };
        GraphCanvas.Children.Add(hit);
    }

    private void AddEdgeArrow(
        double x, double y, IBrush fill, bool pointRight, bool pointUp = false, bool pointLeft = false)
    {
        var arrow = new Avalonia.Controls.Shapes.Polygon
        {
            Fill = fill,
            IsHitTestVisible = false,
            Points = pointLeft
                ? [new Point(0, -4), new Point(-7, 0), new Point(0, 4)]
                : pointRight
                    ? [new Point(0, -4), new Point(7, 0), new Point(0, 4)]
                    : pointUp
                        ? [new Point(-4, 0), new Point(0, -7), new Point(4, 0)]
                        : [new Point(-4, 0), new Point(0, 7), new Point(4, 0)]
        };

        Canvas.SetLeft(arrow, x);
        Canvas.SetTop(arrow, y);
        GraphCanvas.Children.Add(arrow);
    }

    private void RegisterEdgeLines(ChapterEdge edge, List<Line> segments, IBrush stroke, double thickness)
    {
        (string, string, string) key = (edge.FromEpisodeId, edge.ToEpisodeId, EdgeLabelKey(edge));
        _lineByEdge[key] = segments;
        _lineBase[key] = (stroke, thickness);
    }

    private void DrawDirectEdge(ChapterEdge edge)
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


        // 간선의 정체를 시각 요소에 남긴다. 화면 없는 렌더 검증(Gate A)이 "무엇이 그려졌는지"를
        // 색·좌표로 역추론하지 않고 이름으로 확인할 수 있어야 한다.
        line.Tag = EdgeTag(edge);
        GraphCanvas.Children.Add(line);

        // 선택 강조는 제자리에서 입힌다(ApplySelectionVisuals) — 여기서는 기본 모습만 등록한다.
        string fromId = edge.FromEpisodeId;
        string toId = edge.ToEpisodeId;
        string labelKey = EdgeLabelKey(edge);
        _lineByEdge[(fromId, toId, labelKey)] = [line];
        _lineBase[(fromId, toId, labelKey)] = (line.Stroke, line.StrokeThickness);

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
            UiGuard.Run(_session, "간선 선택", () => SelectEdgeKey(fromId, toId, labelKey));
        };
        GraphCanvas.Children.Add(hit);

        // v11 — 엔딩 간선은 판에서 보여야 한다. 이 길을 타면 챕터가 끝나는데 라벨이
        // 조용하면 기획자가 "여기서 끝난다"를 그래프에서 읽을 수 없다.
        string label = string.Join(" · ", new[]
        {
            edge.OptionLabel,
            edge.ConditionLabel is null ? null : $"[{edge.ConditionLabel}]",
            edge.IsEnding ? $"⏹ {edge.EndingKey}" : null,
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
            UiGuard.Run(_session, "간선 선택", () => SelectEdgeKey(fromId, toId, labelKey));
        };

        text.Measure(Size.Infinity);
        Canvas.SetLeft(text, ((x1 + x2) / 2) - (text.DesiredSize.Width / 2));
        Canvas.SetTop(text, ((y1 + y2) / 2) - (text.DesiredSize.Height / 2));
        GraphCanvas.Children.Add(text);
    }

    private void DrawEpisode(ChapterGraphModel model, ChapterEpisode episode)
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

        // 관문은 v8에서 길(간선)의 것이 됐다 — 카드는 "들어오는 길에 관문이 있다"를 표시한다.
        if (GatedIncoming(model, episode) is { Count: > 0 } gatedPaths)
        {
            var lockMark = new TextBlock { Text = "🔒", FontSize = 11 };
            ToolTip.SetTip(lockMark, GateSummary(gatedPaths));
            header.Children.Add(lockMark);
        }

        // v11 — 엔딩은 간선의 것이다. "이 에피소드로 들어오는 엔딩 간선이 있는가"를 묻는다.
        if (model.IsEndingEpisode(episode.EpisodeId))
        {
            header.Children.Add(new TextBlock { Text = "★", FontSize = 11, Foreground = Brushes.Goldenrod });
        }

        if (hasError)
        {
            header.Children.Add(new TextBlock { Text = "⚠", FontSize = 11, Foreground = Brushes.IndianRed });
        }

        bool hasTitle = !string.IsNullOrWhiteSpace(episode.Title);

        header.Children.Add(new TextBlock
        {
            Text = hasTitle ? episode.Title : episode.EpisodeId,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var body = new StackPanel { Spacing = 1 };
        body.Children.Add(header);

        // 제목이 없으면 굵은 줄이 이미 Id다 — 같은 글자를 두 번 쓰지 않는다.
        if (hasTitle)
        {
            body.Children.Add(new TextBlock
            {
                Text = episode.EpisodeId,
                FontSize = 10,
                Opacity = 0.6,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        // 여기 도착했을 때의 스탯 (2026-08-17 소유자: "간선을 따라 왔을 때 스탯의 변화량이
        // 노드에 표시되도록. 여러 루트가 있을 때는 스탯의 최소최대량을 표기"). 도달성 증명이
        // 이미 (에피소드, 스탯 벡터)로 걸으므로 그 결과를 읽기만 한다 — 따로 세지 않는다.
        if (StatSpanText(episode) is { Length: > 0 } spans)
        {
            body.Children.Add(new TextBlock
            {
                Text = spans,
                FontSize = 9,
                Opacity = 0.75,
                Foreground = new SolidColorBrush(Color.Parse("#3E7B9B")),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Tag = StatLineTag // 검증이 이 줄을 글자 짐작 없이 집는다
            });
        }

        // 선택지 포트 (2026-08-15 소유자) — 연출 그래프의 조건 갈래 포트처럼 카드
        // 오른변에 뚫린다. 카드는 선택지 수만큼 아래로 자라고(많아야 3개), 문구·원은
        // 간선 그리기와 같은 PortY 산식으로 캔버스에 앉는다 — 줄과 선이 어긋날 수 없다.
        List<ChapterEdge> options = _optionsByEpisode.GetValueOrDefault(episode.EpisodeId) ?? [];

        var card = new Border
        {
            Width = CardWidth,
            Height = CardHeight + options.Count * PortRowHeight,
            Padding = new Thickness(9, 7),
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(hasError ? 2 : 1),
            BorderBrush = hasError
                ? Brushes.IndianRed
                : model.IsEndingEpisode(episode.EpisodeId)
                    ? new SolidColorBrush(Color.Parse("#C09A3E"))
                    : new SolidColorBrush(Color.Parse("#7F8A96")),
            Background = new SolidColorBrush(Color.Parse("#FAFBFCFD")),
            Child = body,
            // 노드 카드임을 EpisodeId로 표시한다. 간선 라벨도 Border라서, 표식이 없으면
            // 검증이 카드와 라벨을 구별하지 못한다.
            Tag = episode.EpisodeId
        };

        ToolTip.SetTip(card, Tooltip(model, episode) + StatSpanDetail(episode));

        if (GatedIncoming(model, episode).Count > 0)
        {
            card.BorderThickness = new Thickness(1.6);
            card.BorderBrush = new SolidColorBrush(Color.Parse("#C08A3E"));
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

        DrawOptionPorts(model, episode, options, x, y);
    }

    /// <summary>
    /// 카드 오른변의 선택지 포트들 — 문구(카드 안 오른쪽 정렬) + 테두리 위의 원.
    /// v9에서는 포트 하나가 곧 간선 하나다(문구가 붙은 길). 클릭하면 그 간선이 선택된다.
    /// </summary>
    private void DrawOptionPorts(
        ChapterGraphModel model, ChapterEpisode episode, IReadOnlyList<ChapterEdge> options, double x, double y)
    {
        for (int index = 0; index < options.Count; index++)
        {
            ChapterEdge wired = options[index];

            // v12 — 문구가 비면 그것은 길의 종류가 아니라 **고칠 것**이다. 판에서
            // 그 자리를 짚어 주지 않으면 기획자는 엑셀의 빈 칸을 못 찾는다.
            string option = wired.HasNoOptionLabel ? "⚠ 문구 없음" : wired.OptionLabel!;
            double portY = PortY(y, index);

            var label = new TextBlock
            {
                Text = wired.ConditionLabel is { } gate ? $"{option} [{gate}]" : option,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.Parse(wired.HasNoOptionLabel ? "#C0392B" : "#C06A14")),
                MaxWidth = CardWidth - 24,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Background = Brushes.Transparent,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            label.Measure(Size.Infinity);
            Canvas.SetLeft(label, x + CardWidth - 12 - label.DesiredSize.Width);
            Canvas.SetTop(label, portY - label.DesiredSize.Height / 2);

            var port = new Avalonia.Controls.Shapes.Ellipse
            {
                Width = 9,
                Height = 9,
                Stroke = new SolidColorBrush(Color.Parse("#C06A14")),
                StrokeThickness = 1.6,
                Fill = new SolidColorBrush(Color.Parse("#C06A14")),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            Canvas.SetLeft(port, x + CardWidth - 4.5);
            Canvas.SetTop(port, portY - 4.5);

            const string tip = "클릭하면 이 간선의 조건·해금·스탯변화를 편집합니다.";
            ToolTip.SetTip(label, tip);
            ToolTip.SetTip(port, tip);

            ChapterEdge capturedEdge = wired;

            void OnPressed(object? _, Avalonia.Input.PointerPressedEventArgs e)
            {
                e.Handled = true;
                UiGuard.Run(_session, "선택지 포트", () =>
                    SelectEdgeKey(capturedEdge.FromEpisodeId, capturedEdge.ToEpisodeId, EdgeLabelKey(capturedEdge)));
            }

            label.PointerPressed += OnPressed;
            port.PointerPressed += OnPressed;

            GraphCanvas.Children.Add(label);
            GraphCanvas.Children.Add(port);
        }
    }

    /// <summary>
    /// 카드에 세울 스탯 줄 — `신뢰 1~3 · 분노 0` (2026-08-17). 값은 <b>도착 직후</b>다
    /// (그 노드로 들어오는 간선의 증감까지 커밋한 뒤). 루트가 하나면 값 하나, 갈래가
    /// 여럿이면 최소~최대로 벌어진다.
    ///
    /// 닿을 수 없는 에피소드에는 아무것도 안 쓴다 — 도착이 없으니 도착 시점 값도 없다.
    /// bool 스탯은 숫자 대신 참/거짓으로 읽는다(0·1이 무슨 뜻인지 카드에서 알 수 없다).
    /// </summary>
    private string StatSpanText(ChapterEpisode episode)
    {
        if (_validation is not { } validation || SelectedModel is not { } model)
        {
            return string.Empty;
        }

        // 챕터 어디에서도 움직이지 않는 스탯은 뺀다 — 모든 카드에 `분노 0`이 붙으면
        // 정작 움직이는 스탯이 그 줄에 묻힌다.
        //
        // 다만 <b>아무 스탯도 안 움직이는 챕터</b>에서는 거르지 않고 전부 보여 준다
        // (2026-08-17 소유자: "도착 스탯이 보여야 하는데 아직 안보이네"). 증감을 아직
        // 하나도 안 적은 판이 그렇고, 그때 빈 카드는 기능이 없는 것처럼 읽힌다 —
        // 초기값이라도 서 있어야 "여기에 그 값이 뜬다"가 보인다.
        var moved = model.Edges
            .SelectMany(edge => edge.StatChanges)
            .Select(change => change.Key)
            .ToHashSet(StringComparer.Ordinal);

        List<ChapterStatSpan> spans = validation.Reachability
            .SpansFor(episode.EpisodeId)
            .Where(span => moved.Count == 0 || moved.Contains(span.Key))
            .ToList();

        if (spans.Count == 0)
        {
            return string.Empty;
        }

        string Value(ChapterStatSpan span)
        {
            bool isBool = model.Stats.FirstOrDefault(stat =>
                string.Equals(stat.Key, span.Key, StringComparison.Ordinal))?.Type == ChapterStatType.Bool;

            if (!isBool)
            {
                return span.IsFixed
                    ? span.Minimum.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : $"{span.Minimum}~{span.Maximum}";
            }

            return span.IsFixed ? (span.Minimum == 1 ? "참" : "거짓") : "참·거짓";
        }

        string body = string.Join(" · ", spans.Select(span =>
            $"{(span.DisplayName.Length > 0 ? span.DisplayName : span.Key)} {Value(span)}"));

        // 탐색이 상한에서 잘렸으면 이 폭은 "적어도 이만큼"이다 — 확정처럼 보이면 안 된다.
        return validation.Reachability.ExplorationComplete ? body : "≈ " + body;
    }

    /// <summary>카드의 도착 스탯 줄에 붙는 표식 — 검증이 그 줄을 집는 손잡이다.</summary>
    internal const string StatLineTag = "episode-stats";

    /// <summary>카드 툴팁의 스탯 절 — 좁은 카드 줄이 잘려도 여기서는 다 읽힌다.</summary>
    private string StatSpanDetail(ChapterEpisode episode)
    {
        string line = StatSpanText(episode);

        if (line.Length == 0)
        {
            return string.Empty;
        }

        bool partial = line.StartsWith("≈ ", StringComparison.Ordinal);

        return "\n여기 도착했을 때의 스탯 —\n  "
            + string.Join("\n  ", (partial ? line[2..] : line).Split(" · "))
            + "\n  (범위는 들어오는 루트가 여럿일 때 벌어집니다)"
            + (partial ? "\n  ≈ 탐색이 상한에서 멈춰 실제 폭은 더 넓을 수 있습니다." : string.Empty);
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

    /// <summary>
    /// 선택된 챕터의 대본 폴더 <c>episodes/{ChapterId}/</c> (2026-08-16 — 챕터별 격리).
    /// EpisodeId는 챕터 안에서만 유일하므로, 파일을 찾는 모든 길이 이 범위를 지난다.
    /// </summary>
    private string? SelectedEpisodesFolder =>
        _selectedChapterId is { } chapterId
            ? EpisodeLibrary.FolderFor(_session?.ProjectPath, chapterId)
            : null;

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
    // 간선 신원 = (출발, 도착, 라벨) — 여러 선택지가 같은 에피소드로 이어질 수 있다
    // (2026-08-15 소유자). Label은 정규화된 값(무라벨 진행 = 빈 문자열)이다.
    private (string From, string To, string Label)? _selectedEdgeKey;

    private static string EdgeLabelKey(ChapterEdge edge) => edge.OptionLabel?.Trim() ?? string.Empty;

    // 선택 강조는 다시 그리지 않고 제자리에서 바꾼다. 클릭 핸들러 안에서 캔버스를 다시 만들면
    // 방금 누른 카드가 파괴되어 더블클릭(둘째 탭이 다른 인스턴스에 떨어짐)과 드래그(캡처가
    // 죽은 카드에 걸림)가 죽는다 — 실사용에서 "클릭이 안 먹힌다"로 나타났던 결함이다.
    private readonly Dictionary<string, Border> _cardById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (IBrush? Brush, Thickness Thickness)> _cardBase =
        new(StringComparer.Ordinal);
    // 포트 철도에서 간선 하나가 선분 여럿이 된다 — 강조·복원은 선분 묶음 단위다.
    private readonly Dictionary<(string, string, string), List<Line>> _lineByEdge = new();
    private readonly Dictionary<(string, string, string), (IBrush? Stroke, double Thickness)> _lineBase = new();


    // 2026-08-16 소유자 — 선택이 탭을 끌고 다니지 않는다. 대사 탭을 보며 노드를 갈아타는
    // 흐름이 실사용의 대부분이라, 편집 탭 강제 전환("클릭이 안 먹힌다" 방지책)을 폐지했다.
    // 편집이 필요하면 사람이 편집 탭을 누른다.
    internal void SelectEpisode(string? episodeId)
    {
        _selectedEpisodeId = episodeId;
        _selectedEdgeKey = null;
        HideEdgeForm(); // 다른 노드로 넘어가면 열려 있던 연결 폼은 닫는다
        ApplySelectionVisuals();
        RefreshPropertyPanel();
    }

    internal void SelectEdgeKey(string fromEpisodeId, string toEpisodeId, string optionLabel = "")
    {
        _selectedEdgeKey = (fromEpisodeId, toEpisodeId, optionLabel.Trim());
        _selectedEpisodeId = null;
        HideEdgeForm();
        ApplySelectionVisuals();
        RefreshPropertyPanel();
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

        foreach (((string, string, string) key, List<Line> lines) in _lineByEdge)
        {
            (IBrush? stroke, double thickness) = _lineBase[key];

            foreach (Line line in lines)
            {
                line.Stroke = stroke;
                line.StrokeThickness = thickness;
            }
        }

        if (_selectedEpisodeId is { } episodeId && _cardById.TryGetValue(episodeId, out Border? selected))
        {
            selected.BorderBrush = new SolidColorBrush(Color.Parse("#3D7BD9"));
            selected.BorderThickness = new Thickness(2.4);
        }

        if (_selectedEdgeKey is { } edgeKey && _lineByEdge.TryGetValue(edgeKey, out List<Line>? selectedLines))
        {
            foreach (Line line in selectedLines)
            {
                line.Stroke = new SolidColorBrush(Color.Parse("#3D7BD9"));
                line.StrokeThickness = 3.4;
            }
        }
    }

    /// <summary>편집 칸을 마지막으로 채운 선택. 같은 선택이면 다시 채우지 않는다.</summary>
    private (string? Episode, (string From, string To, string Label)? Edge) _panelFilledFor;

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
                candidate.FromEpisodeId == key.From &&
                candidate.ToEpisodeId == key.To &&
                EdgeLabelKey(candidate) == key.Label)
            : null;

        PropertyPanel.IsVisible = episode is not null;
        EdgePanel.IsVisible = edge is not null;
        NoSelectionText.IsVisible = episode is null && edge is null;
        ApplyEditability();

        (string? Episode, (string From, string To, string Label)? Edge) selection =
            (episode?.EpisodeId,
                edge is null ? null : (edge.FromEpisodeId, edge.ToEpisodeId, EdgeLabelKey(edge)));

        FillDialoguePreview(episode?.EpisodeId);

        bool fill = !preserveTyping || selection != _panelFilledFor;
        _panelFilledFor = selection;

        RefreshConditionList(model);

        if (model is not null && edge is not null)
        {
            // 자동 저장(2026-08-17)이 붙은 뒤로는 이 칸들을 채우는 것도 저장을 부를 수
            // 있다 — 화면을 그리는 일이 파일을 쓰면 안 된다.
            _fillingPanel = true;

            try
            {
                RefreshEdgePanel(model, edge, fill);
            }
            finally
            {
                _fillingPanel = false;
            }
        }

        if (model is null || episode is null)
        {
            return;
        }

        // 콤보를 프로그램이 채우는 동안 자동 저장이 울리면 안 된다 — 고르지도 않은 값이
        // 셀로 나간다. 사람 손이 만든 SelectionChanged만 저장을 부른다.
        _fillingPanel = true;

        try
        {
            if (fill)
            {
                IdBox.Text = episode.EpisodeId;
            }


            SetItems(EdgeTargetCombo,
                model.Episodes
                    .Where(candidate => candidate.EpisodeId != episode.EpisodeId)
                    .Select(candidate => candidate.EpisodeId)
                    .ToList(),
                EdgeTargetCombo.SelectedItem as string); // 도착 고르기는 언제나 사람 소유

            // 문구 목록은 챕터 전체 사전이다 (v9) — 어느 에피소드에서든 다 고를 수 있다.
            SetItems(EdgeLabelBox, ChoiceLabelItems(model), EdgeLabelBox.SelectedItem as string);

            RefreshEdgeList(model, episode);
        }
        finally
        {
            _fillingPanel = false;
        }
    }

    // ── 편집 모드 ───────────────────────────────────────────────────────────
    //
    // 2026-08-16~08-23: 우측 위 [엑셀에서만 편집] 체크(기본 켬)가 문지기였다.
    // 2026-08-24 소유자 — "저걸 툴사용자가 체크하는 게 아니라, 엑셀이 켜지면 자동으로
    // 잠기면서 편집이 불가능하게 막는게 좋겠어."
    //
    // 그래서 체크를 없앴다. 문을 잠그는 것은 <b>사실 하나</b>다: 엑셀이 이 챕터 파일을
    // 잡고 있는가. 체크는 그 사실의 사본이었고, 사본이라 어긋날 수 있었다 — 체크가
    // 풀려 있는데 엑셀이 열려 있으면 툴은 "편집 가능"인 척하다 쓰기에서 거부됐다.
    // 이제 사실은 한 곳에만 산다.

    /// <summary>콤보·목록을 프로그램이 채우는 중 — 자동 저장 억제.</summary>
    private bool _fillingPanel;

    /// <summary>
    /// 엑셀이 이 챕터를 잡고 있는가. <b>이 한 칸이 편집 가능 여부의 전부다</b> —
    /// 사람이 돌릴 스위치는 없다. 답의 주인은 파일이고 이 칸은 마지막으로 물어본 답이다.
    /// </summary>
    private bool _excelHoldsChapter;

    private bool ToolEditable => !_excelHoldsChapter;

    /// <summary>
    /// 엑셀이 잡고 있는지 묻는 손. 진짜 잠긴 파일은 <b>쓸 수도 없어서</b>, "잠기기 직전에
    /// 낸다"를 실제 잠금으로는 검증할 수 없다 — 검증이 이 손을 갈아끼운다.
    /// </summary>
    internal Func<string?, bool> LockProbe { get; set; } = ChapterWorkbookWriter.IsLockedByAnotherApp;

    /// <summary>
    /// 엑셀이 이 챕터를 열고 있으면 배너로 말한다 (2026-08-16 실사례) — 그 상태에서는
    /// 툴의 모든 쓰기가 거부되므로, 누르고 나서가 아니라 <b>누르기 전에</b> 보여야 한다.
    /// 2026-08-24부터는 <b>말하는 데서 그치지 않는다</b>: 이 답이 곧 편집 가능 여부다.
    /// </summary>
    private void RefreshLockBanner()
    {
        bool locked = LockProbe(SelectedChapterPath);

        LockBanner.IsVisible = locked;
        LockBannerText.Text = locked
            ? $"⚠ 엑셀이 '{_selectedChapterId}.xlsx'를 열고 있습니다 — 그동안 툴 편집은 잠깁니다. " +
              "엑셀에서 그 파일을 닫으면 저절로 풀립니다."
            : string.Empty;

        ApplyLockGate(locked);
    }

    /// <summary>
    /// 잠금을 다시 묻고, <b>답이 바뀌었으면 패널까지 그 답에 맞춘다</b>.
    ///
    /// <see cref="RefreshLockBanner"/>와 나눠 둔 이유가 있다: 저쪽은
    /// <see cref="ApplyEditability"/>가 값을 읽기 <em>직전에</em> 부르는 자리라 되짚어
    /// 부르면 스스로를 다시 부른다. 밖에서 들어오는 길(다시 읽기·쓰기 결과·감시자)은
    /// 이쪽으로 온다 — 그때는 화면 전체가 따라와야 한다.
    /// </summary>
    private void RefreshLockState()
    {
        bool before = _excelHoldsChapter;

        RefreshLockBanner();

        if (_excelHoldsChapter != before)
        {
            RefreshPropertyPanel(preserveTyping: true);
        }
    }

    /// <summary>
    /// 잠금 상태를 <b>받아 적는다</b>. 이 메서드는 판단하지 않는다 — 판단은
    /// <see cref="LockProbe"/>가 파일에 물어 이미 끝냈고, 여기서는 그 답이 <em>바뀌었을 때</em>
    /// 해야 할 일만 한다.
    ///
    /// 편집 중에 엑셀이 열리면 <b>아직 단추를 안 누른 값을 먼저 낸다</b> (2026-08-17 소유자:
    /// "하는 중에 엑셀이 열리면 현재까지된걸 저장한 뒤에 잠그고"). 엑셀이 먼저 파일을
    /// 잡았으면 그 쓰기는 거부되지만, 값은 패널에 그대로 남는다 — 엑셀을 닫고 다시 고르면
    /// 된다. 낡은 값을 나중에 몰래 밀어 넣지는 않는다: 그 사이 엑셀에서 고쳤을 수 있고,
    /// 그러면 사람이 방금 한 편집을 툴이 덮는다.
    ///
    /// ⚠ <b>바뀔 때만 움직인다.</b> 이 자리는 다시 그리기·쓰기·감시자 알림마다 불리므로,
    /// 매번 상태줄에 말하면 "엑셀이 닫혔습니다"가 끝없이 흐른다.
    /// </summary>
    private void ApplyLockGate(bool locked)
    {
        // ⛔ 이 빗장을 빼면 <b>스택이 넘친다</b> — 지운 적이 있고 그 자리에서 확인했다
        // (2026-08-24). 길은 이렇다: 여기서 부르는 FlushPendingEdits가 워크북을 쓰고,
        // 쓰기 결과는 Report로 가고, Report는 "거부됐다면 대개 엑셀이 잡은 것"이라며
        // 잠금을 다시 묻는다 — 그래서 여기로 돌아온다. 값이 아직 안 바뀐 채로.
        if (_applyingLockGate || locked == _excelHoldsChapter)
        {
            return;
        }

        _applyingLockGate = true;

        try
        {
            if (locked)
            {
                // 순서가 곧 규칙이다 — 잠갔다고 적기 <b>전에</b> 내야 그 쓰기가 열린
                // 문으로 나간다. 뒤집으면 툴이 자기 관문에 스스로 막힌다.
                FlushPendingEdits();
                _excelHoldsChapter = true;
                return;
            }

            _excelHoldsChapter = false;
            _session?.SetStatus("엑셀이 닫혔습니다 — 툴 편집이 다시 열렸습니다.");
        }
        finally
        {
            _applyingLockGate = false;
        }
    }

    /// <summary>잠금 반영이 자기 자신을 다시 부르지 않게 하는 빗장 — 위 ⛔ 참조.</summary>
    private bool _applyingLockGate;

    /// <summary>
    /// 열려 있는 폼의 값을 낸다 — 잠기기 직전의 마지막 저장. 개명(IdBox)은 일부러 빼둔다:
    /// Enter가 곧 확정인 칸이라, 치다 만 이름을 여기서 확정하면 사람이 안 시킨 개명이 된다.
    /// </summary>
    private void FlushPendingEdits()
    {
        if (_edgeFormIndex >= 0 && _edgeFormEdge is not null && EdgeTargetCombo.SelectedItem is string)
        {
            SubmitEdgeForm();
            return;
        }

        if (EdgePanel.IsVisible && _selectedEdgeKey is not null)
        {
            ApplyEdgeFromPanel();
        }
    }

    private void ApplyEditability()
    {
        // ⚠ 여기서는 배너 쪽만 부른다 — RefreshLockState는 되짚어 이 메서드를 부른다.
        RefreshLockBanner();

        bool editable = ToolEditable;

        IdBox.IsEnabled = editable;

        // 잠겨 있으면 **그 자리에서** 이유를 말한다 (2026-08-24). 비활성 칸이 아무 말도
        // 없으면 "이 기능이 없다"로 읽힌다 — 실제로 그렇게 읽혔다.
        //
        // ⚠ 열려 있을 때는 <b>아무 말도 안 한다</b>(같은 날 소유자 — 상시 안내 제거).
        // 잠금은 사람이 안 한 일이라 말해 줘야 하지만, 열린 것은 기본값이라 말할 것이 없다.
        IdBoxHint.IsVisible = !editable;
        IdBoxHint.Text = editable
            ? string.Empty
            : "이름 — 엑셀이 이 챕터를 열고 있어 잠겼습니다";
        VisibleCombo.IsEnabled = editable;
        UnlockCombo.IsEnabled = editable;
        AddNextEdgeButton.IsVisible = editable;
        // 삭제는 <b>늘 같은 자리에</b> 있고 상태만 바뀐다 (2026-08-16 소유자 보고) —
        // 체크를 푸는 순간 단추가 튀어나오면 그 자리를 누르던 손이 삭제를 누른다.
        // 평소엔 회색·비활성, 편집이 열리면 빨갛게 살아난다.
        DeleteEpisodeButton.IsEnabled = editable;
        DeleteEpisodeButton.Background = editable
            ? new SolidColorBrush(Color.Parse("#C0392B"))
            : new SolidColorBrush(Color.Parse("#E0E0E0"));
        DeleteEpisodeButton.Foreground = editable ? Brushes.White : new SolidColorBrush(Color.Parse("#9A9A9A"));
        AddEpisodeButton.IsEnabled = editable;

        // 간선 패널(그래프에서 간선 클릭)도 같은 스위치를 탄다.
        EdgeLabelEditBox.IsEnabled = editable;
        EdgeVisibleCombo.IsEnabled = editable;
        EdgeConditionCombo.IsEnabled = editable;
        EdgeHideCheck.IsEnabled = editable;
        EdgeLockedMsgBox.IsEnabled = editable;
        _edgeStats.Editable = editable;
        _formStats.Editable = editable;
        EdgeDeleteButton.IsVisible = editable;

        if (!editable)
        {
            HideEdgeForm();
        }
    }

    /// <summary>표시·해금 콤보의 자동 저장 — 고르는 순간 그 셀 하나가 써진다.</summary>
    private void AutoSaveGates()
    {
        if (_fillingPanel || !ToolEditable)
        {
            return;
        }

        UiGuard.Run(_session, "조건 저장", ApplyEpisodeFromPanel);
    }

    /// <summary>
    /// 간선 패널의 자동 저장 (2026-08-17) — 고치는 순간 그 셀이 써진다. 패널을 채우는
    /// 중(<see cref="_fillingPanel"/>)이나 읽기 전용일 때는 울리지 않는다: 화면을 채우는
    /// 일이 저장을 부르면 아무것도 안 고쳤는데 파일이 계속 써진다.
    /// </summary>
    private void AutoSaveEdge()
    {
        if (_fillingPanel || !ToolEditable || _selectedEdgeKey is null)
        {
            return;
        }

        UiGuard.Run(_session, "간선 저장", ApplyEdgeFromPanel);
    }

    /// <summary>
    /// [대사] 탭 — 선택된 에피소드의 대사를 워크북에서 바로 읽어 세운다. 연출 그래프까지
    /// 안 가도 챕터 그래프에서 방금 쓴 대사를 확인한다(소유자 요청). 읽기 전용이고 고치는
    /// 곳은 엑셀이다. 선택이 없거나 대본이 비면 그 사실을 말한다.
    /// </summary>
    private void FillDialoguePreview(string? episodeId)
    {
        if (episodeId is null)
        {
            DialoguePreviewHeader.Text = "에피소드를 선택하면 그 대사가 여기 보입니다.";
            DialoguePreviewText.Text = string.Empty;
            return;
        }

        string preview = string.Empty;
        string? folder = SelectedEpisodesFolder;

        if (folder is not null && EpisodeLibrary.FindExisting(folder, episodeId) is { } path)
        {
            try
            {
                preview = PreviewText(EpisodeWorkbookReader.Read(path).Rows);
            }
            catch (XlsxReadException exception)
            {
                preview = $"대본을 읽지 못했습니다: {exception.Message}";
            }
        }

        DialoguePreviewHeader.Text = preview.Length > 0
            ? "읽기 전용 · 고치는 곳은 엑셀 (노드 더블클릭)"
            : "아직 적힌 대사가 없습니다 — 노드를 더블클릭해 엑셀에서 쓰세요";
        DialoguePreviewText.Text = preview;
    }

    /// <summary>
    /// 대본을 읽는 눈 기준으로 편다 (v10) — 조건 블록은 들여쓰기로 보인다. 시트에서는
    /// IF와 END가 각자 한 행이지만, 읽을 때 중요한 것은 <b>어디까지가 조건 안인가</b>다.
    /// </summary>
    private static string PreviewText(IReadOnlyList<EpisodeRow> rows)
    {
        var lines = new List<string>();
        int depth = 0;

        static string Indent(int level) => new(' ', Math.Max(0, level) * 2);

        foreach (EpisodeRow row in rows.Where(row => !row.IsBlank))
        {
            switch (row.Kind)
            {
                case EpisodeRowKind.End:
                    // 닫는 줄은 안 세운다 — 들여쓰기가 이미 그 말을 한다.
                    depth = Math.Max(0, depth - 1);
                    break;

                case EpisodeRowKind.If:
                    lines.Add(Indent(depth) + $"IF {row.ConditionLabel}");
                    depth++;
                    break;

                // 2026-08-17 소유자 보고 — ELSEIF가 안 보였다. 화자·내용이 비어 있어
                // 아래 대사 가지에서 빈 줄로 걸러졌다. 표지는 같은 체인의 <b>바깥</b>
                // 깊이에 서고 깊이는 변하지 않는다(평평화의 <<elseif>>와 같은 자리).
                case EpisodeRowKind.ElseIf:
                    lines.Add(Indent(depth - 1) + $"ELSEIF {row.ConditionLabel}");
                    break;

                default:
                    string body = row.Speaker.Length > 0 ? $"{row.Speaker}: {row.Text}" : row.Text;

                    if (body.Length > 0)
                    {
                        lines.Add(Indent(depth) + body);
                    }

                    break;
            }
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// 엑셀이 소유한 값들을 한 덩어리로. 비어 있는 것은 줄에 세우지 않는다 —
    /// "(없음)"만 늘어놓으면 읽을거리가 아니라 소음이 된다.
    /// </summary>

    /// <summary>목록을 갈되 고른 값은 지킨다(그 값이 새 목록에 남아 있다면).</summary>
    private static void SetItems(ComboBox combo, IReadOnlyList<string> items, string? selected)
    {
        combo.ItemsSource = items;
        combo.SelectedItem = selected is not null && items.Contains(selected) ? selected : null;
    }

    // ── 선택지 목록 + 잇기 폼 (v9, 2026-08-17 소유자) ────────────────────────
    // 목록의 줄 = 이 에피소드에서 나가는 길 하나이고, 길 하나가 곧 선택지 하나다.
    // 문구는 챕터 전체의 사전에서 고른다 — "모든 선택지 중에서 자유자재로."

    /// <summary>문구 없는 길(보이지 않는 기본)을 드롭다운에서 가리키는 이름.</summary>
    private const string PlainAdvanceItem = "(문구 없음 · 보이지 않는 기본)";

    /// <summary>폼이 붙은 간선의 신원 (도착, 문구). null이면 <b>새 선택지</b>를 여는 중이다.</summary>
    private (string To, string Label)? _edgeFormEdge;

    /// <summary>목록 안에서 폼이 설 자리. -1 = 닫힘.</summary>
    private int _edgeFormIndex = -1;

    private void HideEdgeForm()
    {
        _edgeFormIndex = -1;
        _edgeFormEdge = null;
        EdgeFormHost.IsVisible = false;
        EdgeFormPanel.IsVisible = false;
    }

    /// <summary>[＋] — 빈 폼을 목록 끝에 연다. 문구와 도착을 고르면 그때 길이 생긴다.</summary>
    private void AddChoiceSlotFromPanel()
    {
        if (_selectedEpisodeId is null || SelectedChapterPath is null)
        {
            _session?.SetStatus("에피소드를 먼저 골라 주세요.");
            return;
        }

        // 이미 새 선택지 폼이 열려 있으면 접는다 — 여는 손과 닫는 손이 같다.
        if (_edgeFormIndex >= 0 && _edgeFormEdge is null)
        {
            HideEdgeForm();
            RefreshPropertyPanel(preserveTyping: true);
            return;
        }

        OpenEdgeForm(edge: null, rowIndex: int.MaxValue - 1);
    }

    /// <summary>
    /// 줄 클릭 — 그 길의 문구·도착을 고치는 폼이 줄 바로 아래에 열린다. 문구 드롭다운은
    /// <b>챕터의 모든 문구</b>를 담는다 (2026-08-17 소유자: "자기 것만 고르는 게 아니라
    /// 모든 선택지 중에서 자유자재로"). <paramref name="edge"/>가 null이면 새 선택지다.
    /// </summary>
    internal void OpenEdgeForm(ChapterEdge? edge, int rowIndex)
    {
        _edgeFormEdge = edge is null ? null : (edge.ToEpisodeId, EdgeLabelKey(edge));
        _edgeFormIndex = rowIndex + 1;
        RefreshPropertyPanel(preserveTyping: true);

        _fillingPanel = true;

        try
        {
            EdgeTargetCombo.SelectedItem = edge?.ToEpisodeId;
            EdgeLabelBox.SelectedItem = edge is null
                ? null
                : edge.HasNoOptionLabel ? PlainAdvanceItem : edge.OptionLabel;

            // 스탯변화도 여기서 만진다 (2026-08-17) — 새 길이면 빈 목록에서 시작한다.
            _formStats.Editable = ToolEditable;
            _formStats.Load(SelectedModel?.Stats ?? [], edge?.StatChanges ?? []);
        }
        finally
        {
            _fillingPanel = false;
        }
    }

    /// <summary>문구 드롭다운의 목록 — 챕터 사전 전체 + 보이지 않는 기본.</summary>
    private static List<string> ChoiceLabelItems(ChapterGraphModel model)
    {
        var items = new List<string> { PlainAdvanceItem };
        items.AddRange(model.ChoiceLabels);
        return items;
    }

    /// <summary>폼의 [잇기]/[수정] — 이 길의 문구와 도착을 한 저장으로 정한다.</summary>
    internal void SubmitEdgeForm()
    {
        if (_edgeFormIndex < 0 ||
            _selectedEpisodeId is not { } from ||
            SelectedChapterPath is not { } path)
        {
            _session?.SetStatus("선택지 줄을 다시 눌러 주세요. 선택이 풀렸습니다.");
            return;
        }

        if (EdgeTargetCombo.SelectedItem is not string to)
        {
            _session?.SetStatus("도착 에피소드를 골라 주세요.");
            return;
        }

        // 문구를 안 고른 것과 "문구 없음"을 고른 것은 같은 뜻으로 본다 — 둘 다 자동 진행.
        string label = EdgeLabelBox.SelectedItem as string is { } picked && picked != PlainAdvanceItem
            ? picked
            : string.Empty;

        // 배선과 증감이 한 저장으로 나간다 — 두 번 쓰면 그 사이에 엑셀이 파일을 잡을 수 있고,
        // 반쪽만 적힌 길이 남는다.
        string stats = _formStats.ToSheetText();

        ChapterWriteResult result = _edgeFormEdge is { } current
            ? ChapterWorkbookWriter.SetEdgeRoute(path, from, current.To, current.Label, to, label, stats)
            : ChapterWorkbookWriter.AddEdge(path, from, to, optionLabel: label, statChanges: stats);

        if (result.Written)
        {
            HideEdgeForm();
        }

        Report(result, label.Length > 0
            ? $"'{label}' → {to} 로 이었습니다."
            : $"{from} → {to} 를 보이지 않는 기본으로 이었습니다.");
    }

    private void RefreshEdgeList(ChapterGraphModel model, ChapterEpisode episode)
    {
        // 폼(EdgeFormPanel)은 axaml이 만든 단일 인스턴스다 — Clear는 트리에서 떼어낼 뿐이라
        // 아래에서 원하는 자리에 다시 꽂는다.
        EdgeListPanel.Children.Clear();

        bool editable = ToolEditable;
        List<ChapterEdge> edges = model.Edges
            .Where(candidate =>
                string.Equals(candidate.FromEpisodeId, episode.EpisodeId, StringComparison.Ordinal))
            .ToList();
        ChoiceHeaderText.Text = edges.Count > 0 ? $"선택지 ({edges.Count})" : "선택지";

        for (int rowIndex = 0; rowIndex < edges.Count; rowIndex++)
        {
            EdgeListPanel.Children.Add(BuildEdgeRow(edges[rowIndex], rowIndex, editable));
        }

        if (edges.Count == 0)
        {
            EdgeListPanel.Children.Add(new TextBlock
            {
                Text = "나가는 길이 없습니다 — 여기서 챕터가 끝납니다. [＋]로 선택지를 엽니다.",
                FontSize = 10,
                Opacity = 0.5,
                TextWrapping = TextWrapping.Wrap
            });
        }

        // 폼을 제자리에 꽂는다 — 누른 줄 바로 아래(새 선택지면 목록 끝).
        bool formOpen = editable && _edgeFormIndex >= 0;
        EdgeFormHost.IsVisible = formOpen;
        EdgeFormPanel.IsVisible = formOpen;

        if (formOpen)
        {
            // 라벨은 둘뿐이다 — 있는 길은 [수정], 새로 여는 길은 [잇기].
            AddEdgeButton.Content = _edgeFormEdge is null ? "잇기" : "수정";
            EdgeTargetCombo.IsEnabled = true;

            _fillingPanel = true;

            try
            {
                // 문구 목록은 폼이 열릴 때마다 새로 깐다 — 엑셀에서 사전에 낱말을 더하면
                // 다시 읽기 한 번으로 여기에 나타난다.
                SetItems(EdgeLabelBox, ChoiceLabelItems(model), EdgeLabelBox.SelectedItem as string);
            }
            finally
            {
                _fillingPanel = false;
            }

            EdgeListPanel.Children.Insert(
                Math.Min(_edgeFormIndex, EdgeListPanel.Children.Count), EdgeFormHost);
        }
        else
        {
            EdgeListPanel.Children.Add(EdgeFormHost); // 트리 밖에 두지 않는다 — 자리만 숨김
        }
    }

    /// <summary>
    /// 선택지 한 줄 — `문구 → 도착 [조건]`. 문구가 없으면 "(기본 · 보이지 않음)"이다.
    /// 클릭 = 그 줄 아래에서 문구·도착 고치기, 다시 클릭 = 접기.
    /// </summary>
    private Control BuildEdgeRow(ChapterEdge edge, int rowIndex, bool editable)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

        // 순번 — 화면에 뜨는 차례다(간선 시트의 행 순서). 신원이 아니라 자리 표시다.
        row.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse(edge.HasNoOptionLabel ? "#22808080" : "#22C06A14")),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            Child = new TextBlock
            {
                Text = (rowIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                FontSize = 10,
                Opacity = 0.8
            }
        });

        string label = edge.HasNoOptionLabel ? "(기본 · 보이지 않음)" : edge.OptionLabel!;
        string condition = edge.ConditionLabel is { } gate ? $"  [{gate}]" : string.Empty;

        var text = new TextBlock
        {
            Text = $"{label}   → {edge.ToEpisodeId}{condition}",
            FontSize = 11,
            Opacity = edge.HasNoOptionLabel ? 0.7 : 1,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            // 배경 없는 TextBlock은 글자 획 위만 클릭된다 — 행 전체가 눌리게 깔아 둔다.
            Background = Brushes.Transparent,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        Grid.SetColumn(text, 1);

        // 툴팁 없음 (2026-08-16 소유자) — 목록 위에 뜨는 설명 상자가 화면을 가렸다.
        if (editable)
        {
            (string To, string Label) key = (edge.ToEpisodeId, EdgeLabelKey(edge));
            ChapterEdge captured = edge;
            int capturedRow = rowIndex;
            // 같은 줄을 다시 누르면 접힌다 (2026-08-16 소유자) — 여는 손과 닫는 손이 같다.
            text.PointerPressed += (_, _) =>
                UiGuard.Run(_session, "선택지 잇기 폼", () =>
                {
                    if (_edgeFormEdge == key)
                    {
                        HideEdgeForm();
                        RefreshPropertyPanel(preserveTyping: true);
                        return;
                    }

                    OpenEdgeForm(captured, capturedRow);
                });
        }

        row.Children.Add(text);

        if (editable)
        {
            string from = edge.FromEpisodeId;
            string to = edge.ToEpisodeId;
            string optionLabel = EdgeLabelKey(edge);
            var remove = new Button
            {
                Content = "✕",
                FontSize = 10,
                Padding = new Thickness(5, 1),
                [ToolTip.TipProperty] = "이 길을 지웁니다. 선택지 문구는 사전에 남습니다."
            };
            Grid.SetColumn(remove, 2);
            remove.Click += (_, _) => UiGuard.Run(_session, "선택지 삭제", () =>
                Report(ChapterWorkbookWriter.RemoveEdge(SelectedChapterPath!, from, to, optionLabel),
                    $"{from} → {to} 를 지웠습니다."));
            row.Children.Add(remove);
        }

        return row;
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
            EdgeHideCheck.IsChecked = edge.HideWhenLocked;
            EdgeLockedMsgBox.Text = edge.LockedMessage ?? string.Empty;
            _edgeStats.Editable = ToolEditable;
            _edgeStats.Load(model.Stats, edge.StatChanges);
        }

        // 이 길의 문구 (v9) — 챕터의 모든 문구 중에서 고른다. 목록·저장이 선택지 목록의
        // 폼과 같은 규칙 하나를 쓴다(사본 금지): 안 고른 것과 "문구 없음"은 같은 뜻이다.
        SetItems(EdgeLabelEditBox, ChoiceLabelItems(model),
            fill
                ? edge.HasNoOptionLabel ? PlainAdvanceItem : edge.OptionLabel
                : EdgeLabelEditBox.SelectedItem as string);

        var labels = new List<string> { "(없음)" };
        labels.AddRange(model.Conditions.Select(condition => condition.Label));

        // 관문 둘 — v8에서 에피소드에서 여기로 옮겨 왔다.
        SetItems(EdgeVisibleCombo, labels,
            fill ? edge.VisibleConditionLabel ?? "(없음)" : EdgeVisibleCombo.SelectedItem as string);
        SetItems(EdgeConditionCombo, labels,
            fill ? edge.ConditionLabel ?? "(없음)" : EdgeConditionCombo.SelectedItem as string);
    }

    /// <summary>
    /// 간선 패널에서 그 끝의 에피소드로 건너뛰는 고리. 누르면 그 에피소드가 선택되어
    /// 속성 패널(Id [이름] 칸·제목)이 열린다 — 간선을 보다가 에피소드 이름을 고치려 할 때
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
                candidate.FromEpisodeId == key.From &&
                candidate.ToEpisodeId == key.To &&
                EdgeLabelKey(candidate) == key.Label) is not { } edge)
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

        string? Gate(ComboBox combo) =>
            combo.SelectedItem as string == "(없음)" ? string.Empty : combo.SelectedItem as string;

        // 문구도 이 저장에 실린다 (v9) — 신원의 일부라, 찾을 때는 고치기 전 값을 쓴다.
        string pickedLabel = EdgeLabelEditBox.SelectedItem as string is { } picked && picked != PlainAdvanceItem
            ? picked
            : string.Empty;

        ChapterWriteResult result = ChapterWorkbookWriter.UpdateEdge(
            path, key.From, key.To,
            visibleConditionLabel: Changed(Gate(EdgeVisibleCombo), edge.VisibleConditionLabel ?? string.Empty),
            conditionLabel: Changed(Gate(EdgeConditionCombo), edge.ConditionLabel ?? string.Empty),
            hideWhenLocked: EdgeHideCheck.IsChecked == edge.HideWhenLocked ? null : EdgeHideCheck.IsChecked,
            lockedMessage: Changed(EdgeLockedMsgBox.Text, edge.LockedMessage ?? string.Empty),
            statChanges: Changed(_edgeStats.ToSheetText(), StatChangesText(edge)),
            matchOptionLabel: EdgeLabelKey(edge),
            optionLabel: Changed(pickedLabel, EdgeLabelKey(edge)));

        // 문구를 바꿨으면 신원도 바뀌었다 — 선택이 풀리지 않게 열쇠를 따라 옮긴다.
        if (result.Written)
        {
            _selectedEdgeKey = (key.From, key.To, pickedLabel);
        }

        Report(result, $"간선 {key.From}→{key.To}을 저장했습니다.");

        // 쓴 뒤에는 판을 다시 읽는다. 자동 저장(2026-08-17)이 붙으면서 한 번 고칠 때마다
        // 신원(문구)이 바뀔 수 있는데, 손에 든 모델이 낡은 채로 남으면 <b>다음</b> 저장이
        // 그 간선을 못 찾는다 — 문구를 바꾸고 곧바로 조건을 고르면 조건이 안 써졌다
        // (테스트가 잡았다). 감시자가 어차피 곧 다시 읽지만, 그 사이를 비워 두면 안 된다.
        if (result.Written)
        {
            Reload();
        }
    }

    /// <summary>간선 스탯변화를 시트 문법 그대로 — 패널 칸과 셀이 같은 글을 쓴다.</summary>
    private static string StatChangesText(ChapterEdge edge) => string.Join("; ", edge.StatChanges
        .Select(delta => $"{delta.Key} {(delta.Amount >= 0 ? "+" : "")}{delta.Amount}"));

    /// <summary>
    /// 에피소드 값 저장 — 지금 이 패널에서 툴이 쓰는 값은 없다. 제목·엔딩키·메모는 칸을
    /// 뺐고(2026-08-16), 표시·해금조건은 v8에서 길(간선)로 옮겨 갔다. 개명은 Enter가,
    /// 선택지는 목록이 맡는다. 라이터 경로는 살아 있으므로 필요하면 여기로 되돌아온다.
    /// </summary>
    internal void ApplyEpisodeFromPanel()
    {
        if (_selectedEpisodeId is null || SelectedChapterPath is null)
        {
            _session?.SetStatus("에피소드를 다시 골라 주세요. 선택이 풀렸거나 그 에피소드가 사라졌습니다.");
        }
    }

    internal void DeleteSelectedEdge()
    {
        if (_selectedEdgeKey is not { } deleteKey || SelectedChapterPath is not { } deletePath)
        {
            return;
        }

        ChapterWriteResult result = ChapterWorkbookWriter.RemoveEdge(
            deletePath, deleteKey.From, deleteKey.To, deleteKey.Label);

        if (result.Written)
        {
            _selectedEdgeKey = null;
        }

        Report(result, $"간선 {deleteKey.From}→{deleteKey.To}을 지웠습니다.");
    }


    /// <summary>
    /// [챕터] 탭의 읽기 전용 스탯 표. 어디에서도 값이 안 보이던 것이라 여기 세운다
    /// (소유자 점검). 에피소드·간선은 그래프가 이미 그리므로 반복하지 않는다.
    ///
    /// ⛔ 픽스처 표는 2026-08-24에 걷었다 (소유자 — 한 번도 안 썼다).
    /// </summary>
    private void RefreshChapterSheets(ChapterGraphModel? model)
    {
        StatListPanel.Children.Clear();

        static SelectableTextBlock SheetLine(string text, bool dim = false) => new()
        {
            Text = text,
            FontSize = 10,
            Opacity = dim ? 0.55 : 0.75,
            TextWrapping = TextWrapping.Wrap
        };

        foreach (ChapterStat stat in model?.Stats ?? Enumerable.Empty<ChapterStat>())
        {
            string name = stat.DisplayName.Length > 0 && stat.DisplayName != stat.Key
                ? $"{stat.Key} ({stat.DisplayName})"
                : stat.Key;
            StatListPanel.Children.Add(SheetLine($"{name} — 초기 {stat.Initial} · 범위 {stat.Minimum}~{stat.Maximum}"));
        }

        if (StatListPanel.Children.Count == 0)
        {
            StatListPanel.Children.Add(SheetLine("스탯 시트가 비어 있습니다.", dim: true));
        }

    }

    private void RefreshConditionList(ChapterGraphModel? model)
    {
        RefreshChapterSheets(model);
        ConditionListPanel.Children.Clear();

        // 읽기 전용 표다 (2026-08-16 소유자) — 편집은 챕터 엑셀의 `조건` 시트에서.
        foreach (ChapterCondition condition in model?.Conditions ?? Enumerable.Empty<ChapterCondition>())
        {
            ConditionListPanel.Children.Add(new SelectableTextBlock
            {
                Text = $"{condition.Label} = {condition.Expression}",
                FontSize = 10,
                Opacity = 0.75,
                TextWrapping = TextWrapping.Wrap
            });
        }

        if (ConditionListPanel.Children.Count == 0)
        {
            ConditionListPanel.Children.Add(new TextBlock
            {
                Text = "조건이 없습니다 — 챕터 엑셀의 `조건` 시트에서 추가합니다.",
                FontSize = 10,
                Opacity = 0.5
            });
        }
    }

    /// <summary>속성 패널의 [적용]. 모델의 현재 값과 다른 필드만 셀에 쓴다.</summary>
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

        string? episodesFolder = SelectedEpisodesFolder;

        // 새 이름의 대본 파일이 이미 있으면 시작도 하지 않는다 — 챕터만 개명된 채
        // 원고가 옛 이름에 남는 어중간한 상태를 만들지 않는다.
        if (episodesFolder is not null &&
            EpisodeLibrary.FindExisting(episodesFolder, oldId) is not null &&
            EpisodeLibrary.FindExisting(episodesFolder, newId) is not null)
        {
            _session?.SetStatus(
                $"'{newId}' 이름의 대본 파일이 이미 있어 개명하지 않았습니다. 파일을 먼저 정리해 주세요.");
            return;
        }

        // 대본 파일을 <b>먼저</b> 옮긴다 — 엑셀이 잠그고 있으면 여기서 전부 멈춘다. 챕터만
        // 개명된 채 원고가 옛 이름에 남으면, 새 이름을 여는 순간 빈 워크북이 생겨 원고가
        // 고아가 된다(실사례 2026-08-15: new02 원고가 남고 빈 rrr.xlsx가 생겼다).
        string? moveFailure = episodesFolder is null
            ? null
            : EpisodeLibrary.RenameWorkbook(episodesFolder, oldId, newId);

        if (moveFailure is not null)
        {
            _session?.SetStatus($"개명하지 않았습니다 — {moveFailure}");
            return;
        }

        ChapterWriteResult result = ChapterWorkbookWriter.RenameEpisode(path, oldId, newId);

        if (!result.Written)
        {
            // 챕터 쪽이 거부됐다 — 옮긴 대본 파일을 되돌려 원상태로 맞춘다.
            if (episodesFolder is not null)
            {
                EpisodeLibrary.RenameWorkbook(episodesFolder, newId, oldId);
            }

            Report(result, string.Empty);
            return;
        }

        _selectedEpisodeId = newId;

        // 대사 노드도 따라간다 — 규약(대사엔트리 = Id)을 따르던 노드만. 노드를 새로 만들지
        // 않고 이름만 바꾸므로 줄·연출·행 신원(ExcelLineMap)이 전부 보존된다. 엑셀 표식
        // (ExcelEpisodeId)도 함께 간다 — 옛 Id로 남으면 연출 그래프가 챕터 밖 노드로
        // 보고 레일을 끊는다.
        if (_session is { } session &&
            session.Project.EnumerateNodes().OfType<Vn.Authoring.Model.DialogueNode>()
                .FirstOrDefault(node => string.Equals(node.Name, oldId, StringComparison.Ordinal))
            is { } dialogueNode)
        {
            if (dialogueNode.ExcelEpisodeId is not null)
            {
                dialogueNode.ExcelEpisodeId = newId;
            }

            session.Editor.RenameNode(dialogueNode.Id, newId);
        }

        _session?.SetStatus(
            $"'{oldId}' → '{newId}' 개명했습니다. 간선·픽스처·대본 파일·대사 노드가 함께 따라갔습니다.");
    }

    /// <summary>도착만 주고 잇기 — 문구 없는 길(보이지 않는 기본)이 선다.</summary>
    internal void AddEdgeFromPanel()
    {
        if (_selectedEpisodeId is not { } from ||
            EdgeTargetCombo.SelectedItem is not string to ||
            SelectedChapterPath is not { } path)
        {
            _session?.SetStatus("간선을 추가하려면 도착 에피소드를 골라 주세요.");
            return;
        }

        // v12 — 문구 없이 길을 놓지 않는다.
        Report(ChapterWorkbookWriter.AddEdge(path, from, to, optionLabel: DefaultOptionLabel),
            $"간선 {from}→{to}을 '{DefaultOptionLabel}'로 더했습니다 — 문구는 그 줄을 눌러 고칩니다.");
    }

    internal void DeleteSelectedEpisode()
    {
        if (_selectedEpisodeId is not { } episodeId || SelectedChapterPath is not { } path)
        {
            // 조용한 무동작 금지 — 단추가 늘 떠 있으므로(2026-08-16) 대상이 없다는 것을 말한다.
            _session?.SetStatus("지울 에피소드를 판에서 먼저 골라 주세요.");
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
    /// 사람이 패널의 [이름] 칸에서 고쳐 정하게 한다(Enter로 확정).
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

        // ⭐ v12 (2026-08-24 소유자) — **에피소드를 더하면 간선이 함께 선다.** 고른 것이
        // 없으면 마지막 에피소드에서 잇는다: 떨어진 섬을 만들지 않는다(도달성 증명이 곧바로
        // 오류로 짚을 자리이기도 하다). 챕터가 비어 있을 때만 간선 없이 첫 카드가 선다.
        string? parent = _selectedEpisodeId is { } id && model.FindEpisode(id) is not null
            ? id
            : model.Episodes.Count > 0 ? model.Episodes[^1].EpisodeId : null;

        ChapterWriteResult result;

        if (parent is not null)
        {
            (double x, double y) = ChapterBranchPlanner.SuggestPlacement(model, parent);
            // 문구를 함께 준다 — v12에서 모든 길은 선택지이고, 빈 문구는 오류다.
            result = ChapterWorkbookWriter.AddNextEpisode(
                path, parent, episodeId, title: string.Empty, x, y, optionLabel: DefaultOptionLabel);
        }
        else
        {
            double x = model.Episodes.Count == 0 ? 0 : model.Episodes.Max(episode => episode.X) + 220;
            result = ChapterWorkbookWriter.AddEpisode(path, episodeId, title: string.Empty, x, 0);
        }

        if (result.Written)
        {
            _selectedEpisodeId = episodeId;

            // 대본 워크북도 지금 만든다 (v4) — "노드를 클릭해야 생긴다"는 느슨함을 없앤다.
            // 생성은 없던 파일을 만드는 것이라 단일 writer 원칙과 충돌하지 않고,
            // 이후 툴은 이 파일을 다시는 쓰지 않는다.
            if (SelectedEpisodesFolder is { } episodesFolder &&
                EpisodeLibrary.EnsureWorkbook(
                    episodesFolder,
                    episodeId,
                    ProjectSpeakerNames(),
                    model.Conditions.Select(condition => condition.Label).ToList()))
            {
                StartWatchingEpisodes(EpisodeLibrary.FolderFor(_session?.ProjectPath));
            }
        }

        Report(result, $"'{episodeId}' 행을 더했습니다. Id와 대사엔트리를 패널에서 채워 주세요.");
    }

    /// <summary>
    /// 쓰기 결과를 상태줄로 + 성공이면 판을 다시 읽는다. 챕터 워크북을 바꾸는 길은
    /// 전부 여기로 모인다 — 갱신을 한 자리에서 챙기려고 모아 둔 길목이다.
    /// </summary>
    private void Report(ChapterWriteResult result, string success)
    {
        _session?.SetStatus(result.Written ? success : result.Failure!);

        // 거부됐다면 대개 엑셀이 잡고 있어서다 — 그 사실을 배너로도 세운다(상태줄은 묻힌다).
        RefreshLockState(); // 엑셀을 열거나 닫으면 그 사실이 여기로 온다

        // 방금 우리가 워크북을 바꿨다 — 판을 다시 읽어야 화면이 파일과 같은 말을 한다.
        //
        // <b>예전에는 이 줄이 없어도 됐다</b>: 바로 위 SetStatus가 "프로젝트가 바뀌었다"고
        // 방송했고 그 신호에 재읽기가 딸려 왔다. 상태 한 줄이 화면 갱신을 대신하던
        // <b>우연한 배선</b>이었고, 그 우연이 노드 60개에서 58초를 만든 정체이기도 하다
        // (2026-08-18). 이제는 쓴 쪽이 자기 입으로 말한다 — 쓴 자리가 곧 아는 자리다.
        //
        // 감시자도 이 저장을 잡아 같은 길로 오지만, 그쪽은 파일 사건을 기다리느라
        // 한 박자 늦다. 툴이 누른 단추는 그 자리에서 보여야 한다.
        if (result.Written)
        {
            QueueReload();
        }
    }

    /// <summary>간선 하나를 가리키는 표식. 화면 검증이 이 이름으로 간선을 찾는다.</summary>
    internal static string EdgeTag(ChapterEdge edge) =>
        $"{edge.FromEpisodeId}→{edge.ToEpisodeId}" +
        (edge.HasNoOptionLabel ? string.Empty : $" [{edge.OptionLabel}]");

    /// <summary>그 에피소드로 들어오는 길 가운데 관문이 걸린 것들 (v8 — 관문은 길의 것).</summary>
    private static List<ChapterEdge> GatedIncoming(ChapterGraphModel model, ChapterEpisode episode) =>
        model.Edges
            .Where(edge =>
                string.Equals(edge.ToEpisodeId, episode.EpisodeId, StringComparison.Ordinal) &&
                edge.HasGate)
            .ToList();

    private static string GateSummary(IReadOnlyList<ChapterEdge> gated) => string.Join("\n",
        gated.Select(edge => string.Join(" · ", new[]
        {
            $"{edge.FromEpisodeId} →",
            edge.VisibleConditionLabel is null ? null : $"표시: {edge.VisibleConditionLabel}",
            edge.ConditionLabel is null ? null : $"해금: {edge.ConditionLabel}"
        }.Where(part => part is not null))));

    private static string Tooltip(ChapterGraphModel model, ChapterEpisode episode)
    {
        var lines = new List<string>
        {
            $"{episode.EpisodeId} ({episode.SourceRow}행)",
            $"대사엔트리: {episode.DialogueEntry}",
            $"위치: X={episode.X:0.##} Y={episode.Y:0.##}",
            $"선택지 {model.Edges.Count(edge => string.Equals(edge.FromEpisodeId, episode.EpisodeId, StringComparison.Ordinal))}개"
        };

        string gate = GateSummary(GatedIncoming(model, episode));

        if (gate.Length > 0)
        {
            lines.Add("들어오는 길의 관문 —");
            lines.Add(gate);
        }

        if (model.EndingKeyOf(episode.EpisodeId) is { } endingKey)
        {
            lines.Add($"엔딩키: {endingKey} (간선이 소유 — v11)");
        }

        if (!string.IsNullOrWhiteSpace(episode.Memo))
        {
            lines.Add($"메모: {episode.Memo}");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// 머리글의 오류 표식 — <b>빨강</b> (2026-08-24 소유자: "관례대로 오류를 빨강,
    /// 경고를 노랑으로").
    ///
    /// 색을 <b>이모지로</b> 내는 이유: 머리글이 문자열 하나라 글자마다 색을 줄 자리가
    /// 없다. 표식을 글에 심으면 그 한 줄이 색을 갖는다.
    ///
    /// ⛔ <b>이 표식이 유일한 알림 창구다.</b> 같은 날 자동 펼침을 껐으므로(사람이 접어 둔
    /// 것을 저장할 때마다 다시 열던 규칙), 여기서 표식을 빼면 오류가 조용해진다.
    /// </summary>
    private const string ErrorMark = "🔴";

    /// <summary>머리글의 경고 표식 — <b>노랑</b>. 위 ⛔ 참조.</summary>
    private const string WarningMark = "🟡";

    /// <summary>
    /// 에피소드 동기화 보고를 먼저, 그 뒤에 오류·경고·정보를 심각도 순으로. 동기화는 사람이
    /// 방금 한 행동의 결과인데 목록 끝에 두면 알림 더미에 묻혀 스크롤 밖으로 나간다(실사례 —
    /// "숫자는 2개라는데 볼 방법이 없어"). 각 줄이 파일·시트·행·열을 그대로 말하고,
    /// 머리글의 표식이 오류·경고를 든다.
    /// </summary>
    private void DrawDiagnostics(ChapterGraphModel model)
    {
        // 워크북 읽기 진단 + 검증기(구조·교차·도달성) 진단을 한 목록으로 본다.
        // 같은 진단이 두 곳에서 나올 수 있으므로(리더가 낸 것을 검증기가 다시 담는다) 겹은 지운다.
        List<ChapterDiagnostic> all = model.Diagnostics
            .Concat(_validation?.All ?? Enumerable.Empty<ChapterDiagnostic>())
            .Concat(_boardWarnings)
            .Distinct()
            .ToList();

        int errors = all.Count(item => item.Severity == ChapterDiagnosticSeverity.Error);
        int warnings = all.Count(item => item.Severity == ChapterDiagnosticSeverity.Warning);
        int rejected = _syncReports.Sum(report => report.RejectionCount);

        string? exportNotice = ExportNotice();

        var header = new System.Text.StringBuilder("검증 보고");

        if (errors > 0)
        {
            header.Append($" · {ErrorMark} 오류 {errors}");
        }

        if (warnings > 0)
        {
            header.Append($" · {WarningMark} 경고 {warnings}");
        }

        // ⚠ "동기화 N건 반영"은 <b>더 이상 적지 않는다</b> (2026-08-24 소유자: "동기화가
        // 몇개 반영됬는지 표기할 필요는 없어"). 여기는 <em>검증 보고</em>이고, 잘된 일의
        // 개수는 검증할 것이 아니다 — 문제만 적힌 목록이라야 문제가 눈에 띈다.
        // 거부·경고는 그대로 든다: 조용한 무반영이 최악이다(G3-1).
        if (rejected > 0)
        {
            header.Append($" · {WarningMark} 동기화 거부·경고 {rejected}건");
        }

        if (exportNotice is not null)
        {
            // 접혀 있어도 보여야 한다 — 런타임으로 나갈 것이 안 나간 상태다.
            header.Append($" · {ErrorMark} 진행 JSON 미출력");
        }

        if (header.Length == "검증 보고".Length)
        {
            header.Append($" — 오류 없음 (알림 {all.Count}건)");
        }

        DiagnosticsExpander.Header = header.ToString();

        // ⛔ <b>저절로 펼치지 않는다</b> (2026-08-24 소유자: "오류가 있으면 검증 보고가
        // 저절로 펼쳐지는 규칙이 있는데, 그것까지 꺼줘. 대신에 … 시각적인 이모티콘을
        // 붙여놓기만 해").
        //
        // 예전에는 여기서 <c>IsExpanded</c>를 <b>양방향으로</b> 밀었다 — 오류가 있으면
        // 열고, 없으면 닫았다. 그래서 사람이 접어 둔 것을 다시 열고, 펼쳐 둔 것을 다시
        // 닫았다. 다시 그리기는 저장할 때마다 도니까 그 싸움이 계속됐다.
        // 이제 그 칸은 <b>사람만 만진다.</b> 알릴 것은 머리글의 표식이 든다
        // (🔴 오류 · 🟡 경고 — <see cref="ErrorMark"/>).

        // 내보내기 결론이 맨 위다 — 아래 오류들이 그 사유이므로 결론과 근거가 붙어 선다.
        if (exportNotice is not null)
        {
            DiagnosticsPanel.Children.Add(
                DiagnosticLine(exportNotice, Brushes.IndianRed, dim: false, bold: true));
        }

        // 탐색이 상한에서 멈췄으면 "도달 불가"가 단정이 아니라는 사실을 먼저 말한다.
        if (_validation is { Reachability.ExplorationComplete: false })
        {
            DiagnosticsPanel.Children.Add(DiagnosticLine(
                "도달성 탐색이 상한에서 중단됐습니다 — 아래의 도달 불가는 단정이 아니라 " +
                "'경로를 찾지 못했다'입니다.", Brushes.DarkGoldenrod, dim: false, bold: true));
        }

        // ⚠ 세는 것은 <b>말할 것이 있는</b> 보고뿐이다 (2026-08-24). 예전에는 보고가
        // 하나라도 있으면 여기를 지나쳤는데, 이제 대부분의 보고가 아무 줄도 내지 않으므로
        // 그대로 두면 <b>텅 빈 상자</b>가 선다("보고할 것이 없습니다"조차 없이).
        if (all.Count == 0 && !_syncReports.Any(HasSomethingToSay))
        {
            DiagnosticsPanel.Children.Add(new TextBlock
            {
                Text = "보고할 것이 없습니다.",
                FontSize = 11,
                Opacity = 0.55
            });

            return;
        }

        DrawSyncReports();

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

        OfferStatRegistration(model, all);
    }

    /// <summary>
    /// "스탯이 정의 파일에 없다" 경고 밑에 [등록] 단추를 세운다. 시트를 자동으로 믿고 쓰면
    /// 오타까지 게임 어휘가 되므로, 정의 파일 쓰기는 사람이 이 단추를 누른 순간에만 일어난다.
    /// </summary>
    private void OfferStatRegistration(ChapterGraphModel model, List<ChapterDiagnostic> all)
    {
        if (_session?.ProjectPath is null ||
            all.All(item => item.Code != ChapterDiagnosticCode.StatMissingFromGameDefinition))
        {
            return;
        }

        GameDefinition definition = _session.Definition;

        VariableSpec[] missing = model.Stats
            .Where(stat => !definition.Variables.Any(variable =>
                string.Equals(variable.Name, stat.Key, StringComparison.Ordinal)))
            .Select(stat => new VariableSpec
            {
                Name = stat.Key,
                Type = "number",
                Description = string.IsNullOrWhiteSpace(stat.DisplayName) ? null : stat.DisplayName
            })
            .ToArray();

        if (missing.Length == 0)
        {
            return;
        }

        var register = new Button
        {
            Content = $"이 스탯 {missing.Length}개를 {GameDefinition.FileName}에 등록",
            FontSize = 10,
            Padding = new Thickness(8, 2),
            Margin = new Thickness(0, 4, 0, 2)
        };

        ToolTip.SetTip(register,
            "정의 파일의 variables에 없는 이름만 더합니다. 값(초기/최소/최대)은 계속 스탯 시트가 소유합니다.");

        register.Click += (_, _) => UiGuard.Run(_session, "스탯 등록", () =>
        {
            if (_session.RegisterVariables(missing))
            {
                Reload(); // 새 정의로 다시 읽어 경고가 걷힌 화면을 보인다
            }
        });

        DiagnosticsPanel.Children.Add(register);
    }

    /// <summary>
    /// 보고를 텍스트 그대로 클립보드에 — 협업자나 세션에 붙여넣는 통로. 화면에 그려진
    /// 줄이 곧 복사되는 줄이다(별도 조립 없음 — 두 벌이면 어긋난다).
    /// </summary>
    private async Task CopyDiagnosticsAsync()
    {
        var lines = new List<string>();

        if (DiagnosticsExpander.Header is string header)
        {
            lines.Add(header);
        }

        lines.AddRange(DiagnosticsPanel.Children
            .OfType<TextBlock>()
            .Select(block => block.Text ?? string.Empty)
            .Where(text => text.Length > 0));

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;

        if (clipboard is null)
        {
            _session?.SetStatus("클립보드에 접근할 수 없습니다.");
            return;
        }

        await clipboard.SetTextAsync(string.Join(Environment.NewLine, lines));
        _session?.SetStatus($"검증 보고 {lines.Count}줄을 복사했습니다.");
    }

    /// <summary>
    /// 에피소드 동기화 결과. 거부·삭제·함께 접힌 논리를 <b>목록으로</b> 보인다 —
    /// 조용한 무반영이 최악이다(G3-1·G3-2).
    /// </summary>
    private void DrawSyncReports()
    {
        foreach (EpisodeSyncReport report in _syncReports.Where(HasSomethingToSay))
        {
            // 반영된 것에는 <b>제목만 적고 넘어가지 않는다</b> — 아래에 짚을 것이 있어서
            // 여기 왔으므로, 그 줄들이 어느 에피소드의 것인지 이름표가 필요하다.
            string summary = report.Applied
                ? $"에피소드 {report.EpisodeId}"
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

        }
    }

    /// <summary>
    /// 이 보고가 <b>검증 보고에 낄 자격이 있는가</b> (2026-08-24 소유자: "동기화가 몇개
    /// 반영됬는지 표기할 필요는 없어. 그런 동기화 문구들은 굳이 표시가 안 되도록").
    ///
    /// 가르는 선은 <b>문제인가 아닌가</b>다. 잘된 일의 개수는 검증할 것이 아니고, 그것이
    /// 목록을 채우면 정작 봐야 할 줄이 그 사이에 묻힌다 — 이 상자의 존재 이유가 그
    /// 반대다("숫자는 2개라는데 볼 방법이 없어").
    ///
    /// ⛔ <b>거부는 언제나 남는다.</b> 조용한 무반영이 최악이다(G3-1) — 여기서 거부까지
    /// 걸러 내면 작가는 자기 대사가 왜 안 나오는지 영영 모른다.
    ///
    /// 함께 사라진 것: <c>새 줄 N개에 LineId를 발급했습니다</c>. 문제가 아니라 툴이 제
    /// 장부를 적었다는 말이고, 새 줄을 쓸 때마다 떴다.
    /// </summary>
    private static bool HasSomethingToSay(EpisodeSyncReport report)
    {
        // 아직 대사를 안 쓴 워크북은 아예 말하지 않는다 — 잘못한 것이 없다.
        if (report.NotYetWritten)
        {
            return false;
        }

        return !report.Applied ||
            report.Problems.Count > 0 ||
            report.Pruned.Count > 0 ||
            report.Diagnostics.Any(item => item.Severity != ChapterDiagnosticSeverity.Info);
    }

    // SelectableTextBlock — [보고 복사]가 전체를 들고 가고, 드래그는 한 줄만 집어 간다.
    private static TextBlock DiagnosticLine(
        string text, IBrush? foreground, bool dim, bool bold = false) => new SelectableTextBlock
    {
        Text = text,
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Foreground = foreground,
        FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        Opacity = dim ? 0.6 : 1
    };
}
