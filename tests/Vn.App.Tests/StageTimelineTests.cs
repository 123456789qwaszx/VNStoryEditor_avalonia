using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Definition;
using Vn.Authoring.Flow;

namespace Vn.App.Tests;

/// <summary>
/// 무대 아래 프레임 타임라인 (2026-08-22 소유자: "프레임별로 눈금을 매긴 다음, 재생될 때
/// 핸들이 현재 재생 중인 프레임에 맞춰지도록. 핸들도 프레임 단위로 움직이도록").
///
/// 여기서 지키는 것 셋: <b>눈금 한 칸 = 한 프레임</b> · <b>손잡이는 프레임 사이에 안 선다</b>
/// (스냅 설정만으로는 끄는 동안 미끄러진다) · <b>재생이 손잡이를 민다</b>.
/// </summary>
public sealed class StageTimelineTests
{
    /// <summary>1초짜리 라인 = 24프레임 — 눈금이 24칸이다.</summary>
    private const double LineSeconds = 1.0;

    private static (StageSceneView View, Slider Scrub, TextBlock Label) Timeline()
    {
        var view = new StageSceneView();
        view.Attach(new AuthoringSession());
        view.Render(new MiniStagePreviewRequest(
            "테스트",
            MiniStageFold.Fold(PresentationCommandCatalog.Default, [], []),
            HasPresentation: true,
            SelectedLineId: "ln1",
            SpeakerName: null,
            LineText: "대사",
            EditContext: new StageEditContext("nd_pres", "ln1"),
            TransitionSeconds: LineSeconds));

        Control timeline = view.BuildTimelineScrubber();
        return (
            view,
            timeline.GetLogicalDescendants().OfType<Slider>().Single(),
            timeline.GetLogicalDescendants().OfType<TextBlock>().Single());
    }

    [Fact]
    public void 눈금_한_칸이_한_프레임이다() => HeadlessUi.Run(() =>
    {
        (_, Slider scrub, TextBlock label) = Timeline();

        Assert.Equal(0, scrub.Minimum);
        Assert.Equal(24, scrub.Maximum); // 1초 × 24fps
        Assert.Equal(1, scrub.TickFrequency);
        Assert.True(scrub.IsSnapToTickEnabled);

        // ⚠ 눈금이 <b>보여야</b> 한다 — TickFrequency만 있고 배치가 없으면 그려지지 않는다.
        Assert.NotEqual(TickPlacement.None, scrub.TickPlacement);

        // 키보드·페이지 이동도 한 프레임씩 — 손짓마다 걸음이 다르면 안 된다.
        Assert.Equal(1, scrub.SmallChange);
        Assert.Equal(1, scrub.LargeChange);

        Assert.Equal("0/24fr", label.Text);
    });

    [Fact]
    public void 손잡이는_프레임_사이에_서지_않는다() => HeadlessUi.Run(() =>
    {
        (_, Slider scrub, TextBlock label) = Timeline();

        // 끄는 동안 들어오는 중간값 — 스냅 설정만 믿으면 그대로 앉는다.
        scrub.Value = 7.4;
        Assert.Equal(7, scrub.Value);
        Assert.Equal("7/24fr", label.Text);

        scrub.Value = 12.6;
        Assert.Equal(13, scrub.Value);
        Assert.Equal("13/24fr", label.Text);
    });

    [Fact]
    public void 재생이_손잡이를_지금_프레임으로_민다() => HeadlessUi.Run(() =>
    {
        (StageSceneView view, Slider scrub, TextBlock label) = Timeline();

        view.SetTransitionProgress(0.5);
        Assert.Equal(12, scrub.Value);
        Assert.Equal("12/24fr", label.Text);

        view.SetTransitionProgress(1);
        Assert.Equal(24, scrub.Value);

        // 가장 가까운 프레임에 선다 — 0.51 × 24 = 12.24.
        view.SetTransitionProgress(0.51);
        Assert.Equal(12, scrub.Value);

        // null = 정지 화면 = 0프레임 (라인이 시작되는 순간).
        view.SetTransitionProgress(null);
        Assert.Equal(0, scrub.Value);
        Assert.Equal("0/24fr", label.Text);
    });
}
