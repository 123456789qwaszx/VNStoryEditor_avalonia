using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Vn.App.Services;
using Vn.Authoring.Assets;

namespace Vn.App.Views;

/// <summary>
/// 좌측 에셋 탐색기 — 배경은 폴더 구조 그대로, 초상화는 캐릭터→variant→emotion 키 구조.
///
/// 트리는 <see cref="AssetExplorerModel"/>의 순수 계산 결과를 그리기만 한다.
/// 문제(파일 없음·고아·초상화 없는 화자)는 항목 옆 ⚠와 문구로 보인다 — 침묵을 화면으로.
/// 파일 감시는 없다. 새로 고침 버튼이 유일한 갱신 경로다.
/// 항목은 W20 프리뷰 드래그의 소스가 된다.
/// </summary>
public partial class AssetExplorerView : UserControl
{
    private static readonly SolidColorBrush ProblemBrush = new(Color.FromRgb(194, 65, 12));

    private AuthoringSession? _session;
    private PreviewAssetLibrary? _renderedLibrary;
    private readonly HashSet<string> _collapsedPaths = new(StringComparer.Ordinal);

    public AssetExplorerView()
    {
        InitializeComponent();

        RefreshButton.Click += async (_, _) =>
            await UiGuard.RunAsync(_session, "에셋 새로 고침", RefreshWithFeedbackAsync);
        // 에셋 루트 변경의 상시 진입점 (X8 — 프리뷰 위 버튼을 걷어낸 자리).
        RootsButton.Click += (_, _) => UiGuard.Run(_session, "에셋 폴더 설정", ShowRootsFlyout);
        // 튜닝 관리의 상시 진입점 (W46) — 기본 생성·기존 폴더 연결.
        TuningButton.Click += (_, _) => UiGuard.Run(_session, "튜닝 설정", ShowTuningFlyout);
        CollapseToggle.IsCheckedChanged += (_, _) =>
        {
            TreeScroll.IsVisible = CollapseToggle.IsChecked == true;
            CollapseToggle.Content = CollapseToggle.IsChecked == true ? "▼" : "▶";
        };
    }

    internal void Attach(AuthoringSession session)
    {
        _session = session;

        // 트리 입력은 에셋 인덱스와 게임 정의뿐이다. 인덱스 인스턴스가 그대로면
        // (루트가 안 바뀌었으면) 편집 알림이 와도 다시 그릴 것이 없다.
        session.Changed += (_, _) =>
        {
            if (!ReferenceEquals(_renderedLibrary, session.AssetLibrary))
            {
                Rebuild();
            }
        };

        Rebuild();
    }

    /// <summary>
    /// 새로 고침 — <b>도는 동안 그 사실이 화면에 보여야 한다</b> (2026-08-21 소유자 보고:
    /// 새 튜닝 덤프를 넣고 누르자 창이 30초 넘게 굳어 멈춘 줄 알았다).
    ///
    /// 일 자체는 여전히 UI 스레드의 동기 작업이다(에셋 png 재귀 스캔 + 튜닝 재읽기 +
    /// 비트맵 캐시 비우기 + 트리 재구성). 가벼운 쪽으로 고른 것은 <b>일을 옮기는 대신
    /// 상태를 먼저 그리는 것</b>이다: 단추를 잠그고 "다시 읽는 중"을 적은 뒤, 렌더 패스가
    /// 지나가도록 <see cref="DispatcherPriority.Background"/>로 한 박자 양보하고 시작한다.
    /// 양보가 없으면 상태줄도 잠긴 단추도 <b>일이 끝난 뒤에야</b> 그려져 아무 소용이 없다.
    ///
    /// 이 대기가 위험한 이유는 느려서가 아니라, 굳은 창을 죽이면 저장 안 한 편집이
    /// 함께 날아가기 때문이다(같은 보고). 근본 해결(스캔을 백그라운드 스레드로)은 호출처가
    /// 일곱 군데라 따로 다룬다.
    /// </summary>
    private async Task RefreshWithFeedbackAsync()
    {
        RefreshButton.IsEnabled = false;
        _session?.SetStatus("프리뷰 에셋과 튜닝을 다시 읽는 중… (폴더가 크면 시간이 걸립니다)");

        try
        {
            await Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    _session?.RefreshAssets();
                    Rebuild();
                },
                DispatcherPriority.Background);
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    internal void Rebuild()
    {
        if (_session is null)
        {
            return;
        }

        PreviewAssetLibrary library = _session.AssetLibrary;
        _renderedLibrary = library;
        AssetExplorerTree tree = AssetExplorerModel.Build(library, _session.Definition);

        TreeHost.Children.Clear();

        int problemCount = tree.Problems.Count;
        ProblemCountText.Text = problemCount > 0 ? $"문제 {problemCount}" : string.Empty;

        if (!tree.BackgroundsConfigured && !tree.PortraitsConfigured)
        {
            BuildEmptyState();
            return;
        }

        TreeHost.Children.Add(SectionHeaderRow(
            "배경",
            _session.BackgroundsRoot,
            importAction: ImportBackgroundsAsync));

        if (!tree.BackgroundsConfigured)
        {
            TreeHost.Children.Add(ConfigureRow("배경 폴더가 설정되지 않았습니다", backgrounds: true));
        }
        else if (tree.BackgroundItems.Count == 0)
        {
            // 빈칸에는 어디에 어떤 이름으로 넣으면 되는지가 보인다 (W-asset-02 §3.4).
            TreeHost.Children.Add(EmptyLabel(AssetExplorerModel.BackgroundPlacementGuide));
        }
        else
        {
            AddItems(tree.BackgroundItems, depth: 0, parentPath: "bg");
        }

        Control portraitHeader = SectionHeaderRow(
            "초상화",
            _session.PortraitsRoot,
            importAnchor => ShowPortraitImportFlyout(importAnchor));

        TreeHost.Children.Add(portraitHeader);

        if (!tree.PortraitsConfigured)
        {
            TreeHost.Children.Add(ConfigureRow("초상화 폴더가 설정되지 않았습니다", backgrounds: false));
        }
        else if (tree.PortraitItems.Count == 0)
        {
            TreeHost.Children.Add(EmptyLabel(AssetExplorerModel.PortraitPlacementGuide));
        }
        else
        {
            AddItems(tree.PortraitItems, depth: 0, parentPath: "pt");
        }

        // 오디오 (W59) — 배경과 같은 규약: 파일명이 곧 clipKey. 연출 조절창의 오디오 탭이 쓴다.
        AddAudioSection("BGM", bgm: true, _session.BgmRoot);
        AddAudioSection("효과음", bgm: false, _session.SfxRoot);
    }

    /// <summary>오디오 섹션 (W59) — 파일 목록 + 가져오기/폴더 열기. 규약은 배경과 같다.</summary>
    private void AddAudioSection(string title, bool bgm, string? root)
    {
        TreeHost.Children.Add(SectionHeaderRow(
            title,
            root,
            importAnchor => ImportAudioAsync(bgm)));

        if (root is null)
        {
            TreeHost.Children.Add(EmptyLabel("프로젝트를 저장하면 assets 아래 폴더가 준비됩니다."));
            return;
        }

        IReadOnlyList<string> keys = _session!.AudioClipKeys(root);

        if (keys.Count == 0)
        {
            TreeHost.Children.Add(EmptyLabel(
                $"{(bgm ? "assets/bgm" : "assets/sfx")}에 mp3·wav·ogg를 넣으면 파일명이 곧 clipKey가 됩니다."));
            return;
        }

        foreach (string key in keys)
        {
            // ♪ 항목이 곧 미리 듣기 버튼이다 (W62). 같은 파일을 다시 누르면 멈춘다.
            var audition = new Button
            {
                Content = $"▶ {key}",
                FontSize = 11,
                Padding = new Thickness(4, 1),
                Margin = new Thickness(4, 0, 0, 0),
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            ToolTip.SetTip(audition, "미리 듣기 — 다시 누르면 멈춥니다.");
            audition.Click += (_, _) => UiGuard.Run(_session, "오디오 미리 듣기", () =>
            {
                if (_session!.ResolveAudioClipPath(root, key) is { } path)
                {
                    AudioPreview.ToggleAudition(path, bgm);
                }
                else
                {
                    _session.SetStatus($"clipKey '{key}' 파일이 사라졌습니다 — 에셋 새로 고침을 눌러 보세요.");
                }
            });
            TreeHost.Children.Add(audition);
        }
    }

    private async void ImportAudioAsync(bool bgm)
    {
        await UiGuard.RunAsync(_session, "오디오 가져오기", async () =>
        {
            if (_session is null ||
                TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
            {
                return;
            }

            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = $"가져올 {(bgm ? "BGM" : "효과음")} 파일 (여러 개 가능)",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("오디오 (mp3·wav·ogg)")
                    {
                        Patterns = new[] { "*.mp3", "*.wav", "*.ogg" }
                    }
                }
            });

            List<string> paths = files
                .Select(file => file.TryGetLocalPath())
                .Where(path => path is not null)
                .Select(path => path!)
                .ToList();

            if (paths.Count > 0)
            {
                _session.ImportAudio(bgm, paths);
                Rebuild();
            }
        });
    }

    // ── 가져오기·폴더 열기 (W48) ──────────────────────────────────────────

    /// <summary>섹션 제목 + [가져오기…]/[열기] — 루트가 없으면 버튼도 없다(안내가 대신한다).</summary>
    private Control SectionHeaderRow(string title, string? root, Action<Control>? importAction)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 6, 0, 2)
        };

        row.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 10,
            Opacity = 0.55,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (root is not null && importAction is not null)
        {
            var import = new Button { Content = "가져오기…", FontSize = 9, Padding = new Thickness(5, 1) };
            ToolTip.SetTip(import, "다른 폴더의 PNG를 골라 이 자리로 복제해 옵니다.");
            import.Click += (_, _) => importAction(import);
            row.Children.Add(import);

            var open = new Button { Content = "폴더 열기", FontSize = 9, Padding = new Thickness(5, 1) };
            ToolTip.SetTip(open, root);
            open.Click += (_, _) => UiGuard.Run(_session, "폴더 열기", () => OpenInExplorer(root));
            row.Children.Add(open);
        }

        return row;
    }

    /// <summary>지정 폴더를 파일 탐색기로 연다 (W48). 없으면 만들어서 연다 — 빈칸 채우기가 목적이다.</summary>
    private static void OpenInExplorer(string path)
    {
        System.IO.Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private static FilePickerFileType PngFileType => new("PNG 이미지") { Patterns = ["*.png"] };

    private async void ImportBackgroundsAsync(Control anchor)
    {
        await UiGuard.RunAsync(_session, "배경 가져오기", async () =>
        {
            if (_session is null ||
                TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
            {
                return;
            }

            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "가져올 배경 PNG 선택 (여러 개 가능)",
                AllowMultiple = true,
                FileTypeFilter = [PngFileType]
            });

            List<string> paths = files
                .Select(file => file.TryGetLocalPath())
                .Where(path => path is not null)
                .Select(path => path!)
                .ToList();

            if (paths.Count > 0)
            {
                _session.ImportBackgrounds(paths);
                Rebuild();
            }
        });
    }

    /// <summary>초상화 가져오기 (W48) — 캐릭터·변형을 정하고 PNG를 고르면 표정 번호가 차례로 붙는다.</summary>
    private void ShowPortraitImportFlyout(Control anchor)
    {
        if (_session is null)
        {
            return;
        }

        var panel = new StackPanel { Spacing = 4, MinWidth = 230 };

        panel.Children.Add(new TextBlock
        {
            Text = "어느 캐릭터의 초상화인가요? 고른 PNG는 표정 01, 02…로 차례로 들어갑니다.",
            FontSize = 10,
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 230
        });

        var character = new AutoCompleteBox
        {
            PlaceholderText = "캐릭터 키 (예: willow)",
            FontSize = 11,
            ItemsSource = _session.Definition.Speakers
                .Select(speaker => speaker.CharacterId)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0
        };
        panel.Children.Add(character);

        var variantRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        variantRow.Children.Add(new TextBlock
        {
            Text = "변형(포즈)",
            FontSize = 10,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center
        });
        var variant = new TextBox { Text = "a", Width = 40, FontSize = 11 };
        variantRow.Children.Add(variant);
        panel.Children.Add(variantRow);

        var pick = new Button
        {
            Content = "PNG 고르기…",
            FontSize = 11,
            Padding = new Thickness(8, 3),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        pick.Click += async (_, _) => await UiGuard.RunAsync(_session, "초상화 가져오기", async () =>
        {
            string key = character.Text?.Trim() ?? string.Empty;

            if (key.Length == 0)
            {
                _session.SetStatus("캐릭터 키를 먼저 입력하세요.");
                return;
            }

            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
            {
                return;
            }

            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = $"{key}의 초상화 PNG 선택 (여러 개 가능)",
                AllowMultiple = true,
                FileTypeFilter = [PngFileType]
            });

            List<string> paths = files
                .Select(file => file.TryGetLocalPath())
                .Where(path => path is not null)
                .Select(path => path!)
                .ToList();

            if (paths.Count > 0)
            {
                char variantSuffix = variant.Text?.Trim() is { Length: > 0 } trimmed ? trimmed[0] : 'a';
                _session.ImportPortraits(key, variantSuffix, paths);
                Rebuild();
            }
        });
        panel.Children.Add(pick);

        new Flyout { Content = panel, Placement = PlacementMode.Bottom }.ShowAt(anchor);
    }

    /// <summary>현재 루트 경로를 보여 주고 바꿀 수 있는 팝오버 — 지정은 언제나 여기서 가능하다.</summary>
    private void ShowRootsFlyout()
    {
        if (_session is null)
        {
            return;
        }

        var panel = new StackPanel { Spacing = 4, MinWidth = 220 };

        // 프로젝트 폴더가 모든 규약 경로의 기준이다 — 바로 열 수 있게 한다 (W48).
        if (_session.ProjectPath is { } projectPath &&
            System.IO.Path.GetDirectoryName(projectPath) is { } projectRoot)
        {
            var openProject = new Button
            {
                Content = "프로젝트 폴더 열기",
                FontSize = 11,
                Padding = new Thickness(8, 3),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            openProject.Click += (_, _) => UiGuard.Run(_session, "폴더 열기", () => OpenInExplorer(projectRoot));
            panel.Children.Add(openProject);
        }

        panel.Children.Add(new TextBlock
        {
            Text = $"배경: {_session.Project.AssetRoots.BackgroundsPath ?? "(미설정)"}",
            FontSize = 10,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(ConfigureRow("배경 폴더 지정…", backgrounds: true));
        panel.Children.Add(new TextBlock
        {
            Text = $"초상화: {_session.Project.AssetRoots.PortraitsPath ?? "(미설정)"}",
            FontSize = 10,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        panel.Children.Add(ConfigureRow("초상화 폴더 지정…", backgrounds: false));

        new Flyout { Content = panel, Placement = PlacementMode.Bottom }.ShowAt(RootsButton);
    }

    /// <summary>
    /// 튜닝 관리 팝오버 (W46) — 현재 상태와 두 진입점.
    /// [기본 튜닝 생성] = 앱 내장 실측 덤프 스냅샷을 프로젝트 옆 규약 폴더에 쓴다(기존 불가침).
    /// [튜닝 폴더 연결…] = 갖고 있는 ExportedTuning 폴더를 골라 규약 자리로 복사한다.
    /// </summary>
    private void ShowTuningFlyout()
    {
        if (_session is null)
        {
            return;
        }

        RuntimeTuningLibrary tuning = _session.TuningLibrary;
        var panel = new StackPanel { Spacing = 4, MinWidth = 260, MaxWidth = 340 };

        panel.Children.Add(new TextBlock
        {
            Text = $"상태: {tuning.Summary}",
            FontSize = 10,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap
        });

        if (tuning.Directory is { } directory)
        {
            panel.Children.Add(new TextBlock
            {
                Text = directory,
                FontSize = 9,
                Opacity = 0.55,
                TextWrapping = TextWrapping.Wrap
            });

            var openTuning = new Button
            {
                Content = "폴더 열기",
                FontSize = 11,
                Padding = new Thickness(8, 3),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 2, 0, 0)
            };
            openTuning.Click += (_, _) => UiGuard.Run(_session, "폴더 열기", () => OpenInExplorer(directory));
            panel.Children.Add(openTuning);
        }

        var create = new Button
        {
            Content = "기본 튜닝 생성",
            FontSize = 11,
            Padding = new Thickness(8, 3),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 6, 0, 0)
        };
        ToolTip.SetTip(create, "앱에 내장된 기본값(런타임 실측 덤프)으로 프로젝트 옆 ExportedTuning 폴더를 만듭니다. 이미 있으면 덮어쓰지 않습니다.");
        create.Click += (_, _) => UiGuard.Run(_session, "기본 튜닝 생성", () =>
        {
            _session.CreateDefaultTuning();
            Rebuild();
        });
        panel.Children.Add(create);

        var connect = new Button
        {
            Content = "튜닝 폴더 연결…",
            FontSize = 11,
            Padding = new Thickness(8, 3),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        ToolTip.SetTip(connect, "갖고 있는 ExportedTuning 폴더를 골라 프로젝트 옆으로 복사합니다. 같은 파일은 덮어씁니다.");
        connect.Click += async (_, _) => await UiGuard.RunAsync(_session, "튜닝 폴더 연결", async () =>
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanPickFolder: true } storage)
            {
                _session.SetStatus("이 환경에서는 폴더 선택 창을 열 수 없습니다.");
                return;
            }

            IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "연결할 튜닝 폴더 (ExportedTuning)",
                    AllowMultiple = false
                });

            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } picked)
            {
                _session.ConnectTuningFolder(picked);
                Rebuild();
            }
        });
        panel.Children.Add(connect);

        panel.Children.Add(new TextBlock
        {
            Text = "튜닝은 게임 화면의 실측 배치값입니다. 없으면 프리뷰 좌표가 근사로 표시됩니다.",
            FontSize = 9,
            Opacity = 0.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });

        new Flyout { Content = panel, Placement = PlacementMode.Bottom }.ShowAt(TuningButton);
    }

    /// <summary>빈 상태는 기능 잠금이 아니라 안내다 — 다음 할 일과 이동 버튼을 준다.</summary>
    private void BuildEmptyState()
    {
        TreeHost.Children.Add(new TextBlock
        {
            Text = "에셋 폴더가 설정되지 않았습니다. 폴더를 지정하면 배경과 초상화가 여기 나타납니다.",
            FontSize = 11,
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap
        });
        TreeHost.Children.Add(ConfigureRow("배경 폴더 지정…", backgrounds: true));
        TreeHost.Children.Add(ConfigureRow("초상화 폴더 지정…", backgrounds: false));

        // 지정한 뒤 무엇을 하면 되는지도 같은 자리에서 미리 알려 준다 (W-asset-02 §3.4).
        TreeHost.Children.Add(EmptyLabel(AssetExplorerModel.BackgroundPlacementGuide));
        TreeHost.Children.Add(EmptyLabel(AssetExplorerModel.PortraitPlacementGuide));
    }

    private Control ConfigureRow(string label, bool backgrounds)
    {
        var button = new Button
        {
            Content = label,
            FontSize = 11,
            Padding = new Thickness(8, 3),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        button.Click += async (_, _) => await UiGuard.RunAsync(_session, "에셋 폴더 지정", async () =>
        {
            if (_session is not null && await AssetRootPicker.PickAsync(this, _session, backgrounds))
            {
                Rebuild();
            }
        });

        return button;
    }

    private void AddItems(IReadOnlyList<AssetExplorerItem> items, int depth, string parentPath)
    {
        foreach (AssetExplorerItem item in items)
        {
            string path = $"{parentPath}/{item.Label}";
            bool hasChildren = item.Children.Count > 0;

            TreeHost.Children.Add(BuildRow(item, depth, path, hasChildren));

            if (hasChildren && !_collapsedPaths.Contains(path))
            {
                AddItems(item.Children, depth + 1, path);
            }
        }
    }

    private Control BuildRow(AssetExplorerItem item, int depth, string path, bool hasChildren)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Margin = new Thickness(depth * 14, 0, 0, 0)
        };

        if (hasChildren)
        {
            var toggle = new Button
            {
                Content = _collapsedPaths.Contains(path) ? "▶" : "▼",
                FontSize = 8,
                Padding = new Thickness(3, 1),
                VerticalAlignment = VerticalAlignment.Center
            };

            toggle.Click += (_, _) =>
            {
                if (!_collapsedPaths.Remove(path))
                {
                    _collapsedPaths.Add(path);
                }

                Rebuild();
            };

            row.Children.Add(toggle);
        }

        if (item.FilePath is { } filePath && _session?.ImageCache.Get(filePath) is { } bitmap)
        {
            row.Children.Add(new Image
            {
                Source = bitmap,
                Width = 26,
                Height = 26,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        row.Children.Add(new TextBlock
        {
            Text = item.Label,
            FontSize = 11,
            FontWeight = item.Kind is AssetExplorerItemKind.Character or AssetExplorerItemKind.Folder
                ? FontWeight.SemiBold
                : FontWeight.Normal,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (item.Kind == AssetExplorerItemKind.Background && item.BackgroundKey is { } key)
        {
            row.Children.Add(new TextBlock
            {
                Text = key,
                FontSize = 9,
                Opacity = 0.55,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        if (item.Problem is { } problem)
        {
            row.Children.Add(new TextBlock
            {
                Text = $"⚠ {problem}",
                FontSize = 9,
                Foreground = ProblemBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });
        }
        else if (hasChildren && item.HasProblem && _collapsedPaths.Contains(path))
        {
            // 접힌 그룹 안의 문제도 밖에서 보인다.
            row.Children.Add(new TextBlock
            {
                Text = "⚠",
                FontSize = 10,
                Foreground = ProblemBrush,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        // 프리뷰 드래그 소스 — 페이로드 형식은 StageDragFormats로 드롭 쪽과 공유한다.
        if (item.BackgroundKey is not null || item.Portrait is not null)
        {
            row.Cursor = new Cursor(StandardCursorType.Hand);
            // async void 핸들러다 — 포획 없이 새어 나간 예외는 곧 앱 종료다(X1, 불변식 4).
            row.PointerPressed += async (_, args) => await UiGuard.RunAsync(_session, "에셋 드래그", async () =>
            {
                if (!args.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
                {
                    return;
                }

                var payload = new DataTransferItem();

                if (item.BackgroundKey is { } backgroundKey)
                {
                    payload.Set(StageDragFormats.Background, backgroundKey);
                }
                else if (item.Portrait is { } portrait)
                {
                    payload.Set(
                        StageDragFormats.Portrait,
                        $"{portrait.CharacterId}|{portrait.VariantKey}|{portrait.EmotionKey}");
                }

                var data = new DataTransfer();
                data.Add(payload);
                await DragDrop.DoDragDropAsync(args, data, DragDropEffects.Copy);
            });
        }

        row.Tag = item;
        return row;
    }

    private static Control SectionHeader(string text) => new TextBlock
    {
        Text = text,
        FontSize = 10,
        Opacity = 0.55,
        Margin = new Thickness(0, 6, 0, 2)
    };

    private static Control EmptyLabel(string text) => new TextBlock
    {
        Text = text,
        FontSize = 11,
        Opacity = 0.6,
        TextWrapping = TextWrapping.Wrap
    };
}
