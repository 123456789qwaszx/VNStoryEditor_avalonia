using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.LogicalTree;
using Ked.Presentation.Core;
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

        return new MiniStagePreviewRequest(
            "테스트",
            fold.State,
            HasPresentation: true,
            SelectedLineId: "ln1",
            SpeakerName: null,
            LineText: "대사",
            CoreState: fold.CoreState,
            MotionCues: StageMotionCues.Of(Catalog, setup, lines, lineCommands, Tuning));
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
    public void 선언이_없는_커맨드는_칩도_궤적도_없다() => HeadlessUi.Run(() =>
    {
        var view = new StageSceneView();
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();

        view.Render(BuildRequest(Command(
            "char_rig_staging.scale_by", ("slot", "c1"), ("multiplier", "1.2"))));
        window.Measure(new Avalonia.Size(800, 600));
        window.Arrange(new Avalonia.Rect(0, 0, 800, 600));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Canvas canvas = CanvasOf(view);

        Assert.Empty(Chips(canvas));
        Assert.Empty(Trails(canvas));

        window.Close();
    });
}
