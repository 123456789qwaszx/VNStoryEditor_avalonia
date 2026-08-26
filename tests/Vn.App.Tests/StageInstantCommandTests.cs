using Avalonia.Controls;
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
/// <b>duration 0 = 즉시</b> (2026-08-26 소유자 보고: "fade_in의 경우 분명 duration을
/// 0으로 설정했는데도 즉각적으로 반영이 안되고 … place를 비롯한 이동계열 커맨드들은
/// 현재 duration을 0으로 했는데 반영이 안되는 버그").
///
/// 런타임은 duration 0을 <b>즉시 스냅</b>으로 돈다 — 정지 화면(진행 null)도 재생 어느
/// 지점도 그 커맨드는 이미 적용된 채여야 한다. 시간을 가진 커맨드만이 "정지 = 출발
/// 자리"(W66)의 대상이다.
/// </summary>
public sealed class StageInstantCommandTests
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

    private static PresentationResultCommand[] CastInSetup =>
    [
        Command("char_rig_cast.slot", ("slotKey", "c1")),
        Command("char_rig_cast.cast", ("slot", "c1"), ("characterKey", "parkeunseol"), ("emotionKey", "1")),
    ];

    private static MiniStagePreviewRequest Request(
        PresentationResultCommand[] setup,
        PresentationResultCommand[][] lines)
    {
        MiniStageFoldLine[] foldLines = lines
            .Select((commands, index) => new MiniStageFoldLine($"ln{index + 1}", false, commands))
            .ToArray();

        PresentationResultCommand[] lineCommands = lines.Length > 0 ? lines[^1] : [];
        CoreStageFoldResult fold = CoreStageFold.Fold(Catalog, setup, foldLines, Tuning);

        return new MiniStagePreviewRequest(
            "테스트",
            fold.State,
            HasPresentation: true,
            SelectedLineId: foldLines.Length > 0 ? foldLines[^1].LineId : null,
            SpeakerName: null,
            LineText: "대사",
            CoreState: fold.CoreState,
            TransitionSeconds: StageTransitions.SecondsFor(Catalog, lineCommands),
            MotionCues: StageMotionCues.Of(Catalog, setup, foldLines, lineCommands, Tuning),
            MotionPlan: StageMotionPlan.Build(Catalog, setup, foldLines, lineCommands, Tuning));
    }

    private sealed record Stage(StageSceneView View, Window Window)
    {
        public void Draw(MiniStagePreviewRequest request)
        {
            View.Render(request);
            Window.Measure(new Avalonia.Size(800, 600));
            Window.Arrange(new Avalonia.Rect(0, 0, 800, 600));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        public Control[] Positioned() => View
            .GetLogicalDescendants().OfType<Canvas>().First()
            .Children
            .Where(control => !double.IsNaN(Canvas.GetLeft(control)))
            .ToArray();
    }

    private static Stage Open()
    {
        var view = new StageSceneView();
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();

        return new Stage(view, window);
    }

    [Fact]
    public void duration_0의_place는_정지_화면부터_도착_자리다() => HeadlessUi.Run(() =>
    {
        Stage stage = Open();

        // 기준 — place 없는 무대의 자리들.
        stage.Draw(Request(
            [.. CastInSetup, Command("char_rig_entrance.show", ("slot", "c1"))],
            [[]]));
        double[] before = stage.Positioned().Select(Canvas.GetLeft).OrderBy(left => left).ToArray();

        // duration 0의 place — 런타임은 즉시 스냅이므로 정지 화면이 이미 왼쪽이어야 한다.
        stage.Draw(Request(
            [.. CastInSetup, Command("char_rig_entrance.show", ("slot", "c1"))],
            [[Command("char_rig_placement.place",
                ("slot", "c1"), ("screenPoint", "left"), ("duration", "0fr"))]]));
        double[] after = stage.Positioned().Select(Canvas.GetLeft).OrderBy(left => left).ToArray();

        Assert.NotEqual(before, after);

        stage.Window.Close();
    });

    [Fact]
    public void duration_0의_place는_재생_중에도_미끄러지지_않고_스냅이다() => HeadlessUi.Run(() =>
    {
        // 2026-08-26 소유자 보고 — "place … 0fr로 바꿧는데도 … 라인재생을 누르면
        // duration이 있는 상태로 천천히 이동". 직전 라인을 그려 기준선(직전 자리)을
        // 세워 두는 것이 핵심이다 — 그 기준선과의 일괄 보간이 미끄러짐의 정체였고,
        // 연출 그래프를 다녀오면 기준선이 비어 우연히 정상으로 보였다.
        Stage stage = Open();

        PresentationResultCommand[] setup =
            [.. CastInSetup, Command("char_rig_entrance.show", ("slot", "c1"))];

        stage.Draw(Request(setup, [[]]));                                  // ln1 — 기준선
        stage.Draw(Request(setup, [
            [],
            [Command("char_rig_placement.place",
                ("slot", "c1"), ("screenPoint", "left"), ("duration", "0fr"))]]));

        // 도착 자리를 적어 둔다.
        stage.View.SetTransitionProgress(1);
        double[] arrived = stage.Positioned().Select(Canvas.GetLeft).OrderBy(left => left).ToArray();

        // 재생 중간에도 그 자리 그대로다 — 0fr은 태울 구간이 없다.
        stage.View.SetTransitionProgress(0.5);
        Assert.Equal(
            arrived,
            stage.Positioned().Select(Canvas.GetLeft).OrderBy(left => left).ToArray());

        stage.Window.Close();
    });

    [Fact]
    public void 전부_0fr인_라인의_전이_시간은_0이다()
    {
        // ⛔ 예전에는 "시간 가진 커맨드 없음 = 기본 0.35초"가 전부 0fr인 라인까지 삼켰다 —
        //    0fr은 작가가 "즉시"라고 적은 것이다. 기본 전이는 커맨드가 하나도 없는
        //    라인의 것이다(W33).
        PresentationResultCommand[] instant =
            [Command("char_rig_placement.place", ("slot", "c1"), ("duration", "0fr"))];

        Assert.Equal(0, StageTransitions.SecondsFor(Catalog, instant));
        Assert.Equal(
            StageTransitions.DefaultSeconds, StageTransitions.SecondsFor(Catalog, []));
    }

    [Fact]
    public void duration_0의_fade_in은_정지_화면부터_보인다() => HeadlessUi.Run(() =>
    {
        Stage stage = Open();

        // ln1 — 캐스팅만(숨김 고스트). ln2 — duration 0의 fade_in.
        stage.Draw(Request(CastInSetup, [[]]));
        stage.Draw(Request(CastInSetup, [
            [],
            [Command("char_rig_presentation.fade_in", ("slot", "c1"), ("duration", "0fr"))]]));

        // 정지 화면(재생 전) — 이미 완전히 보여야 한다.
        Assert.Contains(stage.Positioned(), control => control.Opacity >= 0.99 &&
            control is not Border { Child: null });

        // 슬롯 하나가 실제 초상(이미지 자리)으로 섰다 — 고스트 윤곽이 아니라.
        Assert.Contains(1.0, stage.Positioned().Select(control => control.Opacity));

        stage.Window.Close();
    });
}
