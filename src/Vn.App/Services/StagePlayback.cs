namespace Vn.App.Services;

/// <summary>
/// 무대 재생의 진행 모델 (W31) — 시간의 계산만 있고 타이머·UI가 없다(테스트 가능).
///
/// 시간의 원천은 호스트가 부르는 <see cref="Tick"/> 하나이고, 라인 이동은
/// <see cref="MoveRequested"/>(delta)로 기존 편집기 선택 경로에 위임한다 —
/// 재생용 별도 상태 경로를 만들지 않는다(지시서 W31-1). 이동이 실제로 반영되면
/// 새 프리뷰 요청이 <see cref="OnRequest"/>로 돌아와 다음 라인의 체류가 시작된다.
///
/// 재생 위치는 뷰 상태다 — 저장하지 않는다(원칙 E).
/// </summary>
internal sealed class StagePlayback
{
    /// <summary>라인당 체류 시간 규약 — 대사 길이 비례 + 최소/최대. 타자기(W32)가 오면
    /// "타자 완료 + 여운"으로 대체된다.</summary>
    public const double MinDwellSeconds = 1.4;

    public const double PerCharacterSeconds = 0.045;

    public const double MaxDwellSeconds = 6.0;

    private double _elapsed;
    private double _dwell = MinDwellSeconds;

    /// <summary>이동을 요청하고 아직 새 요청이 돌아오지 않은 상태 — 틱을 멈춰 중복 요청을 막는다.</summary>
    private bool _awaitingMove;

    public bool IsPlaying { get; private set; }

    /// <summary>마지막 프리뷰 요청의 라인 위치. -1이면 보여 줄 라인이 없다.</summary>
    public int LineIndex { get; private set; } = -1;

    public int LineCount { get; private set; }

    public bool CanPlay => LineCount > 0 && LineIndex >= 0;

    /// <summary>재생/정지·진행 표시가 바뀌었다 — 컨트롤이 라벨을 다시 그린다.</summary>
    public event Action? StateChanged;

    /// <summary>라인 이동 요청(delta). 활성 편집기의 선택이 움직인다 — 기존 경로 재사용.</summary>
    public event Action<int>? MoveRequested;

    public static double DwellFor(string? lineText)
    {
        return Math.Clamp(
            MinDwellSeconds + (lineText?.Length ?? 0) * PerCharacterSeconds,
            MinDwellSeconds,
            MaxDwellSeconds);
    }

    /// <summary>새 프리뷰 요청이 도착했다 — 라인이 바뀌었으면 체류를 새로 시작한다.</summary>
    public void OnRequest(int lineIndex, int lineCount, string? lineText)
    {
        LineCount = lineCount;
        _awaitingMove = false;

        if (lineIndex != LineIndex)
        {
            LineIndex = lineIndex;
            _elapsed = 0;
            _dwell = DwellFor(lineText);
        }

        if (lineIndex < 0 || lineCount <= 0)
        {
            IsPlaying = false; // 보여 줄 라인이 없으면 재생도 없다
        }

        StateChanged?.Invoke();
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

        IsPlaying = true;
        _elapsed = 0;
        StateChanged?.Invoke();
    }

    public void Pause()
    {
        IsPlaying = false;
        StateChanged?.Invoke();
    }

    /// <summary>처음부터 — 재생 중이면 이어서 재생, 일시정지면 첫 라인만 보여 준다.</summary>
    public void Restart()
    {
        _elapsed = 0;

        if (LineIndex > 0)
        {
            _awaitingMove = true;
            MoveRequested?.Invoke(-LineIndex);
        }

        StateChanged?.Invoke();
    }

    /// <summary>
    /// 재생 중 무대 클릭 — 이 라인을 즉시 완료하고 다음으로. 재생 중이 아니면 false를
    /// 돌려주고 클릭은 기존 조작(조절창)으로 흐른다.
    /// </summary>
    public bool TryAdvanceByInput()
    {
        if (!IsPlaying)
        {
            return false;
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

        _elapsed += deltaSeconds;

        if (_elapsed >= _dwell)
        {
            Advance();
        }
    }

    private void Advance()
    {
        if (LineIndex >= LineCount - 1)
        {
            IsPlaying = false; // 끝 도달 — 멈추고 ⏮로 돌아갈 수 있다
            StateChanged?.Invoke();
            return;
        }

        _awaitingMove = true;
        _elapsed = 0;
        MoveRequested?.Invoke(1);
    }

    public string ProgressLabel => CanPlay ? $"{LineIndex + 1}/{LineCount}" : "—";
}
