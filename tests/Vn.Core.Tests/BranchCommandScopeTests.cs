using Vn.Core.Story;

namespace Vn.Core.Tests;

/// <summary>
/// 명령은 쌓였다가 다음 라인이 재생될 때 함께 실행된다. 그런데 "다음 라인"을 원본 순서로만
/// 따지면 <c>&lt;&lt;if&gt;&gt;</c> 갈래의 명령이 <c>&lt;&lt;else&gt;&gt;</c> 갈래의 첫 대사에 붙는다.
/// 둘은 서로 배타적이라 절대 같이 실행되지 않는데도 화면에는 한 박스로 보인다.
///
/// 갈래가 바뀌면 쌓인 것은 그 갈래에 남는다.
/// </summary>
public class BranchCommandScopeTests
{
    private static StoryBlock OnlyBlock(StoryNode node)
    {
        return Assert.IsType<StoryBlockElement>(Assert.Single(node.Body)).Block;
    }

    private static StoryLine FirstLine(StoryBranch branch)
    {
        return Assert.IsType<StoryLineElement>(branch.Children[0]).Line;
    }

    [Fact]
    public void if_갈래의_명령이_else_갈래_대사에_붙지_않는다()
    {
        StoryBlock block = OnlyBlock(Fixture.Node("""
            title: T
            ---
            <<if $favor >= 5>>
                윌로: 참일 때.
                <<set $trust = $trust + 1>>
            <<else>>
                윌로: 거짓일 때.
                <<set $anger = $anger + 1>>
            <<endif>>
            ===
            """));

        Assert.Equal(2, block.Branches.Count);

        // 각 갈래의 대사는 자기 갈래에서 쌓인 것만 본다. 여기서는 앞에 쌓인 것이 없다.
        Assert.All(block.Branches, branch => Assert.Empty(FirstLine(branch).CommandsSincePreviousLine));

        // 대사 뒤에 온 명령은 그 갈래에 남는다. 다음 갈래로 넘어가지 않는다.
        Assert.Equal(
            new[] { "<<set $trust = $trust + 1>>" },
            block.Branches[0].Commands.Select(command => command.Raw));

        Assert.Equal(
            new[] { "<<set $anger = $anger + 1>>" },
            block.Branches[1].Commands.Select(command => command.Raw));
    }

    [Fact]
    public void elseif_세_갈래가_각각_자기_명령만_갖는다()
    {
        StoryBlock block = OnlyBlock(Fixture.Node("""
            title: T
            ---
            <<if $favor >= 8>>
                <<set $trust = 3>>
                라루: 높음.
            <<elseif $favor >= 5>>
                <<set $trust = 2>>
                윌로: 중간.
            <<else>>
                <<set $trust = 1>>
                아야메: 낮음.
            <<endif>>
            ===
            """));

        Assert.Equal(3, block.Branches.Count);

        // 명령이 대사 앞에 오면 그 대사의 박스에 들어간다. 갈래를 넘지 않는다.
        Assert.Equal(
            new[] { "<<set $trust = 3>>" },
            FirstLine(block.Branches[0]).CommandsSincePreviousLine.Select(c => c.Raw));

        Assert.Equal(
            new[] { "<<set $trust = 2>>" },
            FirstLine(block.Branches[1]).CommandsSincePreviousLine.Select(c => c.Raw));

        Assert.Equal(
            new[] { "<<set $trust = 1>>" },
            FirstLine(block.Branches[2]).CommandsSincePreviousLine.Select(c => c.Raw));
    }

    /// <summary>
    /// 조건 블록 뒤에 오는 대사는 블록 안에서 쌓인 것을 물려받지 않는다.
    /// 어느 갈래를 탔는지에 따라 달라지는 것을 한 박스로 보여줄 수 없기 때문이다.
    /// </summary>
    [Fact]
    public void 조건_블록_뒤의_대사는_블록_안_명령을_물려받지_않는다()
    {
        StoryNode node = Fixture.Node("""
            title: T
            ---
            <<if $favor >= 5>>
                윌로: 참일 때.
                <<set $trust = $trust + 1>>
            <<endif>>
            라루: 조건 뒤.
            ===
            """);

        StoryLine after = Assert.IsType<StoryLineElement>(node.Body[1]).Line;

        Assert.Equal("조건 뒤.", after.Text);
        Assert.Empty(after.CommandsSincePreviousLine);
    }
}
