using System.Text;
using Vn.App.Services;

namespace Vn.App.Tests;

/// <summary>
/// 최근 프로젝트 기억은 편의 기능이다.
/// 설정 파일이 없거나 깨져 있다는 이유로 앱이 시작하지 못하면 안 된다.
/// </summary>
public class AppSettingsServiceTests
{
    private static string TempSettings() => Path.Combine(
        Path.GetTempPath(),
        $"VnTool.Settings.{Guid.NewGuid():N}",
        "settings.json");

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private static void Cleanup(string path)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);

            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void 설정_파일이_없으면_기본값으로_시작한다()
    {
        string path = TempSettings();

        Assert.Null(AppSettingsService.LoadRecentProject(path));
        Assert.Null(AppSettingsService.Load(path).RecentProject);
        Assert.Empty(AppSettingsService.Load(path).RecentNodes);
    }

    [Theory]
    [InlineData("{ 이건 JSON이 아니다")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{ \"RecentProject\": 12345 }")]
    [InlineData("{ \"RecentNodes\": \"문자열이면 안 되는 자리\" }")]
    public void 손상된_설정_파일이어도_던지지_않고_기본값이_된다(string content)
    {
        string path = TempSettings();
        Write(path, content);

        try
        {
            Assert.Null(AppSettingsService.LoadRecentProject(path));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void 잘린_UTF8_바이트가_들어_있어도_시작을_막지_않는다()
    {
        string path = TempSettings();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[] { 0x7B, 0x22, 0xED, 0x8A, 0xB9, 0xFF, 0xFE });

        try
        {
            Assert.Null(AppSettingsService.LoadRecentProject(path));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void 사라진_프로젝트를_가리키면_복원하지_않는다()
    {
        string path = TempSettings();
        string missing = Path.Combine(Path.GetTempPath(), $"VnTool.Gone.{Guid.NewGuid():N}.yarnproject");

        Write(path, $"{{ \"RecentProject\": {System.Text.Json.JsonSerializer.Serialize(missing)} }}");

        try
        {
            Assert.Null(AppSettingsService.LoadRecentProject(path));
        }
        finally
        {
            Cleanup(path);
        }
    }

    /// <summary>
    /// 설정에 형식부터 깨진 경로가 남아 있어도 예외가 밖으로 나가면 안 된다.
    /// 이 예외는 창이 열린 직후의 async void 핸들러에서 나므로 앱 전체를 죽인다.
    /// </summary>
    [Fact]
    public void 경로_형식이_깨져_있어도_던지지_않는다()
    {
        string path = TempSettings();
        Write(path, "{ \"RecentProject\": \"C:\\\\<>|:*?\\\\없는것.yarnproject\" }");

        try
        {
            Assert.Null(AppSettingsService.LoadRecentProject(path));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void 정상적인_설정은_그대로_읽는다()
    {
        string path = TempSettings();
        string project = Path.GetFullPath("../../../../../samples/Valid/Demo.yarnproject");

        Write(path, $"{{ \"RecentProject\": {System.Text.Json.JsonSerializer.Serialize(project)} }}");

        try
        {
            Assert.Equal(project, AppSettingsService.LoadRecentProject(path));
        }
        finally
        {
            Cleanup(path);
        }
    }
}
