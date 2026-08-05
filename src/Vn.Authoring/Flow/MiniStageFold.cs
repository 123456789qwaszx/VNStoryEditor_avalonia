using Vn.Authoring.Definition;
using Vn.Authoring.Results;

namespace Vn.Authoring.Flow;

/// <summary>무대 위 슬롯 하나의 접힌 상태. 좌표·크기는 없다 — 그건 2b의 일이다.</summary>
public sealed record MiniStageSlot(
    string? CharacterId,
    string? VariantKey,
    string? EmotionKey,
    bool Visible,
    bool Mirrored);

/// <summary>
/// 폴드가 반영하지 못한 연출 하나. <b>커맨드명과 라인을 보존한다</b> —
/// 이 목록이 "이 라인에 반영 안 된 연출 N개" 뱃지가 되고, 2b 확장의 백로그가 된다.
/// </summary>
/// <param name="LineId">null이면 Setup 블록의 커맨드다.</param>
public sealed record MiniStageUnhandled(string? LineId, string CommandName);

/// <summary>
/// 선택 라인까지 접은 미니 무대 상태. "무슨 배경에서 누가 말하는가"에만 답한다.
/// </summary>
/// <param name="PassedBranchApproximation">
/// 지나온 구간에 조건·선택 갈래가 있어 "문서 순서대로 전부 적용" 근사가 쓰였다.
/// 프리뷰는 이 표시를 띄운다 — 갈래 인식 폴드는 2b에서.
/// </param>
public sealed record MiniStageState(
    string? BackgroundKey,
    string? BackgroundRigKey,
    IReadOnlyDictionary<string, MiniStageSlot> Slots,
    IReadOnlyDictionary<string, string> Aliases,
    string NamedBoxKind,
    string ProtagonistBoxKind,
    IReadOnlyList<MiniStageUnhandled> Unhandled,
    bool PassedBranchApproximation)
{
    /// <summary>런타임 box_reset이 복원하는 기본값(커맨드 레퍼런스 §2).</summary>
    public const string DefaultNamedBoxKind = "Speaker";

    public const string DefaultProtagonistBoxKind = "Surface";

    public static MiniStageState Empty { get; } = new(
        null,
        null,
        new Dictionary<string, MiniStageSlot>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        DefaultNamedBoxKind,
        DefaultProtagonistBoxKind,
        Array.Empty<MiniStageUnhandled>(),
        PassedBranchApproximation: false);

    /// <summary>화자 있는 라인이면 named, 무명이면 protagonist 박스다.</summary>
    public string BoxKindFor(bool hasSpeaker) => hasSpeaker ? NamedBoxKind : ProtagonistBoxKind;

    /// <summary>프리뷰가 가로로 나열할 슬롯 — visible만, 슬롯 키 순서(Ordinal)다. 좌표 재현이 아니다.</summary>
    public IEnumerable<KeyValuePair<string, MiniStageSlot>> VisibleSlots =>
        Slots.Where(slot => slot.Value.Visible).OrderBy(slot => slot.Key, StringComparer.Ordinal);

    public int UnhandledCountFor(string? lineId) =>
        Unhandled.Count(item => string.Equals(item.LineId, lineId, StringComparison.Ordinal));
}

/// <summary>폴드 입력 라인 하나. 커맨드는 프리셋이 해석된 최종 값이다(발행 Freeze와 같은 모양).</summary>
public sealed record MiniStageFoldLine(
    string LineId,
    bool HasBranchTransition,
    IReadOnlyList<PresentationResultCommand> Commands);

/// <summary>
/// 미니 프리뷰의 라인 폴드. <b>순수 함수</b>다 — 계산은 저장하지 않는다.
///
/// 장면 재현이 아니라 정보가 목표라서 접는 것은 "마지막 값" 딕셔너리 두 개
/// (배경 키, 슬롯 상태)와 대사창 종류뿐이다. 라인 안쪽 커맨드 묶음은 배치로 재생되므로
/// (커맨드 레퍼런스 §0.1) 라인 경계의 확정 상태는 시간과 무관하다 — 그래서
/// 타임라인 없이 처음부터 선택 라인까지 접기만 하면 된다.
///
/// 인식하는 커맨드는 outputCommand 기준 ~20종(배경·슬롯·캐스팅·표시·표정·대사창).
/// <b>나머지는 조용히 버리지 않고 전부 <see cref="MiniStageState.Unhandled"/>에 남긴다.</b>
/// 위치·크기·효과 커맨드를 무시해도 "무슨 배경에서 누가 말하는가"의 답은 정확하다.
///
/// v1은 갈래를 가정하지 않는다 — 조건·선택 갈래의 라인도 문서 순서대로 전부 적용하는
/// 단순한 근사를 쓰고, 근사가 쓰였음을 <see cref="MiniStageState.PassedBranchApproximation"/>으로
/// 알린다. 갈래 인식 폴드는 2b의 일이다.
/// </summary>
public static class MiniStageFold
{
    public static MiniStageState Fold(
        PresentationCommandCatalog catalog,
        IReadOnlyList<PresentationResultCommand> setupCommands,
        IReadOnlyList<MiniStageFoldLine> lines)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(setupCommands);
        ArgumentNullException.ThrowIfNull(lines);

        var state = new FoldState();

        foreach (PresentationResultCommand command in setupCommands)
        {
            Apply(state, catalog, lineId: null, command);
        }

        foreach (MiniStageFoldLine line in lines)
        {
            state.PassedBranch |= line.HasBranchTransition;

            foreach (PresentationResultCommand command in line.Commands)
            {
                Apply(state, catalog, line.LineId, command);
            }
        }

        return Build(state);
    }

    internal static MiniStageState Build(FoldState state)
    {
        return new MiniStageState(
            state.BackgroundKey,
            state.BackgroundRigKey,
            state.Slots,
            state.Aliases,
            state.NamedBoxKind,
            state.ProtagonistBoxKind,
            state.Unhandled,
            state.PassedBranch);
    }

    /// <summary>
    /// 발행된 대사·연출 결과 쌍에서 폴드 입력을 만든다 — 문서 순서대로 선택 라인까지(포함).
    /// 선택 라인이 결과에 없으면 전체를 접는다(없는 라인은 프리뷰가 따로 알린다).
    /// </summary>
    public static IReadOnlyList<MiniStageFoldLine> LinesUpTo(
        DialogueResult dialogue,
        IReadOnlyList<PresentationResultBinding> bindings,
        string? selectedLineId)
    {
        ArgumentNullException.ThrowIfNull(dialogue);
        ArgumentNullException.ThrowIfNull(bindings);

        var lines = new List<MiniStageFoldLine>();

        foreach (DialogueResultLine line in dialogue.Lines)
        {
            PresentationResultBinding? binding = bindings.FirstOrDefault(
                item => string.Equals(item.LineId, line.LineId, StringComparison.Ordinal));

            lines.Add(new MiniStageFoldLine(
                line.LineId,
                line.Transition is not null,
                binding?.Commands ?? Array.Empty<PresentationResultCommand>()));

            if (string.Equals(line.LineId, selectedLineId, StringComparison.Ordinal))
            {
                break;
            }
        }

        return lines;
    }

    /// <summary>폴드 누적 상태. <see cref="CoreStageFold"/>가 보완 폴드로 재사용한다(같은 규칙 한 벌).</summary>
    internal sealed class FoldState
    {
        public string? BackgroundKey;
        public string? BackgroundRigKey;
        public readonly Dictionary<string, MiniStageSlot> Slots = new(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal);
        public string NamedBoxKind = MiniStageState.DefaultNamedBoxKind;
        public string ProtagonistBoxKind = MiniStageState.DefaultProtagonistBoxKind;
        public readonly List<MiniStageUnhandled> Unhandled = new();
        public bool PassedBranch;
    }

    internal static void Apply(
        FoldState state,
        PresentationCommandCatalog catalog,
        string? lineId,
        PresentationResultCommand command)
    {
        PresentationCommandDefinition? definition = catalog.Find(command.DefinitionId);

        // 카탈로그에 없는 정의도 커맨드명(정의 Id)째로 남긴다. 프리셋 참조는 발행 단계에서
        // 이미 최종 정의·인자로 해석되어 있으므로 여기서는 outputCommand로만 판정한다.
        if (definition is null)
        {
            state.Unhandled.Add(new MiniStageUnhandled(lineId, command.DefinitionId));
            return;
        }

        // 인자는 카탈로그 기본값 위에 발행 값이 덮인다 — 이미터의 생략 규칙과 같은 방향.
        string? Arg(string name)
        {
            return command.Arguments.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : definition.FindParameter(name)?.Default;
        }

        // 슬롯 인자는 @별칭일 수 있다. 등록된 별칭은 1단계 치환, 미등록은 원문 그대로
        // (런타임도 경고 후 원문 통과 — 커맨드 레퍼런스 §1.4).
        string? Slot(string name)
        {
            string? raw = Arg(name);

            return raw is not null && raw.StartsWith('@') &&
                state.Aliases.TryGetValue(raw, out string? mapped)
                ? mapped
                : raw;
        }

        switch (definition.OutputCommandName)
        {
            case "bg_spawn" or "bg_sprite" or "bg_slot00" or "bg_slot01" or "bg_slot02"
                when Arg("spriteKey") is { } spriteKey:
                state.BackgroundKey = spriteKey;
                // 리그 키도 접는다 — 직접 조작의 배경 교체(bg_sprite)가 대상 리그를 알아야 한다.
                state.BackgroundRigKey = Arg("rigKey") ?? state.BackgroundRigKey;
                break;

            case "slot" or "slot00" or "slot01" or "slot02" when Arg("slotKey") is { } slotKey:
                EnsureSlot(state, slotKey);
                break;

            case "slot_tyrant":
                // 런타임 매크로 의미: 슬롯 "tyrant" 생성 → cast tyrant Tyrant a 2 → fade_in.
                state.Slots["tyrant"] = new MiniStageSlot("Tyrant", "a", "2", Visible: true, Mirrored: false);
                break;

            case "cast" when Slot("slot") is { } slotKey:
                state.Slots[slotKey] = EnsureSlot(state, slotKey) with
                {
                    CharacterId = Arg("characterKey"),
                    VariantKey = Arg("variantKey"),
                    EmotionKey = Arg("emotionKey")
                };
                break;

            case "pose" when Slot("slot") is { } slotKey:
                state.Slots[slotKey] = EnsureSlot(state, slotKey) with { VariantKey = Arg("variantKey") };
                break;

            case "face" when Slot("slot") is { } slotKey:
                state.Slots[slotKey] = EnsureSlot(state, slotKey) with { EmotionKey = Arg("emotionKey") };
                break;

            case "actor" when Arg("aliasSymbol") is { } alias && Arg("targetKey") is { } target:
                state.Aliases[alias] = target;
                break;

            case "show" when Slot("slot") is { } slotKey:
                // show는 표정 설정 + 표시를 함께 한다. faceToken은 ShowFaceAliasParser 정규화(e1→1).
                state.Slots[slotKey] = EnsureSlot(state, slotKey) with
                {
                    EmotionKey = NormalizeFaceToken(Arg("face")),
                    Visible = true
                };
                break;

            case "fade_in" when Slot("slot") is { } slotKey:
                state.Slots[slotKey] = EnsureSlot(state, slotKey) with { Visible = true };
                break;

            case "fade_out" when Slot("slot") is { } slotKey:
                state.Slots[slotKey] = EnsureSlot(state, slotKey) with { Visible = false };
                break;

            case "face_swap" when Slot("slot") is { } slotKey:
                state.Slots[slotKey] = EnsureSlot(state, slotKey) with { EmotionKey = Arg("emotion") };
                break;

            case "face_crossfade" when Slot("slot") is { } slotKey:
                // 캐릭터 키까지 갱신 가능 — 다른 캐릭터로의 전환에도 쓰인다.
                MiniStageSlot current = EnsureSlot(state, slotKey);
                state.Slots[slotKey] = current with
                {
                    CharacterId = Arg("character") ?? current.CharacterId,
                    EmotionKey = Arg("emotion") ?? current.EmotionKey
                };
                break;

            case "mirror" when Slot("slot") is { } slotKey:
                MiniStageSlot mirrored = EnsureSlot(state, slotKey);
                state.Slots[slotKey] = mirrored with
                {
                    Mirrored = ResolveMirror(Arg("mode"), mirrored.Mirrored)
                };
                break;

            case "box_named" when Arg("kind") is { } namedKind:
                state.NamedBoxKind = namedKind;
                break;

            case "box_protagonist" when Arg("kind") is { } protagonistKind:
                state.ProtagonistBoxKind = protagonistKind;
                break;

            case "box_reset":
                state.NamedBoxKind = MiniStageState.DefaultNamedBoxKind;
                state.ProtagonistBoxKind = MiniStageState.DefaultProtagonistBoxKind;
                break;

            default:
                // 인식 못 한 것도, 인식했지만 필수 인자가 비어 적용 못 한 것도 여기로 온다.
                state.Unhandled.Add(new MiniStageUnhandled(lineId, definition.OutputCommandName));
                break;
        }
    }

    private static MiniStageSlot EnsureSlot(FoldState state, string slotKey)
    {
        // 런타임은 slot 없이 cast하면 경고 후 무시하지만(§4), v1 프리뷰는 관대하게
        // 슬롯을 만들어 보여 준다 — 정보가 목표라서, 순서 실수는 유니티 게이트가 잡는다.
        if (!state.Slots.TryGetValue(slotKey, out MiniStageSlot? slot))
        {
            slot = new MiniStageSlot(null, null, null, Visible: false, Mirrored: false);
            state.Slots[slotKey] = slot;
        }

        return slot;
    }

    /// <summary>ShowFaceAliasParser 이식: e1→1, emo2→2, face3→3, emotion4→4.</summary>
    private static string? NormalizeFaceToken(string? faceToken)
    {
        if (faceToken is null)
        {
            return null;
        }

        foreach (string prefix in (string[])["emotion", "face", "emo", "e"])
        {
            if (faceToken.StartsWith(prefix, StringComparison.Ordinal) &&
                faceToken.Length > prefix.Length &&
                faceToken[prefix.Length..].All(char.IsAsciiDigit))
            {
                return faceToken[prefix.Length..];
            }
        }

        return faceToken;
    }

    /// <summary>CharacterMirrorModeParser 이식. 무인자·그 외 토큰은 전부 토글이다.</summary>
    private static bool ResolveMirror(string? mode, bool current)
    {
        return mode switch
        {
            "left" or "l" or "mirror" or "mirrored" or "true" or "1" => true,
            "right" or "r" or "normal" or "default" or "unmirror" or "unmirrored" or "false" or "0" => false,
            _ => !current
        };
    }
}
