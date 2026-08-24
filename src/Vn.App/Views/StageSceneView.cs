using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Ked.Presentation.Core;
using Vn.App.Services;
using Vn.Authoring.Assets;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

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

    /// <summary>조절창의 전역 선택 슬롯. 팝오버를 다시 열어도 유지된다 — "지금 무엇을 조작 중인가".</summary>
    private string? _popoverSlotKey;

    /// <summary>조절창 탭 선택. 적용 후 재구성돼도 보던 탭이 유지된다.</summary>
    private int _popoverTabIndex;

    // 조작 콘솔 팝업 (W37) — 위치 고정 + 드래그 이동. 뷰 좌상단 기준 오프셋으로 자리를 잡는다.
    private readonly Popup _consolePopup;
    private Point _consoleOpenAt;
    private Point? _consoleUserPosition;

    /// <summary>
    /// 열려 있는 조절창의 재구성 손잡이 (2026-08-22). 바깥(터미널에서 칩을 담는 길)이
    /// <b>팝업을 닫지 않고</b> 내용만 새로 그리게 한다 — 소유자: "추가되는게 바로바로 보이도록".
    /// </summary>
    private Action? _consoleRebuild;

    /// <summary>담기 모드가 켜지거나 꺼졌다 — 터미널이 ★ 표시를 켜고 꺼야 한다.</summary>
    internal event Action? QuickEditModeChanged;

    /// <summary>
    /// 지금 터미널이 담기 상태여야 하는가 — <b>두 편집 중 하나라도 켜져 있으면</b>이다.
    /// 담을 곳이 어디인지는 <see cref="QuickPinTarget"/>가 따로 답한다.
    /// </summary>
    internal bool IsQuickPinMode => _commandEditMode || _bundleEditMode;

    /// <summary>조절창을 그 자리에서 다시 그린다.</summary>
    internal void RefreshConsole() => _consoleRebuild?.Invoke();

    /// <summary>
    /// 무대 조절창을 <b>붙박이 판</b>으로 짓는다 (2026-08-22 소유자: "화면을 클릭했을 때
    /// 나오는 콘솔이 연출 프리뷰 오른쪽에 상시 표시되면 좋겠어 — 챕터그래프와
    /// 연출그래프에서처럼").
    ///
    /// 내용은 팝업 시절과 <b>같은 함수</b>(<see cref="BuildStagePopover"/>)가 짓는다 —
    /// 담는 그릇만 바뀌었다. 딸려 죽은 것: 끌기 손잡이·✕·우클릭 닫기·라이트 디스미스.
    /// 닫히지 않는 판에는 닫는 손짓이 없다.
    /// </summary>
    internal Control BuildDockedConsole()
    {
        var content = new StackPanel { Spacing = 6, Margin = new Thickness(10, 8) };

        void Rebuild()
        {
            content.Children.Clear();
            UiGuard.Run(_session, "조절창 갱신", () =>
            {
                if (_session is null || _request is null)
                {
                    content.Children.Add(new TextBlock
                    {
                        Text = "씬을 고르면 여기서 무대를 조작합니다.",
                        FontSize = 11,
                        Opacity = 0.55,
                        TextWrapping = TextWrapping.Wrap
                    });
                    return;
                }

                // 잠긴 화면(공급된 발행본)에서는 누를 것이 없다 — 이유를 적고 만다.
                // 예전에는 팝업이 아예 안 열려 이 상태를 말할 자리가 없었다.
                if (!CanManipulate())
                {
                    content.Children.Add(new TextBlock
                    {
                        Text = _request.EditContext?.DisabledReason
                            ?? "라인을 고르면 무대를 조작할 수 있습니다.",
                        FontSize = 11,
                        Opacity = 0.6,
                        TextWrapping = TextWrapping.Wrap
                    });
                    return;
                }

                BuildStagePopover(content, Rebuild);
            });
        }

        _consoleRebuild = Rebuild;
        Rebuild();

        return new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
    }

    /// <summary>
    /// 담기 모드를 켜고 끄는 유일한 통로 — <b>바뀔 때만</b> 신호를 쏜다. 조절창을 열
    /// 때마다 무조건 쏘면 터미널이 매번 헛되이 다시 그려지고, 그 재렌더가 조절창을 여는
    /// 도중에 끼어든다.
    /// </summary>
    private void SetCommandEditMode(bool enabled)
    {
        if (_commandEditMode == enabled)
        {
            return;
        }

        _commandEditMode = enabled;
        _commandExpandedIndex = null;
        QuickEditModeChanged?.Invoke();
    }

    /// <inheritdoc cref="SetCommandEditMode"/>
    private void SetBundleEditMode(bool enabled)
    {
        if (_bundleEditMode == enabled)
        {
            return;
        }

        _bundleEditMode = enabled;
        _bundlePinIndex = null;
        _bundleExpandedStep = null;
        QuickEditModeChanged?.Invoke();
    }

    /// <summary>직접 조작이 편집을 만들었다. 편집기 카드가 새 커맨드 행을 그려야 한다.</summary>
    internal event Action? ManipulationApplied;

    /// <summary>
    /// 재생 진행 입력 (W31) — 재생 중이면 true를 돌려주고 클릭을 소비한다(타자 중이면 전문
    /// 완성, 그 뒤면 다음 라인). false면 클릭은 기존 조작(조절창·캐릭터 팝오버)으로 흐른다.
    /// 도킹·분리 창의 무대가 같은 재생 모델의 이 판정 하나를 본다.
    /// </summary>
    internal Func<bool>? PlaybackAdvance;

    /// <summary>갈래 선택이 바뀌었다 (W35) — 편집이 아니지만 다시 접어야 하므로 편집기가 재렌더한다.</summary>
    internal event Action? BranchSelectionChanged;

    // 타자기 (W32) — 대사창 텍스트만 갱신하기 위한 핸들. 캔버스 재렌더 시 다시 잡힌다.
    private TextBlock? _dialogueText;
    private string? _dialogueFullText;

    // 전이 (W33) — 직전 렌더의 자리들과 이번 렌더의 보간 대상. 코어가 낸 두 정지 프레임
    // 사이를 뷰가 선형 보간으로 잇는다 — 좌표 계산이 아니라 자리 옮기기다(H-5).
    private Dictionary<string, StageRect> _portraitRects = new(StringComparer.Ordinal);
    private Dictionary<string, StageRect> _previousPortraitRects = new(StringComparer.Ordinal);

    /// <summary>
    /// 슬롯이 <b>보이는 상태였는지</b> — 자리(<see cref="_portraitRects"/>)와 따로 기억한다
    /// (2026-08-24 소유자 보고: "fade_in이 종종 안 먹는다").
    ///
    /// ⛔ <b>자리로 등장을 판정하면 안 된다.</b> 숨김 슬롯도 고스트 윤곽으로 자리를
    /// 등록하므로(W28), 무대에 이미 서 있던 슬롯의 <c>fade_in</c>이 "자리가 있었으니
    /// 이동"으로 읽혀 불투명도 1로 튀어 올랐다 — 페이드가 도는 것은 슬롯이 그 라인에서
    /// <b>처음 생길 때</b>뿐이었고, 그래서 "종종" 먹었다. 등장은 <b>가시성이 바뀐 것</b>이다.
    /// </summary>
    private Dictionary<string, bool> _portraitVisible = new(StringComparer.Ordinal);
    private Dictionary<string, bool> _previousPortraitVisible = new(StringComparer.Ordinal);

    /// <summary>
    /// 직전 렌더의 <b>보이는 초상 컨트롤</b> — 퇴장 크로스페이드가 다시 얹을 그림이다
    /// (2026-08-24 소유자: "퇴장도 같은 결로").
    ///
    /// <c>fade_out</c>이 든 라인에서 이 슬롯은 고스트 윤곽으로 다시 그려지므로, 나가는
    /// 초상은 이미 캔버스에서 내려가 있다. 페이드하려면 <b>다시 얹어야</b> 한다 — 배경
    /// 크로스페이드(<see cref="_backgroundOverlay"/>)와 같은 결이다. 그림을 비트맵에서
    /// 새로 짜지 않고 그 컨트롤을 그대로 재활용한다: 좌우 반전·화자 테두리·"없음" 자리표시가
    /// 전부 따라오고, 같은 모양을 짓는 코드가 두 벌이 되지 않는다.
    /// </summary>
    private Dictionary<string, Control> _portraitControls = new(StringComparer.Ordinal);
    private Dictionary<string, Control> _previousPortraitControls = new(StringComparer.Ordinal);

    /// <summary>지금 걷히는 중인 초상들 — 진행도가 확정되면 캔버스에서 내린다.</summary>
    private readonly Dictionary<string, Control> _departingPortraits = new(StringComparer.Ordinal);

    /// <summary>
    /// 지금 무대에 선 <b>프레임의 정체</b> — 어느 노드의 어느 라인인가. 전이 기준선
    /// (직전 자리·가시성·초상 컨트롤·배경)을 미룰지 밀지가 이 값 하나로 갈린다.
    ///
    /// ⛔ <b>렌더 한 번이 전이 한 번이 아니다</b> (2026-08-24 소유자 2차 보고: "커맨드를
    /// 추가했을 때 재생을 시켜도 Fade가 안 먹고, 다른 데 나갔다 오면 된다"). 같은 라인을
    /// 다시 그리는 일은 늘 있다 — 커맨드 추가·커맨드 선택·[＋ 연출 추가] 플라이아웃·스탯
    /// HUD 토글·에셋 루트 변경이 전부 <see cref="Render"/>를 부른다. 그때마다 기준선을
    /// 밀면 <b>직전 라인이 지워지고 이번 라인이 그 자리에 앉아</b> 등장·퇴장·배경
    /// 크로스페이드의 근거가 통째로 사라진다(직전 = 이번이면 바뀐 것이 없다).
    /// "다른 데 나갔다 오면 된다"가 그 증거다 — 다른 라인이 기준선을 제대로 채워 줬을 뿐이다.
    ///
    /// 그래서 <b>기준선의 단위는 렌더가 아니라 프레임</b>이다. 같은 프레임을 몇 번을 다시
    /// 그리든 출발점은 직전 라인 하나로 남는다.
    /// </summary>
    private (string? NodeId, string? ContextLabel, string? LineId)? _renderedFrame;

    private readonly List<(string SlotKey, Control Control, StageRect To)> _transitionEntries = new();
    private string? _backgroundImagePath;
    private string? _previousBackgroundImagePath;
    private Image? _backgroundImage;
    private Image? _backgroundOverlay;

    /// <summary>
    /// 전이 진행도 적용 (W33). null/1 이상 = 확정 상태(모든 자리 최종값·오버레이 제거).
    /// 0..1 = 직전 자리 → 새 자리 선형 보간, <b>등장한 슬롯은 페이드 인, 퇴장한 슬롯은
    /// 나가는 초상이 페이드 아웃</b>(2026-08-24), 배경이 바뀌었으면 옛 배경을 위에 얹어
    /// 크로스페이드.
    ///
    /// ⚠ 등장·퇴장의 근거는 언제나 <b>가시성 변화</b>다(<see cref="Appeared"/>·
    /// <see cref="Departed"/>) — "직전에 자리가 있었나"가 아니다. 숨김 슬롯도 고스트로
    /// 자리를 등록하므로 그 판정은 무대에 이미 선 슬롯의 fade를 통째로 삼킨다.
    /// </summary>
    internal void SetTransitionProgress(double? progress)
    {
        double lineSeconds = _request?.TransitionSeconds ?? 0;

        SyncTimelineHandle(progress);

        if (progress is not { } t || t >= 1)
        {
            // null = 정지 화면 (W66 소유자 결정): 시간을 가진 커맨드의 슬롯은 이 라인이
            // 시작되는 순간의 자리·크기 — 곧 출발이다. 어디로 가는지는 타임라인을 끌거나
            // ▶로 태워서 본다(도착을 겹쳐 그리던 궤적·고스트는 2026-08-21에 걷혔다).
            // 1 = 재생이 끝까지 태웠다 — 도착에 남는다.
            IReadOnlyDictionary<string, StageRect>? rest = progress is null ? _motionStartRects : null;

            foreach ((string slotKey, Control control, StageRect to) in _transitionEntries)
            {
                ApplyRect(
                    control,
                    rest is not null && rest.TryGetValue(slotKey, out StageRect? start) ? start : to);
                control.Opacity = 1;
            }

            // 걷히던 초상도 함께 내린다 — 확정 프레임에 반투명한 잔상이 남으면 그것이 버그다.
            ClearDepartingPortraits();

            if (_backgroundOverlay is not null)
            {
                _canvas.Children.Remove(_backgroundOverlay);
                _backgroundOverlay = null;
            }

            if (_backgroundImage is not null)
            {
                _backgroundImage.Opacity = 1;
            }

            return;
        }

        // 시간을 가진 커맨드가 있는 라인은 그 시각의 무대를 다시 합성한다 (2026-08-21) —
        // 커맨드마다 제 duration·이징으로 흐르고(라인 최대가 아니다), 자리도 크기도
        // 코어가 낸 상태에서 나온다. 모양은 코어 EaseFunctions/CurveFunctions —
        // 런타임과 등가 고정된 그 곡선이다(W66b).
        IReadOnlyDictionary<string, StageRect>? frame = ComposeMotionRects(t * lineSeconds);

        foreach ((string slotKey, Control control, StageRect to) in _transitionEntries)
        {
            bool hadRect = _previousPortraitRects.TryGetValue(slotKey, out StageRect? from) && from is not null;

            // 페이드 인의 근거는 <b>가시성이 켜진 것</b>이다 (2026-08-24) — 자리가 없었다는
            // 것만으로 판정하면, 숨김 고스트로 이미 자리를 갖고 있던 슬롯의 fade_in이
            // 불투명도 1로 튀어 오른다(소유자 보고: "fade_in이 종종 안 먹는다"). 무대에
            // 처음 서는 슬롯은 자리도 가시성 기록도 없으니 어느 쪽으로도 페이드 인이다.
            control.Opacity = !hadRect || Appeared(slotKey) ? t : 1;

            if (frame is not null &&
                _motionPlan?.AnimatedSlots.Contains(slotKey) == true &&
                frame.TryGetValue(slotKey, out StageRect? now))
            {
                ApplyRect(control, now);
            }
            else if (hadRect)
            {
                Canvas.SetLeft(control, Lerp(from!.X, to.X, t));
                Canvas.SetTop(control, Lerp(from.Y, to.Y, t));
                control.Width = Lerp(from.Width, to.Width, t);
                control.Height = Lerp(from.Height, to.Height, t);
            }

            // 퇴장 — 나가는 초상을 고스트 위에 얹고 서서히 걷는다. 자리 계산이 끝난 뒤라
            // <b>고스트의 지금 자리를 그대로 베낀다</b>: 같은 산식을 두 번 쓰면 place가
            // 함께 걸린 라인에서 둘이 갈려 초상이 뒤에 남는다.
            if (Departed(slotKey) && TakeDepartingPortrait(slotKey, control) is { } leaving)
            {
                Canvas.SetLeft(leaving, Canvas.GetLeft(control));
                Canvas.SetTop(leaving, Canvas.GetTop(control));
                leaving.Width = control.Width;
                leaving.Height = control.Height;
                leaving.Opacity = 1 - t;
            }
        }

        if (_backgroundImage is not null &&
            !string.Equals(_previousBackgroundImagePath, _backgroundImagePath, StringComparison.Ordinal))
        {
            if (_previousBackgroundImagePath is { } previousPath)
            {
                // 배경 교체 — 옛 배경을 새 배경 위에 얹고 서서히 걷는다.
                if (_backgroundOverlay is null && _session?.ImageCache.Get(previousPath) is { } previousBitmap)
                {
                    _backgroundOverlay = new Image
                    {
                        Source = previousBitmap,
                        Stretch = Stretch.UniformToFill,
                        Width = _canvas.Width,
                        Height = _canvas.Height,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(_backgroundOverlay, 0);
                    Canvas.SetTop(_backgroundOverlay, 0);

                    int backgroundIndex = _canvas.Children.IndexOf(_backgroundImage);
                    _canvas.Children.Insert(Math.Max(backgroundIndex + 1, 0), _backgroundOverlay);
                }

                if (_backgroundOverlay is not null)
                {
                    _backgroundOverlay.Opacity = 1 - t;
                }
            }
            else
            {
                _backgroundImage.Opacity = t; // 첫 배경 — 페이드 인
            }
        }
    }

    /// <summary>
    /// 전이 대상 하나를 등록한다 — 자리와 <b>가시성</b>을 함께. 두 렌더 경로(보이는 초상 ·
    /// 숨김 고스트)가 같은 자리를 지나야 둘이 어긋나지 않는다.
    /// </summary>
    private void RegisterTransition(string slotKey, Control control, StageRect rect, bool visible)
    {
        _portraitRects[slotKey] = rect;
        _portraitVisible[slotKey] = visible;
        _transitionEntries.Add((slotKey, control, rect));

        // 퇴장 크로스페이드가 다시 얹을 그림 — 보이는 초상만이다(고스트 윤곽은 얹을 것이 아니다).
        if (visible)
        {
            _portraitControls[slotKey] = control;
        }
    }

    /// <summary>
    /// 이 슬롯이 <b>이번 라인에서 등장했는가</b> — 안 보이던(또는 무대에 없던) 것이 보이게
    /// 됐는가. 이것이 페이드 인의 유일한 근거다(자리가 있었는지는 근거가 아니다).
    /// </summary>
    private bool Appeared(string slotKey) => Visible(_portraitVisible, slotKey) &&
        !Visible(_previousPortraitVisible, slotKey);

    /// <summary>
    /// 이 슬롯이 <b>이번 라인에서 퇴장했는가</b> — 보이던 것이 안 보이게 됐는가.
    /// <see cref="Appeared"/>의 거울이고, 판정 근거도 같다(가시성 변화 하나).
    /// </summary>
    private bool Departed(string slotKey) => !Visible(_portraitVisible, slotKey) &&
        Visible(_previousPortraitVisible, slotKey);

    private static bool Visible(Dictionary<string, bool> record, string slotKey) =>
        record.TryGetValue(slotKey, out bool visible) && visible;

    /// <summary>
    /// 걷히는 중인 초상을 준비한다 — 직전 렌더의 컨트롤을 <b>고스트 바로 위</b>에 다시
    /// 얹는다(한 번만). 얹을 그림이 없으면 null이고, 그때 퇴장은 그냥 사라짐이다.
    ///
    /// ⚠ <b>손짓을 뗀다</b>(<c>IsHitTestVisible</c>) — 걷히는 중인 그림을 눌러 조절창이
    /// 열리면, 사람은 이미 화면에 없는 것을 만지고 있는 셈이다.
    /// </summary>
    private Control? TakeDepartingPortrait(string slotKey, Control ghost)
    {
        if (_departingPortraits.TryGetValue(slotKey, out Control? existing))
        {
            return existing;
        }

        if (!_previousPortraitControls.TryGetValue(slotKey, out Control? leaving))
        {
            return null;
        }

        leaving.IsHitTestVisible = false;

        int index = _canvas.Children.IndexOf(ghost);
        _canvas.Children.Insert(index < 0 ? _canvas.Children.Count : index + 1, leaving);
        _departingPortraits[slotKey] = leaving;

        return leaving;
    }

    private void ClearDepartingPortraits()
    {
        foreach (Control leaving in _departingPortraits.Values)
        {
            _canvas.Children.Remove(leaving);
        }

        _departingPortraits.Clear();
    }

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;

    /// <summary>자리와 크기를 한 번에 — 뎁스처럼 둘이 함께 걸린 커맨드가 있어 나눌 수 없다.</summary>
    private static void ApplyRect(Control control, StageRect rect)
    {
        Canvas.SetLeft(control, rect.X);
        Canvas.SetTop(control, rect.Y);
        control.Width = rect.Width;
        control.Height = rect.Height;
    }

    /// <summary>
    /// 대사창에 보일 글자 수 (W32). null = 전문. 재생 타이머가 라인 전체를 다시 그리지 않고
    /// 이 한 곳으로 타자를 찍는다.
    /// </summary>
    internal void SetDialogueVisibleCharacters(int? count)
    {
        if (_dialogueText is null || _dialogueFullText is null)
        {
            return;
        }

        _dialogueText.Text = count is { } visible
            ? _dialogueFullText[..Math.Clamp(visible, 0, _dialogueFullText.Length)]
            : _dialogueFullText;
    }

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

        // 조작 콘솔 팝업 (W37) — 좌상단 고정 + 아래·오른쪽으로만 자라서 크기가 바뀌어도
        // 자리가 튀지 않는다. 화면 밖으로 밀리면 미끄러지기만 한다(뒤집힘 없음).
        _consolePopup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft,
            PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight,
            PlacementConstraintAdjustment =
                Avalonia.Controls.Primitives.PopupPositioning.PopupPositionerConstraintAdjustment.SlideX |
                Avalonia.Controls.Primitives.PopupPositioning.PopupPositionerConstraintAdjustment.SlideY,
            // ⚠ 라이트 디스미스를 껐다 (2026-08-22 소유자: "열려있던 조작 콘솔은 닫히면
            // 안되고"). 켜져 있으면 바깥 클릭 한 번이 곧 닫기라, 조절창을 띄워 놓고
            // 터미널에서 커맨드를 담는 흐름 자체가 성립하지 않는다. 닫는 길은 헤더가
            // 이미 광고하는 둘이다 — ✕ 단추와 조절창 안 우클릭.
            IsLightDismissEnabled = false
        };
        // 무대 위에 잠깐 뜨는 판(값 시뮬 등)의 팝업이다 — 조절창은 2026-08-22에 오른쪽
        // 붙박이 기둥으로 갔으므로 여기서 `_consoleRebuild`·담기 모드를 건드리지 않는다.
        _consolePopup.Closed += (_, _) => _consolePopup.Child = null;

        // 레터박스 여백은 검정 — 창 어디를 늘려도 무대 비율은 변하지 않는다.
        Content = new Panel
        {
            Children =
            {
                new Border { Background = Brushes.Black, Child = _viewbox },
                _consolePopup
            }
        };

        // 무대 빈 곳 좌클릭 → 무대 조절창(전역 슬롯 + 배경/슬롯/캐릭터 탭). 초상화 클릭은
        // 자기 핸들러가 먼저 잡는다. 조절창은 적용해도 닫히지 않는다 — 우클릭이 닫는다.
        // 클릭·드롭 경로는 전부 UiGuard 아래다 — 예외는 무동작 + 상태줄이 된다(X1, 불변식 4).
        _canvas.PointerPressed += (_, args) =>
        {
            if (args.Handled)
            {
                return;
            }

            // 무대 우클릭 = 열려 있는 조절창 닫기 (2026-08-22). 라이트 디스미스를 끄면서
            // "바깥을 누르면 닫힌다"는 손버릇이 갈 곳을 잃었다 — 우클릭만 그 자리를 잇는다
            // (좌클릭은 여전히 조절창을 여는 손짓이라 겹치면 안 된다). 무대 우클릭은
            // 지금까지 아무 뜻도 없었으므로 빼앗는 것이 없다.
            if (args.GetCurrentPoint(_canvas).Properties.IsRightButtonPressed)
            {
                if (_consolePopup.IsOpen)
                {
                    CloseConsole();
                    args.Handled = true;
                }

                return;
            }

            if (!args.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed)
            {
                return;
            }

            // 재생 중에는 클릭이 진행이다 — 조작 대신 이 라인 즉시 완료 → 다음 (W31).
            if (PlaybackAdvance?.Invoke() == true)
            {
                args.Handled = true;
                return;
            }

            // 빈 무대 좌클릭이 조절창을 열던 길은 2026-08-22에 사라졌다 — 판이 오른쪽에
            // 늘 서 있으므로 열 것이 없다. 초상 클릭만 남는다(그 슬롯의 [캐릭터] 탭으로).
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
        _dialogueText = null;
        _dialogueFullText = null;

        // 전이 (W33): 이번 렌더가 "새 자리"가 되고 직전 <b>프레임</b>의 자리가 "출발점"이 된다.
        //
        // ⛔ 출발점을 미는 것은 <b>다른 프레임으로 넘어갈 때뿐이다</b> (2026-08-24) — 같은
        //    라인을 다시 그리는 것은 전이가 아니다. 자세한 이유는 _renderedFrame 참조.
        (string?, string?, string?) frame =
            (request?.EditContext?.PresentationNodeId, request?.ContextLabel, request?.SelectedLineId);
        bool sameFrame = _renderedFrame is { } rendered && rendered == frame;
        _renderedFrame = frame;

        if (!sameFrame)
        {
            _previousPortraitRects = _portraitRects;
            _previousPortraitVisible = _portraitVisible;
            _previousPortraitControls = _portraitControls;
            _previousBackgroundImagePath = _backgroundImagePath;
        }

        _portraitRects = new Dictionary<string, StageRect>(StringComparer.Ordinal);
        _portraitVisible = new Dictionary<string, bool>(StringComparer.Ordinal);
        _portraitControls = new Dictionary<string, Control>(StringComparer.Ordinal);

        // 캔버스를 비웠으니 얹혀 있던 퇴장 초상도 함께 내려갔다 — 손잡이만 정리한다.
        _departingPortraits.Clear();
        _backgroundImagePath = null;
        _backgroundImage = null;
        _backgroundOverlay = null;
        _transitionEntries.Clear();
        _motionPlan = null;
        _motionStartRects = null;

        (double width, double height) = _session?.Definition.PreviewResolution ?? (1920, 1080);
        _canvas.Width = width;
        _canvas.Height = height;
        double em = height / 1080 * 34; // 기준 해상도 비례 글자 크기

        if (request is null)
        {
            AddCenteredText(width, height, em, "라인을 선택하면 그 시점의 무대가 표시됩니다.");
            _consoleRebuild?.Invoke(); // 판도 빈 안내로 돌아간다
            return;
        }

        PreviewAssetLibrary library = _session?.AssetLibrary ?? PreviewAssetLibrary.Empty;
        string? speakerCharacterId = _session?.Definition.FindSpeakerCharacterId(request.SpeakerName);

        RenderBackground(library, request.State, width, height, em);

        // 시간 흐름 (2026-08-21) — 라인 시작 자리를 <b>같은 컴포저</b>로 짓는다.
        // 이동·배치·뎁스가 저마다 다른 노드를 만져도 여기 한 번의 합성으로 합쳐진다.
        _composeContext = (request.State, request.SpeakerName, speakerCharacterId, width, height);
        _motionPlan = request.MotionPlan;
        _motionStartRects = ComposeMotionRects(0);

        StageSceneLayout layout = StageSceneComposer.Compose(
            request.State,
            request.SpeakerName,
            speakerCharacterId,
            width,
            height,
            request.CoreState,
            _session?.TuningLibrary.SurfaceLayouts);

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

        // 옵션 라벨은 대사가 아니다 — 대사창에 흘리는 대신 블록의 버튼 묶음을 중앙에 제시한다.
        if (request.ChoiceOptions is { Count: > 0 } choices)
        {
            RenderChoiceOptions(choices, width, height, em);
        }
        else
        {
            RenderDialogueBox(layout, request, em);
        }

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

        RenderAudioCues(request, height, em);
        ApplyMotionStartLayout();
        RenderStatsHud(request, width, em);

        // 붙박이 조절창은 무대와 함께 간다 (2026-08-22) — 슬롯 목록·표정 썸네일·수치가
        // 전부 이 라인의 것이라, 라인이 바뀌었는데 판이 옛 라인을 들고 있으면 거짓말이다.
        // 팝업 시절에는 다시 열 때 새로 지어져 이 문제가 없었다.
        _consoleRebuild?.Invoke();
    }

    /// <summary>
    /// <b>흐르는 슬롯을 출발 자리에 세운다</b> — 정지 화면 = 이 라인이 시작되는 순간이다
    /// (W66 소유자 결정). ▶가 그 길을 태우고, 무대 아래 타임라인이 중간 프레임을 짚는다.
    /// 자리는 코어가 접은 상태를 컴포저에 통과시킨 결과이고, 이동뿐 아니라 배치(place)·
    /// 뎁스(size)도 같은 규칙을 탄다 (2026-08-21).
    ///
    /// ⚠ <b>무대는 이것만 얹는다.</b> 도착 자리의 노란 고스트 윤곽·점선 궤적(2026-08-21
    /// 걷힘 — "잘 안쓰게 되네"), 커맨드 칩, 프레임 스크럽은 전부 여기 없다: 목록·편집
    /// 입구는 왼쪽 대본 패널이고 타임라인은 무대 아래 재생 줄이다. 상시로 겹쳐 그리면
    /// 정지 화면이 그만큼 시끄러워진다.
    /// </summary>
    private void ApplyMotionStartLayout()
    {
        // 재생이 돌고 있으면 곧이어 오는 전이 동기화가 실제 진행도로 덮는다
        // (MiniStagePreview.Render 끝의 SyncTransition).
        foreach ((string slotKey, Control control, _) in _transitionEntries)
        {
            if (_motionPlan?.AnimatedSlots.Contains(slotKey) == true &&
                _motionStartRects is { } rects && rects.TryGetValue(slotKey, out StageRect? rest))
            {
                ApplyRect(control, rest);
            }
        }
    }

    /// <summary>
    /// 선택 커맨드의 수치 조절 내용 (2026-08-21 소유자: "점의 세부 조절창과 연출
    /// 편집창이 합쳐지는 게 맞겠네") — 예전 플라이아웃 대신 터미널 아래 작업대
    /// (Inspector)에 얹을 컨트롤을 돌려준다. 이동 커맨드는 이동 편집기(슬라이더+이징+
    /// 곡선), 조절 가능한 파라미터가 선언된 커맨드는 파라미터 슬라이더·선택기다.
    /// 어느 것도 아니면 null — 커맨드 행(칩)만으로 충분한 커맨드다.
    /// </summary>
    internal Control? BuildInspectorContent(PresentationResultCommand command)
    {
        if (_session is null || _request?.EditContext is not { Editable: true })
        {
            return null;
        }

        var host = new StackPanel { Spacing = 6 };

        StageMotionCue? cue = _request.MotionCues?.FirstOrDefault(item =>
            string.Equals(item.CommandId, command.CommandId, StringComparison.Ordinal));

        if (cue is not null)
        {
            AddMotionEditorRows(host, cue);
        }
        else if (ManipulationCatalog.Find(command.DefinitionId) is { } definition &&
                 definition.Parameters.Any(IsAdjustableParameter))
        {
            AddParameterEditorRows(host, definition, command);
        }

        return host.Children.Count > 0 ? host : null;
    }

    /// <summary>
    /// 프레임 타임라인 (W66b → 2026-08-21 무대 아래 이사, 소유자: "재생이 프리뷰
    /// 아래쪽에 오는게 더 좋아보여. 타임라인이랑 같이") — 이 라인 배치의 내부 시간을
    /// 손으로 끈다. 기존 전이 보간(<see cref="SetTransitionProgress"/>)에 진행도를
    /// 흘릴 뿐이고, 곡선 모양도 재생과 같은 코어 이징이다. ▶ 재생이 돌면 재생 틱이
    /// 이 값을 덮는다.
    ///
    /// <b>상시로 선다</b> (2026-08-21 소유자: "어떨 때는 나오고 어떨때는 안 나오는데
    /// 상시 표기되도록") — 끌 시간이 없는 라인에서는 <b>비활성</b>으로 서고 사라지지
    /// 않는다. 있다 없다 하면 재생 줄이 라인마다 다른 얼굴이 되고, 없는 날에는 이
    /// 도구가 있다는 사실 자체가 안 보인다.
    /// </summary>
    // 타임라인 손잡이 (2026-08-22 소유자: "프레임별로 눈금을 매기고, 재생될 때 핸들이
    // 현재 재생 중인 프레임에 맞춰지도록. 핸들도 프레임 단위로 움직이도록") —
    // 재생이 슬라이더를 밀고, 슬라이더가 다시 재생을 밀지 않도록 빗장 하나를 둔다.
    private Slider? _timelineScrub;
    private TextBlock? _timelineLabel;
    private double _timelineFrames = 1;
    private bool _syncingTimeline;

    internal Control BuildTimelineScrubber()
    {
        double lineSeconds = _request?.TransitionSeconds ?? 0;
        bool scrubbable = _motionPlan is not null && lineSeconds > 0;
        double frames = Math.Max(1, Math.Round(lineSeconds * DurationToken.FramesPerSecond));

        var frameLabel = new TextBlock
        {
            Text = $"0/{frames:0}fr",
            FontSize = 11,
            Width = 52,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            // 색은 평범하게 (2026-08-21 소유자) — 노란 강조가 이 숫자를 실제보다
            // 중요해 보이게 했다. 진행 표시(1/3)와 같은 결이면 충분하다.
            Opacity = 0.75
        };

        var scrub = new Slider
        {
            Minimum = 0,
            Maximum = frames,
            Value = 0,
            // 눈금 한 칸 = 한 프레임 (2026-08-22 소유자). 키보드·페이지 이동도 같은 걸음이라
            // 어떤 손짓으로 움직여도 프레임 사이에 서는 일이 없다.
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            TickPlacement = TickPlacement.Outside,
            SmallChange = 1,
            LargeChange = 1,
            MinWidth = 160,
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = scrubbable
        };
        ToolTip.SetTip(scrub, scrubbable
            ? "이 라인 배치의 내부 시간 — 눈금 한 칸이 한 프레임이다. 끌면 그 프레임의 정지 화면이 서고, 재생 중에는 지금 프레임을 가리킨다."
            : "이 라인에는 시간을 가진 연출이 없습니다.");

        scrub.ValueChanged += (_, args) =>
        {
            // ⚠ 프레임 단위로 못 박는다 — <c>IsSnapToTickEnabled</c>만으로는 끄는 동안
            // 값이 프레임 사이에 서고, 그 자리를 무대가 그대로 그려 손잡이가 미끄러진다.
            double snapped = Math.Round(args.NewValue);

            if (Math.Abs(snapped - args.NewValue) > 0.0001)
            {
                scrub.Value = snapped;
                return; // 되돌아온 ValueChanged가 나머지를 한다
            }

            frameLabel.Text = $"{snapped:0}/{frames:0}fr";

            // 재생이 밀어 넣은 값이면 여기서 멈춘다 — 무대는 이미 그 진행도로 그려졌고,
            // 되받아 치면 재생의 부드러운 보간이 프레임 격자로 덮인다.
            if (_syncingTimeline)
            {
                return;
            }

            // 0은 정지 화면(출발 자리)과 같다 — null이 아니라 0을 흘려도 보간이 같은 자리다.
            SetTransitionProgress(snapped / frames);
        };

        _timelineScrub = scrub;
        _timelineLabel = frameLabel;
        _timelineFrames = frames;

        var layout = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(frameLabel, Dock.Right);
        layout.Children.Add(frameLabel);
        layout.Children.Add(scrub);
        return layout;
    }

    /// <summary>
    /// 재생이 흘린 진행도를 손잡이에 옮긴다 (2026-08-22) — <b>가장 가까운 프레임</b>에
    /// 선다. null(정지 화면)이면 0프레임이다. 빗장은 되먹임을 막는다: 이 쓰기가 부르는
    /// <c>ValueChanged</c>가 다시 <see cref="SetTransitionProgress"/>를 부르면 재생의
    /// 보간이 프레임 격자로 덮이고, 두 값이 서로를 밀며 떨린다.
    /// </summary>
    private void SyncTimelineHandle(double? progress)
    {
        if (_timelineScrub is not { } scrub)
        {
            return;
        }

        double frame = Math.Round(Math.Clamp(progress ?? 0, 0, 1) * _timelineFrames);

        if (Math.Abs(scrub.Value - frame) < 0.0001)
        {
            return;
        }

        _syncingTimeline = true;

        try
        {
            scrub.Value = frame;
        }
        finally
        {
            _syncingTimeline = false;
        }

        if (_timelineLabel is { } label)
        {
            label.Text = $"{frame:0}/{_timelineFrames:0}fr";
        }
    }

    /// <summary>
    /// 이동이 아닌 커맨드의 칩 — 카탈로그에 슬라이더 선언 파라미터가 있으면 수치 칩(⚙,
    /// W68: scale_by·shot_zoom부터)이고, 없으면 커맨드가 있다는 사실을 알리는 표시 칩이다.
    /// 무엇이 만질 수 있는 수치인지는 코드가 아니라 선언이 정한다.
    /// </summary>
    // (칩 스트립 제거 — 2026-08-20 소유자 정리: 커맨드 목록은 왼쪽 대본 패널로 갔다)

    /// <summary>
    /// 칩에서 상세 조절이 되는 파라미터인가 (W68b) — 슬라이더 선언(숫자축) ·
    /// duration(fr 슬라이더) · 후보 토큰이 선언된 타입(depthPreset·focusPreset·
    /// screenPoint 등 = 선택기). 무대 대상(slot/alias)은 여기서 안 바꾼다 —
    /// 대상 교체는 조작이 아니라 다른 커맨드다.
    /// </summary>
    private static bool IsAdjustableParameter(PresentationCommandParameter parameter) =>
        parameter.Slider is not null ||
        string.Equals(parameter.Type, "duration", StringComparison.Ordinal) ||
        (!ArgumentTokenCandidates.IsStageTargetType(parameter.Type) &&
         ArgumentTokenCandidates.For(parameter.Type).Count > 0);

    /// <summary>
    /// 파라미터 수치 편집 행들 (W68 → 2026-08-21 작업대 이사) — 슬라이더 선언이 있는
    /// 파라미터와 duration이 슬라이더로, 후보 토큰 타입은 선택기로 선다. 이동 편집기와
    /// 같은 규칙: 끄는 동안은 라벨만, 손을 뗄 때 한 번 저장, 확정 즉시 정지 프레임이
    /// 새 값으로 다시 접힌다.
    /// </summary>
    private void AddParameterEditorRows(
        StackPanel host, PresentationCommandDefinition definition, PresentationResultCommand command)
    {
        if (_session is null || _request?.EditContext is not { Editable: true } context)
        {
            return;
        }

        {
            foreach (PresentationCommandParameter parameter in definition.Parameters)
            {
                string written = command.Arguments.TryGetValue(parameter.Name, out string? value)
                    ? value
                    : parameter.Default ?? string.Empty;

                // u 토큰 슬라이더 (2026-08-21 소유자: 넛지 distance · 샷 x·y) — 값이
                // <c>3u</c>·<c>-2.5u</c> 같은 <b>토큰</b>이라 숫자 슬라이더로는 안 된다:
                // 읽을 때도(`double.TryParse("3u")`가 0을 낸다) 쓸 때도 u를 알아야 한다.
                // 그래서 <see cref="UnitToken"/>을 지나는 제 갈래를 두고 숫자 슬라이더보다
                // <b>먼저</b> 본다.
                //
                // <b>슬라이더 선언이 있을 때만 선다</b> — 어느 칸을 슬라이더로 만질지는
                // 카탈로그가 정한다(이 코드베이스의 "선언이 정한다" 규칙). 선언이 없는
                // u 칸은 지금처럼 칩 텍스트로 편집한다.
                //
                // 부호는 타입이 정한다: <c>unit</c>은 안 붙이고(방향이 커맨드 이름에
                // 있다 — left·right·up·down, 음수는 런타임이 0으로 클램프),
                // <c>signedUnit</c>은 붙인다(축 하나가 양쪽으로 간다).
                if (UnitSliderKind(parameter) is { } unitSigned &&
                    parameter.Slider is { } unitSlider &&
                    UnitToken.TryParseUnits(written, out float writtenUnits))
                {
                    string UnitLabel(double units) =>
                        (unitSigned && units >= 0 ? "+" : string.Empty) +
                        units.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "u";

                    host.Children.Add(BuildMotionSlider(
                        CommandSink(context.PresentationNodeId, command.CommandId),
                        label: unitSigned ? parameter.Name : "거리",
                        argumentName: parameter.Name,
                        value: writtenUnits,
                        minimum: unitSlider.Minimum,
                        maximum: unitSlider.Maximum,
                        tick: unitSlider.Step,
                        format: UnitLabel,
                        token: UnitLabel));
                }
                else if (parameter.Slider is { } slider)
                {
                    double.TryParse(
                        written, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double current);

                    host.Children.Add(BuildMotionSlider(
                        CommandSink(context.PresentationNodeId, command.CommandId),
                        label: parameter.Name,
                        argumentName: parameter.Name,
                        value: current,
                        minimum: slider.Minimum,
                        maximum: slider.Maximum,
                        tick: slider.Step,
                        format: number => number.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                        token: number => number.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
                }
                else if (string.Equals(parameter.Type, "depthPreset", StringComparison.Ordinal))
                {
                    // 뎁스는 작업대에서 <b>레벨 슬라이더</b>로 만진다 (2026-08-21 소유자:
                    // "조작 콘솔에서는 나쁘진 않은데, 터미널 아래의 연출 편집기에서는
                    // -10 ~ 10까지의 Level을 슬라이더로 직접 조절"). 프리셋 키(close·far…)는
                    // 조작 콘솔과 인자 칩에 그대로 남는다 — 잃는 길은 없다.
                    AddDepthLevelRows(host, context.PresentationNodeId, command, parameter, written);
                }
                else if (string.Equals(parameter.Type, "duration", StringComparison.Ordinal) &&
                         DurationToken.TryParseSeconds(written, out float seconds))
                {
                    host.Children.Add(BuildMotionSlider(
                        CommandSink(context.PresentationNodeId, command.CommandId),
                        label: "시간",
                        argumentName: parameter.Name,
                        value: seconds * DurationToken.FramesPerSecond,
                        minimum: 0,
                        maximum: DurationSliderMax(seconds * DurationToken.FramesPerSecond),
                        tick: 1,
                        format: frames => frames <= 0 ? "0fr (즉시)" : $"{frames:0}fr",
                        token: frames => $"{frames:0}fr"));
                }
                else if (string.Equals(parameter.Type, "ease", StringComparison.Ordinal))
                {
                    // 이징 칸 (2026-08-21 런타임이 place·size·회전에도 열었다) — 이동과
                    // 같은 선택기다: 곡선 미리보기 + 커스텀 곡선(@이름) 편집까지.
                    // 미지정 기본은 런타임 스펙 그대로 EaseKindOf(null)가 말한다.
                    host.Children.Add(BuildEaseSelector(
                        context.PresentationNodeId,
                        command.CommandId,
                        written.Length > 0 ? written : null,
                        CurveKeysOf(written),
                        parameter.Name,
                        StageMotionPlan.EaseKindOf(null).ToString()));
                }
                else if (string.Equals(parameter.Type, "oscillation", StringComparison.Ordinal))
                {
                    // 진동 칸 (2026-08-21 증보) — 같은 선택기이되 <b>핑퐁</b>으로 그린다.
                    // 표준 이징이 왕복의 절반으로 읽히므로 35종이 그대로 몸짓이 되고,
                    // [곡선 편집…]은 그 핑퐁을 구워 열어 준다(끝점이 (0,0)·(1,0)이라
                    // 구운 결과가 그대로 유효한 진동 곡선이다).
                    //
                    // 기본값은 <c>OutSine</c>이다 — 빈 토큰의 기본 혹 sin(πt)와
                    // <b>같은 함수</b>라(오차 0) 고르면 인자가 지워지고 텍스트가 안 늘어난다.
                    host.Children.Add(BuildEaseSelector(
                        context.PresentationNodeId,
                        command.CommandId,
                        written.Length > 0 ? written : null,
                        CurveKeysOf(written),
                        parameter.Name,
                        EaseKind.OutSine.ToString(),
                        CurveKind.Oscillation));
                }
                else if (!ArgumentTokenCandidates.IsStageTargetType(parameter.Type) &&
                         ArgumentTokenCandidates.For(parameter.Type) is { Count: > 0 } candidates)
                {
                    // 프리셋 토큰 파라미터 (W68b — Depth·Place가 첫 고객): 후보 선택기.
                    // 후보는 제안이지 제약이 아니다 — 후보 밖 값(뎁스의 연속 레벨 숫자 등)은
                    // 선택 없음 + 현재 값 표시로 두고, 자유 입력은 기존 칩 텍스트 편집의 몫이다.
                    host.Children.Add(BuildTokenSelector(
                        CommandSink(context.PresentationNodeId, command.CommandId),
                        parameter,
                        written,
                        candidates));
                }
            }
        }
    }

    /// <summary>
    /// 뎁스 레벨 슬라이더 (2026-08-21 소유자 지시) — 런타임은 <c>size</c>의 depth 자리에
    /// <b>임의 실수 레벨</b>을 받아 커브로 푼다(설계 구간 0~10, 그 밖은 끝 키 기울기로
    /// 외삽 — 그래서 음수도 뜻이 있다). 프리셋 키가 적혀 있으면 슬라이더는 가운데(5)에
    /// 서고 무엇이 적혀 있는지 라벨이 말한다 — 끌기 전에는 아무것도 쓰지 않는다.
    ///
    /// ⚠ 프리뷰 폴드는 아직 레벨을 모른다(코어 DTO가 level 커브를 안 읽는다 —
    /// "레벨 수치는 커브 폴드 미지원"). 그래서 레벨을 쓴 커맨드는 무대에 "반영 안 됨"으로
    /// 선다. 재생·발행은 정상이다. 저쪽 코어가 커브 폴드를 열면 이 안내가 사라진다.
    /// </summary>
    private void AddDepthLevelRows(
        StackPanel host,
        string presentationNodeId,
        PresentationResultCommand command,
        PresentationCommandParameter parameter,
        string written)
    {
        // 적힌 것이 수치면 그 자리, 라벨(far·mid…)이면 <b>그 라벨의 눈금</b>이다
        // (2026-08-21 런타임 개통: 라벨은 커브 위의 점 이름이 됐다 — DepthLevelLabels).
        // 둘 다 아니면 폴드도 거부하는 토큰이라 가운데에 세우고 원문을 보인다.
        bool known =
            double.TryParse(
                written,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double level) ||
            TryLabelLevel(written, out level);

        host.Children.Add(BuildMotionSlider(
            CommandSink(presentationNodeId, command.CommandId),
            label: "뎁스",
            argumentName: parameter.Name,
            value: known ? level : DepthLevelLabels.Mid,
            // 커브 설계 구간은 [0,20]이다 (2026-08-21 저쪽이 close 위로 늘렸다 — 배율
            // 상한 2.2). 음수 쪽은 외삽이라 소유자가 정한 -10을 유지한다.
            minimum: -10,
            maximum: 20,
            tick: 0.5,
            // 눈금에 정확히 선 값은 이름으로도 읽힌다 — 슬라이더 하나가 두 표기를 겸한다.
            format: value => !known && Math.Abs(value - DepthLevelLabels.Mid) < 0.001
                ? "(" + written + ")"
                : FormatDepthLevel(value),
            token: value => value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// u 토큰 슬라이더로 만질 칸인가, 그렇다면 부호를 붙이는가.
    /// <c>unit</c> = 부호 없음(false) · <c>signedUnit</c> = 부호 있음(true) ·
    /// 그 밖 = null(슬라이더 갈래가 아니다).
    /// </summary>
    private static bool? UnitSliderKind(PresentationCommandParameter parameter) =>
        string.Equals(parameter.Type, "unit", StringComparison.Ordinal) ? false
        : string.Equals(parameter.Type, "signedUnit", StringComparison.Ordinal) ? true
        : null;

    /// <summary>라벨 토큰(far·mid·별칭)의 눈금 — 판정은 코어 <see cref="DepthLevelLabels"/> 하나다.</summary>
    private static bool TryLabelLevel(string token, out double level)
    {
        if (DepthLevelLabels.TryGetLevel(token, out float found))
        {
            level = found;
            return true;
        }

        level = 0;
        return false;
    }

    /// <summary>눈금에 정확히 선 값은 이름을 함께 보인다(5 → "5 (mid)").</summary>
    private static string FormatDepthLevel(double level)
    {
        string number = level.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

        string name = Math.Abs(level - DepthLevelLabels.Far) < 0.001 ? "far"
            : Math.Abs(level - DepthLevelLabels.Back) < 0.001 ? "back"
            : Math.Abs(level - DepthLevelLabels.Mid) < 0.001 ? "mid"
            : Math.Abs(level - DepthLevelLabels.Front) < 0.001 ? "front"
            : Math.Abs(level - DepthLevelLabels.Close) < 0.001 ? "close"
            : string.Empty;

        return name.Length > 0 ? number + " (" + name + ")" : number;
    }

    /// <summary>
    /// <c>@이름</c>이면 프로젝트 곡선의 키. 못 찾으면 null — 런타임과 같은 폴백이고,
    /// 내보내기 검증이 그 어긋남을 막는다(계획이 쓰는 규칙과 같은 자리).
    /// </summary>
    private IReadOnlyList<CurveKey>? CurveKeysOf(string? ease) =>
        ease is ['@', .. var name]
            ? _session?.Project.EaseCurves.FirstOrDefault(curve =>
                string.Equals(curve.Name, name, StringComparison.Ordinal))?.Keys
            : null;

    /// <summary>프리셋 토큰 한 줄 — 고르면 그 인자만 바뀐다(같은 커맨드 인자 수정 통로).</summary>
    private Control BuildTokenSelector(
        ArgumentSink write,
        PresentationCommandParameter parameter,
        string currentValue,
        IReadOnlyList<string> candidates)
    {
        var combo = new ComboBox
        {
            ItemsSource = candidates,
            SelectedIndex = candidates.ToList().FindIndex(candidate =>
                string.Equals(candidate, currentValue, StringComparison.Ordinal)),
            FontSize = 10,
            MinWidth = 120,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (combo.SelectedIndex < 0 && currentValue.Length > 0)
        {
            combo.PlaceholderText = currentValue; // 후보 밖 값(연속 레벨 등) — 현재 값을 보인다.
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (_session is null || combo.SelectedIndex < 0)
            {
                return;
            }

            string selected = candidates[combo.SelectedIndex];

            if (!string.Equals(selected, currentValue, StringComparison.Ordinal))
            {
                write(parameter.Name, selected);
            }
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var label = new TextBlock
        {
            Text = parameter.Name,
            FontSize = 10,
            Opacity = 0.7,
            MinWidth = 74,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(label, 0);
        Grid.SetColumn(combo, 1);
        row.Children.Add(label);
        row.Children.Add(combo);

        return row;
    }

    /// <summary>
    /// 이 라인의 시간 흐름 (2026-08-21) — 커맨드마다 자기 duration·이징으로 자기가 바꾼
    /// 노드를 끈다. 이동·배치·뎁스가 한 라인에 같이 있어도 각자 흐른 뒤 컴포저가 합친다.
    /// </summary>
    private StageMotionPlan? _motionPlan;

    /// <summary>라인이 시작되는 순간의 자리들(진행 0) — 정지 화면이 서는 자리다.</summary>
    private IReadOnlyDictionary<string, StageRect>? _motionStartRects;

    /// <summary>합성에 필요한 재료 — 진행도마다 같은 컴포저를 다시 부르기 위해 렌더가 남긴다.</summary>
    private (MiniStageState State, string? SpeakerName, string? SpeakerCharacterId, double Width, double Height)?
        _composeContext;

    /// <summary>
    /// 라인 시작 후 <paramref name="elapsedSeconds"/> 시점의 자리들. 계획이 없으면 null —
    /// 그때는 라인 사이 전이(직전 렌더 → 이번 렌더)가 화면을 맡는다.
    ///
    /// 좌표를 여기서 손으로 더하지 않는다: <b>보간한 무대 상태를 컴포저에 그대로 넘긴다.</b>
    /// 그래서 배율·포커스 보정처럼 자리와 크기가 함께 걸린 커맨드도 저절로 맞는다.
    /// </summary>
    private IReadOnlyDictionary<string, StageRect>? ComposeMotionRects(double elapsedSeconds)
    {
        if (_motionPlan is not { } plan || _composeContext is not { } context)
        {
            return null;
        }

        StageSceneLayout layout = StageSceneComposer.Compose(
            context.State,
            context.SpeakerName,
            context.SpeakerCharacterId,
            context.Width,
            context.Height,
            plan.Evaluate(elapsedSeconds),
            _session?.TuningLibrary.SurfaceLayouts);

        var rects = new Dictionary<string, StageRect>(StringComparer.Ordinal);

        foreach (StagePortraitPlacement portrait in layout.Portraits)
        {
            rects[portrait.SlotKey] = portrait.Rect;
        }

        return rects;
    }

    /// <summary>
    /// 칩의 이징 이름 → 코어 어휘. 모르는 이름은 런타임 스펙 기본값(OutCubic)으로 —
    /// 브리지의 파싱 실패 처리와 같은 방향이다(로그 대신 카탈로그가 후보를 제한한다).
    /// </summary>
    private static EaseKind EaseKindOf(string? name) => StageMotionPlan.EaseKindOf(name);


    /// <summary>픽셀 값을 사람이 읽는 u 토큰으로. 환산의 유일한 자리는 UnitToken이다.</summary>
    private string FormatUnits(double pixels)
    {
        float perUnit = UnitToken.PixelsPerUnit(
            _session?.TuningLibrary.Tuning?.ReferenceStageWidth ?? 1920f);
        double units = perUnit > 0 ? pixels / perUnit : 0;

        return $"{(units >= 0 ? "+" : "")}{units:0.##}u";
    }

    /// <summary>
    /// 이 라인의 소리 커맨드 ♪ 칩 (W34-b) — 정지 프레임에 그릴 것이 없는 오디오가
    /// 조용히 사라지지 않게 좌측에 알린다(규칙 14의 소리 판).
    /// </summary>
    /// <summary>
    /// 시뮬 시작값 편집 (W36-b) — "이 변수가 이 값으로 시작한다고 치자". 뷰 상태라 저장되지
    /// 않고, 빈 값이면 등록 초기값으로 돌아간다. 적용해도 닫히지 않는다(우클릭 닫기).
    /// </summary>
    private void ShowSimulationFlyout(IReadOnlyList<StatFold.StatValue> stats)
    {
        if (_session is null)
        {
            return;
        }

        ShowManipulationFlyout((host, rebuild) =>
        {
            host.MinWidth = 210;
            host.Children.Add(new TextBlock
            {
                Text = "시뮬 시작값 — 조건 갈래를 자동 판정합니다 (우클릭으로 닫기)",
                FontSize = 10,
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 230
            });

            foreach (StatFold.StatValue stat in stats)
            {
                string variable = stat.Variable;
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };

                var label = new TextBlock
                {
                    Text = variable,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var input = new TextBox
                {
                    Text = _session.SimulationValues.TryGetValue(variable, out string? overridden)
                        ? overridden
                        : string.Empty,
                    PlaceholderText = "등록 초기값",
                    FontSize = 11,
                    MinHeight = 24
                };

                void Commit()
                {
                    string text = input.Text?.Trim() ?? string.Empty;

                    if (text.Length == 0)
                    {
                        _session.SimulationValues.Remove(variable);
                    }
                    else
                    {
                        _session.SimulationValues[variable] = text;
                    }

                    BranchSelectionChanged?.Invoke(); // 갈래 판정·HUD가 다시 계산된다
                    rebuild();
                }

                input.KeyDown += (_, keyArgs) =>
                {
                    if (keyArgs.Key == Key.Enter)
                    {
                        Commit();
                    }
                };
                input.LostFocus += (_, _) => Commit();

                Grid.SetColumn(label, 0);
                Grid.SetColumn(input, 1);
                row.Children.Add(label);
                row.Children.Add(input);
                host.Children.Add(row);
            }

            if (_session.SimulationValues.Count > 0)
            {
                var reset = new Button
                {
                    Content = "시뮬 값 전부 지우기",
                    FontSize = 10,
                    Padding = new Thickness(8, 3),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                reset.Click += (_, _) =>
                {
                    _session.SimulationValues.Clear();
                    BranchSelectionChanged?.Invoke();
                    rebuild();
                };
                host.Children.Add(reset);
            }
        });
    }

    private void RenderAudioCues(MiniStagePreviewRequest request, double height, double em)
    {
        if (request.AudioCues is not { Count: > 0 } cues)
        {
            return;
        }

        var rows = new StackPanel { Spacing = em * 0.15 };

        foreach (string cue in cues)
        {
            rows.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(150, 15, 40, 70)),
                CornerRadius = new CornerRadius(em * 0.2),
                Padding = new Thickness(em * 0.35, em * 0.12),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = $"♪ {cue}",
                    FontSize = em * 0.55,
                    Foreground = new SolidColorBrush(Color.FromArgb(235, 147, 197, 253))
                }
            });
        }

        Add(rows, new StageRect(em * 0.6, height * 0.10, em * 14, em * (cues.Count + 1)));
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
                var backgroundImage = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.UniformToFill,
                    Width = width,
                    Height = height
                };
                Add(backgroundImage, new StageRect(0, 0, width, height));

                // 전이(W33)의 크로스페이드 대상.
                _backgroundImage = backgroundImage;
                _backgroundImagePath = path;
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

        // 숨김/빈 슬롯도 자리와 크기가 보인다 — 네모 윤곽 + 슬롯명 태그 (소유자 지시 2026-08-06).
        if (!slot.Visible)
        {
            RenderGhostSlot(portrait, em);
            return;
        }

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
                if (!args.GetCurrentPoint(image).Properties.IsLeftButtonPressed)
                {
                    return;
                }

                if (PlaybackAdvance?.Invoke() == true)
                {
                    args.Handled = true;
                    return;
                }

                _consoleOpenAt = args.GetPosition(this);
                UiGuard.Run(
                    _session,
                    "캐릭터 조작",
                    () => ShowCharacterPopover(portrait.SlotKey));
                args.Handled = true;
            };
        }

        Add(image, portrait.Rect);

        // 전이(W33) 대상 — 이 슬롯의 자리. 보이는 초상이므로 가시성은 true다.
        RegisterTransition(portrait.SlotKey, image, portrait.Rect, visible: true);

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

    /// <summary>
    /// 이미지 없는(숨김·빈) 슬롯의 자리 표시 — 점선 네모 + 슬롯명 태그. 클릭하면 그 슬롯을
    /// 조작하는 팝오버가 열린다. 화면에 없는 것을 없는 척하지 않는다(규칙 14의 자리 버전).
    /// </summary>
    private void RenderGhostSlot(StagePortraitPlacement portrait, double em)
    {
        var outline = new Avalonia.Controls.Shapes.Rectangle
        {
            Width = portrait.Rect.Width,
            Height = portrait.Rect.Height,
            Stroke = new SolidColorBrush(Color.FromArgb(150, 148, 163, 184)),
            StrokeThickness = Math.Max(1.5, em * 0.06),
            StrokeDashArray = [4, 4],
            Fill = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255))
        };

        if (CanManipulate())
        {
            outline.Cursor = new Cursor(StandardCursorType.Hand);
            outline.PointerPressed += (_, args) =>
            {
                if (!args.GetCurrentPoint(outline).Properties.IsLeftButtonPressed)
                {
                    return;
                }

                if (PlaybackAdvance?.Invoke() == true)
                {
                    args.Handled = true;
                    return;
                }

                _consoleOpenAt = args.GetPosition(this);
                UiGuard.Run(_session, "슬롯 조작", () => ShowCharacterPopover(portrait.SlotKey));
                args.Handled = true;
            };
        }

        Add(outline, portrait.Rect);

        // 전이(W33) 대상 — 숨김 슬롯의 자리도 미끄러진다. ⚠ 자리는 등록하되 <b>가시성은
        // false</b>다: 이 구분이 없으면 다음 라인의 fade_in이 등장이 아니라 이동으로 읽힌다.
        RegisterTransition(portrait.SlotKey, outline, portrait.Rect, visible: false);

        string label = portrait.Slot.CharacterId is { } cast
            ? $"{portrait.SlotKey} · {cast} (숨김)"
            : $"{portrait.SlotKey} (빈 슬롯)";

        Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(170, 30, 41, 59)),
            CornerRadius = new CornerRadius(em * 0.15),
            Padding = new Thickness(em * 0.3, em * 0.1),
            Child = new TextBlock
            {
                Text = label,
                FontSize = em * 0.55,
                Foreground = new SolidColorBrush(Color.FromArgb(230, 203, 213, 225))
            }
        }, new StageRect(
            portrait.Rect.X + em * 0.2,
            portrait.Rect.Y + em * 0.2,
            Math.Max(portrait.Rect.Width - em * 0.4, em),
            em * 1.1));
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

        var dialogueText = new TextBlock
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
        };
        Add(dialogueText, box.TextRect);

        // 타자기(W32)가 이 텍스트만 갱신한다 — 전체 재렌더 없이.
        _dialogueText = dialogueText;
        _dialogueFullText = request.LineText ?? string.Empty;

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
    /// 선택지 제시 — 블록의 옵션 전부를 화면 중앙에 세로로 쌓는다. 지금 선택된 라벨은
    /// 노란 테두리다. 실제 배치·전이는 런타임의 것이고 이것은 "무엇이 함께 제시되는가"의 근사다.
    /// </summary>
    private void RenderChoiceOptions(
        IReadOnlyList<StageChoiceOption> options,
        double width,
        double height,
        double em)
    {
        double optionWidth = width * 0.46;
        double optionHeight = em * 1.8;
        double gap = em * 0.45;
        double totalHeight = options.Count * optionHeight + (options.Count - 1) * gap;
        double y = Math.Max(em, (height - totalHeight) / 2);

        foreach (StageChoiceOption option in options)
        {
            var optionBox = new Border
            {
                Width = optionWidth,
                Height = optionHeight,
                // 현재 접히는 갈래(W35)는 배경 틴트, 커서 라벨은 노란 테두리 — 두 표시는 별개다.
                Background = option.IsTakenBranch
                    ? new SolidColorBrush(Color.FromArgb(205, 20, 55, 35))
                    : BoxBackground,
                CornerRadius = new CornerRadius(optionHeight / 2),
                BorderThickness = new Thickness(em * 0.09),
                BorderBrush = option.IsSelected
                    ? SpeakerHighlight
                    : new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
                Child = new TextBlock
                {
                    Text = (option.IsTakenBranch ? "▶ " : string.Empty) +
                        (option.Text.Length == 0 ? "(빈 라벨)" : option.Text),
                    FontSize = em * 0.85,
                    Foreground = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    MaxWidth = optionWidth - em
                }
            };

            // 옵션 클릭 = 그 갈래 선택 (W35). 재생 중이면 선택 후 곧장 진행이기도 하다(W35-5).
            if (option is { LineId: { } optionLineId, BlockLineId: { } blockLineId } && _session is not null)
            {
                optionBox.Cursor = new Cursor(StandardCursorType.Hand);
                optionBox.PointerPressed += (_, args) =>
                {
                    if (!args.GetCurrentPoint(optionBox).Properties.IsLeftButtonPressed)
                    {
                        return;
                    }

                    UiGuard.Run(_session, "갈래 선택", () =>
                    {
                        _session.BranchSelection.SelectChoice(blockLineId, optionLineId);
                        PlaybackAdvance?.Invoke(); // 재생 중이면 선택지에서 멈춘 진행을 잇는다
                        BranchSelectionChanged?.Invoke();
                    });
                    args.Handled = true;
                };
            }

            Add(optionBox, new StageRect((width - optionWidth) / 2, y, optionWidth, optionHeight));

            y += optionHeight + gap;
        }
    }

    /// <summary>
    /// 스탯 HUD (X3·W35) — 등록 변수의 "이 라인까지" 누적 값. 선택된 갈래 기준이고,
    /// 미선택 갈래가 남아 있으면(요청의 근사 뱃지) 그 구간만 문서 순서 근사다(규칙 14).
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

        // 조건 값 시뮬 (W36-b) — 시작값을 바꿔 "이 값이면 어느 갈래인가"를 본다.
        bool simActive = _session?.SimulationValues.Count > 0;
        var simChip = new Border
        {
            Background = new SolidColorBrush(simActive
                ? Color.FromArgb(190, 30, 64, 175)
                : Color.FromArgb(150, 0, 0, 0)),
            CornerRadius = new CornerRadius(em * 0.2),
            Padding = new Thickness(em * 0.35, em * 0.12),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock
            {
                Text = simActive ? "시뮬 ●" : "시뮬",
                FontSize = em * 0.6,
                Foreground = Brushes.White,
                Opacity = 0.9
            }
        };
        ToolTip.SetTip(simChip, "변수 시작값을 바꿔 조건 갈래 자동 판정을 봅니다.");
        simChip.PointerPressed += (_, args) =>
        {
            _consoleOpenAt = args.GetPosition(this);
            UiGuard.Run(_session, "값 시뮬", () => ShowSimulationFlyout(stats));
            args.Handled = true;
        };
        Add(simChip, new StageRect(width - em * 6.6, em * 0.5, em * 2.9, em * 1.2));

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
            Text = (_request?.State.PassedBranchApproximation == true
                    ? "미선택 갈래 있음 — 그 구간은 문서 순서 근사"
                    : "선택된 갈래 기준") +
                (_session?.SimulationValues.Count > 0 ? " · 시뮬 시작값 적용 중" : string.Empty),
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
    /// 커맨드 여럿을 <b>한 번의 편집으로</b> 라인에 반영한다 — 자주 쓰는 묶음 칩의 통로다
    /// (2026-08-24). 되돌리기 한 번이 단추 한 번을 원복한다.
    ///
    /// 상태줄도 한 번만 말한다: 단계마다 알리면 다섯 단계짜리 칩이 화면 아래를 다섯 번
    /// 갈아 치우고, 사람이 읽을 수 있는 것은 마지막 하나뿐이다.
    /// </summary>
    private void ApplyStageCommands(
        string chipName,
        IReadOnlyList<(string Output, IReadOnlyDictionary<string, string> Arguments)> steps)
    {
        if (_session is null || steps.Count == 0 ||
            _request?.EditContext is not { Editable: true } context)
        {
            return;
        }

        UiGuard.Run(_session, "무대 조작", () =>
        {
            PresentationStageActions.ApplyAll(
                _session.Editor,
                ManipulationCatalog,
                context.PresentationNodeId,
                context.LineId!,
                steps);

            if (steps.Count > 1)
            {
                _session.SetStatus($"'{chipName}' {steps.Count}단계를 붙였습니다 (되돌리기 한 번).");
            }

            ManipulationApplied?.Invoke();
        });
    }

    /// <summary>
    /// 장면 준비 조작(슬롯 생성·캐스팅)을 노드 Setup에 반영한다 — 어느 라인의 프리뷰에서
    /// 조작했든 같은 자리다. 같은 대상의 재설정은 커맨드가 쌓이지 않고 값이 바뀐다.
    /// </summary>
    private void ApplySetupCommand(string outputCommand, params (string Key, string Value)[] arguments)
    {
        if (_session is null || _request?.EditContext is not { Editable: true } context)
        {
            return;
        }

        UiGuard.Run(_session, "무대 Setup 조작", () =>
        {
            PresentationStageActions.ApplyToSetup(
                _session.Editor,
                ManipulationCatalog,
                context.PresentationNodeId,
                outputCommand,
                StageArgs(arguments));
            ManipulationApplied?.Invoke();
        });
    }

    /// <summary>
    /// 이동 수치 편집기 본체 (W66 → 2026-08-21 작업대 Inspector로 이사) — 선언된 축(x·y)과
    /// 시간을 슬라이더로 만진다. 축 슬라이더는 선언이 만든다: x·y를 코드에 박지 않는다.
    ///
    /// <b>끄는 동안은 편집이 아니다</b>(성능 규칙): 값 라벨만 따라 움직이고 프로젝트는
    /// 그대로다. 손을 뗄 때 한 번만 <see cref="ProjectEditor.UpdatePresentationCommandArguments"/>를
    /// 지나므로 <b>되돌리기 한 번이 조작 하나</b>를 원복한다 — 슬라이더가 지나온 중간값이
    /// undo 스택에 쌓이지 않는다. 만지는 대상은 커맨드 <b>하나</b>다.
    /// </summary>
    private void AddMotionEditorRows(StackPanel host, StageMotionCue cue, string headerSuffix = "")
    {
        if (_session is null || _request?.EditContext is not { Editable: true } context)
        {
            return;
        }

        PresentationCommandDefinition? definition = ManipulationCatalog.Find(cue.DefinitionId);

        if (definition?.Motion is not { } motion)
        {
            return;
        }

        float perUnit = UnitToken.PixelsPerUnit(
            _session.TuningLibrary.Tuning?.ReferenceStageWidth ?? 1920f);

        host.Children.Add(new TextBlock
        {
            Text = $"{definition.DisplayName} — {cue.SlotKey}{headerSuffix}",
            FontSize = 10,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 250
        });

        foreach (PresentationMotionAxis axis in motion.Axes)
        {
            double pixels = string.Equals(axis.Axis, PresentationMotionAxis.XAxis, StringComparison.Ordinal)
                ? cue.DeltaX
                : cue.DeltaY;

            host.Children.Add(BuildMotionSlider(
                CommandSink(context.PresentationNodeId, cue.CommandId),
                label: axis.Axis == PresentationMotionAxis.XAxis ? "가로" : "세로",
                argumentName: axis.ParameterName,
                value: perUnit > 0 ? pixels / perUnit : 0,
                minimum: -6,
                maximum: 6,
                tick: 0.25,
                format: units => $"{(units >= 0 ? "+" : "")}{units:0.##}u",
                token: units => $"{(units >= 0 ? "+" : "")}{units:0.##}u"));
        }

        if (motion.DurationParameterName is { } durationParameter)
        {
            host.Children.Add(BuildMotionSlider(
                CommandSink(context.PresentationNodeId, cue.CommandId),
                label: "시간",
                argumentName: durationParameter,
                value: cue.DurationFrames,
                minimum: 0,
                maximum: DurationSliderMax(cue.DurationFrames),
                tick: 1,
                format: frames => frames <= 0 ? "0fr (즉시)" : $"{frames:0}fr",
                token: frames => $"{frames:0}fr"));
        }

        if (motion.EaseParameterName is { } easeParameter)
        {
            host.Children.Add(BuildEaseSelector(
                context.PresentationNodeId, cue.CommandId, cue.Ease, cue.CurveKeys,
                easeParameter, motion.DefaultEase));
        }
    }

    /// <summary>
    /// 이징 선택기 (W67) — 후보는 코어 <see cref="EaseKind"/> 어휘 그대로이고, 옆의 곡선
    /// 미리보기는 재생이 쓰는 그 <see cref="EaseFunctions"/>로 그린다(사본 없음 — 선택기가
    /// 보여 주는 모양 = 재생이 타는 모양). <b>기본값(OutCubic)을 고르면 인자를 지운다</b> —
    /// 다섯째 토큰이 생략돼 기존 대본과의 diff가 최소가 된다.
    /// </summary>
    private Control BuildEaseSelector(
        string presentationNodeId,
        string commandId,
        string? ease,
        IReadOnlyList<CurveKey>? curveKeys,
        string easeParameter,
        string? defaultEase,
        CurveKind curveKind = CurveKind.Motion)
    {
        // 진동 칸은 같은 선택기를 쓰되 <b>모양을 핑퐁으로</b> 그린다 — 표준 이징이
        // 왕복의 절반으로 읽히기 때문이다(2026-08-21 증보). 미리보기가 재생과 같은
        // 코어 함수를 지나므로 고를 때 보이는 것이 그대로 나온다.
        bool oscillation = curveKind == CurveKind.Oscillation;
        Func<EaseKind, float, float> Shape = oscillation
            ? OscillationFunctions.PingPong
            : EaseFunctions.Evaluate;

        // 안 적었을 때 콤보가 짚을 자리 — <b>종류마다 다르다</b>. 이동은 런타임 스펙
        // 기본 OutCubic이고, 진동은 <c>OutSine</c>이다(빈 토큰의 기본 혹 sin(πt)와 같은
        // 함수라 오차 0). 여기서 이동 기본을 쓰면 "안 적었는데 OutCubic이라고 적힌"
        // 화면이 되어 실제 재생과 어긋난다.
        EaseKind Current(string? token) =>
            Enum.TryParse(token, ignoreCase: true, out EaseKind parsed)
                ? parsed
                : oscillation ? EaseKind.OutSine : EaseKindOf(null);
        // 커스텀(커맨드 소유 곡선)이면 콤보 첫 칸이 그것이다 — 예전처럼 선택 없음(-1)으로
        // 두면 "지금 쓰이는 ease가 안 보이는" 화면이 된다(2026-08-20 소유자 보고).
        bool isCustom = ease is ['@', ..];
        string[] enumNames = Enum.GetNames<EaseKind>();
        string[] candidates = isCustom ? ["커스텀 곡선", .. enumNames] : enumNames;

        var curvePreview = new Avalonia.Controls.Shapes.Polyline
        {
            Stroke = new SolidColorBrush(Color.FromArgb(220, 250, 204, 21)),
            StrokeThickness = 1.5,
            Width = 44,
            Height = 26,
            VerticalAlignment = VerticalAlignment.Center
        };

        void DrawShape(Func<float, float> shape)
        {
            const int sampleCount = 32;
            var points = new Avalonia.Points();

            for (int i = 0; i <= sampleCount; i++)
            {
                float t = i / (float)sampleCount;
                // Back·Elastic·커스텀이 [0,1]을 벗어나므로 여유를 두고 세로를 뒤집는다.
                points.Add(new Point(t * 44, 26 - Math.Clamp(shape(t), -0.25f, 1.25f) * 18 - 4));
            }

            curvePreview.Points = points;
        }

        if (isCustom && curveKeys is { Count: >= 2 } customKeys)
        {
            CurveKey[] keys = customKeys as CurveKey[] ?? customKeys.ToArray();
            DrawShape(t => CurveFunctions.Evaluate(keys, t));
        }
        else
        {
            EaseKind current = Current(ease);
            DrawShape(t => Shape(current, t));
        }

        var combo = new ComboBox
        {
            ItemsSource = candidates,
            SelectedIndex = isCustom ? 0 : Array.IndexOf(enumNames, Current(ease).ToString()),
            FontSize = 10,
            MinWidth = 110,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(combo, isCustom
            ? "이 커맨드의 커스텀 곡선입니다. 표준 이징을 고르면 커스텀을 버리고 되돌아갑니다."
            : "이징 곡선 — 미리보기 모양 그대로 재생됩니다. 기본(OutCubic)을 고르면 텍스트에서 생략됩니다.");

        combo.SelectionChanged += (_, _) =>
        {
            if (_session is null || combo.SelectedIndex < 0)
            {
                return;
            }

            string selected = candidates[combo.SelectedIndex];

            if (string.Equals(selected, "커스텀 곡선", StringComparison.Ordinal))
            {
                return; // 이미 커스텀 — 되돌아온 선택은 편집이 아니다.
            }

            DrawShape(t => Shape(Enum.Parse<EaseKind>(selected), t));

            // 기본값 = 생략 (빈 값이 인자를 지운다 — SetPresentationCommandArgument 규약).
            string? token = string.Equals(selected, defaultEase, StringComparison.OrdinalIgnoreCase)
                ? null
                : selected;

            UiGuard.Run(_session, "이징 선택", () =>
            {
                if (isCustom)
                {
                    // 표준으로 복귀 = 소유 곡선 폐기(보관함 사본은 남는다).
                    EaseCurveCommandActions.DiscardOwned(
                        _session.Editor, presentationNodeId, commandId, easeParameter, token);
                }
                else
                {
                    _session.Editor.SetPresentationCommandArgument(
                        presentationNodeId, commandId, easeParameter, token);
                }

                ManipulationApplied?.Invoke();
            });
        };

        // 곡선 편집 (W67 후속, 소유자: "마야처럼 키를 줘서 커스텀") — 선택한 이징에서
        // 출발해 키를 만진다. 저장은 프로젝트 곡선 + 커맨드 인자 @이름이다.
        var editButton = new Button
        {
            Content = "곡선 편집…",
            FontSize = 10,
            Padding = new Thickness(6, 2),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(editButton, "이 곡선에서 출발해 키를 만듭니다 — 저장하면 커스텀 곡선(@이름)이 됩니다.");
        editButton.Click += (_, _) =>
            ShowCurveEditorWindow(presentationNodeId, commandId, ease, easeParameter, curveKind);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        var label = new TextBlock
        {
            Text = "곡선",
            FontSize = 10,
            Opacity = 0.7,
            Width = 30,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(label, 0);
        Grid.SetColumn(combo, 1);
        Grid.SetColumn(curvePreview, 2);
        Grid.SetColumn(editButton, 3);
        row.Children.Add(label);
        row.Children.Add(combo);
        row.Children.Add(curvePreview);
        row.Children.Add(editButton);

        return row;
    }
    /// <summary>곡선 편집 분리 창 — 도킹·분리 무대가 각자 하나씩 갖고, 닫으면 비운다.</summary>
    private EaseCurveWindow? _curveWindow;

    /// <summary>
    /// 곡선 편집을 별도 창으로 연다 (2026-08-20 소유자: "별도의 그래프로 빼줄래" —
    /// 조작 콘솔 팝업 안에서는 캔버스가 스크롤 껍데기에 갇혀 동작하지 않았다).
    /// 이미 열려 있으면 그 창이 새 커맨드로 갈아탄다. 편집 규칙(열면 소유 곡선 생성 ·
    /// 실시간 커밋 · 보관함 복사)은 창이 진다 — <see cref="EaseCurveWindow"/>.
    /// </summary>
    private void ShowCurveEditorWindow(
        string presentationNodeId, string commandId, string? ease, string easeParameter,
        CurveKind curveKind = CurveKind.Motion)
    {
        if (_session is null || _request?.EditContext is not { Editable: true })
        {
            return;
        }

        if (_curveWindow is null)
        {
            _curveWindow = new EaseCurveWindow();
            _curveWindow.Closed += (_, _) => _curveWindow = null;
            _curveWindow.ShowFor(
                _session, presentationNodeId, commandId, ease, easeParameter,
                () => ManipulationApplied?.Invoke(), curveKind);

            if (VisualRoot is Window owner)
            {
                _curveWindow.Show(owner);
            }
            else
            {
                _curveWindow.Show();
            }
        }
        else
        {
            _curveWindow.ShowFor(
                _session, presentationNodeId, commandId, ease, easeParameter,
                () => ManipulationApplied?.Invoke(), curveKind);
            _curveWindow.Activate();
        }
    }

    /// <summary>
    /// 시간 슬라이더의 오른쪽 끝. 기본은 프레임 별칭이 있는 48fr(2초)까지지만,
    /// <b>이미 그보다 길게 적힌 값은 그 자리가 보이게</b> 늘린다 (2026-08-21 소유자:
    /// "실제 커맨드가 사용하는 프레임을 쓰도록") — 48로 잘라 두면 슬라이더가 대본을
    /// 잘못 말한다. 초 단위 토큰(예: 3s)이 이 자리로 온다.
    /// </summary>
    private static double DurationSliderMax(double frames) =>
        Math.Max(48, Math.Ceiling(frames));

    /// <summary>
    /// 값 하나짜리 슬라이더 줄. 끄는 동안은 라벨만 갱신하고, <b>손을 뗄 때 한 번</b> 저장한다.
    /// </summary>
    /// <summary>
    /// 인자 하나를 <b>어디에</b> 쓸 것인가 (2026-08-22) — 노드의 커맨드냐, [자주 쓰는]
    /// 칩이냐. 수치 편집기가 대상의 정체를 모르게 하는 유일한 이음매다: 슬라이더·선택기
    /// 한 벌이 두 곳을 섬긴다(사본 금지).
    /// </summary>
    private delegate void ArgumentSink(string argumentName, string value);

    /// <summary>노드 커맨드에 쓰는 통로 — 예전 그대로 편집기를 지나고 화면을 다시 접는다.</summary>
    private ArgumentSink CommandSink(string presentationNodeId, string commandId) =>
        (argumentName, value) =>
        {
            if (_session is null)
            {
                return;
            }

            UiGuard.Run(_session, "수치 조절", () =>
            {
                _session.Editor.SetPresentationCommandArgument(
                    presentationNodeId, commandId, argumentName, value);
                ManipulationApplied?.Invoke();
            });
        };

    private Control BuildMotionSlider(
        ArgumentSink write,
        string label,
        string argumentName,
        double value,
        double minimum,
        double maximum,
        double tick,
        Func<double, string> format,
        Func<double, string> token)
    {
        double snapped = Math.Clamp(Math.Round(value / tick) * tick, minimum, maximum);

        var valueLabel = new TextBlock
        {
            Text = format(snapped),
            FontSize = 10,
            Width = 66,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var slider = new Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = snapped,
            TickFrequency = tick,
            IsSnapToTickEnabled = true,
            Width = 140,
            VerticalAlignment = VerticalAlignment.Center
        };

        slider.ValueChanged += (_, args) => valueLabel.Text = format(args.NewValue);

        // 확정만 편집이다 — 끄는 도중의 중간값은 프로젝트에 닿지 않는다.
        void Commit() => write(argumentName, token(slider.Value));

        slider.PointerCaptureLost += (_, _) => Commit();
        slider.KeyUp += (_, _) => Commit();

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 10,
            Opacity = 0.7,
            Width = 30,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(labelText, 0);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(valueLabel, 2);
        row.Children.Add(labelText);
        row.Children.Add(slider);
        row.Children.Add(valueLabel);

        return row;
    }

    /// <summary>슬롯 표시/숨김 — 같은 라인의 반대 방향 fade는 걷어내고 원하는 쪽만 남는다.</summary>
    private void ApplySlotVisibility(string slotKey, bool visible)
    {
        if (_session is null || _request?.EditContext is not { Editable: true } context)
        {
            return;
        }

        UiGuard.Run(_session, "무대 표시 조작", () =>
        {
            PresentationStageActions.ApplyVisibility(
                _session.Editor,
                ManipulationCatalog,
                context.PresentationNodeId,
                context.LineId!,
                slotKey,
                visible);
            ManipulationApplied?.Invoke();
        });
    }

    private static IReadOnlyDictionary<string, string> StageArgs(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    /// <summary>
    /// 적용해도 닫히지 않는 조작 콘솔의 공통 틀 (소유자 지시 2026-08-06).
    /// 조작 하나가 끝나면 내용만 현재 상태로 다시 그려지고, <b>우클릭·✕·바깥 클릭이 닫는다</b>.
    ///
    /// Flyout이 아니라 위치 고정 Popup이다 (W37): 탭 전환·내용 갱신으로 크기가 바뀌어도
    /// 자리가 튀지 않고(좌상단 고정 + 아래·오른쪽으로만 자람), <b>윗부분 핸들을 잡아
    /// 원하는 위치로 끌 수 있다</b>. 끌어 둔 위치는 다음에 열 때도 유지된다.
    /// </summary>
    private void ShowManipulationFlyout(Action<StackPanel, Action> build)
    {
        var content = new StackPanel { Spacing = 6 };

        void Rebuild()
        {
            content.Children.Clear();
            UiGuard.Run(_session, "조절창 갱신", () => build(content, Rebuild));
        }

        // ── 헤더 = 드래그 핸들 + 닫기 ──
        var title = new TextBlock
        {
            Text = "⠿ 조절창 — 끌어서 이동 · 우클릭 닫기",
            FontSize = 10,
            Opacity = 0.8,
            VerticalAlignment = VerticalAlignment.Center
        };

        var closeButton = new Button { Content = "✕", FontSize = 10, Padding = new Thickness(6, 1) };
        closeButton.Click += (_, _) => CloseConsole();

        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(title, 0);
        Grid.SetColumn(closeButton, 1);
        headerGrid.Children.Add(title);
        headerGrid.Children.Add(closeButton);

        var handle = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 214, 218, 228)),
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            Padding = new Thickness(8, 4),
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Child = headerGrid
        };

        bool dragging = false;
        Point dragStart = default;
        (double X, double Y) dragBase = default;

        handle.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
            {
                dragging = true;
                dragStart = args.GetPosition(this);
                dragBase = (_consolePopup.HorizontalOffset, _consolePopup.VerticalOffset);
                args.Pointer.Capture(handle);
                args.Handled = true;
            }
        };
        handle.PointerMoved += (_, args) =>
        {
            if (dragging)
            {
                Point position = args.GetPosition(this);
                _consolePopup.HorizontalOffset = dragBase.X + (position.X - dragStart.X);
                _consolePopup.VerticalOffset = dragBase.Y + (position.Y - dragStart.Y);
                _consoleUserPosition = new Point(_consolePopup.HorizontalOffset, _consolePopup.VerticalOffset);
            }
        };
        handle.PointerReleased += (_, args) =>
        {
            dragging = false;
            args.Pointer.Capture(null);
        };

        // 밝은 패널 — 앱 테마의 기본 글자색(검정)이 그대로 읽힌다. 검은 무대 위에서도 또렷하다.
        // 내용은 높이 상한 안에서만 자란다: 위(핸들·탭)는 고정, 아래만 내용 길이대로 늘고,
        // 상한을 넘으면 안에서 스크롤된다 — 화면 끝에서 위로 밀려 높낮이가 튀는 일이 없다 (W38).
        var root = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(250, 244, 246, 250)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(140, 90, 96, 110)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            MaxWidth = 340,
            Child = new StackPanel
            {
                Children =
                {
                    handle,
                    new ScrollViewer
                    {
                        MaxHeight = 440,
                        HorizontalScrollBarVisibility =
                            Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                        Content = new Border { Padding = new Thickness(8), Child = content }
                    }
                }
            }
        };

        AttachConsoleCloseGestures(root);

        // ⚠ 열려 있으면 <b>먼저 닫는다</b>. 라이트 디스미스를 끈 뒤로 바깥 클릭이 팝업을
        // 닫지 않고 곧장 여기로 오므로 <b>열린 팝업의 Child를 갈아 끼우는</b> 길이 생겼는데,
        // Avalonia는 그 교체를 온전히 반영하지 않아 옛 판과 새 판이 섞인다(2026-08-22
        // 소유자 보고). 닫았다 여는 것이 유일하게 정의된 경로다.
        if (_consolePopup.IsOpen)
        {
            _consolePopup.IsOpen = false;
        }

        // ⚠ 이 팝업은 <b>무대 조절창이 아니다</b> (2026-08-22 이후) — 조절창은 오른쪽
        // 붙박이 기둥으로 갔고, 여기 남은 것은 값 시뮬처럼 무대 위에 잠깐 뜨는 판이다.
        // 그래서 `_consoleRebuild`(붙박이 판의 손잡이)를 건드리지 않는다.

        // 끌어 둔 자리 우선, 처음이면 클릭한 지점.
        Point openAt = _consoleUserPosition ?? _consoleOpenAt;
        _consolePopup.HorizontalOffset = openAt.X;
        _consolePopup.VerticalOffset = openAt.Y;
        _consolePopup.Child = root;

        Rebuild();
        _consolePopup.IsOpen = true;
    }

    private void CloseConsole()
    {
        _consolePopup.IsOpen = false;
        _consolePopup.Child = null;
    }

    /// <summary>
    /// 우클릭 = 닫기 (헤더가 광고하는 두 길 중 하나) — 세 갈래로 잡는다.
    ///
    /// ⚠ 2026-08-22에 소유자가 <b>두 번</b> "우클릭 닫기가 안 된다"고 보고했다. 처음엔
    /// 버블 핸들러라 판 안쪽 컨트롤(탭 머리·내용 컨테이너·글자 칸)이 먼저 삼켰고, 터널로
    /// 옮긴 뒤에도 실기에서 안 닫혔다. 헤드리스로는 재현되지 않는다(팝업이 창 안 오버레이로
    /// 서지만 실기 Windows에서는 <b>별도 네이티브 팝업 창</b>이라 입력 경로가 다르다).
    ///
    /// 그래서 <b>한 경로에 걸지 않는다</b>. 셋 다 같은 뜻이고 닫기는 멱등이다:
    /// ① 누름(터널·이미 처리된 것도) ② 뗌(누름이 플랫폼에서 삼켜지는 경우) ③
    /// <see cref="Control.ContextRequestedEvent"/>(우클릭의 정식 이름 — 버튼 상태가 아니라
    /// "여기서 맥락 메뉴를 원한다"는 뜻이라 누름·뗌을 어떻게 소비하든 온다).
    ///
    /// 대가로 판 안 글자 칸의 기본 우클릭 메뉴를 잃는다. 이 판에서 우클릭의 뜻은 하나로
    /// 정해 두는 편이 낫다.
    /// </summary>
    private void AttachConsoleCloseGestures(Control root)
    {
        void CloseIfRight(PointerEventArgs args)
        {
            PointerPointProperties properties = args.GetCurrentPoint(root).Properties;

            // 누름은 버튼 상태로, 뗌은 UpdateKind로 본다 — 뗀 뒤에는 눌린 버튼이 없다.
            if (properties.IsRightButtonPressed ||
                properties.PointerUpdateKind == PointerUpdateKind.RightButtonReleased)
            {
                args.Handled = true;
                CloseConsole();
            }
        }

        root.AddHandler(
            InputElement.PointerPressedEvent,
            (object? _, PointerPressedEventArgs args) => CloseIfRight(args),
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        root.AddHandler(
            InputElement.PointerReleasedEvent,
            (object? _, PointerReleasedEventArgs args) => CloseIfRight(args),
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        root.AddHandler(
            Control.ContextRequestedEvent,
            (_, args) =>
            {
                args.Handled = true;
                CloseConsole();
            },
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>
    /// 캐릭터(또는 빈 슬롯) 클릭 — <b>오른쪽 조절창</b>을 그 슬롯의 [캐릭터] 탭으로 돌린다.
    /// 별도 팝오버가 아니라 같은 조절창 하나다 (소유자 지시 2026-08-06 2차). 판이 붙박이가
    /// 된 2026-08-22 이후로는 여는 것이 아니라 <b>가리키는 것</b>이 됐다.
    /// </summary>
    private void ShowCharacterPopover(string slotKey)
    {
        if (_session is null)
        {
            return;
        }

        _popoverSlotKey = slotKey; // 전역 선택도 이 슬롯을 따라간다
        _popoverTabIndex = CharacterTabIndex;
        _consoleRebuild?.Invoke();
    }

    /// <summary>
    /// 캐릭터 탭 — 캐릭터 클릭 화면 그대로: 표정 교체(스프라이트)·variant(pose)·
    /// 위치(place)·깊이(size)·미러. 전부 이 라인의 커맨드가 되고, 같은 대상의 재조작은
    /// 값만 바뀐다. 표정은 보이는 캐릭터면 face_swap, 캐스팅만 된 캐릭터면 face다.
    /// 캐스팅은 [슬롯] 탭, 등장/퇴장은 탭 아래 전역 줄이 담당한다.
    /// </summary>
    private Control BuildCharacterTab(Action onApplied)
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 0), MinWidth = 210 };
        MiniStageSlot? slot = null;

        if (_session is null || _popoverSlotKey is not { } slotKey ||
            _request?.State.Slots.TryGetValue(slotKey, out slot) != true || slot is null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "조작할 슬롯이 없습니다. 상단 [+]로 추가하세요.",
                FontSize = 10,
                Opacity = 0.65
            });
            return panel;
        }

        PreviewAssetLibrary library = _session.AssetLibrary;

        void Commit(string outputCommand, params (string Key, string Value)[] args)
        {
            ApplyStageCommand(
                outputCommand,
                args.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
            onApplied();
        }

        if (slot.CharacterId is { } characterId)
        {
            // 보이면 face_swap(파라미터 emotion), 캐스팅만 됐으면 face(파라미터 emotionKey).
            string faceOutput = slot.Visible ? "face_swap" : "face";
            string faceParameter = slot.Visible ? "emotion" : "emotionKey";

            // 표정 그리드 — 이 캐릭터의 현재 variant가 가진 emotion 썸네일.
            string variant = string.IsNullOrWhiteSpace(slot.VariantKey) ? PortraitKey.DefaultVariantKey : slot.VariantKey;
            PortraitAssetEntry[] emotions = library.PortraitEntries
                .Where(entry => string.Equals(entry.Key.CharacterId, characterId, StringComparison.Ordinal) &&
                    string.Equals(entry.Key.VariantKey, variant, StringComparison.Ordinal) &&
                    entry.FileExists)
                .OrderBy(entry => entry.Key.EmotionKey, StringComparer.Ordinal)
                .ToArray();

            // 표정 섹션은 <b>항상</b> 있다. 썸네일이 없다고 스프라이트 교체 자체가 사라지면,
            // 에셋을 아직 안 넣은 사람에게는 기능이 없는 것과 같다.
            panel.Children.Add(new TextBlock { Text = "표정 (스프라이트 교체)", FontSize = 10, Opacity = 0.6 });

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

                    string emotionKey = entry.Key.EmotionKey;
                    bool current = string.Equals(emotionKey, slot.EmotionKey, StringComparison.Ordinal);

                    var cellButton = new Button
                    {
                        Content = cell,
                        Padding = new Thickness(2),
                        BorderThickness = new Thickness(current ? 2 : 0),
                        BorderBrush = current ? SpeakerHighlight : Brushes.Transparent
                    };
                    cellButton.Click += (_, _) => Commit(faceOutput, ("slot", slotKey), (faceParameter, emotionKey));
                    grid.Children.Add(cellButton);
                }

                panel.Children.Add(grid);
            }
            else
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"'{characterId}'의 초상화 파일을 찾지 못했습니다. 키를 직접 적으면 커맨드는 그대로 나갑니다.",
                    FontSize = 9,
                    Opacity = 0.55,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 240
                });
            }

            // 썸네일에 없는 표정 키도 쓸 수 있어야 한다. 비슷한 이름을 추측해 고쳐 주지는 않는다(원칙 §2.3).
            var emotionInput = new TextBox
            {
                PlaceholderText = "표정 키 직접 입력 후 Enter",
                FontSize = 10,
                MinHeight = 24
            };
            emotionInput.KeyDown += (_, keyArgs) =>
            {
                if (keyArgs.Key == Key.Enter && !string.IsNullOrWhiteSpace(emotionInput.Text))
                {
                    Commit(faceOutput, ("slot", slotKey), (faceParameter, emotionInput.Text.Trim()));
                }
            };
            panel.Children.Add(emotionInput);

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

        // 위치 이동은 캐스팅 여부와 무관하다 — 빈 슬롯도 자리를 정할 수 있다.
        panel.Children.Add(new TextBlock { Text = "위치 (place)", FontSize = 10, Opacity = 0.6 });
        panel.Children.Add(BuildScreenPointGrid(() => slotKey, onApplied));

        panel.Children.Add(new TextBlock { Text = "깊이 (size)", FontSize = 10, Opacity = 0.6 });
        panel.Children.Add(BuildDepthRow(() => slotKey, onApplied));

        if (PlaceApproximationNote() is { } characterPlaceNote)
        {
            panel.Children.Add(characterPlaceNote);
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

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

        return panel;
    }

    /// <summary>
    /// 등장/퇴장 버튼 줄 — 우측 정렬 + 구분 색(등장 초록·퇴장 붉음). 조절창 하단의 전역
    /// 줄이고, <b>슬롯을 겨누는 탭에서만</b> 선다(<see cref="HidesVisibilityRow"/>).
    /// 반대 방향 fade는 걷히고 원하는 쪽만 남는다.
    /// </summary>
    private Control BuildVisibilityRow(string slotKey, Action onApplied)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0)
        };

        var fadeIn = new Button
        {
            Content = "등장 (fade_in)",
            FontSize = 10,
            Padding = new Thickness(8, 3),
            Background = new SolidColorBrush(Color.FromArgb(60, 34, 197, 94))
        };
        fadeIn.Click += (_, _) =>
        {
            ApplySlotVisibility(slotKey, visible: true);
            onApplied();
        };
        row.Children.Add(fadeIn);

        var fadeOut = new Button
        {
            Content = "퇴장 (fade_out)",
            FontSize = 10,
            Padding = new Thickness(8, 3),
            Background = new SolidColorBrush(Color.FromArgb(60, 220, 38, 38))
        };
        fadeOut.Click += (_, _) =>
        {
            ApplySlotVisibility(slotKey, visible: false);
            onApplied();
        };
        row.Children.Add(fadeOut);

        return row;
    }

    /// <summary>
    /// 조절창 본문 — 전역 슬롯 헤더(추가 + 선택) 위에 [★ 자주 쓰는]·배경·슬롯·캐릭터·
    /// 오디오 다섯 탭. 전부 정식 커맨드가 된다: 배경·위치·깊이·표시 상태는 선택된 라인의
    /// 바인딩에, <b>슬롯 생성·위치(무대/레이어)·캐스팅은 노드 Setup에</b>(장면 준비는
    /// 라인이 아니라 노드에 속한다). 프리뷰만 임시로 바꾸는 경로는 없다.
    ///
    /// 2026-08-22부터 <b>오른쪽 붙박이 기둥</b>이 이 판을 든다(<see cref="BuildDockedConsole"/>).
    /// </summary>
    private void BuildStagePopover(StackPanel host, Action rebuild)
    {
        if (_session is null || _request is null)
        {
            return;
        }


        string[] slots = _request.State.Slots.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();

        if (_popoverSlotKey is null || !_request.State.Slots.ContainsKey(_popoverSlotKey))
        {
            _popoverSlotKey = slots.FirstOrDefault();
        }

        // ── 전역: 새 슬롯 추가 — 컴팩트 한 줄 (이름 + [+]) ──
        string suggestedKey = NextFreeSlotKey(_request.State);
        var addRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var keyInput = new TextBox
        {
            PlaceholderText = suggestedKey,
            FontSize = 11,
            MinHeight = 24
        };
        ToolTip.SetTip(keyInput, "새 슬롯 이름. 비워 두면 제안된 이름으로 만듭니다.");
        var addButton = new Button
        {
            Content = "+",
            FontSize = 13,
            Width = 30,
            Padding = new Thickness(0, 1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        ToolTip.SetTip(addButton, "슬롯 추가 (노드 Setup에 기록)");

        void AddSlot()
        {
            string key = string.IsNullOrWhiteSpace(keyInput.Text) ? suggestedKey : keyInput.Text.Trim();
            ApplySetupCommand("slot", ("slotKey", key));
            _popoverSlotKey = key;
            rebuild();
        }

        addButton.Click += (_, _) => AddSlot();
        keyInput.KeyDown += (_, keyArgs) =>
        {
            if (keyArgs.Key == Key.Enter)
            {
                AddSlot();
            }
        };

        Grid.SetColumn(keyInput, 0);
        Grid.SetColumn(addButton, 1);
        addRow.Children.Add(keyInput);
        addRow.Children.Add(addButton);
        host.Children.Add(addRow);

        // ── 전역: 슬롯 선택 + 현재 선택 표시 ──
        if (slots.Length == 0)
        {
            host.Children.Add(new TextBlock
            {
                Text = "슬롯이 없습니다 — 위 [+]로 추가하세요. (우클릭으로 닫기)",
                FontSize = 10,
                Opacity = 0.65
            });
        }
        else
        {
            var slotCombo = new ComboBox
            {
                ItemsSource = slots
                    .Select(key => _request.State.Slots[key].CharacterId is { } cast
                        ? $"{key} · {cast}"
                        : $"{key} (캐스팅 없음)")
                    .ToArray(),
                SelectedIndex = Math.Max(0, Array.IndexOf(slots, _popoverSlotKey)),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            slotCombo.SelectionChanged += (_, _) =>
            {
                if (slotCombo.SelectedIndex >= 0 && slotCombo.SelectedIndex < slots.Length &&
                    !string.Equals(slots[slotCombo.SelectedIndex], _popoverSlotKey, StringComparison.Ordinal))
                {
                    _popoverSlotKey = slots[slotCombo.SelectedIndex];
                    rebuild();
                }
            };
            host.Children.Add(slotCombo);
            // 현재 조작 중인 슬롯 표시는 위 콤보가 이미 한다 — 별도 칩은 소유자 지시로 제거 (W47).
        }

        host.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(60, 148, 163, 184))
        });

        // ── 탭 — 적용 후 재구성돼도 보던 탭이 유지된다 ──
        var tabs = new TabControl { MinWidth = 270 };

        // [자주 쓰는]이 맨 앞이자 기본 탭이다 (2026-08-22 소유자) — 나머지 네 탭이 "누가·
        // 어디·어떤 표정"을 명사로 말하는 동안, 시간·리액션·카메라·화면은 126종 목록으로
        // 나가야 했다. 그 넷을 칩 한 줄로 당겨 온 자리다.
        // ⚠ 순서가 규격이다 (2026-08-24 소유자) — 자주 쓰는 · 슬롯 · 캐릭터 · 배경 · 오디오.
        //   슬롯·캐릭터가 앞에 붙은 이유는 <b>둘이 한 손놀림</b>이기 때문이다: 슬롯을 세우고
        //   곧바로 그 슬롯의 캐릭터를 만진다. 배경·오디오는 그 뒤에 한 번씩 손대는 것이다.
        //   자리를 옮길 때는 아래 자리 상수(QuickTabIndex 등)를 함께 옮긴다.
        tabs.Items.Add(new TabItem
        {
            Header = new TextBlock { Text = "★ 자주 쓰는", FontSize = 11 },
            Content = BuildQuickTab(rebuild)
        });
        tabs.Items.Add(new TabItem
        {
            Header = new TextBlock { Text = "슬롯", FontSize = 11 },
            Content = BuildSlotTab(rebuild)
        });
        tabs.Items.Add(new TabItem
        {
            Header = new TextBlock { Text = "캐릭터", FontSize = 11 },
            Content = BuildCharacterTab(rebuild)
        });
        tabs.Items.Add(new TabItem
        {
            Header = new TextBlock { Text = "배경", FontSize = 11 },
            Content = BuildBackgroundTab(rebuild)
        });
        // [이동] 탭은 없다 (2026-08-21 소유자: "이동도 결국 커맨드 하나일 뿐") —
        // 이동 편집은 터미널에서 그 커맨드를 고르는 정식 경로 하나로 간다.
        tabs.Items.Add(new TabItem
        {
            Header = new TextBlock { Text = "오디오", FontSize = 11 },
            Content = BuildAudioTab(rebuild)
        });

        tabs.SelectedIndex = Math.Clamp(_popoverTabIndex, 0, tabs.Items.Count - 1);

        host.Children.Add(tabs);

        // ── 등장/퇴장 — 슬롯을 만지는 탭에서만 서는 전역 줄 (선택 슬롯 대상).
        Control? visibilityRow = _popoverSlotKey is { } selectedSlotKey
            ? BuildVisibilityRow(selectedSlotKey, rebuild)
            : null;

        if (visibilityRow is not null)
        {
            visibilityRow.IsVisible = !HidesVisibilityRow(tabs.SelectedIndex);
            host.Children.Add(visibilityRow);
        }

        tabs.SelectionChanged += (_, _) =>
        {
            if (tabs.SelectedIndex >= 0)
            {
                _popoverTabIndex = tabs.SelectedIndex;
            }

            if (visibilityRow is not null)
            {
                visibilityRow.IsVisible = !HidesVisibilityRow(tabs.SelectedIndex);
            }
        };
    }

    // ── 탭의 자리 ───────────────────────────────────────────────────────────
    //
    // 번호가 아니라 이름으로 가리킨다 — 순서는 소유자가 옮기는 것이고, 그때 코드가
    // 조용히 엉뚱한 탭을 가리키면 안 된다. 순서를 바꿀 때 함께 고치는 자리는 여기뿐이다.

    private const int QuickTabIndex = 0;
    private const int CharacterTabIndex = 2;
    private const int BackgroundTabIndex = 3;

    /// <summary>
    /// 등장/퇴장 줄을 숨기는 탭인가.
    ///
    /// <b>배경</b> — 배경은 슬롯이 아니다 (소유자 지시 2026-08-06 2차).
    /// <b>자주 쓰는</b> — 그 판의 칩은 <b>제 대상을 자기가 들고 있다</b> (2026-08-24 소유자).
    /// 아래에 선 등장/퇴장은 위 콤보의 슬롯을 겨누므로, 칩을 누르는 손과 다른 대상을
    /// 가리키는 단추 둘이 한 화면에 서서 <b>같은 것처럼 보인다</b>. 슬롯을 겨누는 일은
    /// [슬롯]·[캐릭터] 탭의 것이고, 등장/퇴장도 거기 있으면 된다.
    /// </summary>
    private static bool HidesVisibilityRow(int tabIndex) =>
        tabIndex is QuickTabIndex or BackgroundTabIndex;

    // ── [자주 쓰는] 탭 (2026-08-22) ─────────────────────────────────────────

    /// <summary>
    /// <b>두 구역의 편집은 따로 켠다</b> (2026-08-24 3차 소유자: "개별 커맨드와 묶음 커맨드의
    /// 조작을 분리하는게 좋겠어").
    ///
    /// 하나였을 때 <c>[＋ 묶음]</c>이 그 하나를 켜는 바람에, 묶음을 만들려던 사람의 눈앞에서
    /// <b>커맨드 구역까지 통째로 편집 줄로 바뀌었다</b> — 만지지도 않을 판이 모양을 바꾸는 것은
    /// 그 자체로 소음이다. 두 구역은 손잡이도 어휘도 다르다(단추의 이름·수치 ↔ 표의 순서·단계).
    /// </summary>
    private bool _commandEditMode;

    /// <inheritdoc cref="_commandEditMode"/>
    private bool _bundleEditMode;

    /// <summary>
    /// 검증용 손잡이 — 조절창 팝업을 띄우지 않고 [자주 쓰는] 판만 짓는다. 헤드리스에서는
    /// <see cref="Popup"/>이 뜨지 않아 칩을 눌러 볼 길이 이것뿐이다.
    /// </summary>
    internal Control BuildQuickTabProbe(string? slotKey, Action? onApplied = null)
    {
        _popoverSlotKey = slotKey;
        return BuildQuickTab(onApplied ?? (() => { }));
    }

    /// <summary>검증용 손잡이 — [커맨드] 구역의 편집을 켠다(평소에는 그 구역의 [편집]이 켠다).</summary>
    internal void SetCommandEditModeProbe(bool enabled) => SetCommandEditMode(enabled);

    /// <summary>검증용 손잡이 — [묶음] 구역의 편집을 켠다.</summary>
    internal void SetBundleEditModeProbe(bool enabled) => SetBundleEditMode(enabled);

    /// <summary>
    /// 검증용 손잡이 — 조절창 본문(슬롯 헤더 + 탭 + 등장/퇴장)을 짓는다.
    /// 탭 <b>순서</b>가 자리 상수들(<see cref="QuickTabIndex"/>·<see cref="CharacterTabIndex"/>·
    /// <see cref="BackgroundTabIndex"/>)의 근거라 눈이 아니라 테스트가 지킨다.
    /// </summary>
    internal Control BuildStagePopoverProbe()
    {
        var host = new StackPanel();
        BuildStagePopover(host, () => { });
        return host;
    }

    /// <summary>검증용 손잡이 — 초상 클릭과 같은 길로 조절창을 그 슬롯의 [캐릭터] 탭으로 돌린다.</summary>
    internal void PointConsoleAtSlotProbe(string slotKey) => ShowCharacterPopover(slotKey);

    /// <summary>
    /// 자주 쓰는 칩 판 — 누르면 <b>지금 고른 라인</b>에 그 커맨드가 붙는다. 대상 슬롯은
    /// 칩이 아니라 위 콤보가 정한다(<see cref="StageQuickCommand"/> 주석).
    ///
    /// 같은 커맨드를 같은 대상에 다시 누르면 값만 바뀐다 — 조절창 전체를 관통하는 규칙
    /// (<see cref="PresentationStageActions.Apply"/>)을 칩만 예외로 두지 않는다. 그래서
    /// [흔들기] 뒤에 [끄덕임]은 gesture 두 개가 아니라 "이 라인의 몸짓은 끄덕임"이 된다.
    /// </summary>
    private Control BuildQuickTab(Action onApplied)
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 0), MinWidth = 250 };

        if (_session is null)
        {
            return panel;
        }

        PresentationCommandCatalog catalog = ManipulationCatalog;
        IReadOnlyList<StageQuickCommand> chips = _session.Project.EffectiveQuickCommands;

        // ── 머리 줄: 안내 한 줄 ──
        //    [편집]은 여기 없다 (2026-08-24 3차) — 구역마다 따로 켜므로 구역 제목 옆이
        //    제 자리다. 이 줄이 답하는 것은 <b>터미널 클릭이 어디로 가나</b> 하나이고,
        //    그건 두 구역에 걸친 사실이라 위에 남는다.
        panel.Children.Add(new TextBlock
        {
            Text = QuickHeaderText(chips),
            FontSize = 10,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap
        });

        // ── 구역 둘 (2026-08-24 소유자: "칩커맨드와 단일 커맨드가 구별되도록 영역을 구분")
        //
        // 데이터는 한 목록이고 <b>자리(index)는 그대로 쓴다</b> — 구역은 표시일 뿐이라
        // 나누면서 번호를 다시 매기면 펼침·편집이 가리키는 칩이 어긋난다.
        var single = new List<int>();
        var bundles = new List<int>();

        for (int index = 0; index < chips.Count; index++)
        {
            StageQuickCommand chip = chips[index];

            // 빈 그릇은 묶음 편집 중에만 보인다 — 평소에 누를 것이 없는 칩을 세우지 않는다.
            if (chip.Steps.Count == 0)
            {
                if (_bundleEditMode)
                {
                    bundles.Add(index);
                }

                continue;
            }

            // 단계가 하나도 이 게임 정의에 없으면 조용히 빠진다 — 눌러도 예외가 날 칩을
            // 그리지 않는다(기존 규칙). 일부만 없는 묶음은 <b>회색으로 서서 말한다</b>:
            // 조용히 나머지만 실행하면 사람은 다 붙은 줄 안다.
            if (!chip.Steps.Any(step => catalog.Find(step.DefinitionId) is not null))
            {
                continue;
            }

            (chip.Steps.Count > 1 ? bundles : single).Add(index);
        }

        // ── 커맨드 구역 (하나짜리 칩) ──
        AddQuickHeading(panel, "커맨드", "커맨드 하나짜리 칩입니다.", BuildCommandEditToggle(onApplied));

        if (single.Count > 0)
        {
            Panel pad = _commandEditMode ? new StackPanel { Spacing = 3 } : new WrapPanel { MaxWidth = 260 };

            foreach (int index in single)
            {
                pad.Children.Add(_commandEditMode
                    ? BuildQuickChipEditRow(chips[index], index, onApplied)
                    : BuildQuickChip(chips[index], onApplied));
            }

            panel.Children.Add(pad);
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = chips.Count == 0
                    ? "칩이 없습니다. [편집]을 누르고 터미널의 커맨드를 클릭해 담으세요."
                    : "커맨드 칩이 없습니다.",
                FontSize = 10,
                Opacity = 0.55,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 250
            });
        }

        // ── 구분선 (2026-08-24 소유자: "두 종류의 프리셋 사이에 구분선") ──
        //    두 구역은 <b>누르는 감각이 다르다</b> — 하나는 단추, 하나는 표다.
        //    제목만으로는 그 경계가 목록의 리듬에 묻힌다.
        panel.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(0, 9, 0, 3),
            Background = new SolidColorBrush(Color.FromArgb(60, 148, 163, 184))
        });

        // ── 묶음 구역 (표) ──
        //    ⚠ 이 구역은 <b>비어도 선다</b> — [＋ 묶음]이 여기 있기 때문이다.
        //    만들 입구가 목록이 비었다는 이유로 사라지면 첫 묶음을 만들 길이 없다.
        AddQuickHeading(panel, "묶음", "누르면 담긴 순서대로 전부 붙습니다.", BuildBundleEditToggle(onApplied));

        foreach (int index in bundles)
        {
            panel.Children.Add(BuildQuickBundleCard(chips[index], index, onApplied));
        }

        if (bundles.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "묶음이 없습니다. [＋ 묶음]으로 만들고 터미널의 커맨드를 골라 담으세요.",
                FontSize = 10,
                Opacity = 0.55,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 250
            });
        }

        panel.Children.Add(BuildAddBundleButton(onApplied));

        // [기본값 복원]은 목록 <b>전체</b>를 되돌린다 — 구역 하나의 일이 아니므로 맨 아래에
        // 두고, 어느 쪽이든 편집 중일 때만 보인다.
        if (IsQuickPinMode)
        {
            var reset = new Button
            {
                Content = "기본값 복원",
                FontSize = 10,
                Padding = new Thickness(7, 2),
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                IsEnabled = _session.Project.QuickCommands is not null
            };
            ToolTip.SetTip(reset, "커맨드·묶음을 통째로 기본 목록으로 되돌립니다.");
            reset.Click += (_, _) =>
            {
                UiGuard.Run(_session, "자주 쓰는 기본값 복원", () => _session.Editor.ResetQuickCommands());
                onApplied();
            };
            panel.Children.Add(reset);
        }

        return panel;
    }

    /// <summary>
    /// 구역 제목 + 그 구역만의 [편집] (2026-08-24 소유자: "칩커맨드와 단일 커맨드가 구별되도록
    /// 둘의 영역을 구분" · 3차: "개별 커맨드와 묶음 커맨드의 조작을 분리").
    /// </summary>
    private static void AddQuickHeading(StackPanel panel, string title, string hint, Control toggle)
    {
        var heading = new TextBlock
        {
            Text = title,
            FontSize = 9,
            Opacity = 0.5,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(heading, hint);

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(1, 2, 0, 2)
        };
        Grid.SetColumn(heading, 0);
        Grid.SetColumn(toggle, 1);
        row.Children.Add(heading);
        row.Children.Add(toggle);
        panel.Children.Add(row);
    }

    /// <summary>[커맨드] 구역의 [편집] — 이름 칸·✕·수치 조절을 켠다.</summary>
    private Control BuildCommandEditToggle(Action onApplied)
    {
        var toggle = new Button
        {
            Content = _commandEditMode ? "완료" : "편집",
            FontSize = 10,
            Padding = new Thickness(7, 1)
        };
        ToolTip.SetTip(toggle, _commandEditMode
            ? "커맨드 칩 편집을 마칩니다."
            : "커맨드 칩의 이름·수치를 고치고, 터미널에서 새 칩을 담습니다.");
        toggle.Click += (_, _) =>
        {
            SetCommandEditMode(!_commandEditMode);
            onApplied();
        };
        return toggle;
    }

    /// <summary>
    /// [묶음] 구역의 [편집] — 표의 손잡이(▲▼✕)·이름 칸·담을 대상 지정을 켠다.
    ///
    /// [완료]는 만들다 만 <b>빈 그릇을 걷는다</b> — 누를 것이 없는 칩을 저장물에 남기지
    /// 않는다. 켤 때가 아니라 끌 때인 이유는 담는 도중에 잠깐 비는 것이 정상이기 때문이다.
    /// </summary>
    private Control BuildBundleEditToggle(Action onApplied)
    {
        var toggle = new Button
        {
            Content = _bundleEditMode ? "완료" : "편집",
            FontSize = 10,
            Padding = new Thickness(7, 1)
        };
        ToolTip.SetTip(toggle, _bundleEditMode
            ? "묶음 편집을 마칩니다 — 빈 묶음은 걷힙니다."
            : "묶음의 단계 순서·수치를 고치고, 터미널에서 골라 담습니다.");
        toggle.Click += (_, _) =>
        {
            if (_bundleEditMode)
            {
                UiGuard.Run(_session, "빈 묶음 정리", () => _session!.Editor.RemoveEmptyQuickCommands());
            }

            SetBundleEditMode(!_bundleEditMode);
            onApplied();
        };
        return toggle;
    }

    /// <summary>
    /// [＋ 묶음] — 빈 그릇을 만들고 담기를 시작한다.
    ///
    /// <b>묶음 구역 맨 아래에 선다</b> (2026-08-24 2차 소유자: "+묶음은 묶음 커맨드 전용의
    /// 것이니 아래쪽으로"). 머리 줄에 있을 때는 [편집]과 나란해서 <b>탭 전체의 손잡이</b>처럼
    /// 보였는데, 실제로는 묶음 하나만 만드는 단추다. 목록 끝에 서면 "여기에 하나 더"가 된다.
    ///
    /// 평소 모드에서도 눌린다 — 누르면 편집이 함께 켜진다. "만들려면 먼저 편집을 켜라"는
    /// 한 단계는 순수한 통행세다.
    /// </summary>
    private Control BuildAddBundleButton(Action onApplied)
    {
        var button = new Button
        {
            Content = "＋ 묶음",
            FontSize = 10,
            Padding = new Thickness(8, 3),
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        ToolTip.SetTip(button, "빈 묶음을 만들고 담기를 시작합니다 — 터미널의 커맨드를 클릭해 골라 담습니다.");

        button.Click += (_, _) =>
        {
            UiGuard.Run(_session, "묶음 만들기", () =>
            {
                int created = _session!.Editor.CreateQuickBundle();
                SetBundleEditMode(true);

                // 만든 그릇을 바로 펴 둔다 = 담을 대상이다. 만들자마자 담을 수 있어야
                // "만들기 → 고르기"가 한 흐름으로 이어진다.
                _bundlePinIndex = created;
                _bundleExpandedStep = null;
                _quickCollapsed.Remove(_session.Project.EffectiveQuickCommands[created].DisplayName);
            });

            onApplied();
            QuickEditModeChanged?.Invoke();
        };

        return button;
    }

    /// <summary>
    /// 지금 펼쳐 놓은 칩의 자리. 하나만 열린다 — 작업대와 같은 감각이다.
    ///
    /// <b>이름 칸이 초점을 받은 묶음이 담을 대상이다</b> (2026-08-24) — 터미널에서 클릭한
    /// 커맨드는 그 묶음 뒤에 붙는다. 담기 단추를 묶음마다 따로 두지 않은 이유는 그것이
    /// 손잡이를 하나 더 만들 뿐이기 때문이다: 이름을 만지고 있다는 것은 이미 "이 묶음을
    /// 지금 작업 중"이라는 뜻이다.
    /// </summary>
    private int? _bundlePinIndex;

    /// <summary>담을 대상 묶음 <b>안에서</b> 수치를 펴 놓은 단계. 하나만 열린다.</summary>
    private int? _bundleExpandedStep;

    /// <summary>[커맨드] 구역에서 수치를 펴 놓은 칩. 하나만 열린다 — 작업대와 같은 감각이다.</summary>
    private int? _commandExpandedIndex;

    /// <summary>
    /// 표를 접어 둔 묶음들 — <b>이름</b>으로 기억한다 (2026-08-24 소유자: "물론 접기도
    /// 가능해야하고").
    ///
    /// ⛔ 자리(index)로 기억하면 안 된다: 칩 하나를 빼는 순간 뒤가 밀려 <b>엉뚱한 묶음이
    /// 접힌다.</b> 이름으로 잡으면 개명했을 때 펼쳐지는데, 기본이 펼침이라 그건 손해가 아니다.
    ///
    /// 기본이 펼침인 이유는 소유자의 요구 자체다 — "편집때만이 아니라 언제든지 세부내역을
    /// 대략적으로라도 확인할 수 있도록." 접는 것은 시야를 아끼려는 사람의 선택이다.
    /// </summary>
    private readonly HashSet<string> _quickCollapsed = new(StringComparer.Ordinal);

    /// <summary>
    /// 지금 터미널 클릭이 담길 곳 — 담을 대상 묶음의 자리, 없으면 null(= 새 커맨드 칩).
    /// 터미널 쪽 배선(<c>MiniStagePreview</c>)이 이 하나만 보고 갈라진다.
    ///
    /// ⚠ <b>묶음 편집이 켜져 있을 때만</b> 답한다 — 커맨드 편집만 켜 놓고 담는 사람에게는
    /// 묶음이 목적지가 될 수 없다(그것이 두 조작을 가른 이유다).
    /// </summary>
    internal int? QuickPinTarget => _bundleEditMode ? _bundlePinIndex : null;

    /// <summary>검증용 손잡이 — 담을 대상 묶음을 정한다(평소에는 이름 칸의 초점이 정한다).</summary>
    internal void ExpandQuickChipProbe(int? index)
    {
        _bundlePinIndex = index;
        _bundleExpandedStep = null;
    }

    /// <summary>
    /// 머리 줄의 안내 — <b>지금 터미널 클릭이 어디로 가는지</b>를 말한다.
    /// 두 구역에 걸친 사실이라 구역 제목이 아니라 판 맨 위에 선다.
    /// </summary>
    private string QuickHeaderText(IReadOnlyList<StageQuickCommand> chips)
    {
        if (_bundleEditMode && _bundlePinIndex is { } index && index >= 0 && index < chips.Count)
        {
            return $"담는 중: '{chips[index].DisplayName}' — 터미널의 커맨드를 클릭하면 " +
                   $"{chips[index].Steps.Count + 1}번째 단계로 붙습니다.";
        }

        if (_bundleEditMode)
        {
            return "담을 묶음의 이름 칸을 누르면 그 묶음에 골라 담습니다. " +
                   "새로 만들려면 아래 [＋ 묶음]을 누르세요.";
        }

        if (_commandEditMode)
        {
            return "터미널의 커맨드를 클릭하면 커맨드 칩 하나로 담깁니다.";
        }

        return "누르면 이 라인에 붙습니다.";
    }

    /// <summary>
    /// [＋ 이 라인 전부] — 지금 라인의 커맨드를 순서대로 이 칩에 담는다 (2026-08-24).
    ///
    /// <b>골라 담기의 편의 기능</b>이지 담기의 정식 경로가 아니다. 정식은 터미널에서 원하는
    /// 것만 클릭하는 것이고, 이 단추는 "이 라인 것을 통째로"가 흔해서 다섯 번 클릭을 한 번으로
    /// 줄인다. 그래서 <b>펼친 칩 안에</b> 있다 — 어디에 담기는지가 자리로 보인다.
    ///
    /// 꺼진 커맨드는 담지 않는다 — 실행되지 않는 것을 담으면 칩이 화면과 다른 말을 한다.
    /// </summary>
    private Control BuildQuickLinePinButton(StageQuickCommand chip)
    {
        IReadOnlyList<PresentationResultCommand> lineCommands = SelectedLineCommands();

        var button = new Button
        {
            Content = lineCommands.Count == 0
                ? "＋ 이 라인 전부"
                : $"＋ 이 라인 전부 ({lineCommands.Count}개)",
            FontSize = 10,
            Padding = new Thickness(7, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 3, 0, 0),
            IsEnabled = lineCommands.Count > 0
        };

        ToolTip.SetTip(button, lineCommands.Count == 0
            ? "이 라인에는 담을 연출 커맨드가 없습니다."
            : $"이 라인의 커맨드 {lineCommands.Count}개를 '{chip.DisplayName}'에 순서대로 담습니다.");

        button.Click += (_, _) => QuickPinRequested?.Invoke(lineCommands);

        return button;
    }

    /// <summary>
    /// 담아 달라는 신호 — 커맨드 목록 하나가 칩 하나(또는 펼친 칩의 뒤 단계들)가 된다.
    /// 이름 짓기와 담을 곳 판정은 터미널 쪽 배선이 든다(<c>MiniStagePreview</c>) —
    /// 터미널 행 ★과 이 단추가 <b>같은 함수</b>로 흐르게 하려는 것이다.
    /// </summary>
    internal event Action<IReadOnlyList<PresentationResultCommand>>? QuickPinRequested;

    /// <summary>지금 고른 라인의 켜진 연출 커맨드 — 대본 패널이 그리는 것과 같은 행에서 읽는다.</summary>
    private IReadOnlyList<PresentationResultCommand> SelectedLineCommands()
    {
        if (_request is not { SelectedLineId: { } lineId, ScriptRows: { } rows })
        {
            return [];
        }

        return rows
            .Where(row =>
                row.Kind == PresentationScriptRowKind.Command &&
                row.IsEnabled &&
                row.Command is not null &&
                string.Equals(row.LineId, lineId, StringComparison.Ordinal))
            .Select(row => row.Command!)
            .ToList();
    }

    /// <summary>
    /// 묶음 한 장 — <b>엑셀의 표 모양</b>이다 (2026-08-24 소유자: "버튼으로 하는 대신 엑셀의
    /// 표와 같이해서 조금 더 직관적이고 정리되도록 … 편집때만이 아니라 언제든지 세부내역을
    /// 대략적으로라도 확인할 수 있도록").
    ///
    /// <b>평소와 편집이 같은 표다.</b> 단추였을 때는 무엇이 들었는지가 툴팁에만 있어서
    /// 손을 얹어야 보였고, 담긴 내용을 보려면 편집을 켜야 했다. 지금은 늘 보이고,
    /// 편집을 켜면 같은 표에 손잡이(▲▼✕)와 이름 칸이 붙을 뿐이다.
    ///
    /// <code>
    /// ▾ 퇴장 한 벌          ·2        [붙이기]
    ///   1  fade_out   c1 0.4s
    ///   2  pause      0.2
    /// </code>
    /// </summary>
    private Control BuildQuickBundleCard(StageQuickCommand chip, int index, Action onApplied)
    {
        bool collapsed = _quickCollapsed.Contains(chip.DisplayName);
        bool pinTarget = _bundleEditMode && _bundlePinIndex == index;

        var card = new StackPanel();
        var frame = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(pinTarget
                ? Color.FromArgb(150, 250, 204, 21)   // 담는 중인 그릇은 테두리가 말한다
                : Color.FromArgb(45, 148, 163, 184)),
            Background = new SolidColorBrush(Color.FromArgb((byte)(pinTarget ? 22 : 10), 148, 163, 184)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 4),
            Margin = new Thickness(0, 0, 0, 4),
            Child = card
        };

        card.Children.Add(BuildQuickBundleHeader(chip, index, collapsed, onApplied));

        if (collapsed)
        {
            return frame;
        }

        for (int stepIndex = 0; stepIndex < chip.Steps.Count; stepIndex++)
        {
            card.Children.Add(BuildQuickStepRow(chip, index, stepIndex, onApplied));
        }

        if (chip.Steps.Count == 0)
        {
            card.Children.Add(new TextBlock
            {
                Text = "빈 묶음입니다 — 터미널의 커맨드를 클릭해 골라 담으세요.",
                FontSize = 9,
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 3, 0, 0)
            });
        }

        if (!_bundleEditMode)
        {
            return frame;
        }

        card.Children.Add(new TextBlock
        {
            Text = pinTarget
                ? "터미널의 커맨드를 클릭하면 여기 맨 뒤로 붙습니다."
                : "이 묶음에 담으려면 이름을 눌러 담는 중으로 만드세요.",
            FontSize = 9,
            Opacity = 0.45,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 4, 0, 0)
        });

        if (pinTarget)
        {
            card.Children.Add(BuildQuickLinePinButton(chip));
        }

        return frame;
    }

    /// <summary>
    /// 표의 머리 줄 — [▾ 접기][이름][·N][붙이기 또는 ✕].
    ///
    /// 편집 중에는 이름이 <b>칸</b>이 되고, 그 칸을 누르는 것이 곧 "이 묶음에 담겠다"는
    /// 뜻이다(<see cref="_bundlePinIndex"/>) — 담기 대상을 고르는 손잡이를 따로 두지
    /// 않는다. 평소에는 [붙이기]가 그 자리를 쓴다.
    /// </summary>
    private Control BuildQuickBundleHeader(
        StageQuickCommand chip,
        int index,
        bool collapsed,
        Action onApplied)
    {
        var fold = new Button
        {
            Content = collapsed ? "▸" : "▾",
            FontSize = 9,
            Padding = new Thickness(4, 1),
            Margin = new Thickness(0, 0, 4, 0),
            Background = Brushes.Transparent
        };
        ToolTip.SetTip(fold, collapsed ? "펼칩니다." : "접습니다 — 머리 줄만 남습니다.");
        fold.Click += (_, _) =>
        {
            if (!_quickCollapsed.Remove(chip.DisplayName))
            {
                _quickCollapsed.Add(chip.DisplayName);
            }

            onApplied();
        };

        var count = new TextBlock
        {
            Text = chip.Steps.Count == 0 ? "비었음" : $"·{chip.Steps.Count}",
            FontSize = 9,
            Opacity = 0.55,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0, 5, 0)
        };

        Control name = _bundleEditMode
            ? BuildQuickBundleNameBox(chip, index, onApplied)
            : new TextBlock
            {
                Text = chip.DisplayName,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        Control[] cells = [fold, name, count, BuildQuickBundleAction(chip, index, onApplied)];

        for (int column = 0; column < cells.Length; column++)
        {
            Grid.SetColumn(cells[column], column);
            row.Children.Add(cells[column]);
        }

        return row;
    }

    /// <summary>머리 줄 오른쪽 — 평소에는 [붙이기], 편집 중에는 묶음째 빼는 ✕.</summary>
    private Control BuildQuickBundleAction(StageQuickCommand chip, int index, Action onApplied)
    {
        if (_bundleEditMode)
        {
            var remove = new Button
            {
                Content = "✕",
                FontSize = 10,
                Padding = new Thickness(6, 2),
                Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                Background = Brushes.Transparent
            };
            ToolTip.SetTip(remove, $"'{chip.DisplayName}' 묶음을 통째로 뺍니다.");
            remove.Click += (_, _) =>
            {
                UiGuard.Run(_session, "자주 쓰는 칩 제거", () => _session!.Editor.RemoveQuickCommandAt(index));
                _bundlePinIndex = null;
                _bundleExpandedStep = null;
                onApplied();
            };
            return remove;
        }

        (IReadOnlyList<(string Output, IReadOnlyDictionary<string, string> Arguments)> steps, string? blocked) =
            ResolveQuickSteps(chip);

        var apply = new Button
        {
            Content = "붙이기",
            FontSize = 10,
            Padding = new Thickness(8, 2),
            IsEnabled = blocked is null && steps.Count > 0
        };
        ToolTip.SetTip(apply, blocked ?? $"담긴 {chip.Steps.Count}개를 순서대로 이 라인에 붙입니다.");
        apply.Click += (_, _) =>
        {
            if (blocked is not null)
            {
                return;
            }

            ApplyStageCommands(chip.DisplayName, steps);
            onApplied();
        };

        return apply;
    }

    /// <summary>
    /// 편집 중 머리 줄의 이름 칸. <b>초점을 받는 것이 곧 "이 묶음에 담겠다"</b>이므로
    /// 담기 대상 선택과 이름 고치기가 같은 한 칸이다 — 손잡이를 늘리지 않는다.
    /// 커밋은 Enter와 초점 잃음 둘 다(이름을 치다 터미널을 클릭하면 초점이 먼저 빠진다).
    /// </summary>
    private Control BuildQuickBundleNameBox(StageQuickCommand chip, int index, Action onApplied)
    {
        var name = new TextBox
        {
            Text = chip.DisplayName,
            FontSize = 11,
            MinHeight = 24,
            Padding = new Thickness(5, 1),
            Background = Brushes.Transparent
        };

        void Commit()
        {
            if (_session is null || string.Equals(name.Text, chip.DisplayName, StringComparison.Ordinal))
            {
                return;
            }

            UiGuard.Run(_session, "칩 이름", () =>
                _session.Editor.RenameQuickCommandAt(index, name.Text ?? string.Empty));
        }

        name.GotFocus += (_, _) =>
        {
            if (_bundlePinIndex == index)
            {
                return;
            }

            _bundlePinIndex = index;
            _bundleExpandedStep = null;
            QuickEditModeChanged?.Invoke(); // 터미널 행 툴팁이 목적지를 말한다
            onApplied();
        };

        name.LostFocus += (_, _) => Commit();
        name.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                args.Handled = true;
                Commit();
                onApplied();
            }
        };

        return name;
    }

    /// <summary>
    /// 편집 중의 칩 한 줄 — [▸ 펼치기][이름 칸][✕], 펼치면 그 아래 <b>수치 조절</b>이 선다
    /// (2026-08-22 소유자: "개별 커맨드의 세부 수치를 조절할 수 있게 … 터미널 아래에서
    /// 그랬던 것 처럼"). 슬라이더·선택기는 작업대와 <b>같은 함수</b>이고 쓰는 곳만 다르다
    /// (<see cref="ArgumentSink"/>).
    ///
    /// 이름 칸이 있는 이유: 담기가 정의 이름을 자동으로 붙이므로(집는 순간 이름을 물으면
    /// 흐름이 끊긴다) 같은 커맨드를 값만 달리해 담은 칩 둘이 같은 이름으로 선다.
    /// 커밋은 Enter와 <b>초점 잃음</b> 둘 다 — 이름을 치다 터미널을 클릭하면 초점이 먼저
    /// 빠지므로, 그 길로 들어온 이름도 살아남는다.
    /// </summary>
    private Control BuildQuickChipEditRow(
        StageQuickCommand chip,
        int index,
        Action onApplied)
    {
        bool expanded = _commandExpandedIndex == index;
        bool adjustable = ManipulationCatalog.Find(chip.Steps[0].DefinitionId)
            is { } definition && definition.Parameters.Any(IsAdjustableParameter);

        var expand = new Button
        {
            Content = expanded ? "▾" : "▸",
            FontSize = 10,
            Padding = new Thickness(5, 2),
            Margin = new Thickness(0, 0, 4, 0),
            IsEnabled = adjustable,
            Opacity = adjustable ? 1 : 0.35
        };
        ToolTip.SetTip(expand, adjustable
            ? "이 칩의 수치를 폅니다."
            : "이 커맨드에는 조절할 수치가 없습니다.");
        expand.Click += (_, _) =>
        {
            _commandExpandedIndex = expanded ? null : index;
            onApplied();
        };

        var name = new TextBox
        {
            Text = chip.DisplayName,
            FontSize = 11,
            MinHeight = 26,
            Padding = new Thickness(6, 2)
        };
        ToolTip.SetTip(name, QuickChipTip(chip));

        void Commit()
        {
            if (_session is null || string.Equals(name.Text, chip.DisplayName, StringComparison.Ordinal))
            {
                return;
            }

            UiGuard.Run(_session, "칩 이름", () =>
                _session.Editor.RenameQuickCommandAt(index, name.Text ?? string.Empty));
        }

        name.LostFocus += (_, _) => Commit();
        name.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                args.Handled = true;
                Commit();
                onApplied();
            }
        };

        var remove = new Button
        {
            Content = "✕",
            FontSize = 10,
            Padding = new Thickness(6, 2),
            Margin = new Thickness(4, 0, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38))
        };
        ToolTip.SetTip(remove, $"'{chip.DisplayName}' 칩을 뺍니다.");
        remove.Click += (_, _) =>
        {
            UiGuard.Run(_session, "자주 쓰는 칩 제거", () => _session!.Editor.RemoveQuickCommandAt(index));
            onApplied();
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(expand, 0);
        Grid.SetColumn(name, 1);
        Grid.SetColumn(remove, 2);
        row.Children.Add(expand);
        row.Children.Add(name);
        row.Children.Add(remove);

        if (!expanded)
        {
            return row;
        }

        // 커맨드 칩은 <b>곧 그 커맨드</b>다 — 단계 번호도, 순서 화살표도, 이어 담기도
        // 여기 없다(그건 묶음의 어휘다, 2026-08-24 3차). 펼치면 수치만 바로 선다.
        var body = new StackPanel { Spacing = 3, Margin = new Thickness(18, 4, 0, 6) };

        if (ManipulationCatalog.Find(chip.Steps[0].DefinitionId) is { } only)
        {
            AddQuickChipParameterRows(body, chip.Steps[0], only, index, stepIndex: 0);
        }

        return new StackPanel { Children = { row, body } };
    }

    /// <summary>
    /// 펼친 칩의 단계 한 줄 — <c>1 &lt;&lt;fade_out c1 0.4s&gt;&gt;</c> 와 [▲][▼][✕].
    ///
    /// <b>줄 자체가 수치 입구다</b>: 누르면 그 단계의 슬라이더·선택기가 아래 펼쳐진다
    /// (칩과 마찬가지로 하나만 열린다). 화살표를 따로 두면 좁은 줄에 손잡이가 넷이 된다.
    ///
    /// 요약은 <b>이 단계가 실제로 낼 커맨드</b>다 — 이름이 무엇을 감추는지 숨기지 않는다.
    /// </summary>
    private Control BuildQuickStepRow(
        StageQuickCommand chip,
        int index,
        int stepIndex,
        Action onApplied)
    {
        StageQuickStep step = chip.Steps[stepIndex];
        PresentationCommandDefinition? definition = ManipulationCatalog.Find(step.DefinitionId);
        bool expanded = _bundleEditMode && _bundleExpandedStep == stepIndex;

        // 표의 한 행 — [번호][커맨드][인자]. 세 칸으로 가른 이유는 <b>세로로 줄이 맞아야</b>
        // 목록이 표로 읽히기 때문이다: 한 줄에 몰아 쓰면 커맨드 이름 길이에 따라 인자가
        // 들쭉날쭉해져 "몇 개가 무슨 순서로" 가 한눈에 안 들어온다.
        var number = new TextBlock
        {
            Text = $"{stepIndex + 1}",
            FontSize = 9,
            Opacity = 0.4,
            MinWidth = 12,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var command = new TextBlock
        {
            Text = definition?.OutputCommandName ?? "(없는 커맨드)",
            FontSize = 10,
            Opacity = definition is null ? 0.5 : 0.9,
            Margin = new Thickness(6, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var arguments = new TextBlock
        {
            Text = definition is null ? string.Empty : QuickStepArguments(definition, step.Arguments),
            FontSize = 9,
            Opacity = 0.55,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var cellsRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };
        Control[] textCells = [number, command, arguments];

        for (int column = 0; column < textCells.Length; column++)
        {
            Grid.SetColumn(textCells[column], column);
            cellsRow.Children.Add(textCells[column]);
        }

        // 평소에는 행이 <b>읽는 것</b>이다 — 누를 것이 없으니 단추로 만들지 않는다.
        if (!_bundleEditMode)
        {
            cellsRow.Margin = new Thickness(4, 1, 2, 1);
            ToolTip.SetTip(cellsRow, definition is null
                ? "이 게임 정의에 없는 커맨드입니다 — 묶음 전체가 회색으로 섭니다."
                : QuickStepTip(definition, step.Arguments));
            return cellsRow;
        }

        var summary = new Button
        {
            Content = cellsRow,
            FontSize = 10,
            Padding = new Thickness(4, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = expanded ? new SolidColorBrush(Color.FromArgb(40, 250, 204, 21)) : Brushes.Transparent
        };
        ToolTip.SetTip(summary, definition is null
            ? "이 단계는 실행할 수 없습니다 — 묶음 전체가 회색으로 섭니다."
            : "누르면 이 단계의 수치를 폅니다.");
        summary.Click += (_, _) =>
        {
            _bundleExpandedStep = expanded ? null : stepIndex;
            onApplied();
        };

        Button Arrow(string glyph, int delta, string tip)
        {
            var button = new Button
            {
                Content = glyph,
                FontSize = 9,
                Padding = new Thickness(4, 1),
                Margin = new Thickness(2, 0, 0, 0),
                IsEnabled = stepIndex + delta >= 0 && stepIndex + delta < chip.Steps.Count
            };
            ToolTip.SetTip(button, tip);
            button.Click += (_, _) =>
            {
                UiGuard.Run(_session, "칩 단계 순서", () =>
                    _session!.Editor.MoveQuickCommandStep(index, stepIndex, delta));
                // 옮긴 단계를 계속 따라간다 — 펼쳐 놓은 것이 발밑에서 바뀌면 안 된다.
                _bundleExpandedStep = expanded ? stepIndex + delta : _bundleExpandedStep;
                onApplied();
            };
            return button;
        }

        var drop = new Button
        {
            Content = "✕",
            FontSize = 9,
            Padding = new Thickness(5, 1),
            Margin = new Thickness(2, 0, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38))
        };
        ToolTip.SetTip(drop, chip.Steps.Count == 1
            ? "마지막 단계입니다 — 빼면 빈 묶음이 됩니다([완료]가 걷습니다)."
            : "이 단계를 뺍니다.");
        drop.Click += (_, _) =>
        {
            UiGuard.Run(_session, "칩 단계 제거", () =>
                _session!.Editor.RemoveQuickCommandStepAt(index, stepIndex));
            _bundleExpandedStep = null;
            onApplied();
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };
        Control[] cells = [summary, Arrow("▲", -1, "위로"), Arrow("▼", 1, "아래로"), drop];

        for (int column = 0; column < cells.Length; column++)
        {
            Grid.SetColumn(cells[column], column);
            row.Children.Add(cells[column]);
        }

        if (!expanded || definition is null)
        {
            return row;
        }

        var body = new StackPanel { Spacing = 4, Margin = new Thickness(14, 3, 0, 5) };
        AddQuickChipParameterRows(body, step, definition, index, stepIndex);

        return new StackPanel { Children = { row, body } };
    }

    /// <summary>
    /// 칩의 수치 조절 줄 — 작업대(<see cref="AddParameterEditorRows"/>)와 <b>같은 위젯</b>을
    /// 쓰되 쓰는 곳만 칩이다. 다른 점 둘:
    ///
    /// - <b>대상(slot/alias)은 안 만진다</b> — 작업대의 규칙 그대로("대상 교체는 조작이
    ///   아니라 다른 커맨드다"). 칩의 대상을 바꾸려면 원하는 커맨드를 다시 담는다.
    /// - <b>곡선 편집기는 안 연다</b> — 커스텀 곡선은 노드의 커맨드에 매인 자산이라
    ///   칩이 소유할 수 없다. <c>ease</c>는 표준 이징 <b>선택기</b>로만 선다(후보 목록이
    ///   곧 어휘다). <c>oscillation</c>은 후보가 없어 조용히 빠진다.
    /// </summary>
    private void AddQuickChipParameterRows(
        StackPanel host,
        StageQuickStep chip,
        PresentationCommandDefinition definition,
        int index,
        int stepIndex)
    {
        if (_session is null)
        {
            return;
        }

        void Write(string argumentName, string value)
        {
            UiGuard.Run(_session, "칩 수치 조절", () =>
                _session.Editor.SetQuickCommandArgument(index, stepIndex, argumentName, value));
        }

        foreach (PresentationCommandParameter parameter in definition.Parameters)
        {
            if (ArgumentTokenCandidates.IsStageTargetType(parameter.Type))
            {
                continue;
            }

            string written = chip.Arguments.TryGetValue(parameter.Name, out string? value)
                ? value
                : parameter.Default ?? string.Empty;

            if (UnitSliderKind(parameter) is { } unitSigned &&
                parameter.Slider is { } unitSlider &&
                UnitToken.TryParseUnits(written, out float writtenUnits))
            {
                string UnitLabel(double units) =>
                    (unitSigned && units >= 0 ? "+" : string.Empty) +
                    units.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "u";

                host.Children.Add(BuildMotionSlider(
                    Write,
                    label: parameter.Name,
                    argumentName: parameter.Name,
                    value: writtenUnits,
                    minimum: unitSlider.Minimum,
                    maximum: unitSlider.Maximum,
                    tick: unitSlider.Step,
                    format: UnitLabel,
                    token: UnitLabel));
            }
            else if (parameter.Slider is { } slider)
            {
                double.TryParse(
                    written, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double current);

                host.Children.Add(BuildMotionSlider(
                    Write,
                    label: parameter.Name,
                    argumentName: parameter.Name,
                    value: current,
                    minimum: slider.Minimum,
                    maximum: slider.Maximum,
                    tick: slider.Step,
                    format: number => number.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                    token: number => number.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
            }
            else if (string.Equals(parameter.Type, "duration", StringComparison.Ordinal) &&
                     DurationToken.TryParseSeconds(written, out float seconds))
            {
                host.Children.Add(BuildMotionSlider(
                    Write,
                    label: "시간",
                    argumentName: parameter.Name,
                    value: seconds * DurationToken.FramesPerSecond,
                    minimum: 0,
                    maximum: DurationSliderMax(seconds * DurationToken.FramesPerSecond),
                    tick: 1,
                    format: frames => frames <= 0 ? "0fr (즉시)" : $"{frames:0}fr",
                    token: frames => $"{frames:0}fr"));
            }
            else if (ArgumentTokenCandidates.For(parameter.Type) is { Count: > 0 } candidates)
            {
                host.Children.Add(BuildTokenSelector(Write, parameter, written, candidates));
            }
        }

        if (host.Children.Count == 0)
        {
            host.Children.Add(new TextBlock
            {
                Text = "조절할 수치가 없는 커맨드입니다.",
                FontSize = 10,
                Opacity = 0.55
            });
        }
    }

    /// <summary>
    /// 칩 하나 — 누르면 담긴 단계가 <b>순서대로 한 번에</b> 지금 고른 라인에 붙는다.
    /// 되돌리기 한 번이 그 누름 하나를 원복한다(<c>PresentationStageActions.ApplyAll</c>).
    ///
    /// 묶음은 이름 옆 <c>·N</c>으로 몇 단계짜리인지 말하고, 툴팁이 그 N줄을 그대로 편다 —
    /// 눌러 보기 전에 무엇이 붙을지 알 수 있어야 한다.
    /// </summary>
    private Control BuildQuickChip(StageQuickCommand chip, Action onApplied)
    {
        (IReadOnlyList<(string Output, IReadOnlyDictionary<string, string> Arguments)> steps, string? blocked) =
            ResolveQuickSteps(chip);

        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        label.Children.Add(new TextBlock { Text = chip.DisplayName, FontSize = 11 });

        if (chip.Steps.Count > 1)
        {
            label.Children.Add(new TextBlock
            {
                Text = $"·{chip.Steps.Count}",
                FontSize = 10,
                Opacity = 0.55,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        var button = new Button
        {
            Content = label,
            FontSize = 11,
            Padding = new Thickness(9, 4),
            Margin = new Thickness(0, 0, 4, 4),
            // 한 단계라도 낼 수 없으면 회색이다 — 나머지만 조용히 붙이면 사람은 다 붙은 줄 안다.
            IsEnabled = blocked is null
        };

        ToolTip.SetTip(button, blocked ?? QuickChipTip(chip));

        button.Click += (_, _) =>
        {
            if (blocked is not null)
            {
                return;
            }

            ApplyStageCommands(chip.DisplayName, steps);
            onApplied();
        };

        return button;
    }

    /// <summary>
    /// 칩이 실제로 낼 커맨드 열 — 단계마다 카탈로그 기본값 위에 <b>담아 둔 값을 얹는다</b>
    /// (슬롯·duration 포함, 2026-08-22 소유자). 담긴 값이 언제나 이긴다.
    ///
    /// 조절창의 선택 슬롯이 들어가는 경우는 하나뿐이다: 커맨드가 대상을 요구하는데
    /// <b>그 단계가 그 자리를 안 담았을 때</b>(기본 목록·손으로 지운 칩).
    ///
    /// 막힌 사유를 <b>문장으로</b> 돌려준다 — 회색 단추는 왜 회색인지 말할 수 있어야 한다.
    /// 묶음에서는 "몇 번째 단계가" 까지 말해야 사람이 어디를 고칠지 안다.
    /// </summary>
    private (IReadOnlyList<(string Output, IReadOnlyDictionary<string, string> Arguments)> Steps, string? Blocked)
        ResolveQuickSteps(StageQuickCommand chip)
    {
        PresentationCommandCatalog catalog = ManipulationCatalog;
        var resolved = new List<(string, IReadOnlyDictionary<string, string>)>(chip.Steps.Count);

        for (int index = 0; index < chip.Steps.Count; index++)
        {
            StageQuickStep step = chip.Steps[index];

            if (catalog.Find(step.DefinitionId) is not { } definition)
            {
                return (resolved, $"{index + 1}번째 단계의 커맨드가 이 게임 정의에 없습니다.");
            }

            var arguments = new Dictionary<string, string>(
                definition.DefaultArgumentValues(), StringComparer.Ordinal);

            foreach ((string key, string value) in step.Arguments)
            {
                arguments[key] = value;
            }

            foreach (PresentationCommandParameter parameter in definition.Parameters)
            {
                if (!ArgumentTokenCandidates.IsStageTargetType(parameter.Type) ||
                    step.Arguments.ContainsKey(parameter.Name))
                {
                    continue;
                }

                if (_popoverSlotKey is not { } slotKey)
                {
                    return (resolved,
                        $"{index + 1}번째 단계(<<{definition.OutputCommandName}>>)의 대상 슬롯이 없습니다. " +
                        "위 [+]로 슬롯을 먼저 만드세요.");
                }

                arguments[parameter.Name] = slotKey;
            }

            resolved.Add((definition.OutputCommandName, arguments));
        }

        return (resolved, null);
    }

    /// <summary>툴팁 = 이 칩이 낼 커맨드 그대로, 단계마다 한 줄. 이름이 감추는 것을 숨기지 않는다.</summary>
    private string QuickChipTip(StageQuickCommand chip)
    {
        PresentationCommandCatalog catalog = ManipulationCatalog;

        return string.Join('\n', chip.Steps.Select((step, index) =>
        {
            string prefix = chip.Steps.Count > 1 ? $"{index + 1}  " : string.Empty;

            return catalog.Find(step.DefinitionId) is { } definition
                ? prefix + QuickStepTip(definition, step.Arguments)
                : prefix + "(이 게임 정의에 없는 커맨드)";
        }));
    }

    /// <summary>단계 한 줄의 표기 — 담긴 인자를 정의 순서대로 편 yarn 한 줄이다.</summary>
    private static string QuickStepTip(
        PresentationCommandDefinition definition,
        IReadOnlyDictionary<string, string> arguments) =>
        $"<<{definition.OutputCommandName} {QuickStepArguments(definition, arguments)}>>".Replace(" >>", ">>");

    /// <summary>
    /// 표의 [인자] 칸 — 담긴 값만 <b>정의 순서대로</b> 늘어놓는다(이름은 빼고 값만).
    /// 이름까지 적으면 좁은 칸에서 값이 잘려, 정작 사람이 확인하려던 수치가 안 보인다.
    /// </summary>
    private static string QuickStepArguments(
        PresentationCommandDefinition definition,
        IReadOnlyDictionary<string, string> arguments) =>
        string.Join(' ', definition.Parameters
            .Where(parameter => arguments.ContainsKey(parameter.Name))
            .Select(parameter => arguments[parameter.Name]));

    /// <summary>배경 탭 — 탐색기와 같은 목록에서 고르면 bg_sprite/bg_spawn이 된다(기존 동작 그대로).</summary>
    private Control BuildBackgroundTab(Action onApplied)
    {
        PreviewAssetLibrary library = _session!.AssetLibrary;
        var list = new StackPanel { Spacing = 2 };
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 0) };

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
                ApplyBackground(entry.SpriteKey);
                onApplied();
            };

            list.Children.Add(rowButton);
        }

        return panel;
    }

    /// <summary>
    /// 슬롯 탭 — <b>전역 선택 슬롯 하나</b>의 조작이다(추가·선택은 조절창 상단 전역 헤더).
    /// 무대/레이어(Setup의 slot 재선언 — 부착만 갱신), 위치(place)·깊이(size)는 이 라인.
    /// tuning이 수입돼 있으면 전부 코어 좌표로 실반영되고(W25·W27), 없으면 근사임을 쓴다(규칙 14).
    /// </summary>
    private Control BuildSlotTab(Action onApplied)
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };

        if (_popoverSlotKey is not { } slotKey || _request is null ||
            !_request.State.Slots.ContainsKey(slotKey))
        {
            panel.Children.Add(new TextBlock
            {
                Text = "조작할 슬롯이 없습니다. 상단 [+]로 추가하세요.",
                FontSize = 10,
                Opacity = 0.65
            });
            return panel;
        }

        // ── 슬롯의 위치(무대/레이어) → Setup slot 재선언 (부착만 갱신, W27) ──
        // 현재 값은 코어 부착 상태에서 읽는다. 후보 어휘는 카탈로그 타입 후보 한 곳.
        string currentStage = "stage00";
        string currentLayer = "mid";

        if (_request.CoreState is { } core &&
            core.TryGetAttachment(slotKey, out Ked.Presentation.Core.SlotAttachment attachment))
        {
            currentStage = attachment.StageKey ?? currentStage;
            currentLayer = attachment.LayerKey ?? currentLayer;
        }

        panel.Children.Add(new TextBlock
        {
            Text = "무대 / 레이어 (앞뒤 겹침 — Setup에 기록)",
            FontSize = 10,
            Opacity = 0.6
        });

        var stageCombo = new ComboBox
        {
            ItemsSource = ArgumentTokenCandidates.For("stageKey").ToArray(),
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        stageCombo.SelectedItem = currentStage;
        ToolTip.SetTip(stageCombo, "무대(stage) — 뒤 무대일수록 먼저 그려집니다.");

        var layerCombo = new ComboBox
        {
            ItemsSource = ArgumentTokenCandidates.For("layerKey").ToArray(),
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        layerCombo.SelectedItem = currentLayer;
        ToolTip.SetTip(layerCombo, "레이어 — far(뒤)부터 close(앞) 순으로 겹칩니다.");

        void ApplyAttachment()
        {
            ApplySetupCommand(
                "slot",
                ("slotKey", slotKey),
                ("stage", stageCombo.SelectedItem as string ?? currentStage),
                ("layer", layerCombo.SelectedItem as string ?? currentLayer));
            onApplied();
        }

        stageCombo.SelectionChanged += (_, _) => ApplyAttachment();
        layerCombo.SelectionChanged += (_, _) => ApplyAttachment();

        var positionRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,4,*") };
        Grid.SetColumn(stageCombo, 0);
        Grid.SetColumn(layerCombo, 2);
        positionRow.Children.Add(stageCombo);
        positionRow.Children.Add(layerCombo);
        panel.Children.Add(positionRow);

        // ── 캐스팅 → Setup (위치·깊이는 [캐릭터] 탭 — 겹치지 않는다) ──
        MiniStageSlot slot = _request.State.Slots[slotKey];
        PreviewAssetLibrary library = _session!.AssetLibrary;

        string[] characters = CastingCandidates(
            library.PortraitEntries.Select(entry => entry.Key.CharacterId),
            _session.Definition.Speakers.Select(speaker => speaker.CharacterId));

        panel.Children.Add(new TextBlock
        {
            Text = $"'{slotKey}' 캐스팅 (노드 Setup에 기록)",
            FontSize = 10,
            Opacity = 0.6
        });

        if (characters.Length == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "초상화 폴더나 화자 등록에서 캐릭터를 찾지 못했습니다.",
                FontSize = 10,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap
            });
        }

        var characterWrap = new WrapPanel { MaxWidth = 260 };

        foreach (string characterId in characters)
        {
            var characterButton = new Button
            {
                Content = characterId,
                FontSize = 10,
                Padding = new Thickness(6, 2),
                Margin = new Thickness(0, 0, 4, 4),
                IsEnabled = !string.Equals(characterId, slot.CharacterId, StringComparison.Ordinal)
            };

            characterButton.Click += (_, _) =>
            {
                ApplySetupCommand("cast", ("slot", slotKey), ("characterKey", characterId));
                onApplied();
            };

            characterWrap.Children.Add(characterButton);
        }

        panel.Children.Add(characterWrap);

        return panel;
    }

    /// <summary>
    /// 캐스팅에 고를 수 있는 캐릭터들 — 출처가 <b>둘</b>이다:
    /// 초상화 폴더 · 정의 파일 speakers의 캐릭터키.
    ///
    /// 초상화가 아직 없어도 <b>이름은 정해진 것</b>이니 고를 수 있어야 한다 (2026-08-17
    /// 소유자 보고: "기획자가 지정한 캐릭터도 안 보이고" — 표정 단추와 같은 구멍이었다).
    /// ⚠ 셋째 출처였던 <b>챕터 `화자` 시트</b>는 2026-08-23에 시트째 폐지됐다 — 기획자가
    /// 적는 자리가 정의 파일 하나가 되어 둘째가 그것을 이미 담는다.
    /// </summary>
    internal static string[] CastingCandidates(
        IEnumerable<string> portraits,
        IEnumerable<string?> defined) =>
        portraits
            .Concat(defined.Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

    /// <summary>런타임 screenPoint 프리셋을 화면 배열 그대로 놓은 3×3.</summary>
    private static readonly string[][] ScreenPointRows =
    [
        ["tl", "top", "tr"],
        ["left", "center", "right"],
        ["bl", "bottom", "br"]
    ];

    /// <summary>
    /// 위치 이동 격자. 슬롯 탭과 캐릭터 팝오버가 <b>같은 격자 하나</b>를 쓴다(사본 금지).
    /// 누르면 이 라인의 <c>place</c>가 되고, 같은 슬롯을 여러 번 옮겨도 커맨드는 하나가 수정된다.
    /// </summary>
    private Control BuildScreenPointGrid(Func<string?> resolveSlotKey, Action onApplied)
    {
        var pointGrid = new StackPanel { Spacing = 2 };

        foreach (string[] pointRow in ScreenPointRows)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };

            foreach (string point in pointRow)
            {
                var pointButton = new Button
                {
                    Content = point,
                    FontSize = 10,
                    Width = 62,
                    Padding = new Thickness(0, 2),
                    HorizontalContentAlignment = HorizontalAlignment.Center
                };

                pointButton.Click += (_, _) =>
                {
                    if (resolveSlotKey() is { } slotKey)
                    {
                        ApplyStageCommand("place", StageArgs(("slot", slotKey), ("screenPoint", point)));
                        onApplied();
                    }
                };

                row.Children.Add(pointButton);
            }

            pointGrid.Children.Add(row);
        }

        return pointGrid;
    }

    /// <summary>
    /// 깊이(size) 버튼 행 — 슬롯 탭과 캐릭터 팝오버가 <b>같은 행 하나</b>를 쓴다(사본 금지).
    /// 누르면 이 라인의 <c>size</c>가 되고, 같은 슬롯을 다시 고르면 커맨드는 하나가 수정된다.
    /// far(작게·뒤)부터 close(크게·앞)까지 — 스케일·높이 반영은 코어 depth 프리셋의 일이다(W27).
    /// </summary>
    private Control BuildDepthRow(Func<string?> resolveSlotKey, Action onApplied)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };

        foreach (string preset in ArgumentTokenCandidates.For("depthPreset"))
        {
            var presetButton = new Button
            {
                Content = preset,
                FontSize = 10,
                Padding = new Thickness(6, 2),
                HorizontalContentAlignment = HorizontalAlignment.Center
            };

            presetButton.Click += (_, _) =>
            {
                if (resolveSlotKey() is { } slotKey)
                {
                    ApplyStageCommand("size", StageArgs(("slot", slotKey), ("depth", preset)));
                    onApplied();
                }
            };

            row.Children.Add(presetButton);
        }

        return row;
    }

    /// <summary>
    /// 코어 좌표가 있으면(tuning 수입됨) place는 실제로 그려지므로 안내가 필요 없다 (W25).
    /// 없으면 기존 근사임을 숨기지 않는다(규칙 14).
    /// </summary>
    private TextBlock? PlaceApproximationNote()
    {
        if (_request?.CoreState is not null)
        {
            return null;
        }

        return new TextBlock
        {
            Text = "tuning 미수입이라 위치는 커맨드로만 기록됩니다(균등 나열 근사 + 뱃지). " +
                "ExportedTuning 폴더를 프로젝트에 넣으면 실제 배치로 그려집니다.",
            FontSize = 9,
            Opacity = 0.55,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 250
        };
    }

    /// <summary>
    /// 오디오 탭 (W59) — 이 라인에 BGM/효과음 커맨드를 단다. 목록은 assets/bgm·assets/sfx
    /// 폴더의 파일(파일명 = clipKey)이고, 적용은 다른 탭과 같은 커맨드 경로 하나다.
    /// 정지 프레임에서는 ♪ 칩(W34-b)이 이 라인의 소리를 알리고, 재생이 이 라인에
    /// 도달하면 실제로 울린다 (W62). ▶는 커맨드를 달지 않는 미리 듣기다.
    /// </summary>
    private Control BuildAudioTab(Action onApplied)
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };

        void AddSection(string title, bool bgm, string playCommand, string stopCommand, string stopLabel)
        {
            // 제목 옆 볼륨 슬라이더 (W63) — 툴 미리 듣기 볼륨이다. 게임 출력에는 영향이 없고,
            // 울리고 있는 소리에도 즉시 적용되며 세션을 넘어 기억된다(AppSettings).
            double volume = bgm ? AudioPreview.BgmVolume : AudioPreview.SfxVolume;

            var volumeLabel = new TextBlock
            {
                Text = $"{Math.Round(volume * 100)}%",
                FontSize = 9,
                Opacity = 0.6,
                Width = 30,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            var volumeSlider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = volume * 100,
                Width = 90,
                Margin = new Thickness(8, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(volumeSlider, "툴 미리 듣기 볼륨 — 게임 출력에는 영향이 없습니다.");
            volumeSlider.ValueChanged += (_, args) =>
            {
                double next = args.NewValue / 100;

                if (bgm)
                {
                    AudioPreview.BgmVolume = next;
                }
                else
                {
                    AudioPreview.SfxVolume = next;
                }

                volumeLabel.Text = $"{Math.Round(args.NewValue)}%";
            };

            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center
            };

            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
            Grid.SetColumn(titleText, 0);
            Grid.SetColumn(volumeSlider, 1);
            Grid.SetColumn(volumeLabel, 2);
            header.Children.Add(titleText);
            header.Children.Add(volumeSlider);
            header.Children.Add(volumeLabel);
            panel.Children.Add(header);

            string? root = bgm ? _session!.BgmRoot : _session!.SfxRoot;
            IReadOnlyList<string> keys = _session!.AudioClipKeys(root);
            var list = new StackPanel { Spacing = 2 };

            if (keys.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = root is null
                        ? "폴더가 없습니다 — 프로젝트를 저장하면 assets 아래 준비됩니다."
                        : $"파일이 없습니다 — {(bgm ? "assets/bgm" : "assets/sfx")}에 mp3·wav·ogg를 넣고 에셋 새로 고침.",
                    FontSize = 9,
                    Opacity = 0.55,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 250
                });
            }

            foreach (string key in keys)
            {
                var button = new Button
                {
                    Content = $"♪ {key}",
                    FontSize = 10,
                    Padding = new Thickness(6, 2),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };
                button.Click += (_, _) =>
                {
                    ApplyStageCommand(playCommand, new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["clipKey"] = key
                    });
                    onApplied();
                };

                // ▶ = 미리 듣기 (W62) — 커맨드를 달지 않고 소리만 확인한다. 다시 누르면 멈춘다.
                var audition = new Button
                {
                    Content = "▶",
                    FontSize = 10,
                    Padding = new Thickness(5, 2),
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                ToolTip.SetTip(audition, "미리 듣기 — 라인에 달지 않고 소리만 확인합니다. 다시 누르면 멈춥니다.");
                audition.Click += (_, _) => UiGuard.Run(_session, "오디오 미리 듣기", () =>
                {
                    if (_session!.ResolveAudioClipPath(root, key) is { } path)
                    {
                        AudioPreview.ToggleAudition(path, bgm);
                    }
                });

                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                Grid.SetColumn(button, 0);
                Grid.SetColumn(audition, 1);
                row.Children.Add(button);
                row.Children.Add(audition);
                list.Children.Add(row);
            }

            panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 140 });

            var stop = new Button
            {
                Content = stopLabel,
                FontSize = 10,
                Padding = new Thickness(6, 2),
                Background = new SolidColorBrush(Color.FromArgb(60, 220, 38, 38))
            };
            stop.Click += (_, _) =>
            {
                ApplyStageCommand(stopCommand, new Dictionary<string, string>(StringComparer.Ordinal));
                onApplied();
            };
            panel.Children.Add(stop);
        }

        AddSection("BGM (파일명 = clipKey)", bgm: true, "bgm", "stop_bgm", "BGM 정지 (stop_bgm)");
        panel.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(60, 148, 163, 184))
        });
        AddSection("효과음 (파일명 = clipKey)", bgm: false, "sfx", "stop_all_sfx", "효과음 모두 정지 (stop_all_sfx)");

        return panel;
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
