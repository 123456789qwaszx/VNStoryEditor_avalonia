using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests.Chapters;

/// <summary>
/// 챕터 런 상태 + 관문 판정의 단일 구현 (2026-08-27) — 무대 프리뷰가 "선택을 따라가며
/// 스탯이 챕터 단위로 누적"되도록 증명기·워커의 규칙을 한 벌로 뽑아 셋이 함께 쓴다.
/// 시드는 `스탯` 시트 초기값, 변화는 간선 커밋뿐(최소~최대로 잘라낸다), 판정은 커밋 전 값.
/// </summary>
public sealed class ChapterRunStateTests
{
    private static ChapterGraphModel Model(
        IReadOnlyList<ChapterStat>? stats = null,
        IReadOnlyList<ChapterCondition>? conditions = null) => new(
        "ch01",
        "chapters/ch01.xlsx",
        [],
        [],
        conditions ?? [],
        stats ?? [],
        [],
        []);

    private static ChapterStat Trust(int initial = 0) =>
        new("trust", "신뢰", initial, 0, 5, SourceRow: 2);

    private static ChapterCondition Condition(
        string label, ConditionComparison comparison, int value, bool isValid = true) => new(
        label,
        $"trust 조건 원문",
        Description: null,
        [new ConditionTerm(ConditionTermKind.StatComparison, "trust", comparison, value)],
        isValid,
        SourceRow: 2);

    [Fact]
    public void 시드는_스탯_시트의_초기값이다()
    {
        var run = new ChapterRunState(Model(stats: [Trust(initial: 3)]));

        ChapterRunStatValue value = Assert.Single(run.Values);
        Assert.Equal(3, value.Value);
        Assert.Equal("3", value.DisplayText);
    }

    [Fact]
    public void 커밋은_증감을_경계로_잘라낸다()
    {
        var run = new ChapterRunState(Model(stats: [Trust(initial: 4)]));

        run.Commit([new StatDelta("trust", 3)]); // 4+3=7 → 최대 5
        Assert.Equal(5, Assert.Single(run.Values).Value);

        run.Commit([new StatDelta("trust", -9)]); // 5-9=-4 → 최소 0
        Assert.Equal(0, Assert.Single(run.Values).Value);
    }

    [Fact]
    public void 깃발_지정은_이전_값을_보지_않는다()
    {
        ChapterStat flag = new("met", "만남", 0, 0, 1, SourceRow: 2, ChapterStatType.Bool);
        var run = new ChapterRunState(Model(stats: [flag]));

        run.Commit([new StatDelta("met", 1, StatChangeKind.Set)]);
        Assert.Equal("true", Assert.Single(run.Values).DisplayText);

        run.Commit([new StatDelta("met", 0, StatChangeKind.Set)]);
        Assert.Equal("false", Assert.Single(run.Values).DisplayText);
    }

    [Fact]
    public void 판정은_지금_값으로_한다()
    {
        var run = new ChapterRunState(Model(
            stats: [Trust(initial: 0)],
            conditions: [Condition("신뢰2", ConditionComparison.AtLeast, 2)]));

        Assert.Equal(ChapterGateVerdict.Blocked, run.Judge("신뢰2"));

        run.Commit([new StatDelta("trust", 2)]);
        Assert.Equal(ChapterGateVerdict.Open, run.Judge("신뢰2"));
    }

    [Fact]
    public void 라벨_없는_관문은_언제나_열려_있다()
    {
        var run = new ChapterRunState(Model());

        Assert.Equal(ChapterGateVerdict.Open, run.Judge(null));
        Assert.Equal(ChapterGateVerdict.Open, run.Judge(string.Empty));
    }

    [Fact]
    public void 미정의_라벨과_깨진_식은_막힘이_아니라_깨짐이다()
    {
        // 스탯의 문제와 데이터의 문제를 가른다 — 화면이 "왜 못 가는가"를 다르게 말한다.
        // 어느 쪽이든 지나갈 수는 없다(깨진 조건을 통과시켜 도달을 부풀리지 않는다 — 증명기와 한 벌).
        var run = new ChapterRunState(Model(
            stats: [Trust()],
            conditions: [Condition("깨짐", ConditionComparison.AtLeast, 2, isValid: false)]));

        Assert.Equal(ChapterGateVerdict.Broken, run.Judge("없는라벨"));
        Assert.Equal(ChapterGateVerdict.Broken, run.Judge("깨짐"));
    }

    [Fact]
    public void 조건이_등록되지_않은_스탯키를_보면_막힘이다()
    {
        // 구조 검증이 이미 오류로 잡는 데이터 — 판정은 증명기와 같게 "못 지나감"이다.
        var run = new ChapterRunState(Model(
            stats: [Trust()],
            conditions:
            [
                new ChapterCondition(
                    "유령키", "karma >= 1", null,
                    [new ConditionTerm(ConditionTermKind.StatComparison, "karma", ConditionComparison.AtLeast, 1)],
                    IsValid: true, SourceRow: 2)
            ]));

        Assert.Equal(ChapterGateVerdict.Blocked, run.Judge("유령키"));
    }
}
