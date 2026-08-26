using Ked.Presentation.Core;
using Vn.Authoring.Definition;
using Vn.Authoring.Results;

namespace Vn.Authoring.Flow;

/// <summary>
/// 이 라인의 페이드 시간 — slotKey → 그 슬롯의 fade_in/fade_out이 선언한 초
/// (2026-08-26 소유자: "fade_in역시 동일한 원인으로 같은 문제가 있는 것 같아").
///
/// 페이드 불투명도는 <b>라인 시계(커맨드 duration 최댓값)가 아니라 그 페이드 커맨드
/// 자신의 duration</b>으로 흘러야 런타임과 같다 — 라인 시계를 타면 같은 라인에 더 긴
/// 커맨드가 있을 때 0fr 페이드도 그 시간만큼 천천히 밝아진다. 모션 계획이 이동을
/// 커맨드마다 제 duration으로 끄는 것과 같은 규칙이고, 페이드는 rect 노드가 아니라
/// 계획에 못 실리므로 이 사전이 그 몫을 진다.
/// </summary>
public static class StageFades
{
    /// <summary>
    /// 항목이 없는 슬롯은 이 라인에 페이드가 없다 — 가시성이 바뀌었다면(show 등) 즉시다.
    /// 같은 슬롯에 페이드가 여럿이면 뒤의 것이 이긴다(라인 안 커맨드는 순서 재생).
    /// </summary>
    public static IReadOnlyDictionary<string, double> Of(
        PresentationCommandCatalog catalog,
        IReadOnlyList<PresentationResultCommand>? commands,
        MiniStageState state)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(state);

        var fades = new Dictionary<string, double>(StringComparer.Ordinal);

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

            fades[slot] =
                Arg("duration") is { } token && DurationToken.TryParseSeconds(token, out float seconds)
                    ? seconds
                    : 0;
        }

        return fades;
    }
}
