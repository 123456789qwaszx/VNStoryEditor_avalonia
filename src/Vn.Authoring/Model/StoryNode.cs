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
    /// 실행 가능한 DialogueNode와 SetNode가 사용한다. PresentationNode는 실행 흐름에
    /// 참여하지 않고 Presentation link만 가지므로 이 값을 사용하지 않는다.
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

/// <summary>
/// PresentationNode에 커맨드 범주와 프리셋을 공급하는 노드.
///
/// 커맨드 전체를 평평한 드롭다운으로 주지 않고, "카메라 노드"·"캐릭터 연출 노드"처럼
/// 역할 묶음을 연결해야 해당 커맨드군이 보이게 한다. 어떤 범주 묶음을 무엇이라 부를지는
/// 데이터다 — 이 노드가 담는 것은 카탈로그의 범주 Id 집합이지 코드가 아는 이름이 아니다.
///
/// 조건이 SetNode에서 태어나듯, 커맨드 프리셋은 이 노드에서 태어난다.
/// </summary>
public sealed class CommandSupplyNode : StoryNode
{
    public CommandSupplyNode(string? id = null, string name = "새 연출 공급")
        : base(id, name)
    {
    }

    /// <summary>공급하는 범주 Id 집합(게임 정의의 presentationCommandCategories 참조).</summary>
    public List<string> Categories { get; init; } = new();

    /// <summary>이 노드가 소유한 커맨드 프리셋. 목록 순서가 드롭다운 순서다.</summary>
    public List<CommandPreset> Presets { get; init; } = new();

    public CommandPreset? FindPreset(string? presetId)
    {
        return presetId is null
            ? null
            : Presets.FirstOrDefault(preset =>
                string.Equals(preset.Id, presetId, StringComparison.Ordinal));
    }

    public override StoryNode Clone()
    {
        return new CommandSupplyNode(Id, Name)
        {
            Layout = Layout.Clone(),
            DefaultExitTargetNodeId = DefaultExitTargetNodeId,
            Categories = new List<string>(Categories),
            Presets = Presets.Select(preset => preset.Clone()).ToList()
        };
    }
}

/// <summary>
/// 값이 세팅된 "정확한 연출종류" 하나. Yarn 인자로 노출되지 않는 하드코딩 기본값
/// (계약서 E3의 b′층)의 이주지다. PresentationNode는 이것을 참조해 완성된 커맨드를 쓴다.
///
/// 발행 시에는 참조가 아니라 <b>해석된 최종 인자 값</b>이 결과에 얼어붙는다.
/// 프리셋을 나중에 고쳐도 발행된 결과는 불변이다.
/// </summary>
public sealed class CommandPreset
{
    public CommandPreset(string? id = null)
    {
        Id = id ?? Identifier.Preset();
    }

    public string Id { get; }

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>카탈로그의 커맨드 정의 Id.</summary>
    public string CommandDefinitionId { get; set; } = string.Empty;

    /// <summary>파라미터 이름 → 값. 출력 순서는 언제나 카탈로그의 파라미터 순서다.</summary>
    public Dictionary<string, string> ArgumentValues { get; init; } = new(StringComparer.Ordinal);

    public string? Note { get; set; }

    public CommandPreset Clone()
    {
        return new CommandPreset(Id)
        {
            DisplayName = DisplayName,
            CommandDefinitionId = CommandDefinitionId,
            ArgumentValues = new Dictionary<string, string>(ArgumentValues, StringComparer.Ordinal),
            Note = Note
        };
    }
}

/// <summary>변수 하나에 값을 넣는다. 값은 게임이 해석하므로 문자열로 들고 있는다.</summary>
public sealed class VariableAssignment
{
    /// <summary>현재 지원하는 유일한 타입. 스탯은 숫자로 선언 출력된다(계약서 D4).</summary>
    public const string FloatType = "float";

    public string Variable { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// 변수 타입. 지금은 <see cref="FloatType"/> 하나지만 드롭다운 구조로 노출해
    /// 이후 타입 추가(bool 플래그 등)에 대비한다. <c>&lt;&lt;declare&gt;&gt;</c> 출력은
    /// 값 문자열 그대로라 타입 필드가 출력에 관여하지 않는다 — 정합의 책임은
    /// 값을 그 타입으로 쓰는 편집 UI에 있다.
    /// </summary>
    public string Type { get; set; } = FloatType;

    public VariableAssignment Clone() => new() { Variable = Variable, Value = Value, Type = Type };
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
/// 대본 하나를 읽어 그 줄들에 <b>대사 논리</b>를 얹는 노드.
///
/// 줄 순서와 화자·대사는 여기 없다. <see cref="ScriptId"/>가 가리키는
/// <see cref="Script.ScriptDocument"/>가 소유한다. 이 노드가 소유하는 것은
/// LineId에 매달린 조건 전환과 출구뿐이다. 앞으로 선택지·변수 변경·인라인 이벤트도
/// 같은 자리에 붙는다.
///
/// 이 분리 덕분에 대본을 다시 읽어 문구가 바뀌어도 조건 구조가 그대로 남고,
/// 같은 문장을 고칠 수 있는 자리가 도구 안에 하나만 존재한다.
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

    /// <summary>이 노드가 읽는 대본. null이면 아직 대본을 고르지 않은 것이다.</summary>
    public string? ScriptId { get; set; }

    /// <summary>
    /// LineId별 대사 논리. 목록 순서는 파일에서 읽는 순서일 뿐이고 실행 순서가 아니다.
    /// 실행 순서는 언제나 대본이 정한다.
    /// </summary>
    public List<DialogueLineExtension> LineExtensions { get; init; } = new();

    /// <summary>갈래를 여는 줄의 Id → 그 갈래가 끝났을 때 이동할 노드.</summary>
    public Dictionary<string, string> BranchExits { get; init; } = new(StringComparer.Ordinal);

    public DialogueLineExtension? FindExtension(string? lineId)
    {
        return lineId is null
            ? null
            : LineExtensions.FirstOrDefault(
                item => string.Equals(item.LineId, lineId, StringComparison.Ordinal));
    }

    public override StoryNode Clone()
    {
        return new DialogueNode(Id, Name)
        {
            Layout = Layout.Clone(),
            ScriptId = ScriptId,
            LineExtensions = LineExtensions.Select(item => item.Clone()).ToList(),
            DefaultExitTargetNodeId = DefaultExitTargetNodeId,
            BranchExits = new Dictionary<string, string>(BranchExits, StringComparer.Ordinal)
        };
    }
}
