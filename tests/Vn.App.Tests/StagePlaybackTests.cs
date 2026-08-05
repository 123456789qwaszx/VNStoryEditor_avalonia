using Vn.App.Services;

namespace Vn.App.Tests;

/// <summary>
/// W31 — 재생 진행 모델. 시간의 계산이 UI 없이 옳아야 한다: 자동 진행·클릭 스킵·
/// 일시정지·끝 도달·처음부터. 이동은 delta 요청으로만 나가고, 반영은 새 요청이 돌아와야
/// 완성된다(편집기 선택 경로 재사용).
/// </summary>
public class StagePlaybackTests
{
    private static (StagePlayback Playback, List<int> Moves) Build(int lineIndex, int lineCount, string? text = "대사")
    {
        var playback = new StagePlayback();
        var moves = new List<int>();
        playback.MoveRequested += moves.Add;
        playback.OnRequest(lineIndex, lineCount, text);
        return (playback, moves);
    }

    /// <summary>이동 요청 → 편집기가 선택을 옮겼다는 회신(새 프리뷰 요청)을 흉내 낸다.</summary>
    private static void Arrive(StagePlayback playback, int lineIndex, int lineCount, string? text = "대사")
        => playback.OnRequest(lineIndex, lineCount, text);

    [Fact]
    public void 체류_시간은_대사_길이에_비례하고_최소_최대로_잘린다()
    {
        Assert.Equal(StagePlayback.MinDwellSeconds, StagePlayback.DwellFor(null));
        Assert.Equal(StagePlayback.MinDwellSeconds, StagePlayback.DwellFor(""));
        Assert.True(StagePlayback.DwellFor("스무 글자쯤 되는 조금 긴 대사입니다") > StagePlayback.MinDwellSeconds);
        Assert.Equal(StagePlayback.MaxDwellSeconds, StagePlayback.DwellFor(new string('가', 500)));
    }

    [Fact]
    public void 재생하면_체류_시간_뒤에_다음_라인을_요청한다()
    {
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 0, lineCount: 3, text: "");

        playback.Play();
        Assert.True(playback.IsPlaying);

        playback.Tick(StagePlayback.MinDwellSeconds - 0.1);
        Assert.Empty(moves); // 아직 체류 중

        playback.Tick(0.2);
        Assert.Equal([1], moves);

        // 이동이 반영되기 전에는 틱이 중복 요청을 만들지 않는다.
        playback.Tick(10);
        Assert.Equal([1], moves);

        Arrive(playback, 1, 3, "");
        playback.Tick(StagePlayback.MinDwellSeconds + 0.1);
        Assert.Equal([1, 1], moves);
    }

    [Fact]
    public void 마지막_라인에_도달하면_체류_후_멈춘다()
    {
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 0, lineCount: 2, text: "");

        playback.Play();
        playback.Tick(StagePlayback.MinDwellSeconds + 0.1);
        Assert.Equal([1], moves);

        Arrive(playback, 1, 2, ""); // 마지막 라인 도착
        playback.Tick(StagePlayback.MinDwellSeconds + 0.1);

        Assert.False(playback.IsPlaying); // 끝 도달 — 멈춘다
        Assert.Equal([1], moves);         // 더 갈 곳이 없으니 추가 이동 요청도 없다
    }

    [Fact]
    public void 재생_중_클릭은_즉시_다음이고_아니면_기존_조작이다()
    {
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 0, lineCount: 3);

        Assert.False(playback.TryAdvanceByInput()); // 일시정지 중 클릭 → 조절창 몫
        Assert.Empty(moves);

        playback.Play();
        Assert.True(playback.TryAdvanceByInput());  // 재생 중 클릭 → 즉시 다음
        Assert.Equal([1], moves);
    }

    [Fact]
    public void 일시정지는_자리를_지키고_재생을_이어갈_수_있다()
    {
        (StagePlayback playback, List<int> moves) = Build(lineIndex: 0, lineCount: 3, text: "");

        playback.Play();
        playback.Tick(0.5);
        playback.Pause();

        playback.Tick(100); // 일시정지 중에는 시간이 흐르지 않는다
        Assert.Empty(moves);
        Assert.Equal(0, playback.LineIndex);

        playback.Play(); // 체류는 처음부터 다시
        playback.Tick(StagePlayback.MinDwellSeconds + 0.1);
        Assert.Equal([1], moves);
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
        playback.Tick(StagePlayback.MinDwellSeconds + 0.1);
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
