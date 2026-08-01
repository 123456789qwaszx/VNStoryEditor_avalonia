using Vn.Core.Story;

namespace Vn.Core.Tests;

/// <summary>
/// 명령이 갈래 카드와 그 안 박스에 두 번 보이면 안 된다.
///
/// 라인 앞에 온 명령은 그 라인의 박스가 가져간다. 갈래에는 어느 박스도 닫지 못한 것만 남는다.
/// 갈래에 대사가 하나도 없는 경우가 흔한데, 그때는 남은 것이 갈래의 내용 전부다.
/// </summary>
public class BranchCommandDuplicationTests
{
    private static StoryBlock OnlyBlock(StoryNode node)
    {
        return Assert.IsType<StoryBlockElement>(Assert.Single(node.Body)).Block;
    }

    [Fact]
    public void 라인_앞의_명령은_박스에만_있고_갈래에는_없다()
    {
        StoryBlock block = OnlyBlock(Fixture.Node("""
            title: T
            ---
            -> 첫째 선택지
                <<set $anger = $anger + 1>>
                <<set $trust = $trust + 1>>
                라루: 좋아.
            -> 둘째 선택지
                윌로: 알겠습니다.
            ===
            """));

        StoryBranch first = block.Branches[0];

        Assert.Equal(
            new[] { "<<set $anger = $anger + 1>>", "<<set $trust = $trust + 1>>" },
            Assert.IsType<StoryLineElement>(first.Children[0]).Line
                .CommandsSincePreviousLine.Select(command => command.Raw));

        // 갈래 카드에는 남지 않는다. 남으면 화면에서 같은 명령이 두 번 보인다.
        Assert.Empty(first.Commands);
    }

    /// <summary>
    /// 마지막 대사 뒤의 명령은 닫을 박스가 없다. 그것만 갈래에 남는다.
    /// </summary>
    [Fact]
    public void 마지막_대사_뒤의_명령만_갈래에_남는다()
    {
        StoryBlock block = OnlyBlock(Fixture.Node("""
            title: T
            ---
            -> 첫째 선택지
                <<set $anger = $anger + 1>>
                라루: 좋아.
                <<set $trust = $trust + 1>>
            -> 둘째 선택지
                윌로: 알겠습니다.
            ===
            """));

        StoryBranch first = block.Branches[0];

        Assert.Equal(
            new[] { "<<set $anger = $anger + 1>>" },
            Assert.IsType<StoryLineElement>(first.Children[0]).Line
                .CommandsSincePreviousLine.Select(command => command.Raw));

        Assert.Equal(
            new[] { "<<set $trust = $trust + 1>>" },
            first.Commands.Select(command => command.Raw));
    }

    /// <summary>
    /// 대사가 없는 갈래는 명령이 내용 전부다. 이때는 갈래에 남는 것이 맞다.
    /// </summary>
    [Fact]
    public void 대사가_없는_갈래는_명령이_전부_갈래에_남는다()
    {
        StoryNode node = Assert.Single(
            Fixture.Analyze("""
                title: T
                ---
                -> 첫째 선택지
                    <<set $anger = $anger + 1>>
                    <<jump Other>>
                -> 둘째 선택지
                    윌로: 알겠습니다.
                ===

                title: Other
                ---
                윌로: 저쪽.
                ===
                """).Nodes,
            item => item.Title == "T");

        StoryBlock block = OnlyBlock(node);

        StoryBranch first = block.Branches[0];

        Assert.Empty(first.Children);

        // 끝의 <<jump>>는 목적지로 올라가고 나머지가 남는다.
        Assert.Equal("Other", first.Destination);

        Assert.Equal(
            new[] { "<<set $anger = $anger + 1>>" },
            first.Commands.Select(command => command.Raw));
    }
}
