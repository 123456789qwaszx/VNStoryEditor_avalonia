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
        Assert.Null(playback.TransitionProgress); // 전이 종료 — 확정 상태
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

        Assert.True(playback.TryAdvanceByInput()); // 1차 클릭 = 전이 즉시 완료
        Assert.Null(playback.TransitionProgress);
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
        Assert.Null(playback.TransitionProgress);    // 노드를 넘는 전이는 흐르지 않는다
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
    public void 처음부터는_첫_라인으로_돌아가는_이동_요청이다()
    {
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 2, lineCount: 3);

        playback.Restart();
        Assert.Equal([-2], moves);

        Arrive(playback, 0, 3);
        Assert.Equal(0, playback.LineIndex);
    }

    [Fact]
    public void 끝에서_재생을_누르면_처음부터_다시_시작한다()
    {
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 2, lineCount: 3, text: "");

        playback.Play();
        Assert.Equal([-2], moves); // 끝에서 ▶ = ⏮ + 재생
        Assert.True(playback.IsPlaying);

        Arrive(playback, 0, 3, "");
        playback.Tick(0.01);
        playback.Tick(StagePlayback.AfterTypeDwellSeconds);
        Assert.Equal([-2, 1], moves);
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
