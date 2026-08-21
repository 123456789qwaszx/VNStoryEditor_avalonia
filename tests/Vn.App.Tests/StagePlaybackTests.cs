using Vn.App.Services;

namespace Vn.App.Tests;

/// <summary>
/// W31·W32 — 재생 진행 모델. 시간의 계산이 UI 없이 옳아야 한다:
/// 타자(글자 노출) → 여운 → 다음 라인, 클릭 규약(타자 중=전문·그 뒤=다음),
/// 선택지 정지, 일시정지, 끝 도달, 처음부터. 이동은 delta 요청으로만 나가고,
/// 반영은 새 요청이 돌아와야 완성된다(편집기 선택 경로 재사용).
/// </summary>
public class StagePlaybackTests
{
    private static (StagePlayback Playback, List<int> Moves) Build(
        int lineIndex, int lineCount, string? text = "대사", bool isChoice = false)
    {
        var playback = new StagePlayback();
        var moves = new List<int>();
        playback.MoveRequested += moves.Add;
        playback.OnRequest(lineIndex, lineCount, text, isChoice);
        return (playback, moves);
    }

    /// <summary>이동 요청 → 편집기가 선택을 옮겼다는 회신(새 프리뷰 요청)을 흉내 낸다.</summary>
    private static void Arrive(
        StagePlayback playback, int lineIndex, int lineCount, string? text = "대사", bool isChoice = false)
        => playback.OnRequest(lineIndex, lineCount, text, isChoice);

    // ── 라인만 재생 (2026-08-21 소유자: "현재 라인만 재생기능") ──────────

    [Fact]
    public void 라인만_재생은_이_라인을_끝까지_돌고_다음으로_넘어가지_않는다()
    {
        string text = new('가', 30);
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 0, lineCount: 3, text);

        playback.PlayCurrentLine();
        Assert.True(playback.IsPlaying);
        Assert.Equal(0, playback.VisibleCharacters); // 전체 재생과 같은 경로 — 타자 처음부터

        playback.Tick(1.1); // 타자 완료
        playback.Tick(StagePlayback.AfterTypeDwellSeconds + 0.01); // 여운 끝 = 넘어갈 문턱

        Assert.False(playback.IsPlaying); // 멈춘다 — 이동 요청 없음
        Assert.Empty(moves);
    }

    [Fact]
    public void 라인만_재생_중_전문_클릭_뒤_클릭도_넘어가지_않고_멈춘다()
    {
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 0, lineCount: 3, text: new string('가', 100));

        playback.PlayCurrentLine();
        Assert.True(playback.TryAdvanceByInput()); // 1차 = 전문 완성
        Assert.True(playback.TryAdvanceByInput()); // 2차 = 다음 대신 정지

        Assert.False(playback.IsPlaying);
        Assert.Empty(moves);
    }

    [Fact]
    public void 라인만_재생_뒤_보통_재생은_다시_전체_진행이다()
    {
        string text = new('가', 30);
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 0, lineCount: 3, text);

        playback.PlayCurrentLine();
        playback.Tick(1.1);
        playback.Tick(StagePlayback.AfterTypeDwellSeconds + 0.01); // 라인만 재생 종료

        playback.Play(); // 라인 전용 상태가 남아 있으면 안 된다
        playback.Tick(1.1);
        playback.Tick(StagePlayback.AfterTypeDwellSeconds + 0.01);
        Assert.Equal([1], moves); // 이번에는 다음 라인으로 간다
    }

    // ── 타자기 (W32) ──────────────────────────────────────────────────────

    [Fact]
    public void 타자는_속도대로_글자를_노출하고_완료_후_여운_뒤에_넘어간다()
    {
        string text = new('가', 30); // 30자 — CharactersPerSecond=30이면 타자에 1초
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 0, lineCount: 2, text);

        Assert.Null(playback.VisibleCharacters); // 정지 중엔 전문

        playback.Play();
        Assert.Equal(0, playback.VisibleCharacters); // 재생 시작 — 처음부터 찍는다

        playback.Tick(0.5);
        Assert.Equal(15, playback.VisibleCharacters);

        playback.Tick(0.6); // 타자 완료 → 여운 시작
        Assert.Null(playback.VisibleCharacters); // 전문 표시
        Assert.Empty(moves);

        playback.Tick(StagePlayback.AfterTypeDwellSeconds + 0.01);
        Assert.Equal([1], moves);
    }

    [Fact]
    public void 타자_중_클릭은_전문을_완성하고_그_뒤_클릭이_다음이다()
    {
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 0, lineCount: 3, text: new string('가', 100));

        playback.Play();
        playback.Tick(0.2); // 타자 중

        Assert.True(playback.TryAdvanceByInput()); // 1차 클릭 = 전문 완성
        Assert.Null(playback.VisibleCharacters);
        Assert.Empty(moves);

        Assert.True(playback.TryAdvanceByInput()); // 2차 클릭 = 다음 라인
        Assert.Equal([1], moves);
    }

    [Fact]
    public void 선택지_라인은_시간이_흘러도_멈춰_있고_클릭이_진행이다()
    {
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 0, lineCount: 3, text: "라벨", isChoice: true);

        playback.Play();
        playback.Tick(60); // 아무리 기다려도

        Assert.Empty(moves); // 자동 진행 없음
        Assert.True(playback.IsPlaying);
        Assert.Null(playback.VisibleCharacters); // 선택지 화면 — 타자 없음

        Assert.True(playback.TryAdvanceByInput());
        Assert.Equal([1], moves);
    }

    [Fact]
    public void 빈_대사는_여운만으로_넘어간다()
    {
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 0, lineCount: 3, text: "");

        playback.Play();
        playback.Tick(0.01); // 타자 즉시 완료 → 여운
        Assert.Empty(moves);

        playback.Tick(StagePlayback.AfterTypeDwellSeconds);
        Assert.Equal([1], moves);

        // 이동이 반영되기 전에는 틱이 중복 요청을 만들지 않는다.
        playback.Tick(10);
        Assert.Equal([1], moves);
    }

    // ── 재생 배율 (W34-a) ─────────────────────────────────────────────────

    [Fact]
    public void 배율은_타자와_여운에_함께_적용된다()
    {
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 0, lineCount: 2, text: new string('가', 60));

        playback.SpeedMultiplier = 2; // 2배 — 60자 타자가 1초, 여운이 0.6초
        playback.Play();

        playback.Tick(0.5);
        Assert.Equal(30, playback.VisibleCharacters); // 0.5초 × 30cps × 2배

        playback.Tick(0.5); // 타자 완료
        playback.Tick(StagePlayback.AfterTypeDwellSeconds / 2 + 0.01); // 절반 시간이면 여운 끝
        Assert.Equal([1], moves);
    }

    [Fact]
    public void 배율은_경계로_잘리고_이상값은_1로_돌아온다()
    {
        var playback = new StagePlayback();

        playback.SpeedMultiplier = 100;
        Assert.Equal(4, playback.SpeedMultiplier); // 상한

        playback.SpeedMultiplier = double.NaN;
        Assert.Equal(1, playback.SpeedMultiplier);
    }

    // ── 전이 (W33) ────────────────────────────────────────────────────────

    [Fact]
    public void 재생_중_라인이_바뀌면_전이가_흐르고_끝나면_확정_상태다()
    {
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 0, lineCount: 3, text: "");

        playback.Play();
        Assert.Null(playback.TransitionProgress); // 첫 표시는 전이 없음

        playback.TryAdvanceByInput(); // 여운 단계에서 클릭 → 다음 라인 요청
        Assert.Equal([1], moves);

        Arrive2(playback, 1, 3, transitionSeconds: 0.4);
        Assert.Equal(0, playback.TransitionProgress); // 전이 시작

        playback.Tick(0.2);
        Assert.Equal(0.5, playback.TransitionProgress!.Value, precision: 3);

        playback.Tick(0.3);
        // 전이 종료 — 재생 중에는 1로 남는다 (W66): null로 떨어지면 뷰가 정지 화면
        // (이동 슬롯 = 출발 자리)으로 읽어 캐릭터가 되돌아가 버린다.
        Assert.Equal(1, playback.TransitionProgress!.Value, precision: 3);
    }

    [Fact]
    public void 전이_중에는_여운이_기다리고_클릭은_전이를_즉시_완료한다()
    {
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 0, lineCount: 3, text: "");

        playback.Play();
        playback.TryAdvanceByInput();
        Arrive2(playback, 1, 3, transitionSeconds: 10); // 아주 긴 전이

        playback.Tick(StagePlayback.AfterTypeDwellSeconds + 1);
        Assert.Equal([1], moves); // 전이가 안 끝났으니 다음 라인으로 못 간다

        Assert.True(playback.TryAdvanceByInput()); // 1차 클릭 = 전이 즉시 완료 (진행도 1)
        Assert.Equal(1, playback.TransitionProgress!.Value, precision: 3);
        Assert.Equal([1], moves);

        Assert.True(playback.TryAdvanceByInput()); // 2차 클릭 = 다음 라인
        Assert.Equal([1, 1], moves);
    }

    [Fact]
    public void 일시정지와_정지_상태에서는_전이가_없다()
    {
        (StagePlayback playback, _) = Build(lineIndex: 0, lineCount: 3, text: "");

        // 정지 중 라인 이동(작가가 라인을 클릭) — 전이 없이 확정 상태.
        Arrive2(playback, 1, 3, transitionSeconds: 0.4);
        Assert.Null(playback.TransitionProgress);

        playback.Play();
        playback.TryAdvanceByInput();
        Arrive2(playback, 2, 3, transitionSeconds: 0.4);
        Assert.NotNull(playback.TransitionProgress);

        playback.Pause(); // 일시정지 = 확정 상태로
        Assert.Null(playback.TransitionProgress);
    }

    private static void Arrive2(
        StagePlayback playback, int lineIndex, int lineCount, double transitionSeconds)
        => playback.OnRequest(lineIndex, lineCount, "", isChoice: false, transitionSeconds);

    [Fact]
    public void 재생을_누르면_현재_라인의_이동을_처음부터_태운다()
    {
        // W66 — 타자만 처음부터 되감고 연출은 끝 상태로 두면 "재생"이 아니다.
        // 정지 상태에서 라인을 보고 있다가 ▶ — 그 라인의 전이가 0부터 흐른다.
        (StagePlayback playback, _) = Build(lineIndex: 0, lineCount: 2, text: "");

        Arrive2(playback, 0, 2, transitionSeconds: 0.4); // 같은 라인 재요청 — 시간만 갱신
        Assert.Null(playback.TransitionProgress);        // 정지 중에는 확정 상태

        playback.Play();
        Assert.Equal(0, playback.TransitionProgress);    // ▶ = 이동도 처음부터

        playback.Tick(0.2);
        Assert.Equal(0.5, playback.TransitionProgress!.Value, precision: 3);

        playback.Tick(0.3);
        Assert.Equal(1, playback.TransitionProgress!.Value, precision: 3); // 이동 종료 — 도착 자리 유지

        playback.Pause();
        Assert.Null(playback.TransitionProgress); // 정지 화면 — 뷰가 출발 자리로 되돌린다
    }

    // ── 진행·정지 (W31) ───────────────────────────────────────────────────

    [Fact]
    public void 마지막_라인에_도달하면_여운_후_멈춘다()
    {
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 0, lineCount: 2, text: "");

        playback.Play();
        playback.Tick(0.01);
        playback.Tick(StagePlayback.AfterTypeDwellSeconds);
        Assert.Equal([1], moves);

        Arrive(playback, 1, 2, ""); // 마지막 라인 도착
        playback.Tick(0.01);
        playback.Tick(StagePlayback.AfterTypeDwellSeconds);

        Assert.False(playback.IsPlaying); // 끝 도달 — 멈춘다
        Assert.Equal([1], moves);         // 더 갈 곳이 없으니 추가 이동 요청도 없다
    }

    // ── 노드 간 이어 재생 (W39) ───────────────────────────────────────────

    [Fact]
    public void 문서_끝에서_노드_전환이_받아들여지면_재생이_이어진다()
    {
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 0, lineCount: 1, text: "");
        int exitAsks = 0;

        playback.NodeExitRequested = () =>
        {
            exitAsks++;
            playback.OnNodeSwitch(); // 편집기가 노드를 바꾸고 —
            Arrive(playback, 0, 2, new string('가', 30)); // 새 노드의 첫 라인이 도착한다
            return true;
        };

        playback.Play();
        playback.Tick(0.01);
        playback.Tick(StagePlayback.AfterTypeDwellSeconds);

        Assert.Equal(1, exitAsks);
        Assert.True(playback.IsPlaying);            // 멈추지 않고 이어진다
        Assert.Empty(moves);                        // 라인 이동이 아니라 노드 전환이다
        Assert.Equal(0, playback.VisibleCharacters); // 새 노드 첫 라인의 타자가 처음부터
        Assert.Null(playback.TransitionProgress);   // 노드를 넘는 전이 보간은 없다

        playback.Tick(0.5);
        Assert.Equal(15, playback.VisibleCharacters); // 시간이 정상으로 흐른다
    }

    [Fact]
    public void 문서_끝에서_전환이_거절되면_기존대로_멈춘다()
    {
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 0, lineCount: 1, text: "");
        playback.NodeExitRequested = () => false;

        playback.Play();
        playback.Tick(0.01);
        playback.Tick(StagePlayback.AfterTypeDwellSeconds);

        Assert.False(playback.IsPlaying);
        Assert.Empty(moves);
    }

    [Fact]
    public void 노드_전환은_같은_인덱스의_요청도_새_라인으로_시작한다()
    {
        // 이전 노드가 1줄짜리면 끝 인덱스도 0, 새 노드 첫 라인도 0 — 그래도 타자가 다시 시작돼야 한다.
        (StagePlayback playback, _) = Build(lineIndex: 0, lineCount: 1, text: new string('가', 30));

        playback.Play();
        playback.Tick(0.5); // 15자 타자 중

        playback.OnNodeSwitch();
        Arrive2(playback, 0, 3, transitionSeconds: 0.4); // 같은 인덱스 0으로 도착

        Assert.Equal(0, playback.VisibleCharacters); // 새 라인 취급 — 처음부터
        // 노드를 넘는 전이는 흐르지 않는다 — 진행도 1(확정 자리)로 곧장 선다.
        // (null이 아니라 1인 이유: 재생 중 null은 정지 화면 = 이동 슬롯이 출발 자리다, W66)
        Assert.Equal(1, playback.TransitionProgress!.Value, precision: 3);
    }

    [Fact]
    public void 경로가_중간에_끝나면_정지가_대기를_풀고_멈춘다()
    {
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 0, lineCount: 3, text: "");

        playback.Play();
        playback.Tick(0.01);
        playback.Tick(StagePlayback.AfterTypeDwellSeconds);
        Assert.Equal([1], moves); // 이동 요청이 나갔지만 —

        playback.StopAtEnd(); // 편집기: 경로 끝(갈래 출구 뒤), 이어질 노드 없음

        Assert.False(playback.IsPlaying);
        playback.Tick(10);
        Assert.Equal([1], moves); // 대기에 걸려 있지 않고 조용하다
    }

    [Fact]
    public void 재생_중이_아니면_클릭은_기존_조작이다()
    {
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 0, lineCount: 3);

        Assert.False(playback.TryAdvanceByInput()); // 일시정지 중 클릭 → 조절창 몫
        Assert.Empty(moves);
    }

    [Fact]
    public void 일시정지는_전문을_보여주고_재생은_타자를_처음부터_다시_한다()
    {
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 0, lineCount: 3, text: new string('가', 100));

        playback.Play();
        playback.Tick(0.5);
        Assert.Equal(15, playback.VisibleCharacters);

        playback.Pause();
        Assert.Null(playback.VisibleCharacters); // 조작 모드 = 전문

        playback.Tick(100); // 일시정지 중에는 시간이 흐르지 않는다
        Assert.Empty(moves);

        playback.Play(); // 타자를 처음부터
        Assert.Equal(0, playback.VisibleCharacters);
    }

    [Fact]
    public void 재생은_현재_라인부터_끝까지이고_되감지_않는다()
    {
        // 2026-08-21 소유자: [⏮ 처음부터]를 걷고 ▶는 "현재 라인부터 끝까지"가 됐다.
        // 예전에는 마지막 라인에서 ▶가 첫 라인으로 되감았다(⏮와 한 쌍의 규칙이었다).
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 2, lineCount: 3, text: "");

        playback.Play();
        Assert.True(playback.IsPlaying);
        Assert.Empty(moves); // 되감기 없음

        playback.Tick(0.01);
        playback.Tick(StagePlayback.AfterTypeDwellSeconds);

        // 마지막 라인이라 여운 뒤에는 그대로 끝난다 — 이동도 없다.
        Assert.Empty(moves);
        Assert.False(playback.IsPlaying);
    }

    [Fact]
    public void 중간_라인에서_재생하면_거기서부터_끝까지_간다()
    {
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 1, lineCount: 3, text: "");

        playback.Play();
        playback.Tick(0.01);
        playback.Tick(StagePlayback.AfterTypeDwellSeconds);
        Assert.Equal([1], moves); // 앞이 아니라 다음 라인으로

        Arrive(playback, 2, 3, "");
        playback.Tick(0.01);
        playback.Tick(StagePlayback.AfterTypeDwellSeconds);
        Assert.Equal([1], moves); // 끝에 닿았다 — 더 가지 않는다
        Assert.False(playback.IsPlaying);
    }

    // ── 두 버튼은 각자 제 모드만 비춘다 (2026-08-21 소유자) ────────────────

    [Fact]
    public void 라인만_재생_중에는_전체_재생_버튼이_재생_상태로_남는다()
    {
        // 소유자: "라인만 재생을 누르는데 재생버튼이 일시정지로 바뀌는 것도 불편".
        (StagePlayback playback, _) = Build(lineIndex: 0, lineCount: 3, text: new string('가', 30));

        playback.ToggleLinePlay();
        Assert.True(playback.IsPlaying);
        Assert.True(playback.IsPlayingLine);   // [라인]이 ⏸로 선다
        Assert.False(playback.IsPlayingAll);   // [▶]는 ▶ 그대로

        // 같은 버튼을 다시 누르면 멈춘다.
        playback.ToggleLinePlay();
        Assert.False(playback.IsPlaying);
        Assert.False(playback.IsPlayingLine);
    }

    [Fact]
    public void 전체_재생_중에는_라인_버튼이_재생_상태로_남고_서로_갈아탄다()
    {
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 0, lineCount: 3, text: new string('가', 30));

        playback.TogglePlay();
        Assert.True(playback.IsPlayingAll);
        Assert.False(playback.IsPlayingLine);

        // 전체 재생 중 [라인]을 누르면 멈추는 게 아니라 이 라인 반복으로 갈아탄다.
        playback.ToggleLinePlay();
        Assert.True(playback.IsPlayingLine);
        Assert.False(playback.IsPlayingAll);

        // 라인만 재생 중 [▶]를 누르면 전체 재생으로 갈아탄다 — 실제로 다음 라인까지 간다.
        playback.TogglePlay();
        Assert.True(playback.IsPlayingAll);
        playback.Tick(1.1);
        playback.Tick(StagePlayback.AfterTypeDwellSeconds + 0.01);
        Assert.Equal([1], moves);

        // 전체 재생 중 [▶]는 일시정지다.
        Arrive(playback, 1, 3, "대사");
        playback.TogglePlay();
        Assert.False(playback.IsPlaying);
    }

    [Fact]
    public void 보여_줄_라인이_없으면_재생하지_않는다()
    {
        (StagePlayback playback, List<int> moves) = Build(lineIndex: -1, lineCount: 0, text: null);

        Assert.False(playback.CanPlay);
        playback.Play();
        Assert.False(playback.IsPlaying);
        Assert.Empty(moves);

        // 재생 중 노드를 벗어나면(라인 없음) 재생도 멈춘다.
        Arrive(playback, 0, 2);
        playback.Play();
        Assert.True(playback.IsPlaying);
        Arrive(playback, -1, 0, null);
        Assert.False(playback.IsPlaying);
    }
}
