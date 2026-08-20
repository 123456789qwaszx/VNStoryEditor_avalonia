using System.Text.RegularExpressions;
using Ked.Presentation.Core;

namespace Vn.Authoring.Model;

/// <summary>
/// 작가가 만든 커스텀 이징 곡선 하나 (W67 후속 — 커스텀 곡선). 커맨드의 다섯째 인자가
/// <c>@이름</c>으로 참조한다.
///
/// <b>프로젝트에 산다</b> — 커브는 작가 자산이라 game.definition(기획자 전용)이 아니라
/// 여기다(WriterSpeakers와 같은 자리 감각). 런타임이 먹는 <c>curves.json</c>은 저장
/// 파일이 아니라 <b>내보내기 산출물</b>이다 — 값의 주인은 이 모델 하나다.
///
/// 키 모델은 코어 <see cref="CurveKey"/> 그대로(Unity AnimationCurve 동형 Hermite) —
/// 평가도 코어 <see cref="CurveFunctions"/> 하나를 프리뷰·런타임이 같이 쓴다.
/// </summary>
public sealed class EaseCurve
{
    /// <summary>커맨드 토큰에 실리는 이름 — <c>@</c> 뒤의 부분. 규칙은 <see cref="IsValidName"/>.</summary>
    public string Name { get; set; } = string.Empty;

    public List<CurveKey> Keys { get; set; } = new();

    /// <summary>
    /// 이 곡선을 소유한 커맨드의 Id (2026-08-20 소유자 결정 — "커맨드 단위로 customEase").
    /// null이면 <b>보관함</b> 곡선이다: 작가가 이름 붙여 모아 두는 편집 공간으로,
    /// 커맨드와는 <b>복사로만</b> 오간다(참조 공유 없음 — 한 커맨드를 만져도 다른
    /// 커맨드가 안 변하는 것이 이 설계의 목적이다).
    /// </summary>
    public string? OwnerCommandId { get; set; }

    /// <summary>보관함 곡선인가 — 곡선 편집 공간의 목록·가져오기 후보는 이것만 든다.</summary>
    public bool IsLibrary => OwnerCommandId is null;

    /// <summary>
    /// 커맨드 소유 곡선의 자동 이름 — 커맨드 Id에서 파생하므로 커맨드당 하나로 안정된다.
    /// 이름 규칙([a-z0-9_])에 맞게 소문자·언더스코어로 접는다.
    /// </summary>
    public static string OwnedNameFor(string commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);

        var builder = new System.Text.StringBuilder("cmd_");

        foreach (char letter in commandId.ToLowerInvariant())
        {
            builder.Append(letter is (>= 'a' and <= 'z') or (>= '0' and <= '9') ? letter : '_');
        }

        return builder.ToString();
    }

    /// <summary>런타임 로더와 같은 이름 규칙 — 커맨드 토큰에 실리므로 공백·특수문자 금지.</summary>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name) && Regex.IsMatch(name, "^[a-z0-9_]+$");

    /// <summary>
    /// 런타임 로더와 같은 키 규칙 — 키 2개 이상 · t 오름차순 · 첫 키 t=0 · 마지막 키 t=1.
    /// 저작이 1차 방어이므로 여기서는 위반을 <b>저장 자체를 거부</b>하는 데 쓴다
    /// (런타임은 경고 로그 + 무시로 물러선다).
    /// </summary>
    public static string? ValidateKeys(IReadOnlyList<CurveKey> keys)
    {
        if (keys is null || keys.Count < 2)
        {
            return "키가 2개 이상이어야 합니다 (첫 키 t=0, 마지막 키 t=1).";
        }

        if (Math.Abs(keys[0].Time) > 0.0001f)
        {
            return "첫 키는 t=0이어야 합니다.";
        }

        if (Math.Abs(keys[^1].Time - 1f) > 0.0001f)
        {
            return "마지막 키는 t=1이어야 합니다.";
        }

        for (int i = 1; i < keys.Count; i++)
        {
            if (keys[i].Time <= keys[i - 1].Time)
            {
                return "키는 t 오름차순이어야 합니다 (겹친 t 불가).";
            }
        }

        return null;
    }

    public EaseCurve Clone() => new()
    {
        Name = Name,
        Keys = new List<CurveKey>(Keys),
        OwnerCommandId = OwnerCommandId
    };
}
