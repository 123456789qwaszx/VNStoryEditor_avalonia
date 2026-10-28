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

        // 슬라이드 (2026-08-24) — 퇴장은 나간 자리가 곧 정착 상태라 화면에 그려야 한다.
        // 등장은 정착으로는 항등이지만 <b>여기 있어야 한다</b>: 없으면 "접혔지만 안 그린다"
        // 뱃지로 남고, 고칠 것이 없는 뱃지는 진짜 미표시를 그 안에 묻는다.
        "slide_in", "slide_out",

        // 좌표 축 (W25에서 그리기 시작) — place 14종.
        "place", "place_left", "place_center", "place_right",
        "place_tl", "place_top", "place_tr",
        "place_bl", "place_bottom", "place_br",
        "place_inner_tl", "place_inner_tr", "place_inner_bl", "place_inner_br",

        // 뎁스 6종.
        "size", "size_far", "size_back", "size_mid", "size_front", "size_close",

        // 이동·스케일·회전.
        "left", "right", "up", "down",
        "move_by", "move_reset", "scale_by", "scale_reset", "rotate_by", "rotate_reset",

        // 샷 5종.
        "shot_focus_to", "shot_zoom", "shot_to", "shot_track", "shot_reset",
    };

    /// <param name="stopBeforeCommandId">
    /// 주면 그 커맨드를 <b>적용하기 직전</b>에 멈춘다 — 모션 편집이 "이 이동이 시작하는
    /// 자리"를 알아야 해서 열어 둔 문이다(W66). 그 자리를 뒤에서 빼서 구하지 않는 이유는
    /// 상대 이동에만 통하는 셈법이기 때문이다. null이면 전부 접는다.
    /// </param>
    public static CoreStageFoldResult Fold(
        PresentationCommandCatalog catalog,
        IReadOnlyList<PresentationResultCommand> setupCommands,
        IReadOnlyList<MiniStageFoldLine> lines,
        StageReducerTuning? tuning,
        string? stopBeforeCommandId = null)
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

        bool Stop(PresentationResultCommand command) =>
            stopBeforeCommandId is not null &&
            string.Equals(command.CommandId, stopBeforeCommandId, StringComparison.Ordinal);

        foreach (PresentationResultCommand command in setupCommands)
        {
            if (Stop(command))
            {
                return new CoreStageFoldResult(MiniStageFold.Build(fold), core);
            }

            core = ApplyOne(core, fold, catalog, tuning, lineId: null, command);
        }

        foreach (MiniStageFoldLine line in lines)
        {
            fold.PassedBranch |= line.HasBranchTransition;

            foreach (PresentationResultCommand command in line.Commands)
            {
                if (Stop(command))
                {
                    return new CoreStageFoldResult(MiniStageFold.Build(fold), core);
                }

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

        if (MiniStageFold.IsAudioCue(definition))
        {
            // 코어에 넣으면 "이관 안 됨" 진단만 쌓인다 — 표현은 ♪ 칩·실재생(W62)의 것.
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

        if (EmotionCarryingCommands.Contains(outputCommand))
        {
            // 표정은 코어 상태가 아니다 (코어 사본 최신화 W64) — 런타임 CastBinding도
            // 변형만 들고, 표정은 커맨드 인자로만 흘러 사이징에 쓰인다. 화면에 지금
            // 무슨 표정이 떠 있는가는 보완 폴드가 Mirrored와 같은 자리에서 진다.
            MiniStageFold.Apply(fold, catalog, lineId, command);
        }

        if (DrawnCoreCommands.Contains(outputCommand))
        {
            ProjectSlots(fold, next);
        }
        else
        {
            // 접혔지만 아직 안 그리는 축 — H-3 "미표시"로 남긴다(규칙 14, W26 뱃지 분리).
            fold.Unhandled.Add(new MiniStageUnhandled(lineId, outputCommand, FoldedButNotDrawn: true));
        }

        return next;
    }

    /// <summary>
    /// 표정을 실어 나르는 코어 커맨드 — 코어는 접되 표정 축만은 보완 폴드가 든다.
    /// 코어 상태가 표정을 버렸기 때문이다(변형만 상태, 표정은 인자 — StageReducer.Portrait 참조).
    /// </summary>
    private static readonly HashSet<string> EmotionCarryingCommands = new(StringComparer.Ordinal)
    {
        "cast", "show", "face", "face_swap",
    };

    /// <summary>
    /// 코어 상태 → 기존 슬롯 표시 모양. 코어가 아는 슬롯만 덮고(merge), 보완 폴드만 아는
    /// 슬롯(slot_tyrant·관대한 생성분)과 코어 밖 축(Mirrored·표정)은 보존한다.
    /// 표정은 보완 폴드의 어휘(원문 토큰)다 — 초상 해석이 <c>PortraitKey.Normalize</c>로
    /// 같은 정규화를 지나므로 화면 결과는 같다.
    /// </summary>
    private static void ProjectSlots(MiniStageFold.FoldState fold, StageState core)
    {
        foreach (string slotKey in core.Slots)
        {
            bool hasCast = core.TryGetCharacter(slotKey, out string? characterKey);

            // 코어의 빈 변형 = 기본 변형으로 물러선다는 뜻 — 화면 어휘로는 "a"다.
            string variantKey = core.GetVariant(slotKey);
            if (hasCast && variantKey.Length == 0)
                variantKey = PortraitDimensionsFileDto.DefaultVariantKey;

            bool visible = core.GetAlpha(
                StageState.NodeKeyOf(slotKey, "CharacterPortraitSprite_Root")) > 0.5f;

            bool hasExisting = fold.Slots.TryGetValue(slotKey, out MiniStageSlot? existing);
            bool mirrored = hasExisting && existing!.Mirrored;
            string? emotion = hasExisting ? existing!.EmotionKey : null;

            fold.Slots[slotKey] = new MiniStageSlot(
                characterKey,
                hasCast ? variantKey : null,
                hasCast ? emotion : null,
                visible,
                mirrored);
        }
    }
}
