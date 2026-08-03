namespace Vn.Authoring.Model;

/// <summary>
/// 조건·선택 흐름을 바꾸는 지점의 종류. 두 체인은 같은 기계로 계산되지만 섞이지 않는다
/// (중첩은 발행 검증에서 거부 — 계약서 D7의 가용 배열 인덱스 문제를 아예 만들지 않는다).
/// </summary>
public enum ConditionTransitionKind
{
    /// <summary>조건 바깥에서 새 조건 갈래를 연다.</summary>
    BeginIf,

    /// <summary>조건 안에서 같은 깊이의 다른 갈래로 넘어간다. 깊이는 늘지 않는다.</summary>
    BeginElseIf,

    /// <summary>조건 체인을 닫는다. 이 줄부터 다시 일반 흐름이다.</summary>
    EndIf,

    /// <summary>이 줄이 선택 블록의 첫 옵션 라벨이다. 줄 본문이 곧 버튼 텍스트다.</summary>
    BeginChoice,

    /// <summary>같은 선택 블록의 다음 옵션 라벨. elseif와 동형이라 깊이는 늘지 않는다.</summary>
    BeginNextOption,

    /// <summary>선택 블록을 닫는다. 이 줄부터 다시 일반 흐름이다.</summary>
    EndChoice
}

/// <summary>
/// 이 줄에서 조건·선택 흐름이 어떻게 바뀌는지.
///
/// 줄마다 "지금 어떤 갈래 안에 있는지"를 반복 저장하지 않는다.
/// 반복 저장하면 같은 사실이 여러 줄에 흩어져 한 줄만 고쳤을 때 서로 어긋난다.
/// 대신 <b>바뀌는 지점만</b> 기록하고, 나머지는 앞에서부터 계산한다.
/// 계산하는 쪽은 <see cref="Flow.ConditionFlowResolver"/>다.
///
/// 전환 슬롯이 하나뿐이므로 "옵션 라벨 라인에 조건 전환이 겹치는" 상태는
/// 구조적으로 존재할 수 없다 (계약서 D7의 Phase 1 결정).
/// </summary>
public sealed class LineConditionTransition
{
    public LineConditionTransition(
        ConditionTransitionKind kind,
        string? conditionId = null,
        string? optionId = null)
    {
        Kind = kind;
        ConditionId = kind is ConditionTransitionKind.BeginIf or ConditionTransitionKind.BeginElseIf
            ? conditionId
            : null;
        OptionId = kind is ConditionTransitionKind.BeginChoice or ConditionTransitionKind.BeginNextOption
            ? optionId
            : null;
    }

    public ConditionTransitionKind Kind { get; }

    /// <summary>조건을 여는 전환에만 있다. 그 외에는 언제나 null이다.</summary>
    public string? ConditionId { get; }

    /// <summary>
    /// 옵션 라벨 전환에만 있는 안정 식별자(<c>op_</c>). 라벨 문구를 고쳐도 변하지 않는다.
    /// 선택지 리플레이는 위치 기반이므로(계약서 C3) 이 Id로 순서 변경을 감지해 경고한다.
    /// </summary>
    public string? OptionId { get; }

    public static LineConditionTransition BeginIf(string conditionId) =>
        new(ConditionTransitionKind.BeginIf, conditionId);

    public static LineConditionTransition BeginElseIf(string conditionId) =>
        new(ConditionTransitionKind.BeginElseIf, conditionId);

    public static LineConditionTransition EndIf() =>
        new(ConditionTransitionKind.EndIf);

    public static LineConditionTransition BeginChoice(string? optionId = null) =>
        new(ConditionTransitionKind.BeginChoice, optionId: optionId);

    public static LineConditionTransition BeginNextOption(string? optionId = null) =>
        new(ConditionTransitionKind.BeginNextOption, optionId: optionId);

    public static LineConditionTransition EndChoice() =>
        new(ConditionTransitionKind.EndChoice);

    /// <summary>갈래를 여는 전환인지. 여는 전환만 갈래 출구를 가질 수 있다.</summary>
    public bool OpensBranch =>
        Kind is ConditionTransitionKind.BeginIf
            or ConditionTransitionKind.BeginElseIf
            or ConditionTransitionKind.BeginChoice
            or ConditionTransitionKind.BeginNextOption;

    /// <summary>선택 체인에 속한 전환인지.</summary>
    public bool IsChoiceKind =>
        Kind is ConditionTransitionKind.BeginChoice
            or ConditionTransitionKind.BeginNextOption
            or ConditionTransitionKind.EndChoice;

    /// <summary>옵션 라벨 전환인지. 라벨 라인의 본문이 버튼 텍스트가 된다.</summary>
    public bool OpensOption =>
        Kind is ConditionTransitionKind.BeginChoice or ConditionTransitionKind.BeginNextOption;

    public LineConditionTransition Clone() => new(Kind, ConditionId, OptionId);
}

/// <summary>변수를 어떻게 바꾸는지. Yarn의 <c>&lt;&lt;set&gt;&gt;</c> 연산자에 대응한다.</summary>
public enum SetOperatorKind
{
    /// <summary><c>$x = value</c></summary>
    Assign,

    /// <summary><c>$x += value</c></summary>
    Add,

    /// <summary><c>$x -= value</c></summary>
    Subtract
}

/// <summary>set 연산자의 표기 문자열은 여기 하나뿐이다. Yarn 표기와 같게 둔다.</summary>
public static class SetOperators
{
    public static string Symbol(SetOperatorKind kind) => kind switch
    {
        SetOperatorKind.Add => "+=",
        SetOperatorKind.Subtract => "-=",
        _ => "="
    };

    public static SetOperatorKind Parse(string? symbol) => symbol switch
    {
        "+=" => SetOperatorKind.Add,
        "-=" => SetOperatorKind.Subtract,
        _ => SetOperatorKind.Assign
    };
}

/// <summary>
/// 이 줄에 도달했을 때 실행할 변수 변경 하나.
///
/// 변수 이름 후보는 게임 정의의 variables가 공급하지만, 후보에 없는 이름도 막지 않는다
/// (편의 기능이 없다고 원고를 못 쓰게 하지 않는다). 값은 문자열 그대로 실어 나른다 —
/// 숫자인지 불리언인지는 게임이 해석한다.
///
/// 세이브 리플레이가 <c>&lt;&lt;set&gt;&gt;</c>을 재실행해 변수를 재구축하므로(계약서 C5)
/// 값은 결정적이어야 한다. 랜덤·외부 상태 참조를 여기 넣지 않는다.
/// </summary>
public sealed class SetOperation
{
    public string Variable { get; set; } = string.Empty;

    public SetOperatorKind Operator { get; set; } = SetOperatorKind.Assign;

    public string Value { get; set; } = string.Empty;

    public SetOperation Clone() => new()
    {
        Variable = Variable,
        Operator = Operator,
        Value = Value
    };
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

    /// <summary>이 줄에 도달했을 때 실행할 변수 변경. 목록 순서가 곧 실행 순서다.</summary>
    public List<SetOperation> SetOperations { get; init; } = new();

    /// <summary>이 확장이 아무것도 담고 있지 않은지. 빈 확장은 저장하지 않는다.</summary>
    public bool IsEmpty => Transition is null && SetOperations.Count == 0;

    public DialogueLineExtension Clone() =>
        new(LineId)
        {
            Transition = Transition?.Clone(),
            SetOperations = SetOperations.Select(operation => operation.Clone()).ToList()
        };
}
