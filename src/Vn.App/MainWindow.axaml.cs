using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Vn.App.Services;
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

        Graph.Attach(_session);
        // 왼쪽 챕터 목록의 원천은 챕터 그래프 뷰가 읽은 목록 하나다 — 두 곳이 따로 읽으면
        // 감시·재시도 규칙이 두 벌이 된다. Attach 전에 구독해야 첫 읽기부터 받는다.
        ChapterGraph.EntriesReloaded += entries =>
        {
            _chapters = entries;
            RebuildFileList();
            // 시나리오 그래프의 철도 배선(T1)도 같은 목록 하나를 본다 — 두 곳이 따로 읽지 않는다.
            Graph.SupplyChapters(entries);
        };
        // 챕터 그래프 탭에서는 시나리오 계층의 chrome을 접는다(소유자 보고 — "발행·무대
        // 프리뷰는 이 화면에서 필요 없는 정보"). 우측 열이 통째로 접혀 그래프가 그 폭을
        // 얻고, 상단의 Yarn/CSV 내보내기도 숨는다 — 챕터 툴바의 [내보내기](진행 JSON)와
        // 이름이 같아 한 화면에 "내보내기"가 둘 있으면 어느 쪽인지 헷갈린다.
        MainTabs.SelectionChanged += (_, _) =>
        {
            bool chapterMode = ReferenceEquals(MainTabs.SelectedItem, ChapterTabItem);

            RightColumn.IsVisible = !chapterMode;
            RightSplitter.IsVisible = !chapterMode;
            CenterColumns.ColumnDefinitions[3].Width = new GridLength(chapterMode ? 0 : 6);
            CenterColumns.ColumnDefinitions[4].Width = new GridLength(chapterMode ? 0 : 460);

            ExportButton.IsVisible = !chapterMode;
            CsvExportButton.IsVisible = !chapterMode;
            ExportFormatsButton.IsVisible = !chapterMode;
        };
        ChapterGraph.Attach(_session);
        DialogueEditor.Attach(_session);
        SetEditor.Attach(_session);
        PresentationEditor.Attach(_session);
        SupplyEditor.Attach(_session);
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
        _session.SelectionChanged += OnSelectionChanged;
        _session.FileGraphStateChanged += OnFileGraphStateChanged;

        AddFileButton.Click += (_, _) => UiGuard.Run(_session, "새 챕터", ShowAddChapterFlyout);
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

        flyout.ShowAt(AddFileButton);
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
            FileListPanel.Children.Clear();

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

                FileListPanel.Children.Add(row);
            }

            if (_chapters.Count == 0)
            {
                FileListPanel.Children.Add(new TextBlock
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
    /// 챕터 행 우클릭 — 이름 바꾸기. 워크북 파일이 옮겨지고 판(StoryFile) 이름이 따라간다.
    /// 챕터 삭제는 두지 않는다: 워크북 파일 삭제는 되돌릴 수 없어 툴이 대신하지 않는다 —
    /// 지우려면 폴더에서 사람이 지운다(폴더 열기가 그 길이다).
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

            string? folder = ChapterLibrary.FolderFor(_session.ProjectPath);

            if (folder is null)
            {
                _session.SetStatus("프로젝트를 먼저 저장해야 합니다.");
                return;
            }

            ChapterWriteResult result = ChapterWorkbookWriter.RenameChapterWorkbook(folder, chapterId, newId);

            if (!result.Written)
            {
                _session.SetStatus(result.Failure!);
                return;
            }

            // 판이 따라간다 — 챕터=판 1:1. 판이 아직 없었다면 만들 것도 없다.
            if (_session.Project.Files.FirstOrDefault(file =>
                    string.Equals(file.Name, chapterId, StringComparison.Ordinal)) is { } board)
            {
                _session.Editor.RenameStoryFile(board.Id, newId);
            }

            ChapterGraph.RefreshFromDisk();
            ChapterGraph.SelectChapter(newId);
            flyout.Hide();
            _session.SetStatus($"챕터 '{chapterId}' → '{newId}'로 바꿨습니다. 내보낸 JSON이 있었다면 다시 내보내 주세요.");
        });
        panel.Children.Add(rename);

        flyout.ShowAt(anchor);
        name.SelectAll();
        name.Focus();
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

            IReadOnlyList<string> written = YarnBundleEmitter.WriteBundles(bundles, folder);

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

        var list = new ListBox
        {
            ItemsSource = exportable
                .Select(item =>
                {
                    string pair = item.WorkingPresentation is not null
                        ? "현재 대사 + 연출"
                        : "현재 대사 (연출 공급 없음)";
                    string warning = item.Warnings.Count > 0 ? $" · 경고 {item.Warnings.Count}" : string.Empty;
                    return $"{item.DialogueNodeName} · {pair}{warning}";
                })
                .ToList(),
            SelectionMode = SelectionMode.Multiple
        };
        list.SelectAll();

        var confirm = new Button
        {
            Content = "내보내기",
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var dialog = new Window
        {
            Title = "내보낼 대사 노드 선택 (여러 개 선택 가능)",
            Width = 460,
            Height = 340,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel
            {
                Margin = new Thickness(12),
                Children =
                {
                    Docked(confirm, Dock.Bottom),
                    new ScrollViewer { Content = list }
                }
            }
        };

        var picked = new List<LiveComposition>();

        confirm.Click += (_, _) =>
        {
            foreach (object? item in list.Selection.SelectedIndexes.OrderBy(index => index))
            {
                if (item is int index && index >= 0 && index < exportable.Count)
                {
                    picked.Add(exportable[index]);
                }
            }

            dialog.Close();
        };

        await dialog.ShowDialog(this);
        return picked;
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

    /// <summary>
    /// 좌측의 편집 자료 요약 — 대본 원본·발행 결과·합성 목록. 전부 읽기 전용이다.
    /// 발행된 것에는 수정 UI가 없다는 원칙 그대로, 여기는 현황판일 뿐이다.
    /// </summary>
    private void RebuildResourcePanel()
    {
        ResourcePanel.Children.Clear();

        void Section(string title, IEnumerable<string> rows)
        {
            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Opacity = 0.8
            });

            bool any = false;

            foreach (string row in rows)
            {
                any = true;
                stack.Children.Add(new TextBlock
                {
                    Text = row,
                    FontSize = 11,
                    Opacity = 0.7,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            if (!any)
            {
                stack.Children.Add(new TextBlock { Text = "없음", FontSize = 11, Opacity = 0.4 });
            }

            ResourcePanel.Children.Add(stack);
        }

        Section("대본", _session.Project.Scripts.Select(script =>
            $"{script.Name} · {script.ActiveLines.Count()}줄" +
            (string.IsNullOrWhiteSpace(script.SourcePath) ? string.Empty : " · 원본 연결됨")));

        Section("발행 결과", _session.Project.Results.DialogueResults
            .Select(result => $"{result.SourceNodeName} · {result.Identity.Label} · 대사")
            .Concat(_session.Project.Results.PresentationResults
                .Select(result => $"{result.SourceNodeName} · {result.Identity.Label} · 연출")));

        // 내보내기 짝은 명시적 합성이 아니라 연출 공급 연결에서 계산된다.
        Section("연출 공급", _session.Project.Links
            .Where(link => link.Kind == NodeLinkKind.PresentationSupply && link.IsEnabled)
            .Select(link =>
            {
                string presentation = _session.Project.FindNode(link.SourceNodeId)?.Name ?? link.SourceNodeId;
                string dialogue = _session.Project.FindNode(link.TargetNodeId)?.Name ?? link.TargetNodeId;
                return $"{dialogue} ← {presentation}";
            }));
    }

    private void ShowSelectedNode()
    {
        StoryNode? node = _session.SelectedNode;

        DialogueEditor.IsVisible = node is DialogueNode;
        SetEditor.IsVisible = node is SetNode;
        PresentationEditor.IsVisible = node is PresentationNode;
        SupplyEditor.IsVisible = node is CommandSupplyNode;
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
        else if (node is CommandSupplyNode)
        {
            SupplyEditor.Show(node.Id);
        }

        // 무대 프리뷰는 대사·연출 편집기만 채운다. 다른 노드에서는 빈 상태로 돌린다.
        if (node is not DialogueNode && node is not PresentationNode)
        {
            StagePreview.Show(null);
        }
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

        if (node is CommandSupplyNode && SupplyEditor.NodeId == node.Id)
        {
            SupplyEditor.Rebuild();
            return;
        }

        ShowSelectedNode();
    }

    private void RefreshShell()
    {
        RebuildResourcePanel();

        string name = _session.ProjectPath is null
            ? _session.Project.Title
            : ProjectDisplayName(_session.ProjectPath);

        ProjectNameText.Text = name;
        ProjectPathText.Text = _session.ProjectPath ?? "저장되지 않음";
        ActiveFileSummaryText.Text = _session.ActiveFile is { } activeFile
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
