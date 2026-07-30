namespace Vn.Core.Yarn;

/// <summary>
/// Yarn이 돌려준 파일 식별자를 우리 경로 표기로 옮긴다.
///
/// Yarn은 세 가지를 같은 자리에 섞어 내보낸다.
///   - file:// URI
///   - 절대 경로
///   - <c>(External)</c> 처럼 파일이 아닌 의사 이름
///
/// 마지막 것을 <see cref="Path.GetFullPath(string)"/>에 넘기면 현재 작업 디렉터리가 앞에 붙어
/// 실재하지 않는 절대 경로가 만들어진다. 그 뒤로는 진짜 경로와 구분할 방법이 없고,
/// 출력이 "어디서 실행했느냐"에 따라 달라진다. 골든 픽스처가 바로 여기서 깨진다.
///
/// 규칙을 한 곳에 두는 이유는, 진단·노드·점프·심볼 인덱스가 전부 같은 문자열로
/// 서로를 찾기 때문이다. 한쪽만 다르게 정규화하면 조회가 조용히 어긋난다.
/// </summary>
internal static class YarnPaths
{
    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
            uri.IsFile)
        {
            return uri.LocalPath;
        }

        try
        {
            // 이미 절대 경로면 .. 같은 조각만 정리한다.
            // 상대 경로는 실제로 그 파일이 있을 때만 절대 경로로 올린다.
            if (Path.IsPathRooted(value) || File.Exists(value))
            {
                return Path.GetFullPath(value);
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                IOException or
                NotSupportedException or
                UnauthorizedAccessException)
        {
            // 경로로 해석할 수 없는 값이다. 아래에서 원본을 그대로 돌려준다.
        }

        return value;
    }
}
