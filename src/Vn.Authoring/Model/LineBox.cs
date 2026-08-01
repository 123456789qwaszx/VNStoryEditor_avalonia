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
}

/// <summary>
/// 작가가 보는 가장 작은 서사 단위. 화면의 카드 하나에 대응한다.
///
/// 화면에 보이는 순서(Index)는 목록에서의 위치일 뿐이고, 이 줄의 정체성은 <see cref="Id"/>다.
/// 줄을 위아래로 옮겨도 조건 갈래 출구와 그래프 간선이 따라오는 것은 이 구분 덕분이다.
///
/// 앞으로 변수 변경·이벤트·BGM·연출 같은 것이 이 카드에 붙는다.
/// 지금 그것을 위한 범용 플러그인 층을 만들지는 않는다. 필요해질 때 여기에 속성을 더한다.
/// </summary>
public sealed class LineBox
{
    public LineBox(string? id = null)
    {
        Id = id ?? Identifier.Line();
    }

    public string Id { get; }

    public string Speaker { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 이 줄에서 조건 흐름이 바뀐다면 그 내용. null이면 앞 줄의 상태를 그대로 물려받는다.
    /// </summary>
    public LineConditionTransition? Transition { get; set; }

    public LineBox Clone()
    {
        return new LineBox(Id)
        {
            Speaker = Speaker,
            Text = Text,
            Transition = Transition is null
                ? null
                : new LineConditionTransition(Transition.Kind, Transition.ConditionId)
        };
    }
}
