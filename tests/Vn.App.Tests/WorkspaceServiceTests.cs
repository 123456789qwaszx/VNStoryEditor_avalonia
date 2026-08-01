using System.Text;
using Vn.App.Services;

namespace Vn.App.Tests;

/// <summary>
/// 그래프 좌표는 편집기 전용 데이터다. <c>.yarn</c>에 넣을 자리가 없어 따로 둔다.
///
/// 이 파일이 없거나 깨졌다고 앱이 죽으면 안 된다. 원고가 아니라 편의를 위한 것이므로
/// 못 읽으면 자동 배치로 조용히 돌아간다.
/// </summary>
public class WorkspaceServiceTests
{
    private static void InDirectory(Action<string> work)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"VnTool.WorkspaceServiceTests.{Guid.NewGuid():N}");

        Directory.CreateDirectory(directory);

        try
        {
            work(Path.Combine(directory, "Demo.yarnproject"));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void 좌표를_저장하고_다시_읽는다()
    {
        InDirectory(projectPath =>
        {
            WorkspaceService.SavePositions(projectPath, new Dictionary<string, NodePosition>
            {
                ["gerie1"] = new() { X = 120, Y = 240 },
                ["gerie1_good_end"] = new() { X = 360, Y = 80 }
            });

            var loaded = WorkspaceService.LoadPositions(projectPath);

            Assert.Equal(2, loaded.Count);
            Assert.Equal(120, loaded["gerie1"].X);
            Assert.Equal(240, loaded["gerie1"].Y);
            Assert.Equal(360, loaded["gerie1_good_end"].X);
        });
    }

    [Fact]
    public void 파일은_프로젝트_폴더_옆에_생긴다()
    {
        InDirectory(projectPath =>
        {
            WorkspaceService.SavePositions(
                projectPath,
                new Dictionary<string, NodePosition> { ["T"] = new() { X = 1, Y = 2 } });

            string expected = Path.Combine(
                Path.GetDirectoryName(projectPath)!,
                WorkspaceService.FileName);

            Assert.True(File.Exists(expected));
        });
    }

    [Fact]
    public void 파일이_없으면_빈_결과다()
    {
        InDirectory(projectPath =>
            Assert.Empty(WorkspaceService.LoadPositions(projectPath)));
    }

    [Fact]
    public void 깨진_파일이면_빈_결과이고_예외가_나지_않는다()
    {
        InDirectory(projectPath =>
        {
            File.WriteAllText(
                WorkspaceService.PathFor(projectPath),
                "{ 이건 JSON이 아니다",
                new UTF8Encoding(false));

            Assert.Empty(WorkspaceService.LoadPositions(projectPath));
        });
    }

    /// <summary>좌표가 숫자가 아닌 항목만 버리고 나머지는 살린다.</summary>
    [Fact]
    public void 값이_이상한_항목만_버린다()
    {
        InDirectory(projectPath =>
        {
            File.WriteAllText(
                WorkspaceService.PathFor(projectPath),
                """
                {
                  "nodePositions": {
                    "정상": { "x": 40, "y": 80 },
                    "": { "x": 1, "y": 2 }
                  }
                }
                """,
                new UTF8Encoding(false));

            var loaded = WorkspaceService.LoadPositions(projectPath);

            Assert.Equal(40, Assert.Single(loaded).Value.X);
        });
    }

    /// <summary>좌표 말고는 아무것도 담지 않는다. 분석 결과는 원본에서 다시 계산된다.</summary>
    [Fact]
    public void 좌표_말고는_저장하지_않는다()
    {
        InDirectory(projectPath =>
        {
            WorkspaceService.SavePositions(
                projectPath,
                new Dictionary<string, NodePosition> { ["T"] = new() { X = 1, Y = 2 } });

            string json = File.ReadAllText(WorkspaceService.PathFor(projectPath));

            Assert.Contains("nodePositions", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("diagnostic", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("line", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("recent", json, StringComparison.OrdinalIgnoreCase);
        });
    }
}
