using Vn.Authoring.Chapters;

namespace Vn.Authoring.Tests;

/// <summary>
/// 작가 조건의 <b>식 ↔ 세 칸</b> 왕복 (2026-08-17 소유자: "대사노드에서 Set을 다루는 감각과
/// 동일하게, 만든 아이템·능력을 고르고 부호를 고른 뒤 수치를 입력하도록").
///
/// 화면이 식을 짓고 다시 읽으므로, 한쪽이 다른 쪽을 못 읽으면 <b>작가가 적은 조건이 조용히
/// 빈 칸으로 보인다</b>. 분해기는 챕터 `조건` 시트와 같은 것 하나를 쓴다(사본 금지) —
/// 작가 식에는 <c>$</c>가 붙으므로 떼고 넣는다.
/// </summary>
public sealed class ConditionRoundTripTests
{
    private static (string Name, string Op, string Value) Decompose(string expression)
    {
        Assert.True(
            ConditionExpressionParser.TryDecomposeSingle(
                expression.Replace("$", string.Empty, StringComparison.Ordinal),
                out string name, out string op, out string value),
            $"'{expression}'을 세 칸으로 나누지 못했습니다.");

        return (name, op, value);
    }

    [Theory]
    [InlineData("$약초 >= 3", "약초", ">=", "3")]
    [InlineData("$약초 <= 1", "약초", "<=", "1")]
    [InlineData("$약초 == 0", "약초", "==", "0")]
    [InlineData("$위험 > 5", "위험", ">", "5")]
    [InlineData("$위험 < 2", "위험", "<", "2")]
    public void 아이템_조건은_이름_부호_수치로_나뉜다(string expression, string name, string op, string value)
    {
        Assert.Equal((name, op, value), Decompose(expression));
    }

    [Theory]
    [InlineData("$자물쇠따기 == true", "true")]
    [InlineData("$자물쇠따기 == false", "false")]
    public void 능력_조건은_부호_자리에_On_Off가_온다(string expression, string state)
    {
        // 능력에는 비교 부호가 없다(소유자 지시) — 분해기도 true/false를 부호 자리에 준다.
        (string name, string op, string value) = Decompose(expression);

        Assert.Equal("자물쇠따기", name);
        Assert.Equal(state, op);
        Assert.Empty(value); // 값 칸은 쓰지 않는다
    }

    [Fact]
    public void 복합식은_나누지_않는다()
    {
        // 손으로 적어 둔 여러 항은 그대로 지킨다 — 조용히 뭉개면 원고가 사라진다.
        Assert.False(ConditionExpressionParser.TryDecomposeSingle(
            "약초 >= 3; 위험 < 2", out _, out _, out _));
    }
}
