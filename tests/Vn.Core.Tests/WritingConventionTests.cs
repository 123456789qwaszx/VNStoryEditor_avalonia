using Vn.Core.Analysis;
using Vn.Core.Diagnostics;

namespace Vn.Core.Tests;

/// <summary>
/// 작성 규약은 전부 Warning이다. 파서는 관대하게 읽고 알리기만 한다.
/// 규약을 못 지킨 파일이 열리지 않으면 작가는 자기 원고를 볼 수 없게 된다.
/// </summary>
public class WritingConventionTests
{
    private static IReadOnlyList<VnDiagnostic> Of(string yarn, string code)
    {
        return Fixture.Analyze(yarn)
            .Diagnostics
            .Where(diagnostic => diagnostic.Code == code)
            .ToList();
    }

    [Fact]
    public void VN5001_선택지_갈래에_대사가_없으면_알린다()
    {
        IReadOnlyList<VnDiagnostic> found = Of("""
            title: T
            ---
            -> 대사 없는 선택지
                <<set $anger = 1>>
            -> 대사 있는 선택지
                라루: 있다.
            ===
            """, VnDiagnosticCodes.OptionBranchHasNoLine);

        VnDiagnostic diagnostic = Assert.Single(found);

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(3, diagnostic.Line);
        Assert.Contains("대사 없는 선택지", diagnostic.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 조건문으로 감싼 대사도 "고르면 나오는 말"이다. 그것까지 없다고 하면 거짓말이 된다.
    /// </summary>
    [Fact]
    public void 조건_안의_대사도_대사로_친다()
    {
        Assert.Empty(Of("""
            title: T
            ---
            -> 조건 안에만 대사가 있는 선택지
                <<if $favor >= 5>>
                    라루: 있다.
                <<endif>>
            -> 다른 선택지
                윌로: 있다.
            ===
            """, VnDiagnosticCodes.OptionBranchHasNoLine));
    }

    [Fact]
    public void VN5002_목적지가_없으면_알린다()
    {
        IReadOnlyList<VnDiagnostic> found = Of("""
            title: T
            ---
            -> 점프 없는 선택지
                라루: 흘러간다.
            -> 점프 있는 선택지
                라루: 간다.
                <<jump Other>>
            ===

            title: Other
            ---
            윌로: 저쪽.
            ===
            """, VnDiagnosticCodes.OptionBranchHasNoDestination);

        VnDiagnostic diagnostic = Assert.Single(found);

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(3, diagnostic.Line);

        // 통과가 의도인 경우도 있으므로 무시해도 된다고 말해 준다.
        Assert.Contains("무시하세요", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VN5003_점프가_여러_개면_알린다()
    {
        IReadOnlyList<VnDiagnostic> found = Of("""
            title: T
            ---
            -> 점프가 둘인 선택지
                라루: 간다.
                <<jump Other>>
                <<jump Other>>
            ===

            title: Other
            ---
            윌로: 저쪽.
            ===
            """, VnDiagnosticCodes.OptionBranchHasManyJumps);

        VnDiagnostic diagnostic = Assert.Single(found);

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("2개", diagnostic.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 점프가 여럿이면 목적지도 당연히 없다. 같은 자리에 두 번 말하지 않는다.
    /// </summary>
    [Fact]
    public void 점프가_여러_개일_때_목적지_경고를_겹쳐_내지_않는다()
    {
        const string yarn = """
            title: T
            ---
            -> 점프가 둘인 선택지
                라루: 간다.
                <<jump Other>>
                <<jump Other>>
            ===

            title: Other
            ---
            윌로: 저쪽.
            ===
            """;

        Assert.Empty(Of(yarn, VnDiagnosticCodes.OptionBranchHasNoDestination));
    }

    /// <summary>조건 갈래에는 목적지 개념이 없다. 규약 검사도 하지 않는다.</summary>
    [Fact]
    public void 조건_갈래는_규약_검사를_하지_않는다()
    {
        AnalysisReport report = Fixture.Analyze("""
            title: T
            ---
            <<if $favor >= 5>>
                라루: 참.
            <<else>>
                윌로: 거짓.
            <<endif>>
            ===
            """);

        Assert.DoesNotContain(
            report.Diagnostics,
            diagnostic => diagnostic.Code.StartsWith("VN5", StringComparison.Ordinal));
    }

    [Fact]
    public void 규약_위반은_종료_코드를_바꾸지_않는다()
    {
        // 빈 스키마에서는 변수와 명령이 전부 VN3001/VN3002 오류가 되므로
        // 규약 위반만 남기려면 갈래를 비워 두어야 한다.
        AnalysisReport report = Fixture.Analyze("""
            title: T
            ---
            -> 대사도 목적지도 없는 선택지
            -> 다른 선택지
                라루: 있다.
            ===
            """);

        Assert.NotEmpty(report.Diagnostics);
        Assert.False(report.HasErrors);
    }
}
