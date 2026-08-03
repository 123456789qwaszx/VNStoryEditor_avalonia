using Avalonia.Controls;
using Vn.App.Services;
using Vn.Authoring.Assets;
using Vn.Authoring.Flow;

namespace Vn.App.Views;

/// <summary>공유 무대 프리뷰에 밀어 넣는 요청 하나. 폴드는 호출자가 이미 끝냈다.</summary>
/// <param name="HasPresentation">false면 연출 공급이 없는 것 — 오류가 아니라 화자만 표시한다.</param>
/// <param name="LineIndex">문서에서 선택 라인의 0기준 위치. 없으면 -1 — 창 하단 표시용.</param>
/// <param name="Notice">선택 라인이 발행본에 없다는 등 호출자가 덧붙이는 알림.</param>
internal sealed record MiniStagePreviewRequest(
    string ContextLabel,
    MiniStageState State,
    bool HasPresentation,
    string? SelectedLineId,
    string? SpeakerName,
    string? LineText,
    string? Notice = null,
    int LineIndex = -1,
    int LineCount = 0);

/// <summary>
/// 편집기 하단의 축소판 무대 프리뷰. 무대 그리기는 <see cref="StageSceneView"/>가
/// (분리 창과 같은 코드로) 하고, 이 패널은 뱃지·알림·에셋 설정 버튼과
/// 분리 창(<see cref="StagePreviewWindow"/>)의 수명을 맡는다.
/// </summary>
public partial class MiniStagePreview : UserControl
{
    private AuthoringSession? _session;
    private MiniStagePreviewRequest? _current;
    private readonly StageSceneView _scene = new();
    private StagePreviewWindow? _window;

    /// <summary>분리 창의 이전/다음 버튼. delta(-1/+1)를 활성 편집기가 소화한다.</summary>
    internal event Action<int>? LineMoveRequested;

    public MiniStagePreview()
    {
        InitializeComponent();

        SceneHost.Content = _scene;

        RefreshButton.Click += (_, _) =>
        {
            _session?.RefreshAssets();
            Render();
        };
        OpenWindowButton.Click += (_, _) => OpenWindow();
        BackgroundsRootButton.Click += async (_, _) => await PickAssetRoot(backgrounds: true);
        PortraitsRootButton.Click += async (_, _) => await PickAssetRoot(backgrounds: false);
    }

    internal void Attach(AuthoringSession session)
    {
        _session = session;
        _scene.Attach(session);
    }

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
        _scene.Render(request);

        if (request is null)
        {
            BadgeRow.Children.Clear();
            UnhandledHost.Children.Clear();
            UnhandledHost.IsVisible = false;
            NoticeHost.Children.Clear();
        }
        else
        {
            StageIndicators.FillBadges(request, BadgeRow, UnhandledHost);
            StageIndicators.FillNotices(library, request, NoticeHost, includeRootHint: true);
        }

        _window?.Push(request);
    }

    private void OpenWindow()
    {
        if (_session is null)
        {
            return;
        }

        if (_window is null)
        {
            _window = new StagePreviewWindow();
            _window.Attach(_session);
            _window.MoveRequested += delta => LineMoveRequested?.Invoke(delta);
            _window.Closed += (_, _) => _window = null;

            if (TopLevel.GetTopLevel(this) is Window owner)
            {
                _window.Show(owner);
            }
            else
            {
                _window.Show();
            }
        }
        else
        {
            _window.Activate();
        }

        _window?.Push(_current);
    }

    private async Task PickAssetRoot(bool backgrounds)
    {
        if (_session is not null && await AssetRootPicker.PickAsync(this, _session, backgrounds))
        {
            Render();
        }
    }
}
