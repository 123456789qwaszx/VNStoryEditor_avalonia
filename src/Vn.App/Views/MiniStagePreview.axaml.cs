using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Vn.App.Services;
using Vn.Authoring.Assets;
using Vn.Authoring.Flow;

namespace Vn.App.Views;

/// <summary>공유 무대 프리뷰 패널에 밀어 넣는 요청 하나. 폴드는 호출자가 이미 끝냈다.</summary>
/// <param name="HasPresentation">false면 연출 공급이 없는 것 — 오류가 아니라 화자만 표시한다.</param>
/// <param name="Notice">선택 라인이 발행본에 없다는 등 호출자가 덧붙이는 알림.</param>
internal sealed record MiniStagePreviewRequest(
    string ContextLabel,
    MiniStageState State,
    bool HasPresentation,
    string? SelectedLineId,
    string? SpeakerName,
    string? LineText,
    string? Notice = null);

/// <summary>
/// "무슨 배경에서 누가 말하는가"를 보여 주는 읽기 전용 미니 무대.
///
/// 장면 재현이 아니다 — 배경 이미지 위에 visible 슬롯의 초상화를 슬롯 키 순서로
/// <b>균등 나열</b>하고 화자를 강조할 뿐, 좌표·크기·이펙트는 그리지 않는다(그건 2b).
///
/// 에셋이 없어도 같은 레이아웃이 플레이스홀더로 나온다 — 어느 키가 없는지는
/// 항상 문자로 보이고, 반영하지 못한 연출은 개수와 이름으로 보인다.
/// 편의 기능이 저작을 막지 않고, 침묵하는 실패를 만들지 않는다.
/// </summary>
public partial class MiniStagePreview : UserControl
{
    private static readonly SolidColorBrush SpeakerHighlight = new(Color.FromRgb(250, 204, 21));
    private static readonly SolidColorBrush PlaceholderTile = new(Color.FromArgb(70, 255, 255, 255));

    private AuthoringSession? _session;
    private MiniStagePreviewRequest? _current;
    private bool _unhandledExpanded;

    public MiniStagePreview()
    {
        InitializeComponent();

        RefreshButton.Click += (_, _) =>
        {
            _session?.RefreshAssets();
            Render();
        };
        BackgroundsRootButton.Click += async (_, _) => await PickAssetRoot(backgrounds: true);
        PortraitsRootButton.Click += async (_, _) => await PickAssetRoot(backgrounds: false);
    }

    internal void Attach(AuthoringSession session) => _session = session;

    /// <summary>null이면 보여 줄 라인이 없는 상태다(노드 미선택 등).</summary>
    internal void Show(MiniStagePreviewRequest? request)
    {
        _current = request;
        Render();
    }

    private void Render()
    {
        MiniStagePreviewRequest? request = _current;
        PreviewAssetLibrary library = _session?.AssetLibrary ?? PreviewAssetLibrary.Empty;

        ContextText.Text = request?.ContextLabel ?? string.Empty;
        BadgeRow.Children.Clear();
        NoticeHost.Children.Clear();
        UnhandledHost.Children.Clear();
        UnhandledHost.IsVisible = false;
        PortraitRow.Children.Clear();
        BackgroundImage.Source = null;
        BackgroundKeyText.Text = string.Empty;
        OffStageSpeakerText.IsVisible = false;

        if (request is null)
        {
            StageEmptyText.Text = "라인을 선택하면 그 시점의 무대가 표시됩니다.";
            StageEmptyText.IsVisible = true;
            BoxKindBadge.IsVisible = false;
            LineText.Text = string.Empty;
            return;
        }

        MiniStageState state = request.State;
        bool hasSpeaker = !string.IsNullOrWhiteSpace(request.SpeakerName);

        RenderBackground(library, state);
        RenderPortraits(library, state, request);

        // 대사 본문과 박스 종류 — 박스는 화자 유무에 따라 named/protagonist가 갈린다.
        BoxKindBadge.IsVisible = request.HasPresentation;
        BoxKindText.Text = state.BoxKindFor(hasSpeaker);
        LineText.Text = hasSpeaker ? $"{request.SpeakerName}: {request.LineText}" : request.LineText;

        RenderBadges(state, request);
        RenderNotices(library, request);
    }

    private void RenderBackground(PreviewAssetLibrary library, MiniStageState state)
    {
        StageEmptyText.IsVisible = false;

        if (state.BackgroundKey is null)
        {
            BackgroundKeyText.Text = "배경 없음";
            return;
        }

        BackgroundResolution background = library.ResolveBackground(state.BackgroundKey);

        if (background.FilePath is { } path && _session?.ImageCache.Get(path) is { } bitmap)
        {
            BackgroundImage.Source = bitmap;
            BackgroundKeyText.Text = string.Empty;
        }
        else
        {
            // 못 찾은 배경은 키 문자열이 적힌 회색 무대다. 어느 키가 없는지 숨기지 않는다.
            BackgroundKeyText.Text = library.BackgroundsConfigured
                ? $"배경 없음: {background.SpriteKey}"
                : $"배경 {background.SpriteKey} (에셋 루트 미설정)";
        }
    }

    private void RenderPortraits(
        PreviewAssetLibrary library,
        MiniStageState state,
        MiniStagePreviewRequest request)
    {
        string? speakerCharacterId = _session?.Definition.FindSpeakerCharacterId(request.SpeakerName);
        bool speakerOnStage = false;

        foreach ((string slotKey, MiniStageSlot slot) in state.VisibleSlots)
        {
            bool isSpeaker = speakerCharacterId is not null &&
                string.Equals(slot.CharacterId, speakerCharacterId, StringComparison.Ordinal);
            speakerOnStage |= isSpeaker;

            PortraitRow.Children.Add(BuildPortraitTile(library, slotKey, slot, isSpeaker, request.SpeakerName));
        }

        if (PortraitRow.Children.Count == 0 && request.HasPresentation)
        {
            StageEmptyText.Text = "무대 위에 표시된 캐릭터가 없습니다.";
            StageEmptyText.IsVisible = true;
        }

        if (!request.HasPresentation)
        {
            StageEmptyText.Text = "연출 공급 없음 — 화자만 표시합니다.";
            StageEmptyText.IsVisible = true;
        }

        // 화자가 무대에 없으면(매핑이 없거나 아직 등장 전) 이름만 상단에 보여 준다.
        if (!string.IsNullOrWhiteSpace(request.SpeakerName) && !speakerOnStage)
        {
            OffStageSpeakerText.Text = $"화자: {request.SpeakerName}";
            OffStageSpeakerText.IsVisible = true;
        }
    }

    private Control BuildPortraitTile(
        PreviewAssetLibrary library,
        string slotKey,
        MiniStageSlot slot,
        bool isSpeaker,
        string? speakerName)
    {
        Control image;
        string caption;

        if (slot.CharacterId is null)
        {
            image = PlaceholderPortrait(slotKey);
            caption = $"{slotKey} (캐스팅 없음)";
        }
        else
        {
            PortraitResolution portrait = library.ResolvePortrait(
                slot.CharacterId,
                slot.VariantKey,
                slot.EmotionKey);

            if (portrait.FilePath is { } path && _session?.ImageCache.Get(path) is { } bitmap)
            {
                image = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    Width = 84,
                    Height = 126
                };

                caption = portrait.Kind == AssetResolutionKind.Fallback
                    ? $"{slotKey} · {portrait.RequestedKey} → 기본"
                    : $"{slotKey} · {portrait.ResolvedKey}";
            }
            else
            {
                // 누락 초상화는 이니셜 실루엣 — 어느 키가 없는지 캡션에 남는다.
                image = PlaceholderPortrait(slot.CharacterId);
                caption = $"{slotKey} · 없음: {portrait.RequestedKey}";
            }
        }

        if (slot.Mirrored)
        {
            image.RenderTransformOrigin = RelativePoint.Center;
            image.RenderTransform = new ScaleTransform(-1, 1);
        }

        var tile = new StackPanel { Spacing = 2 };
        tile.Children.Add(image);
        tile.Children.Add(new TextBlock
        {
            Text = caption,
            FontSize = 9,
            Foreground = Brushes.White,
            Opacity = 0.85,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 96,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        if (isSpeaker)
        {
            tile.Children.Add(new Border
            {
                Background = SpeakerHighlight,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new TextBlock
                {
                    Text = speakerName,
                    FontSize = 10,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.Black
                }
            });
        }

        return new Border
        {
            BorderThickness = new Thickness(2),
            BorderBrush = isSpeaker ? SpeakerHighlight : Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(3),
            Child = tile
        };
    }

    private static Control PlaceholderPortrait(string label)
    {
        return new Border
        {
            Width = 84,
            Height = 126,
            Background = PlaceholderTile,
            CornerRadius = new CornerRadius(4),
            Child = new TextBlock
            {
                Text = label.Length == 0 ? "?" : label[..1].ToUpperInvariant(),
                FontSize = 34,
                FontWeight = FontWeight.Bold,
                Opacity = 0.6,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private void RenderBadges(MiniStageState state, MiniStagePreviewRequest request)
    {
        if (!request.HasPresentation)
        {
            return;
        }

        int total = state.Unhandled.Count;
        int forLine = state.UnhandledCountFor(request.SelectedLineId);

        if (total > 0)
        {
            // 2b 확장의 백로그가 되는 목록이다. 조용히 버려진 연출은 없다.
            var badge = new Button
            {
                Content = $"반영 안 된 연출 {total}" + (forLine > 0 ? $" (이 라인 {forLine})" : string.Empty),
                FontSize = 10,
                Padding = new Thickness(6, 2),
                Background = new SolidColorBrush(Color.FromArgb(40, 220, 38, 38))
            };

            badge.Click += (_, _) =>
            {
                _unhandledExpanded = !_unhandledExpanded;
                UnhandledHost.IsVisible = _unhandledExpanded;
            };

            BadgeRow.Children.Add(badge);

            foreach (MiniStageUnhandled unhandled in state.Unhandled)
            {
                UnhandledHost.Children.Add(new TextBlock
                {
                    Text = $"• {unhandled.CommandName} — {(unhandled.LineId is null ? "Setup" : unhandled.LineId)}",
                    FontSize = 10,
                    Opacity = 0.75
                });
            }

            UnhandledHost.IsVisible = _unhandledExpanded;
        }

        if (state.PassedBranchApproximation)
        {
            BadgeRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, 217, 119, 6)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2),
                Child = new TextBlock
                {
                    Text = "지나온 구간에 갈래 있음 — 문서 순서 근사",
                    FontSize = 10
                }
            });
        }
    }

    private void RenderNotices(PreviewAssetLibrary library, MiniStagePreviewRequest request)
    {
        if (request.Notice is { } notice)
        {
            AddNotice(notice, warning: true);
        }

        if (!library.BackgroundsConfigured || !library.PortraitsConfigured)
        {
            AddNotice("에셋 루트가 없어 플레이스홀더로 표시합니다. 위의 폴더 버튼으로 설정하세요.", warning: false);
        }

        foreach (string problem in library.Problems)
        {
            AddNotice(problem, warning: true);
        }
    }

    private void AddNotice(string message, bool warning)
    {
        NoticeHost.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Opacity = warning ? 0.9 : 0.6,
            Foreground = warning ? new SolidColorBrush(Color.FromRgb(194, 65, 12)) : null
        });
    }

    private async Task PickAssetRoot(bool backgrounds)
    {
        if (_session is not null && await AssetRootPicker.PickAsync(this, _session, backgrounds))
        {
            Render();
        }
    }
}
