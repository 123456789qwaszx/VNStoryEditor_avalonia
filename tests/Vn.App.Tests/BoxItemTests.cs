using Vn.App.Views;
using Vn.Core;
using Vn.Core.Analysis;
using Vn.Core.Story;

namespace Vn.App.Tests;

/// <summary>
/// 박스 뷰가 화면에 무엇을 그릴지는 <see cref="BoxItem"/>이 정한다.
/// 클릭은 자동으로 확인할 수 없지만, 박스 개수와 각 칸의 내용은 여기서 고정할 수 있다.
/// </summary>
public class BoxItemTests
{
    private const string ValidProject = "../../../../../samples/Valid/Demo.yarnproject";
    private const string ValidSchema = "../../../../../samples/Valid/game.schema.json";

    private static IReadOnlyList<BoxItem> BoxesOf(string title)
    {
        AnalysisReport report =
            new VnProjectAnalyzer().Analyze(ValidProject, ValidSchema);

        StoryNode node = Assert.Single(report.Nodes, n => n.Title == title);

        return node.Lines.Select(line => new BoxItem(line)).ToList();
    }

    [Fact]
    public void Start를_고르면_박스가_셋이다()
    {
        Assert.Equal(3, BoxesOf("Start").Count);
    }

    [Fact]
    public void 첫_박스는_명령_없이_대사만_있다()
    {
        BoxItem box = BoxesOf("Start")[0];

        Assert.False(box.HasCommands);
        Assert.Equal("Ann", box.Speaker);
        Assert.Equal("어서 오세요.", box.Text);
        Assert.False(box.IsOption);
        Assert.False(box.HasHashtags);
    }

    [Fact]
    public void 둘째_박스는_명령_두_줄과_선택지다()
    {
        BoxItem box = BoxesOf("Start")[1];

        Assert.True(box.HasCommands);
        Assert.Equal(
            new[] { "<<play_bgm \"guesthouse_day\">>", "<<set $affection_ann += 1>>" },
            box.Commands.Split(Environment.NewLine));

        Assert.True(box.IsOption);
        Assert.Equal("열쇠를 건넨다", box.Text);

        // 선택지에는 화자가 없다. 화자 칸은 비어 있어야 한다.
        Assert.Equal(string.Empty, box.Speaker);
    }

    [Fact]
    public void 셋째_박스는_명령_세_줄과_선택지다()
    {
        BoxItem box = BoxesOf("Start")[2];

        Assert.Equal(
            new[]
            {
                "<<set $has_room_key = true>>",
                "<<give_item \"room_key\">>",
                "<<jump Ending>>"
            },
            box.Commands.Split(Environment.NewLine));

        Assert.True(box.IsOption);
        Assert.Equal("잠시 기다린다", box.Text);
    }

    [Fact]
    public void Depth가_0이면_왼쪽_여백이_없다()
    {
        Assert.All(
            BoxesOf("Start"),
            box => Assert.Equal(0, box.Indent.Left));
    }

    [Fact]
    public void Depth가_깊어지면_여백이_늘어난다()
    {
        var line = new StoryLine(
            Speaker: null,
            Text: "깊이 2",
            Hashtags: Array.Empty<string>(),
            FilePath: "Story.yarn",
            Line: 1,
            Column: 9,
            Depth: 2,
            IsOption: false,
            CommandsSincePreviousLine: Array.Empty<string>());

        var box = new BoxItem(line);

        Assert.True(box.Indent.Left > 0);
        Assert.Equal(2 * new BoxItem(line with { Depth = 1 }).Indent.Left, box.Indent.Left);
    }

    [Fact]
    public void 해시태그는_샵을_붙여_이어_쓴다()
    {
        var line = new StoryLine(
            Speaker: "윌로",
            Text: "태그가 둘이다.",
            Hashtags: new[] { "emotion:calm", "wip" },
            FilePath: "Story.yarn",
            Line: 1,
            Column: 1,
            Depth: 0,
            IsOption: false,
            CommandsSincePreviousLine: Array.Empty<string>());

        var box = new BoxItem(line);

        Assert.True(box.HasHashtags);
        Assert.Equal("#emotion:calm #wip", box.Hashtags);
    }
}
