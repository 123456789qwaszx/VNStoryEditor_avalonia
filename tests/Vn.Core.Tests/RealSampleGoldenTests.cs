using System.Text;
using Vn.Core.Analysis;
using Vn.Core.Reporting;

namespace Vn.Core.Tests;

/// <summary>
/// samples/Real의 골든 픽스처를 셸을 거치지 않고 검사한다.
///
/// build-and-run.ps1도 같은 것을 비교하지만, 셸을 지나면 콘솔 코드 페이지가 결과에 끼어든다.
/// 실제 분석 결과가 바뀐 것과 셸이 한글을 잘못 읽은 것을 구분하려면 셸 밖에도 기준이 하나 있어야 한다.
/// </summary>
public class RealSampleGoldenTests
{
    private static string RepositoryRoot =>
        Path.GetFullPath("../../../../../");

    [Fact]
    public void Real_샘플_분석_결과가_골든_픽스처와_같다()
    {
        string sampleDirectory = Path.Combine(RepositoryRoot, "samples", "Real");
        string projectPath = Path.Combine(sampleDirectory, "Demo.yarnproject");
        string schemaPath = Path.Combine(sampleDirectory, "game.schema.json");
        string expectedPath = Path.Combine(sampleDirectory, "expected.txt");

        AnalysisReport report = new VnProjectAnalyzer().Analyze(projectPath, schemaPath);
        string actual = string.Join("\n", ListReportFormatter.Format(report));
        string expected = GoldenText.Read(expectedPath);

        string? difference = GoldenText.DescribeFirstDifference(expected, actual);

        Assert.True(
            difference is null,
            $"samples/Real/expected.txt와 분석 결과가 다릅니다.\n{difference}");
    }

    [Fact]
    public void Real_샘플은_오류를_가진_기준선이다()
    {
        string sampleDirectory = Path.Combine(RepositoryRoot, "samples", "Real");

        AnalysisReport report = new VnProjectAnalyzer().Analyze(
            Path.Combine(sampleDirectory, "Demo.yarnproject"),
            Path.Combine(sampleDirectory, "game.schema.json"));

        // build-and-run.ps1은 이 샘플에서 종료 코드 1을 기대한다.
        Assert.True(report.HasErrors);
    }

    [Fact]
    public void BOM_없는_UTF8_한글_골든_파일도_그대로_읽는다()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"VnTool.Golden.{Guid.NewGuid():N}.txt");

        const string content = "line\tgerie1\tStory.yarn\t17\t0\t-\t윌로\t0\t0";

        try
        {
            File.WriteAllText(path, content, new UTF8Encoding(false));

            // BOM이 없어도 ANSI로 넘어가지 않는다. 넘어가면 "윌로"가 "?뚮줈"가 된다.
            Assert.Equal(content, GoldenText.Read(path), StringComparer.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BOM_있는_UTF8_골든_파일은_BOM_때문에_달라지지_않는다()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"VnTool.Golden.{Guid.NewGuid():N}");

        Directory.CreateDirectory(directory);

        const string content = "diag\tVN3001\tError\tStory.yarn\t6\t7\n라루\t윌로";
        string withBom = Path.Combine(directory, "bom.txt");
        string withoutBom = Path.Combine(directory, "no-bom.txt");

        try
        {
            File.WriteAllText(withBom, content, new UTF8Encoding(true));
            File.WriteAllText(withoutBom, content, new UTF8Encoding(false));

            Assert.Equal(GoldenText.Read(withoutBom), GoldenText.Read(withBom), StringComparer.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CRLF와_LF와_끝_줄바꿈_차이는_의미_차이로_보지_않는다()
    {
        Assert.Null(GoldenText.DescribeFirstDifference("가\n나", "가\r\n나\r\n"));
        Assert.Null(GoldenText.DescribeFirstDifference("가\n나\n", "가\n나"));
    }

    [Fact]
    public void 실제_내용이_다르면_첫_번째_다른_줄을_알려준다()
    {
        string? difference = GoldenText.DescribeFirstDifference(
            "가\n나\n다",
            "가\n라\n다");

        Assert.NotNull(difference);
        Assert.Contains("2번째 줄", difference, StringComparison.Ordinal);
        Assert.Contains("나", difference, StringComparison.Ordinal);
        Assert.Contains("라", difference, StringComparison.Ordinal);
    }

    [Fact]
    public void 줄_수가_다르면_어느_줄부터_없는지_알려준다()
    {
        string? missing = GoldenText.DescribeFirstDifference("가\n나\n다", "가\n나");
        string? extra = GoldenText.DescribeFirstDifference("가\n나", "가\n나\n다");

        Assert.NotNull(missing);
        Assert.Contains("3번째", missing, StringComparison.Ordinal);
        Assert.NotNull(extra);
        Assert.Contains("3번째", extra, StringComparison.Ordinal);
    }
}
