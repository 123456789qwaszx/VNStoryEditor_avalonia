using Avalonia.Controls;
using Vn.App.Services;
using Vn.Authoring.Assets;

namespace Vn.App.Views;

/// <summary>
/// 분리된 무대 프리뷰 창 — "완성된 비주얼노벨이라 가정하고 보는 뷰".
///
/// 무대는 도킹 패널과 같은 <see cref="StageSceneView"/>가 그린다(기준 해상도 좌표계 +
/// Viewbox Uniform이라 창을 어떻게 늘려도 레터박스로 비율이 유지된다).
///
/// 따라가기(기본 켬)면 메인 창의 라인 선택을 그대로 비추고, 이전/다음 버튼은
/// 메인 편집기의 선택을 움직인다(선택은 하나뿐 — 창 전용 커서를 만들지 않는다).
/// 따라가기를 끄면 지금 장면이 고정되고 이동 버튼도 잠긴다.
/// </summary>
public partial class StagePreviewWindow : Window
{
    private readonly StageSceneView _scene = new();
    private AuthoringSession? _session;
    private MiniStagePreviewRequest? _latest;
    private bool _rendered;

    /// <summary>이전/다음 요청. delta는 -1/+1이고 활성 편집기의 선택이 움직인다.</summary>
    internal event Action<int>? MoveRequested;

    public StagePreviewWindow()
    {
        InitializeComponent();

        SceneHost.Content = _scene;

        PrevButton.Click += (_, _) => MoveRequested?.Invoke(-1);
        NextButton.Click += (_, _) => MoveRequested?.Invoke(1);
        FollowToggle.IsCheckedChanged += (_, _) =>
        {
            bool follow = FollowToggle.IsChecked == true;
            PrevButton.IsEnabled = follow;
            NextButton.IsEnabled = follow;

            if (follow)
            {
                Render(_latest); // 끄고 있는 동안 밀린 최신 장면을 따라잡는다
            }
        };
    }

    internal void Attach(AuthoringSession session)
    {
        _session = session;
        _scene.Attach(session);
    }

    /// <summary>메인 쪽에서 미는 최신 요청. 따라가기가 꺼져 있으면 장면은 고정된다.</summary>
    internal void Push(MiniStagePreviewRequest? request)
    {
        _latest = request;

        if (FollowToggle.IsChecked == true || !_rendered)
        {
            Render(request);
        }
    }

    private void Render(MiniStagePreviewRequest? request)
    {
        _rendered = true;
        _scene.Render(request);

        if (request is null)
        {
            PositionText.Text = "라인을 선택하면 무대가 표시됩니다.";
            BadgeRow.Children.Clear();
            NoticeHost.Children.Clear();
            UnhandledHost.Children.Clear();
            UnhandledHost.IsVisible = false;
            return;
        }

        string position = request.ContextLabel;

        if (request.LineIndex >= 0 && request.LineCount > 0)
        {
            position += $" · {request.LineIndex + 1}/{request.LineCount}";
        }

        if (request.SelectedLineId is { } lineId)
        {
            position += $" · {lineId}";
        }

        PositionText.Text = position;

        StageIndicators.FillBadges(request, BadgeRow, UnhandledHost);
        StageIndicators.FillNotices(
            _session?.AssetLibrary ?? PreviewAssetLibrary.Empty,
            request,
            NoticeHost,
            includeRootHint: false);
    }
}
