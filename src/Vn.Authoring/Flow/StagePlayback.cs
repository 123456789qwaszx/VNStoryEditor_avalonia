namespace Vn.Authoring.Flow;

/// <summary>
/// 무대 재생의 진행 모델 (W31·W32) — 시간의 계산만 있고 타이머·UI가 없다(테스트 가능).
///
/// 라인 하나의 진행은 세 단계다:
///   타자(글자 단위 노출) → 여운(전문 표시 후 잠깐) → 다음 라인.
/// 선택지 라인은 자동 진행 대신 클릭을 기다린다(WaitInput).
/// 진행 입력(클릭)은 타자 중이면 전문 완성, 그 뒤면 다음 라인이다 — 비주얼노벨 규약.
///
/// 시간의 원천은 호스트가 부르는 <see cref="Tick"/> 하나이고, 라인 이동은
/// <see cref="MoveRequested"/>(delta)로 기존 편집기 선택 경로에 위임한다 —
/// 재생용 별도 상태 경로를 만들지 않는다(지시서 W31-1). 이동이 실제로 반영되면
/// 새 프리뷰 요청이 <see cref="OnRequest"/>로 돌아와 다음 라인의 타자가 시작된다.
///
/// 재생 위치는 뷰 상태다 — 저장하지 않는다(원칙 E).
/// </summary>
public sealed class StagePlayback
{
    /// <summary>타자 속도(문자/초) 기본값 — 툴 편의 설정(게임 사양이 아니다). 체감 속도는 배율이 조절한다.</summary>
    public const double CharactersPerSecond = 30;

    /// <summary>배율 순환 순서 (W34-a) — 컨트롤 버튼이 이 순서로 돈다.</summary>
    public static readonly double[] SpeedSteps = [0.5, 1, 1.5, 2];

    private double _speedMultiplier = 1;

    /// <summary>
    /// 재생 속도 배율 (W34-a) — 타자·여운·전이 전부에 일괄 적용된다(시간의 원천이 하나라
    /// 배율도 한 곳이다). 툴 편의 설정이라 세션을 넘어 기억된다(AppSettings).
    /// <b>실제 게임에도 배속이 있어 남긴다</b> (2026-08-21 소유자).
    /// </summary>
    public double SpeedMultiplier
    {
        get => _speedMultiplier;
        set
        {
            double clamped = double.IsFinite(value) ? Math.Clamp(value, 0.25, 4) : 1;

            if (Math.Abs(clamped - _speedMultiplier) < 0.0001)
            {
                return;
            }

            _speedMultiplier = clamped;
            StateChanged?.Invoke();
        }
    }

    /// <summary>전문이 다 보인 뒤 다음 라인까지의 여운.</summary>
    public const double AfterTypeDwellSeconds = 1.2;

    private enum Phase
    {
        /// <summary>글자 단위 노출 중.</summary>
        Typing,

        /// <summary>전문 표시, 여운 대기.</summary>
        Dwell,

        /// <summary>선택지 제시 — 자동 진행 없음, 클릭 대기.</summary>
        WaitInput,
    }

    private Phase _phase = Phase.Typing;
    private double _typedCharacters;
    private int _textLength;
    private bool _isChoice;
    private double _elapsed;

    // 전이 (W33) — 직전 라인의 정지 프레임에서 이 라인의 정지 프레임으로 보간 중인가.
    private bool _transitionActive;
    private double _transitionDuration;
    private double _transitionElapsed;

    /// <summary>이동을 요청하고 아직 새 요청이 돌아오지 않은 상태 — 틱을 멈춰 중복 요청을 막는다.</summary>
    private bool _awaitingMove;

    public bool IsPlaying { get; private set; }

    /// <summary>마지막 프리뷰 요청의 라인 위치. -1이면 보여 줄 라인이 없다.</summary>
    public int LineIndex { get; private set; } = -1;

    public int LineCount { get; private set; }

    public bool CanPlay => LineCount > 0 && LineIndex >= 0;

    /// <summary>
    /// 전체 재생 중 — [▶] 버튼만 ⏸로 선다 (2026-08-21 소유자: "라인만 재생을 눌렀는데
    /// 재생버튼이 일시정지로 바뀌는 것도 불편"). 두 버튼이 각자 제 모드만 비춘다.
    /// </summary>
    public bool IsPlayingAll => IsPlaying && !_lineOnly;

    /// <summary>이 라인만 재생 중 — [라인] 버튼만 ⏸로 선다.</summary>
    public bool IsPlayingLine => IsPlaying && _lineOnly;

    /// <summary>
    /// 대사창에 보일 글자 수 (W32). null이면 전문 — 타자 중일 때만 값이 있다.
    /// 일시정지·정지·선택지 화면에서는 전문이다(조작 모드는 타자를 하지 않는다).
    /// </summary>
    public int? VisibleCharacters =>
        IsPlaying && !_isChoice && _phase == Phase.Typing
            ? (int)Math.Clamp(_typedCharacters, 0, _textLength)
            : null;

    /// <summary>
    /// 전이 진행도 (W33). <b>null = 정지 화면</b> — 뷰는 라인 시작의 정지 프레임을 보인다
    /// (이동 슬롯은 출발 자리, W66 소유자 결정). 값 0..1은 재생 중의 보간이고,
    /// <b>전이가 끝나도 재생 중에는 1로 남는다</b> — null로 떨어지면 이동 슬롯이
    /// 출발 자리로 되돌아가 버린다.
    /// </summary>
    public double? TransitionProgress =>
        IsPlaying && _transitionDuration > 0
            ? Math.Clamp(_transitionElapsed / _transitionDuration, 0, 1)
            : null;

    /// <summary>전이 진행도가 바뀌었다 — 뷰가 무대 자리만 보간해 옮긴다.</summary>
    public event Action? TransitionChanged;

    /// <summary>재생/정지·진행 표시가 바뀌었다 — 컨트롤이 라벨을 다시 그린다.</summary>
    public event Action? StateChanged;

    /// <summary>타자 글자 수가 바뀌었다 — 뷰가 대사창 텍스트만 다시 그린다.</summary>
    public event Action? TypingProgress;

    /// <summary>라인 이동 요청(delta). 활성 편집기의 선택이 움직인다 — 기존 경로 재사용.</summary>
    public event Action<int>? MoveRequested;

    /// <summary>
    /// 문서 끝에서 호스트에게 묻는다 (W39): 실행 출구를 따라 다음 노드로 이어질 수 있는가.
    /// true = 전환이 시작됐다 — <see cref="OnNodeSwitch"/>가 대기를 걸고 새 노드의 첫
    /// 프리뷰 요청이 푼다. null/false = 기존대로 끝에서 멈춘다.
    /// </summary>
    public Func<bool>? NodeExitRequested;

    /// <summary>새 프리뷰 요청이 도착했다 — 라인이 바뀌었으면 전이와 타자를 새로 시작한다.</summary>
    public void OnRequest(
        int lineIndex, int lineCount, string? lineText, bool isChoice = false, double transitionSeconds = 0)
    {
        LineCount = lineCount;
        _awaitingMove = false;

        if (lineIndex != LineIndex)
        {
            // 전이는 재생 중 "이전 화면이 있던" 라인 이동에만 — 첫 표시·정지 중엔 확정 상태 그대로.
            bool transition = IsPlaying && LineIndex >= 0 && lineIndex >= 0 && transitionSeconds > 0;

            LineIndex = lineIndex;
            BeginLine(lineText, isChoice);

            _transitionActive = transition;
            _transitionDuration = transitionSeconds;
            // 전이를 안 태우는 도착(첫 표시 등)은 곧장 끝 자리다 — 0으로 두면 재생 중
            // 진행도가 0에 얼어붙어 이동 슬롯이 출발 자리에 붙박인다.
            _transitionElapsed = transition ? 0 : transitionSeconds;
            TransitionChanged?.Invoke();
        }
        else
        {
            // 같은 라인의 재요청(에셋 새로 고침·편집 반영) — 타자 위치는 유지하되 본문은 갱신.
            _textLength = lineText?.Length ?? 0;
            _isChoice = isChoice;

            // 이 라인의 전이 시간도 기억한다 (W66) — ▶가 현재 라인의 이동을 다시
            // 태우려면(아래 Play) 라인 이동 없이도 시간이 최신이어야 한다.
            _transitionDuration = transitionSeconds;
        }

        if (lineIndex < 0 || lineCount <= 0)
        {
            IsPlaying = false; // 보여 줄 라인이 없으면 재생도 없다
        }

        StateChanged?.Invoke();
    }

    private void BeginLine(string? lineText, bool isChoice)
    {
        _textLength = lineText?.Length ?? 0;
        _isChoice = isChoice;
        _typedCharacters = 0;
        _elapsed = 0;
        _phase = isChoice ? Phase.WaitInput : Phase.Typing;
        TypingProgress?.Invoke();
    }

    /// <summary>
    /// [▶] — <b>전체 재생만</b> 토글한다. 라인만 재생 중에 누르면 멈추는 것이 아니라
    /// 전체 재생으로 갈아탄다(그 버튼이 뜻하는 일 하나를 그대로 한다).
    /// </summary>
    public void TogglePlay()
    {
        if (IsPlayingAll)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    /// <summary>[라인] — 이 라인만 재생을 토글한다. 전체 재생 중이면 이 라인 반복으로 갈아탄다.</summary>
    public void ToggleLinePlay()
    {
        if (IsPlayingLine)
        {
            Pause();
        }
        else
        {
            PlayCurrentLine();
        }
    }

    /// <summary>
    /// 이 라인만 재생하고 멈춘다 (2026-08-21 소유자: "현재 라인만 재생기능") —
    /// 진행 경로는 전체 재생과 완전히 같고(전이·타자·여운), 다음 라인으로 넘어가는
    /// 문턱에서만 멈춘다. 연출 하나를 다듬으며 반복해서 돌려 보는 용도다.
    /// </summary>
    public void PlayCurrentLine()
    {
        if (!CanPlay)
        {
            return;
        }

        _lineOnly = true;

        // 현재 라인의 타자·이동을 처음부터 — Play()와 같은 규칙(끝 자리 되감기 없음).
        _typedCharacters = 0;
        _elapsed = 0;
        _phase = _isChoice ? Phase.WaitInput : Phase.Typing;
        IsPlaying = true;
        _transitionElapsed = 0;
        _transitionActive = _transitionDuration > 0;

        TypingProgress?.Invoke();
        TransitionChanged?.Invoke();
        StateChanged?.Invoke();
    }

    /// <summary>라인만 재생 중 — 다음 라인 문턱에서 멈춘다.</summary>
    private bool _lineOnly;

    /// <summary>일시정지로 멈춘 상태인가 — 이어 붙는 재생과 새 재생을 가른다(detour 이력).</summary>
    private bool _pausedMidRun;

    /// <summary>
    /// <b>현재 라인부터 끝까지</b> 재생한다 (2026-08-21 소유자 지시). 예전에는 마지막
    /// 라인에서 누르면 첫 라인으로 되감아 다시 틀었는데, 그 되감기는 [⏮] 버튼과 한 쌍의
    /// 규칙이었다 — 버튼이 걷힌 지금은 ▶가 있는 자리에서 앞으로만 간다.
    /// </summary>
    public void Play()
    {
        if (!CanPlay)
        {
            return;
        }

        _lineOnly = false;

        // 새 전체 재생은 detour 이력을 안 물려받는다 — 안 그러면 두 번째 재생에서
        // 다녀온 detour가 통째로 건너뛰어진다. 일시정지에서 이어 붙는 재생은 예외다.
        if (!_pausedMidRun)
        {
            ResetDetours();
        }

        _pausedMidRun = false;

        // 현재 라인의 타자를 처음부터 — 일시정지에서 돌아와도 같은 규칙이다.
        _typedCharacters = 0;
        _elapsed = 0;
        _phase = _isChoice ? Phase.WaitInput : Phase.Typing;
        IsPlaying = true;

        // 이 라인의 이동도 처음부터 (W66) — 타자를 되감으면서 연출만 끝 상태로 두면
        // "재생"이 아니다. 시간이 0이면 탈 것이 없다.
        _transitionElapsed = 0;
        _transitionActive = _transitionDuration > 0;

        TypingProgress?.Invoke();
        TransitionChanged?.Invoke();
        StateChanged?.Invoke();
    }

    public void Pause()
    {
        // 이어 붙일 재생이다 — 다음 Play()는 detour 이력을 지우지 않는다(돌아올 자리와
        // "다녀왔음" 표시가 사라지면 재개가 같은 detour를 또 탄다).
        _pausedMidRun = true;

        IsPlaying = false;
        _lineOnly = false;
        _transitionActive = false; // 일시정지 = 확정 상태(정지 프레임)로
        TransitionChanged?.Invoke();
        TypingProgress?.Invoke(); // 일시정지 = 전문 표시(조작 모드)
        StateChanged?.Invoke();
    }

    // [⏮ 처음부터]는 2026-08-21에 걷혔다 (소유자: 배속과 함께 "거의 쓰지도 않는게 너무
    // 큰 위치를 차지"). 첫 라인으로 가려면 터미널에서 그 라인을 고르면 된다 — 재생 모델이
    // 따로 질 일이 아니었다(이동은 어차피 편집기 선택 경로로 나갔다).

    /// <summary>
    /// 재생 중 무대 클릭 — 타자 중이면 전문을 즉시 완성하고, 그 뒤면 다음 라인으로.
    /// 재생 중이 아니면 false를 돌려주고 클릭은 기존 조작(조절창)으로 흐른다.
    /// </summary>
    public bool TryAdvanceByInput()
    {
        if (!IsPlaying)
        {
            return false;
        }

        if (_awaitingMove)
        {
            return true; // 이동 반영 대기 중의 연타는 소비만 한다
        }

        // 1차 클릭 = 이 라인의 화면을 즉시 완성한다 — 전이도 타자도 한꺼번에 (W33).
        bool typingIncomplete = _phase == Phase.Typing && _typedCharacters < _textLength;

        if (_transitionActive || typingIncomplete)
        {
            if (_transitionActive)
            {
                _transitionActive = false;
                _transitionElapsed = _transitionDuration; // 즉시 완료 = 진행도 1 (끝 자리 유지)
                TransitionChanged?.Invoke();
            }

            if (typingIncomplete || _phase == Phase.Typing)
            {
                _typedCharacters = _textLength;
                _phase = Phase.Dwell;
                _elapsed = 0;
                TypingProgress?.Invoke();
            }

            return true;
        }

        Advance();
        return true;
    }

    public void Tick(double deltaSeconds)
    {
        if (!IsPlaying || _awaitingMove)
        {
            return;
        }

        deltaSeconds *= _speedMultiplier; // 배율은 시간의 입구 한 곳에서만 (W34-a)

        // 전이는 타자와 나란히 흐른다 — 움직이면서 대사가 찍히는 쪽이 재생답다.
        if (_transitionActive)
        {
            _transitionElapsed += deltaSeconds;

            if (_transitionElapsed >= _transitionDuration)
            {
                _transitionActive = false;
            }

            TransitionChanged?.Invoke();
        }

        switch (_phase)
        {
            case Phase.Typing:
                _typedCharacters += deltaSeconds * CharactersPerSecond;
                TypingProgress?.Invoke();

                if (_typedCharacters >= _textLength)
                {
                    _phase = Phase.Dwell;
                    _elapsed = 0;
                }

                break;

            case Phase.Dwell:
                if (_transitionActive)
                {
                    break; // 아직 움직이는 중 — 자리 잡은 뒤에 여운을 센다
                }

                _elapsed += deltaSeconds;

                if (_elapsed >= AfterTypeDwellSeconds)
                {
                    Advance();
                }

                break;

            case Phase.WaitInput:
                break; // 선택지 — 클릭이 올 때까지 시간이 흐르지 않는다
        }
    }

    private void Advance()
    {
        if (_lineOnly)
        {
            // 라인만 재생 — 다음 라인으로 넘어가지 않고 멈춘다. 멈춤 = 정지 화면
            // (라인 시작 순간) 규약 그대로라 전이 동기화도 알린다.
            _lineOnly = false;
            IsPlaying = false;
            _transitionActive = false;
            TransitionChanged?.Invoke();
            TypingProgress?.Invoke();
            StateChanged?.Invoke();
            return;
        }

        if (LineIndex >= LineCount - 1)
        {
            // 문서 끝 — 실행 출구로 이어질 수 있는지 호스트에게 묻는다 (W39). 전환이
            // 시작되면 OnNodeSwitch가 대기를 걸고, 새 노드의 첫 요청이 푼다(그 요청이
            // 이 호출 안에서 동기로 이미 도착했을 수도 있으므로 여기서는 더 만지지 않는다).
            if (NodeExitRequested?.Invoke() == true)
            {
                return;
            }

            IsPlaying = false; // 끝 도달, 이어질 노드 없음 — 멈추고 ⏮로 돌아갈 수 있다
            TypingProgress?.Invoke();
            StateChanged?.Invoke();
            return;
        }

        _awaitingMove = true;
        _elapsed = 0;
        MoveRequested?.Invoke(1);
    }

    /// <summary>
    /// 노드 전환 직전에 호스트가 부른다 (W39) — 위치를 초기화해 새 노드의 첫 요청이
    /// 같은 인덱스여도 새 라인으로 취급되게 하고(타자가 다시 시작된다), 노드를 넘는
    /// 전이 보간은 하지 않는다. 요청이 올 때까지 틱은 대기한다.
    /// </summary>
    public void OnNodeSwitch()
    {
        LineIndex = -1;
        _awaitingMove = true;
        _transitionActive = false;
        TransitionChanged?.Invoke();
    }

    /// <summary>
    /// 경로가 문서 끝 전에 끝났는데 이어질 노드가 없다 (W39) — 갈래 출구 뒤 라인은
    /// 실행되지 않으므로 여기서 멈춘다. 이동은 일어나지 않았으니 대기도 푼다.
    /// </summary>
    public void StopAtEnd()
    {
        // 이어 붙일 것이 없는 멈춤이다 — 다음 재생은 처음부터고, detour 이력도 새로 쌓는다.
        _pausedMidRun = false;
        _awaitingMove = false;
        _transitionActive = false;
        IsPlaying = false;
        TransitionChanged?.Invoke();
        TypingProgress?.Invoke();
        StateChanged?.Invoke();
    }

    // ── detour 복귀 (2026-08-22 소유자 보고: "detour로 중간에 다른 노드로 넘어가는 건
    //    구현이 되는데, 끝까지 재생한 뒤에 원래 노드로 돌아오지가 않네") ─────────────
    //
    // 계약(§A2-1)에서 조건 갈래의 커스텀 씬 출구는 <<jump>>가 아니라 <<detour>>다 —
    // <b>씬을 재생하고 갈래로 돌아와 나머지 대본을 계속한다</b>. 프리뷰 재생은 나가는
    // 절반만 하고 있었다. 돌아올 자리를 쌓아 두는 것이 이 스택이고, 스택이 비면 예전처럼
    // 거기서 끝난다(기본 출구 = jump는 애초에 돌아오지 않는다).

    /// <param name="NodeId">돌아갈 대사 노드.</param>
    /// <param name="ResumeAfterLineId">그 노드에서 <b>이 줄 다음</b>부터 잇는다.</param>
    /// <param name="ExitOwnerLineId">다녀온 detour를 소유한 줄 — 다시 타지 않도록 표시한다.</param>
    public readonly record struct DetourReturn(
        string NodeId,
        string ResumeAfterLineId,
        string ExitOwnerLineId);

    private readonly List<DetourReturn> _detours = new();
    private readonly Dictionary<string, HashSet<string>> _spentExits = new(StringComparer.Ordinal);

    /// <summary>이 노드에서 이미 다녀온 detour의 소유 줄들 — 경로 계산이 이걸 넘긴다.</summary>
    public IReadOnlySet<string> SpentExitsOf(string nodeId) =>
        _spentExits.TryGetValue(nodeId, out HashSet<string>? spent)
            ? spent
            : (IReadOnlySet<string>)new HashSet<string>(StringComparer.Ordinal);

    /// <summary>detour로 나가기 직전 — 돌아올 자리를 쌓는다.</summary>
    public void PushDetour(DetourReturn ret) => _detours.Add(ret);

    /// <summary>
    /// 나갈 곳이 없는 노드 끝에서 부른다 — 돌아갈 자리가 있으면 꺼내고, <b>그 detour를
    /// 다녀온 것으로 표시한다</b>(같은 출구를 또 타면 나갔다 돌아오기를 무한히 돈다).
    /// </summary>
    public DetourReturn? PopDetour()
    {
        if (_detours.Count == 0)
        {
            return null;
        }

        DetourReturn ret = _detours[^1];
        _detours.RemoveAt(_detours.Count - 1);

        if (!_spentExits.TryGetValue(ret.NodeId, out HashSet<string>? spent))
        {
            spent = new HashSet<string>(StringComparer.Ordinal);
            _spentExits[ret.NodeId] = spent;
        }

        spent.Add(ret.ExitOwnerLineId);
        return ret;
    }

    /// <summary>
    /// 재생을 처음부터 다시 시작한다 — 쌓인 복귀와 "다녀왔음" 표시, 그리고 자유 씬 뒤에
    /// 이어질 도착 에피소드를 버린다. 이 청소가 없으면 두 번째 재생에서 detour가 통째로
    /// 건너뛰어지고, 지난 재생의 선택이 이번 재생의 끝에 되살아난다.
    /// </summary>
    public void ResetDetours()
    {
        _detours.Clear();
        _spentExits.Clear();
        _pendingEpisodeTargetNodeId = null;
    }

    // ── 에피소드 끝 선택지 (2026-08-27 소유자 보고: 에피소드가 끝나도 선택지가 안 선다) ──
    //
    // 전이 규칙 v9①: 에피소드 대본이 끝나면 챕터 `간선`의 선택지가 제시된다. 간선에 자유
    // 씬(ViaNode)이 매달려 있으면 런타임은 그 씬을 재생한 뒤 도착 에피소드로 간다 — 그
    // "씬 다음 자리"를 여기 적어 두고, 나갈 곳 없는 노드 끝에서 detour 복귀 다음 순서로
    // 소비한다. detour 스택과 같은 수명이라 청소도 같은 자리다(ResetDetours).

    private string? _pendingEpisodeTargetNodeId;

    /// <summary>자유 씬으로 나가기 직전 — 씬이 끝나면 이어질 도착 에피소드 노드를 적는다.</summary>
    public void SetPendingEpisodeTarget(string nodeId) => _pendingEpisodeTargetNodeId = nodeId;

    /// <summary>적어 둔 도착 에피소드를 꺼낸다(소비) — 없으면 null.</summary>
    public string? TakePendingEpisodeTarget()
    {
        string? target = _pendingEpisodeTargetNodeId;
        _pendingEpisodeTargetNodeId = null;
        return target;
    }

    /// <summary>
    /// 에피소드 끝 선택지가 화면에 섰다 — 클릭이 올 때까지 시간이 흐르지 않는다
    /// (선택지 라인의 WaitInput과 같은 위상). 재생 중이 아니면 이미 멈춰 있으니 할 일이 없다.
    /// </summary>
    public void WaitForChoiceInput()
    {
        if (!IsPlaying)
        {
            return;
        }

        _isChoice = true;
        _phase = Phase.WaitInput;
        TypingProgress?.Invoke();
        StateChanged?.Invoke();
    }

    /// <summary>그 방향으로 옮길 라인이 있는가 — 재생 줄의 [◀ 이전]·[다음 ▶]이 이걸 본다.</summary>
    public bool CanMove(int delta) =>
        LineCount > 0 &&
        LineIndex >= 0 &&
        LineIndex + delta >= 0 &&
        LineIndex + delta < LineCount;

    /// <summary>
    /// 사람이 라인을 옮긴다 (2026-08-22 소유자 — 타임라인 오른쪽의 [◀ 이전]·[다음 ▶]).
    /// 이동 자체는 자동 진행과 <b>같은 통로</b>(<see cref="MoveRequested"/>)를 지난다 —
    /// 선택을 실제로 옮기는 것은 활성 편집기의 몫이라는 규칙이 그대로다.
    ///
    /// ⚠ <b>재생 중이면 먼저 멈춘다.</b> 자동 진행은 제가 부른 이동을 기다리는 중
    /// (<c>_awaitingMove</c>)인데 거기에 사람의 이동이 끼어들면 둘이 같은 자리를 다투고,
    /// 어느 쪽이 이겼는지 화면이 말하지 못한다.
    /// </summary>
    public void MoveBy(int delta)
    {
        if (!CanMove(delta))
        {
            return;
        }

        if (IsPlaying)
        {
            StopAtEnd();
        }

        MoveRequested?.Invoke(delta);
    }

    public string ProgressLabel => CanPlay ? $"{LineIndex + 1}/{LineCount}" : "—";
}
