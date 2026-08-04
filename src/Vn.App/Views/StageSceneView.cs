using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Vn.App.Services;
using Vn.Authoring.Assets;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Flow;

namespace Vn.App.Views;

/// <summary>탐색기 → 프리뷰 드래그의 데이터 형식. 소스와 대상이 같은 형식 객체를 쓴다.</summary>
internal static class StageDragFormats
{
    public static readonly DataFormat<string> Background =
        DataFormat.CreateStringApplicationFormat("vntool/background-key");

    /// <summary>"characterId|variantKey|emotionKey".</summary>
    public static readonly DataFormat<string> Portrait =
        DataFormat.CreateStringApplicationFormat("vntool/portrait-key");
}

/// <summary>
/// 기준 해상도 좌표계의 무대 한 장면을 그린다 — "완성된 비주얼노벨이라 가정하고 보는 뷰"의 근사.
///
/// 배치는 전부 <see cref="StageSceneComposer"/>의 순수 계산에서 오고, 이 클래스는 그 결과를
/// Avalonia 컨트롤로 옮길 뿐이다. 도킹 미니 패널과 분리 프리뷰 창이 <b>같은 인스턴스 종류</b>를
/// 쓰므로 렌더 규칙의 사본이 없다. Viewbox(Uniform)가 창 크기와 무관한 레터박스를 보장한다.
///
/// 에셋이 없으면 같은 배치가 플레이스홀더로 나온다. 어느 키가 없는지는 항상 문자로 남는다.
/// </summary>
internal sealed class StageSceneView : UserControl
{
    private static readonly SolidColorBrush StageBackground = new(Color.FromRgb(46, 46, 50));
    private static readonly SolidColorBrush BoxBackground = new(Color.FromArgb(205, 12, 12, 16));
    private static readonly SolidColorBrush NameTagBackground = new(Color.FromArgb(230, 250, 204, 21));
    private static readonly SolidColorBrush PlaceholderTile = new(Color.FromArgb(70, 255, 255, 255));
    private static readonly SolidColorBrush SpeakerHighlight = new(Color.FromRgb(250, 204, 21));

    private AuthoringSession? _session;
    private MiniStagePreviewRequest? _request;
    private readonly Canvas _canvas;
    private readonly Viewbox _viewbox;
    private bool _statsVisible = true; // 스탯 HUD 토글 (X3) — 기본 표시

    /// <summary>직접 조작이 편집을 만들었다. 편집기 카드가 새 커맨드 행을 그려야 한다.</summary>
    internal event Action? ManipulationApplied;

    public StageSceneView()
    {
        _canvas = new Canvas { Background = StageBackground, ClipToBounds = true };
        _viewbox = new Viewbox
        {
            Stretch = Stretch.Uniform,
            Child = _canvas,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // 레터박스 여백은 검정 — 창 어디를 늘려도 무대 비율은 변하지 않는다.
        Content = new Border { Background = Brushes.Black, Child = _viewbox };

        // 무대 빈 곳(=배경) 클릭 → 배경 선택. 초상화 클릭은 자기 핸들러가 먼저 잡는다.
        // 클릭·드롭 경로는 전부 UiGuard 아래다 — 예외는 무동작 + 상태줄이 된다(X1, 불변식 4).
        _canvas.PointerPressed += (_, args) =>
        {
            if (!args.Handled && CanManipulate())
            {
                UiGuard.Run(_session, "배경 선택", () => ShowBackgroundPopover(this));
                args.Handled = true;
            }
        };

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, args) => UiGuard.Run(_session, "드래그 판정", () =>
        {
            args.DragEffects = CanManipulate() && args.DataTransfer is { } transfer &&
                (transfer.Contains(StageDragFormats.Background) || transfer.Contains(StageDragFormats.Portrait))
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }));
        AddHandler(DragDrop.DropEvent, (_, args) => UiGuard.Run(_session, "에셋 드롭", () => OnDrop(args)));
    }

    internal void Attach(AuthoringSession session) => _session = session;

    internal void Render(MiniStagePreviewRequest? request)
    {
        _request = request;
        _canvas.Children.Clear();

        (double width, double height) = _session?.Definition.PreviewResolution ?? (1920, 1080);
        _canvas.Width = width;
        _canvas.Height = height;
        double em = height / 1080 * 34; // 기준 해상도 비례 글자 크기

        if (request is null)
        {
            AddCenteredText(width, height, em, "라인을 선택하면 그 시점의 무대가 표시됩니다.");
            return;
        }

        PreviewAssetLibrary library = _session?.AssetLibrary ?? PreviewAssetLibrary.Empty;
        string? speakerCharacterId = _session?.Definition.FindSpeakerCharacterId(request.SpeakerName);

        RenderBackground(library, request.State, width, height, em);

        StageSceneLayout layout = StageSceneComposer.Compose(
            request.State,
            request.SpeakerName,
            speakerCharacterId,
            width,
            height);

        foreach (StagePortraitPlacement portrait in layout.Portraits)
        {
            RenderPortrait(library, portrait, em);
        }

        if (!request.HasPresentation)
        {
            AddCenteredText(width, height * 0.7, em, "연출 공급 없음 — 화자만 표시합니다.");
        }

        if (request.HasPresentation && layout.Portraits.Count == 0)
        {
            AddCenteredText(width, height * 0.7, em * 0.8, "무대 위에 표시된 캐릭터가 없습니다.");
        }

        RenderDialogueBox(layout, request, em);

        // 발행 결과처럼 잠긴 화면임을 숨기지 않는다 — 조작이 안 되는 이유가 그 자리에 있다.
        if (request.EditContext is { DisabledReason: { } reason })
        {
            Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
                CornerRadius = new CornerRadius(em * 0.2),
                Padding = new Thickness(em * 0.4, em * 0.15),
                Child = new TextBlock
                {
                    Text = $"읽기 전용 — {reason}",
                    FontSize = em * 0.55,
                    Foreground = Brushes.White,
                    Opacity = 0.9
                }
            }, new StageRect(width * 0.02, height - em * 1.5, width * 0.7, em * 1.2));
        }

        // 대사창에 이름표가 없는 스타일에서 화자가 무대에도 없으면 상단에 이름만 남긴다.
        if (layout.OffStageSpeakerName is { } offStage &&
            layout.DialogueBox is { NameRect: null })
        {
            Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)),
                CornerRadius = new CornerRadius(em * 0.2),
                Padding = new Thickness(em * 0.5, em * 0.2),
                Child = new TextBlock
                {
                    Text = $"화자: {offStage}",
                    FontSize = em * 0.8,
                    Foreground = Brushes.White
                }
            }, new StageRect(width * 0.02, height * 0.03, width * 0.4, em * 1.6));
        }

        RenderStatsHud(request, width, em);
    }

    private void RenderBackground(
        PreviewAssetLibrary library,
        MiniStageState state,
        double width,
        double height,
        double em)
    {
        string? keyText;

        if (state.BackgroundKey is null)
        {
            keyText = "배경 없음";
        }
        else
        {
            BackgroundResolution background = library.ResolveBackground(state.BackgroundKey);

            if (background.FilePath is { } path && _session?.ImageCache.Get(path) is { } bitmap)
            {
                Add(new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.UniformToFill,
                    Width = width,
                    Height = height
                }, new StageRect(0, 0, width, height));
                return;
            }

            keyText = library.BackgroundsConfigured
                ? $"배경 없음: {background.SpriteKey}"
                : $"배경 {background.SpriteKey} (에셋 루트 미설정)";
        }

        Add(new TextBlock
        {
            Text = keyText,
            FontSize = em * 0.75,
            Foreground = Brushes.White,
            Opacity = 0.8,
            TextAlignment = TextAlignment.Center,
            Width = width
        }, new StageRect(0, height * 0.04, width, em * 1.4));
    }

    private void RenderPortrait(PreviewAssetLibrary library, StagePortraitPlacement portrait, double em)
    {
        MiniStageSlot slot = portrait.Slot;
        Control image;
        string? caption = null;

        if (slot.CharacterId is null)
        {
            image = PlaceholderPortrait(portrait.SlotKey, portrait.Rect, em);
            caption = $"{portrait.SlotKey} (캐스팅 없음)";
        }
        else
        {
            PortraitResolution resolution = library.ResolvePortrait(
                slot.CharacterId,
                slot.VariantKey,
                slot.EmotionKey);

            if (resolution.FilePath is { } path && _session?.ImageCache.Get(path) is { } bitmap)
            {
                image = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    Width = portrait.Rect.Width,
                    Height = portrait.Rect.Height,
                    VerticalAlignment = VerticalAlignment.Bottom
                };

                if (resolution.Kind == AssetResolutionKind.Fallback)
                {
                    caption = $"{resolution.RequestedKey} → 기본";
                }
            }
            else
            {
                image = PlaceholderPortrait(slot.CharacterId, portrait.Rect, em);
                caption = $"없음: {resolution.RequestedKey}";
            }
        }

        if (slot.Mirrored)
        {
            image.RenderTransformOrigin = RelativePoint.Center;
            image.RenderTransform = new ScaleTransform(-1, 1);
        }

        if (portrait.IsSpeaker)
        {
            image = new Border
            {
                BorderBrush = SpeakerHighlight,
                BorderThickness = new Thickness(em * 0.09),
                CornerRadius = new CornerRadius(em * 0.15),
                Width = portrait.Rect.Width,
                Height = portrait.Rect.Height,
                Child = image
            };
        }

        if (CanManipulate())
        {
            image.Cursor = new Cursor(StandardCursorType.Hand);
            image.PointerPressed += (_, args) =>
            {
                UiGuard.Run(
                    _session,
                    "캐릭터 조작",
                    () => ShowCharacterPopover(image, portrait.SlotKey, portrait.Slot));
                args.Handled = true;
            };
        }

        Add(image, portrait.Rect);

        if (caption is not null)
        {
            Add(new TextBlock
            {
                Text = caption,
                FontSize = em * 0.55,
                Foreground = Brushes.White,
                Opacity = 0.85,
                TextAlignment = TextAlignment.Center,
                Width = portrait.Rect.Width
            }, new StageRect(portrait.Rect.X, portrait.Rect.Bottom + em * 0.15, portrait.Rect.Width, em));
        }
    }

    private void RenderDialogueBox(StageSceneLayout layout, MiniStagePreviewRequest request, double em)
    {
        if (layout.DialogueBox is not { } box || !request.HasPresentation && request.LineText is null)
        {
            return;
        }

        if (box.TopBand is { } top && box.BottomBand is { } bottom)
        {
            Add(new Border { Background = Brushes.Black }, top);
            Add(new Border { Background = Brushes.Black }, bottom);
        }

        if (box.BoxRect is { } boxRect)
        {
            Add(new Border
            {
                Background = BoxBackground,
                CornerRadius = new CornerRadius(em * 0.35),
                Width = boxRect.Width,
                Height = boxRect.Height
            }, boxRect);
        }

        if (box.NameRect is { } nameRect && !string.IsNullOrWhiteSpace(request.SpeakerName))
        {
            Add(new Border
            {
                Background = NameTagBackground,
                CornerRadius = new CornerRadius(em * 0.2),
                Width = nameRect.Width,
                Height = nameRect.Height,
                Child = new TextBlock
                {
                    Text = request.SpeakerName,
                    FontSize = em * 0.75,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }, nameRect);
        }

        Add(new TextBlock
        {
            Text = request.LineText ?? string.Empty,
            FontSize = em,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = box.Style == StageDialogueBoxStyle.LetterBox
                ? TextAlignment.Center
                : TextAlignment.Left,
            Width = box.TextRect.Width,
            MaxHeight = box.TextRect.Height
        }, box.TextRect);

        // Speaker 근사로 대신 그린 종류는 뱃지로 정직하게 알린다.
        if (box.Approximated && box.BoxRect is { } badgeAnchor)
        {
            Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 37, 99, 235)),
                CornerRadius = new CornerRadius(em * 0.15),
                Padding = new Thickness(em * 0.3, em * 0.1),
                Child = new TextBlock
                {
                    Text = $"{box.BoxKind} (근사)",
                    FontSize = em * 0.55,
                    Foreground = Brushes.White
                }
            }, new StageRect(badgeAnchor.Right - em * 6, badgeAnchor.Y - em * 0.9, em * 5.7, em * 1.1));
        }
    }

    /// <summary>
    /// 스탯 HUD (X3) — 등록 변수의 "이 라인까지" 누적 값. 갈래를 가리지 않는
    /// 문서 순서 근사이므로 그 사실을 라벨에 그대로 쓴다(규칙 14).
    /// </summary>
    private void RenderStatsHud(MiniStagePreviewRequest request, double width, double em)
    {
        if (request.Stats is not { Count: > 0 } stats)
        {
            return;
        }

        var toggle = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
            CornerRadius = new CornerRadius(em * 0.2),
            Padding = new Thickness(em * 0.35, em * 0.12),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock
            {
                Text = _statsVisible ? "스탯 ▾" : "스탯 ▸",
                FontSize = em * 0.6,
                Foreground = Brushes.White,
                Opacity = 0.9
            }
        };
        ToolTip.SetTip(toggle, "플레이어 스탯 HUD를 켜고 끕니다.");

        toggle.PointerPressed += (_, args) =>
        {
            _statsVisible = !_statsVisible;
            Render(_request);
            args.Handled = true;
        };

        Add(toggle, new StageRect(width - em * 3.4, em * 0.5, em * 2.9, em * 1.2));

        if (!_statsVisible)
        {
            return;
        }

        var rows = new StackPanel { Spacing = em * 0.08 };

        foreach (StatFold.StatValue stat in stats)
        {
            rows.Children.Add(new TextBlock
            {
                Text = $"{stat.Variable}  {stat.Display}",
                FontSize = em * 0.62,
                Foreground = Brushes.White
            });
        }

        rows.Children.Add(new TextBlock
        {
            Text = "문서 순서 근사 — 갈래 미반영",
            FontSize = em * 0.45,
            Foreground = Brushes.White,
            Opacity = 0.55
        });

        Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
            CornerRadius = new CornerRadius(em * 0.2),
            Padding = new Thickness(em * 0.4, em * 0.25),
            Child = rows
        }, new StageRect(width - em * 8.4, em * 1.9, em * 7.9, em * (stats.Count + 2)));
    }

    private Control PlaceholderPortrait(string label, StageRect rect, double em)
    {
        return new Border
        {
            Width = rect.Width,
            Height = rect.Height,
            Background = PlaceholderTile,
            CornerRadius = new CornerRadius(em * 0.2),
            Child = new TextBlock
            {
                Text = label.Length == 0 ? "?" : label[..1].ToUpperInvariant(),
                FontSize = em * 2.6,
                FontWeight = FontWeight.Bold,
                Opacity = 0.6,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private void AddCenteredText(double width, double y, double em, string text)
    {
        Add(new TextBlock
        {
            Text = text,
            FontSize = em * 0.85,
            Foreground = Brushes.White,
            Opacity = 0.75,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Width = width * 0.8
        }, new StageRect(width * 0.1, y * 0.5, width * 0.8, em * 3));
    }

    private void Add(Control control, StageRect rect)
    {
        Canvas.SetLeft(control, rect.X);
        Canvas.SetTop(control, rect.Y);
        _canvas.Children.Add(control);
    }

    // ── 직접 조작 (W20) — 인식 22종 어휘 = 조작 어휘 ─────────────────────

    private bool CanManipulate() => _session is not null && _request?.EditContext?.Editable == true;

    private PresentationCommandCatalog ManipulationCatalog =>
        PresentationCommandCatalog.For(_session?.Definition);

    /// <summary>조작 하나를 편집으로 만든다 — 같은 종류는 수정, 전부 ProjectEditor 경유.</summary>
    private void ApplyStageCommand(string outputCommand, IReadOnlyDictionary<string, string> arguments)
    {
        if (_session is null || _request?.EditContext is not { Editable: true } context)
        {
            return;
        }

        UiGuard.Run(_session, "무대 조작", () =>
        {
            PresentationStageActions.Apply(
                _session.Editor,
                ManipulationCatalog,
                context.PresentationNodeId,
                context.LineId!,
                outputCommand,
                arguments);
            ManipulationApplied?.Invoke();
        });
    }

    /// <summary>
    /// 캐릭터 클릭 팝오버 — 표정 그리드·등장/퇴장·미러·variant.
    /// 표정은 보이는 캐릭터면 face_swap, 캐스팅만 된 캐릭터면 face다.
    /// </summary>
    private void ShowCharacterPopover(Control anchor, string slotKey, MiniStageSlot slot)
    {
        if (_session is null)
        {
            return;
        }

        PreviewAssetLibrary library = _session.AssetLibrary;
        var panel = new StackPanel { Spacing = 6, MinWidth = 200 };
        var flyout = new Flyout { Content = panel, Placement = PlacementMode.Pointer };

        void Commit(string outputCommand, params (string Key, string Value)[] args)
        {
            flyout.Hide();
            ApplyStageCommand(
                outputCommand,
                args.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        }

        panel.Children.Add(new TextBlock
        {
            Text = $"{slotKey}" + (slot.CharacterId is null ? " (캐스팅 없음)" : $" · {slot.CharacterId}"),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold
        });

        if (slot.CharacterId is { } characterId)
        {
            // 표정 그리드 — 이 캐릭터의 현재 variant가 가진 emotion 썸네일.
            string variant = string.IsNullOrWhiteSpace(slot.VariantKey) ? PortraitKey.DefaultVariantKey : slot.VariantKey;
            PortraitAssetEntry[] emotions = library.PortraitEntries
                .Where(entry => string.Equals(entry.Key.CharacterId, characterId, StringComparison.Ordinal) &&
                    string.Equals(entry.Key.VariantKey, variant, StringComparison.Ordinal) &&
                    entry.FileExists)
                .OrderBy(entry => entry.Key.EmotionKey, StringComparer.Ordinal)
                .ToArray();

            if (emotions.Length > 0)
            {
                var grid = new WrapPanel { MaxWidth = 240 };

                foreach (PortraitAssetEntry entry in emotions)
                {
                    var cell = new StackPanel { Margin = new Thickness(0, 0, 4, 4) };

                    if (_session.ImageCache.Get(entry.FilePath!) is { } bitmap)
                    {
                        cell.Children.Add(new Image { Source = bitmap, Width = 44, Height = 60, Stretch = Stretch.Uniform });
                    }

                    cell.Children.Add(new TextBlock
                    {
                        Text = entry.Key.EmotionKey,
                        FontSize = 9,
                        HorizontalAlignment = HorizontalAlignment.Center
                    });

                    // 보이면 face_swap(파라미터 emotion), 캐스팅만 됐으면 face(파라미터 emotionKey).
                    string output = slot.Visible ? "face_swap" : "face";
                    string parameterName = slot.Visible ? "emotion" : "emotionKey";
                    string emotionKey = entry.Key.EmotionKey;

                    var cellButton = new Button { Content = cell, Padding = new Thickness(2) };
                    cellButton.Click += (_, _) => Commit(output, ("slot", slotKey), (parameterName, emotionKey));
                    grid.Children.Add(cellButton);
                }

                panel.Children.Add(new TextBlock { Text = "표정", FontSize = 10, Opacity = 0.6 });
                panel.Children.Add(grid);
            }

            // variant 전환 — pose.
            string[] variants = library.PortraitEntries
                .Where(entry => string.Equals(entry.Key.CharacterId, characterId, StringComparison.Ordinal))
                .Select(entry => entry.Key.VariantKey)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();

            if (variants.Length > 1)
            {
                var variantRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                variantRow.Children.Add(new TextBlock
                {
                    Text = "variant",
                    FontSize = 10,
                    Opacity = 0.6,
                    VerticalAlignment = VerticalAlignment.Center
                });

                foreach (string variantKey in variants)
                {
                    var variantButton = new Button
                    {
                        Content = variantKey,
                        FontSize = 10,
                        Padding = new Thickness(7, 2),
                        IsEnabled = !string.Equals(variantKey, variant, StringComparison.Ordinal)
                    };
                    variantButton.Click += (_, _) => Commit("pose", ("slot", slotKey), ("variantKey", variantKey));
                    variantRow.Children.Add(variantButton);
                }

                panel.Children.Add(variantRow);
            }
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

        var visibility = new Button
        {
            Content = slot.Visible ? "퇴장 (fade_out)" : "등장 (fade_in)",
            FontSize = 10,
            Padding = new Thickness(7, 2)
        };
        visibility.Click += (_, _) => Commit(slot.Visible ? "fade_out" : "fade_in", ("slot", slotKey));
        actions.Children.Add(visibility);

        var mirror = new Button
        {
            Content = slot.Mirrored ? "반전 해제" : "좌우 반전",
            FontSize = 10,
            Padding = new Thickness(7, 2)
        };
        // 토글 대신 명시 토큰 — 같은 커맨드를 수정하는 규칙과 겹쳐도 결과가 예측 가능하다.
        mirror.Click += (_, _) => Commit("mirror", ("slot", slotKey), ("mode", slot.Mirrored ? "right" : "left"));
        actions.Children.Add(mirror);

        panel.Children.Add(actions);
        flyout.ShowAt(anchor);
    }

    /// <summary>배경 클릭 팝오버 — 탐색기와 같은 목록에서 고르면 bg_sprite/bg_spawn이 된다.</summary>
    private void ShowBackgroundPopover(Control anchor)
    {
        if (_session is null || _request is null)
        {
            return;
        }

        PreviewAssetLibrary library = _session.AssetLibrary;
        var list = new StackPanel { Spacing = 2 };
        var panel = new StackPanel { Spacing = 4, MinWidth = 220 };
        var flyout = new Flyout { Content = panel, Placement = PlacementMode.Pointer };

        panel.Children.Add(new TextBlock { Text = "배경 교체", FontSize = 11, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 300 });

        if (library.BackgroundEntries.Count == 0)
        {
            list.Children.Add(new TextBlock
            {
                Text = library.BackgroundsConfigured ? "배경 PNG가 없습니다." : "배경 폴더를 먼저 지정하세요.",
                FontSize = 10,
                Opacity = 0.65
            });
        }

        foreach (BackgroundAssetEntry entry in library.BackgroundEntries)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

            if (_session.ImageCache.Get(entry.FilePath) is { } bitmap)
            {
                row.Children.Add(new Image { Source = bitmap, Width = 48, Height = 27, Stretch = Stretch.UniformToFill });
            }

            row.Children.Add(new TextBlock
            {
                Text = entry.SpriteKey,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            });

            var rowButton = new Button
            {
                Content = row,
                Padding = new Thickness(4, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };

            rowButton.Click += (_, _) =>
            {
                flyout.Hide();
                ApplyBackground(entry.SpriteKey);
            };

            list.Children.Add(rowButton);
        }

        flyout.ShowAt(anchor);
    }

    private void ApplyBackground(string spriteKey)
    {
        if (_request is null)
        {
            return;
        }

        (string outputCommand, IReadOnlyDictionary<string, string> arguments) =
            PresentationStageActions.BackgroundCommandFor(_request.State, spriteKey);
        ApplyStageCommand(outputCommand, arguments);
    }

    /// <summary>
    /// 탐색기에서 끌어온 에셋. 배경은 배경 교체와 같고, 초상화는 무대 위 여부에 따라
    /// 표정 교체 또는 캐스팅 시퀀스(슬롯 선택 → slot+cast+fade_in 개별 3개)다.
    /// </summary>
    private void OnDrop(DragEventArgs args)
    {
        if (!CanManipulate() || args.DataTransfer is not { } transfer)
        {
            return;
        }

        if (transfer.TryGetValue(StageDragFormats.Background) is { } backgroundKey)
        {
            ApplyBackground(backgroundKey);
            args.Handled = true;
            return;
        }

        if (transfer.TryGetValue(StageDragFormats.Portrait) is not { } payload)
        {
            return;
        }

        string[] parts = payload.Split('|');

        if (parts.Length != 3 || _request is null)
        {
            return;
        }

        (string characterId, string variantKey, string emotionKey) = (parts[0], parts[1], parts[2]);

        if (PresentationStageActions.FaceCommandFor(_request.State, characterId) is { } face)
        {
            ApplyStageCommand(
                face.OutputCommand,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["slot"] = face.SlotKey,
                    [face.OutputCommand == "face" ? "emotionKey" : "emotion"] = emotionKey
                });
        }
        else
        {
            ShowCastingPopover(characterId, variantKey, emotionKey);
        }

        args.Handled = true;
    }

    /// <summary>
    /// 무대에 없는 캐릭터의 슬롯 선택. 시퀀스는 매크로가 아니라 개별 커맨드 3개로
    /// 바인딩에 그대로 남는다 — 툴은 표준 등장 시퀀스를 대신 타이핑해 줄 뿐이다.
    /// </summary>
    private void ShowCastingPopover(string characterId, string variantKey, string emotionKey)
    {
        if (_session is null || _request?.EditContext is not { Editable: true } context)
        {
            return;
        }

        var panel = new StackPanel { Spacing = 4, MinWidth = 190 };
        var flyout = new Flyout { Content = panel, Placement = PlacementMode.Pointer };

        panel.Children.Add(new TextBlock
        {
            Text = $"{characterId}를 세울 슬롯 (slot + cast + fade_in)",
            FontSize = 10,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap
        });

        void Commit(string slotKey)
        {
            flyout.Hide();
            UiGuard.Run(_session, "캐스팅", () =>
            {
                PresentationStageActions.ApplyCastingSequence(
                    _session.Editor,
                    ManipulationCatalog,
                    _request!.State,
                    context.PresentationNodeId,
                    context.LineId!,
                    slotKey,
                    characterId,
                    variantKey,
                    emotionKey);
                ManipulationApplied?.Invoke();
            });
        }

        // 비어 있는 기존 슬롯이 먼저, 그다음 새 슬롯 이름.
        foreach ((string slotKey, MiniStageSlot slot) in _request.State.Slots
                     .Where(item => item.Value.CharacterId is null)
                     .OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var existing = new Button
            {
                Content = $"{slotKey} (빈 슬롯)",
                FontSize = 11,
                Padding = new Thickness(8, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            existing.Click += (_, _) => Commit(slotKey);
            panel.Children.Add(existing);
        }

        string nextFree = NextFreeSlotKey(_request.State);
        var fresh = new Button
        {
            Content = $"{nextFree} (새 슬롯)",
            FontSize = 11,
            Padding = new Thickness(8, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        fresh.Click += (_, _) => Commit(nextFree);
        panel.Children.Add(fresh);

        var input = new TextBox { PlaceholderText = "직접 입력 후 Enter", FontSize = 11 };
        input.KeyDown += (_, keyArgs) =>
        {
            if (keyArgs.Key == Key.Enter && !string.IsNullOrWhiteSpace(input.Text))
            {
                Commit(input.Text.Trim());
            }
        };
        panel.Children.Add(input);

        flyout.ShowAt(this);
    }

    private static string NextFreeSlotKey(MiniStageState state)
    {
        for (int index = 1; ; index++)
        {
            string candidate = $"c{index}";

            if (!state.Slots.ContainsKey(candidate))
            {
                return candidate;
            }
        }
    }
}
