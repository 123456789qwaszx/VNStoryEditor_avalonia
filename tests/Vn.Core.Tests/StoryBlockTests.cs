using Vn.Core.Analysis;
using Vn.Core.Story;

namespace Vn.Core.Tests;

/// <summary>
/// 트리 조립의 최소 확인. 중첩과 조건문의 여러 형태는 다음 커밋에서 체계적으로 검증한다.
/// 여기서는 Valid 샘플과, 앞선 시도를 무너뜨린 중첩 케이스 하나를 잡아둔다.
/// </summary>
public class StoryBlockTests
{
    private static StoryNode ValidStart()
    {
        AnalysisReport report = new VnProjectAnalyzer().Analyze(
            "../../../../../samples/Valid/Demo.yarnproject",
            "../../../../../samples/Valid/game.schema.json");

        return Assert.Single(report.Nodes, node => node.Title == "Start");
    }

    private static StoryBlock SingleBlock(IReadOnlyList<StoryElement> elements)
    {
        return Assert.IsType<StoryBlockElement>(
            Assert.Single(elements, element => element is StoryBlockElement)).Block;
    }

    [Fact]
    public void Start는_대사_하나와_선택지_블록_하나다()
    {
        StoryNode start = ValidStart();

        Assert.Collection(
            start.Body,
            element => Assert.Equal(3, Assert.IsType<StoryLineElement>(element).Line.Line),
            element => Assert.IsType<StoryBlockElement>(element));

        StoryBlock block = SingleBlock(start.Body);

        Assert.Equal(StoryBlockKind.Option, block.Kind);
        Assert.Equal(2, block.Branches.Count);
        Assert.Equal(0, block.Depth);
        Assert.Equal(7, block.StartLine);
    }

    /// <summary>
    /// 이 변경의 실질. 12행의 <c>&lt;&lt;jump Ending&gt;&gt;</c>는 뒤에 라인이 없어서
    /// 평평한 모델에서 통째로 버려지던 것이다. 트리에서는 갈래의 목적지로 제자리를 찾는다.
    /// </summary>
    [Fact]
    public void 갈래_끝의_점프는_목적지로_올라간다()
    {
        StoryBlock block = SingleBlock(ValidStart().Body);

        Assert.All(block.Branches, branch => Assert.Equal("Ending", branch.Destination));

        // 둘째 갈래의 내용은 12행 점프뿐이었다. 그것이 올라갔으므로 남는 것이 없다.
        StoryBranch second = block.Branches[1];

        Assert.Equal("잠시 기다린다", second.Label);
        Assert.Empty(second.Children);
        Assert.Empty(second.Commands);
    }

    /// <summary>
    /// 갈래 안에 대사가 하나도 없으면 명령이 갈래의 내용 전부다.
    /// 트리가 명령을 담지 않던 때에는 이 셋이 통째로 보이지 않았다.
    /// </summary>
    [Fact]
    public void 대사_없는_갈래의_명령도_트리에_남는다()
    {
        StoryBranch first = SingleBlock(ValidStart().Body).Branches[0];

        Assert.Equal("열쇠를 건넨다", first.Label);
        Assert.Empty(first.Children);

        // 점프는 목적지로 빠지고 나머지 둘이 남는다.
        Assert.Equal(
            new[] { "<<set $has_room_key = true>>", "<<give_item \"room_key\">>" },
            first.Commands.Select(command => command.Raw));

        Assert.Equal(new[] { 8, 9 }, first.Commands.Select(command => command.Line));
        Assert.All(first.Commands, command => Assert.Equal(1, command.Depth));
    }

    /// <summary>
    /// 앞선 시도가 무너진 자리. 들여쓴 <c>&lt;&lt;if&gt;&gt;</c>가 일반 명령으로 분류되어
    /// 선택지 갈래 안의 조건 블록이 통째로 사라졌었다.
    /// </summary>
    [Fact]
    public void 선택지_갈래_안의_조건이_블록으로_중첩된다()
    {
        StoryNode node = Fixture.Node("""
            title: T
            ---
            -> 첫째 선택지
                <<if $favor >= 5>>
                라루: 안쪽.
                <<endif>>
                윌로: 갈래 끝.
            -> 둘째 선택지
                아야메: 둘째.
            ===
            """);

        StoryBlock options = SingleBlock(node.Body);

        Assert.Equal(StoryBlockKind.Option, options.Kind);

        StoryBranch first = options.Branches[0];

        StoryBlock nested = Assert.IsType<StoryBlockElement>(first.Children[0]).Block;

        Assert.Equal(StoryBlockKind.Condition, nested.Kind);
        Assert.Equal(4, nested.StartLine);
        Assert.Equal(6, nested.EndLine);
        Assert.Equal(1, nested.Depth);

        StoryBranch conditionBranch = Assert.Single(nested.Branches);
        Assert.Equal("<<if $favor >= 5>>", conditionBranch.Label);
        Assert.Null(conditionBranch.Destination);

        // 조건 뒤의 대사는 조건 블록 밖, 선택지 갈래 안이다.
        Assert.Equal(
            7,
            Assert.IsType<StoryLineElement>(first.Children[1]).Line.Line);
    }

    /// <summary>
    /// 승격된 if 계열은 트리 안의 라인에서 빠진다. 블록 라벨과 박스 안에 같은 것이 두 번 보이면 안 된다.
    /// 평평한 <see cref="StoryNode.Lines"/>는 그대로 두므로 CLI 픽스처는 영향을 받지 않는다.
    /// </summary>
    [Fact]
    public void 승격된_조건은_트리의_쌓인_명령에서_빠지고_평평한_목록에는_남는다()
    {
        StoryNode node = Fixture.Node("""
            title: T
            ---
            <<if $favor >= 5>>
            라루: 안쪽.
            <<endif>>
            ===
            """);

        StoryBlock block = SingleBlock(node.Body);
        StoryBranch branch = Assert.Single(block.Branches);

        StoryLine inTree = Assert.IsType<StoryLineElement>(branch.Children[0]).Line;
        Assert.Empty(inTree.CommandsSincePreviousLine);

        StoryLine flat = Assert.Single(node.Lines);
        Assert.Equal(
            new[] { "<<if $favor >= 5>>" },
            flat.CommandsSincePreviousLine.Select(command => command.Raw));
    }
}
