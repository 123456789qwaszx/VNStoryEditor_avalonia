using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 이미 <b>최신 규격이라고 판정이 끝난</b> 워크북을 다시 파고들지 않게 한다 (2026-08-24 성능).
///
/// <b>왜 있어야 했나 — 실측</b>. 두 이행기는 "고칠 것이 있는가"를 <em>워크북을 통째로
/// 파싱해서</em> 판정한다. 그런데 그 판정은 거의 언제나 "없음"이고, 앱은 다시 읽을 때마다
/// (감시자가 깨울 때마다) 모든 챕터·대본 워크북에 그 질문을 다시 한다. 워크북 72개
/// (챕터 8 · 대본 64)로 재 보니:
///
/// <code>
/// 지문(전 파일 SHA256)   16ms      ← 싸다. 여기가 문제가 아니었다
/// 챕터 이행 프로브        354ms  ⚠  전부 "필요 없음"
/// ChapterLibrary.Load    364ms      필요한 일
/// 대본 이행 프로브        717ms  ⚠  전부 "필요 없음"
/// 대본 읽기              681ms      필요한 일
/// </code>
///
/// <b>워크북 작업의 절반이 아무것도 낳지 않는 프로브였다.</b> 이행기 주석은
/// <i>"필요 없는 파일에는 손대지 않으므로 매번 불러도 <b>쓰기</b>는 한 번뿐"</i>이라고
/// 적어 두었는데, 맞는 말이지만 <em>읽기</em>는 매번이었고 그 읽기가 값이었다.
///
/// <b>왜 내용 해시인가 — 시각·크기가 아니라.</b> 이 저장소는 같은 질문을 검증 캐시에서
/// 이미 한 번 답했다: <i>"캐시가 만드는 가장 나쁜 거짓말"</i>은 낡은 답을 붙드는 것이다.
/// 시각·크기는 싸지만 "고쳤는데 툴이 모른다"를 만들 수 있고, 그 사고는 조용하다.
/// 내용 해시는 그 거짓말을 구조적으로 못 한다 — 그리고 위 표가 보이듯 전 파일 해시는
/// 16ms다. 44ms짜리 파싱 하나보다 싸다.
/// </summary>
public static class WorkbookMigrationGate
{
    /// <summary>경로 → 그 내용일 때 "이행할 것 없음"이 나왔다는 기록.</summary>
    private static readonly ConcurrentDictionary<string, string> Current = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 이 파일은 <b>지금 이 내용 그대로</b> 이미 판정이 끝났는가. 참이면 이행기는 그냥
    /// 돌아간다.
    ///
    /// ⚠ 파일을 못 읽으면(잠김·없음) <b>거짓</b>이다 — 모를 때는 아는 척하지 않는다.
    /// 그러면 이행기가 평소대로 프로브하고, 그쪽이 사유를 남긴다.
    /// </summary>
    public static bool IsKnownCurrent(string? path)
    {
        return path is not null &&
            ContentHash(path) is { } hash &&
            Current.TryGetValue(path, out string? seen) &&
            string.Equals(seen, hash, StringComparison.Ordinal);
    }

    /// <summary>
    /// 방금 "이행할 것 없음"이 나왔다 — 이 내용인 동안은 다시 묻지 않는다.
    ///
    /// ⚠ 이행을 <b>한</b> 경우에도 부를 수 있다: 그때는 파일이 이미 새 내용이라 그 새
    /// 내용으로 기록된다. 못 읽으면 아무것도 기록하지 않는다.
    /// </summary>
    public static void MarkCurrent(string? path)
    {
        if (path is not null && ContentHash(path) is { } hash)
        {
            Current[path] = hash;
        }
    }

    /// <summary>기억을 통째로 비운다 — 프로젝트를 닫거나 테스트가 판을 새로 깔 때.</summary>
    public static void Clear() => Current.Clear();

    private static string? ContentHash(string path)
    {
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                         FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }
}
