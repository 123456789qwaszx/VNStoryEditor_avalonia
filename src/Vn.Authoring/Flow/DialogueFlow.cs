using Vn.Authoring.Model;

namespace Vn.Authoring.Flow;

/// <summary>
/// 계산된 조건 갈래 하나.
///
/// 저장되는 것이 아니라 <see cref="ConditionFlowResolver"/>가 줄들을 훑어서 만들어 낸다.
/// 갈래를 모델에 따로 저장하지 않는 이유는, 저장하면 줄의 전환 정보와 갈래 목록이
/// 같은 사실을 두 벌 들고 있게 되어 편집할 때마다 어긋나기 때문이다.
/// </summary>
/// <param name="OpenLineId">이 갈래를 여는 줄의 Id. 갈래의 정체성이자 출구를 매다는 자리다.</param>
/// <param name="ChainIndex">노드 안에서 몇 번째 체인인지(조건·선택 공용 일련번호).</param>
/// <param name="BranchIndexInChain">체인 안에서 몇 번째 갈래인지. 0이 <c>if</c>(또는 첫 옵션), 1부터 <c>elseif</c>(다음 옵션).</param>
/// <param name="PaletteIndex">노드 안에서 몇 번째로 나타난 갈래인지. 색을 고르는 데 쓴다.</param>
/// <param name="LastLineIndex">이 갈래의 마지막 줄. 출구가 있다면 이 줄에 표시된다.</param>
/// <param name="OptionId">선택 갈래일 때만 있는 옵션의 안정 식별자.</param>
public sealed record ConditionBranch(
    string OpenLineId,
    string ConditionId,
    int ChainIndex,
    int BranchIndexInChain,
    int PaletteIndex,
    int FirstLineIndex,
    int LastLineIndex,
    string? ExitTargetNodeId,
    string? OptionId = null)
{
    public bool HasExit => ExitTargetNodeId is not null;

    /// <summary>선택 블록의 옵션 갈래인지. 조건 갈래와 같은 기계로 계산되지만 의미가 다르다.</summary>
    public bool IsChoice => OptionId is not null;
}

/// <summary>
/// 줄 하나가 계산 결과 어디에 속하는지.
/// </summary>
/// <param name="Branch">
/// 이 줄이 속한 갈래. 전환을 적용한 <em>뒤</em>의 상태다.
/// 그래서 <c>BeginIf</c> 줄은 새 갈래 안에 있고, <c>EndIf</c> 줄은 이미 바깥이다.
/// </param>
/// <param name="PrecedingBranch">
/// 전환을 적용하기 <em>전</em>의 상태. 조건 드롭다운이 무엇을 제시할지는 이것으로 정해진다.
/// </param>
/// <param name="Depth">첫 버전에서는 0 또는 1이다. <c>elseif</c>로 바뀌어도 늘지 않는다.</param>
/// <param name="IsBranchExit">이 줄이 자기 갈래의 마지막이고 그 갈래에 출구가 있는지.</param>
public sealed record ResolvedLine(
    DialogueLine Line,
    int Index,
    int Depth,
    ConditionBranch? Branch,
    ConditionBranch? PrecedingBranch,
    bool IsBranchExit);

public enum FlowProblemKind
{
    /// <summary>이 대사 노드가 읽을 대본을 아직 고르지 않았거나 대본이 사라졌다.</summary>
    MissingScript,

    /// <summary>대본에 없는 LineId에 조건 전환이 남아 있다. 지우지 않고 알린다.</summary>
    OrphanedLineExtension,

    /// <summary>조건 안에서 다시 <c>if</c>를 열었다. 첫 버전은 중첩을 지원하지 않는다.</summary>
    NestedCondition,

    /// <summary>열린 조건이 없는데 <c>elseif</c>가 나왔다.</summary>
    ElseIfWithoutIf,

    /// <summary>열린 조건이 없는데 <c>endif</c>가 나왔다.</summary>
    EndIfWithoutIf,

    /// <summary>여는 전환이 가리키는 조건 정의가 프로젝트나 게임 정의에 없다.</summary>
    UnknownCondition,

    /// <summary>조건 정의는 남아 있지만 현재 DialogueNode의 Settings 범위에는 없다.</summary>
    UnavailableCondition,

    /// <summary>더 이상 갈래를 열지 않는 줄에 조건 출구가 매달려 있다.</summary>
    OrphanedBranchExit,

    /// <summary>출구가 가리키는 노드가 프로젝트에 없다.</summary>
    MissingExitTarget,

    /// <summary>조건 체인과 선택 체인이 겹쳤다. Phase 1은 두 체인의 중첩을 지원하지 않는다.</summary>
    MixedChain,

    /// <summary>열린 선택 블록이 없는데 다음 옵션·선택 종료가 나왔다.</summary>
    OptionWithoutChoice
}

/// <param name="LineId">문제가 걸린 줄. 노드 전체의 문제면 null이다.</param>
public sealed record FlowProblem(FlowProblemKind Kind, string? LineId, string Message);

/// <summary>
/// 한 <see cref="DialogueNode"/>를 훑은 결과 전체.
/// 화면·그래프·검증이 모두 이 하나를 본다. 각자 다시 계산하면 서로 다른 말을 하게 된다.
/// </summary>
public sealed class DialogueFlow
{
    public DialogueFlow(
        DialogueScript script,
        IReadOnlyList<ResolvedLine> lines,
        IReadOnlyList<ConditionBranch> branches,
        IReadOnlyList<FlowProblem> problems)
    {
        Script = script;
        Lines = lines;
        Branches = branches;
        Problems = problems;
    }

    /// <summary>이 흐름을 계산한 대본 투영. 화면은 여기서 화자·대사와 고아 목록을 얻는다.</summary>
    public DialogueScript Script { get; }

    public IReadOnlyList<ResolvedLine> Lines { get; }

    /// <summary>나타난 순서대로의 갈래 목록. 그래프의 조건 출력 포트가 이것과 1:1이다.</summary>
    public IReadOnlyList<ConditionBranch> Branches { get; }

    public IReadOnlyList<FlowProblem> Problems { get; }

    public ConditionBranch? FindBranch(string openLineId)
    {
        return Branches.FirstOrDefault(
            branch => string.Equals(branch.OpenLineId, openLineId, StringComparison.Ordinal));
    }
}
