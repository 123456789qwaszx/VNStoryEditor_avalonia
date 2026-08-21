using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.LogicalTree;
using Ked.Presentation.Core;
using Vn.App.Services;
using Vn.App.Views;
using Vn.Authoring.Assets;
using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;
using Path = System.IO.Path;

namespace Vn.App.Tests;

/// <summary>
/// W66 — 이동 커맨드가 <b>실제로 무대에 그려지는지</b>를 사람 눈 없이 닫는다.
///
/// 정지 프레임은 이동이 끝난 자리라, 화면만 봐서는 캐릭터가 움직였다는 사실 자체가
/// 안 보인다. 그래서 출발 자리와 궤적을 겹쳐 그린다 — 그것이 진짜 시각 트리에 서는지
/// 확인하는 것이 이 테스트다.
/// </summary>
public sealed class StageMotionRenderTests
{
    private static readonly PresentationCommandCatalog Catalog = PresentationCommandCatalog.Default;

    private static readonly string FixtureDirectory = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..",
        "Vn.Authoring.Tests", "TuningFixtures", "ExportedTuning"));

    private static StageReducerTuning Tuning { get; } =
        RuntimeTuningLibrary.Load(FixtureDirectory, (1920, 1080)).Tuning!;

    private static PresentationResultCommand Command(
        string definitionId, params (string Key, string Value)[] args)
    {
        return new PresentationResultCommand(
            Identifier.PresentationCommand(),
            definitionId,
            args.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    }

    /// <summary>c1을 세우고 보이게 한 뒤 이동 하나를 붙인 요청을 만든다.</summary>
    private static MiniStagePreviewRequest BuildRequest(params PresentationResultCommand[] lineCommands)
    {
        PresentationResultCommand[] setup =
        [
            Command("char_rig_cast.slot", ("slotKey", "c1")),
            Command("char_rig_cast.cast", ("slot", "c1"), ("characterKey", "parkeunseol"), ("emotionKey", "1")),
            Command("char_rig_entrance.show", ("slot", "c1"))
        ];

        MiniStageFoldLine[] lines = [new MiniStageFoldLine("ln1", false, lineCommands)];
        CoreStageFoldResult fold = CoreStageFold.Fold(Catalog, setup, lines, Tuning);
        IReadOnlyList<StageMotionCue>? cues =
            StageMotionCues.Of(Catalog, setup, lines, lineCommands, Tuning);
        StageMotionPlan? plan = StageMotionPlan.Build(Catalog, setup, lines, lineCommands, Tuning);

        return new MiniStagePreviewRequest(
            "테스트",
            fold.State,
            HasPresentation: true,
            SelectedLineId: "ln1",
            SpeakerName: null,
            LineText: "대사",
            CoreState: fold.CoreState,
            // 재생 보간 검증용 — 실제 편집기와 같은 계산(라인 커맨드 duration 최대).
            TransitionSeconds: StageTransitions.SecondsFor(Catalog, lineCommands),
            MotionCues: cues,
            MotionPlan: plan);
    }

    private static Canvas CanvasOf(StageSceneView view) =>
        view.GetLogicalDescendants().OfType<Canvas>().First();

    [Fact]
    public void 정지는_출발_자리이고_재생_보간이_실제_이징_곡선을_탄다() => HeadlessUi.Run(() =>
    {
        // W66 소유자 결정 — 정지 화면은 "이 라인이 시작되는 순간"이라 이동 슬롯이 출발
        // 자리에 서고, 진행 t가 흐르면 도착으로 미끄러지며, 1이면 도착에 남는다.
        // 모양은 코어 EaseFunctions(W66b) — move_by의 기본 OutCubic 곡선 그대로다.
        var view = new StageSceneView();
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();

        view.Render(BuildRequest(Command(
            "char_rig_staging.move_by", ("slot", "c1"), ("x", "+2u"), ("duration", "12fr"))));
        window.Measure(new Avalonia.Size(800, 600));
        window.Arrange(new Avalonia.Rect(0, 0, 800, 600));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Canvas canvas = CanvasOf(view);

        // 렌더 직후 = 정지 화면(출발 자리). 이 자리를 기준으로 적어 둔다.
        var rests = canvas.Children
            .Select(control => (Control: control, Left: Canvas.GetLeft(control)))
            .Where(entry => !double.IsNaN(entry.Left))
            .ToArray();

        double ShiftOf(Control control, double restLeft) => Canvas.GetLeft(control) - restLeft;

        // 진행 1 = 도착. +2u = 80px(1920 기준) 오른쪽으로 간 컨트롤이 정확히 하나.
        view.SetTransitionProgress(1);
        Assert.Single(rests, entry => Math.Abs(ShiftOf(entry.Control, entry.Left) - 80) < 0.5);

        // 진행 0.5 — 선형(+40)이 아니라 OutCubic 곡선의 자리다. 기대값도 같은 코어
        // 함수에서 온다(수치 하드코딩 금지 — 골든 대조가 그 함수를 이미 심판했다).
        view.SetTransitionProgress(0.5);
        double eased = 80 * EaseFunctions.Evaluate(EaseKind.OutCubic, 0.5f);
        Assert.True(Math.Abs(eased - 40) > 5, "OutCubic 중간값이 선형과 구분돼야 검증이 의미 있다");
        Assert.Single(rests, entry => Math.Abs(ShiftOf(entry.Control, entry.Left) - eased) < 0.5);

        // null = 정지 화면 — 전부 출발 자리로 돌아온다.
        view.SetTransitionProgress(null);
        foreach ((Control control, double left) in rests)
        {
            Assert.Equal(left, Canvas.GetLeft(control), 1);
        }

        window.Close();
    });

    [Fact]
    public void 프레임_타임라인을_끌면_그_프레임의_자리가_보인다() => HeadlessUi.Run(() =>
    {
        // W66b 소유자 요청 — "먹고가는 프레임별로 상태를 확인" → 2026-08-21 무대 아래
        // 재생 줄로 이사. 스크럽은 재생과 같은 보간·같은 곡선에 진행도를 흘릴 뿐이다.
        var view = new StageSceneView();
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();

        view.Render(BuildRequest(Command(
            "char_rig_staging.move_by", ("slot", "c1"), ("x", "+2u"), ("duration", "12fr"))));
        window.Measure(new Avalonia.Size(800, 600));
        window.Arrange(new Avalonia.Rect(0, 0, 800, 600));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Canvas canvas = CanvasOf(view);

        // 무대 위에는 더 이상 스크럽이 없다 — 타임라인은 무대 아래 재생 줄의 것이다.
        Assert.Empty(canvas.GetLogicalDescendants().OfType<Slider>());

        Control timeline = Assert.IsAssignableFrom<Control>(view.BuildTimelineScrubber());
        Slider scrub = timeline.GetLogicalDescendants().OfType<Slider>().Single();
        Assert.Equal(12, scrub.Maximum); // 12fr 이동 = 라인 배치 12프레임

        var rests = canvas.Children
            .Select(control => (Control: control, Left: Canvas.GetLeft(control)))
            .Where(entry => !double.IsNaN(entry.Left))
            .ToArray();

        // 마지막 프레임으로 끌면 도착 자리(+80px)다.
        scrub.Value = 12;
        Assert.Single(rests, entry => Math.Abs(Canvas.GetLeft(entry.Control) - entry.Left - 80) < 0.5);

        // 0프레임으로 돌리면 출발 자리다.
        scrub.Value = 0;
        foreach ((Control control, double left) in rests)
        {
            Assert.Equal(left, Canvas.GetLeft(control), 1);
        }

        window.Close();
    });

    [Fact]
    public void 스물네_프레임을_넘는_이동도_끝까지_흐른다() => HeadlessUi.Run(() =>
    {
        // 2026-08-21 소유자: "24프레임 넘어가는 건 그냥 바로 끊긴다음 snap시키는데,
        // 실제 커맨드가 사용하는 프레임을 쓰도록". 라인 시계에 1초(=24fr) 상한이
        // 있어 그보다 긴 커맨드는 중간에 잘리고 확정 자리로 튀었다.
        var view = new StageSceneView();
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();

        MiniStagePreviewRequest request = BuildRequest(Command(
            "char_rig_staging.move_by", ("slot", "c1"), ("x", "+2u"), ("duration", "48fr")));

        // 라인 시계가 커맨드가 쓴 프레임 그대로다 — 48fr = 2초.
        Assert.Equal(2.0, request.TransitionSeconds, 3);

        view.Render(request);
        window.Measure(new Avalonia.Size(800, 600));
        window.Arrange(new Avalonia.Rect(0, 0, 800, 600));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Canvas canvas = CanvasOf(view);
        var rests = canvas.Children
            .Select(control => (Control: control, Left: Canvas.GetLeft(control)))
            .Where(entry => !double.IsNaN(entry.Left))
            .ToArray();

        // 진행 0.5 = 24프레임 지점. 예전에는 여기서 이미 도착해 있었다(상한 탓).
        // 이제는 OutCubic이 24fr에 놓는 자리 — 도착(+80px)보다 앞이다.
        view.SetTransitionProgress(0.5);
        double eased = 80 * EaseFunctions.Evaluate(EaseKind.OutCubic, 0.5f);
        (Control moved, double restLeft) = Assert.Single(
            rests, entry => Math.Abs(Canvas.GetLeft(entry.Control) - entry.Left - eased) < 0.5);
        Assert.True(Canvas.GetLeft(moved) - restLeft < 80 - 1, "24프레임 지점이 도착이면 안 된다");

        // 끝까지 가면 도착이다.
        view.SetTransitionProgress(1);
        Assert.Equal(80, Canvas.GetLeft(moved) - restLeft, 1);

        window.Close();
    });

    [Fact]
    public void 시간이_없는_커맨드는_보간_없이_스냅이다() => HeadlessUi.Run(() =>
    {
        // 0fr = 런타임도 즉시 스냅이다. 태울 구간이 없으니 진행도를 흘려도 자리가
        // 그대로다(place의 duration 기본값이 0fr이라 그냥 쓰면 지금도 스냅이다).
        var view = new StageSceneView();
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();

        view.Render(BuildRequest(Command(
            "char_rig_placement.place", ("slot", "c1"), ("screenPoint", "left"), ("duration", "0fr"))));
        window.Measure(new Avalonia.Size(800, 600));
        window.Arrange(new Avalonia.Rect(0, 0, 800, 600));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Canvas canvas = CanvasOf(view);
        double[] before = canvas.Children.Select(Canvas.GetLeft).ToArray();

        view.SetTransitionProgress(1);

        Assert.Equal(before, canvas.Children.Select(Canvas.GetLeft).ToArray());

        window.Close();
    });

    [Fact]
    public void place와_depth도_duration만큼_시간에_따라_움직인다() => HeadlessUi.Run(() =>
    {
        // 2026-08-21 소유자: "place랑 depth의 경우에도 move랑 마찬가지로 … snap 되는게
        // 아니라 실제 코어쪽과 동일하게 시간에 따라서 움직이도록". 배치는 자리를,
        // 뎁스는 크기를 바꾸므로 둘 다 정지(라인 시작)와 도착이 달라야 한다.
        var view = new StageSceneView();
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();

        view.Render(BuildRequest(
            Command("char_rig_placement.place",
                ("slot", "c1"), ("focus", "bust"), ("screenPoint", "left"), ("duration", "12fr")),
            Command("char_rig_depth.size",
                ("slot", "c1"), ("depth", "close"), ("duration", "10fr"))));
        window.Measure(new Avalonia.Size(800, 600));
        window.Arrange(new Avalonia.Rect(0, 0, 800, 600));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Canvas canvas = CanvasOf(view);

        // 렌더 직후 = 라인이 시작되는 순간. 초상은 아직 옛 자리·옛 크기다.
        (Control Control, double Left, double Width)[] rests = canvas.Children
            .Select(control => (Control: control, Left: Canvas.GetLeft(control), control.Width))
            .Where(entry => !double.IsNaN(entry.Left) && !double.IsNaN(entry.Width))
            .ToArray();

        view.SetTransitionProgress(1);

        // 자리가 옮겨졌고(place) 크기가 자랐다(depth) — 한 컨트롤에서 둘 다.
        (Control moved, double restLeft, double restWidth) = Assert.Single(
            rests,
            entry => Math.Abs(Canvas.GetLeft(entry.Control) - entry.Left) > 1 &&
                     Math.Abs(entry.Control.Width - entry.Width) > 1);

        double finalLeft = Canvas.GetLeft(moved);
        double finalWidth = moved.Width;

        // 중간 프레임은 출발도 도착도 아니다 — 시간에 따라 흐른다.
        view.SetTransitionProgress(0.5);
        double midLeft = Canvas.GetLeft(moved);
        double midWidth = moved.Width;

        Assert.InRange(midLeft, Math.Min(restLeft, finalLeft) + 0.5, Math.Max(restLeft, finalLeft) - 0.5);
        Assert.InRange(midWidth, Math.Min(restWidth, finalWidth) + 0.5, Math.Max(restWidth, finalWidth) - 0.5);

        // 정지로 돌아오면 다시 라인 시작이다.
        view.SetTransitionProgress(null);
        Assert.Equal(restLeft, Canvas.GetLeft(moved), 1);
        Assert.Equal(restWidth, moved.Width, 1);

        window.Close();
    });

    [Fact]
    public void 타임라인은_시간이_없는_라인에서도_비활성으로_선다() => HeadlessUi.Run(() =>
    {
        // 2026-08-21 소유자: "어떨 때는 나오고 어떨때는 안 나오는데 상시 표기되도록".
        // 있다 없다 하면 재생 줄이 라인마다 다른 얼굴이 되고, 없는 날에는 이 도구가
        // 있다는 사실 자체가 안 보인다.
        var view = new StageSceneView();
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();

        Slider SliderOf()
        {
            Control timeline = view.BuildTimelineScrubber();
            Assert.NotNull(timeline);
            return timeline.GetLogicalDescendants().OfType<Slider>().Single();
        }

        // 아무것도 안 그린 상태 — 그래도 선다(끌 수는 없다).
        Assert.False(SliderOf().IsEnabled);

        // 시간을 가진 커맨드가 있으면 끌 수 있다.
        view.Render(BuildRequest(Command(
            "char_rig_staging.move_by", ("slot", "c1"), ("x", "+2u"), ("duration", "12fr"))));
        window.Measure(new Avalonia.Size(800, 600));
        window.Arrange(new Avalonia.Rect(0, 0, 800, 600));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(SliderOf().IsEnabled);

        window.Close();
    });
}
