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
            CommandChips: StageCommandChips.Of(Catalog, lineCommands, cues));
    }

    private static Canvas CanvasOf(StageSceneView view) =>
        view.GetLogicalDescendants().OfType<Canvas>().First();

    /// <summary>궤적 선 — 점선으로 그려진 것만 센다(다른 선과 섞이지 않게).</summary>
    private static IReadOnlyList<Line> Trails(Canvas canvas) =>
        canvas.Children.OfType<Line>().Where(line => line.StrokeDashArray is { Count: > 0 }).ToArray();

    private static IReadOnlyList<TextBlock> Chips(Canvas canvas) =>
        canvas.GetLogicalDescendants().OfType<TextBlock>()
            .Where(text => text.Text is { } value && value.StartsWith('⇢'))
            .ToArray();

    [Fact]
    public void 이동_커맨드는_칩과_궤적으로_그려진다() => HeadlessUi.Run(() =>
    {
        var view = new StageSceneView();
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();

        view.Render(BuildRequest(Command(
            "char_rig_staging.move_by", ("slot", "c1"), ("x", "+2u"), ("duration", "12fr"))));
        window.Measure(new Avalonia.Size(800, 600));
        window.Arrange(new Avalonia.Rect(0, 0, 800, 600));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Canvas canvas = CanvasOf(view);

        // 칩이 이동량과 시간을 사람이 읽는 말로 든다.
        TextBlock chip = Assert.Single(Chips(canvas));
        Assert.Contains("c1", chip.Text!, StringComparison.Ordinal);
        Assert.Contains("+2u", chip.Text!, StringComparison.Ordinal);
        Assert.Contains("12fr", chip.Text!, StringComparison.Ordinal);

        // 출발 자리에서 지금 자리로 잇는 궤적이 실제로 섰다.
        Assert.Single(Trails(canvas));

        window.Close();
    });

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
    public void 프레임_스크럽을_끌면_그_프레임의_자리가_보인다() => HeadlessUi.Run(() =>
    {
        // W66b 소유자 요청 — "먹고가는 프레임별로 상태를 확인". 스크럽은 재생과 같은
        // 보간·같은 곡선에 진행도를 흘릴 뿐이다(별도 계산 없음).
        var view = new StageSceneView();
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();

        view.Render(BuildRequest(Command(
            "char_rig_staging.move_by", ("slot", "c1"), ("x", "+2u"), ("duration", "12fr"))));
        window.Measure(new Avalonia.Size(800, 600));
        window.Arrange(new Avalonia.Rect(0, 0, 800, 600));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Canvas canvas = CanvasOf(view);
        Slider scrub = Assert.Single(canvas.GetLogicalDescendants().OfType<Slider>().ToArray());
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
    public void 움직이지_않는_이동은_궤적을_그리지_않는다() => HeadlessUi.Run(() =>
    {
        // 0u 이동은 그릴 궤적이 없다 — 없는 선을 그려 두면 화면이 거짓말한다.
        var view = new StageSceneView();
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();

        view.Render(BuildRequest(Command(
            "char_rig_staging.move_by", ("slot", "c1"), ("x", "0u"), ("y", "0u"))));
        window.Measure(new Avalonia.Size(800, 600));
        window.Arrange(new Avalonia.Rect(0, 0, 800, 600));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Canvas canvas = CanvasOf(view);

        Assert.Single(Chips(canvas)); // 칩은 선다 — 커맨드가 있다는 사실은 숨기지 않는다
        Assert.Empty(Trails(canvas));

        window.Close();
    });

    [Fact]
    public void 슬라이더_선언_커맨드는_편집_화면에서_수치_칩으로_선다() => HeadlessUi.Run(() =>
    {
        // W68 — scale_by는 모션 선언(궤적)은 없지만 multiplier 슬라이더 선언이 있다.
        // 편집 가능한 화면에서는 ⚙ 수치 칩으로, 읽기 화면에서는 표시 칩으로 선다.
        var view = new StageSceneView();
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();

        MiniStagePreviewRequest request = BuildRequest(Command(
            "char_rig_staging.scale_by", ("slot", "c1"), ("multiplier", "1.2")));
        view.Render(request with { EditContext = new StageEditContext("np_test", "ln1") });
        window.Measure(new Avalonia.Size(800, 600));
        window.Arrange(new Avalonia.Rect(0, 0, 800, 600));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Canvas canvas = CanvasOf(view);

        Assert.Contains(canvas.GetLogicalDescendants().OfType<TextBlock>(),
            text => text.Text is { } value &&
                value.StartsWith('⚙') && value.Contains("scale_by", StringComparison.Ordinal));

        window.Close();
    });

    [Fact]
    public void 선언이_없는_커맨드는_표시_칩만_서고_궤적은_없다() => HeadlessUi.Run(() =>
    {
        // 커맨드가 이 라인에 있다는 사실은 숨기지 않되(표시 칩), 수치·궤적은
        // 모션 선언이 있는 것에만 붙는다 — 추측으로 축을 그리지 않는다.
        var view = new StageSceneView();
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();

        view.Render(BuildRequest(Command(
            "char_rig_staging.scale_by", ("slot", "c1"), ("multiplier", "1.2"))));
        window.Measure(new Avalonia.Size(800, 600));
        window.Arrange(new Avalonia.Rect(0, 0, 800, 600));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Canvas canvas = CanvasOf(view);

        Assert.Empty(Chips(canvas)); // ⇢ 이동 칩은 없다
        Assert.Empty(Trails(canvas));

        // 표시 칩은 선다 — 병기 텍스트 그대로.
        Assert.Contains(canvas.GetLogicalDescendants().OfType<TextBlock>(),
            text => text.Text is { } value && value.Contains("scale_by", StringComparison.Ordinal));

        window.Close();
    });
}
