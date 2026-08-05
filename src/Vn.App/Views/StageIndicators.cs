using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Vn.App.Services;
using Vn.Authoring.Assets;
using Vn.Authoring.Flow;

namespace Vn.App.Views;

/// <summary>
/// "반영 안 된 연출 N" 뱃지·갈래 근사 표시·알림 목록의 단일 구현.
/// 도킹 패널과 분리 프리뷰 창이 같은 표시 규칙을 쓴다 — 여기 없는 곳에서
/// 뱃지를 따로 만들면 두 화면이 다른 이야기를 하게 된다.
/// </summary>
internal static class StageIndicators
{
    public static void FillBadges(
        MiniStagePreviewRequest request,
        Panel badgeRow,
        Panel unhandledHost)
    {
        badgeRow.Children.Clear();

        bool wasExpanded = unhandledHost.IsVisible;
        unhandledHost.Children.Clear();
        unhandledHost.IsVisible = false;

        if (!request.HasPresentation)
        {
            return;
        }

        MiniStageState state = request.State;

        // H-3 두 갈래 (W26): "반영 안 됨" = 코어도 툴도 못 접은 커맨드,
        // "미표시" = 상태로는 접혔지만 아직 그리지 않는 축(구조 기록·셰이더 축).
        MiniStageUnhandled[] notFolded = state.Unhandled.Where(item => !item.FoldedButNotDrawn).ToArray();
        MiniStageUnhandled[] notDrawn = state.Unhandled.Where(item => item.FoldedButNotDrawn).ToArray();
        bool hasDetail = state.Unhandled.Count > 0 ||
            (request.CoreState?.Unhandled.Count ?? 0) > 0;

        if (notFolded.Length > 0)
        {
            int forLine = notFolded.Count(item =>
                string.Equals(item.LineId, request.SelectedLineId, StringComparison.Ordinal));

            // 확장의 백로그가 되는 목록이다. 조용히 버려진 연출은 없다.
            AddToggleBadge(
                badgeRow,
                unhandledHost,
                $"반영 안 된 연출 {notFolded.Length}" + (forLine > 0 ? $" (이 라인 {forLine})" : string.Empty),
                Color.FromArgb(40, 220, 38, 38));
        }

        if (notDrawn.Length > 0)
        {
            AddToggleBadge(
                badgeRow,
                unhandledHost,
                $"미표시 {notDrawn.Length}",
                Color.FromArgb(40, 120, 120, 128));
        }

        if (hasDetail)
        {
            foreach (MiniStageUnhandled unhandled in state.Unhandled)
            {
                unhandledHost.Children.Add(new TextBlock
                {
                    Text = $"• {unhandled.CommandName} — " +
                        $"{(unhandled.LineId is null ? "Setup" : unhandled.LineId)}" +
                        (unhandled.FoldedButNotDrawn ? " (접힘·미표시)" : string.Empty),
                    FontSize = 10,
                    Opacity = 0.75
                });
            }

            // 코어가 남긴 진단(초상 치수 없음 등) — 접다 만 이유가 사람에게 보여야 한다(규칙 14).
            if (request.CoreState is { Unhandled.Count: > 0 } core)
            {
                foreach (Ked.Presentation.Core.UnhandledCommand diagnostic in core.Unhandled)
                {
                    unhandledHost.Children.Add(new TextBlock
                    {
                        Text = $"◦ 코어 진단: {diagnostic.Command.Name} — {diagnostic.Reason}" +
                            (diagnostic.Command.Source is { } source ? $" ({source})" : string.Empty),
                        FontSize = 10,
                        Opacity = 0.6,
                        TextWrapping = TextWrapping.Wrap
                    });
                }
            }

            unhandledHost.IsVisible = wasExpanded;
        }

        // tuning 미수입이면 배치가 균등 나열 근사다 — 근사를 정확한 척하지 않는다 (W25, 규칙 14).
        if (request.CoreState is null)
        {
            badgeRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, 217, 119, 6)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2),
                Child = new TextBlock
                {
                    Text = "좌표 근사 (tuning 없음)",
                    FontSize = 10
                }
            });
        }

        if (state.PassedBranchApproximation)
        {
            badgeRow.Children.Add(new Border
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

    public static void FillNotices(
        PreviewAssetLibrary library,
        RuntimeTuningLibrary tuning,
        MiniStagePreviewRequest? request,
        Panel noticeHost,
        bool includeRootHint)
    {
        noticeHost.Children.Clear();

        if (request?.Notice is { } notice)
        {
            AddNotice(noticeHost, notice, warning: true);
        }

        if (includeRootHint && (!library.BackgroundsConfigured || !library.PortraitsConfigured))
        {
            AddNotice(
                noticeHost,
                "에셋 루트가 없어 플레이스홀더로 표시합니다. 좌측 에셋 탐색기의 [폴더…]에서 설정하세요.",
                warning: false);
        }

        // 런타임 tuning 미수입은 오류가 아니라 안내다 — 어디에 무엇을 놓으면 되는지 말한다(W23).
        if (includeRootHint && !tuning.IsLoaded && tuning.Problems.Count == 0)
        {
            AddNotice(
                noticeHost,
                $"런타임 tuning이 없어 좌표 배치는 근사로 표시됩니다. 런타임에서 내보낸 " +
                $"{RuntimeTuningLibrary.DefaultFolderName} 폴더를 프로젝트 폴더에 복사하면 됩니다.",
                warning: false);
        }

        foreach (string problem in library.Problems)
        {
            AddNotice(noticeHost, problem, warning: true);
        }

        foreach (string problem in tuning.Problems)
        {
            AddNotice(noticeHost, problem, warning: true);
        }
    }

    private static void AddToggleBadge(Panel badgeRow, Panel unhandledHost, string text, Color background)
    {
        var badge = new Button
        {
            Content = text,
            FontSize = 10,
            Padding = new Thickness(6, 2),
            Background = new SolidColorBrush(background)
        };

        badge.Click += (_, _) => unhandledHost.IsVisible = !unhandledHost.IsVisible;
        badgeRow.Children.Add(badge);
    }

    private static void AddNotice(Panel host, string message, bool warning)
    {
        host.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Opacity = warning ? 0.9 : 0.6,
            Foreground = warning ? new SolidColorBrush(Color.FromRgb(194, 65, 12)) : null
        });
    }
}
