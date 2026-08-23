using System.Security.Cryptography;
using System.Text;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 워크북 폴더들의 <b>지금 모습</b>을 한 줄로. 감시자가 깨울 때 지난번과 견주어
/// "정말 바뀌었나"를 가린다.
///
/// <b>왜 화면 밖으로 나왔나 (2026-08-24)</b> — 이것은 파일의 일이지 화면의 일이 아니고,
/// 무엇보다 <b>화면 없이 시험할 수 있어야 했다.</b> 아래 결함이 뷰 안에 숨어 있었기
/// 때문이다.
///
/// ⛔ <b>여기서 가장 하기 쉬운 실수: 못 읽은 파일을 상수로 적는 것.</b>
///
/// 소유자 보고 (2026-08-24) — "엑셀에서 대사를 추가했는데 연출그래프에서는 반영이 안 돼.
/// 챕터그래프의 읽기전용 대사목록에서는 반영이 되는데… 엑셀을 닫으니까 그제서야 반영이 된다."
///
/// 그 정체가 이것이었다. 예전 판은 <c>File.ReadAllBytes</c>로 해시했는데, 그것은
/// <c>FileShare.Read</c>로 연다 — <b>엑셀이 쥐고 있는 파일에서는 IOException이 난다</b>
/// (엑셀은 쓰기 권한으로 열고 있으므로). 그때 지문에 <c>'?'</c>를 적었고, <c>'?'</c>는
/// <b>상수라 다음 저장에도 그대로였다.</b> 그래서:
///
/// <code>
/// 엑셀이 연다 → 저장 → 지문 '?' (바뀜) → 반영됨 → 지문에 '?'가 박힌다
///             → 또 저장 → 지문 '?' (그대로!) → <b>아무 일도 안 일어남</b>
///             → 엑셀을 닫는다 → 진짜 해시 (바뀜) → 그제서야 반영
/// </code>
///
/// 읽기 전용 대사 미리보기는 <see cref="EpisodeWorkbookReader"/>로 <b>파일을 직접</b>
/// 읽어서 멀쩡했다 — 그 비대칭이 신고의 모양 그대로다.
///
/// 그래서 둘을 지킨다:
///   ① <b>리더와 같은 공유 모드로 연다</b>(<c>FileShare.ReadWrite</c>) — 엑셀이 쥐고
///      있어도 읽힌다. 리더가 이미 그렇게 하고 있었다는 것이 증거다.
///   ② 그래도 못 읽으면 <b>상수를 적지 않는다</b> — 쓴 시각과 길이를 적는다. 모를 때도
///      <em>움직이는</em> 값이라야 다음 저장이 묻히지 않는다.
/// </summary>
public static class WorkbookFolderFingerprint
{
    /// <param name="folders">챕터·대본 폴더. null이거나 없는 폴더도 그 사실을 적는다.</param>
    public static string Of(params string?[] folders)
    {
        ArgumentNullException.ThrowIfNull(folders);

        var builder = new StringBuilder();

        foreach (string? folder in folders)
        {
            AppendFolder(builder, folder);
        }

        return builder.ToString();
    }

    private static void AppendFolder(StringBuilder builder, string? folder)
    {
        if (folder is null || !Directory.Exists(folder))
        {
            builder.Append(folder ?? "-").Append("|없음\n");
            return;
        }

        foreach (string path in Directory
                     .EnumerateFiles(folder, "*.xls*", SearchOption.AllDirectories)
                     // 엑셀의 잠금 파일(~$…)은 내용이 아니다 — 열고 닫는 것이 저장이 아니다.
                     .Where(file => !Path.GetFileName(file).StartsWith("~$", StringComparison.Ordinal))
                     .OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(path).Append('|').Append(Mark(path)).Append('\n');
        }
    }

    /// <summary>이 파일의 지금 모습 — 되도록 내용 해시, 안 되면 <b>움직이는</b> 대체값.</summary>
    private static string Mark(string path)
    {
        try
        {
            // ⚠ 리더와 같은 공유 모드다. 엑셀이 쓰기 권한으로 쥐고 있어도 읽힌다.
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // ⛔ 여기서 상수를 돌려주면 그 파일은 <b>영영 안 바뀐 것</b>이 된다.
            // 쓴 시각과 길이는 저장할 때마다 움직이므로, 몰라도 묻히지는 않는다.
            try
            {
                var info = new FileInfo(path);
                return $"잠김:{info.LastWriteTimeUtc.Ticks}:{info.Length}";
            }
            catch (Exception inner) when (
                inner is IOException or UnauthorizedAccessException)
            {
                // 메타데이터조차 못 읽는다 — 그 사실 자체가 상태다.
                return "잠김:알수없음";
            }
        }
    }
}
