using Ked.Presentation.Core;
using Vn.Authoring.Assets;
using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;
using Vn.Authoring.Results;

namespace Vn.Authoring.Tests;

/// <summary>
/// 슬라이드가 <b>프리뷰까지</b> 온다 (2026-08-24 소유자: "SlideOut, SlideIn이 지금 동작하지
/// 않는데"). 코어 폴드는 <c>SlideReductionTests</c>가 지키고, 여기서는 그것이 무대 화면과
/// 시간 계획에 실제로 닿는지를 본다 — 접혀도 <c>DrawnCoreCommands</c>에 없으면 "접혔지만
/// 안 그린다"로 남고, 사람 눈에는 여전히 안 먹는 것이다.
/// </summary>
public sealed class SlidePreviewTests
{
    private static readonly PresentationCommandCatalog Catalog = PresentationCommandCatalog.Default;

    private static readonly string FixtureDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TuningFixtures", "ExportedTuning"));

    private static StageReducerTuning Tuning => RuntimeTuningLibrary.Load(FixtureDirectory, (1920, 1080)).Tuning!;

    private static PresentationResultCommand Command(
        string definitionId, params (string Key, string Value)[] args)
    {
        return new PresentationResultCommand(
            Identifier.PresentationCommand(),
            definitionId,
            args.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    }

    private static PresentationResultCommand[] Setup =>
    [
        Command("char_rig_cast.slot", ("slotKey", "c1")),
        Command("char_rig_cast.cast", ("slot", "c1"), ("characterKey", "parkeunseol"), ("emotionKey", "1")),
        Command("char_rig_entrance.show", ("slot", "c1")),
    ];

    private static CoreStageFoldResult FoldWith(params PresentationResultCommand[] lineCommands) =>
        CoreStageFold.Fold(
            Catalog, Setup, [new MiniStageFoldLine("ln1", false, lineCommands)], Tuning);

    private static double TrackX(StageState core) =>
        core.Nodes.GetState("c1/CharSlot_Track").AnchoredPosition.X;

    // ── 뱃지가 걷힌다 ───────────────────────────────────────────────────────

    [Fact]
    public void 슬라이드는_이제_반영_안_된_연출이_아니다()
    {
        // ⛔ 소유자가 본 그 화면이다 — 리듀서·폴드에 케이스가 없어 둘 다 뱃지로만 남았다.
        CoreStageFoldResult fold = FoldWith(
            Command("char_rig_presentation.slide_in", ("slot", "c1")),
            Command("char_rig_presentation.slide_out", ("slot", "c1")));

        Assert.DoesNotContain(fold.State.Unhandled, entry =>
            entry.CommandName.StartsWith("slide", StringComparison.Ordinal));
    }

    [Fact]
    public void 슬라이드는_그리는_커맨드_목록에_있다()
    {
        // 접히기만 하고 이 목록에 없으면 "접혔지만 안 그린다"(FoldedButNotDrawn)로 남는다 —
        // 상태는 맞는데 화면이 안 움직이는, 가장 헷갈리는 모양이다.
        Assert.Contains("slide_in", CoreStageFold.DrawnCoreCommands);
        Assert.Contains("slide_out", CoreStageFold.DrawnCoreCommands);
    }

    // ── 정착 상태 ───────────────────────────────────────────────────────────

    [Fact]
    public void slide_out은_무대에서_실제로_나간다()
    {
        double before = TrackX(FoldWith().CoreState!);
        double after = TrackX(FoldWith(
            Command("char_rig_presentation.slide_out", ("slot", "c1"), ("direction", "right"))).CoreState!);

        Assert.True(after > before, $"오른쪽으로 나가야 한다 — before={before}, after={after}");
    }

    [Fact]
    public void slide_in은_무대의_자리를_바꾸지_않는다()
    {
        // 정착 상태가 항등인 것이 <b>옳다</b> — 들어오는 움직임은 시간 축의 일이다.
        double before = TrackX(FoldWith().CoreState!);
        double after = TrackX(FoldWith(
            Command("char_rig_presentation.slide_in", ("slot", "c1"))).CoreState!);

        Assert.Equal(before, after, 3);
    }

    // ── 시간 축 ─────────────────────────────────────────────────────────────

    [Fact]
    public void slide_out은_타임라인과_재생이_태운다()
    {
        // 노드 차이에서 저절로 나온다 — move_by와 같은 rect를 밀기 때문이다.
        PresentationResultCommand[] line =
        [
            Command("char_rig_presentation.slide_out", ("slot", "c1"), ("direction", "right"))
        ];

        StageMotionPlan? plan = StageMotionPlan.Build(
            Catalog, Setup, [new MiniStageFoldLine("ln1", false, line)], line, Tuning);

        Assert.NotNull(plan);
        Assert.Contains("c1", plan!.AnimatedSlots);
    }

    private static StageMotionPlan PlanOf(params PresentationResultCommand[] line) =>
        StageMotionPlan.Build(
            Catalog, Setup, [new MiniStageFoldLine("ln1", false, line)], line, Tuning)!;

    private static double TrackXAt(StageMotionPlan plan, double seconds) =>
        plan.Evaluate(seconds).Nodes.GetState("c1/CharSlot_Track").AnchoredPosition.X;

    [Fact]
    public void slide_in은_화면_밖에서_제자리로_들어온다()
    {
        // ⛔ 등장의 핵심 — 순변위가 0이라 <b>노드 차이로는 아무것도 안 보인다.</b> 화면 밖
        //    출발점을 `현재 자리 + 방향 × 거리`로 합성해야 들어오는 움직임이 산다.
        StageMotionPlan plan = PlanOf(
            Command("char_rig_presentation.slide_in", ("slot", "c1"), ("direction", "left")));

        Assert.Contains("c1", plan.AnimatedSlots);

        double rest = TrackXAt(plan, plan.LongestSeconds);
        double start = TrackXAt(plan, 0);

        // left에서 온다 = 출발점이 제자리보다 왼쪽이고, 끝나면 제자리다.
        Assert.True(start < rest - 100, $"왼쪽 화면 밖에서 시작해야 한다 — start={start}, rest={rest}");
        Assert.Equal(0, rest, 3);
    }

    [Fact]
    public void 등장_방향은_들어오는_쪽을_뜻한다()
    {
        // direction은 "온 방향"이다(퇴장의 "갈 방향"과 반대 뜻) — 여기서 갈리면 캐릭터가
        // 반대편에서 들어온다.
        double fromLeft = TrackXAt(PlanOf(
            Command("char_rig_presentation.slide_in", ("slot", "c1"), ("direction", "left"))), 0);
        double fromRight = TrackXAt(PlanOf(
            Command("char_rig_presentation.slide_in", ("slot", "c1"), ("direction", "right"))), 0);

        Assert.True(fromLeft < 0, $"왼쪽에서 = 음수 자리 — {fromLeft}");
        Assert.True(fromRight > 0, $"오른쪽에서 = 양수 자리 — {fromRight}");
        Assert.Equal(-fromLeft, fromRight, 3);
    }

    [Fact]
    public void 슬라이드의_이징은_스펙_상수다()
    {
        // Yarn 인자가 없는 축이라 카탈로그를 뒤져도 없다 — 등장 OutCubic · 퇴장 InCubic.
        // ⚠ 둘이 같으면(기본 폴백) 퇴장이 반대로 흐른다.
        Assert.Equal(
            EaseKind.OutCubic.ToString(),
            PlanOf(Command("char_rig_presentation.slide_in", ("slot", "c1"))).Tweens[0].Ease);

        Assert.Equal(
            EaseKind.InCubic.ToString(),
            PlanOf(Command("char_rig_presentation.slide_out", ("slot", "c1"))).Tweens[0].Ease);
    }

    /// <summary>
    /// 튐이 없는 순수 이징이라면 이 진행도에서 어디였을까 — 튐의 증거는 이것과의 차이다.
    /// (자리는 <c>출발 + 이동거리 × 모양</c>이므로 모양만 알면 자리가 나온다.)
    /// </summary>
    private static double PureEaseShape(EaseKind ease, double progress) =>
        EaseFunctions.Evaluate(ease, (float)progress);

    [Fact]
    public void 등장의_튐은_도착_직전에_앞당긴다()
    {
        // 런타임의 punch(등장 24px) — 별도 좌표 항이 아니라 <b>진행도의 모양</b>으로 얹힌다.
        // 슬라이드는 출발·도착이 한 축 위에 있는 1차원 운동이라 정확히 같은 값이 나온다.
        //
        // ⚠ 기본 수치(24px 대 480px)에서는 도착을 <b>지나치지 않는다</b> — 도착 직전에
        //    부풀어 base보다 앞서 갈 뿐이다("overshoot that settles back"이라는 스펙 주석은
        //    punch를 크게 줬을 때의 이야기다). 그래서 증거는 순수 이징과의 <b>차이</b>다.
        StageMotionPlan plan = PlanOf(
            Command("char_rig_presentation.slide_in", ("slot", "c1"), ("direction", "left")));

        MotionSlidePunch punch = plan.Tweens[0].Punch!;
        Assert.True(punch.TowardEnd, "등장은 도착 직전에 튄다");
        Assert.True(punch.Ratio > 0);

        // 왼쪽에서 오므로 자리 = -거리 × (1 - 모양). 모양이 크면 더 오른쪽이다.
        double start = TrackXAt(plan, 0);
        double distance = -start;

        double PureEaseAt(double t) => start + (distance * PureEaseShape(EaseKind.OutCubic, t));

        // 중간 어딘가에서 순수 이징보다 <b>앞서</b> 있다.
        bool ahead = Enumerable.Range(1, 39)
            .Select(step => step / 40.0)
            .Any(t => TrackXAt(plan, plan.LongestSeconds * t) > PureEaseAt(t) + 0.5);

        Assert.True(ahead, "튐이 얹히면 순수 이징보다 앞서는 순간이 있어야 한다");

        // 그래도 <b>양 끝은 그대로</b>다 — 튐은 출발도 도착도 옮기지 않는다.
        Assert.Equal(0, TrackXAt(plan, plan.LongestSeconds), 3);
        Assert.Equal(start, TrackXAt(plan, 0), 3);
    }

    [Fact]
    public void 퇴장의_튐은_출발_직후에_앞당긴다()
    {
        StageMotionPlan plan = PlanOf(
            Command("char_rig_presentation.slide_out", ("slot", "c1"), ("direction", "right")));

        MotionSlidePunch punch = plan.Tweens[0].Punch!;
        Assert.False(punch.TowardEnd, "퇴장은 출발 직후에 튄다");

        double end = TrackXAt(plan, plan.LongestSeconds);

        double PureEaseAt(double t) => end * PureEaseShape(EaseKind.InCubic, t);

        // 튐이 가장 큰 앞부분에서 순수 이징을 앞선다.
        bool ahead = Enumerable.Range(1, 19)
            .Select(step => step / 40.0)
            .Any(t => TrackXAt(plan, plan.LongestSeconds * t) > PureEaseAt(t) + 0.5);

        Assert.True(ahead, "출발 직후에 튀어 나가야 한다");
        Assert.Equal(0, TrackXAt(plan, 0), 3);
    }

    [Fact]
    public void 거리가_0이면_태울_것이_없다()
    {
        // 런타임도 제자리다 — 0u 이동과 같은 취급이다.
        StageMotionPlan? plan = StageMotionPlan.Build(
            Catalog,
            Setup,
            [new MiniStageFoldLine("ln1", false, [
                Command("char_rig_presentation.slide_in", ("slot", "c1"), ("distance", "0u"))
            ])],
            [Command("char_rig_presentation.slide_in", ("slot", "c1"), ("distance", "0u"))],
            Tuning);

        Assert.True(plan is null || !plan.AnimatedSlots.Contains("c1"));
    }

    // ── 기존 폴드와의 갈림 ──────────────────────────────────────────────────

    [Fact]
    public void 보완_폴드는_슬라이드를_모른다는_사실이_바뀌지_않았다()
    {
        // 코어가 접으므로 `MiniStageFold`에는 케이스를 넣지 않았다 — 좌표 축의 해석은
        // 코어 하나라는 규칙(H-2)이다. 튜닝이 없는 폴백에서는 여전히 뱃지로 남고,
        // 그것이 옳다: 좌표가 없으면 어디로 나갔는지 말할 수 없다.
        MiniStageState legacy = MiniStageFold.Fold(
            Catalog,
            Setup,
            [new MiniStageFoldLine("ln1", false, [
                Command("char_rig_presentation.slide_out", ("slot", "c1"))
            ])]);

        Assert.Contains(legacy.Unhandled, entry =>
            string.Equals(entry.CommandName, "slide_out", StringComparison.Ordinal));
    }
}
