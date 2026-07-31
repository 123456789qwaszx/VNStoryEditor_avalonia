using Vn.Core.Validation;

namespace Vn.Core.Tests;

public class NameSuggesterTests
{
    [Fact]
    public void 한글자_다른_이름을_제안한다()
    {
        // Arrange — 테스트에 필요한 값을 준비한다
        string unknown = "$affection_an";
        string[] candidates = { "$affection_ann", "$fatigue" };

        // Act — 실제로 검증할 동작을 실행한다
        string? result = NameSuggester.FindClosest(unknown, candidates);

        // Assert — 결과가 기대와 같은지 확인한다
        Assert.Equal("$affection_ann", result);
    }
    
    [Fact]
    public void 후보와_완전히_같으면_그대로_돌려준다()
    {
        string? result = NameSuggester.FindClosest(
            "$fatigue",
            new[] { "$fatigue", "$affection" });

        Assert.Equal("$fatigue", result);
    }

    [Fact]
    public void 너무_다르면_아무것도_제안하지_않는다()
    {
        string? result = NameSuggester.FindClosest(
            "$xyz",
            new[] { "$completely_different_name" });

        Assert.Null(result);
    }

    [Fact]
    public void 후보가_없으면_null이다()
    {
        string? result = NameSuggester.FindClosest(
            "$affection",
            Array.Empty<string>());

        Assert.Null(result);
    }
}