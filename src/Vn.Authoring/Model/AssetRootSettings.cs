namespace Vn.Authoring.Model;

/// <summary>
/// 프리뷰가 읽는 에셋 폴더 두 곳 — 배경 폴더와 초상화 폴더(매니페스트 포함).
///
/// 프로젝트 manifest에 상대 경로로 저장한다. 프로젝트 폴더를 통째로 옮겨도
/// 에셋이 같이 움직이면 설정이 그대로 살아 있게 하기 위해서다. 에셋 폴더는 보통
/// 런타임 저장소처럼 프로젝트 밖에 있으므로 <c>..</c>를 허용한다 — StoryFile 경로와
/// 달리 여기는 읽기 전용이라 프로젝트 밖을 가리켜도 위험하지 않다.
///
/// 미설정(null)이어도 저작은 계속된다. 프리뷰만 플레이스홀더가 될 뿐이다.
/// </summary>
public sealed class AssetRootSettings
{
    /// <summary>배경 PNG 폴더. 파일명이 곧 spriteKey다(매니페스트 없음).</summary>
    public string? BackgroundsPath { get; set; }

    /// <summary><c>portraits.manifest.json</c>과 PNG가 있는 초상화 폴더.</summary>
    public string? PortraitsPath { get; set; }

    public bool IsEmpty => BackgroundsPath is null && PortraitsPath is null;

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

    public AssetRootSettings Clone() => new()
    {
        BackgroundsPath = BackgroundsPath,
        PortraitsPath = PortraitsPath
    };
}
