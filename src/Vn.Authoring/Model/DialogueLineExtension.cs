namespace Vn.Authoring.Model;

/// <summary>
/// 조건 흐름을 바꾸는 지점의 종류.
/// </summary>
public enum ConditionTransitionKind
{
    /// <summary>조건 바깥에서 새 조건 갈래를 연다.</summary>
    BeginIf,

    /// <summary>조건 안에서 같은 깊이의 다른 갈래로 넘어간다. 깊이는 늘지 않는다.</summary>
    BeginElseIf,

    /// <summary>조건 체인을 닫는다. 이 줄부터 다시 일반 흐름이다.</summary>
    EndIf
}

/// <summary>
/// 이 줄에서 조건 흐름이 어떻게 바뀌는지.
///
/// 줄마다 "지금 어떤 조건 안에 있는지"를 반복 저장하지 않는다.
/// 반복 저장하면 같은 사실이 여러 줄에 흩어져 한 줄만 고쳤을 때 서로 어긋난다.
/// 대신 <b>바뀌는 지점만</b> 기록하고, 나머지는 앞에서부터 계산한다.
/// 계산하는 쪽은 <see cref="Flow.ConditionFlowResolver"/>다.
/// </summary>
public sealed class LineConditionTransition
{
    public LineConditionTransition(ConditionTransitionKind kind, string? conditionId = null)
    {
        Kind = kind;
        ConditionId = kind == ConditionTransitionKind.EndIf ? null : conditionId;
    }

    public ConditionTransitionKind Kind { get; }

    /// <summary>여는 전환에만 있다. <see cref="ConditionTransitionKind.EndIf"/>면 언제나 null이다.</summary>
    public string? ConditionId { get; }

    public static LineConditionTransition BeginIf(string conditionId) =>
        new(ConditionTransitionKind.BeginIf, conditionId);

    public static LineConditionTransition BeginElseIf(string conditionId) =>
        new(ConditionTransitionKind.BeginElseIf, conditionId);

    public static LineConditionTransition EndIf() =>
        new(ConditionTransitionKind.EndIf);

    /// <summary>갈래를 여는 전환인지. 여는 전환만 조건 갈래 출구를 가질 수 있다.</summary>
    public bool OpensBranch =>
        Kind is ConditionTransitionKind.BeginIf or ConditionTransitionKind.BeginElseIf;

    public LineConditionTransition Clone() => new(Kind, ConditionId);
}

/// <summary>
/// DialogueNode가 <b>대본 한 줄에 덧붙이는 대사 논리</b>.
///
/// 화자와 대사는 여기 없다. 그것은 <see cref="Script.ScriptDocument"/>가 소유하고,
/// 이 객체는 안정된 <see cref="LineId"/>로 그 줄을 가리키기만 한다. 같은 문장을 고칠 수 있는
/// 자리가 두 곳이 되면 어느 쪽이 진실인지 아무도 답할 수 없게 된다.
///
/// 목록에서의 순서는 의미가 없다. 줄 순서는 언제나 대본이 정한다.
/// 대본에서 사라진 LineId의 확장 데이터도 자동으로 지우지 않는다. 작가가 만든 조건 구조가
/// 말없이 사라지는 것보다 고아로 남아 눈에 띄는 편이 낫다.
///
/// 앞으로 선택지·변수 변경·인라인 이벤트가 붙을 자리도 여기다.
/// </summary>
public sealed class DialogueLineExtension
{
    public DialogueLineExtension(string lineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        LineId = lineId;
    }

    public string LineId { get; }

    /// <summary>
    /// 이 줄에서 조건 흐름이 바뀐다면 그 내용. null이면 앞 줄의 상태를 그대로 물려받는다.
    /// </summary>
    public LineConditionTransition? Transition { get; set; }

    /// <summary>이 확장이 아무것도 담고 있지 않은지. 빈 확장은 저장하지 않는다.</summary>
    public bool IsEmpty => Transition is null;

    public DialogueLineExtension Clone() =>
        new(LineId) { Transition = Transition?.Clone() };
}
