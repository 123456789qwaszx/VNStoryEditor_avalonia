using Avalonia.Controls;
using Avalonia.Input.Platform;
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
/// 무대에서 수치를 만질 수 있는 이동 커맨드 하나 (W66) — <b>이동 편집기(슬라이더·이징·곡선)와
/// 휴지 배치</b>가 이것 하나를 본다. 이것을 함께 보던 무대 칩과 점선 궤적은 걷혔다
/// (2026-08-20 칩 · 2026-08-21 궤적).
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
    IReadOnlyList<PresentationScriptRow>? ScriptRows = null,
    StageMotionPlan? MotionPlan = null);

/// <summary>
/// 무대 프리뷰 판 (2026-08-20 중앙 탭 승격) — 좌측 대본 터미널 + 우측 무대.
/// 무대 그리기는 <see cref="StageSceneView"/>가 하고, 이 판은 뱃지·알림·재생과
/// 대본 패널의 신호 배선을 맡는다. 옛 분리 창·접기는 탭 승격으로 걷혔다.
/// </summary>
public partial class MiniStagePreview : UserControl
{
    private AuthoringSession? _session;
    private MiniStagePreviewRequest? _current;
    private readonly StageSceneView _scene = new();
    private PreviewAssetLibrary? _renderedLibrary;
    private readonly Avalonia.Threading.DispatcherTimer _playbackTimer;

    /// <summary>재생 진행 모델 (W31). 재생 컨트롤과 무대 클릭이 전부 이 하나를 본다.</summary>
    internal StagePlayback Playback { get; } = new();

    /// <summary>무대 뷰 — 조절창의 주인. 검증이 담기 모드를 켜는 손잡이이기도 하다.</summary>
    internal StageSceneView Scene => _scene;

    /// <summary>이전/다음·재생 자동 진행. delta(-1/+1)를 활성 편집기가 소화한다.</summary>
    internal event Action<int>? LineMoveRequested;

    /// <summary>
    /// 문서 끝에서 다음 노드로 이어 재생 (W39) — 활성 편집기가 실행 출구를 따라
    /// 노드 전환에 성공하면 true를 돌려준다.
    /// </summary>
    internal Func<bool>? NodeExitRequested;

    /// <summary>무대 직접 조작이 편집을 만들었다 — 편집기가 다시 그린다.</summary>
    internal event Action? ManipulationApplied;

    /// <summary>대본 패널의 대사 행 클릭 — 활성 편집기가 그 라인을 선택한다.</summary>
    internal event Action<string>? LineSelectRequested;

    /// <summary>
    /// detour에서 돌아왔다 (2026-08-22) — <b>지금 활성인</b> 편집기가 그 줄을 고르고 한 줄
    /// 나아간다. 셸이 라우팅하는 이유는 하나다: 나가는 노드의 편집기와 돌아가는 노드의
    /// 편집기가 서로 다를 수 있다(연출 노드 ↔ 커스텀 대사 노드).
    /// </summary>
    internal event Action<string>? DetourResumeRequested;

    /// <summary>편집기가 복귀를 확정한 뒤 부른다 — 선택은 이미 그 노드로 옮겨져 있다.</summary>
    internal void RequestDetourResume(string lineId) => DetourResumeRequested?.Invoke(lineId);

    /// <summary>
    /// 씬 선택기에서 대사 노드 하나를 골랐다 (2026-08-21) — MainWindow가 연출 채널을
    /// 확보하고(EnsurePresentationChannel) 그 채널을 연다. 값은 대사 노드 Id다.
    /// </summary>
    internal event Action<string>? SceneChosen;

    private bool _updatingSceneCombo;

    private readonly PresentationScriptPanel _script = new();

    /// <summary>
    /// 선택 커맨드 하나의 Inspector 공급자 (2026-08-21 소유자: "일종의 Inspector") —
    /// 활성 편집기가 커맨드 행 + 수치 조절을 한 판으로 짓는다. MainWindow가 배선한다.
    /// </summary>
    internal Func<string, Control?>? CommandDetailProvider { get; set; }

    /// <summary>
    /// 연출 추가 콘솔 공급자 — (lineId, setup, close)를 받아 콘솔 한 판을 짓는다.
    /// 터미널 우클릭 [＋ 연출 추가]가 플라이아웃으로 띄운다 (2026-08-21 소유자: "아래쪽에서
    /// 보이게 한다는 느낌 대신에 콘솔을 띄운다는 느낌으로"). MainWindow가 배선한다.
    /// </summary>
    internal Func<string?, bool, Action, Control?>? AddConsoleProvider { get; set; }

    /// <summary>터미널의 Setup 구획이 선택돼 있다 — 작업대가 라인 대신 Setup을 보인다.</summary>
    private bool _setupSelected;

    /// <summary>
    /// 복사한 커맨드 (2026-08-21 소유자: 복사/붙여넣기 + Ctrl+C/V) — 결과 행의 해석된
    /// 값을 담으므로 프리셋 참조 없이 자립한다. 정적이라 노드를 오가며 붙일 수 있다.
    /// </summary>
    private static (string DefinitionId, Dictionary<string, string> Arguments, string? Note)? _commandClipboard;

    public MiniStagePreview()
    {
        InitializeComponent();

        SceneHost.Content = _scene;
        ScriptHost.Content = _script;

        // 목록은 열 때마다 다시 짓는다 — 노드 생성·개명·동기화를 따로 구독하지 않아도
        // 여는 순간이 곧 최신이다.
        SceneCombo.DropDownOpened += (_, _) => RebuildScenePicker(CurrentComboSceneId());
        SceneCombo.SelectionChanged += (_, _) =>
        {
            if (!_updatingSceneCombo &&
                SceneCombo.Tag is List<DialogueNode> scenes &&
                SceneCombo.SelectedIndex >= 0 &&
                SceneCombo.SelectedIndex < scenes.Count)
            {
                SceneChosen?.Invoke(scenes[SceneCombo.SelectedIndex].Id);
            }
        };
        _script.LineClicked += lineId =>
        {
            // 라인 선택은 Setup 선택을 걷는다. 같은 라인 재클릭이라 편집기가 다시 밀지
            // 않아도 작업대 전환은 보여야 하므로 여기서도 한 번 그린다.
            _setupSelected = false;
            LineSelectRequested?.Invoke(lineId);
            Render();
        };
        _script.SetupClicked += () =>
        {
            _setupSelected = true;
            Render();
        };
        // 커맨드 선택 = Inspector 전환 (2026-08-21) — 점은 선택이 아니라 켜고 끄기다.
        _script.CommandSelected += _ =>
        {
            Render();
        };
        _script.CommandToggleRequested += ApplyScriptCommandToggle;
        _script.CommandMoveRequested += ApplyScriptCommandMove;
        _script.CommandRemoveRequested += ApplyScriptCommandRemove;
        // 우클릭 메뉴 + Ctrl+C/V/D (2026-08-21 소유자 지시).
        _script.AddCommandRequested += OnAddCommandRequested;
        _script.CommandCopyRequested += CopyScriptCommand;
        _script.CommandDuplicateRequested += ApplyScriptCommandDuplicate;
        _script.CommandPasteRequested += ApplyScriptCommandPaste;
        // 조절창이 무대 오른쪽 붙박이 기둥으로 왔다 (2026-08-22) — 무대 뷰가 짓고
        // 여기가 자리를 준다. 이후 갱신은 무대가 다시 그릴 때마다 스스로 한다.
        ConsoleHost.Content = _scene.BuildDockedConsole();

        _script.CommandPinRequested += PinQuickCommand;
        // 조절창의 [＋ 이 라인 통째로]도 같은 담기 함수로 들어온다 — 입구는 둘, 규칙은 하나.
        _scene.QuickPinRequested += PinQuickCommands;
        _script.HasClipboardCommand = () => _commandClipboard is not null;
        // 담기 모드가 켜지고 꺼졌다 — 터미널의 ★를 다시 그린다. 조절창은 안 건드린다
        // (Render는 캔버스와 터미널만 만지고 팝업은 제 자리에 남는다).
        _scene.QuickEditModeChanged += Render;
        _scene.ManipulationApplied += () => ManipulationApplied?.Invoke();
        // 갈래 선택(W35)은 편집이 아니지만 다시 접어야 한다 — 같은 재렌더 경로를 탄다.
        _scene.BranchSelectionChanged += () => ManipulationApplied?.Invoke();

        // 재생 배선 — 시간의 원천은 이 타이머 하나, 라인 이동은 기존 선택 경로다.
        Playback.MoveRequested += delta => LineMoveRequested?.Invoke(delta);
        Playback.NodeExitRequested = () => NodeExitRequested?.Invoke() == true;
        _scene.PlaybackAdvance = Playback.TryAdvanceByInput;
        // 재생 컨트롤은 타임라인을 사이에 두고 양 끝으로 갈린다 (2026-08-21 소유자) —
        // 왼쪽은 지금 다듬는 라인의 것, 오른쪽 끝은 가끔 쓰는 것(노드 전체 재생·배속).
        PlaybackHost.Content = StagePlaybackControls.BuildLeading(Playback);
        LineStepHost.Content = StagePlaybackControls.BuildLineStep(Playback);
        PlaybackTrailingHost.Content = StagePlaybackControls.BuildTrailing(Playback);

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

    }

    private void SyncTransition()
    {
        double? progress = Playback.TransitionProgress;
        _scene.SetTransitionProgress(progress);

    }

    internal void Attach(AuthoringSession session)
    {
        _session = session;
        _scene.Attach(session);
        RebuildScenePicker(null);

        // 에셋 루트가 바뀌면(탐색기에서 지정·새로 고침·되돌리기) 같은 요청을 새 에셋으로 다시 그린다.
        session.Changed += (_, _) =>
        {
            if (_current is not null && !ReferenceEquals(_renderedLibrary, session.AssetLibrary))
            {
                Render();
            }
        };
    }

    /// <summary>무대 뷰의 수치 조절 내용 — Inspector 공급자(MainWindow 배선)가 조합해 쓴다.</summary>
    internal Control? BuildSceneInspector(PresentationResultCommand command) =>
        _scene.BuildInspectorContent(command);

    // ⚠ [진단 복사] 단추와 그 뒤의 `DiagnosticsText`·`CopyDiagnosticsAsync`는 2026-08-24에
    //   걷혔다 (소유자). 남은 복사 통로는 <b>줄 드래그</b>다 — 알림·미반영 줄은 그대로
    //   SelectableTextBlock이라 마우스로 긁어 가져간다. 챕터 그래프의 [보고 복사]는
    //   그쪽 검증 보고의 것이고 이것과 무관하다(살아 있다).

    // ── 씬 선택기 (2026-08-21) ──────────────────────────────────────────────

    /// <summary>지금 콤보가 가리키는 대사 노드 Id — 목록을 다시 지어도 선택을 지킨다.</summary>
    private string? CurrentComboSceneId()
    {
        return SceneCombo.Tag is List<DialogueNode> scenes &&
            SceneCombo.SelectedIndex >= 0 &&
            SceneCombo.SelectedIndex < scenes.Count
                ? scenes[SceneCombo.SelectedIndex].Id
                : null;
    }

    /// <summary>
    /// 바깥(그래프 클릭 등)에서 씬이 바뀌었다 — 콤보를 따라오게 한다. 이벤트는 안 쏜다.
    /// null이면 선택만 비운다(대사·연출이 아닌 노드).
    /// </summary>
    internal void SetCurrentScene(string? dialogueNodeId) => RebuildScenePicker(dialogueNodeId);

    /// <summary>
    /// 연출할 수 있는 씬 목록 — 프로젝트의 모든 대사 노드. 에피소드(📄)가 먼저,
    /// 커스텀 씬(✎)이 뒤다(파일 순서 그대로 — 판의 읽는 순서와 같다).
    /// </summary>
    private void RebuildScenePicker(string? selectedDialogueId)
    {
        if (_session is null)
        {
            return;
        }

        List<DialogueNode> scenes = _session.Project.EnumerateNodes()
            .OfType<DialogueNode>()
            .OrderBy(node => node.ExcelEpisodeId is null ? 1 : 0)
            .ToList();

        _updatingSceneCombo = true;

        try
        {
            SceneCombo.ItemsSource = scenes
                .Select(node => $"{(node.ExcelEpisodeId is null ? "✎" : "📄")} {node.Name}")
                .ToList();
            SceneCombo.Tag = scenes;
            SceneCombo.SelectedIndex = selectedDialogueId is null
                ? -1
                : scenes.FindIndex(node =>
                    string.Equals(node.Id, selectedDialogueId, StringComparison.Ordinal));
        }
        finally
        {
            _updatingSceneCombo = false;
        }
    }

    /// <summary>점 클릭 = 켜고 끄기 (2026-08-21) — 꺼진 커맨드도 행으로 남아 다시 켤 수 있다.</summary>
    private void ApplyScriptCommandToggle(PresentationResultCommand command, bool currentlyEnabled)
    {
        if (_session is null || _current?.EditContext is not { DisabledReason: null } context)
        {
            return;
        }

        UiGuard.Run(_session, "커맨드 켜고 끄기", () =>
        {
            _session.Editor.SetPresentationCommandEnabled(
                context.PresentationNodeId, command.CommandId, !currentlyEnabled);
            ManipulationApplied?.Invoke();
        });
    }

    /// <summary>null이면 보여 줄 라인이 없는 상태다(노드 미선택 등).</summary>
    internal void Show(MiniStagePreviewRequest? request)
    {
        _current = request;
        Render();
    }

    /// <summary>드래그 이동 적용 — 편집 통로 하나(<see cref="ProjectEditor.MovePresentationCommand"/>), undo 한 번.</summary>
    private void ApplyScriptCommandMove(
        PresentationResultCommand command, string? targetLineId, int insertIndex)
    {
        if (_session is null || _current?.EditContext is not { Editable: true } context)
        {
            return;
        }

        UiGuard.Run(_session, "커맨드 자리 이동", () =>
        {
            _session.Editor.MovePresentationCommand(
                context.PresentationNodeId, command.CommandId, targetLineId, insertIndex);
            ManipulationApplied?.Invoke();
        });
    }

    /// <summary>
    /// 작업대가 비었을 때의 안내 — <b>영역은 그대로 두고</b> 글자만 바꾼다 (2026-08-21
    /// 소유자: 클릭마다 접혔다 펴지며 터미널 높이가 흔들리던 것). 이 높이를 바꾸는 것은
    /// 사람의 스플리터 드래그뿐이다.
    /// </summary>
    private static Control BuildEmptyDetailHint() => new TextBlock
    {
        Text = "터미널에서 커맨드를 클릭하면 여기에서 수치를 조절합니다. " +
            "우클릭 메뉴로 연출 추가·복사·붙여넣기(Ctrl+C/V/D)를 씁니다.",
        FontSize = 11,
        Opacity = 0.4,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap
    };

    /// <summary>
    /// 우클릭 [＋ 연출 추가] — 그 자리(라인 또는 Setup)를 선택 상태로 맞추고 연출 추가
    /// 콘솔을 플라이아웃으로 띄운다 (2026-08-21 소유자: "아래쪽에서 보이게 한다는 느낌
    /// 대신에 콘솔을 띄운다는 느낌으로"). 플라이아웃의 과녁은 터미널 패널 자신이다 —
    /// 선택 이동이 터미널을 다시 그려 우클릭한 행이 사라져도 콘솔은 제자리에 남는다.
    /// </summary>
    private void OnAddCommandRequested(string? lineId, bool setup)
    {
        if (setup)
        {
            _setupSelected = true;
        }
        else if (lineId is not null)
        {
            _setupSelected = false;

            if (!string.Equals(_current?.SelectedLineId, lineId, StringComparison.Ordinal))
            {
                LineSelectRequested?.Invoke(lineId);
            }
        }

        Render();

        var flyout = new Flyout();
        Control? console = AddConsoleProvider?.Invoke(setup ? null : lineId, setup, () => flyout.Hide());

        if (console is null)
        {
            return;
        }

        flyout.Content = console;
        flyout.ShowAt(_script, showAtPointer: true);
    }

    /// <summary>복사 = 결과 행의 해석된 값을 담는다 — 붙여넣은 커맨드는 원본과 독립이다.</summary>
    private void CopyScriptCommand(PresentationResultCommand command)
    {
        _commandClipboard = (
            command.DefinitionId,
            new Dictionary<string, string>(command.Arguments, StringComparer.Ordinal),
            command.Note);
        _session?.SetStatus("커맨드를 복사했습니다 — 붙여넣기(Ctrl+V)로 원하는 라인에 답니다.");
    }

    /// <summary>
    /// 터미널 행 우측 ★ (2026-08-22 소유자) — 이 커맨드를 [★ 자주 쓰는] 칩으로 담는다.
    ///
    /// 세 가지가 이 함수의 요구다:
    /// ① <b>인자를 통째로 복사한다</b> — 슬롯도 duration도 그대로("그대로 복사하도록 하는게
    ///    포인트"). 도구가 무엇을 남길지 고르지 않는다.
    /// ② <b>조절창을 닫지 않는다</b> — 담기는 <c>Content</c> 변경이라 셸이 화면을 다시
    ///    짓지 않고(<c>ProjectRefreshPlanner</c>), 팝업의 라이트 디스미스도 꺼져 있다.
    /// ③ <b>바로 보인다</b> — <c>RefreshConsole()</c>이 열린 판을 그 자리에서 다시 그린다.
    ///
    /// 이름은 묻지 않는다(흐름이 끊긴다). 정의 이름을 붙이되 <b>같은 이름이 이미 있으면
    /// 번호를 단다</b> — 같은 커맨드를 값만 달리해 담는 것이 이 판의 정상 쓰임이라 이름이
    /// 겹치는 것이 예외가 아니다. 고치는 자리는 조절창 [편집]의 이름 칸이다.
    /// </summary>
    /// <summary>담기 모드 행 툴팁의 문장 — 조절창이 정한 목적지를 그대로 옮긴다.</summary>
    private string QuickPinHint()
    {
        IReadOnlyList<StageQuickCommand> chips =
            _session?.Project.EffectiveQuickCommands ?? [];

        return _scene.QuickPinTarget is { } target && target >= 0 && target < chips.Count
            ? $"클릭하면 '{chips[target].DisplayName}'의 {chips[target].Steps.Count + 1}번째 단계로 담깁니다."
            : "클릭하면 [★ 자주 쓰는]에 새 칩으로 담습니다 (슬롯·시간 그대로).";
    }

    private void PinQuickCommand(PresentationResultCommand command) => PinQuickCommands([command]);

    /// <summary>
    /// 담기의 유일한 통로 (2026-08-24 — 묶음 칩) — 커맨드 목록 하나가 칩이 된다.
    /// 터미널 행 하나를 집는 것도, 조절창의 [＋ 이 라인 통째로]도 여기로 들어온다:
    /// 이름 짓기와 <b>담을 곳 판정</b>이 두 벌이 되면 두 입구가 다르게 굴기 시작한다.
    ///
    /// <b>담을 곳은 조절창이 정한다</b>(<see cref="StageSceneView.QuickPinTarget"/>):
    /// 편집 중에 펼쳐 놓은 칩이 있으면 그 뒤에 단계로 붙고, 없으면 새 칩이다.
    /// 규칙이 하나라 "이번 클릭이 어디로 가나"를 화면(안내 줄)이 늘 말할 수 있다.
    /// </summary>
    private void PinQuickCommands(IReadOnlyList<PresentationResultCommand> commands)
    {
        if (_session is null || commands.Count == 0)
        {
            return;
        }

        PresentationCommandCatalog catalog = PresentationCommandCatalog.For(_session.Definition);

        List<(PresentationCommandDefinition Definition, StageQuickStep Step)> steps = commands
            .Select(command => (Definition: catalog.Find(command.DefinitionId), Command: command))
            .Where(pair => pair.Definition is not null)
            .Select(pair => (pair.Definition!, new StageQuickStep(
                pair.Definition!.Id,
                new Dictionary<string, string>(pair.Command.Arguments, StringComparer.Ordinal))))
            .ToList();

        if (steps.Count == 0)
        {
            _session.SetStatus("카탈로그에 없는 커맨드는 담을 수 없습니다.");
            return;
        }

        IReadOnlyList<StageQuickCommand> chips = _session.Project.EffectiveQuickCommands;

        if (_scene.QuickPinTarget is { } target && target >= 0 && target < chips.Count)
        {
            string chipName = chips[target].DisplayName;
            int total = chips[target].Steps.Count + steps.Count;

            UiGuard.Run(_session, "자주 쓰는 데 담기", () =>
            {
                _session.Editor.AppendQuickCommandSteps(target, steps.Select(pair => pair.Step).ToList());
                _session.SetStatus($"'{chipName}'에 {steps.Count}개를 이어 담았습니다 (총 {total}단계).");
            });

            _scene.RefreshConsole();
            return;
        }

        // 새 칩의 이름 — 한 개면 그 커맨드 이름, 묶음이면 "첫 커맨드 외 N". 묻지 않는 이유는
        // 담는 순간 이름을 물으면 흐름이 끊기기 때문이다(2026-08-22). 고치는 자리는 이름 칸이다.
        string displayName = _session.Editor.UniqueQuickCommandName(steps.Count == 1
            ? steps[0].Definition.DisplayName
            : $"{steps[0].Definition.DisplayName} 외 {steps.Count - 1}");

        UiGuard.Run(_session, "자주 쓰는 데 담기", () =>
        {
            _session.Editor.PinQuickCommand(new StageQuickCommand(
                displayName,
                steps.Select(pair => pair.Step).ToList()));
            _session.SetStatus(steps.Count == 1
                ? $"'{displayName}' 칩을 담았습니다."
                : $"'{displayName}' 칩을 담았습니다 ({steps.Count}단계).");
        });

        _scene.RefreshConsole();
    }

    /// <summary>Ctrl+D / [복제] — 원본 바로 뒤에, 편집 통로 하나(undo 한 번)로.</summary>
    private void ApplyScriptCommandDuplicate(PresentationResultCommand command)
    {
        if (_session is null || _current?.EditContext is not { DisabledReason: null } context)
        {
            return;
        }

        UiGuard.Run(_session, "커맨드 복제", () =>
        {
            _session.Editor.DuplicatePresentationCommand(context.PresentationNodeId, command.CommandId);
            ManipulationApplied?.Invoke();
        });
    }

    /// <summary>Ctrl+V / [붙여넣기] — 클립보드 커맨드를 그 자리(라인 끝 또는 Setup 끝)에 단다.</summary>
    private void ApplyScriptCommandPaste(string? lineId, bool setup)
    {
        if (_session is null || _commandClipboard is not { } clip ||
            _current?.EditContext is not { DisabledReason: null } context ||
            (!setup && lineId is null))
        {
            return;
        }

        UiGuard.Run(_session, "커맨드 붙여넣기", () =>
        {
            if (setup)
            {
                _session.Editor.AddPresentationSetupCommand(
                    context.PresentationNodeId, clip.DefinitionId, clip.Arguments, note: clip.Note);
            }
            else
            {
                _session.Editor.AddPresentationCommand(
                    context.PresentationNodeId, lineId!, clip.DefinitionId, clip.Arguments, note: clip.Note);
            }

            ManipulationApplied?.Invoke();
        });
    }

    /// <summary>터미널 행 우측 X (2026-08-21) — 작업대의 ✕ 버튼을 대체한 제거 통로다.</summary>
    private void ApplyScriptCommandRemove(PresentationResultCommand command)
    {
        // Setup 커맨드는 LineId 없이도 편집 대상이다 — Editable(라인 필요) 대신
        // 잠금 사유 없음만 본다.
        if (_session is null || _current?.EditContext is not { DisabledReason: null } context)
        {
            return;
        }

        UiGuard.Run(_session, "커맨드 제거", () =>
        {
            _session.Editor.RemovePresentationCommand(context.PresentationNodeId, command.CommandId);
            ManipulationApplied?.Invoke();
        });
    }

    private void Render()
    {
        MiniStagePreviewRequest? request = _current;
        PreviewAssetLibrary library = _session?.AssetLibrary ?? PreviewAssetLibrary.Empty;
        _renderedLibrary = library;

        ContextText.Text = request?.ContextLabel ?? string.Empty;
        _scene.Render(request);

        bool editable = request?.EditContext?.Editable == true;
        // Editable(라인 선택 필요)과 달리 Setup은 라인 없이도 편집 대상 — 잠금 사유만 본다.
        bool unlocked = request?.EditContext is { DisabledReason: null };
        // 담기 모드의 주인은 조절창 하나다 (2026-08-22) — 여기서 매번 물어보므로 두 화면의
        // 모드가 어긋날 자리가 없다(사본 금지).
        _script.PinMode = _scene.IsQuickPinMode;
        _script.PinHint = QuickPinHint();
        _script.Show(request?.ScriptRows, request?.SelectedLineId, unlocked, _setupSelected);
        ScriptHost.IsVisible = _script.IsVisible;

        // 작업대 = Inspector (2026-08-21 소유자) — 평소에는 비어 있고, 커맨드를 고르면
        // 그 하나의 편집 행 + 수치 조절이 선다. 추가 UI는 여기 안 선다 — 터미널 우클릭
        // [＋ 연출 추가]가 콘솔을 플라이아웃으로 띄운다(OnAddCommandRequested).
        // 공급자가 같은 구성을 캐시하므로 칩 편집 중 재렌더에 팝업이 닫히지 않는다.
        Control? detail = null;

        if (unlocked)
        {
            if (_script.SelectedCommandId is { } selectedCommandId)
            {
                detail = CommandDetailProvider?.Invoke(selectedCommandId);
            }
        }

        DetailHost.Content = detail ?? BuildEmptyDetailHint();

        // 프레임 타임라인 (2026-08-21) — 무대 아래 재생 줄에 이 라인의 내부 시간이 선다.
        TimelineHost.Content = _scene.BuildTimelineScrubber();

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
}
