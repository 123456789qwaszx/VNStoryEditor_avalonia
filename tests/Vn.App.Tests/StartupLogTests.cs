using System.Text;
using Vn.App.Services;

namespace Vn.App.Tests;

/// <summary>
/// 시작 실패 기록은 마지막 안전망이다.
/// 기록기가 원래 예외를 가리거나 스스로 예외를 던지면 안전망이 아니라 또 하나의 실패가 된다.
/// </summary>
public class StartupLogTests
{
    private static string TempFile() => Path.Combine(
        Path.GetTempPath(),
        $"VnTool.StartupLog.{Guid.NewGuid():N}",
        "startup-error.log");

    [Fact]
    public void 예외를_적고_기록한_경로를_돌려준다()
    {
        string path = TempFile();

        try
        {
            string? written = StartupLog.TryWriteTo(
                path,
                "주 창 만들기",
                new InvalidOperationException("창을 만들지 못했습니다"));

            Assert.Equal(path, written);
            Assert.True(File.Exists(path));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void 원래_예외의_형과_메시지와_스택을_그대로_남긴다()
    {
        string path = TempFile();

        try
        {
            Exception captured = Caught();
            StartupLog.TryWriteTo(path, "주 창 만들기", captured);

            string log = File.ReadAllText(path, new UTF8Encoding(false));

            Assert.Contains(nameof(NullReferenceException), log, StringComparison.Ordinal);
            Assert.Contains("원래 메시지", log, StringComparison.Ordinal);
            Assert.Contains(nameof(Caught), log, StringComparison.Ordinal);
            Assert.Contains("주 창 만들기", log, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void 안쪽_예외까지_남겨_원인을_숨기지_않는다()
    {
        string path = TempFile();

        try
        {
            var exception = new InvalidOperationException(
                "바깥 메시지",
                new FileNotFoundException("안쪽 원인"));

            StartupLog.TryWriteTo(path, "최근 프로젝트 복원", exception);

            string log = File.ReadAllText(path, new UTF8Encoding(false));

            Assert.Contains("바깥 메시지", log, StringComparison.Ordinal);
            Assert.Contains("안쪽 원인", log, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void 여러_번_기록해도_앞선_기록을_지우지_않는다()
    {
        string path = TempFile();

        try
        {
            StartupLog.TryWriteTo(path, "첫 번째", new InvalidOperationException("하나"));
            StartupLog.TryWriteTo(path, "두 번째", new InvalidOperationException("둘"));

            string log = File.ReadAllText(path, new UTF8Encoding(false));

            Assert.Contains("하나", log, StringComparison.Ordinal);
            Assert.Contains("둘", log, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(path);
        }
    }

    /// <summary>
    /// 기록에 실패하는 상황에서도 던지지 않는다.
    /// 여기서 예외가 나가면 원래 예외 대신 기록기의 예외가 보고되어 원인이 뒤바뀐다.
    /// </summary>
    [Fact]
    public void 기록하지_못해도_예외를_던지지_않는다()
    {
        // 파일 이름 자리에 이미 파일이 있는 경로. 디렉터리를 만들 수 없다.
        string blocker = Path.Combine(Path.GetTempPath(), $"VnTool.Blocker.{Guid.NewGuid():N}");
        File.WriteAllText(blocker, "not a directory");

        try
        {
            string? written = StartupLog.TryWriteTo(
                Path.Combine(blocker, "sub", "startup-error.log"),
                "주 창 만들기",
                new InvalidOperationException("원래 예외"));

            Assert.Null(written);
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    [Fact]
    public void 로그_경로는_사용자_프로필_아래의_고정된_자리다()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(local, StartupLog.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VnTool", StartupLog.FilePath, StringComparison.Ordinal);

        // 작가에게 보여줄 문장에는 로그를 찾아갈 경로가 들어 있어야 한다.
        Assert.Contains(StartupLog.FilePath, StartupLog.WriterMessage("VnTool을 시작하는"), StringComparison.Ordinal);
    }

    private static Exception Caught()
    {
        try
        {
            throw new NullReferenceException("원래 메시지");
        }
        catch (NullReferenceException exception)
        {
            return exception;
        }
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
            // 임시 폴더 정리 실패로 테스트를 떨어뜨리지 않는다.
        }
    }
}
