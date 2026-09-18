using Vn.Authoring.Definition;
using Vn.Authoring.Editing;
using Vn.Authoring.Flow;
using Vn.Authoring.Model;

namespace Vn.Authoring.Chapters;

/// <summary>
/// <b>분기 에피소드의 첫머리 조건</b> — 읽고 쓰는 유일한 자리 (2026-09-18).
///
/// <b>왜 여기 사는가</b> (소유자: *"Detour로 조건 분기가 일어나는 마커를 끼워두고 그곳에서
/// 조건과 내용을 추가한다는게 유지보수 측면에서 얼마나 유리한지"*): 부르는 쪽 대본에는
/// <c>&lt;&lt;detour&gt;&gt;</c> 한 줄뿐이고, <b>조건과 내용이 다녀올 곳에 함께</b> 있다.
/// 짝을 맞춰야 하는 <c>If</c>/<c>EndIf</c>가 남의 대본에 흩어지지 않는다.
///
/// ⭐ <b>성립하면 돈다</b>(허가). 분기 자체는 <b>뚫어뒀다면 반드시 한 번 가는 통로</b>이고
/// (<see cref="ChapterBranchMarkers"/>), 막는 것은 다녀온 쪽이다 — 소유자: *"그 통로로 갔더니
/// 조건으로 막혀있다는 게 논리상 맞고 이해하기 편합니다"*.
///
/// <b>어휘는 챕터의 것</b>이다. 기획자가 고르는 것은 <c>조건</c> 시트의 라벨이고, 그것이
/// 줄에 적히는 조건 Id로 바뀌는 길은 <see cref="ChapterBoardSupply"/>가 이미 깔아 둔
/// 공급 설정노드다 — 여기서 새 배관을 만들지 않는다.
/// </summary>
public static class BranchEntryCondition
{
    /// <param name="Label">걸린 챕터 조건의 라벨. 없으면 <c>null</c>(무조건 돈다).</param>
    /// <param name="Editable">우측 패널이 고칠 수 있는 모양인가.</param>
    /// <param name="Blocked">못 고치는 사유 — 사람에게 그대로 보여 준다.</param>
    public sealed record State(string? Label, bool Editable, string? Blocked)
    {
        public static State Refuse(string reason) => new(null, false, reason);
    }

    /// <summary>
    /// 지금 걸려 있는 조건과, 여기서 고쳐도 되는지.
    ///
    /// ⛔ <b>손으로 지은 구조는 안 건드린다.</b> 툴이 아는 모양은 <i>첫 줄에 <c>BeginIf</c>
    /// 하나 + 꼬리에 <c>EndIf</c> 하나</i>뿐이다. 작가가 대사 편집기에서 갈래를 더 얹었다면
    /// 그 구조를 여기서 덮어쓰는 순간 <b>짝이 어긋난 대본</b>이 된다 — 그래서 읽기만 하고
    /// 사유를 말한다.
    /// </summary>
    public static State Read(StoryProject project, string nodeId, GameDefinition? definition = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (project.FindDialogue(nodeId) is not { } node)
        {
            return State.Refuse("그 분기 노드를 찾지 못했습니다.");
        }

        if (project.FindScript(node.ScriptId)?.ActiveLines.FirstOrDefault() is not { } first)
        {
            return State.Refuse("대본이 비어 있어 조건을 걸 자리가 없습니다 — 첫 줄을 먼저 쓰세요.");
        }

        IReadOnlyList<LineConditionTransition> head =
            node.FindExtension(first.Id)?.Transitions ?? [];

        // 아무것도 없다 = 무조건 도는 분기. 가장 흔한 모양이고, 여기서 처음 걸 수 있다.
        if (head.Count == 0 && node.TrailingTransitions.Count == 0)
        {
            return new State(null, true, null);
        }

        if (head is not [{ Kind: ConditionTransitionKind.BeginIf, ConditionId: { } conditionId }] ||
            node.TrailingTransitions is not [{ Kind: ConditionTransitionKind.EndIf }])
        {
            return State.Refuse(
                "이 대본의 조건 구조는 대사 편집기에서 지은 것이라 여기서 고칠 수 없습니다. " +
                "그 노드를 열어서 고치세요.");
        }

        // 공급된 조건의 `이름`이 곧 챕터 조건의 라벨이다 (ChapterBoardSupply).
        return new State(
            AvailableConditionResolver.Resolve(project, nodeId, definition).Find(conditionId)?.Name
                ?? conditionId,
            true,
            null);
    }

    /// <summary>
    /// 조건을 건다(<c>null</c>이면 걷는다). 못 걸면 <b>사유</b>를 돌려주고 아무것도 안 바꾼다.
    ///
    /// ⚠ 라벨은 <b>이 챕터의 조건 시트</b>에 있어야 한다. 비슷한 것을 추측해 잇지 않는다 —
    /// 조건은 기획자가 먼저 만드는 것이고, 툴이 만들어 주면 오타가 조용히 새 조건이 된다.
    /// </summary>
    public static string? Set(
        ProjectEditor editor, string nodeId, string? label, GameDefinition? definition = null)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (Read(editor.Project, nodeId, definition) is { Editable: false } refused)
        {
            return refused.Blocked;
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            editor.SetEntryCondition(nodeId, null);
            return null;
        }

        AvailableCondition? condition = AvailableConditionResolver
            .Resolve(editor.Project, nodeId, definition).Conditions
            .FirstOrDefault(item =>
                item.SourceKind == AvailableConditionSourceKind.ChapterLayer &&
                string.Equals(item.Name, label, StringComparison.Ordinal));

        if (condition is null)
        {
            return $"조건 '{label}'이 이 판에 공급되지 않았습니다 — 챕터의 `조건` 시트에 " +
                   "있는지, 식이 툴이 옮길 수 있는 모양인지 확인하세요.";
        }

        editor.SetEntryCondition(nodeId, condition.Id);

        return null;
    }
}
