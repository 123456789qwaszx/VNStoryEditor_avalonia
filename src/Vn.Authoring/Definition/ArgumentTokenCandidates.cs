namespace Vn.Authoring.Definition;

/// <summary>
/// 카탈로그 파라미터 <c>type</c>별 후보 토큰. 칩 편집 UI의 드롭다운 후보가 된다.
///
/// 출처는 런타임 파서들의 허용 토큰 표와 타이밍 감각 표다. 그 표를 옮겨 적어 두었던
/// <c>docs/YarnCommandBridge_Reference.md</c>는 유니티 저장소 API의 낡은 사본이라
/// 2026-08-18에 걷었다 — 계약 수준의 어휘 정리는 <c>docs/runtime-contract.md</c> §E에
/// 있고, 커맨드 하나하나의 사양은 런타임 저장소가 정본이다(옛 사본은 git 이력에 남아 있다).
/// <b>후보일 뿐 제약이 아니다</b> — 직접 입력은 언제나 허용되고, 여기 없는 토큰을
/// 막지 않는다(어휘의 진실은 런타임 파서다).
/// slot·alias 후보는 정적이지 않으므로 여기 없다 — 폴드 상태에서 나온다.
/// </summary>
public static class ArgumentTokenCandidates
{
    private static readonly Dictionary<string, string[]> ByType = new(StringComparer.Ordinal)
    {
        // §23.6 타이밍 감각 — 즉시/순간 반응/표준 전환/여유/씬 전환.
        ["duration"] = ["0fr", "4fr", "6fr", "10fr", "14fr", "24fr", "1.2s"],
        ["frames"] = ["1fr", "4fr", "6fr", "12fr", "24fr"],
        ["direction"] = ["left", "right", "up", "down"],
        ["mirrorMode"] = ["left", "right", "toggle"],
        ["stageKey"] = ["stage00", "stage01", "stage02"],
        ["layerKey"] = ["far", "back", "mid", "front", "close"],
        ["focusPreset"] = ["face", "bust", "body", "feet", "hand_left", "hand_right"],
        ["screenPoint"] = ["center", "left", "right", "top", "bottom", "tl", "tr", "bl", "br"],
        ["depthPreset"] = ["far", "back", "mid", "front", "close"],
        ["boxKind"] = ["Portrait", "Speaker", "LetterBox", "OnlyText", "BlackBook", "Surface"]
    };

    /// <summary>
    /// 이징 후보 (W67) — 어휘의 정본은 코어 <see cref="Ked.Presentation.Core.EaseKind"/>다
    /// (골든 덤프와 양방향 일치가 테스트로 고정돼 있다). 여기 옮겨 적으면 사본이 된다.
    /// </summary>
    private static readonly string[] EaseCandidates =
        Enum.GetNames<Ked.Presentation.Core.EaseKind>();

    /// <summary>이 타입에 보여 줄 후보. 없으면 빈 목록 — 자유 입력만 남는다.</summary>
    public static IReadOnlyList<string> For(string? type)
    {
        if (string.Equals(type, "ease", StringComparison.Ordinal))
        {
            return EaseCandidates;
        }

        return type is not null && ByType.TryGetValue(type, out string[]? candidates)
            ? candidates
            : Array.Empty<string>();
    }

    /// <summary>무대 위 대상(slot/alias)을 받는 타입인가 — 후보가 폴드 상태에서 나온다.</summary>
    public static bool IsStageTargetType(string? type) =>
        type is "aliasOrSlot" or "slotKey";
}
