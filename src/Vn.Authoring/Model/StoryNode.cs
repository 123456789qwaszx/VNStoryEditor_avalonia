namespace Vn.Authoring.Model;

/// <summary>그래프에 배치된 좌표. 파일 순서와는 아무 관계가 없다.</summary>
public sealed class NodeLayout
{
    public double X { get; set; }

    public double Y { get; set; }

    public NodeLayout Clone() => new() { X = X, Y = Y };
}

/// <summary>
/// 그래프에 놓이는 노드의 공통 부분.
///
/// 파일에서의 순서는 소유한 <see cref="StoryFile.Nodes"/> 안의 순서이고,
/// 화면에서의 위치는 <see cref="Layout"/>이다. 둘은 별개의 개념이며 서로를 따라가지 않는다.
/// </summary>
public abstract class StoryNode
{
    protected StoryNode(string? id, string name)
    {
        Id = id ?? Identifier.Node();
        Name = name;
    }

    public string Id { get; }

    /// <summary>작가가 읽고 바꾸는 이름. 식별자가 아니므로 중복되어도 연결이 끊어지지 않는다.</summary>
    public string Name { get; set; }

    public NodeLayout Layout { get; set; } = new();

    /// <summary>
    /// 노드가 끝난 뒤 이동할 노드. null이면 이야기가 여기서 멈춘다.
    ///
    /// 종류를 가리지 않고 모든 노드가 하나씩 가진다. 그래서 그래프는 노드가 무엇이든
    /// 기본 출력 포트를 똑같이 그릴 수 있다.
    /// </summary>
    public string? DefaultExitTargetNodeId { get; set; }

    public abstract StoryNode Clone();
}

/// <summary>
/// 뒤따르는 노드들이 쓸 값과 조건을 준비하는 노드.
///
/// 조건은 여기(또는 게임 정의 파일)에서만 만들어진다. 작가가 대사 줄마다 조건식을 직접
/// 타이핑하지 않고 준비된 것 중에서 고르게 하려는 것이다. 그래야 같은 조건이 이름 하나로
/// 묶이고, 그래프 간선에도 그 이름을 그대로 보여 줄 수 있다.
///
/// 변수 이름과 값의 의미는 게임마다 다르다. 그래서 이 클래스는 <c>favor</c> 같은 실제 이름을
/// 하나도 알지 못한다. 무엇을 고를 수 있는지는 게임 정의 파일이 공급한다.
/// </summary>
public sealed class SetNode : StoryNode
{
    public SetNode(string? id = null, string name = "새 설정")
        : base(id, name)
    {
    }

    /// <summary>이 노드를 지날 때 적용되는 값. 게임이 해석한다.</summary>
    public List<VariableAssignment> Assignments { get; init; } = new();

    /// <summary>이 노드가 이후 대사 노드에 공급하는 조건들.</summary>
    public List<ConditionDefinition> Conditions { get; init; } = new();

    public override StoryNode Clone()
    {
        return new SetNode(Id, Name)
        {
            Layout = Layout.Clone(),
            DefaultExitTargetNodeId = DefaultExitTargetNodeId,
            Assignments = Assignments.Select(item => item.Clone()).ToList(),
            Conditions = Conditions.Select(item => item.Clone()).ToList()
        };
    }
}

/// <summary>변수 하나에 값을 넣는다. 값은 게임이 해석하므로 문자열로 들고 있는다.</summary>
public sealed class VariableAssignment
{
    public string Variable { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public VariableAssignment Clone() => new() { Variable = Variable, Value = Value };
}

/// <summary>
/// 작가가 고를 수 있는 조건 하나.
///
/// <see cref="Name"/>은 작가가 읽는 이름이고 그래프 간선에도 이것이 표시된다.
/// <see cref="Expression"/>은 게임이 평가할 식이며 VnTool은 내용을 해석하지 않는다.
/// 이 분리가 있어야 게임마다 다른 식을 쓰면서도 저작 화면은 똑같이 동작한다.
/// </summary>
public sealed class ConditionDefinition
{
    public ConditionDefinition(string? id = null)
    {
        Id = id ?? Identifier.Condition();
    }

    public string Id { get; }

    public string Name { get; set; } = string.Empty;

    public string Expression { get; set; } = string.Empty;

    public ConditionDefinition Clone() =>
        new(Id) { Name = Name, Expression = Expression };
}

/// <summary>
/// 위에서 아래로 쌓인 <see cref="LineBox"/>들과 출구를 가진 노드.
///
/// 출구는 두 종류다.
///   기본 출구   — 모든 줄과 조건 체인이 끝난 뒤 이동한다. 노드 전체가 소유한다.
///   조건 갈래 출구 — 특정 조건 갈래를 지났을 때만 이동한다.
///
/// <b>조건 갈래 출구는 갈래를 여는 줄의 Id로 기억한다.</b> 갈래의 마지막 줄에 매달면
/// 줄을 하나 추가하는 순간 출구가 갈래 중간에 파묻히고, 그 아래 대사가 실행되는
/// 모순된 구조가 된다. 여는 줄은 갈래 그 자체이므로 줄을 아무리 넣고 옮겨도
/// 출구는 언제나 그 갈래의 것으로 남고, 화면에는 항상 갈래의 현재 마지막 줄에 표시된다.
/// </summary>
public sealed class DialogueNode : StoryNode
{
    public DialogueNode(string? id = null, string name = "새 대사")
        : base(id, name)
    {
    }

    public List<LineBox> Lines { get; init; } = new();

    /// <summary>갈래를 여는 줄의 Id → 그 갈래가 끝났을 때 이동할 노드.</summary>
    public Dictionary<string, string> BranchExits { get; init; } = new(StringComparer.Ordinal);

    public override StoryNode Clone()
    {
        return new DialogueNode(Id, Name)
        {
            Layout = Layout.Clone(),
            Lines = Lines.Select(line => line.Clone()).ToList(),
            DefaultExitTargetNodeId = DefaultExitTargetNodeId,
            BranchExits = new Dictionary<string, string>(BranchExits, StringComparer.Ordinal)
        };
    }
}
