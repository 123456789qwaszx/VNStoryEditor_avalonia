using Ked.Presentation.Core;
using Vn.Authoring.Assets;
using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// 라인의 시간 흐름 (2026-08-21 소유자: "place랑 depth의 경우에도 move랑 마찬가지로 …
/// snap 되는게 아니라 실제 코어쪽과 동일하게 시간에 따라서 움직이도록 … move,place,depth를
/// 셋다 동시에 같이쓰는 경우에도").
///
/// 여기서 지키는 것: ① 시간을 가진 커맨드는 <b>축 선언 없이도</b> 흐른다(폴드 차이가 근거)
/// ② 0초는 스냅이라 계획에 안 들어온다 ③ 셋을 같이 써도 각자의 노드에서 각자의 시간으로
/// 흐른다 ④ 진행 0은 라인 시작, 충분히 지나면 확정 상태와 같다.
/// </summary>
public class StageMotionPlanTests
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

    private static PresentationResultCommand[] Setup() =>
    [
        Command("char_rig_cast.slot", ("slotKey", "c1")),
        Command("char_rig_cast.cast", ("slot", "c1"), ("characterKey", "parkeunseol"), ("emotionKey", "1")),
        Command("char_rig_entrance.show", ("slot", "c1"))
    ];

    private static StageMotionPlan? Plan(params PresentationResultCommand[] lineCommands)
    {
        PresentationResultCommand[] setup = Setup();
        MiniStageFoldLine[] lines = [new MiniStageFoldLine("ln1", false, lineCommands)];

        return StageMotionPlan.Build(Catalog, setup, lines, lineCommands, LoadTuning());
    }

    private static Vec2 PositionOf(StageState state, string nodeKey) =>
        state.Nodes.GetState(nodeKey).AnchoredPosition;

    [Fact]
    public void place는_축_선언이_없어도_duration만큼_시간에_따라_흐른다()
    {
        // place는 x·y 인자가 없다(포커스 지점과 화면 지점을 말할 뿐) — 무엇이 얼마나
        // 움직이는지는 코어가 접은 직전·직후 차이가 말한다.
        StageMotionPlan plan = Plan(Command(
            "char_rig_placement.place",
            ("slot", "c1"), ("focus", "bust"), ("screenPoint", "left"), ("duration", "12fr")))!;

        MotionTween tween = Assert.Single(plan.Tweens);
        Assert.Equal("place", tween.OutputCommand);
        Assert.Equal(0.5, tween.DurationSeconds, 3); // 12fr = 0.5초

        // 카탈로그 note가 말하는 그 노드다 — move_by 축과 별개다.
        MotionNodeTween node = Assert.Single(
            tween.Nodes, item => item.NodeKey.EndsWith("CharSlot_Track_Focus", StringComparison.Ordinal));
        Assert.NotEqual(node.From.AnchoredPosition, node.To.AnchoredPosition);

        // 0 = 라인 시작(출발), 구간이 다 지나면 확정 자리.
        Assert.Equal(node.From.AnchoredPosition, PositionOf(plan.Evaluate(0), node.NodeKey));
        Assert.Equal(node.To.AnchoredPosition, PositionOf(plan.Evaluate(0.5), node.NodeKey));

        // 중간은 출발도 도착도 아니다 — 시간에 따라 움직인다(스냅이 아니다).
        Vec2 middle = PositionOf(plan.Evaluate(0.25), node.NodeKey);
        Assert.NotEqual(node.From.AnchoredPosition, middle);
        Assert.NotEqual(node.To.AnchoredPosition, middle);
    }

    [Fact]
    public void depth도_시간에_따라_흐르고_0초는_계획에_들어오지_않는다()
    {
        StageMotionPlan plan = Plan(Command(
            "char_rig_depth.size", ("slot", "c1"), ("depth", "close"), ("duration", "10fr")))!;

        MotionTween tween = Assert.Single(plan.Tweens);
        Assert.Equal("size", tween.OutputCommand);
        Assert.NotEmpty(tween.Nodes);

        // 뎁스는 부모 크기(배율)를 만진다 — 자리만이 아니라 크기가 시간에 따라 자란다.
        Assert.Contains(tween.Nodes, node =>
            Math.Abs(node.From.LocalScale.X - node.To.LocalScale.X) > 0.001f ||
            Math.Abs(node.From.SizeDelta.X - node.To.SizeDelta.X) > 0.001f);

        // 같은 커맨드라도 0fr이면 스냅이다 — 런타임도 그렇고, 태울 구간이 없다.
        Assert.Null(Plan(Command(
            "char_rig_depth.size", ("slot", "c1"), ("depth", "close"), ("duration", "0fr"))));
    }

    [Fact]
    public void move_place_depth를_함께_써도_각자의_노드에서_각자의_시간으로_흐른다()
    {
        PresentationResultCommand move = Command(
            "char_rig_staging.move_by", ("slot", "c1"), ("x", "+2u"), ("y", "0u"), ("duration", "24fr"));
        PresentationResultCommand place = Command(
            "char_rig_placement.place", ("slot", "c1"), ("focus", "face"), ("screenPoint", "right"), ("duration", "12fr"));
        PresentationResultCommand size = Command(
            "char_rig_depth.size", ("slot", "c1"), ("depth", "front"), ("duration", "6fr"));

        StageMotionPlan plan = Plan(move, place, size)!;

        // 셋이 각자의 구간을 갖는다 — 하나로 뭉뚱그리지 않는다.
        Assert.Equal(["move_by", "place", "size"], plan.Tweens.Select(tween => tween.OutputCommand));
        Assert.Equal([1.0, 0.5, 0.25], plan.Tweens.Select(tween => Math.Round(tween.DurationSeconds, 3)));

        // 서로 다른 노드를 만진다(간섭하지 않는다).
        string moveNode = Assert.Single(plan.Tweens[0].Nodes).NodeKey;
        Assert.Contains(plan.Tweens[1].Nodes, node => !string.Equals(node.NodeKey, moveNode, StringComparison.Ordinal));

        // 0.25초 시점: size는 이미 끝났고(6fr), place는 절반, move는 4분의 1 —
        // 짧은 것이 먼저 자리를 잡고 긴 것이 뒤따른다.
        StageState quarter = plan.Evaluate(0.25);
        StageState end = plan.Evaluate(1.0);

        MotionNodeTween sizeNode = plan.Tweens[2].Nodes[0];
        Assert.Equal(
            sizeNode.To.LocalScale.X,
            quarter.Nodes.GetState(sizeNode.NodeKey).LocalScale.X,
            3); // 끝난 구간은 확정값에 머문다

        Assert.NotEqual(
            PositionOf(end, moveNode).X,
            PositionOf(quarter, moveNode).X); // 긴 구간은 아직 가는 중

        // 라인이 다 흐르면 확정 상태와 같다.
        Assert.Equal(PositionOf(plan.Final, moveNode), PositionOf(plan.Evaluate(10), moveNode));
    }

    [Fact]
    public void 움직이지_않는_커맨드와_튜닝_없는_화면은_계획이_없다()
    {
        // 0u 이동 — 시간은 있으나 바뀌는 자리가 없다. 없는 궤적을 태우지 않는다.
        Assert.Null(Plan(Command(
            "char_rig_staging.move_by", ("slot", "c1"), ("x", "0u"), ("y", "0u"), ("duration", "12fr"))));

        // 튜닝 미수입 화면은 배치가 근사다 — 시간도 근사할 수 없다.
        PresentationResultCommand move = Command(
            "char_rig_staging.move_by", ("slot", "c1"), ("x", "+2u"), ("duration", "12fr"));
        Assert.Null(StageMotionPlan.Build(
            Catalog, Setup(), [new MiniStageFoldLine("ln1", false, [move])], [move], tuning: null));
    }

    [Fact]
    public void 이징이_없는_커맨드는_런타임_기본값_곡선을_탄다()
    {
        // place·size에는 이징 칸이 없다 — 런타임 스펙 기본(OutCubic)으로 받는다.
        // 선형이 아니라는 것이 요지다(스냅도 선형도 아닌 실제 곡선).
        StageMotionPlan plan = Plan(Command(
            "char_rig_placement.place",
            ("slot", "c1"), ("screenPoint", "left"), ("duration", "12fr")))!;

        MotionTween tween = Assert.Single(plan.Tweens);
        Assert.Equal(EaseKind.OutCubic, StageMotionPlan.EaseKindOf(tween.Ease));

        MotionNodeTween node = tween.Nodes[0];
        double half = PositionOf(plan.Evaluate(0.25), node.NodeKey).X;
        double linear = node.From.AnchoredPosition.X +
            ((node.To.AnchoredPosition.X - node.From.AnchoredPosition.X) * 0.5);
        double eased = node.From.AnchoredPosition.X +
            ((node.To.AnchoredPosition.X - node.From.AnchoredPosition.X) *
                EaseFunctions.Evaluate(EaseKind.OutCubic, 0.5f));

        Assert.Equal(eased, half, 2);
        Assert.NotEqual(Math.Round(linear, 2), Math.Round(half, 2));
    }
}
