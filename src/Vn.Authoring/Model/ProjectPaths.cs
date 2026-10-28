namespace Vn.Authoring.Model;

/// <summary>
/// 프로젝트 manifest 기준의 경로 규약 — 저장 형식 정규화와 절대 경로 해석.
/// 라이브 출력 폴더(<see cref="StoryProject.OutputPath"/>)처럼 manifest에 상대 경로로
/// 남는 값이 쓴다. 프로젝트 폴더를 통째로 옮겨도 설정이 그대로 살아 있게 하기 위해서다.
/// </summary>
public static class ProjectPaths
{
    /// <summary>저장 형식으로 정규화한다 — 슬래시 통일, 빈 문자열은 미설정.</summary>
    public static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return path.Replace('\\', '/').Trim().TrimEnd('/');
    }

    /// <summary>프로젝트 manifest 경로를 기준으로 절대 경로를 만든다. 미설정이면 null.</summary>
    public static string? ResolveFrom(string projectManifestPath, string? relativeOrAbsolutePath)
    {
        if (relativeOrAbsolutePath is null)
        {
            return null;
        }

        if (Path.IsPathRooted(relativeOrAbsolutePath))
        {
            return Path.GetFullPath(relativeOrAbsolutePath);
        }

        string rootDirectory = Path.GetDirectoryName(Path.GetFullPath(projectManifestPath))
            ?? Environment.CurrentDirectory;

        return Path.GetFullPath(Path.Combine(rootDirectory, relativeOrAbsolutePath));
    }
}
