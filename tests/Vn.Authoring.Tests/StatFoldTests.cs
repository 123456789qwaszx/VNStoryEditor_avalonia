using Vn.Authoring.Flow;
using Vn.Authoring.Model;

namespace Vn.Authoring.Tests;

/// <summary>X3 스탯 HUD — 초기값 + 문서 순서 set 누적. 갈래를 가리지 않는 근사다.</summary>
public class StatFoldTests
{
    private static (string, SetOperatorKind, string) Op(string variable, SetOperatorKind op, string value) =>
        (variable, op, value);

    [Fact]
    public void 초기값에서_시작해_문서_순서로_누적한다()
    {
        IReadOnlyList<StatFold.StatValue> stats = StatFold.Fold(
            [("favor", "0"), ("trust", "3")],
            [
                Op("favor", SetOperatorKind.Add, "2"),
                Op("favor", SetOperatorKind.Add, "1"),
                Op("trust", SetOperatorKind.Subtract, "1"),
                Op("favor", SetOperatorKind.Assign, "10")
            ]);

        Assert.Equal(["favor", "trust"], stats.Select(stat => stat.Variable)); // 등록 순서 유지
        Assert.Equal("10", stats[0].Display); // = 는 교체
        Assert.Equal("2", stats[1].Display);
    }

    [Fact]
    public void 등록되지_않은_변수의_set은_스탯에_나타나지_않는다()
    {
        IReadOnlyList<StatFold.StatValue> stats = StatFold.Fold(
            [("favor", "0")],
            [Op("hidden", SetOperatorKind.Add, "5")]);

        Assert.Single(stats);
        Assert.Equal("favor", stats[0].Variable);
    }

    [Fact]
    public void 수치가_아닌_값은_원문이_남고_조용히_0이_되지_않는다()
    {
        IReadOnlyList<StatFold.StatValue> stats = StatFold.Fold(
            [("route", "\"none\""), ("flag", "false")],
            [
                Op("route", SetOperatorKind.Add, "1"),      // 누적 불가 → 원문 유지
                Op("flag", SetOperatorKind.Assign, "true")  // bool = 교체는 된다
            ]);

        Assert.Equal("\"none\"", stats[0].Display);
        Assert.Equal("true", stats[1].Display);
    }

    [Fact]
    public void 소수는_깔끔하게_정수는_정수로_표시된다()
    {
        IReadOnlyList<StatFold.StatValue> stats = StatFold.Fold(
            [("favor", "1.5")],
            [Op("favor", SetOperatorKind.Add, "0.5")]);

        Assert.Equal("2", stats[0].Display);
    }
}
