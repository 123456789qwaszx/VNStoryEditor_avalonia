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
    Dialogue
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

        string TextOf(PresentationResultCommand command) => CommandText.Format(
            catalog.Find(command.DefinitionId), command.DefinitionId, command.Arguments);

        // 커맨드의 무대 대상 — 묶음 경계 판정용. 카탈로그 선언(IsStageTargetType) 기준이다.
        string? TargetOf(PresentationResultCommand command)
        {
            PresentationCommandParameter? parameter = catalog.Find(command.DefinitionId)?.Parameters
                .FirstOrDefault(item => ArgumentTokenCandidates.IsStageTargetType(item.Type));

            if (parameter is null)
            {
                return null;
            }

            return command.Arguments.TryGetValue(parameter.Name, out string? value)
                ? value
                : parameter.Default;
        }

        void AddCommandRows(string? lineId, IReadOnlyList<PresentationResultCommand> commands)
        {
            string? previousTarget = null;
            bool first = true;

            foreach (PresentationResultCommand command in commands)
            {
                string? target = TargetOf(command);
                bool startsGroup = !first &&
                    !string.Equals(target, previousTarget, StringComparison.Ordinal);

                rows.Add(new PresentationScriptRow(
                    PresentationScriptRowKind.Command, lineId, command, TextOf(command), startsGroup));

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
