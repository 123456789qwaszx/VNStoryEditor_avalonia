using Vn.Authoring.Definition;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;

namespace Vn.Authoring.Editing;

/// <summary>
/// 프리뷰 직접 조작(클릭·드래그)을 편집 명령으로 바꾸는 규칙.
///
/// 조작 어휘 = 폴드가 인식하는 22종이다. 모든 조작은 "선택된 라인의 바인딩에 커맨드
/// 추가/수정"으로 귀결되고 전부 <see cref="ProjectEditor"/>를 지난다 — 프리뷰용 별도
/// 쓰기 경로는 없고, 되돌리기 한 번이 조작 하나를 원복한다.
///
/// 툴이 마법을 부리지 않는다: 캐스팅 시퀀스는 매크로가 아니라 개별 커맨드들이고,
/// 그대로 바인딩에 보인다. 표준 등장 시퀀스를 대신 타이핑해 줄 뿐이다(원칙 §2.3).
/// </summary>
public static class PresentationStageActions
{
    public sealed record Applied(PresentationCommandInstance Command, bool Updated);

    /// <summary>
    /// outputCommand 하나를 라인에 반영한다. 그 라인에 <b>같은 outputCommand + 같은
    /// 대상</b>인 커맨드가 이미 있으면 추가하지 않고 인자를 수정한다 —
    /// 표정을 두 번 고르면 face_swap이 두 개가 되는 게 아니라 값이 바뀐다.
    /// </summary>
    public static Applied Apply(
        ProjectEditor editor,
        PresentationCommandCatalog catalog,
        string presentationNodeId,
        string lineId,
        string outputCommand,
        IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(arguments);

        PresentationCommandDefinition definition = catalog.FindByOutputCommand(outputCommand)
            ?? throw new InvalidOperationException($"카탈로그에 없는 커맨드입니다: '{outputCommand}'");

        PresentationNode node = editor.Project.FindPresentation(presentationNodeId)
            ?? throw new InvalidOperationException($"'{presentationNodeId}'는 연출 노드가 아닙니다.");

        string? target = TargetOf(definition, arguments);
        PresentationCommandInstance? existing = node.FindBinding(lineId)?.Commands
            .FirstOrDefault(command =>
                catalog.Find(command.DefinitionId) is { } commandDefinition &&
                string.Equals(commandDefinition.OutputCommandName, outputCommand, StringComparison.Ordinal) &&
                string.Equals(TargetOf(commandDefinition, command.Arguments), target, StringComparison.Ordinal));

        if (existing is not null)
        {
            editor.UpdatePresentationCommandArguments(presentationNodeId, existing.Id, arguments);
            return new Applied(existing, Updated: true);
        }

        PresentationCommandInstance added = editor.AddPresentationCommand(
            presentationNodeId, lineId, definition.Id, arguments);
        return new Applied(added, Updated: false);
    }

    /// <summary>
    /// 무대에 없는 캐릭터를 세우는 표준 등장 시퀀스 — slot → cast → fade_in.
    /// 폴드가 이미 아는 슬롯이면 slot은 생략된다(리그 재생성 방지).
    /// 개별 커맨드로 저장되고, 되돌리기 한 번에 전부 원복된다.
    /// </summary>
    public static IReadOnlyList<PresentationCommandInstance> ApplyCastingSequence(
        ProjectEditor editor,
        PresentationCommandCatalog catalog,
        MiniStageState stateAtLine,
        string presentationNodeId,
        string lineId,
        string slotKey,
        string characterId,
        string? variantKey = null,
        string? emotionKey = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(stateAtLine);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);

        string RequireId(string outputCommand) =>
            (catalog.FindByOutputCommand(outputCommand)
                ?? throw new InvalidOperationException($"카탈로그에 없는 커맨드입니다: '{outputCommand}'")).Id;

        var commands = new List<(string DefinitionId, IReadOnlyDictionary<string, string> Arguments)>();

        if (!stateAtLine.Slots.ContainsKey(slotKey))
        {
            commands.Add((RequireId("slot"), Args(("slotKey", slotKey))));
        }

        var castArguments = new List<(string, string)> { ("slot", slotKey), ("characterKey", characterId) };

        if (!string.IsNullOrWhiteSpace(variantKey))
        {
            castArguments.Add(("variantKey", variantKey));
        }

        if (!string.IsNullOrWhiteSpace(emotionKey))
        {
            castArguments.Add(("emotionKey", emotionKey));
        }

        commands.Add((RequireId("cast"), Args(castArguments.ToArray())));
        commands.Add((RequireId("fade_in"), Args(("slot", slotKey))));

        return editor.AddPresentationCommands(presentationNodeId, lineId, commands);
    }

    /// <summary>
    /// 배경 선택이 만들 커맨드 — 지나온 무대에 배경 리그가 있으면 그 리그에
    /// <c>bg_sprite</c>, 첫 배경이면 <c>bg_spawn</c>(기본 리그 <c>bg0</c>)이다.
    /// </summary>
    public static (string OutputCommand, IReadOnlyDictionary<string, string> Arguments) BackgroundCommandFor(
        MiniStageState stateAtLine,
        string spriteKey)
    {
        ArgumentNullException.ThrowIfNull(stateAtLine);
        ArgumentException.ThrowIfNullOrWhiteSpace(spriteKey);

        return stateAtLine.BackgroundRigKey is { } rigKey
            ? ("bg_sprite", Args(("rigKey", rigKey), ("spriteKey", spriteKey)))
            : ("bg_spawn", Args(("rigKey", "bg0"), ("spriteKey", spriteKey)));
    }

    /// <summary>
    /// 초상화(캐릭터/표정)를 라인에 반영할 때의 커맨드 — 무대 위 여부가 가른다:
    /// 보이는 캐릭터는 <c>face_swap</c>, 캐스팅만 된 캐릭터는 <c>face</c>,
    /// 무대에 없으면 null(캐스팅 시퀀스가 필요하다는 뜻).
    /// </summary>
    public static (string OutputCommand, string SlotKey)? FaceCommandFor(
        MiniStageState stateAtLine,
        string characterId)
    {
        ArgumentNullException.ThrowIfNull(stateAtLine);

        KeyValuePair<string, MiniStageSlot>? slot = stateAtLine.Slots
            .Where(item => string.Equals(item.Value.CharacterId, characterId, StringComparison.Ordinal))
            .OrderByDescending(item => item.Value.Visible)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Cast<KeyValuePair<string, MiniStageSlot>?>()
            .FirstOrDefault();

        if (slot is not { } found)
        {
            return null;
        }

        return (found.Value.Visible ? "face_swap" : "face", found.Key);
    }

    /// <summary>커맨드의 "대상" — 무대 대상 타입(slot/alias)인 첫 파라미터의 값. 없으면 null.</summary>
    private static string? TargetOf(
        PresentationCommandDefinition definition,
        IReadOnlyDictionary<string, string> arguments)
    {
        PresentationCommandParameter? parameter = definition.Parameters
            .FirstOrDefault(item => ArgumentTokenCandidates.IsStageTargetType(item.Type));

        if (parameter is null)
        {
            return null;
        }

        return arguments.TryGetValue(parameter.Name, out string? value) ? value : parameter.Default;
    }

    private static IReadOnlyDictionary<string, string> Args(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
