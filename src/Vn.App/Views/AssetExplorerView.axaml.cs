using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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

        RefreshButton.Click += (_, _) =>
        {
            _session?.RefreshAssets();
            Rebuild();
        };
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
            TreeHost.Children.Add(EmptyLabel("PNG 파일이 없습니다"));
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
            TreeHost.Children.Add(EmptyLabel("매니페스트 항목이 없습니다"));
        }
        else
        {
            AddItems(tree.PortraitItems, depth: 0, parentPath: "pt");
        }
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

        button.Click += async (_, _) =>
        {
            if (_session is not null && await AssetRootPicker.PickAsync(this, _session, backgrounds))
            {
                Rebuild();
            }
        };

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

        // W20에서 이 행이 프리뷰 드래그의 소스가 된다 — 페이로드는 item에 이미 있다.
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
        Opacity = 0.6
    };
}
