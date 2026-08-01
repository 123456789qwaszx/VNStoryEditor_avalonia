using System.Text;

namespace Vn.Core.Tests;

/// <summary>
/// 골든 픽스처 비교의 인코딩·줄바꿈 정책을 한곳에 모은다.
///
/// 정책은 두 가지다.
/// 1. 파일은 항상 UTF-8로 읽는다. BOM이 있으면 떼고, 없어도 ANSI로 넘어가지 않는다.
///    Windows PowerShell 5.1의 <c>Get-Content</c>는 BOM 없는 파일을 시스템 ANSI(한국어는 949)로
///    읽어 한글을 망가뜨린다. 비교를 셸에 맡기면 실제 분석 결과와 무관하게 결과가 달라진다.
/// 2. 줄바꿈은 LF로 정규화하고 파일 끝의 줄바꿈 유무는 무시한다.
///    CRLF/LF와 마지막 개행은 의미 차이가 아니다.
/// </summary>
internal static class GoldenText
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static string Read(string path)
    {
        // File.ReadAllText는 BOM이 있으면 그것을 우선 인식하고, 없으면 넘긴 인코딩을 쓴다.
        // 따라서 BOM 있는 파일과 없는 파일이 같은 문자열이 된다.
        return Normalize(File.ReadAllText(path, Utf8));
    }

    public static string Normalize(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .TrimEnd('\n');
    }

    public static IReadOnlyList<string> Lines(string text)
    {
        string normalized = Normalize(text);

        return normalized.Length == 0
            ? Array.Empty<string>()
            : normalized.Split('\n');
    }

    /// <summary>
    /// 첫 번째로 다른 줄을 사람이 읽을 수 있게 만든다. 같으면 null.
    /// "어딘가 다르다"만 알려주는 비교는 원인을 못 찾게 만든다.
    /// </summary>
    public static string? DescribeFirstDifference(string expected, string actual)
    {
        IReadOnlyList<string> expectedLines = Lines(expected);
        IReadOnlyList<string> actualLines = Lines(actual);
        int shared = Math.Min(expectedLines.Count, actualLines.Count);

        for (int index = 0; index < shared; index++)
        {
            if (!string.Equals(expectedLines[index], actualLines[index], StringComparison.Ordinal))
            {
                return $"{index + 1}번째 줄이 다릅니다.\n" +
                    $"  expected: {Visible(expectedLines[index])}\n" +
                    $"  actual  : {Visible(actualLines[index])}";
            }
        }

        if (expectedLines.Count == actualLines.Count)
        {
            return null;
        }

        return expectedLines.Count > actualLines.Count
            ? $"actual에 줄이 모자랍니다. expected {expectedLines.Count}줄, actual {actualLines.Count}줄.\n" +
                $"  첫 누락 줄({actualLines.Count + 1}번째): {Visible(expectedLines[actualLines.Count])}"
            : $"actual에 줄이 더 있습니다. expected {expectedLines.Count}줄, actual {actualLines.Count}줄.\n" +
                $"  첫 추가 줄({expectedLines.Count + 1}번째): {Visible(actualLines[expectedLines.Count])}";
    }

    /// <summary>탭과 보이지 않는 차이를 눈으로 구분할 수 있게 바꾼다.</summary>
    private static string Visible(string line)
    {
        return line.Replace("\t", "»", StringComparison.Ordinal);
    }
}
