using Vn.Authoring.Definition;
using Vn.Authoring.Results;

namespace Vn.Authoring.Flow;

public enum PresentationScriptRowKind
{
    /// <summary>구획 머리(Setup 등) — 편집 대상이 아니다.</summary>
    SectionHeader,

    /// <summary>연출 커맨드 한 줄 — 왼쪽 점이 상세조절 입구다.</summary>
    Command,

    /// <summary>대사 한 줄 — 클릭이 그 라인 선택이다.</summary>
    Dialogue,

    /// <summary>
    /// 라인 단위 액터 선언 <c>&lt;&lt;actor @2 willow&gt;&gt;</c> (2026-08-21 소유자:
    /// "라인단위로 actor를 캐릭터를 지정해") — 이 라인의 커맨드가 만지는 슬롯이 누구인지
    /// 읽는 사람에게 알린다. 표시 전용이라 편집 대상이 아니다.
    /// </summary>
    Actor
}

/// <param name="LineId">이 행이 속한 라인. Setup 구획은 null.</param>
/// <param name="Command">커맨드 행의 원본 — 점·인라인 편집이 이것을 만진다.</param>
/// <param name="StartsGroup">
/// 앞 커맨드와 무대 대상이 달라 새 묶음이 시작된다 — 소유자의 대본 감각(액터 단위로
/// 차분하게 묶는다. 장차 beat 노드가 이 묶음을 액터 타깃 프리셋으로 저장·재사용한다).
/// 패널은 이 경계에 여백을 준다.
/// </param>
public sealed record PresentationScriptRow(
    PresentationScriptRowKind Kind,
    string? LineId,
    PresentationResultCommand? Command,
    string Text,
    bool StartsGroup = false);

/// <summary>
/// 연출 대본 텍스트 패널의 행 모델 (2026-08-20 소유자: "프리뷰 왼쪽에 텍스트 로그 터미널").
///
/// 시나리오 전체가 텍스트로 미리 적혀 있는 모양 — 이미터가 내는 대본과 같은 순서다:
/// Setup 커맨드가 머리에, 라인마다 그 라인의 커맨드들이 대사 <b>앞</b>에 선다.
/// 행이 커맨드 원본(<see cref="PresentationScriptRow.Command"/>)을 들고 있어서
/// 점 클릭·인라인 편집이 발행 결과가 아니라 <b>작업 중 커맨드</b>를 만진다.
/// 고아 바인딩(발행본에 없는 라인)은 폴드와 같은 이유로 싣지 않는다.
/// </summary>
public static class PresentationScriptModel
{
    public static IReadOnlyList<PresentationScriptRow> Build(
        PresentationCommandCatalog catalog,
        DialogueResult? dialogue,
        IReadOnlyList<PresentationResultCommand> setupCommands,
        IReadOnlyList<PresentationResultBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(setupCommands);
        ArgumentNullException.ThrowIfNull(bindings);

        var rows = new List<PresentationScriptRow>();

        // 커맨드의 무대 대상 파라미터 — 묶음 경계·별칭 치환용. 카탈로그 선언
        // (IsStageTargetType) 기준이다.
        PresentationCommandParameter? TargetParameterOf(PresentationResultCommand command) =>
            catalog.Find(command.DefinitionId)?.Parameters
                .FirstOrDefault(item => ArgumentTokenCandidates.IsStageTargetType(item.Type));

        string? TargetOf(PresentationResultCommand command)
        {
            if (TargetParameterOf(command) is not { } parameter)
            {
                return null;
            }

            return command.Arguments.TryGetValue(parameter.Name, out string? value)
                ? value
                : parameter.Default;
        }

        // 캐스팅 커맨드 판정 — 이름이 아니라 선언으로: 무대 대상 + characterKey 파라미터를
        // 함께 든 커맨드(현 카탈로그에서는 char_rig_cast.cast 하나)가 슬롯의 배역을 정한다.
        (string Slot, string Character)? CastingOf(PresentationResultCommand command)
        {
            PresentationCommandDefinition? definition = catalog.Find(command.DefinitionId);
            PresentationCommandParameter? character = definition?.Parameters
                .FirstOrDefault(item => string.Equals(item.Type, "characterKey", StringComparison.Ordinal));

            if (character is null || TargetOf(command) is not { } slot ||
                !command.Arguments.TryGetValue(character.Name, out string? characterKey))
            {
                return null;
            }

            return (slot, characterKey);
        }

        // 슬롯 키의 별칭 — c2 → @2. 숫자 꼬리가 없으면 키 그대로 @키다.
        static string AliasOf(string slot)
        {
            string digits = new(slot.SkipWhile(ch => !char.IsAsciiDigit(ch))
                .TakeWhile(char.IsAsciiDigit).ToArray());
            return "@" + (digits.Length > 0 ? digits.TrimStart('0').PadLeft(1, '0') : slot);
        }

        // 라인 사이를 흐르는 캐스팅 상태 — Setup부터 순서대로 접는다.
        var casting = new Dictionary<string, string>(StringComparer.Ordinal);

        string TextOf(PresentationResultCommand command, string? alias)
        {
            IReadOnlyDictionary<string, string> arguments = command.Arguments;

            if (alias is not null && TargetParameterOf(command) is { } parameter &&
                arguments.ContainsKey(parameter.Name))
            {
                // 표시만 별칭으로 — 저장된 인자·칩 편집은 슬롯 그대로다. 런타임이
                // 별칭·배역 지정을 슬롯으로 되돌리므로 대본과 같은 읽기 문법이다.
                var substituted = new Dictionary<string, string>(arguments, StringComparer.Ordinal)
                {
                    [parameter.Name] = alias
                };
                arguments = substituted;
            }

            return CommandText.Format(
                catalog.Find(command.DefinitionId), command.DefinitionId, arguments);
        }

        void AddCommandRows(string? lineId, IReadOnlyList<PresentationResultCommand> commands)
        {
            bool declareActors = lineId is not null;

            // 이 라인이 스스로 정하는 배역을 먼저 본다 — cast 다음 move가 같은 라인에
            // 있어도 선언 줄에는 이미 그 배역이 적힌다.
            var lineCasting = new Dictionary<string, string>(casting, StringComparer.Ordinal);

            if (declareActors)
            {
                foreach (PresentationResultCommand command in commands)
                {
                    if (CastingOf(command) is { } cast)
                    {
                        lineCasting[cast.Slot] = cast.Character;
                    }
                }
            }

            var declared = new HashSet<string>(StringComparer.Ordinal);
            string? previousTarget = null;
            bool first = true;

            foreach (PresentationResultCommand command in commands)
            {
                string? target = TargetOf(command);
                bool boundary = !first &&
                    !string.Equals(target, previousTarget, StringComparison.Ordinal);
                bool startsGroup = boundary;
                string? alias = null;

                if (declareActors && target is not null)
                {
                    alias = AliasOf(target);

                    if (declared.Add(target))
                    {
                        // 액터 선언이 묶음 여백을 대신 진다 — 커맨드 행과 겹으로 벌어지지 않게.
                        rows.Add(new PresentationScriptRow(
                            PresentationScriptRowKind.Actor, lineId, null,
                            $"<<actor {alias} {lineCasting.GetValueOrDefault(target, target)}>>",
                            boundary));
                        startsGroup = false;
                    }
                }

                rows.Add(new PresentationScriptRow(
                    PresentationScriptRowKind.Command, lineId, command, TextOf(command, alias),
                    startsGroup));

                if (CastingOf(command) is { } applied)
                {
                    casting[applied.Slot] = applied.Character;
                }

                previousTarget = target;
                first = false;
            }
        }

        if (setupCommands.Count > 0)
        {
            rows.Add(new PresentationScriptRow(
                PresentationScriptRowKind.SectionHeader, null, null, "── Setup ──"));
            AddCommandRows(lineId: null, setupCommands);
        }

        foreach (DialogueResultLine line in dialogue?.Lines ?? [])
        {
            PresentationResultBinding? binding = bindings.FirstOrDefault(item =>
                !item.IsOrphan && string.Equals(item.LineId, line.LineId, StringComparison.Ordinal));

            AddCommandRows(line.LineId, binding?.Commands ?? []);

            string speaker = string.IsNullOrWhiteSpace(line.CharacterName) ? "" : $"{line.CharacterName}: ";
            rows.Add(new PresentationScriptRow(
                PresentationScriptRowKind.Dialogue, line.LineId, null, speaker + line.Text));
        }

        return rows;
    }
}
