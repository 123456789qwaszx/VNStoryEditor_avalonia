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
        PresentationCommandInstance? existing = FindMatch(
            catalog,
            node.FindBinding(lineId)?.Commands ?? Enumerable.Empty<PresentationCommandInstance>(),
            outputCommand,
            target);

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
    /// outputCommand 하나를 노드 수준 Setup 블록에 반영한다. 장면 준비(슬롯 생성·캐스팅)는
    /// 대사 줄이 아니라 노드에 속한다 — 어느 라인의 프리뷰에서 조작했든 같은 자리에 담긴다.
    /// Setup에 <b>같은 outputCommand + 같은 대상</b>이 이미 있으면 추가하지 않고 인자를
    /// 수정한다 — 같은 슬롯을 다시 캐스팅하면 cast가 쌓이는 게 아니라 값이 바뀐다.
    /// </summary>
    public static Applied ApplyToSetup(
        ProjectEditor editor,
        PresentationCommandCatalog catalog,
        string presentationNodeId,
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
        PresentationCommandInstance? existing = FindMatch(
            catalog, node.SetupCommands, outputCommand, target);

        if (existing is not null)
        {
            editor.UpdatePresentationCommandArguments(presentationNodeId, existing.Id, arguments);
            return new Applied(existing, Updated: true);
        }

        PresentationCommandInstance added = editor.AddPresentationSetupCommand(
            presentationNodeId, definition.Id, arguments);
        return new Applied(added, Updated: false);
    }

    /// <summary>
    /// 슬롯의 보이는 상태를 라인에 반영한다 — 원하는 방향은 <c>fade_in</c>/<c>fade_out</c>.
    /// 같은 라인에 <b>반대 방향</b> 커맨드가 남아 있으면 먼저 지운다: 둘이 한 라인에 쌓이면
    /// 저장된 순서가 사용자의 마지막 선택과 어긋날 수 있다(라인 안 커맨드는 순서 재생).
    /// 반대 방향 제거와 반영이 각각 편집 하나라, 그때는 되돌리기가 두 단계다.
    /// </summary>
    public static Applied ApplyVisibility(
        ProjectEditor editor,
        PresentationCommandCatalog catalog,
        string presentationNodeId,
        string lineId,
        string slotKey,
        bool visible)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotKey);

        string desired = visible ? "fade_in" : "fade_out";
        string opposite = visible ? "fade_out" : "fade_in";

        PresentationNode node = editor.Project.FindPresentation(presentationNodeId)
            ?? throw new InvalidOperationException($"'{presentationNodeId}'는 연출 노드가 아닙니다.");
        PresentationCommandInstance? conflicting = FindMatch(
            catalog,
            node.FindBinding(lineId)?.Commands ?? Enumerable.Empty<PresentationCommandInstance>(),
            opposite,
            slotKey);

        if (conflicting is not null)
        {
            editor.RemovePresentationCommand(presentationNodeId, conflicting.Id);
        }

        return Apply(editor, catalog, presentationNodeId, lineId, desired, Args(("slot", slotKey)));
    }

    /// <summary>
    /// 커맨드 여러 개를 <b>순서대로, 한 번의 편집으로</b> 라인에 반영한다
    /// (2026-08-24 — 자주 쓰는 칩이 묶음이 되면서 생긴 통로).
    ///
    /// <b>합치기 규칙은 <see cref="Apply"/>와 같은 하나다</b>: 같은 outputCommand + 같은
    /// 대상이면 쌓지 않고 값을 바꾼다. 그 규칙이 <b>묶음 안에서도</b> 돈다 — 앞 단계가
    /// 방금 만든 커맨드를 뒤 단계가 다시 겨누면 뒤의 것이 이긴다. 그래서
    /// <b>칩을 나눠 누르든 묶어 누르든 결과가 같다</b>. 이 성질이 없으면 묶음은
    /// "커맨드를 여러 개 붙이는 다른 기능"이 되어, 사람이 규칙을 두 벌 외워야 한다.
    ///
    /// 되돌리기 한 번이 단추 한 번을 원복한다 — 단추 하나를 누른 것은 조작 하나다.
    /// </summary>
    public static IReadOnlyList<Applied> ApplyAll(
        ProjectEditor editor,
        PresentationCommandCatalog catalog,
        string presentationNodeId,
        string lineId,
        IReadOnlyList<(string OutputCommand, IReadOnlyDictionary<string, string> Arguments)> steps)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(steps);

        PresentationNode node = editor.Project.FindPresentation(presentationNodeId)
            ?? throw new InvalidOperationException($"'{presentationNodeId}'는 연출 노드가 아닙니다.");

        IReadOnlyList<PresentationCommandInstance> lineCommands =
            node.FindBinding(lineId)?.Commands ?? [];

        var edits = new List<ProjectEditor.PresentationCommandEdit>(steps.Count);
        var slotByTarget = new Dictionary<(string Output, string Target), int>();
        var updated = new List<bool>(steps.Count);

        foreach ((string outputCommand, IReadOnlyDictionary<string, string> arguments) in steps)
        {
            PresentationCommandDefinition definition = catalog.FindByOutputCommand(outputCommand)
                ?? throw new InvalidOperationException($"카탈로그에 없는 커맨드입니다: '{outputCommand}'");

            (string, string) key = (outputCommand, TargetOf(definition, arguments) ?? string.Empty);

            // 묶음 안에서 이미 겨눈 자리 — 새 항목을 만들지 않고 그 자리의 값을 갈아 끼운다.
            if (slotByTarget.TryGetValue(key, out int slot))
            {
                edits[slot] = edits[slot] with { Arguments = arguments };
                continue;
            }

            PresentationCommandInstance? existing = FindMatch(
                catalog, lineCommands, outputCommand, TargetOf(definition, arguments));

            slotByTarget[key] = edits.Count;
            updated.Add(existing is not null);
            edits.Add(new ProjectEditor.PresentationCommandEdit(existing?.Id, definition.Id, arguments));
        }

        IReadOnlyList<PresentationCommandInstance> applied =
            editor.ApplyPresentationCommandBatch(presentationNodeId, lineId, edits);

        return applied.Select((command, index) => new Applied(command, updated[index])).ToList();
    }

    /// <summary>목록에서 같은 outputCommand + 같은 대상인 커맨드를 찾는다 — dedupe 판정의 단일 구현.</summary>
    private static PresentationCommandInstance? FindMatch(
        PresentationCommandCatalog catalog,
        IEnumerable<PresentationCommandInstance> commands,
        string outputCommand,
        string? target)
    {
        return commands.FirstOrDefault(command =>
            catalog.Find(command.DefinitionId) is { } commandDefinition &&
            string.Equals(commandDefinition.OutputCommandName, outputCommand, StringComparison.Ordinal) &&
            string.Equals(TargetOf(commandDefinition, command.Arguments), target, StringComparison.Ordinal));
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
