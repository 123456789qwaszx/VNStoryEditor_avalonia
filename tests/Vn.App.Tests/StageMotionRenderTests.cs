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
    public void 재생_보간은_출발_자리에서_궤적을_타고_끝나면_확정_자리다() => HeadlessUi.Run(() =>
    {
        // W66 — 전이 진행 t를 흘리면 이동 슬롯의 초상이 "직전 렌더"가 아니라 이동의
        // 진짜 출발에서 지금 자리로 미끄러진다. 모양은 아직 선형이다(EaseFunctions 대기).
        var view = new StageSceneView();
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();

        view.Render(BuildRequest(Command(
            "char_rig_staging.move_by", ("slot", "c1"), ("x", "+2u"), ("duration", "12fr"))));
        window.Measure(new Avalonia.Size(800, 600));
        window.Arrange(new Avalonia.Rect(0, 0, 800, 600));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Canvas canvas = CanvasOf(view);

        // 확정 상태의 자리를 전부 적어 둔다.
        var finals = canvas.Children
            .Select(control => (Control: control, Left: Canvas.GetLeft(control)))
            .Where(entry => !double.IsNaN(entry.Left))
            .ToArray();

        // 반쯤 진행 — 12fr 이동이 라인 전이 시간(= 같은 12fr)과 같으므로 진행도 그대로다.
        view.SetTransitionProgress(0.5);

        // +2u = 80px(1920 기준) 이동의 절반 = 40px 왼쪽에서 오는 중인 컨트롤이 정확히 하나.
        var moved = canvas.Children
            .Select(control => (Control: control, Left: Canvas.GetLeft(control)))
            .Where(entry => !double.IsNaN(entry.Left))
            .Join(finals, entry => entry.Control, final => final.Control,
                (entry, final) => final.Left - entry.Left)
            .Where(shift => Math.Abs(shift - 40) < 0.5)
            .ToArray();
        Assert.Single(moved);

        // 전이 종료 — 전부 확정 자리로 돌아온다.
        view.SetTransitionProgress(null);
        foreach ((Control control, double left) in finals)
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
