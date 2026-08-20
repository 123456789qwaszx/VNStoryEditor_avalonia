namespace Vn.Authoring.Tests;

/// <summary>
/// 런타임 Yarn 브리지에 실제로 등록된 커맨드 이름의 실측 스냅샷을 읽는다 (W65).
///
/// 이 목록은 저쪽 저장소(<c>ked-presentation-runtime</c>)의 <c>AddCommandHandler</c> 등록을
/// 훑어 만든 것이고, 갱신 방법은 픽스처 파일 머리에 적혀 있다. 툴이 저쪽 소스를 직접 읽지
/// 않는 이유는 저장소가 항상 나란히 있으리라는 보장이 없기 때문이다 — 대신 **스냅샷을
/// 저장소에 두고 테스트가 대조**한다. 튜닝 덤프를 픽스처로 두는 것과 같은 규약이다.
/// </summary>
internal static class RuntimeCommandFixture
{
    // 다른 픽스처와 같은 관습 — 출력 폴더로 복사하지 않고 소스 트리에서 직접 읽는다.
    private static readonly string Path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "TuningFixtures", "runtime-commands.txt"));

    public static HashSet<string> Load()
    {
        Assert.True(File.Exists(Path), $"런타임 커맨드 픽스처가 없습니다: {Path}");

        return File.ReadLines(Path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);
    }
}
