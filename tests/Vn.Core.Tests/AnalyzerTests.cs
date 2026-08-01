using Vn.Core.Analysis;
using Vn.Core.Diagnostics;

namespace Vn.Core.Tests;

public class AnalyzerTests
{
    [Fact]
    public void Valid_프로젝트는_오류가_없다()
    {
        var analyzer = new VnProjectAnalyzer();

        AnalysisReport report = analyzer.Analyze(
            "../../../../../samples/Valid/Demo.yarnproject",
            "../../../../../samples/Valid/game.schema.json");

        Assert.False(report.HasErrors);
        Assert.Equal(2, report.Nodes.Count);

        // 이 샘플의 선택지 갈래에는 대사가 없다. 작성 규약 위반이지만 오류는 아니다.
        // 규약 위반은 "틀린 것"이 아니라 "이 툴이 편하게 다루기 어려운 것"이라 Warning이다.
        Assert.All(
            report.Diagnostics,
            diagnostic => Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity));

        Assert.All(
            report.Diagnostics,
            diagnostic => Assert.StartsWith("VN5", diagnostic.Code, StringComparison.Ordinal));
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