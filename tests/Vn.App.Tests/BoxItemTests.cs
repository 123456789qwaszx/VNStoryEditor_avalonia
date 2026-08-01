using Vn.App.Views;
using Vn.Core;
using Vn.Core.Analysis;
using Vn.Core.Story;

namespace Vn.App.Tests;

/// <summary>
/// 박스 뷰가 화면에 무엇을 그릴지는 이 뷰 모델들이 정한다.
/// 클릭과 접기는 자동으로 확인할 수 없지만, 무엇이 어느 카드에 들어가는지는 여기서 고정할 수 있다.
/// </summary>
public class BoxItemTests
{
    private static IReadOnlyList<object> ValidStartBody()
    {
        AnalysisReport report = new VnProjectAnalyzer().Analyze(
            "../../../../../samples/Valid/Demo.yarnproject",
            "../../../../../samples/Valid/game.schema.json");

        StoryNode start = Assert.Single(report.Nodes, node => node.Title == "Start");

        return BoxListView.BuildChildren(start.Body, File.ReadAllText(start.FilePath));
    }

    private static BlockItem OptionBlock()
    {
        return Assert.IsType<BlockItem>(
            Assert.Single(ValidStartBody(), item => item is BlockItem));
    }

    [Fact]
    public void 최상위는_대사_박스_하나와_선택지_블록_하나다()
    {
        IReadOnlyList<object> body = ValidStartBody();

        Assert.Collection(
            body,
            item => Assert.Equal("어서 오세요.", Assert.IsType<BoxItem>(item).Text),
            item => Assert.Equal("선택지", Assert.IsType<BlockItem>(item).Title));
    }

    [Fact]
    public void 선택지_두_개가_각각_카드가_된다()
    {
        BlockItem block = OptionBlock();

        Assert.Equal(2, block.Branches.Count);

        Assert.Equal(
            new[] { "열쇠를 건넨다", "잠시 기다린다" },
            block.Branches.Select(branch => branch.Label));

        // 선택지와 조건은 같은 위젯으로 그리고 표시만 다르다.
        Assert.All(block.Branches, branch => Assert.Equal("→", branch.Marker));
    }

    /// <summary>
    /// 이 커밋의 핵심. 평평한 목록에서는 8~10행 명령이 11행 선택지 박스에 붙어 보였다.
    /// 트리에서는 그것이 속한 갈래로 간다.
    /// </summary>
    [Fact]
    public void 갈래_안의_명령이_다음_선택지_카드에_붙지_않는다()
    {
        BlockItem block = OptionBlock();

        BranchItem first = block.Branches[0];
        BranchItem second = block.Branches[1];

        Assert.True(first.HasCommands);
        Assert.Equal(
            new[] { "<<set $has_room_key = true>>", "<<give_item \"room_key\">>" },
            first.Commands.Split(Environment.NewLine));

        // 둘째 카드는 비어 있다. 예전에는 여기에 명령 세 개가 붙어 있었다.
        Assert.False(second.HasCommands);
    }

    [Fact]
    public void 갈래의_목적지를_카드_머리에_보여준다()
    {
        BlockItem block = OptionBlock();

        Assert.All(block.Branches, branch => Assert.True(branch.HasDestination));
        Assert.All(block.Branches, branch => Assert.Equal("→ Ending", branch.DestinationText));
    }

    [Fact]
    public void 대사_없는_갈래는_자식이_없다()
    {
        Assert.All(OptionBlock().Branches, branch => Assert.Empty(branch.Children));
    }

    [Fact]
    public void 중첩된_블록은_갈래_안의_항목이_된다()
    {
        const string yarn = """
            title: T
            ---
            -> 첫째 선택지
                <<if $favor >= 5>>
                라루: 안쪽.
                <<endif>>
            -> 둘째 선택지
                윌로: 둘째.
            ===
            """;

        StoryNode node = AnalyzeSource(yarn);

        var options = Assert.IsType<BlockItem>(
            Assert.Single(BoxListView.BuildChildren(node.Body, yarn)));

        var nested = Assert.IsType<BlockItem>(
            Assert.Single(options.Branches[0].Children));

        Assert.Equal("조건", nested.Title);
        Assert.Equal("?", Assert.Single(nested.Branches).Marker);

        // 중첩된 블록은 왼쪽으로 들여쓴다.
        Assert.True(nested.Indent.Left > 0);
    }

    [Fact]
    public void 조건_갈래에는_목적지가_없다()
    {
        const string yarn = """
            title: T
            ---
            <<if $favor >= 5>>
            라루: 참.
            <<endif>>
            ===
            """;

        StoryNode node = AnalyzeSource(yarn);

        var block = Assert.IsType<BlockItem>(
            Assert.Single(BoxListView.BuildChildren(node.Body, yarn)));

        Assert.False(Assert.Single(block.Branches).HasDestination);
    }

    private static StoryNode AnalyzeSource(string yarn)
    {
        string workDirectory = Path.Combine(
            Path.GetTempPath(),
            $"VnTool.BoxItemTests.{Guid.NewGuid():N}");

        Directory.CreateDirectory(workDirectory);

        try
        {
            File.WriteAllText(Path.Combine(workDirectory, "Story.yarn"), yarn);

            File.WriteAllText(
                Path.Combine(workDirectory, "Demo.yarnproject"),
                """
                {
                  "projectFileVersion": 3,
                  "baseLanguage": "ko",
                  "sourceFiles": [ "**/*.yarn" ],
                  "excludeFiles": []
                }
                """);

            string schemaPath = Path.Combine(workDirectory, "game.schema.json");

            File.WriteAllText(
                schemaPath,
                """{ "schemaVersion": 1, "variables": [], "commands": [] }""");

            return new VnProjectAnalyzer()
                .Analyze(Path.Combine(workDirectory, "Demo.yarnproject"), schemaPath)
                .Nodes
                .Single();
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
