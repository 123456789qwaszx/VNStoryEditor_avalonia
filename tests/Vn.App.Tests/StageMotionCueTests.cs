using Ked.Presentation.Core;
using Vn.App.Views;
using Vn.Authoring.Assets;
using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.App.Tests;

/// <summary>
/// W66 — 무대가 만질 수 있는 이동 커맨드를 고르는 규칙.
///
/// 무엇이 이동인지는 <b>카탈로그의 모션 선언</b>만이 정한다. 여기서 지키는 것은
/// "선언 없는 커맨드가 칩으로 새어 나오지 않는다"와 "칩이 든 값이 실제 이동량이다" 둘이다.
/// </summary>
public class StageMotionCueTests
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

    private static readonly PresentationResultCommand[] Setup =
    [
        Command("char_rig_cast.slot", ("slotKey", "c1")),
        Command("char_rig_cast.cast", ("slot", "c1"), ("characterKey", "parkeunseol"), ("emotionKey", "1")),
        Command("char_rig_entrance.show", ("slot", "c1"))
    ];

    private static IReadOnlyList<StageMotionCue>? CuesFor(params PresentationResultCommand[] lineCommands)
    {
        MiniStageFoldLine[] lines = [new MiniStageFoldLine("ln1", false, lineCommands)];

        return StageMotionCues.Of(Catalog, Setup, lines, lineCommands, Tuning);
    }

    [Fact]
    public void 모션_선언이_있는_커맨드만_칩이_된다()
    {
        IReadOnlyList<StageMotionCue> cues = CuesFor(
            Command("char_rig_staging.move_by", ("slot", "c1"), ("x", "+2u"), ("duration", "12fr")),
            Command("char_rig_staging.scale_by", ("slot", "c1"), ("multiplier", "1.2")),
            Command("screen_effect.screen_flash", ("preset", "white")))!;

        StageMotionCue cue = Assert.Single(cues);
        Assert.Equal("char_rig_staging.move_by", cue.DefinitionId);
        Assert.Equal("c1", cue.SlotKey);

        // 값은 실제 이동량이다 — 1u는 기준 폭에서 파생된다.
        float unit = UnitToken.PixelsPerUnit(Tuning.ReferenceStageWidth);
        Assert.Equal(2 * unit, cue.DeltaX, 3);
        Assert.Equal(0, cue.DeltaY, 3);
        Assert.Equal(12, cue.DurationFrames, 3);
        Assert.Equal("OutCubic", cue.Ease);
    }

    [Fact]
    public void 이동이_없는_라인은_칩이_없다()
    {
        Assert.Null(CuesFor(Command("screen_effect.screen_flash", ("preset", "white"))));
        Assert.Null(StageMotionCues.Of(Catalog, Setup, [], null, Tuning));
    }

    [Fact]
    public void tuning이_없으면_칩을_내지_않는다()
    {
        // 좌표를 세울 수 없으면 수치도 없다 — 근사값을 칩으로 내밀지 않는다.
        PresentationResultCommand move = Command(
            "char_rig_staging.move_by", ("slot", "c1"), ("x", "+2u"));
        MiniStageFoldLine[] lines = [new MiniStageFoldLine("ln1", false, [move])];

        Assert.Null(StageMotionCues.Of(Catalog, Setup, lines, [move], tuning: null));
    }

    [Fact]
    public void 같은_축을_두_번_밀면_칩도_둘이고_두_번째는_첫째_다음에서_시작한다()
    {
        PresentationResultCommand first = Command(
            "char_rig_staging.move_by", ("slot", "c1"), ("x", "+2u"), ("duration", "12fr"));
        PresentationResultCommand second = Command(
            "char_rig_staging.move_by", ("slot", "c1"), ("x", "+1u"), ("duration", "6fr"));

        IReadOnlyList<StageMotionCue> cues = CuesFor(first, second)!;

        Assert.Equal(2, cues.Count);

        // 각 칩은 자기 구간의 이동량만 든다 — 누적이 아니다.
        float unit = UnitToken.PixelsPerUnit(Tuning.ReferenceStageWidth);
        Assert.Equal(2 * unit, cues[0].DeltaX, 3);
        Assert.Equal(1 * unit, cues[1].DeltaX, 3);
    }
}
