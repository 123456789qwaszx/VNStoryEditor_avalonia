using Vn.Core.Analysis;
using Vn.Core.Diagnostics;

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

    [Fact]
    public void Invalid_프로젝트는_알_수_없는_변수를_잡는다()
    {
        var analyzer = new VnProjectAnalyzer();

        AnalysisReport report = analyzer.Analyze(
            "../../../../../samples/Invalid/Demo.yarnproject",
            "../../../../../samples/Invalid/game.schema.json");

        Assert.True(report.HasErrors);

        VnDiagnostic diagnostic = Assert.Single(
            report.Diagnostics,
            d => d.Code == VnDiagnosticCodes.UnknownVariable);

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.EndsWith("Broken.yarn", diagnostic.FilePath);
        Assert.Equal(5, diagnostic.Line);
        Assert.Equal(7, diagnostic.Column);
    }
}