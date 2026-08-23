using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Vn.Authoring.Chapters;

/// <summary>
/// 워크북 파싱 결과를 <b>내용 해시로</b> 기억한다 (2026-08-24 성능, 소유자: "대본들은 개별
/// 엑셀인데, 그것들 중 변경된 것만 읽으면 될 것 같습니다").
///
/// <b>왜 있어야 했나 — 실측</b>. 대본 32개로 "아무것도 안 바뀐 동기화 한 바퀴"를 재니
/// 594ms였고 그중 <b>540ms(91%)가 워크북 파싱</b>이었다. 대본 하나만 저장해도 그 챕터의
/// 대본을 전부 다시 파고들기 때문이다.
///
/// <b>왜 "안 바뀐 것은 건너뛴다"가 아니라 "다시 파싱만 안 한다"인가.</b> 동기화를 통째로
/// 건너뛰는 길이 더 빠르지만, 그러면 <b>동기화가 대본을 되돌리는 힘</b>이 꺼진다 — 워크북은
/// 그대로인데 노드만 어긋난 경우(되돌리기 등)를 아무도 못 잡는다. 그 힘은 엑셀 되쓰기의
/// 존재 이유라 끄지 않는다(`EpisodeLineEditorTests.노드만_고치면_다음_동기화가_지운다`).
/// 파싱 결과만 기억하면 <b>동작이 한 글자도 안 바뀌면서</b> 91%가 사라진다 — 읽기는
/// <em>순수 함수</em>이기 때문이다: 같은 내용 + 같은 부가 입력 → 같은 모델.
///
/// <b>모델이 불변이라 나눠 써도 된다.</b> <see cref="ChapterGraphModel"/>·
/// <see cref="EpisodeWorkbookModel"/>은 전부 get-only이고 행은 record다. 하나라도
/// 쓰기 가능한 칸이 생기면 이 캐시는 <b>즉시 위험해진다</b> — 그때는 여기를 먼저 지워야
/// 한다.
///
/// ⚠ 열쇠는 경로가 아니라 <b>내용</b>이다(<see cref="WorkbookMigrationGate"/>와 같은 규율).
/// 경로로 기억하면 "고쳤는데 툴이 그대로"가 된다 — 캐시가 만드는 가장 나쁜 거짓말.
/// 부가 입력(챕터 조건 라벨·정의)도 열쇠에 들어간다: 같은 파일이라도 그것이 다르면
/// <b>진단이 달라진다.</b>
/// </summary>
public static class WorkbookParseCache
{
    private sealed record Entry(string Hash, object Model);

    /// <summary>
    /// 열쇠는 <b>경로 + 부가 입력</b>이다. 경로 하나에 한 칸만 두면 <b>서로 밀어낸다</b> —
    /// 실제로 그렇다: 같은 대본을 대사 미리보기는 <em>조건 라벨 없이</em>, 동기화·검증은
    /// <em>라벨과 함께</em> 읽는다. 한 칸이면 둘이 번갈아 캐시를 깨서 <b>고치기 전보다
    /// 느려진다</b>(해시 값까지 얹히므로). 재 보고 알았다.
    /// </summary>
    private static readonly ConcurrentDictionary<(string Path, string Variant), Entry> Cached = new();

    /// <summary>
    /// 캐시가 답할 수 있으면 그 모델을, 아니면 <paramref name="parse"/>를 돌려 기억한다.
    /// </summary>
    /// <param name="variant">
    /// 파일 내용 말고 결과를 바꾸는 것 전부를 한 줄로 (조건 라벨 목록 등).
    /// <b>빠뜨리면 조용히 틀린다</b> — 다른 입력의 답을 그대로 돌려주게 된다.
    /// </param>
    public static T Read<T>(string path, string variant, Func<T> parse)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(parse);

        // 해시를 못 얻으면(잠김·없음) 캐시는 손을 뗀다 — 평소대로 읽고, 그 읽기가
        // 실패하면 그쪽이 사유를 낸다. 모를 때 아는 척하지 않는다.
        string? hash = ContentHash(path);

        if (hash is null)
        {
            return parse();
        }

        (string, string) key = (path, variant);

        if (Cached.TryGetValue(key, out Entry? entry) &&
            string.Equals(entry.Hash, hash, StringComparison.Ordinal) &&
            entry.Model is T hit)
        {
            return hit;
        }

        T model = parse();

        // ⚠ 방금 읽은 내용의 해시로 적는다 — 위에서 잰 것 그대로다. 파싱 도중 파일이
        // 바뀌었다면 다음 호출의 해시가 달라 저절로 무효가 된다.
        Cached[key] = new Entry(hash, model);

        return model;
    }

    /// <summary>기억을 통째로 비운다 — 프로젝트를 닫거나 테스트가 판을 새로 깔 때.</summary>
    public static void Clear() => Cached.Clear();

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
