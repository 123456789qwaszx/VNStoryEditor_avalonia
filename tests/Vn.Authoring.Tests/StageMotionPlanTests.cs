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
    public void 회전_3종이_되살아나_시간에_따라_흐른다()
    {
        // 2026-08-21 런타임 복원 — rotate_by·rotate_reset은 CharSlot_SwayPivot,
        // char_rotate_to는 CharacterPortrait_SwayPivot(초상 축)이다. 노드가 코어
        // 리듀서가 접는 그것과 같아야 "재생 = 정지 프레임"이 유지된다.
        StageMotionPlan slot = Plan(Command(
            "char_rig_staging.rotate_by", ("slot", "c1"), ("degree", "15"), ("duration", "12fr")))!;
        Assert.Contains(
            Assert.Single(slot.Tweens).Nodes,
            node => node.NodeKey.EndsWith("CharSlot_SwayPivot", StringComparison.Ordinal));

        StageMotionPlan portrait = Plan(Command(
            "char_rig_presentation.char_rotate_to", ("slot", "c1"), ("degree", "20"), ("duration", "10fr")))!;
        MotionNodeTween portraitNode = Assert.Single(
            Assert.Single(portrait.Tweens).Nodes,
            node => node.NodeKey.EndsWith("CharacterPortrait_SwayPivot", StringComparison.Ordinal));

        // 각도가 시간에 따라 돈다 — 스냅이 아니다.
        Assert.Equal(0, portraitNode.From.LocalEulerAngles.Z, 3);
        Assert.Equal(20, portraitNode.To.LocalEulerAngles.Z, 3);
        float middle = portrait.Evaluate(0.2).Nodes.GetState(portraitNode.NodeKey).LocalEulerAngles.Z;
        Assert.InRange(middle, 0.5f, 19.5f);
    }

    [Fact]
    public void place와_size의_이징_인자가_계획에_실린다()
    {
        // 2026-08-21 런타임이 이징 칸을 열었다(마지막 위치 인자) — 가정이 아니라 선언이다.
        StageMotionPlan plan = Plan(
            Command("char_rig_placement.place_left",
                ("slot", "c1"), ("focus", "face"), ("duration", "12fr"), ("ease", "OutBack")),
            Command("char_rig_depth.size_close",
                ("slot", "c1"), ("preserveFocus", "bust"), ("duration", "10fr"), ("ease", "Linear")))!;

        Assert.Equal(
            [EaseKind.OutBack, EaseKind.Linear],
            plan.Tweens.Select(tween => StageMotionPlan.EaseKindOf(tween.Ease)));
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

    [Fact]
    public void 숫자_레벨_뎁스도_라벨과_같은_커브로_접히고_흐른다()
    {
        // 2026-08-21 런타임 개통 — 깊이의 진실이 레벨 커브 하나가 됐다.
        // 라벨은 그 커브 위의 눈금이라 `mid`와 `14`는 같은 무대여야 한다
        // (2026-08-21 소유자 상향: back 10 · mid 14 · front 16 · close 20).
        StageMotionPlan byLabel = Plan(Command(
            "char_rig_depth.size", ("slot", "c1"), ("depth", "mid"), ("duration", "10fr")))!;
        StageMotionPlan byLevel = Plan(Command(
            "char_rig_depth.size", ("slot", "c1"), ("depth", "14"), ("duration", "10fr")))!;

        MotionNodeTween labelScale = Assert.Single(
            byLabel.Tweens[0].Nodes, node => node.NodeKey.EndsWith("DepthScale", StringComparison.Ordinal));
        MotionNodeTween levelScale = Assert.Single(
            byLevel.Tweens[0].Nodes, node => node.NodeKey.EndsWith("DepthScale", StringComparison.Ordinal));
        Assert.Equal(labelScale.To.LocalScale.X, levelScale.To.LocalScale.X, 4);

        // 설계 구간 밖도 접힌다 — 끝 두 키의 할선으로 외삽되기 때문이다.
        StageMotionPlan far = Plan(Command(
            "char_rig_depth.size", ("slot", "c1"), ("depth", "-3.5"), ("duration", "10fr")))!;
        MotionNodeTween farScale = Assert.Single(
            far.Tweens[0].Nodes, node => node.NodeKey.EndsWith("DepthScale", StringComparison.Ordinal));
        Assert.True(
            farScale.To.LocalScale.X < labelScale.To.LocalScale.X,
            "음수 레벨은 mid보다 더 작아야 한다(뒤로 물러난다)");

        // 시간도 흐른다 — 중간 프레임이 출발도 도착도 아니다.
        float middle = far.Evaluate(0.2).Nodes.GetState(farScale.NodeKey).LocalScale.X;
        Assert.NotEqual(farScale.From.LocalScale.X, middle, 3);
        Assert.NotEqual(farScale.To.LocalScale.X, middle, 3);

        // close(20)가 설계 구간 끝이 됐다 — 그 너머(22)도 끝 두 키의 할선으로 외삽되어 접힌다.
        StageMotionPlan closeUp = Plan(Command(
            "char_rig_depth.size", ("slot", "c1"), ("depth", "22"), ("duration", "10fr")))!;
        MotionNodeTween closeUpScale = Assert.Single(
            closeUp.Tweens[0].Nodes, node => node.NodeKey.EndsWith("DepthScale", StringComparison.Ordinal));

        StageMotionPlan atClose = Plan(Command(
            "char_rig_depth.size", ("slot", "c1"), ("depth", "close"), ("duration", "10fr")))!;
        MotionNodeTween atCloseScale = Assert.Single(
            atClose.Tweens[0].Nodes, node => node.NodeKey.EndsWith("DepthScale", StringComparison.Ordinal));

        Assert.True(
            closeUpScale.To.LocalScale.X > atCloseScale.To.LocalScale.X,
            "22는 close(20)보다 더 붙어야 한다");

        // 눈금도 수치도 아닌 토큰은 여전히 거부된다 — 조용히 삼키지 않는다.
        Assert.Null(Plan(Command(
            "char_rig_depth.size", ("slot", "c1"), ("depth", "헛토큰"), ("duration", "10fr"))));
    }

    // ── 카메라 (2026-08-21 소유자: "shot_to, shot_focus_to 등의 카메라가 시간이 안 먹고 있어") ──

    [Fact]
    public void shot_to는_duration만큼_카메라가_흐른다()
    {
        // ⚠ 샷은 RectNode가 아니라 StageState.Shot이다 — 노드 차이만 보던 계획이
        // 카메라를 못 봐서 zoom·pan이 늘 스냅이었다.
        StageMotionPlan plan = Plan(Command(
            "shot.shot_to", ("zoom", "4"), ("x", "2u"), ("y", "0u"), ("duration", "12fr")))!;

        MotionTween tween = Assert.Single(plan.Tweens);
        Assert.NotNull(tween.Shot);
        MotionShotTween shot = tween.Shot!;

        // 출발은 기본 카메라, 도착은 적은 값이다.
        Assert.Equal(0, shot.From.Zoom, 3);
        Assert.Equal(4, shot.To.Zoom, 3);

        // 중간은 출발도 도착도 아니다 — 시간이 실제로 흐른다.
        float middle = plan.Evaluate(0.2).Shot.Zoom;
        Assert.NotEqual(shot.From.Zoom, middle, 3);
        Assert.NotEqual(shot.To.Zoom, middle, 3);
        Assert.InRange(middle, 0.0001f, 4f);

        // 진행 0 = 라인 시작, 충분히 지나면 확정이다.
        Assert.Equal(0, plan.Evaluate(0).Shot.Zoom, 3);
        Assert.Equal(4, plan.Evaluate(10).Shot.Zoom, 3);
        Assert.Equal(4, plan.Final.Shot.Zoom, 3);
    }

    [Fact]
    public void shot_focus_to도_같은_규칙으로_흐르고_pan이_함께_간다()
    {
        StageMotionPlan plan = Plan(Command(
            "shot.shot_focus_to",
            ("slot", "c1"), ("focus", "face"), ("screenPoint", "center"),
            ("zoom", "3"), ("duration", "0.5s")))!;

        MotionTween tween = Assert.Single(plan.Tweens);
        Assert.NotNull(tween.Shot);
        MotionShotTween shot = tween.Shot!;

        // 카메라가 확대되고 pan도 움직인다 — focus를 화면 지점으로 데려오는 일이다.
        Assert.Equal(3, shot.To.Zoom, 3);
        Assert.True(
            Math.Abs(shot.To.PanInRigSpace.X - shot.From.PanInRigSpace.X) > 0.5f ||
            Math.Abs(shot.To.PanInRigSpace.Y - shot.From.PanInRigSpace.Y) > 0.5f,
            "focus를 데려오려면 pan이 움직여야 한다");

        // 중간 프레임의 pan은 두 끝 사이다(런타임과 같은 셋 Lerp).
        Vec2 middle = plan.Evaluate(0.25).Shot.PanInRigSpace;
        Assert.InRange(
            middle.X,
            Math.Min(shot.From.PanInRigSpace.X, shot.To.PanInRigSpace.X),
            Math.Max(shot.From.PanInRigSpace.X, shot.To.PanInRigSpace.X));
    }

    [Fact]
    public void 카메라가_흐르면_무대_위의_모든_슬롯이_흐르는_것으로_센다()
    {
        // 샷은 슬롯 하나의 일이 아니다 — 화면 전체가 함께 움직인다.
        StageMotionPlan plan = Plan(Command(
            "shot.shot_to", ("zoom", "4"), ("x", "2u"), ("y", "0u"), ("duration", "12fr")))!;

        Assert.Contains("c1", plan.AnimatedSlots);
    }

    [Fact]
    public void 시간이_0인_카메라는_계획에_들어오지_않는다()
    {
        // 런타임도 duration 0 이하면 즉시 스냅이다(ShotIntentCommandBase).
        Assert.Null(Plan(Command(
            "shot.shot_to", ("zoom", "4"), ("x", "2u"), ("y", "0u"), ("duration", "0fr"))));
    }

    [Fact]
    public void 샷의_이징_칸이_계획에_실린다()
    {
        // 2026-08-21 런타임이 샷 5종의 마지막 위치 인자로 이징을 열었다 —
        // place·size와 같은 자리이므로 계획도 같은 통로(EaseOf)로 읽는다.
        StageMotionPlan eased = Plan(Command(
            "shot.shot_to",
            ("zoom", "4"), ("x", "2u"), ("y", "0u"), ("duration", "12fr"), ("ease", "InOutCubic")))!;

        Assert.Equal("InOutCubic", Assert.Single(eased.Tweens).Ease);

        // 안 적으면 null — 런타임 스펙 기본(OutCubic)으로 물러선다(토큰도 안 나간다).
        StageMotionPlan bare = Plan(Command(
            "shot.shot_to", ("zoom", "4"), ("x", "2u"), ("y", "0u"), ("duration", "12fr")))!;

        Assert.Null(Assert.Single(bare.Tweens).Ease);
        Assert.Equal(EaseKind.OutCubic, StageMotionPlan.EaseKindOf(null));

        // 이징이 다르면 중간 프레임의 zoom도 다르다 — 모양이 실제로 곡선을 탄다.
        Assert.NotEqual(
            eased.Evaluate(0.15).Shot.Zoom,
            bare.Evaluate(0.15).Shot.Zoom,
            3);
    }

    // ── 배율 (2026-08-21 런타임이 scale 3종에 이징을 열었다) ────────────────

    [Fact]
    public void scale_by는_시간에_따라_배율이_자란다()
    {
        // 축 선언 없이 폴드 차이가 근거다 — place·size와 같은 규칙 하나.
        StageMotionPlan plan = Plan(Command(
            "char_rig_staging.scale_by",
            ("slot", "c1"), ("multiplier", "1.5"), ("duration", "24fr")))!;

        MotionTween tween = Assert.Single(plan.Tweens);
        MotionNodeTween node = Assert.Single(
            tween.Nodes,
            item => item.NodeKey.EndsWith("CharSlot_Scale", StringComparison.Ordinal));

        Assert.Equal(1f, node.From.LocalScale.X, 3);
        Assert.Equal(1.5f, node.To.LocalScale.X, 3);

        // 중간은 출발도 도착도 아니다 — 스냅이 아니라 시간이 흐른다.
        float middle = plan.Evaluate(tween.DurationSeconds / 2)
            .Nodes.GetState(node.NodeKey).LocalScale.X;
        Assert.NotEqual(node.From.LocalScale.X, middle, 3);
        Assert.NotEqual(node.To.LocalScale.X, middle, 3);
        Assert.Equal(1.5f, plan.Evaluate(10).Nodes.GetState(node.NodeKey).LocalScale.X, 3);
    }

    [Fact]
    public void char_scale_to는_코어_미이관이라_프리뷰가_시간을_못_그리고_그렇다고_말한다()
    {
        // ⚠ 런타임은 duration·ease로 잘 돈다. 못 그리는 쪽은 <b>프리뷰</b>다 —
        // 코어 리듀서에 char_scale_to가 없다(scale_by·scale_reset만 있다).
        // 조용히 삼키지 않고 Unhandled로 소리를 내는 것이 지금의 정답이다.
        PresentationResultCommand command = Command(
            "char_rig_presentation.char_scale_to",
            ("slot", "c1"), ("xy", "1.2"), ("duration", "10fr"), ("ease", ""));

        Assert.Null(Plan(command));

        StageState folded = CoreStageFold.Fold(
            Catalog, Setup(), [new MiniStageFoldLine("ln1", false, [command])], LoadTuning()).CoreState!;

        Assert.Contains(folded.Unhandled, item =>
            item.Command.Name == "char_scale_to" && item.Reason.Contains("코어"));
    }

    [Fact]
    public void scale_by의_이징_칸이_계획에_실리고_리셋은_배율을_되돌린다()
    {
        StageMotionPlan eased = Plan(Command(
            "char_rig_staging.scale_by",
            ("slot", "c1"), ("multiplier", "1.5"), ("duration", "24fr"), ("ease", "OutBack")))!;

        Assert.Equal("OutBack", Assert.Single(eased.Tweens).Ease);

        // 리셋도 같은 통로다 — 키운 배율이 시간을 두고 1로 돌아온다.
        PresentationResultCommand grow = Command(
            "char_rig_staging.scale_by", ("slot", "c1"), ("multiplier", "1.5"), ("duration", "0fr"));
        PresentationResultCommand reset = Command(
            "char_rig_staging.scale_reset", ("slot", "c1"), ("duration", "12fr"), ("ease", "InOutQuad"));

        StageMotionPlan plan = Plan(grow, reset)!;
        MotionTween tween = Assert.Single(plan.Tweens); // 0fr인 grow는 스냅이라 빠진다

        Assert.Equal("scale_reset", tween.OutputCommand);
        Assert.Equal("InOutQuad", tween.Ease);
        Assert.Contains(tween.Nodes, node => node.From.LocalScale.X > node.To.LocalScale.X);
    }

    // ── 넛지·등속 (2026-08-21) ──────────────────────────────────────────────

    [Fact]
    public void 넛지_4종은_방향축에서_duration만큼_흐른다()
    {
        // left·right는 CharSlot_Track_X, up·down은 CharSlot_Track_Y — 코어가 접는
        // 그 노드와 같아야 "재생 = 정지 프레임"이 유지된다.
        foreach ((string id, string axisNode) in new[]
                 {
                     ("char_rig_entrance.left", "CharSlot_Track_X"),
                     ("char_rig_entrance.right", "CharSlot_Track_X"),
                     ("char_rig_entrance.up", "CharSlot_Track_Y"),
                     ("char_rig_entrance.down", "CharSlot_Track_Y"),
                 })
        {
            StageMotionPlan plan = Plan(Command(
                id, ("slot", "c1"), ("distance", "3u"), ("duration", "12fr")))!;

            MotionTween tween = Assert.Single(plan.Tweens);
            Assert.Equal(0.5, tween.DurationSeconds, 3);

            MotionNodeTween node = Assert.Single(
                tween.Nodes, item => item.NodeKey.EndsWith(axisNode, StringComparison.Ordinal));

            Vec2 middle = PositionOf(plan.Evaluate(0.25), node.NodeKey);
            Assert.NotEqual(node.From.AnchoredPosition, middle);
            Assert.NotEqual(node.To.AnchoredPosition, middle);
        }
    }

    [Fact]
    public void 넛지의_이징_칸이_계획에_실린다()
    {
        // 2026-08-21 런타임이 넛지 4종에도 이징을 열었다(항수 3→4) — place·size·shot·
        // scale과 같은 마지막 위치 인자다.
        StageMotionPlan eased = Plan(Command(
            "char_rig_entrance.left",
            ("slot", "c1"), ("distance", "3u"), ("duration", "12fr"), ("ease", "InOutSine")))!;

        Assert.Equal("InOutSine", Assert.Single(eased.Tweens).Ease);

        // 안 적으면 null — 런타임 스펙 기본(OutCubic)으로 물러선다(토큰도 안 나간다).
        StageMotionPlan bare = Plan(Command(
            "char_rig_entrance.left", ("slot", "c1"), ("distance", "3u"), ("duration", "12fr")))!;

        Assert.Null(Assert.Single(bare.Tweens).Ease);

        // 이징이 다르면 중간 프레임의 자리도 다르다 — 모양이 실제로 곡선을 탄다.
        string node = Assert.Single(eased.Tweens[0].Nodes).NodeKey;
        Assert.NotEqual(
            PositionOf(eased.Evaluate(0.15), node).X,
            PositionOf(bare.Evaluate(0.15), node).X);
    }
}
