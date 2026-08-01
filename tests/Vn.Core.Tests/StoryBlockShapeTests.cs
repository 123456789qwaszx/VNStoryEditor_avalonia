using Vn.Core.Story;

namespace Vn.Core.Tests;

/// <summary>
/// 실제 대본에 나오는 분기 형태들. samples/는 회귀 기준선이라 건드리지 않고 여기서 만든다.
///
/// 두 블록의 경계 판정 방식이 다르다는 것이 이 묶음의 핵심이다.
///   선택지 — 들여쓰기로 갈린다
///   조건   — 구분자로 갈린다. 들여쓰지 않아도 되므로 깊이로는 판정할 수 없다
/// </summary>
public class StoryBlockShapeTests
{
    private static IReadOnlyList<StoryBlock> BlocksOf(string yarn)
    {
        return Flatten(Fixture.Node(yarn).Body).ToList();
    }

    private static IEnumerable<StoryBlock> Flatten(IReadOnlyList<StoryElement> elements)
    {
        foreach (StoryElement element in elements)
        {
            if (element is not StoryBlockElement blockElement)
            {
                continue;
            }

            yield return blockElement.Block;

            foreach (StoryBranch branch in blockElement.Block.Branches)
            {
                foreach (StoryBlock nested in Flatten(branch.Children))
                {
                    yield return nested;
                }
            }
        }
    }

    private static int LineOf(StoryElement element)
    {
        return Assert.IsType<StoryLineElement>(element).Line.Line;
    }

    [Fact]
    public void 조건_블록은_갈래가_둘이다()
    {
        StoryBlock block = Assert.Single(BlocksOf("""
            title: T
            ---
            <<if $favor >= 5>>
            라루: 참.
            <<else>>
            윌로: 거짓.
            <<endif>>
            아야메: 뒤.
            ===
            """));

        Assert.Equal(StoryBlockKind.Condition, block.Kind);
        Assert.Equal(2, block.Branches.Count);
        Assert.Equal(3, block.StartLine);
        Assert.Equal(7, block.EndLine);

        Assert.Equal("<<if $favor >= 5>>", block.Branches[0].Label);
        Assert.Equal(4, LineOf(Assert.Single(block.Branches[0].Children)));

        Assert.Equal("<<else>>", block.Branches[1].Label);
        Assert.Equal(6, LineOf(Assert.Single(block.Branches[1].Children)));

        // 조건 갈래에는 목적지가 없다.
        Assert.All(block.Branches, branch => Assert.Null(branch.Destination));
    }

    [Fact]
    public void elseif가_있으면_갈래가_셋이다()
    {
        StoryBlock block = Assert.Single(BlocksOf("""
            title: T
            ---
            <<if $favor >= 8>>
            라루: 높음.
            <<elseif $favor >= 5>>
            윌로: 중간.
            <<else>>
            아야메: 낮음.
            <<endif>>
            ===
            """));

        Assert.Equal(3, block.Branches.Count);

        Assert.Equal(
            new[] { "<<if $favor >= 8>>", "<<elseif $favor >= 5>>", "<<else>>" },
            block.Branches.Select(branch => branch.Label));

        Assert.Equal(
            new[] { 4, 6, 8 },
            block.Branches.Select(branch => LineOf(Assert.Single(branch.Children))));
    }

    [Fact]
    public void 선택지_안에_조건이_중첩된다()
    {
        IReadOnlyList<StoryBlock> blocks = BlocksOf("""
            title: T
            ---
            -> 첫째 선택지
                <<if $favor >= 5>>
                라루: 안쪽 참.
                <<endif>>
                윌로: 갈래 끝.
            -> 둘째 선택지
                아야메: 둘째.
            ===
            """);

        Assert.Equal(2, blocks.Count);

        StoryBlock options = blocks[0];
        Assert.Equal(StoryBlockKind.Option, options.Kind);
        Assert.Equal(2, options.Branches.Count);

        StoryBlock condition = blocks[1];
        Assert.Equal(StoryBlockKind.Condition, condition.Kind);

        // 중첩된 블록은 첫째 갈래 안에 있고, 깊이가 한 단계 깊다.
        Assert.Equal(0, options.Depth);
        Assert.Equal(1, condition.Depth);

        StoryBranch first = options.Branches[0];
        Assert.Same(condition, Assert.IsType<StoryBlockElement>(first.Children[0]).Block);

        // 조건 뒤의 대사는 조건 밖, 갈래 안이다.
        Assert.Equal(7, LineOf(first.Children[1]));
    }

    [Fact]
    public void 조건_안에_선택지가_중첩된다()
    {
        IReadOnlyList<StoryBlock> blocks = BlocksOf("""
            title: T
            ---
            <<if $favor >= 5>>
            -> 조건 안 선택지 A
                라루: A 본문.
            -> 조건 안 선택지 B
                윌로: B 본문.
            <<endif>>
            ===
            """);

        Assert.Equal(2, blocks.Count);

        StoryBlock condition = blocks[0];
        Assert.Equal(StoryBlockKind.Condition, condition.Kind);
        Assert.Equal(8, condition.EndLine);

        StoryBlock options = blocks[1];
        Assert.Equal(StoryBlockKind.Option, options.Kind);
        Assert.Equal(2, options.Branches.Count);

        Assert.Equal(
            new[] { "조건 안 선택지 A", "조건 안 선택지 B" },
            options.Branches.Select(branch => branch.Label));

        // 선택지 그룹 전체가 조건의 한 갈래 안에 들어간다.
        StoryBranch onlyBranch = Assert.Single(condition.Branches);
        Assert.Same(options, Assert.IsType<StoryBlockElement>(Assert.Single(onlyBranch.Children)).Block);
    }

    /// <summary>
    /// 조건부 선택지의 <c>&lt;&lt;if&gt;&gt;</c>는 갈래를 만들지 않는다.
    /// 한 선택지가 목록에 뜨는지만 정하므로 조건 블록으로 승격하지 않고 라벨에 그대로 남긴다.
    /// 순수 조건문을 승격하는 것과 비대칭이지만 의도된 것이다.
    /// </summary>
    [Fact]
    public void 조건부_선택지는_선택지_갈래이고_조건식이_라벨에_남는다()
    {
        StoryBlock block = Assert.Single(BlocksOf("""
            title: T
            ---
            -> 조용히 믿어본다 <<if $favor >= 8>>
                라루: 믿었다.
            -> 그냥 선택지
                윌로: 안 믿었다.
            ===
            """));

        Assert.Equal(StoryBlockKind.Option, block.Kind);
        Assert.Equal(2, block.Branches.Count);

        Assert.Equal("조용히 믿어본다 <<if $favor >= 8>>", block.Branches[0].Label);
        Assert.Equal("그냥 선택지", block.Branches[1].Label);
    }

    [Fact]
    public void 선택지_블록이_두_개_연속으로_나온다()
    {
        StoryNode node = Fixture.Node("""
            title: T
            ---
            -> 첫 그룹 A
                라루: A.
            -> 첫 그룹 B
                윌로: B.
            아야메: 사이 대사.
            -> 둘째 그룹 A
                라루: 2A.
            -> 둘째 그룹 B
                윌로: 2B.
            ===
            """);

        // 두 그룹이 별개의 블록이고, 사이 대사는 어느 쪽에도 속하지 않는다.
        Assert.Collection(
            node.Body,
            element => Assert.Equal(3, Assert.IsType<StoryBlockElement>(element).Block.StartLine),
            element => Assert.Equal(7, LineOf(element)),
            element => Assert.Equal(8, Assert.IsType<StoryBlockElement>(element).Block.StartLine));

        IReadOnlyList<StoryBlock> blocks = Flatten(node.Body).ToList();

        Assert.Equal(2, blocks.Count);
        Assert.All(blocks, block => Assert.Equal(StoryBlockKind.Option, block.Kind));
        Assert.All(blocks, block => Assert.Equal(2, block.Branches.Count));

        Assert.Equal(
            new[] { "첫 그룹 A", "첫 그룹 B" },
            blocks[0].Branches.Select(branch => branch.Label));

        Assert.Equal(
            new[] { "둘째 그룹 A", "둘째 그룹 B" },
            blocks[1].Branches.Select(branch => branch.Label));
    }
}
