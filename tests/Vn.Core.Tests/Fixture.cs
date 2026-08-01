using System.Text;
using Vn.Core.Analysis;
using Vn.Core.Story;

namespace Vn.Core.Tests;

/// <summary>
/// 임시 폴더에 .yarn 한 개짜리 프로젝트를 만들어 분석한다.
/// samples/는 회귀 기준선이지 기능 시연 자료가 아니므로, 새 형태는 여기서 만든다.
/// 스키마는 빈 껍데기라 VN3001/VN3002가 쏟아지지만 구조만 볼 때는 상관없다.
/// </summary>
internal static class Fixture
{
    public static AnalysisReport Analyze(string yarn)
    {
        string workDirectory = Path.Combine(
            Path.GetTempPath(),
            $"VnTool.Fixture.{Guid.NewGuid():N}");

        Directory.CreateDirectory(workDirectory);

        try
        {
            var utf8 = new UTF8Encoding(false);

            File.WriteAllText(Path.Combine(workDirectory, "Story.yarn"), yarn, utf8);

            File.WriteAllText(
                Path.Combine(workDirectory, "Demo.yarnproject"),
                """
                {
                  "projectFileVersion": 3,
                  "baseLanguage": "ko",
                  "sourceFiles": [ "**/*.yarn" ],
                  "excludeFiles": []
                }
                """,
                utf8);

            string schemaPath = Path.Combine(workDirectory, "game.schema.json");

            File.WriteAllText(
                schemaPath,
                """{ "schemaVersion": 1, "variables": [], "commands": [] }""",
                utf8);

            return new VnProjectAnalyzer().Analyze(
                Path.Combine(workDirectory, "Demo.yarnproject"),
                schemaPath);
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch (IOException)
            {
                // 임시 폴더 정리 실패로 테스트를 떨어뜨리지 않는다.
            }
        }
    }

    public static StoryNode Node(string yarn)
    {
        return Analyze(yarn).Nodes.Single();
    }
}
