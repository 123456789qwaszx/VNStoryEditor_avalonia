using Avalonia.Controls;
using Vn.App.Services;
using Vn.Authoring.Assets;
using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.App.Views;

/// <summary>
/// 직접 조작의 편집 대상. null이면 조작할 것이 없는 화면이고,
/// <see cref="DisabledReason"/>이 있으면 보이지만 잠긴 화면이다(발행 결과 등) —
/// 기능 잠금이 아니라 안내가 함께 간다.
/// </summary>
internal sealed record StageEditContext(
    string PresentationNodeId,
    string? LineId,
    string? DisabledReason = null)
{
    public bool Editable => DisabledReason is null && LineId is not null;
}

/// <summary>
/// 선택지 제시 화면의 옵션 버튼 하나.
/// <see cref="LineId"/>·<see cref="BlockLineId"/>가 있으면 클릭이 그 갈래 선택이 된다 (W35).
/// <see cref="IsTakenBranch"/>는 현재 프리뷰가 접고 있는 갈래 표시다(커서 강조와 별개).
/// </summary>
internal sealed record StageChoiceOption(
    string Text,
    bool IsSelected,
    string? LineId = null,
    string? BlockLineId = null,
    bool IsTakenBranch = false);

/// <summary>
/// 선택지 버튼 목록 조립의 단일 구현 (W35) — 대사·연출 편집기가 같은 규칙 하나를 쓴다.
/// 블록 키 = 첫 라벨의 LineId, 현재 접히는 갈래(IsTakenBranch)는 갈래 분석 블록에서 온다.
/// </summary>
internal static class StageChoiceOptions
{
    public static IReadOnlyList<StageChoiceOption>? Build(
        IReadOnlyList<ChoiceOptionBundle.Option>? bundle,
        Func<int, string> lineIdOf,
        IReadOnlyList<BranchFlow.Block>? branchBlocks)
    {
        if (bundle is null || bundle.Count == 0)
        {
            return null;
        }

        string blockLineId = lineIdOf(bundle[0].LineIndex);
        BranchFlow.Block? block = branchBlocks?.FirstOrDefault(item =>
            string.Equals(item.BlockLineId, blockLineId, StringComparison.Ordinal));

        string? takenLineId = block is { SelectedBranch: { } selected } &&
            selected >= 0 && selected < block.Branches.Count
                ? block.Branches[selected].LineId
                : null;

        return bundle
            .Select(option =>
            {
                string lineId = lineIdOf(option.LineIndex);
                return new StageChoiceOption(
                    option.Text,
                    option.IsSelected,
                    lineId,
                    blockLineId,
                    IsTakenBranch: string.Equals(lineId, takenLineId, StringComparison.Ordinal));
            })
            .ToArray();
    }
}

/// <summary>
/// 이 라인의 소리 커맨드 표시 문자열 (W34-b) — 정지 프레임에 그릴 것이 없는 오디오를
/// ♪ 칩으로 알린다. "오디오" 판정은 카테고리 id <c>audio</c> 규약이다: 다른 id를 쓰는
/// 게임 정의에서는 칩이 안 뜰 뿐, 잘못 접히는 것은 없다(표시 편의이지 해석 규칙이 아니다).
/// </summary>
internal static class StageAudioCues
{
    public static IReadOnlyList<string>? Of(
        PresentationCommandCatalog catalog,
        IReadOnlyList<PresentationResultCommand>? commands)
    {
        string[]? cues = AudioOf(catalog, commands)?
            .Select(command => CommandText.Format(
                catalog.Find(command.DefinitionId), command.DefinitionId, command.Arguments))
            .ToArray();

        return cues is { Length: > 0 } ? cues : null;
    }

    /// <summary>이 라인의 오디오 커맨드 원본 (W62) — 실재생 라우터가 소화한다. 판정 규약은 칩과 같다.</summary>
    public static IReadOnlyList<PresentationResultCommand>? AudioOf(
        PresentationCommandCatalog catalog,
        IReadOnlyList<PresentationResultCommand>? commands)
    {
        if (commands is null || commands.Count == 0)
        {
            return null;
        }

        PresentationResultCommand[] audio = commands
            .Where(command => string.Equals(
                catalog.Find(command.DefinitionId)?.CategoryId, "audio", StringComparison.Ordinal))
            .ToArray();

        return audio.Length > 0 ? audio : null;
    }
}

/// <summary>
/// 무대에서 수치를 만질 수 있는 이동 커맨드 하나 (W66) — 칩·궤적·슬라이더가 이것 하나를 본다.
/// 좌표는 <b>루트 공간 픽셀</b>이고, 화면으로 옮기는 것은 <see cref="StageSceneView"/>가
/// 샷 규약(<c>ShotIntentMath</c>)으로 한다.
/// </summary>
/// <param name="Arguments">현재 인자 토큰 — 슬라이더의 시작값이자 되돌릴 자리다.</param>
/// <param name="CurveKeys">
/// <see cref="Ease"/>가 <c>@이름</c>이고 프로젝트에 그 곡선이 있으면 그 키들 —
/// 재생·스크럽·미리보기가 코어 <c>CurveFunctions</c>로 이 모양을 그대로 탄다.
/// null이면 이름 곡선이 없다는 뜻이고, 런타임과 같은 기본(OutCubic)으로 물러선다
/// (내보내기 검증이 그 어긋남을 막는 자리다).
/// </param>
internal sealed record StageMotionCue(
    string CommandId,
    string DefinitionId,
    string SlotKey,
    double DeltaX,
    double DeltaY,
    double DurationFrames,
    string? Ease,
    IReadOnlyDictionary<string, string> Arguments,
    IReadOnlyList<Ked.Presentation.Core.CurveKey>? CurveKeys = null);

/// <summary>
/// 프리뷰에 합쳐지는 커맨드 스트립의 칩 하나 (W66) — 이 라인의 커맨드가 프리뷰 화면
/// 안에서 보인다. <see cref="Motion"/>이 있으면 이동 칩(⇢·궤적·슬라이더)이고,
/// 아니면서 슬라이더 선언 파라미터가 있으면 수치 칩(⚙, W68), 그 외엔 표시 칩이다.
/// <see cref="Command"/>는 편집 대상 원본 — 칩 클릭이 이 커맨드의 인자를 만진다.
/// </summary>
internal sealed record StageCommandChip(
    string Text, StageMotionCue? Motion, PresentationResultCommand Command);

/// <summary>이 라인의 커맨드 전부를 프리뷰 칩으로 (W66). 오디오는 ♪ 칩이 따로 있어 뺀다.</summary>
internal static class StageCommandChips
{
    public static IReadOnlyList<StageCommandChip>? Of(
        PresentationCommandCatalog catalog,
        IReadOnlyList<PresentationResultCommand>? lineCommands,
        IReadOnlyList<StageMotionCue>? motionCues)
    {
        if (lineCommands is null || lineCommands.Count == 0)
        {
            return null;
        }

        var chips = new List<StageCommandChip>();

        foreach (PresentationResultCommand command in lineCommands)
        {
            PresentationCommandDefinition? definition = catalog.Find(command.DefinitionId);

            if (definition is not null && string.Equals(
                    definition.CategoryId, "audio", StringComparison.Ordinal))
            {
                continue; // ♪ 칩(W34-b)의 자리다.
            }

            StageMotionCue? motion = motionCues?.FirstOrDefault(cue =>
                string.Equals(cue.CommandId, command.CommandId, StringComparison.Ordinal));

            chips.Add(new StageCommandChip(
                CommandText.Format(definition, command.DefinitionId, command.Arguments),
                motion,
                command));
        }

        return chips.Count > 0 ? chips : null;
    }
}

/// <summary>
/// 이 라인의 이동 커맨드를 무대가 만질 수 있는 모양으로 펼친다 (W66).
///
/// 무엇이 이동인지는 <b>카탈로그의 모션 선언</b>만이 정한다 — 이름으로 추측하지 않으므로
/// 선언이 없는 커맨드는 여기 오지 않고 지금처럼 텍스트·갤러리로만 편집된다.
/// 값 계산은 전부 <see cref="MotionInspection"/>이 하고 이 클래스는 고르기만 한다.
/// </summary>
internal static class StageMotionCues
{
    public static IReadOnlyList<StageMotionCue>? Of(
        PresentationCommandCatalog catalog,
        IReadOnlyList<PresentationResultCommand> setupCommands,
        IReadOnlyList<MiniStageFoldLine> foldLines,
        IReadOnlyList<PresentationResultCommand>? lineCommands,
        Ked.Presentation.Core.StageReducerTuning? tuning,
        IReadOnlyList<EaseCurve>? curves = null)
    {
        if (tuning is null || lineCommands is null || lineCommands.Count == 0)
        {
            return null;
        }

        var cues = new List<StageMotionCue>();

        foreach (PresentationResultCommand command in lineCommands)
        {
            if (catalog.Find(command.DefinitionId) is not { Motion: not null } definition)
            {
                continue;
            }

            // 시작 자리는 "이 커맨드 직전"의 상태다 — 접힌 자리에서 빼서 구하지 않는다.
            Ked.Presentation.Core.StageState? before = CoreStageFold
                .Fold(catalog, setupCommands, foldLines, tuning, stopBeforeCommandId: command.CommandId)
                .CoreState;

            if (before is null ||
                MotionInspection.Inspect(definition, command, before, tuning) is not { } segment)
            {
                continue;
            }

            // @이름 → 프로젝트 곡선의 키. 못 찾으면 null — 런타임과 같은 폴백(OutCubic)이고,
            // 내보내기 검증이 어긋남을 막는다.
            IReadOnlyList<Ked.Presentation.Core.CurveKey>? curveKeys = null;

            if (segment.Ease is ['@', .. var curveName])
            {
                curveKeys = curves?.FirstOrDefault(curve =>
                    string.Equals(curve.Name, curveName, StringComparison.Ordinal))?.Keys;
            }

            cues.Add(new StageMotionCue(
                segment.CommandId,
                command.DefinitionId,
                segment.SlotKey,
                segment.Delta.X,
                segment.Delta.Y,
                segment.DurationFrames,
                segment.Ease,
                command.Arguments,
                curveKeys));
        }

        return cues.Count > 0 ? cues : null;
    }
}

/// <summary>공유 무대 프리뷰에 밀어 넣는 요청 하나. 폴드는 호출자가 이미 끝냈다.</summary>
/// <param name="HasPresentation">false면 연출 공급이 없는 것 — 오류가 아니라 화자만 표시한다.</param>
/// <param name="LineIndex">문서에서 선택 라인의 0기준 위치. 없으면 -1 — 창 하단 표시용.</param>
/// <param name="Notice">선택 라인이 발행본에 없다는 등 호출자가 덧붙이는 알림.</param>
/// <param name="ChoiceOptions">
/// 선택 라인이 옵션 라벨이면 그 블록의 옵션 전부. 라벨은 대사가 아니므로 대사창 대신
/// 화면 중앙에 버튼 묶음으로 제시된다 — 런타임의 선택지 제시 근사.
/// </param>
/// <param name="CoreState">
/// 코어 리듀서가 접은 확정 무대 상태 (W25). 있으면 배치가 실제 좌표(리그 트리 + 샷)로
/// 그려지고, 없으면(tuning 미수입) 기존 균등 나열 근사 + "좌표 근사" 뱃지다.
/// </param>
internal sealed record MiniStagePreviewRequest(
    string ContextLabel,
    MiniStageState State,
    bool HasPresentation,
    string? SelectedLineId,
    string? SpeakerName,
    string? LineText,
    string? Notice = null,
    int LineIndex = -1,
    int LineCount = 0,
    StageEditContext? EditContext = null,
    IReadOnlyList<StatFold.StatValue>? Stats = null,
    IReadOnlyList<StageChoiceOption>? ChoiceOptions = null,
    Ked.Presentation.Core.StageState? CoreState = null,
    IReadOnlyList<BranchFlow.Block>? BranchBlocks = null,
    double TransitionSeconds = 0,
    IReadOnlyList<string>? AudioCues = null,
    IReadOnlyList<string>? AutoBranchBlocks = null,
    IReadOnlyList<PresentationResultCommand>? AudioCommands = null,
    IReadOnlyList<StageMotionCue>? MotionCues = null,
    IReadOnlyList<StageCommandChip>? CommandChips = null);

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
    private PreviewAssetLibrary? _renderedLibrary;
    private bool _windowed;
    private bool _collapsed;
    private readonly Avalonia.Threading.DispatcherTimer _playbackTimer;

    /// <summary>재생 진행 모델 (W31). 도킹·분리 창의 컨트롤과 무대 클릭이 전부 이 하나를 본다.</summary>
    internal StagePlayback Playback { get; } = new();

    /// <summary>분리 창의 이전/다음 버튼과 재생 자동 진행. delta(-1/+1)를 활성 편집기가 소화한다.</summary>
    internal event Action<int>? LineMoveRequested;

    /// <summary>
    /// 문서 끝에서 다음 노드로 이어 재생 (W39) — 활성 편집기가 실행 출구를 따라
    /// 노드 전환에 성공하면 true를 돌려준다.
    /// </summary>
    internal Func<bool>? NodeExitRequested;

    /// <summary>도킹/분리 무대 어느 쪽이든 직접 조작이 편집을 만들었다 — 편집기가 다시 그린다.</summary>
    internal event Action? ManipulationApplied;

    public MiniStagePreview()
    {
        InitializeComponent();

        SceneHost.Content = _scene;
        _scene.ManipulationApplied += () => ManipulationApplied?.Invoke();
        // 갈래 선택(W35)은 편집이 아니지만 다시 접어야 한다 — 같은 재렌더 경로를 탄다.
        _scene.BranchSelectionChanged += () => ManipulationApplied?.Invoke();

        OpenWindowButton.Click += (_, _) => UiGuard.Run(_session, "프리뷰 창 열기", OpenWindow);

        // 아래로 접기 (W38) — 헤더만 남긴다. 창 표시 중 숨김과 같은 자리를 쓴다.
        CollapseButton.Click += (_, _) =>
        {
            _collapsed = !_collapsed;
            UpdatePanelVisibility();
            if (!_collapsed)
            {
                Render(); // 펼치면 최신 상태로 되살아난다
            }
        };

        // 재생 배선 — 시간의 원천은 이 타이머 하나, 라인 이동은 기존 선택 경로다.
        Playback.MoveRequested += delta => LineMoveRequested?.Invoke(delta);
        Playback.NodeExitRequested = () => NodeExitRequested?.Invoke() == true;
        _scene.PlaybackAdvance = Playback.TryAdvanceByInput;
        PlaybackHost.Content = StagePlaybackControls.Build(Playback);

        // 재생 배율은 툴 편의 설정 — 세션을 넘어 기억된다 (W34-a).
        Playback.SpeedMultiplier = AppSettingsService.LoadPlaybackSpeed();
        double savedSpeed = Playback.SpeedMultiplier;
        Playback.StateChanged += () =>
        {
            if (Math.Abs(Playback.SpeedMultiplier - savedSpeed) > 0.0001)
            {
                savedSpeed = Playback.SpeedMultiplier;
                AppSettingsService.SavePlaybackSpeed(savedSpeed);
            }
        };

        _playbackTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _playbackTimer.Tick += (_, _) => Playback.Tick(0.05);
        Playback.StateChanged += () => _playbackTimer.IsEnabled = Playback.IsPlaying;

        // 타자기 (W32): 글자 수 변화는 대사창 텍스트 한 곳만 갱신한다 — 전체 재렌더 없이.
        Playback.TypingProgress += SyncTypewriter;

        // 전이 (W33): 진행도 변화는 무대 자리 보간만 갱신한다 — 전체 재렌더 없이.
        Playback.TransitionChanged += SyncTransition;

        // 오디오 실재생 (W62): 정지·일시정지는 소리도 멈추고, 재생 시작은 현재 라인의
        // 소리부터 낸다 — BGM 재개는 곡 처음부터라는 근사(진행 위치 기억 없음).
        Playback.StateChanged += () =>
        {
            if (Playback.IsPlaying == _audioWasPlaying)
            {
                return;
            }

            _audioWasPlaying = Playback.IsPlaying;
            _audioFiredLineId = null;

            if (Playback.IsPlaying)
            {
                FireLineAudio(_current);
            }
            else
            {
                AudioPreview.StopAll();
            }
        };
    }

    private bool _audioWasPlaying;
    private string? _audioFiredLineId;

    /// <summary>
    /// 재생 중 새 라인에 도달했을 때 그 라인의 오디오 커맨드를 소리로 낸다 (W62).
    /// 같은 라인의 재요청(편집 반영·에셋 새로 고침)에는 다시 울리지 않는다.
    /// </summary>
    private void FireLineAudio(MiniStagePreviewRequest? request)
    {
        if (!Playback.IsPlaying || _session is null || request?.SelectedLineId is not { } lineId ||
            string.Equals(_audioFiredLineId, lineId, StringComparison.Ordinal))
        {
            return;
        }

        _audioFiredLineId = lineId;

        if (request.AudioCommands is { Count: > 0 } audio)
        {
            AudioCueRouter.Fire(_session, audio);
        }
    }

    private void SyncTypewriter()
    {
        int? visible = Playback.VisibleCharacters;
        _scene.SetDialogueVisibleCharacters(visible);
        _window?.SetDialogueVisibleCharacters(visible);
    }

    private void SyncTransition()
    {
        double? progress = Playback.TransitionProgress;
        _scene.SetTransitionProgress(progress);
        _window?.SetTransitionProgress(progress);
    }

    internal void Attach(AuthoringSession session)
    {
        _session = session;
        _scene.Attach(session);

        // 에셋 루트가 바뀌면(탐색기에서 지정·새로 고침·되돌리기) 같은 요청을 새 에셋으로 다시 그린다.
        session.Changed += (_, _) =>
        {
            if (_current is not null && !ReferenceEquals(_renderedLibrary, session.AssetLibrary))
            {
                Render();
            }
        };
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
        _renderedLibrary = library;

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
            StageIndicators.FillBadges(
                request,
                BadgeRow,
                UnhandledHost,
                _session?.BranchSelection,
                () => ManipulationApplied?.Invoke());
            StageIndicators.FillNotices(
                library,
                _session?.TuningLibrary ?? RuntimeTuningLibrary.Empty,
                request,
                NoticeHost,
                includeRootHint: true);
        }

        UpdatePanelVisibility(); // FillBadges가 되살린 표시를 숨김 상태가 덮는다

        _window?.Push(request);

        // 재생 모델에 현재 라인 위치를 알린다 — 이동 요청이 반영됐다는 신호이기도 하다.
        Playback.OnRequest(
            request?.LineIndex ?? -1,
            request?.LineCount ?? 0,
            request?.LineText,
            isChoice: request?.ChoiceOptions is { Count: > 0 },
            transitionSeconds: request?.TransitionSeconds ?? 0);
        SyncTypewriter();
        SyncTransition();
        FireLineAudio(request);
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
            _window.AttachPlayback(Playback);
            _window.MoveRequested += delta => LineMoveRequested?.Invoke(delta);
            _window.ManipulationApplied += () => ManipulationApplied?.Invoke();
            _window.Closed += (_, _) =>
            {
                _window = null;
                SetWindowedMode(false);
            };
            SetWindowedMode(true);

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

    /// <summary>
    /// 프리뷰가 분리 창으로 빠지면 도킹 패널의 무대·뱃지·알림을 숨긴다 (W37) —
    /// 같은 화면 두 개가 편집 공간만 차지한다. 창을 닫으면 되살아난다.
    /// </summary>
    private void SetWindowedMode(bool windowed)
    {
        _windowed = windowed;
        OpenWindowButton.Content = windowed ? "창으로 보는 중" : "창으로 열기";
        UpdatePanelVisibility();

        if (!windowed)
        {
            Render(); // 창을 닫으면 도킹 무대가 최신 상태로 되살아난다
        }
    }

    /// <summary>
    /// 창 표시(W37)·접기(W38)를 한 자리에서 본다 — 어느 쪽이든 무대·뱃지·알림을 숨기고
    /// 헤더 줄만 남긴다. Render가 끝날 때마다 다시 적용해 숨김이 렌더에 밀리지 않는다.
    /// </summary>
    private void UpdatePanelVisibility()
    {
        bool hidden = _windowed || _collapsed;
        SceneHost.IsVisible = !hidden;
        BadgeRow.IsVisible = !hidden;
        NoticeHost.IsVisible = !hidden;

        if (hidden)
        {
            UnhandledHost.IsVisible = false;
        }

        CollapseButton.Content = _collapsed ? "펼치기 ▸" : "접기 ▾";
    }
}
