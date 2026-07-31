using Vn.Core.Analysis;

namespace Vn.Core.Tests;

public class AnalyzerTests
{
    [Fact]
    public void Valid_프로젝트는_진단이_없다()
    {
        var analyzer = new VnProjectAnalyzer();

        AnalysisReport report = analyzer.Analyze(
            "../../../../../samples/Valid/Demo.yarnproject",
            "../../../../../samples/Valid/game.schema.json");

        Assert.False(report.HasErrors);
        Assert.Empty(report.Diagnostics);
        Assert.Equal(2, report.Nodes.Count);
    }
}