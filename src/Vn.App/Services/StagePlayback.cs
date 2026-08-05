namespace Vn.App.Services;

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
internal sealed class StagePlayback
{
    /// <summary>타자 속도(문자/초) — 툴 편의 설정의 기본값(게임 사양이 아니다). 설정화는 W34 백로그.</summary>
    public const double CharactersPerSecond = 30;

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

    /// <summary>이동을 요청하고 아직 새 요청이 돌아오지 않은 상태 — 틱을 멈춰 중복 요청을 막는다.</summary>
    private bool _awaitingMove;

    public bool IsPlaying { get; private set; }

    /// <summary>마지막 프리뷰 요청의 라인 위치. -1이면 보여 줄 라인이 없다.</summary>
    public int LineIndex { get; private set; } = -1;

    public int LineCount { get; private set; }

    public bool CanPlay => LineCount > 0 && LineIndex >= 0;

    /// <summary>
    /// 대사창에 보일 글자 수 (W32). null이면 전문 — 타자 중일 때만 값이 있다.
    /// 일시정지·정지·선택지 화면에서는 전문이다(조작 모드는 타자를 하지 않는다).
    /// </summary>
    public int? VisibleCharacters =>
        IsPlaying && !_isChoice && _phase == Phase.Typing
            ? (int)Math.Clamp(_typedCharacters, 0, _textLength)
            : null;

    /// <summary>재생/정지·진행 표시가 바뀌었다 — 컨트롤이 라벨을 다시 그린다.</summary>
    public event Action? StateChanged;

    /// <summary>타자 글자 수가 바뀌었다 — 뷰가 대사창 텍스트만 다시 그린다.</summary>
    public event Action? TypingProgress;

    /// <summary>라인 이동 요청(delta). 활성 편집기의 선택이 움직인다 — 기존 경로 재사용.</summary>
    public event Action<int>? MoveRequested;

    /// <summary>새 프리뷰 요청이 도착했다 — 라인이 바뀌었으면 타자를 새로 시작한다.</summary>
    public void OnRequest(int lineIndex, int lineCount, string? lineText, bool isChoice = false)
    {
        LineCount = lineCount;
        _awaitingMove = false;

        if (lineIndex != LineIndex)
        {
            LineIndex = lineIndex;
            BeginLine(lineText, isChoice);
        }
        else
        {
            // 같은 라인의 재요청(에셋 새로 고침·편집 반영) — 타자 위치는 유지하되 본문은 갱신.
            _textLength = lineText?.Length ?? 0;
            _isChoice = isChoice;
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

    public void TogglePlay()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    public void Play()
    {
        if (!CanPlay)
        {
            return;
        }

        if (LineIndex >= LineCount - 1)
        {
            Restart(); // 끝에서 ▶ = 처음부터 다시
        }

        // 현재 라인의 타자를 처음부터 — 일시정지에서 돌아와도 같은 규칙이다.
        _typedCharacters = 0;
        _elapsed = 0;
        _phase = _isChoice ? Phase.WaitInput : Phase.Typing;
        IsPlaying = true;
        TypingProgress?.Invoke();
        StateChanged?.Invoke();
    }

    public void Pause()
    {
        IsPlaying = false;
        TypingProgress?.Invoke(); // 일시정지 = 전문 표시(조작 모드)
        StateChanged?.Invoke();
    }

    /// <summary>처음부터 — 재생 중이면 이어서 재생, 일시정지면 첫 라인만 보여 준다.</summary>
    public void Restart()
    {
        _elapsed = 0;
        _typedCharacters = 0;
        _phase = _isChoice ? Phase.WaitInput : Phase.Typing;

        if (LineIndex > 0)
        {
            _awaitingMove = true;
            MoveRequested?.Invoke(-LineIndex);
        }

        TypingProgress?.Invoke();
        StateChanged?.Invoke();
    }

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

        if (_phase == Phase.Typing && _typedCharacters < _textLength)
        {
            _typedCharacters = _textLength;
            _phase = Phase.Dwell;
            _elapsed = 0;
            TypingProgress?.Invoke();
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
        if (LineIndex >= LineCount - 1)
        {
            IsPlaying = false; // 끝 도달 — 멈추고 ⏮로 돌아갈 수 있다
            TypingProgress?.Invoke();
            StateChanged?.Invoke();
            return;
        }

        _awaitingMove = true;
        _elapsed = 0;
        MoveRequested?.Invoke(1);
    }

    public string ProgressLabel => CanPlay ? $"{LineIndex + 1}/{LineCount}" : "—";
}
