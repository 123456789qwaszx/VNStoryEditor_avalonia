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

        // 이 챕터의 대본이 자기 판의 노드로 서 있도록 따라잡는다 — 챕터를 처음 고르는
        // 순간이 곧 그 판을 처음 보는 순간이다.
        SyncEpisodes();
    }

    public ChapterGraphView()
    {
        InitializeComponent();

        // 챕터와 에피소드를 함께 따라잡는다 — 클라우드 동기화가 늦게 내려놓은 저장을
        // 기다리지 않고 사람이 지금 가져올 수 있는 유일한 손잡이다.
        ReloadButton.Click += (_, _) => UiGuard.Run(_session, "다시 읽기", () =>
        {
            Reload();
            SyncEpisodes();
        });
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
        // 2026-08-16 소유자 — [개명]·[적용] 단추 폐지: Id는 Enter로 개명하고, 조건 콤보는
        // 고르는 순간 저장된다. 기본은 읽기 전용(엑셀에서만 편집)이라 체크를 풀어야 열린다.
        IdBox.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                e.Handled = true;
                UiGuard.Run(_session, "에피소드 개명", RenameSelectedEpisode);
            }
        };
        // 관문은 v8에서 간선으로 옮겨 갔다 — 에피소드 콤보는 감춰져 있고 저장도 안 한다.
        AddNextEdgeButton.Click += (_, _) => UiGuard.Run(_session, "선택지 추가", AddChoiceSlotFromPanel);
        AddEdgeButton.Click += (_, _) => UiGuard.Run(_session, "간선 연결·수정", SubmitEdgeForm);
        DeleteEpisodeButton.Click += (_, _) => UiGuard.Run(_session, "에피소드 삭제", DeleteSelectedEpisode);
        AddEpisodeButton.Click += (_, _) => UiGuard.Run(_session, "에피소드 추가", AddEpisodeFromToolbar);
        EdgeApplyButton.Click += (_, _) => UiGuard.Run(_session, "간선 저장", ApplyEdgeFromPanel);
        EdgeDeleteButton.Click += (_, _) => UiGuard.Run(_session, "간선 삭제", DeleteSelectedEdge);
        ExcelOnlyCheck.IsCheckedChanged += (_, _) => UiGuard.Run(_session, "편집 모드 전환", () =>
        {
            ApplyEditability();
            RefreshPropertyPanel(preserveTyping: true);
        });
        CopyDiagnosticsButton.Click += async (_, _) =>
            await UiGuard.RunAsync(_session, "보고 복사", CopyDiagnosticsAsync);

        // 대사 접기 — 토글이 곧 제목이다(상자 없이). 접힌 채로 시작하고,
        // 삼각형이 "여기 더 있다"를 말한다(▸ 접힘 · ▾ 펼침).
        DialogueToggle.IsCheckedChanged += (_, _) =>
        {
            bool open = DialogueToggle.IsChecked == true;
            DialoguePreviewText.IsVisible = open;
            DialogueToggle.Content = open ? "▾  대사" : "▸  대사";
        };

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
        if (episodesFolderChanged && _episodeWatcher is not null)
        {
            SyncEpisodes();
        }
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

        ChapterEntry? entry = _entries.FirstOrDefault(item => item.ChapterId == _selectedChapterId);

        if (entry?.Model is null ||
            EpisodeLibrary.FolderFor(_session.ProjectPath, entry.ChapterId) is not { } folder)
        {
            return;
        }

        // 구판 평면 원고(episodes/{Id}.xlsx)를 이 챕터 폴더로 입양한다 (2026-08-16).
        // 주인이 여럿이면 옮기지 않고 사유를 말한다 — 남의 챕터 원고를 가져오지 않는다.
        AdoptFlatWorkbooks(entry);

        if (!Directory.Exists(folder))
        {
            return;
        }

        // 새 노드가 들어갈 판 = 그 챕터의 판 (챕터=판 1:1, G-1 v2). 없으면 만든다 —
        // 왼쪽 챕터 목록 클릭과 같은 규칙 하나를 쓴다.
        string fileId = _session.EnsureChapterBoard(entry.ChapterId);

        // 챕터 `화자` 시트 → 에피소드 워크북 드롭다운 (2026-08-16). 멱등이다 —
        // 목록이 안 바뀐 워크북에는 손대지 않으므로 매 동기화마다 불러도 안전하다.
        PushSpeakersToEpisodes(folder, entry);

        // v9에서 선택지 칸 따라잡기는 사라졌다 — 칸이라는 개념이 없다. 문구 사전은 사람이
        // 엑셀에서 적고, 길은 간선 시트가 곧 그것이다.

        foreach (ChapterEpisode episode in entry.Model.Episodes)
        {
            if (EpisodeLibrary.FindExisting(folder, episode.EpisodeId) is not { } path)
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

        // 챕터 조건을 판의 모든 대사 노드(자유 노드 포함)에 공급한다 — 작가가 조건
        // 드롭다운에서 A 계층 라벨을 바로 고른다. 멱등이라 매번 불러도 안전하다.
        EpisodeSyncService.SupplyChapterConditionsToBoard(
            _session.Editor, _session.Definition, fileId, entry.Model);

        // 가드레일 — 자유 노드의 스탯 set, 엑셀노드로 향하는 출구. 막지 않고 크게 말한다.
        _boardWarnings.Clear();
        _boardWarnings.AddRange(
            EpisodeSyncService.WarnFreeNodeStatWrites(_session.Editor, fileId, entry.Model));
        _boardWarnings.AddRange(
            EpisodeSyncService.WarnExitsIntoExcelNodes(_session.Editor, fileId, entry.Model));

        // 에피소드가 바뀌면 스탯 증감량도 바뀐다 — 도달성을 다시 증명한다.
        Validate();
        Draw();

        int rejected = _syncReports.Sum(report => report.RejectionCount);
        int applied = _syncReports.Count(report => report.Applied);

        // 반영이 있었다면 열려 있는 편집 화면(줄 목록·그래프)을 다시 만들게 알린다 —
        // 대사 수정은 "타이핑 보호" 경로로 전달되어 화면이 옛 줄을 그대로 들고 있었다(실사례).
        if (applied > 0)
        {
            _session.NotifyExternalScriptChange();
        }

        // 반영할 것이 하나도 없었다면 조용히 있는다 — "0개를 반영했습니다"는 소음이다.
        if (_syncReports.Count > 0)
        {
            _session.SetStatus(rejected == 0
                ? $"에피소드 {applied}개를 반영했습니다."
                : $"에피소드 {applied}개 반영 · 거부·경고 {rejected}건 — 아래 검증 보고를 확인하세요.");
        }
    }

    /// <summary>
    /// 구판 평면 대본(<c>episodes/{Id}.xlsx</c>)을 그 챕터 폴더로 옮긴다 (2026-08-16).
    /// EpisodeId를 여러 챕터가 쓰고 있으면 어느 원고인지 알 수 없으므로 손대지 않고 말한다.
    /// </summary>
    private void AdoptFlatWorkbooks(ChapterEntry entry)
    {
        if (entry.Model is not { } model ||
            EpisodeLibrary.FolderFor(_session?.ProjectPath) is not { } root ||
            !Directory.Exists(root))
        {
            return;
        }

        var problems = new List<string>();
        int adopted = 0;

        foreach (ChapterEpisode episode in model.Episodes)
        {
            // 그 이름을 쓰는 챕터 수 — 하나여야 주인이 분명하다.
            int claimants = _entries.Count(candidate =>
                candidate.Model?.FindEpisode(episode.EpisodeId) is not null);

            EpisodeLibrary.FlatAdoption adoption = EpisodeLibrary.AdoptFlatWorkbook(
                root, entry.ChapterId, episode.EpisodeId, claimants);

            if (adoption.Adopted)
            {
                adopted++;
            }
            else if (adoption.Problem is { } problem)
            {
                problems.Add(problem);
            }
        }

        if (problems.Count > 0)
        {
            _session?.SetStatus(problems[0] +
                (problems.Count > 1 ? $" (외 {problems.Count - 1}건)" : string.Empty));
        }
        else if (adopted > 0)
        {
            _session?.SetStatus(
                $"대본 {adopted}개를 episodes/{entry.ChapterId}/ 로 옮겼습니다 — " +
                "챕터마다 같은 이름을 따로 쓸 수 있습니다.");
        }
    }

    /// <summary>
    /// 챕터의 화자 등록을 에피소드 워크북들에 반영한다 (2026-08-16 소유자 지시).
    ///
    /// 두 가지를 한다: ① 구판 챕터 워크북에 `화자` 시트가 없으면 한 번 만들어 준다
    /// (기획자가 등록할 자리부터 있어야 한다) ② 등록된 이름을 각 에피소드 워크북의 숨김
    /// 목록 시트에 밀어 넣는다 — 대본의 화자 칸(H)이 그 목록의 드롭다운을 받는다.
    /// 목록이 같은 워크북은 건드리지 않고, 잠긴 워크북은 건너뛰며 사유를 보고한다.
    /// </summary>
    private void PushSpeakersToEpisodes(string folder, ChapterEntry entry)
    {
        if (entry.Model is not { } model)
        {
            return;
        }

        if (!model.HasSpeakerSheet)
        {
            (bool created, ChapterWriteResult ensure) = ChapterWorkbookWriter.EnsureSpeakerSheet(entry.Path);

            if (created)
            {
                _session?.SetStatus(
                    $"'{entry.ChapterId}'에 화자 시트를 만들었습니다 — 엑셀에서 이름을 등록하면 " +
                    "대본의 화자 칸이 드롭다운이 됩니다.");
            }
            else if (ensure.Failure is { } blocked)
            {
                _session?.SetStatus(blocked);
            }
        }

        List<string> names = model.Speakers.Select(speaker => speaker.Name).ToList();
        var failures = new List<string>();
        int changed = 0;

        foreach (ChapterEpisode episode in model.Episodes)
        {
            EpisodeLibrary.SpeakerListPush push =
                EpisodeLibrary.PushSpeakerList(folder, episode.EpisodeId, names);

            if (push.Changed)
            {
                changed++;
            }
            else if (push.Failure is { } failure)
            {
                failures.Add(failure);
            }
        }

        if (failures.Count > 0)
        {
            _session?.SetStatus(failures[0] +
                (failures.Count > 1 ? $" (외 {failures.Count - 1}건)" : string.Empty));
        }
        else if (changed > 0)
        {
            _session?.SetStatus($"화자 드롭다운을 에피소드 워크북 {changed}개에 반영했습니다.");
        }
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

        if (EpisodeLibrary.EnsureWorkbook(folder, episodeId,
                SelectedModel?.Speakers.Select(speaker => speaker.Name).ToList()))
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

        Validate();
        Draw();
        RefreshLockBanner(); // 엑셀을 닫으면 파일 사건이 여기로 오고 배너가 걷힌다
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
            entry.Model, EpisodeLibrary.FolderFor(_session?.ProjectPath, entry.ChapterId));
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
            entry.Model, EpisodeLibrary.FolderFor(_session.ProjectPath, entry.ChapterId));

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

        // 에피소드별 보이는 선택지 = 문구가 붙은 나가는 간선들 (v9 — 길 하나가 곧 선택지
        // 하나). 포트 그리기와 간선 그리기가 같은 목록 하나를 본다.
        _optionsByEpisode.Clear();

        foreach (ChapterEpisode episode in model.Episodes)
        {
            _optionsByEpisode[episode.EpisodeId] = model.Edges
                .Where(edge =>
                    string.Equals(edge.FromEpisodeId, episode.EpisodeId, StringComparison.Ordinal) &&
                    !edge.IsPlainAdvance)
                .ToList();
        }

        // 간선을 먼저 그려야 노드 카드 아래로 깔린다.
        DrawEpisodeRails(model, pathEdges);

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

    /// <summary>포트 줄 높이 — 카드가 선택지 수만큼 아래로 자란다.</summary>
    private const double PortRowHeight = 18;

    /// <summary>에피소드 → 보이는 선택지(문구 붙은 나가는 간선). Draw가 채우고 카드·간선이 함께 본다.</summary>
    private readonly Dictionary<string, List<ChapterEdge>> _optionsByEpisode = new(StringComparer.Ordinal);

    /// <summary>선택지 포트의 세로 자리 — 카드 그리기와 간선 그리기가 같은 산식을 쓴다.</summary>
    private static double PortY(double cardY, int index) =>
        cardY + CardHeight - 7 + index * PortRowHeight + PortRowHeight / 2;

    /// <summary>
    /// 간선 그리기의 갈림 (2026-08-15 소유자 개정 2) — <b>선택지 포트는 카드 오른쪽</b>이다.
    /// 시나리오 그래프의 조건 갈래 포트와 같은 문법: 카드 오른변에 포트가 뚫리고 각 포트에서
    /// 자기 간선이 나간다(선택지는 많아야 3개). 아래로 줄기를 빼는 철도 흉내는 폐기.
    /// 선택지 없는 에피소드는 기존 중앙 직행선 그대로.
    /// </summary>
    private void DrawEpisodeRails(ChapterGraphModel model, IReadOnlySet<(string, string)> pathEdges)
    {
        foreach (ChapterEpisode episode in model.Episodes)
        {
            List<ChapterEdge> edges = model.Edges
                .Where(edge => string.Equals(edge.FromEpisodeId, episode.EpisodeId, StringComparison.Ordinal))
                .ToList();

            List<ChapterEdge> options = _optionsByEpisode.GetValueOrDefault(episode.EpisodeId) ?? [];

            if (options.Count == 0 || !_placed.TryGetValue(episode.EpisodeId, out (double X, double Y) position))
            {
                // 보이는 선택지 없음 — 진행(보이지 않는 기본)은 중앙 직행선 그대로.
                foreach (ChapterEdge edge in edges)
                {
                    DrawDirectEdge(edge, pathEdges.Contains((edge.FromEpisodeId, edge.ToEpisodeId)));
                }

                continue;
            }

            for (int index = 0; index < options.Count; index++)
            {
                DrawPortEdge(options[index],
                    new Point(position.X + CardWidth + 5, PortY(position.Y, index)),
                    pathEdges);
            }

            // 문구 없는 간선(보이지 않는 기본)은 직행선 — 실재는 숨기지 않는다.
            foreach (ChapterEdge stray in edges.Where(edge => edge.IsPlainAdvance))
            {
                DrawDirectEdge(stray, pathEdges.Contains((stray.FromEpisodeId, stray.ToEpisodeId)));
            }
        }
    }

    /// <summary>포트에서 도착 카드로 — 같은 높이면 왼쪽 진입(▶), 위·아래면 가운데로 꺾어 진입.</summary>
    private void DrawPortEdge(ChapterEdge edge, Point port, IReadOnlySet<(string, string)> pathEdges)
    {
        bool onPath = pathEdges.Contains((edge.FromEpisodeId, edge.ToEpisodeId));

        IBrush stroke = onPath
            ? new SolidColorBrush(Color.Parse("#3E9B57"))
            : edge.ConditionLabel is null
                ? new SolidColorBrush(Color.Parse("#8894A0"))
                : new SolidColorBrush(Color.Parse("#C08A3E"));
        double thickness = onPath ? 3.2 : 1.6;

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
        double y = port.Y;

        if (y >= targetRect.Y && y <= targetRect.Bottom)
        {
            Segment(port.X, y, targetRect.X - 8, y);
            AddEdgeArrow(targetRect.X - 7, y, stroke, pointRight: true);
        }
        else
        {
            double midX = (port.X + targetRect.X) / 2;
            Segment(port.X, y, midX, y);

            double targetY = targetRect.Y + CardHeight / 2;
            Segment(midX, y, midX, targetY);
            Segment(midX, targetY, targetRect.X - 8, targetY);
            AddEdgeArrow(targetRect.X - 7, targetY, stroke, pointRight: true);
        }

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

    private void AddEdgeArrow(double x, double y, IBrush fill, bool pointRight, bool pointUp = false)
    {
        var arrow = new Avalonia.Controls.Shapes.Polygon
        {
            Fill = fill,
            IsHitTestVisible = false,
            Points = pointRight
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

    private void DrawDirectEdge(ChapterEdge edge, bool onPath)
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
            UiGuard.Run(_session, "간선 선택", () => SelectEdgeKey(fromId, toId, labelKey));
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

        // 관문은 v8에서 길(간선)의 것이 됐다 — 카드는 "들어오는 길에 관문이 있다"를 표시한다.
        if (GatedIncoming(model, episode) is { Count: > 0 } gatedPaths)
        {
            var lockMark = new TextBlock { Text = "🔒", FontSize = 11 };
            ToolTip.SetTip(lockMark, GateSummary(gatedPaths));
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

        // 선택지 포트 (2026-08-15 소유자) — 시나리오 그래프의 조건 갈래 포트처럼 카드
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
                : episode.IsEnding
                    ? new SolidColorBrush(Color.Parse("#C09A3E"))
                    : new SolidColorBrush(Color.Parse("#7F8A96")),
            Background = new SolidColorBrush(Color.Parse("#FAFBFCFD")),
            Child = body,
            // 노드 카드임을 EpisodeId로 표시한다. 간선 라벨도 Border라서, 표식이 없으면
            // 검증이 카드와 라벨을 구별하지 못한다.
            Tag = episode.EpisodeId
        };

        ToolTip.SetTip(card, Tooltip(model, episode));

        if (GatedIncoming(model, episode).Count > 0)
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
            string option = wired.OptionLabel ?? string.Empty;
            double portY = PortY(y, index);

            var label = new TextBlock
            {
                Text = wired.ConditionLabel is { } gate ? $"{option} [{gate}]" : option,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.Parse("#C06A14")),
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
            RefreshEdgePanel(model, edge, fill);
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

            // 엑셀이 소유한 값들 — 읽기 전용으로 세워 둔다. 확인하러 엑셀을 열지 않아도 되고,
            // 고치려면 엑셀을 연다는 것이 한눈에 보인다.
            EpisodeFactsText.Text = EpisodeFacts(episode);

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

    // ── 편집 모드 (2026-08-16 소유자) ───────────────────────────────────────
    // 기본 = 엑셀에서만 편집(툴 읽기 전용). 우측 위 체크를 풀어야 툴 편집이 열린다.

    /// <summary>콤보·목록을 프로그램이 채우는 중 — 자동 저장 억제.</summary>
    private bool _fillingPanel;

    private bool ToolEditable => ExcelOnlyCheck.IsChecked != true;

    /// <summary>
    /// 엑셀이 이 챕터를 열고 있으면 배너로 말한다 (2026-08-16 실사례) — 그 상태에서는
    /// 툴의 모든 쓰기가 거부되므로, 누르고 나서가 아니라 <b>누르기 전에</b> 보여야 한다.
    /// </summary>
    private void RefreshLockBanner()
    {
        bool locked = ChapterWorkbookWriter.IsLockedByAnotherApp(SelectedChapterPath);

        LockBanner.IsVisible = locked;
        LockBannerText.Text = locked
            ? $"⚠ 엑셀이 '{_selectedChapterId}.xlsx'를 열고 있습니다 — 툴에서 고친 값은 저장되지 " +
              "않습니다. 엑셀에서 그 파일을 닫으면 바로 편집됩니다."
            : string.Empty;
    }

    private void ApplyEditability()
    {
        RefreshLockBanner();

        bool editable = ToolEditable;

        IdBox.IsEnabled = editable;
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
        EdgeConditionCombo.IsEnabled = editable;
        EdgeHideCheck.IsEnabled = editable;
        EdgeLockedMsgBox.IsEnabled = editable;
        EdgeStatsBox.IsEnabled = editable;
        EdgeApplyButton.IsVisible = editable;
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
    /// [대사] 탭 — 선택된 에피소드의 대사를 워크북에서 바로 읽어 세운다. 시나리오 그래프까지
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
                preview = string.Join("\n", EpisodeWorkbookReader.Read(path).Rows
                    .Where(row => !row.IsBlank)
                    .Select(PreviewLine)
                    .Where(line => line.Length > 0));
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

    /// <summary>워크북 한 행을 미리보기 한 줄로 — 시트의 모양을 그대로 옮기되 읽는 눈 기준으로.</summary>
    private static string PreviewLine(EpisodeRow row)
    {
        string body = row.Kind switch
        {
            EpisodeRowKind.If => $"IF {row.ConditionLabel}" + (row.In is { } target ? $" → {target}" : string.Empty),
            EpisodeRowKind.Choice => "── 선택 ──",
            EpisodeRowKind.Option => $"▶ {row.Text}" + (row.In is { } into ? $" → {into}" : string.Empty),
            _ => row.Speaker.Length > 0 ? $"{row.Speaker}: {row.Text}" : row.Text
        };

        if (body.Length == 0)
        {
            return string.Empty;
        }

        if (row.Tag == EpisodeRowTag.Input)
        {
            body = $"[{row.Index}] {body}"; // IN이 가리키는 구간의 문패
        }

        if (row.OutTarget is { Length: > 0 } exit)
        {
            body += $"  ⏎ {exit}";
        }

        return body;
    }

    /// <summary>
    /// 엑셀이 소유한 값들을 한 덩어리로. 비어 있는 것은 줄에 세우지 않는다 —
    /// "(없음)"만 늘어놓으면 읽을거리가 아니라 소음이 된다.
    /// </summary>
    private static string EpisodeFacts(ChapterEpisode episode)
    {
        // 제목·표시/해금·엔딩키·메모는 위의 편집 칸이 맡는다(2026-08-15 복원) —
        // 여기는 편집 칸이 없는 나머지 값만.
        var facts = new List<string> { $"대사엔트리: {episode.DialogueEntry}" };

        if (!string.IsNullOrWhiteSpace(episode.Index))
        {
            facts.Add($"인덱스: {episode.Index}");
        }

        if (!string.IsNullOrWhiteSpace(episode.Kind))
        {
            facts.Add($"종류: {episode.Kind}");
        }

        facts.Add($"엑셀 {episode.SourceRow}행");

        return string.Join(" · ", facts);
    }

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
                : edge.IsPlainAdvance ? PlainAdvanceItem : edge.OptionLabel;
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

        ChapterWriteResult result = _edgeFormEdge is { } current
            ? ChapterWorkbookWriter.SetEdgeRoute(path, from, current.To, current.Label, to, label)
            : ChapterWorkbookWriter.AddEdge(path, from, to, optionLabel: label);

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
                Math.Min(_edgeFormIndex, EdgeListPanel.Children.Count), EdgeFormPanel);
        }
        else
        {
            EdgeListPanel.Children.Add(EdgeFormPanel); // 트리 밖에 두지 않는다 — 자리만 숨김
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
            Background = new SolidColorBrush(Color.Parse(edge.IsPlainAdvance ? "#22808080" : "#22C06A14")),
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

        string label = edge.IsPlainAdvance ? "(기본 · 보이지 않음)" : edge.OptionLabel!;
        string condition = edge.ConditionLabel is { } gate ? $"  [{gate}]" : string.Empty;

        var text = new TextBlock
        {
            Text = $"{label}   → {edge.ToEpisodeId}{condition}",
            FontSize = 11,
            Opacity = edge.IsPlainAdvance ? 0.7 : 1,
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
            EdgeStatsBox.Text = StatChangesText(edge);
        }

        // 이 길의 문구 (v9) — 읽기 전용 표시다. 바꾸는 일은 선택지 목록의 폼에서 한다.
        string labelText = edge.IsPlainAdvance ? "(기본 · 보이지 않음)" : edge.OptionLabel!;
        SetItems(EdgeLabelEditBox, [labelText], labelText);

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

        ChapterWriteResult result = ChapterWorkbookWriter.UpdateEdge(
            path, key.From, key.To,
            visibleConditionLabel: Changed(Gate(EdgeVisibleCombo), edge.VisibleConditionLabel ?? string.Empty),
            conditionLabel: Changed(Gate(EdgeConditionCombo), edge.ConditionLabel ?? string.Empty),
            hideWhenLocked: EdgeHideCheck.IsChecked == edge.HideWhenLocked ? null : EdgeHideCheck.IsChecked,
            lockedMessage: Changed(EdgeLockedMsgBox.Text, edge.LockedMessage ?? string.Empty),
            statChanges: Changed(EdgeStatsBox.Text, StatChangesText(edge)),
            matchOptionLabel: EdgeLabelKey(edge));

        Report(result, $"간선 {key.From}→{key.To}을 저장했습니다.");
    }

    /// <summary>간선 스탯변화를 시트 문법 그대로 — 패널 칸과 셀이 같은 글을 쓴다.</summary>
    private static string StatChangesText(ChapterEdge edge) => string.Join("; ", edge.StatChanges
        .Select(delta => $"{delta.Key} {(delta.Amount >= 0 ? "+" : "")}{delta.Amount}"));

    /// <summary>
    /// 에피소드 값 저장 — 지금 이 패널에서 툴이 쓰는 값은 없다. 제목·엔딩키·메모는 칸을
    /// 뺐고(2026-08-16), 표시·해금조건은 v8에서 길(간선)로 옮겨 갔다. 개명은 Enter가,
    /// 선택지 칸은 목록이 맡는다. 라이터 경로는 살아 있으므로 필요하면 여기로 되돌아온다.
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
    /// [챕터] 탭의 읽기 전용 표 둘 — 스탯·픽스처. 어디에서도 값이 안 보이던 것들이라
    /// 여기 세운다(소유자 점검). 에피소드·간선은 그래프가 이미 그리므로 반복하지 않는다.
    /// </summary>
    private void RefreshChapterSheets(ChapterGraphModel? model)
    {
        StatListPanel.Children.Clear();
        FixtureListPanel.Children.Clear();

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

        foreach (ChapterFixture fixture in model?.Fixtures ?? Enumerable.Empty<ChapterFixture>())
        {
            string facts = string.Join(" · ", fixture.Stats
                .Select(pair => $"{pair.Key} {pair.Value}")
                .Concat(fixture.Choices.Select(choice => $"고정 {choice.From}→{choice.To}")));

            FixtureListPanel.Children.Add(SheetLine(
                $"{fixture.Name}{(fixture.IsActive ? " (활성)" : "")}" +
                (facts.Length > 0 ? $" — {facts}" : string.Empty)));
        }

        if (FixtureListPanel.Children.Count == 0)
        {
            FixtureListPanel.Children.Add(SheetLine("픽스처 시트가 비어 있습니다.", dim: true));
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
        // (ExcelEpisodeId)도 함께 간다 — 옛 Id로 남으면 시나리오 그래프가 챕터 밖 노드로
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

        Report(ChapterWorkbookWriter.AddEdge(path, from, to),
            $"간선 {from}→{to}을 더했습니다 — 선택지 문구는 그 줄을 눌러 고릅니다.");
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
            result = ChapterWorkbookWriter.AddNextEpisode(path, parent, episodeId, title: string.Empty, x, y);
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
                EpisodeLibrary.EnsureWorkbook(episodesFolder, episodeId,
                    model.Speakers.Select(speaker => speaker.Name).ToList()))
            {
                StartWatchingEpisodes(EpisodeLibrary.FolderFor(_session?.ProjectPath));
            }
        }

        Report(result, $"'{episodeId}' 행을 더했습니다. Id와 대사엔트리를 패널에서 채워 주세요.");
    }

    /// <summary>쓰기 결과를 상태줄로. 성공이면 감시가 다시 읽어 화면이 따라온다.</summary>
    private void Report(ChapterWriteResult result, string success)
    {
        _session?.SetStatus(result.Written ? success : result.Failure!);

        // 거부됐다면 대개 엑셀이 잡고 있어서다 — 그 사실을 배너로도 세운다(상태줄은 묻힌다).
        RefreshLockBanner();
    }

    /// <summary>간선 하나를 가리키는 표식. 화면 검증이 이 이름으로 간선을 찾는다.</summary>
    internal static string EdgeTag(ChapterEdge edge) =>
        $"{edge.FromEpisodeId}→{edge.ToEpisodeId}" +
        (edge.IsPlainAdvance ? string.Empty : $" [{edge.OptionLabel}]");

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
    /// 에피소드 동기화 보고를 먼저, 그 뒤에 오류·경고·정보를 심각도 순으로. 동기화는 사람이
    /// 방금 한 행동의 결과인데 목록 끝에 두면 알림 더미에 묻혀 스크롤 밖으로 나간다(실사례 —
    /// "숫자는 2개라는데 볼 방법이 없어"). 각 줄이 파일·시트·행·열을 그대로 말하고,
    /// 머리글의 배지가 거부 건수를 든다(G5).
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

        string header = errors + warnings == 0
            ? $"검증 보고 — 오류 없음 (알림 {all.Count}건)"
            : $"검증 보고 — 오류 {errors} · 경고 {warnings}";

        int synced = _syncReports.Count(report => !report.NotYetWritten);

        if (synced > 0)
        {
            header += rejected == 0
                ? $" · 동기화 {synced}건 반영"
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
        foreach (EpisodeSyncReport report in _syncReports)
        {
            // 아직 대사를 안 쓴 워크북은 아예 말하지 않는다 — 잘못한 것이 없다.
            if (report.NotYetWritten)
            {
                continue;
            }

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

            if (report.IssuedLineIds.Count > 0)
            {
                DiagnosticsPanel.Children.Add(DiagnosticLine(
                    $"  새 줄 {report.IssuedLineIds.Count}개에 LineId를 발급했습니다 (프로젝트에 기록 — 워크북은 건드리지 않음).",
                    null, dim: true));
            }
        }
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
