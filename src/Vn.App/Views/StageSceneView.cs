using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Ked.Presentation.Core;
using Vn.App.Services;
using Vn.Authoring.Assets;
using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
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
    private readonly List<(string SlotKey, Control Control, StageRect To)> _transitionEntries = new();
    private string? _backgroundImagePath;
    private string? _previousBackgroundImagePath;
    private Image? _backgroundImage;
    private Image? _backgroundOverlay;

    /// <summary>
    /// 전이 진행도 적용 (W33). null/1 이상 = 확정 상태(모든 자리 최종값·오버레이 제거).
    /// 0..1 = 직전 자리 → 새 자리 선형 보간, 새로 등장한 슬롯은 페이드 인,
    /// 배경이 바뀌었으면 옛 배경을 위에 얹어 크로스페이드.
    /// </summary>
    internal void SetTransitionProgress(double? progress)
    {
        if (progress is not { } t || t >= 1)
        {
            // null = 정지 화면 (W66 소유자 결정): 이동 슬롯은 이 라인이 시작되는 순간의
            // 자리 — 곧 출발 자리에 선다. 궤적·고스트가 어디로 갈지를 말하고, ▶가 태운다.
            // 1 = 재생이 이동을 끝까지 태웠다 — 도착 자리에 남는다.
            bool restAtStart = progress is null;

            foreach ((string slotKey, Control control, StageRect to) in _transitionEntries)
            {
                if (restAtStart &&
                    _motionTransitions.TryGetValue(slotKey, out (StageRect From, double Seconds, EaseKind Ease, IReadOnlyList<CurveKey>? Curve) rest))
                {
                    Canvas.SetLeft(control, rest.From.X);
                    Canvas.SetTop(control, rest.From.Y);
                }
                else
                {
                    Canvas.SetLeft(control, to.X);
                    Canvas.SetTop(control, to.Y);
                }

                control.Width = to.Width;
                control.Height = to.Height;
                control.Opacity = 1;
            }

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

        foreach ((string slotKey, Control control, StageRect to) in _transitionEntries)
        {
            if (_motionTransitions.TryGetValue(
                    slotKey,
                    out (StageRect From, double Seconds, EaseKind Ease, IReadOnlyList<CurveKey>? Curve) motion))
            {
                // 이동 커맨드가 있는 슬롯은 궤적을 탄다 (W66) — 출발은 직전 렌더가 아니라
                // 이동의 진짜 출발이고, 시간은 그 커맨드의 duration이다(라인 최대 아님).
                // 모양은 코어 EaseFunctions/CurveFunctions — 런타임과 등가 고정된 그
                // 곡선이다(W66b). Back·Elastic·커스텀은 1을 넘나들 수 있고 Lerp가 그대로 태운다.
                double lineSeconds = _request?.TransitionSeconds ?? 0;
                double motionProgress = motion.Seconds <= 0
                    ? 1
                    : Math.Clamp(t * lineSeconds / motion.Seconds, 0, 1);
                double eased = EvaluateMotionShape(motion, motionProgress);

                control.Opacity = 1;
                Canvas.SetLeft(control, Lerp(motion.From.X, to.X, eased));
                Canvas.SetTop(control, Lerp(motion.From.Y, to.Y, eased));
                control.Width = to.Width;
                control.Height = to.Height;
            }
            else if (_previousPortraitRects.TryGetValue(slotKey, out StageRect? from) && from is not null)
            {
                control.Opacity = 1;
                Canvas.SetLeft(control, Lerp(from.X, to.X, t));
                Canvas.SetTop(control, Lerp(from.Y, to.Y, t));
                control.Width = Lerp(from.Width, to.Width, t);
                control.Height = Lerp(from.Height, to.Height, t);
            }
            else
            {
                control.Opacity = t; // 새로 등장 — 페이드 인
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

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;

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
            IsLightDismissEnabled = true
        };
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
            if (args.Handled || !args.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed)
            {
                return;
            }

            // 재생 중에는 클릭이 진행이다 — 조작 대신 이 라인 즉시 완료 → 다음 (W31).
            if (PlaybackAdvance?.Invoke() == true)
            {
                args.Handled = true;
                return;
            }

            if (CanManipulate())
            {
                _consoleOpenAt = args.GetPosition(this);
                UiGuard.Run(_session, "무대 조절창", ShowStagePopover);
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
        _dialogueText = null;
        _dialogueFullText = null;

        // 전이 (W33): 이번 렌더가 "새 자리"가 되고 직전 렌더의 자리가 "출발점"이 된다.
        _previousPortraitRects = _portraitRects;
        _portraitRects = new Dictionary<string, StageRect>(StringComparer.Ordinal);
        _previousBackgroundImagePath = _backgroundImagePath;
        _backgroundImagePath = null;
        _backgroundImage = null;
        _backgroundOverlay = null;
        _transitionEntries.Clear();
        _motionTransitions.Clear();

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
        RenderMotionCues(request, layout, height, em);
        RenderStatsHud(request, width, em);
    }

    /// <summary>
    /// 이 라인의 커맨드 스트립 (W66) — 라인에 붙은 연출이 프리뷰 화면 안에서 보인다
    /// (소유자 그림: "프리뷰에 커맨드 목록이 합쳐진다"). 이동 커맨드(모션 선언 있음)는
    /// ⇢ 칩 + 궤적 + 슬라이더이고, 나머지는 커맨드가 있다는 사실을 알리는 표시 칩이다.
    ///
    /// <b>정지 화면 = 이 라인이 시작되는 순간이다</b> (소유자 결정): 이동 슬롯은 출발
    /// 자리에 서고, 도착 자리에 고스트 윤곽, 둘 사이에 점선 궤적 — ▶가 그 길을 태운다.
    /// 값은 전부 <see cref="StageMotionCue"/>가 들고 오고 여기서 다시 계산하지 않는다(사본 금지).
    /// </summary>
    private void RenderMotionCues(
        MiniStagePreviewRequest request, StageSceneLayout layout, double height, double em)
    {
        // 샷 배율은 코어 규약 그대로 — 컴포저가 초상을 놓을 때 쓴 그 함수다.
        double cameraScale = request.CoreState is { } core
            ? ShotIntentMath.EvaluateCameraScale(core.Shot.Zoom)
            : 1;

        if (request.MotionCues is { Count: > 0 } cues)
        {
            foreach (StageMotionCue cue in cues)
            {
                StagePortraitPlacement? portrait = layout.Portraits.FirstOrDefault(item =>
                    string.Equals(item.SlotKey, cue.SlotKey, StringComparison.Ordinal));

                if (portrait is not null)
                {
                    RenderMotionTrail(portrait.Rect, cue, cameraScale, em);
                }
            }
        }

        // 휴지 배치 (W66 소유자 결정) — 렌더 직후의 기본 화면은 "이 라인이 시작되는 순간"
        // 이다: 이동 슬롯은 출발 자리에 선다. 재생이 돌고 있으면 곧이어 오는 전이 동기화가
        // 실제 진행도로 덮는다(MiniStagePreview.Render 끝의 SyncTransition).
        foreach ((string slotKey, Control control, _) in _transitionEntries)
        {
            if (_motionTransitions.TryGetValue(slotKey, out (StageRect From, double Seconds, EaseKind Ease, IReadOnlyList<CurveKey>? Curve) rest))
            {
                Canvas.SetLeft(control, rest.From.X);
                Canvas.SetTop(control, rest.From.Y);
            }
        }

        // 커맨드 텍스트는 무대에 띄우지 않는다 (2026-08-20 소유자 정리) — 목록·편집 입구는
        // 왼쪽 대본 패널의 것이고, 무대에는 궤적·고스트와 프레임 스크럽만 남는다.
        if (_motionTransitions.Count > 0)
        {
            Add(BuildFrameScrubber(em),
                new StageRect(em * 0.6, height * 0.10 + em * 3.2, em * 10, em * 1.4));
        }
    }

    /// <summary>
    /// 대본 패널의 점(●)이 가리킨 커맨드의 상세조절을 연다 — 이동 커맨드는 이동 편집기
    /// (슬라이더+이징+곡선), 그 외에 조절 가능한 파라미터가 선언된 커맨드는 파라미터
    /// 조절창이다. 어느 것도 아니면 열 것이 없다(조용히 무시가 아니라 상태줄이 말한다).
    /// </summary>
    internal void ShowInspectorForCommand(PresentationResultCommand command)
    {
        if (_session is null || _request?.EditContext is not { Editable: true })
        {
            return;
        }

        StageMotionCue? cue = _request.MotionCues?.FirstOrDefault(item =>
            string.Equals(item.CommandId, command.CommandId, StringComparison.Ordinal));

        if (cue is not null)
        {
            ShowMotionFlyout(cue);
            return;
        }

        PresentationCommandDefinition? definition = ManipulationCatalog.Find(command.DefinitionId);

        if (definition is not null && definition.Parameters.Any(IsAdjustableParameter))
        {
            ShowParameterFlyout(definition, command);
            return;
        }

        _session.SetStatus("이 커맨드에는 조절할 수치·프리셋이 선언돼 있지 않습니다 — 텍스트로 고치세요.");
    }

    /// <summary>
    /// 프레임 스크럽 (W66b, 소유자: "먹고가는 프레임별로 상태를 확인") — 이 라인 배치의
    /// 내부 시간을 손으로 끈다. 렌더가 아닌 것은 아무것도 바꾸지 않는다: 기존 전이 보간
    /// (<see cref="SetTransitionProgress"/>)에 진행도를 흘릴 뿐이고, 곡선 모양도 재생과
    /// 같은 코어 이징이다. ▶ 재생이 돌면 재생 틱이 이 값을 덮는다.
    /// </summary>
    private Control BuildFrameScrubber(double em)
    {
        double lineSeconds = _request?.TransitionSeconds ?? 0;
        double frames = Math.Max(1, Math.Round(lineSeconds * DurationToken.FramesPerSecond));

        var frameLabel = new TextBlock
        {
            Text = "0fr",
            FontSize = em * 0.45,
            Width = em * 1.8,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromArgb(220, 250, 204, 21))
        };

        var scrub = new Slider
        {
            Minimum = 0,
            Maximum = frames,
            Value = 0,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Width = em * 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(scrub, "이 라인의 프레임을 끌어 확인합니다. 0 = 라인 시작(출발 자리).");
        scrub.ValueChanged += (_, args) =>
        {
            frameLabel.Text = $"{args.NewValue:0}fr";
            // 0은 정지 화면(출발 자리)과 같다 — null이 아니라 0을 흘려도 보간이 같은 자리다.
            SetTransitionProgress(args.NewValue / frames);
        };

        var row = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(130, 30, 30, 40)),
            CornerRadius = new CornerRadius(em * 0.2),
            Padding = new Thickness(em * 0.35, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = em * 0.2,
                Children = { scrub, frameLabel }
            }
        };

        return row;
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
    /// 수치 칩의 조절창 (W68) — 슬라이더 선언이 있는 파라미터와 duration이 슬라이더로
    /// 선다. 이동 편집기와 같은 규칙: 끄는 동안은 라벨만, 손을 뗄 때 한 번 저장,
    /// 확정 즉시 정지 프레임이 새 값으로 다시 접힌다.
    /// </summary>
    private void ShowParameterFlyout(
        PresentationCommandDefinition definition, PresentationResultCommand command)
    {
        if (_session is null || _request?.EditContext is not { Editable: true } context)
        {
            return;
        }

        ShowManipulationFlyout((host, _) =>
        {
            host.MinWidth = 250;
            host.Children.Add(new TextBlock
            {
                Text = $"{definition.DisplayName} (우클릭으로 닫기)",
                FontSize = 10,
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 250
            });

            foreach (PresentationCommandParameter parameter in definition.Parameters)
            {
                string written = command.Arguments.TryGetValue(parameter.Name, out string? value)
                    ? value
                    : parameter.Default ?? string.Empty;

                if (parameter.Slider is { } slider)
                {
                    double.TryParse(
                        written, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double current);

                    host.Children.Add(BuildMotionSlider(
                        context.PresentationNodeId,
                        command.CommandId,
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
                        context.PresentationNodeId,
                        command.CommandId,
                        label: "시간",
                        argumentName: parameter.Name,
                        value: seconds * DurationToken.FramesPerSecond,
                        minimum: 0,
                        maximum: 48,
                        tick: 1,
                        format: frames => frames <= 0 ? "0fr (즉시)" : $"{frames:0}fr",
                        token: frames => $"{frames:0}fr"));
                }
                else if (!ArgumentTokenCandidates.IsStageTargetType(parameter.Type) &&
                         ArgumentTokenCandidates.For(parameter.Type) is { Count: > 0 } candidates)
                {
                    // 프리셋 토큰 파라미터 (W68b — Depth·Place가 첫 고객): 후보 선택기.
                    // 후보는 제안이지 제약이 아니다 — 후보 밖 값(뎁스의 연속 레벨 숫자 등)은
                    // 선택 없음 + 현재 값 표시로 두고, 자유 입력은 기존 칩 텍스트 편집의 몫이다.
                    host.Children.Add(BuildTokenSelector(
                        context.PresentationNodeId,
                        command.CommandId,
                        parameter,
                        written,
                        candidates));
                }
            }
        });
    }

    /// <summary>프리셋 토큰 한 줄 — 고르면 그 인자만 바뀐다(같은 커맨드 인자 수정 통로).</summary>
    private Control BuildTokenSelector(
        string presentationNodeId,
        string commandId,
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

            if (string.Equals(selected, currentValue, StringComparison.Ordinal))
            {
                return;
            }

            UiGuard.Run(_session, "프리셋 선택", () =>
            {
                _session.Editor.SetPresentationCommandArgument(
                    presentationNodeId, commandId, parameter.Name, selected);
                ManipulationApplied?.Invoke();
            });
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
    /// 이 라인에서 이동하는 슬롯의 출발 자리·구간 시간·곡선 모양 (W66) — 재생 보간이
    /// "직전 렌더"가 아니라 <b>이동의 진짜 출발</b>에서 궤적을 타게 한다.
    /// 같은 슬롯에 이동이 여럿이면 마지막 것이 이긴다(런타임의 DOKill 의미론 —
    /// 앞의 것은 즉시 완주되므로 출발 자리에 이미 반영돼 있다).
    /// </summary>
    private readonly Dictionary<string, (StageRect From, double Seconds, EaseKind Ease, IReadOnlyList<CurveKey>? Curve)> _motionTransitions =
        new(StringComparer.Ordinal);

    /// <summary>진행도 → 곡선 값. 커스텀 곡선이 있으면 그것, 없으면 이징 — 재생·스크럽·정지 전부 이 하나다.</summary>
    private static double EvaluateMotionShape(
        (StageRect From, double Seconds, EaseKind Ease, IReadOnlyList<CurveKey>? Curve) motion, double progress) =>
        motion.Curve is { Count: >= 2 } keys
            ? CurveFunctions.Evaluate(keys as CurveKey[] ?? keys.ToArray(), (float)progress)
            : EaseFunctions.Evaluate(motion.Ease, (float)progress);

    /// <summary>
    /// 칩의 이징 이름 → 코어 어휘. 모르는 이름은 런타임 스펙 기본값(OutCubic)으로 —
    /// 브리지의 파싱 실패 처리와 같은 방향이다(로그 대신 카탈로그가 후보를 제한한다).
    /// </summary>
    private static EaseKind EaseKindOf(string? name) =>
        Enum.TryParse(name, ignoreCase: true, out EaseKind kind) ? kind : EaseKind.OutCubic;

    /// <summary>
    /// 도착 자리의 윤곽 + 출발에서 도착으로 잇는 선. 무대 좌표 변환은 컴포저의 규약
    /// 하나를 따른다 — 루트 공간에서 x는 같은 방향, y는 캔버스가 뒤집혀 있다.
    /// </summary>
    private void RenderMotionTrail(StageRect current, StageMotionCue cue, double cameraScale, double em)
    {
        double startX = current.X - cue.DeltaX * cameraScale;
        double startY = current.Y + cue.DeltaY * cameraScale;

        if (Math.Abs(startX - current.X) < 0.5 && Math.Abs(startY - current.Y) < 0.5)
        {
            return; // 움직이지 않는 이동 — 그릴 궤적도, 태울 재생도 없다.
        }

        // 재생 보간용 출발 등록 — 시간은 이 커맨드의 duration이고(라인 최대와 다를 수
        // 있다), 모양은 커맨드의 이징 또는 커스텀 곡선이다.
        _motionTransitions[cue.SlotKey] = (
            new StageRect(startX, startY, current.Width, current.Height),
            DurationToken.FramesToSeconds((float)cue.DurationFrames),
            EaseKindOf(cue.Ease),
            cue.CurveKeys);

        // 고스트는 도착 자리다 — 정지 화면의 초상이 출발 자리에 서므로(아래 휴지 배치),
        // 윤곽은 "어디로 가는가"를 말해야 짝이 맞는다.
        var ghost = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 250, 204, 21)),
            BorderThickness = new Thickness(Math.Max(1, em * 0.06)),
            CornerRadius = new CornerRadius(em * 0.1)
        };
        Add(ghost, current);

        var line = new Avalonia.Controls.Shapes.Line
        {
            StartPoint = new Point(startX + current.Width / 2, startY + current.Height / 2),
            EndPoint = new Point(current.X + current.Width / 2, current.Y + current.Height / 2),
            Stroke = new SolidColorBrush(Color.FromArgb(190, 250, 204, 21)),
            StrokeThickness = Math.Max(1, em * 0.08),
            StrokeDashArray = [3, 2]
        };

        // 선은 캔버스 좌표를 직접 쓴다 — 자리 지정 없이 얹는다.
        _canvas.Children.Add(line);
        Canvas.SetLeft(line, 0);
        Canvas.SetTop(line, 0);
    }


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

        // 전이(W33) 대상 — 이 슬롯의 자리.
        _portraitRects[portrait.SlotKey] = portrait.Rect;
        _transitionEntries.Add((portrait.SlotKey, image, portrait.Rect));

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

        // 전이(W33) 대상 — 숨김 슬롯의 자리도 미끄러진다.
        _portraitRects[portrait.SlotKey] = portrait.Rect;
        _transitionEntries.Add((portrait.SlotKey, outline, portrait.Rect));

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
    /// 이동 수치 조절창 (W66) — 선언된 축(x·y)과 시간을 슬라이더로 만진다.
    ///
    /// <b>끄는 동안은 편집이 아니다</b>(성능 규칙): 값 라벨만 따라 움직이고 프로젝트는
    /// 그대로다. 손을 뗄 때 한 번만 <see cref="ProjectEditor.UpdatePresentationCommandArguments"/>를
    /// 지나므로 <b>되돌리기 한 번이 조작 하나</b>를 원복한다 — 슬라이더가 지나온 중간값이
    /// undo 스택에 쌓이지 않는다.
    ///
    /// 만지는 대상은 커맨드 <b>하나</b>(칩이 가리킨 그것)다. 같은 종류를 찾아 합치는
    /// 직접 조작과 달리, 여기서는 이미 있는 커맨드의 인자만 바뀐다.
    /// </summary>
    private void ShowMotionFlyout(StageMotionCue cue)
    {
        if (_session is null || _request?.EditContext is not { Editable: true })
        {
            return;
        }

        ShowManipulationFlyout((host, _) =>
        {
            host.MinWidth = 250;
            AddMotionEditorRows(host, cue, headerSuffix: " (우클릭으로 닫기)");
        });
    }

    /// <summary>
    /// 이동 수치 편집기 본체 — 칩 플라이아웃과 조절창 [이동] 탭이 <b>같은 이것 하나</b>를
    /// 쓴다. 축 슬라이더는 선언이 만든다: x·y를 코드에 박지 않는다.
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
                context.PresentationNodeId,
                cue.CommandId,
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
                context.PresentationNodeId,
                cue.CommandId,
                label: "시간",
                argumentName: durationParameter,
                value: cue.DurationFrames,
                minimum: 0,
                maximum: 48,
                tick: 1,
                format: frames => frames <= 0 ? "0fr (즉시)" : $"{frames:0}fr",
                token: frames => $"{frames:0}fr"));
        }

        if (motion.EaseParameterName is { } easeParameter)
        {
            host.Children.Add(BuildEaseSelector(
                context.PresentationNodeId, cue, motion, easeParameter));
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
        StageMotionCue cue,
        PresentationMotionDeclaration motion,
        string easeParameter)
    {
        // 커스텀(커맨드 소유 곡선)이면 콤보 첫 칸이 그것이다 — 예전처럼 선택 없음(-1)으로
        // 두면 "지금 쓰이는 ease가 안 보이는" 화면이 된다(2026-08-20 소유자 보고).
        bool isCustom = cue.Ease is ['@', ..];
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

        if (isCustom && cue.CurveKeys is { Count: >= 2 } customKeys)
        {
            CurveKey[] keys = customKeys as CurveKey[] ?? customKeys.ToArray();
            DrawShape(t => CurveFunctions.Evaluate(keys, t));
        }
        else
        {
            EaseKind current = EaseKindOf(cue.Ease);
            DrawShape(t => EaseFunctions.Evaluate(current, t));
        }

        var combo = new ComboBox
        {
            ItemsSource = candidates,
            SelectedIndex = isCustom ? 0 : Array.IndexOf(enumNames, EaseKindOf(cue.Ease).ToString()),
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

            DrawShape(t => EaseFunctions.Evaluate(Enum.Parse<EaseKind>(selected), t));

            // 기본값 = 생략 (빈 값이 인자를 지운다 — SetPresentationCommandArgument 규약).
            string? token = string.Equals(selected, motion.DefaultEase, StringComparison.OrdinalIgnoreCase)
                ? null
                : selected;

            UiGuard.Run(_session, "이징 선택", () =>
            {
                if (isCustom)
                {
                    // 표준으로 복귀 = 소유 곡선 폐기(보관함 사본은 남는다).
                    EaseCurveCommandActions.DiscardOwned(
                        _session.Editor, presentationNodeId, cue.CommandId, easeParameter, token);
                }
                else
                {
                    _session.Editor.SetPresentationCommandArgument(
                        presentationNodeId, cue.CommandId, easeParameter, token);
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
        editButton.Click += (_, _) => ShowCurveEditorWindow(presentationNodeId, cue, easeParameter);

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
    private void ShowCurveEditorWindow(string presentationNodeId, StageMotionCue cue, string easeParameter)
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
                _session, presentationNodeId, cue, easeParameter, () => ManipulationApplied?.Invoke());

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
                _session, presentationNodeId, cue, easeParameter, () => ManipulationApplied?.Invoke());
            _curveWindow.Activate();
        }
    }

    /// 조절창 [이동] 탭 (W66, 소유자 요청) — 선택 슬롯의 이 라인 이동을 여기서도 만든다.
    /// 이동이 이미 있으면 칩과 같은 편집기가 열리고, 없으면 [＋]가 <c>move_by</c>를
    /// 라인에 단다(같은 슬롯을 다시 눌러도 쌓이지 않고 그 커맨드가 수정된다 — dedupe는
    /// <see cref="PresentationStageActions.Apply"/>의 규칙 그대로).
    /// </summary>
    private Control BuildMotionTab(Action onApplied)
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };

        if (_popoverSlotKey is not { } slotKey)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "슬롯을 먼저 고르세요 — 이동은 슬롯의 것입니다.",
                FontSize = 10,
                Opacity = 0.65
            });
            return panel;
        }

        if (_request?.EditContext is not { Editable: true })
        {
            panel.Children.Add(new TextBlock
            {
                Text = "읽기 전용 화면입니다 — 연출 노드를 열면 편집할 수 있습니다.",
                FontSize = 10,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 250
            });
            return panel;
        }

        // 칩과 같은 원천 — 이 라인에서 이 슬롯을 미는 이동. 여럿이면 마지막 것을 만진다
        // (앞의 것은 런타임이 즉시 완주시키므로 눈에 보이는 이동은 마지막 것이다).
        StageMotionCue? cue = _request.MotionCues?
            .LastOrDefault(item => string.Equals(item.SlotKey, slotKey, StringComparison.Ordinal));

        if (cue is not null)
        {
            AddMotionEditorRows(panel, cue);
            return panel;
        }

        panel.Children.Add(new TextBlock
        {
            Text = "이 라인에는 이 슬롯의 이동이 없습니다.",
            FontSize = 10,
            Opacity = 0.65
        });

        var addButton = new Button
        {
            Content = "＋ 이동 추가 (move_by)",
            FontSize = 11,
            Padding = new Thickness(8, 3)
        };
        ToolTip.SetTip(addButton,
            "이 라인에 move_by를 답니다 — 시작값 가로 +1u · 12fr. 슬라이더로 바로 고치세요.");
        addButton.Click += (_, _) =>
        {
            ApplyStageCommand("move_by", StageArgs(
                ("slot", slotKey), ("x", "+1u"), ("y", "0u"), ("duration", "12fr")));
            onApplied();
        };
        panel.Children.Add(addButton);

        return panel;
    }

    /// <summary>
    /// 값 하나짜리 슬라이더 줄. 끄는 동안은 라벨만 갱신하고, <b>손을 뗄 때 한 번</b> 저장한다.
    /// </summary>
    private Control BuildMotionSlider(
        string presentationNodeId,
        string commandId,
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
        void Commit()
        {
            if (_session is null)
            {
                return;
            }

            UiGuard.Run(_session, "수치 조절", () =>
            {
                _session.Editor.SetPresentationCommandArgument(
                    presentationNodeId, commandId, argumentName, token(slider.Value));
                ManipulationApplied?.Invoke();
            });
        }

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

        root.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(root).Properties.IsRightButtonPressed)
            {
                CloseConsole();
                args.Handled = true;
            }
        };

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
    /// 캐릭터(또는 빈 슬롯) 클릭 — 조절창을 그 슬롯의 [캐릭터] 탭으로 연다.
    /// 별도 팝오버가 아니라 같은 조절창 하나다 (소유자 지시 2026-08-06 2차).
    /// </summary>
    private void ShowCharacterPopover(string slotKey)
    {
        if (_session is null)
        {
            return;
        }

        _popoverSlotKey = slotKey; // 전역 선택도 이 슬롯을 따라간다
        _popoverTabIndex = 2;      // 캐릭터 탭
        ShowStagePopover();
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
    /// 등장/퇴장 버튼 줄 — 우측 정렬 + 구분 색(등장 초록·퇴장 붉음). 조절창 하단의
    /// 전역 줄로 어느 탭에서든 보인다(배경 탭 제외). 반대 방향 fade는 걷히고 원하는 쪽만 남는다.
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
    /// 무대 빈 곳 클릭 조절창 — 전역 슬롯 헤더(추가 + 선택) 위에 배경/슬롯/캐릭터 세 탭.
    /// 전부 정식 커맨드가 된다: 배경·위치·깊이·표시 상태는 선택된 라인의 바인딩에,
    /// <b>슬롯 생성·위치(무대/레이어)·캐스팅은 노드 Setup에</b>(장면 준비는 라인이 아니라
    /// 노드에 속한다). 프리뷰만 임시로 바꾸는 경로는 없다.
    /// 적용해도 닫히지 않는다 — 우클릭이 닫는다.
    /// </summary>
    private void ShowStagePopover()
    {
        if (_session is null || _request is null)
        {
            return;
        }

        ShowManipulationFlyout(BuildStagePopover);
    }

    private void BuildStagePopover(StackPanel host, Action rebuild)
    {
        if (_session is null || _request is null)
        {
            return;
        }

        host.MinWidth = 280;

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

        tabs.Items.Add(new TabItem
        {
            Header = new TextBlock { Text = "배경", FontSize = 11 },
            Content = BuildBackgroundTab(rebuild)
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
            Header = new TextBlock { Text = "이동", FontSize = 11 },
            Content = BuildMotionTab(rebuild)
        });
        tabs.Items.Add(new TabItem
        {
            Header = new TextBlock { Text = "오디오", FontSize = 11 },
            Content = BuildAudioTab(rebuild)
        });

        tabs.SelectedIndex = Math.Clamp(_popoverTabIndex, 0, tabs.Items.Count - 1);

        host.Children.Add(tabs);

        // ── 등장/퇴장 — 탭과 무관하게 항상 보이는 전역 줄 (선택 슬롯 대상).
        //    배경 탭에서만 숨긴다 — 배경은 슬롯이 아니다 (소유자 지시 2026-08-06 2차).
        Control? visibilityRow = _popoverSlotKey is { } selectedSlotKey
            ? BuildVisibilityRow(selectedSlotKey, rebuild)
            : null;

        if (visibilityRow is not null)
        {
            visibilityRow.IsVisible = tabs.SelectedIndex != 0;
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
                visibilityRow.IsVisible = tabs.SelectedIndex != 0;
            }
        };
    }

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
            _session.Definition.Speakers.Select(speaker => speaker.CharacterId),
            _session.ChapterSpeakerCharacterIds.Values);

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
    /// 캐스팅에 고를 수 있는 캐릭터들 — 출처가 <b>셋</b>이다:
    /// 초상화 폴더 · 정의 파일 speakers · <b>챕터 `화자` 시트의 캐릭터키</b>.
    ///
    /// 셋째가 빠져 있었다 (2026-08-17 소유자 보고: "기획자가 지정한 캐릭터도 안 보이고").
    /// 기획자가 챕터 시트에만 적어 둔 캐릭터는 목록에 아예 나오지 않았다 — 초상화가 아직
    /// 없어도 <b>이름은 정해진 것</b>이니 고를 수 있어야 한다(표정 단추와 같은 구멍이었다).
    /// </summary>
    internal static string[] CastingCandidates(
        IEnumerable<string> portraits,
        IEnumerable<string?> defined,
        IEnumerable<string> chapterSheet) =>
        portraits
            .Concat(defined.Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>())
            .Concat(chapterSheet)
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
