using System.Text;

namespace Vn.App.Services;

/// <summary>
/// 시작 실패를 사용자가 찾을 수 있는 자리에 남긴다.
///
/// 이 앱은 데스크톱 앱(WinExe)이라 콘솔이 없다. 시작하다 예외가 나면 창도 뜨지 않고
/// 아무 메시지도 없이 프로세스만 사라진다. 작가는 무엇이 잘못됐는지 알 방법이 없다.
/// 그래서 예외는 반드시 파일로 남긴다. 삼키지 않는다.
///
/// 기록 자체가 실패해도(디스크 가득, 권한 없음) 원래 예외를 가리면 안 된다.
/// 그래서 이 클래스의 모든 메서드는 예외를 던지지 않고, 실패하면 null을 돌려준다.
/// </summary>
internal static class StartupLog
{
    private const int MaxBytes = 256 * 1024;

    /// <summary>
    /// 로그 폴더. 사용자 프로필 안이라 관리자 권한 없이 쓸 수 있고, 실행 위치가 바뀌어도 같은 자리다.
    /// </summary>
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VnTool",
        "logs");

    public static string FilePath { get; } = Path.Combine(Directory, "startup-error.log");

    /// <summary>작가에게 보여줄 한 문장. 개발자용 상세 내용은 파일에 있다.</summary>
    public static string WriterMessage(string context) =>
        $"{context} 중에 문제가 생겼습니다. 자세한 내용은 다음 파일에 저장했습니다.\n{FilePath}";

    /// <summary>
    /// 예외를 기록하고 로그 경로를 돌려준다. 기록하지 못하면 null.
    /// 어떤 경우에도 예외를 던지지 않는다 — 기록기가 원래 예외를 가리면 안 된다.
    /// </summary>
    public static string? TryWrite(string context, Exception exception)
    {
        return TryWriteTo(FilePath, context, exception);
    }

    internal static string? TryWriteTo(string path, string context, Exception exception)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            TrimIfTooLarge(path);
            File.AppendAllText(path, Compose(context, exception), new UTF8Encoding(false));
            return path;
        }
        catch (Exception)
        {
            // 기록 실패는 원래 예외보다 덜 중요하다. 조용히 포기하고 호출자가 계속 처리하게 둔다.
            return null;
        }
    }

    internal static string Compose(string context, Exception exception)
    {
        var builder = new StringBuilder();

        builder.Append("---- ")
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"))
            .Append(" · ")
            .AppendLine(context);

        // ToString()은 내부 예외와 스택까지 모두 낸다. 요약하면 원인이 사라진다.
        builder.AppendLine(exception.ToString());
        builder.AppendLine();
        return builder.ToString();
    }

    /// <summary>로그가 무한히 자라 디스크를 먹지 않게 한다. 실패하면 그냥 이어 쓴다.</summary>
    private static void TrimIfTooLarge(string path)
    {
        try
        {
            var file = new FileInfo(path);

            if (file.Exists && file.Length > MaxBytes)
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // 정리에 실패해도 기록은 계속한다.
        }
    }
}
