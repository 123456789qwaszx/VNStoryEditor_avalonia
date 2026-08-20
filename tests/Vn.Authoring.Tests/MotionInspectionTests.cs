using Ked.Presentation.Core;
using Vn.Authoring.Assets;
using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// W66 — 커맨드를 이동 구간으로 펼치기. 여기서 지키는 것은 하나다:
/// <b>펼친 값이 실제 재생·정착과 같은 계산에서 나온다.</b>
/// 시작 자리는 폴드가, 종점은 <c>MoveByReduction</c>이, 단위·시간은 토큰 파서가 준다 —
/// 이 테스트가 무너지면 편집기가 무대에 거짓말을 그리고 있다는 뜻이다.
/// </summary>
public class MotionInspectionTests
{
    private static readonly PresentationCommandCatalog Catalog = PresentationCommandCatalog.Default;

    private static readonly string FixtureDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TuningFixtures", "ExportedTuning"));

    private static StageReducerTuning LoadTuning() =>
        RuntimeTuningLibrary.Load(FixtureDirectory, (1920, 1080)).Tuning!;

    private static PresentationResultCommand Command(
        string definitionId, params (string Key, string Value)[] args)
    {
        return new PresentationResultCommand(
            Identifier.PresentationCommand(),
            definitionId,
            args.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    }

    /// <summary>c1을 세우고 보이게 한 뒤 move_by 하나를 붙인 무대.</summary>
    private static (PresentationResultCommand[] Setup, MiniStageFoldLine[] Lines, PresentationResultCommand Move)
        Stage(params (string Key, string Value)[] moveArguments)
    {
        PresentationResultCommand[] setup =
        [
            Command("char_rig_cast.slot", ("slotKey", "c1")),
            Command("char_rig_cast.cast", ("slot", "c1"), ("characterKey", "parkeunseol"), ("emotionKey", "1")),
            Command("char_rig_entrance.show", ("slot", "c1"))
        ];

        PresentationResultCommand move = Command("char_rig_staging.move_by", moveArguments);
        MiniStageFoldLine[] lines = [new MiniStageFoldLine("ln1", false, [move])];

        return (setup, lines, move);
    }

    private static MotionSegment InspectMove(
        PresentationResultCommand move,
        PresentationResultCommand[] setup,
        MiniStageFoldLine[] lines,
        StageReducerTuning tuning,
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        // 시작 자리 = 그 커맨드를 적용하기 직전의 상태.
        StageState before = CoreStageFold
            .Fold(Catalog, setup, lines, tuning, stopBeforeCommandId: move.CommandId)
            .CoreState!;

        MotionSegment? segment = MotionInspection.Inspect(
            Catalog.Find(move.DefinitionId)!, move, before, tuning, overrides);

        Assert.NotNull(segment);
        return segment!;
    }

    [Fact]
    public void move_by는_선언된_축과_노드로_펼쳐진다()
    {
        StageReducerTuning tuning = LoadTuning();
        (var setup, var lines, PresentationResultCommand move) =
            Stage(("slot", "c1"), ("x", "+2u"), ("y", "-1u"), ("duration", "12fr"));

        MotionSegment segment = InspectMove(move, setup, lines, tuning);

        Assert.Equal("move_by", segment.OutputCommand);
        Assert.Equal("c1", segment.SlotKey);

        // 노드 키는 선언이 정한다 — 이름 추측이 아니다.
        Assert.Equal(StageState.NodeKeyOf("c1", "CharSlot_Track"), segment.NodeKey);

        // 1u는 상수가 아니라 기준 폭에서 파생된다. 환산의 유일한 자리는 UnitToken이다.
        float unit = UnitToken.PixelsPerUnit(tuning.ReferenceStageWidth);
        Assert.Equal(2 * unit, segment.Delta.X, 3);
        Assert.Equal(-1 * unit, segment.Delta.Y, 3);

        // 12fr = 0.5초 (24fps). 프레임 수도 같은 파서에서 온다.
        Assert.Equal(0.5, segment.DurationSeconds, 4);
        Assert.Equal(12, segment.DurationFrames, 4);
        Assert.False(segment.IsInstant);

        // ease 인자는 아직 없다 — 선언이 기록한 런타임 기본값이 보인다.
        Assert.Equal("OutCubic", segment.Ease);
    }

    [Fact]
    public void 종점은_리덕션이_내는_값과_같다()
    {
        StageReducerTuning tuning = LoadTuning();
        (var setup, var lines, PresentationResultCommand move) =
            Stage(("slot", "c1"), ("x", "+2u"), ("y", "-1u"), ("duration", "12fr"));

        MotionSegment segment = InspectMove(move, setup, lines, tuning);

        // 같은 입력을 런타임 커맨드가 쓰는 함수에 직접 넣어 본다.
        StageNodeClaim expected = MoveByReduction.Reduce(
            segment.NodeKey,
            new MoveByReduction.Args(useAbsolutePosition: false, segment.Delta),
            segment.Start);

        Assert.Equal(expected.Value.XY.X, segment.End.X, 3);
        Assert.Equal(expected.Value.XY.Y, segment.End.Y, 3);
    }

    [Fact]
    public void 구간의_종점은_폴드가_실제로_접은_자리다()
    {
        StageReducerTuning tuning = LoadTuning();
        (var setup, var lines, PresentationResultCommand move) =
            Stage(("slot", "c1"), ("x", "+2u"), ("y", "-1u"), ("duration", "12fr"));

        MotionSegment segment = InspectMove(move, setup, lines, tuning);

        // 커맨드까지 전부 접은 상태의 노드 위치 = 구간의 종점이어야 한다.
        // (이게 어긋나면 편집기가 그리는 곳과 무대가 그리는 곳이 다르다.)
        StageState after = CoreStageFold.Fold(Catalog, setup, lines, tuning).CoreState!;
        Vec2 folded = after.Nodes.GetState(segment.NodeKey).AnchoredPosition;

        Assert.Equal(folded.X, segment.End.X, 3);
        Assert.Equal(folded.Y, segment.End.Y, 3);
    }

    [Fact]
    public void 같은_축을_두_번_밀면_둘째의_시작은_첫째의_종점이다()
    {
        // 런타임은 같은 타깃의 둘째 트윈이 첫째를 즉시 완주시킨다(DOKill(true)) —
        // 계단 + 트윈이지 이어진 하나의 곡선이 아니다. 구간 둘도 그렇게 잡혀야 한다.
        StageReducerTuning tuning = LoadTuning();
        PresentationResultCommand[] setup =
        [
            Command("char_rig_cast.slot", ("slotKey", "c1")),
            Command("char_rig_cast.cast", ("slot", "c1"), ("characterKey", "parkeunseol"), ("emotionKey", "1")),
            Command("char_rig_entrance.show", ("slot", "c1"))
        ];

        PresentationResultCommand first = Command(
            "char_rig_staging.move_by", ("slot", "c1"), ("x", "+2u"), ("duration", "12fr"));
        PresentationResultCommand second = Command(
            "char_rig_staging.move_by", ("slot", "c1"), ("x", "+1u"), ("duration", "6fr"));
        MiniStageFoldLine[] lines = [new MiniStageFoldLine("ln1", false, [first, second])];

        MotionSegment one = InspectMove(first, setup, lines, tuning);
        MotionSegment two = InspectMove(second, setup, lines, tuning);

        Assert.Equal(one.End.X, two.Start.X, 3);
        Assert.Equal(one.End.Y, two.Start.Y, 3);

        float unit = UnitToken.PixelsPerUnit(tuning.ReferenceStageWidth);
        Assert.Equal(one.Start.X + 3 * unit, two.End.X, 3);
    }

    [Fact]
    public void 덮어쓴_인자로_펼치면_저장하지_않고도_새_종점이_나온다()
    {
        // 슬라이더를 끄는 동안 프로젝트를 건드리지 않고 미리 보는 길 — 확정만 편집이다.
        StageReducerTuning tuning = LoadTuning();
        (var setup, var lines, PresentationResultCommand move) =
            Stage(("slot", "c1"), ("x", "+2u"), ("y", "0u"), ("duration", "12fr"));

        MotionSegment live = InspectMove(
            move, setup, lines, tuning,
            overrides: new Dictionary<string, string>(StringComparer.Ordinal) { ["x"] = "+5u" });

        float unit = UnitToken.PixelsPerUnit(tuning.ReferenceStageWidth);
        Assert.Equal(5 * unit, live.Delta.X, 3);

        // 저장된 커맨드는 그대로다.
        Assert.Equal("+2u", move.Arguments["x"]);
    }

    [Fact]
    public void ease_인자가_있으면_그것이_기본값을_이긴다()
    {
        // W67 — 다섯째 인자. 없으면 선언이 기록한 런타임 기본(OutCubic)이다.
        StageReducerTuning tuning = LoadTuning();
        (var setup, var lines, PresentationResultCommand move) =
            Stage(("slot", "c1"), ("x", "+2u"), ("duration", "12fr"), ("ease", "InOutSine"));

        Assert.Equal("InOutSine", InspectMove(move, setup, lines, tuning).Ease);

        (var setup2, var lines2, PresentationResultCommand plain) =
            Stage(("slot", "c1"), ("x", "+2u"), ("duration", "12fr"));

        Assert.Equal("OutCubic", InspectMove(plain, setup2, lines2, tuning).Ease);
    }

    [Fact]
    public void duration이_0이면_계단이다()
    {
        StageReducerTuning tuning = LoadTuning();
        (var setup, var lines, PresentationResultCommand move) =
            Stage(("slot", "c1"), ("x", "+2u"), ("duration", "0fr"));

        MotionSegment segment = InspectMove(move, setup, lines, tuning);

        Assert.True(segment.IsInstant);
        Assert.Equal(0, segment.DurationFrames, 4);
    }

    [Fact]
    public void 선언이_없는_커맨드는_펼치지_않는다()
    {
        // 추측 금지 — 모션 선언이 없으면 수치를 내밀지 않는다.
        StageReducerTuning tuning = LoadTuning();
        PresentationResultCommand[] setup =
        [
            Command("char_rig_cast.slot", ("slotKey", "c1")),
            Command("char_rig_cast.cast", ("slot", "c1"), ("characterKey", "parkeunseol"), ("emotionKey", "1")),
            Command("char_rig_entrance.show", ("slot", "c1"))
        ];

        PresentationResultCommand scale = Command(
            "char_rig_staging.scale_by", ("slot", "c1"), ("multiplier", "1.2"));
        MiniStageFoldLine[] lines = [new MiniStageFoldLine("ln1", false, [scale])];

        StageState before = CoreStageFold
            .Fold(Catalog, setup, lines, tuning, stopBeforeCommandId: scale.CommandId)
            .CoreState!;

        Assert.Null(MotionInspection.Inspect(
            Catalog.Find(scale.DefinitionId)!, scale, before, tuning));
    }

    [Fact]
    public void 무대에_없는_슬롯은_펼치지_않는다()
    {
        StageReducerTuning tuning = LoadTuning();
        PresentationResultCommand move = Command(
            "char_rig_staging.move_by", ("slot", "없는슬롯"), ("x", "+2u"));
        MiniStageFoldLine[] lines = [new MiniStageFoldLine("ln1", false, [move])];

        StageState before = CoreStageFold
            .Fold(Catalog, [], lines, tuning, stopBeforeCommandId: move.CommandId)
            .CoreState!;

        Assert.Null(MotionInspection.Inspect(
            Catalog.Find(move.DefinitionId)!, move, before, tuning));
    }
}
