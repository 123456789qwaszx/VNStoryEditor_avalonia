using Vn.Authoring.Model;

namespace Vn.Authoring.Results;

/// <summary>
/// 발행된 줄 하나. 대사 본문까지 담고 있어 원본 대본 없이도 재현된다.
///
/// 결과가 대본을 참조만 하면 나중에 대본을 고쳤을 때 v1이 함께 바뀐다. 그러면 불변이 아니다.
/// 그래서 결과는 그 시점의 화자·대사를 <b>복사해서 얼린다.</b> 이것은 §4.3이 금지하는
/// "수정 가능한 복사본"이 아니다. 결과는 어디에서도 수정할 수 없다.
/// </summary>
/// <param name="Revision">발행 당시 대본 줄의 개정 번호.</param>
/// <param name="BranchExitTargetNodeId">이 줄이 연 갈래가 끝났을 때 이동할 노드.</param>
/// <param name="SetOperations">이 줄에 도달했을 때 실행할 변수 변경. 순서가 실행 순서다.</param>
public sealed record DialogueResultLine(
    int Index,
    string LineId,
    int Revision,
    string CharacterName,
    string Text,
    DialogueResultTransition? Transition = null,
    string? BranchExitTargetNodeId = null,
    IReadOnlyList<DialogueResultSetOperation>? SetOperations = null)
{
    public IReadOnlyList<DialogueResultSetOperation> Sets =>
        SetOperations ?? Array.Empty<DialogueResultSetOperation>();
}

/// <summary>발행 시점에 얼린 변수 변경 하나. Yarn <c>&lt;&lt;set&gt;&gt;</c>이 된다.</summary>
public sealed record DialogueResultSetOperation(
    string Variable,
    SetOperatorKind Operator,
    string Value);

/// <summary>
/// 발행 시점의 조건 전환. 조건의 이름과 식까지 함께 얼린다.
/// 나중에 SetNode에서 조건을 지워도 이미 발행한 결과는 그대로 재현되어야 한다.
/// </summary>
public sealed record DialogueResultTransition(
    ConditionTransitionKind Kind,
    string? ConditionId,
    string? ConditionName,
    string? Expression);

/// <summary>발행 시점에 연결되어 있던 SetNode의 변수 값.</summary>
public sealed record DialogueResultAssignment(string Variable, string Value);

/// <summary>
/// DialogueNode의 작업 상태를 얼린 <b>불변</b> 결과.
///
/// 발행한 뒤 작업 노드를 아무리 고쳐도 이 객체는 바뀌지 않는다. 새 내용은 v2가 된다.
/// PresentationNode와 RuntimeComposition이 참조하는 것은 언제나 이 결과이지 작업 노드가 아니다.
///
/// <b>이 결과는 PresentationResult를 소유하지 않는다.</b> 소유하면 대사와 연출이 서로를
/// 가리키는 순환이 생기고, 어느 쪽을 먼저 발행해야 하는지 답할 수 없게 된다.
/// 둘을 잇는 것은 <see cref="RuntimeComposition"/> 하나뿐이다.
/// </summary>
public sealed class DialogueResult
{
    /// <summary>결과 본문의 구조가 바뀌면 올린다. 해시와 함께 호환성 판정에 쓰인다.</summary>
    /// <remarks>v2: 줄에 SetOperations가 실린다.</remarks>
    public const int CurrentSchemaVersion = 2;

    public DialogueResult(
        ResultIdentity identity,
        string sourceNodeId,
        string sourceNodeName,
        string? sourceScriptId,
        int sourceScriptRevision,
        string locale,
        IReadOnlyList<DialogueResultLine> lines,
        IReadOnlyList<DialogueResultAssignment> assignments,
        string? defaultExitTargetNodeId,
        DateTimeOffset publishedAt)
    {
        Identity = identity;
        SourceNodeId = sourceNodeId;
        SourceNodeName = sourceNodeName;
        SourceScriptId = sourceScriptId;
        SourceScriptRevision = sourceScriptRevision;
        Locale = locale;
        Lines = lines;
        Assignments = assignments;
        DefaultExitTargetNodeId = defaultExitTargetNodeId;
        PublishedAt = publishedAt;
    }

    public ResultIdentity Identity { get; }

    /// <summary>이 결과를 낳은 DialogueNode. 추적용이며 결과의 유효성을 좌우하지 않는다.</summary>
    public string SourceNodeId { get; }

    public string SourceNodeName { get; }

    public string? SourceScriptId { get; }

    public int SourceScriptRevision { get; }

    public string Locale { get; }

    public IReadOnlyList<DialogueResultLine> Lines { get; }

    public IReadOnlyList<DialogueResultAssignment> Assignments { get; }

    public string? DefaultExitTargetNodeId { get; }

    public DateTimeOffset PublishedAt { get; }

    public DialogueResultLine? FindLine(string? lineId)
    {
        return lineId is null
            ? null
            : Lines.FirstOrDefault(line => string.Equals(line.LineId, lineId, StringComparison.Ordinal));
    }

    public bool ContainsLine(string lineId) => FindLine(lineId) is not null;
}
