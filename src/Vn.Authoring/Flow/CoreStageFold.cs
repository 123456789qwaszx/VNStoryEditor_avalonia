using Ked.Presentation.Core;
using Vn.Authoring.Definition;
using Vn.Authoring.Results;

namespace Vn.Authoring.Flow;

/// <summary>
/// 합성 폴드의 결과. <see cref="State"/>는 기존 프리뷰가 그리는 모양 그대로이고,
/// <see cref="CoreState"/>는 코어 리듀서가 접은 확정 무대 상태다(tuning이 없으면 null) —
/// W25의 좌표 배치가 이것을 읽는다.
/// </summary>
public sealed record CoreStageFoldResult(MiniStageState State, StageState? CoreState);

/// <summary>
/// 무대 폴드의 단일 진입점 (W24) — 코어 리듀서 + 툴 보완 폴드의 합성.
///
/// 커맨드 해석의 소유권 (H-2):
/// - <b>코어 <see cref="StageReducer"/>가 접는 축</b>(슬롯·배역·표정·가시성·배치·뎁스·샷)은
///   코어가 유일한 해석이다. 인자 순서는 이미터와 같은 <see cref="CommandText.ResolveOrdered"/> —
///   발행 Freeze가 프리셋을 최종 값으로 풀어 놓으므로 여기서 새 변환 층은 없다(§6.3).
/// - <b>코어에 없는 축</b>(배경·대사창·mirror·face_crossfade·slot_tyrant)과
///   코어가 Unhandled로 돌려준 커맨드만 기존 <see cref="MiniStageFold"/>의 보완 폴드로 간다.
/// - tuning이 없으면 코어를 부르지 않고 기존 폴드 전체가 폴백이다 — 좌표 없는 근사가
///   그대로 유지되고, 그 사실은 프리뷰 안내가 말한다(W23).
///
/// 화면 규약 (규칙 14): 코어가 접었지만 v1 프리뷰가 아직 그리지 않는 축(place·size·shot 등)은
/// 기존과 똑같이 "반영 안 된 연출"로 남는다 — 상태에 있다는 이유로 뱃지에서 내리지 않는다.
/// 내리는 시점은 실제로 그리기 시작하는 W25다.
/// </summary>
public static class CoreStageFold
{
    /// <summary>
    /// 프리뷰가 화면에 그리는 코어 축의 커맨드 — 커맨드 해석이 아니라 <b>표시 정책</b>이다.
    /// 여기 없는 코어 처리 커맨드는 상태에는 접히지만 아직 안 그리므로 뱃지에 남는다(규칙 14).
    /// W25부터 좌표 축(place·size·이동·샷)이 코어 상태 그대로 그려지므로 이 목록에 있다 —
    /// 코어가 접었는데 화면에 안 나오는 것은 이제 구조 기록(char_to)과 셰이더 축뿐이다.
    /// </summary>
    internal static readonly HashSet<string> DrawnCoreCommands = new(StringComparer.Ordinal)
    {
        "slot", "slot00", "slot01", "slot02",
        "cast", "pose", "actor",
        "show", "face", "face_swap", "fade_in", "fade_out",

        // 좌표 축 (W25에서 그리기 시작) — place 14종.
        "place", "place_left", "place_center", "place_right",
        "place_tl", "place_top", "place_tr",
        "place_bl", "place_bottom", "place_br",
        "place_inner_tl", "place_inner_tr", "place_inner_bl", "place_inner_br",

        // 뎁스 6종.
        "size", "size_far", "size_back", "size_mid", "size_front", "size_close",

        // 이동·스케일·회전.
        "left", "right", "up", "down",
        "left_per", "right_per", "up_per", "down_per",
        "move_by", "move_reset", "scale_by", "scale_reset", "rotate_by", "rotate_reset",

        // 샷 5종.
        "shot_focus_to", "shot_zoom", "shot_to", "shot_track", "shot_reset",
    };

    public static CoreStageFoldResult Fold(
        PresentationCommandCatalog catalog,
        IReadOnlyList<PresentationResultCommand> setupCommands,
        IReadOnlyList<MiniStageFoldLine> lines,
        StageReducerTuning? tuning)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(setupCommands);
        ArgumentNullException.ThrowIfNull(lines);

        if (tuning is null)
        {
            return new CoreStageFoldResult(MiniStageFold.Fold(catalog, setupCommands, lines), null);
        }

        var fold = new MiniStageFold.FoldState();
        StageState core = StageReducer.CreateInitialState(tuning);

        foreach (PresentationResultCommand command in setupCommands)
        {
            core = ApplyOne(core, fold, catalog, tuning, lineId: null, command);
        }

        foreach (MiniStageFoldLine line in lines)
        {
            fold.PassedBranch |= line.HasBranchTransition;

            foreach (PresentationResultCommand command in line.Commands)
            {
                core = ApplyOne(core, fold, catalog, tuning, line.LineId, command);
            }
        }

        return new CoreStageFoldResult(MiniStageFold.Build(fold), core);
    }

    private static StageState ApplyOne(
        StageState core,
        MiniStageFold.FoldState fold,
        PresentationCommandCatalog catalog,
        StageReducerTuning tuning,
        string? lineId,
        PresentationResultCommand command)
    {
        PresentationCommandDefinition? definition = catalog.Find(command.DefinitionId);

        if (definition is null)
        {
            // 카탈로그에 없는 정의 — 기존 폴드와 같은 자리에서 같은 모양으로 남긴다.
            fold.Unhandled.Add(new MiniStageUnhandled(lineId, command.DefinitionId));
            return core;
        }

        string outputCommand = definition.OutputCommandName;
        string[] args = CommandText.ResolveOrdered(definition, command.Arguments)
            .Select(argument => argument.Value)
            .ToArray();

        int unhandledBefore = core.Unhandled.Count;
        StageState next = StageReducer.Apply(
            core,
            new StageCommand(outputCommand, args, lineId ?? "Setup"),
            tuning);

        // 코어가 온전히 접었는가 — 새 Unhandled가 없어야 접힌 것이다. 대상 거부든 내부
        // 진단(초상 치수 없음 등)이든 무언가 남았으면 보완 폴드가 기존 의미로 접는다:
        // 화면은 기존과 같게 유지되고(골든), 코어 쪽 기록은 CoreState.Unhandled에 그대로
        // 남아 W26의 "미표시" 뱃지 원료가 된다. 사유 문자열로 종류를 가르지 않는다 —
        // 코어 메시지에 결합하는 판정은 코어가 바뀌는 날 조용히 어긋난다.
        bool coreHandled = next.Unhandled.Count == unhandledBefore;

        if (!coreHandled)
        {
            // 코어 밖 축(배경·대사창·mirror 등)·거부된 대상·진단이 남은 커맨드 — 보완 폴드로.
            MiniStageFold.Apply(fold, catalog, lineId, command);
            return next;
        }

        if (string.Equals(outputCommand, "actor", StringComparison.Ordinal))
        {
            // 별칭 사전은 보완 축(mirror 등)의 @해석과 편집기 표시에 계속 필요하다.
            // 같은 값을 같은 규칙으로 등록할 뿐이라 화면 결과는 코어와 어긋날 수 없다.
            MiniStageFold.Apply(fold, catalog, lineId, command);
            return next;
        }

        if (DrawnCoreCommands.Contains(outputCommand))
        {
            ProjectSlots(fold, next);
        }
        else
        {
            // 접혔지만 아직 안 그리는 축 — 기존 뱃지 의미 그대로 남긴다(규칙 14).
            fold.Unhandled.Add(new MiniStageUnhandled(lineId, outputCommand));
        }

        return next;
    }

    /// <summary>
    /// 코어 상태 → 기존 슬롯 표시 모양. 코어가 아는 슬롯만 덮고(merge), 보완 폴드만 아는
    /// 슬롯(slot_tyrant·관대한 생성분)과 코어 밖 축(Mirrored)은 보존한다.
    /// 표정 키는 코어의 정규화 2자리("02")다 — 초상 해석(<c>PortraitKey.Normalize</c>)과
    /// 같은 어휘라 화면 결과는 같고, 팝오버의 현재 표정 강조는 오히려 정확해진다.
    /// </summary>
    private static void ProjectSlots(MiniStageFold.FoldState fold, StageState core)
    {
        foreach (string slotKey in core.Slots)
        {
            bool hasPortrait = core.TryGetPortrait(slotKey, out char variantSuffix, out string? emotion);
            core.TryGetCharacter(slotKey, out string? characterKey);

            bool visible = core.GetAlpha(
                StageState.NodeKeyOf(slotKey, "CharacterPortraitSprite_Root")) > 0.5f;

            bool mirrored = fold.Slots.TryGetValue(slotKey, out MiniStageSlot? existing) && existing.Mirrored;

            fold.Slots[slotKey] = new MiniStageSlot(
                characterKey,
                hasPortrait ? variantSuffix.ToString() : null,
                hasPortrait ? emotion : null,
                visible,
                mirrored);
        }
    }
}
