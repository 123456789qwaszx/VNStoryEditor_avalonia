using Ked.Presentation.Core;
using Vn.Authoring.Definition;
using Vn.Authoring.Results;

namespace Vn.Authoring.Flow;

/// <summary>슬롯 하나의 이번 라인 페이드 — 방향(등장/퇴장)과 선언한 시간(초).</summary>
public readonly record struct StageFade(double Seconds, bool FadeIn);

/// <summary>
/// 이 라인의 페이드 — slotKey → 그 슬롯의 fade_in/fade_out
/// (2026-08-26 소유자: "fade_in역시 동일한 원인으로 같은 문제가 있는 것 같아").
///
/// 페이드 불투명도는 <b>라인 시계(커맨드 duration 최댓값)가 아니라 그 페이드 커맨드
/// 자신의 duration</b>으로 흘러야 런타임과 같다 — 라인 시계를 타면 같은 라인에 더 긴
/// 커맨드가 있을 때 0fr 페이드도 그 시간만큼 천천히 밝아진다. 모션 계획이 이동을
/// 커맨드마다 제 duration으로 끄는 것과 같은 규칙이고, 페이드는 rect 노드가 아니라
/// 계획에 못 실리므로 이 사전이 그 몫을 진다.
///
/// ⚠ <b>방향도 싣는다</b> (2026-08-26 소유자: "첫번째 말하는 거는 fade_in의 duration을
/// 늘리더라도 0인것처럼 즉시 보이는") — 등장 판정을 렌더 기준선(직전 프레임 가시성)에만
/// 맡기면, 씬에 처음 들어올 때 첫 라인이 정체가 다른 요청으로 두 번 그려지며 기준선이
/// "이미 보임"으로 굳어 첫 라인의 fade_in만 삼켜진다. 이 라인에 fade_in이 적혀 있다는
/// 사실 자체가 등장의 근거다 — 재생은 그 라인의 커맨드를 처음부터 다시 트는 것이다.
/// </summary>
public static class StageFades
{
    /// <summary>
    /// 항목이 없는 슬롯은 이 라인에 페이드가 없다 — 가시성이 바뀌었다면(show 등) 즉시다.
    /// 같은 슬롯에 페이드가 여럿이면 뒤의 것이 이긴다(라인 안 커맨드는 순서 재생).
    /// </summary>
    public static IReadOnlyDictionary<string, StageFade> Of(
        PresentationCommandCatalog catalog,
        IReadOnlyList<PresentationResultCommand>? commands,
        MiniStageState state)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(state);

        var fades = new Dictionary<string, StageFade>(StringComparer.Ordinal);

        foreach (PresentationResultCommand command in commands ?? [])
        {
            if (catalog.Find(command.DefinitionId) is not { } definition ||
                definition.OutputCommandName is not ("fade_in" or "fade_out"))
            {
                continue;
            }

            string? Arg(string name) =>
                command.Arguments.TryGetValue(name, out string? value) &&
                !string.IsNullOrWhiteSpace(value)
                    ? value.Trim()
                    : definition.FindParameter(name)?.Default;

            if (Arg("slot") is not { } slot)
            {
                continue;
            }

            // 슬롯 인자는 @별칭일 수 있다 — 폴드와 같은 1단계 치환(미등록은 원문 그대로).
            if (slot.StartsWith('@') && state.Aliases.TryGetValue(slot, out string? mapped))
            {
                slot = mapped;
            }

            fades[slot] = new StageFade(
                Arg("duration") is { } token && DurationToken.TryParseSeconds(token, out float seconds)
                    ? seconds
                    : 0,
                FadeIn: string.Equals(
                    definition.OutputCommandName, "fade_in", StringComparison.Ordinal));
        }

        return fades;
    }
}
