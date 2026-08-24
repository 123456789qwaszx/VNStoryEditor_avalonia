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
/// <b><c>fade_in</c>이 프리뷰에서 실제로 페이드로 흐르는가</b> (2026-08-24 소유자 보고:
/// "무대 프리뷰에서 fade_in 커맨드가 종종 안 먹을 때가 있는데").
///
/// <b>"종종"의 정체</b> — 페이드 판정이 <em>가시성</em>이 아니라 <em>직전 프레임에 이 슬롯의
/// 자리가 있었는가</em>였다. 숨김 슬롯도 고스트 윤곽으로 자리를 등록하므로(W28), 무대에
/// 이미 서 있던 슬롯의 <c>fade_in</c>은 "자리가 있었으니 이동"으로 읽혀 불투명도 1로 튀어
/// 올랐다. 페이드가 돌던 것은 슬롯이 그 라인에서 <b>처음 생길 때</b>뿐이었다 — 그리고
/// <c>slot</c>·<c>cast</c>는 보통 노드 Setup에 있으므로, 실제 작업에서는 거의 언제나
/// 안 도는 쪽이었다.
///
/// 그래서 이 파일은 <b>두 경로가 같은 답을 내는지</b>를 붙든다: 무대에 처음 서는 슬롯도,
/// 숨겨져 있던 슬롯도, 보이게 되는 순간은 페이드 인이다.
/// </summary>
public sealed class StageFadeTransitionTests
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

    /// <summary>노드 Setup에서 슬롯을 세우고 캐스팅해 둔다 — 실제 작업의 흔한 모양이다.</summary>
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

        CoreStageFoldResult fold = CoreStageFold.Fold(Catalog, setup, foldLines, Tuning);

        return new MiniStagePreviewRequest(
            "테스트",
            fold.State,
            HasPresentation: true,
            SelectedLineId: foldLines.Length > 0 ? foldLines[^1].LineId : null,
            SpeakerName: null,
            LineText: "대사",
            CoreState: fold.CoreState,
            TransitionSeconds: 0.5);
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

        /// <summary>캔버스에 <b>자리를 갖고</b> 놓인 컨트롤들 — 무대에 그려진 것들이다.</summary>
        public Control[] Positioned() => View
            .GetLogicalDescendants().OfType<Canvas>().First()
            .Children
            .Where(control => !double.IsNaN(Canvas.GetLeft(control)))
            .ToArray();

        /// <summary>그것들의 불투명도 — 페이드는 여기로만 보인다.</summary>
        public double[] Opacities() => Positioned().Select(control => control.Opacity).ToArray();

        public int PositionedCount() => Positioned().Length;
    }

    private static Stage Open()
    {
        var view = new StageSceneView();
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();

        return new Stage(view, window);
    }

    private static PresentationResultCommand FadeIn => Command("char_rig_presentation.fade_in", ("slot", "c1"));

    private static PresentationResultCommand FadeOut => Command("char_rig_presentation.fade_out", ("slot", "c1"));

    private static PresentationResultCommand[] Nothing => [];

    // ── 이 기능의 이유 ──────────────────────────────────────────────────────

    [Fact]
    public void 숨어_있던_슬롯의_fade_in도_페이드로_흐른다() => HeadlessUi.Run(() =>
    {
        // ⛔ 이것이 소유자가 본 그 장면이다 — Setup에서 이미 캐스팅해 둔 슬롯(숨김 고스트)이
        //    다음 라인에서 fade_in을 받는다. 고치기 전에는 불투명도가 처음부터 1이었다.
        Stage stage = Open();

        stage.Draw(Request(CastInSetup, [Nothing]));                   // ln1 — 숨김
        stage.Draw(Request(CastInSetup, [Nothing, [FadeIn]]));         // ln2 — 등장

        stage.View.SetTransitionProgress(0.5);

        Assert.Contains(0.5, stage.Opacities());

        stage.Window.Close();
    });

    [Fact]
    public void 무대에_처음_서는_슬롯도_그대로_페이드로_흐른다() => HeadlessUi.Run(() =>
    {
        // 고치기 전에도 되던 절반 — 여기가 깨지면 판정을 갈아 끼우다 원래 되던 것을 잃은 것이다.
        Stage stage = Open();

        stage.Draw(Request([], []));                                   // 빈 무대
        stage.Draw(Request(CastInSetup, [[FadeIn]]));                  // 슬롯이 이 라인에서 생긴다

        stage.View.SetTransitionProgress(0.5);

        Assert.Contains(0.5, stage.Opacities());

        stage.Window.Close();
    });

    // ── 퇴장도 같은 결로 (2026-08-24 소유자) ────────────────────────────────

    [Fact]
    public void fade_out은_나가는_초상이_걷히며_사라진다() => HeadlessUi.Run(() =>
    {
        // 퇴장 라인에서 이 슬롯은 <b>고스트 윤곽</b>으로 다시 그려진다 — 나가는 초상은 이미
        // 캔버스에서 내려가 있으므로, 페이드하려면 다시 얹어야 한다(배경 크로스페이드와 같은 결).
        Stage stage = Open();

        stage.Draw(Request(CastInSetup, [[FadeIn]]));                  // ln1 — 보인다
        int before = stage.PositionedCount();

        stage.Draw(Request(CastInSetup, [[FadeIn], [FadeOut]]));       // ln2 — 퇴장

        stage.View.SetTransitionProgress(0.25);

        // 걷히는 초상이 캔버스에 <b>하나 더</b> 얹혀 있고, 그 불투명도가 1 - t다.
        Assert.Equal(before + 1, stage.PositionedCount());
        Assert.Contains(0.75, stage.Opacities());

        stage.View.SetTransitionProgress(0.75);
        Assert.Contains(0.25, stage.Opacities());

        stage.Window.Close();
    });

    [Fact]
    public void 걷힌_초상은_확정_프레임에서_캔버스에서_내려간다() => HeadlessUi.Run(() =>
    {
        // ⛔ 반투명한 잔상이 남으면 그것이 곧 버그다 — 정지 화면은 언제나 확정 상태다.
        Stage stage = Open();

        stage.Draw(Request(CastInSetup, [[FadeIn]]));
        int before = stage.PositionedCount();

        stage.Draw(Request(CastInSetup, [[FadeIn], [FadeOut]]));

        stage.View.SetTransitionProgress(0.5);
        Assert.Equal(before + 1, stage.PositionedCount());

        stage.View.SetTransitionProgress(1);
        Assert.Equal(before, stage.PositionedCount());
        Assert.All(stage.Opacities(), opacity => Assert.NotEqual(0.5, opacity));

        // 정지 화면(null)에서도 같다 — 타임라인을 0으로 되돌린 순간이다.
        stage.View.SetTransitionProgress(0.5);
        stage.View.SetTransitionProgress(null);
        Assert.Equal(before, stage.PositionedCount());

        stage.Window.Close();
    });

    [Fact]
    public void 걷히는_초상은_손짓을_받지_않는다() => HeadlessUi.Run(() =>
    {
        // 이미 화면에서 나가는 중인 그림을 눌러 조절창이 열리면, 사람은 없는 것을 만진다.
        Stage stage = Open();

        stage.Draw(Request(CastInSetup, [[FadeIn]]));
        stage.Draw(Request(CastInSetup, [[FadeIn], [FadeOut]]));

        stage.View.SetTransitionProgress(0.5);

        Assert.Contains(stage.Positioned(), control =>
            control.Opacity < 1 && !control.IsHitTestVisible);

        stage.Window.Close();
    });

    // ── 안 바뀐 것 (이 판정이 넘보지 않는 자리) ─────────────────────────────

    [Fact]
    public void 이미_보이던_슬롯은_페이드하지_않는다() => HeadlessUi.Run(() =>
    {
        // 보이는 상태가 그대로인 라인은 이동·크기만 흐른다. 여기까지 페이드하면 대사가
        // 넘어갈 때마다 무대 전체가 깜빡인다.
        Stage stage = Open();

        stage.Draw(Request(CastInSetup, [[FadeIn]]));
        stage.Draw(Request(CastInSetup, [[FadeIn], Nothing]));

        stage.View.SetTransitionProgress(0.5);

        Assert.DoesNotContain(0.5, stage.Opacities());

        stage.Window.Close();
    });

    [Fact]
    public void 정지_화면과_끝까지_태운_뒤에는_전부_불투명하다() => HeadlessUi.Run(() =>
    {
        // 페이드는 흐르는 동안만의 일이다 — 확정 프레임에서 반투명하게 남으면 그 자체가 버그다.
        Stage stage = Open();

        stage.Draw(Request(CastInSetup, [Nothing]));
        stage.Draw(Request(CastInSetup, [Nothing, [FadeIn]]));

        stage.View.SetTransitionProgress(1);
        Assert.DoesNotContain(0.5, stage.Opacities());

        stage.View.SetTransitionProgress(null);
        Assert.DoesNotContain(0.5, stage.Opacities());

        stage.Window.Close();
    });
}
