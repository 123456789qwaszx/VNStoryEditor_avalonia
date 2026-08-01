using Vn.Core.Story;
using Vn.Core.Yarn;
using Yarn.Compiler;

namespace Vn.Core.Tests;

/// <summary>
/// 선택지 그룹 뒤에 빈 줄이 오면 Yarn은 다음 줄 맨 앞에 폭 없는
/// <c>BLANK_LINE_FOLLOWING_OPTION</c> 토큰을 붙인다.
///
/// 이것을 건너뛰지 않으면 그 줄이 분류되지 않고 통째로 사라진다.
/// 실제 대본에서 대사 한 줄과 조건 블록 하나가 그렇게 없어졌고, 빌드도 테스트도 픽스처도
/// 전부 통과했다. 작가가 쓴 줄이 조용히 사라지는 경로라 여기서 고정한다.
/// </summary>
public class BlankLineAfterOptionTests
{
    private const string DialogueAfterOptions = """
        title: T
        ---
        -> 첫째 선택지
            라루: 갈래 안.
        -> 둘째 선택지
            윌로: 갈래 안.

        아야메: 그룹 뒤 대사.
        ===
        """;

    private const string ConditionAfterOptions = """
        title: T
        ---
        -> 첫째 선택지
            라루: 갈래 안.
        -> 둘째 선택지
            윌로: 갈래 안.

        <<if $favor >= 5>>
        아야메: 조건 안.
        <<endif>>
        ===
        """;

    [Fact]
    public void 선택지_그룹_뒤_빈_줄_다음의_대사가_Lines에_나타난다()
    {
        StoryNode node = Fixture.Node(DialogueAfterOptions);

        StoryLine line = Assert.Single(node.Lines, item => item.Line == 8);

        Assert.Equal("아야메", line.Speaker);
        Assert.Equal("그룹 뒤 대사.", line.Text);
        Assert.Equal(0, line.Depth);
        Assert.False(line.IsOption);
    }

    [Fact]
    public void 같은_자리의_대사가_트리에도_나타난다()
    {
        StoryNode node = Fixture.Node(DialogueAfterOptions);

        // 선택지 블록 다음에 최상위 라인으로 온다.
        Assert.Collection(
            node.Body,
            element => Assert.IsType<StoryBlockElement>(element),
            element => Assert.Equal(8, Assert.IsType<StoryLineElement>(element).Line.Line));
    }

    [Fact]
    public void 같은_자리의_if가_조건_구분자로_분류된다()
    {
        var job = CompilationJob.CreateFromString("Story.yarn", ConditionAfterOptions);
        job.CompilationType = CompilationJob.Type.FullCompilation;

        CompilationResult result = Compiler.Compile(job);

        YarnScannedLine scanned = Assert.Single(
            YarnBlockScanner.ScanFile(result.ParseResults.Single()),
            item => item.Line == 8);

        Assert.Equal(YarnLineKind.If, scanned.Kind);
        Assert.Equal("<<if $favor >= 5>>", scanned.Raw);
    }

    /// <summary>
    /// 조건 블록은 선택지 갈래보다 얕으므로 갈래 바깥에 놓여야 한다.
    /// 안으로 들어가면 그 안의 대사와 <c>&lt;&lt;endif&gt;&gt;</c>가 블록 밖으로 밀려난다.
    /// </summary>
    [Fact]
    public void 갈래보다_얕은_조건은_갈래_바깥에_놓인다()
    {
        StoryNode node = Fixture.Node(ConditionAfterOptions);

        Assert.Equal(2, node.Body.Count);

        StoryBlock options = Assert.IsType<StoryBlockElement>(node.Body[0]).Block;
        Assert.Equal(StoryBlockKind.Option, options.Kind);
        Assert.All(options.Branches, branch => Assert.Single(branch.Children));

        StoryBlock condition = Assert.IsType<StoryBlockElement>(node.Body[1]).Block;

        Assert.Equal(StoryBlockKind.Condition, condition.Kind);
        Assert.Equal(8, condition.StartLine);
        Assert.Equal(10, condition.EndLine);

        StoryBranch only = Assert.Single(condition.Branches);
        Assert.Equal(9, Assert.IsType<StoryLineElement>(Assert.Single(only.Children)).Line.Line);
    }
}
