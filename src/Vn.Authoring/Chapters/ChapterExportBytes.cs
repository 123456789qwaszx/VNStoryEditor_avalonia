using System.Security.Cryptography;
using System.Text;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 서버에 건네는 진행 JSON의 바이트 계약. 서버는 JSON 의미가 아니라 요청 원본 바이트의
/// SHA-256을 버전 키로 쓰므로 인코딩·BOM·개행은 여기 한 곳에서 고정한다.
/// </summary>
public static class ChapterExportBytes
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    public static string Normalize(string json) => json.ReplaceLineEndings("\n");

    public static byte[] Encode(string json) => Utf8WithoutBom.GetBytes(Normalize(json));

    public static string Sha256(string json) =>
        Convert.ToHexString(SHA256.HashData(Encode(json))).ToLowerInvariant();
}
