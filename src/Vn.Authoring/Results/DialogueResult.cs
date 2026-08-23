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
    IReadOnlyList<DialogueResultSetOperation>? SetOperations = null,
    IReadOnlyList<DialogueResultTransition>? ExtraTransitions = null)
{
    public IReadOnlyList<DialogueResultSetOperation> Sets =>
        SetOperations ?? Array.Empty<DialogueResultSetOperation>();

    /// <summary>
    /// 이 줄 앞에서 일어나는 전환들 — <b>순서가 곧 일어나는 순서</b>다 (2026-08-17).
    ///
    /// Yarn에는 전환만 있는 줄이 없어서(<c>&lt;&lt;endif&gt;&gt;</c>는 대사가 아니다) 블록이
    /// 겹쳐 닫히거나 닫히자마자 다음이 열리면 그 전환들이 전부 다음 대사 줄 앞에 몰린다.
    /// <see cref="Transition"/>은 그 첫 칸이고, 나머지가 <see cref="ExtraTransitions"/>다 —
    /// 옛 결과 파일이 그대로 열리도록 첫 칸의 이름과 자리를 그대로 뒀다.
    /// </summary>
    public IReadOnlyList<DialogueResultTransition> Transitions =>
        Transition is null
            ? Array.Empty<DialogueResultTransition>()
            : [Transition, .. ExtraTransitions ?? Array.Empty<DialogueResultTransition>()];
}

/// <summary>발행 시점에 얼린 변수 변경 하나. Yarn <c>&lt;&lt;set&gt;&gt;</c>이 된다.</summary>
public sealed record DialogueResultSetOperation(
    string Variable,
    SetOperatorKind Operator,
    string Value);

/// <summary>
/// 발행 시점의 조건·선택 전환. 조건의 이름과 식까지 함께 얼린다.
/// 나중에 SetNode에서 조건을 지워도 이미 발행한 결과는 그대로 재현되어야 한다.
/// </summary>
/// <param name="OptionId">옵션 라벨 전환에만 있다. 순서 안정성 경고(계약서 C3)의 기준이다.</param>
/// <param name="ExitTargetNodeId">
/// 이 전환이 <b>여는</b> 갈래의 출구 (v4, 2026-08-24) — 조건 갈래면 detour, 옵션이면 jump.
///
/// ⚠ 줄에도 <see cref="DialogueResultLine.BranchExitTargetNodeId"/>가 있지만 그것은
/// <b>줄당 하나</b>다. 줄 없는 갈래(대본 끝의 빈 블록)에는 실을 줄 자체가 없어서, 출구가
/// <b>전환에</b> 붙어야 결과 문서가 자기 완결이 된다. 줄에 실린 쪽은 그대로 두었다 —
/// 옛 결과 파일이 그 칸으로 읽히기 때문이다.
/// </param>
public sealed record DialogueResultTransition(
    ConditionTransitionKind Kind,
    string? ConditionId,
    string? ConditionName,
    string? Expression,
    string? OptionId = null,
    string? ExitTargetNodeId = null);

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
    /// <remarks>
    /// v2: 줄에 SetOperations가 실린다. v3: 선택 전환(OptionId)이 실린다.
    /// v4: <see cref="TrailingTransitions"/> — 마지막 줄 뒤의 전환(대사 없는 조건 블록).
    /// </remarks>
    public const int CurrentSchemaVersion = 4;

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
        DateTimeOffset publishedAt,
        IReadOnlyList<DialogueResultTransition>? trailingTransitions = null)
    {
        TrailingTransitions = trailingTransitions ?? Array.Empty<DialogueResultTransition>();
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

    /// <summary>
    /// <b>마지막 줄 뒤의 전환들</b> (v4, 2026-08-24) — 대사 없는 조건 블록이 대본의 끝일 때.
    ///
    /// ⚠ <b>결과 문서에 실어야 한다.</b> 노드에서 그때그때 읽어 오면 결과가 자기 완결이
    /// 아니게 되고, 무엇보다 <b>꼬리만 다른 두 노드가 같은 해시</b>를 갖는다 — 발행 비교가
    /// 그 차이를 못 본다.
    /// </summary>
    public IReadOnlyList<DialogueResultTransition> TrailingTransitions { get; }

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
