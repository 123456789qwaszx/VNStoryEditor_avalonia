using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
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

        RefreshButton.Click += (_, _) => UiGuard.Run(_session, "에셋 새로 고침", () =>
        {
            _session?.RefreshAssets();
            Rebuild();
        });
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

        TreeHost.Children.Add(SectionHeader("배경"));

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

        TreeHost.Children.Add(SectionHeader("초상화"));

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
    }

    /// <summary>현재 루트 경로를 보여 주고 바꿀 수 있는 팝오버 — 지정은 언제나 여기서 가능하다.</summary>
    private void ShowRootsFlyout()
    {
        if (_session is null)
        {
            return;
        }

        var panel = new StackPanel { Spacing = 4, MinWidth = 220 };

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
