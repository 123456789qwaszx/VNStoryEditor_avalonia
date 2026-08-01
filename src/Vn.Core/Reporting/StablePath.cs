namespace Vn.Core.Reporting;

/// <summary>
/// 머신·OS에 상관없이 같은 문자열이 나오도록 경로를 다듬는다.
/// 구분자를 <c>/</c>로 통일하므로 픽스처가 Windows와 mac 양쪽에서 그대로 통과한다.
/// </summary>
public static class StablePath
{
    /// <summary>
    /// 경로 기준은 프로젝트 파일이 있는 폴더다.
    /// 현재 작업 디렉터리를 기준으로 삼으면 어디서 실행했느냐에 따라 출력이 달라지고,
    /// 골든 픽스처가 "항상 같은 위치에서 돌린다"는 전제에 매달리게 된다.
    /// </summary>
    public static string RootFor(string projectPath)
    {
        return Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory;
    }

    public static string ToStable(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "-";
        }

        // Yarn은 파일에 매이지 않은 진단에 "(External)" 같은 의사 경로를 쓴다.
        // 실제 경로가 아닌 것에 GetRelativePath를 걸면 "../../(External)" 처럼
        // 프로젝트 폴더가 어느 깊이에 있느냐에 따라 달라지는 문자열이 나온다.
        // 픽스처가 폴더 깊이에 매달리게 되므로, 절대 경로가 아니면 그대로 둔다.
        if (!Path.IsPathRooted(path))
        {
            return path.Replace('\\', '/');
        }

        string relative;

        try
        {
            relative = Path.GetRelativePath(root, path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            relative = path;
        }

        return relative.Replace('\\', '/');
    }
}
