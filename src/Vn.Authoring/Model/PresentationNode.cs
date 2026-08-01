namespace Vn.Authoring.Model;

/// <summary>
/// DialogueNode의 줄에 카메라·표정·화면 효과 같은 연출 명령을 덧붙이는 노드.
///
/// 대사와 화자를 복사하지 않는다. 연결된 DialogueNode의 안정된 LineId만 참조하므로
/// 대사 수정과 줄 순서 이동이 일어나도 같은 줄을 계속 가리킨다.
/// </summary>
public sealed class PresentationNode : StoryNode
{
    public PresentationNode(string? id = null, string name = "새 연출")
        : base(id, name)
    {
    }

    /// <summary>
    /// LineId별 연출 데이터. 목록 순서는 사람이 파일에서 읽는 순서일 뿐이고,
    /// Dialogue 줄과의 결합은 반드시 <see cref="PresentationLineBinding.LineId"/>로 한다.
    /// </summary>
    public List<PresentationLineBinding> Bindings { get; init; } = new();

    public override StoryNode Clone()
    {
        return new PresentationNode(Id, Name)
        {
            Layout = Layout.Clone(),
            Bindings = Bindings.Select(binding => binding.Clone()).ToList()
        };
    }
}

/// <summary>
/// DialogueNode의 한 LineId에 붙는 ordered command 목록.
///
/// 대상 줄이 삭제되어도 이 객체를 자동 삭제하지 않는다. 현재 연결 대상 Dialogue에서
/// LineId를 찾을 수 있는지는 <c>PresentationBindingResolver</c>가 파생 상태로 계산한다.
/// </summary>
public sealed class PresentationLineBinding
{
    public PresentationLineBinding(string lineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        LineId = lineId;
    }

    public string LineId { get; set; }

    /// <summary>작성한 순서가 곧 출력·실행 순서다.</summary>
    public List<PresentationCommandInstance> Commands { get; init; } = new();

    public PresentationLineBinding Clone()
    {
        return new PresentationLineBinding(LineId)
        {
            Commands = Commands.Select(command => command.Clone()).ToList()
        };
    }
}

/// <summary>
/// 게임별 Command Definition 하나를 사용한 연출 명령 인스턴스.
/// DefinitionId와 인자는 아직 특정 엔진을 해석하지 않는 중립 데이터다.
/// </summary>
public sealed class PresentationCommandInstance
{
    public PresentationCommandInstance(string? id = null, string definitionId = "")
    {
        Id = id ?? Identifier.PresentationCommand();
        DefinitionId = definitionId;
    }

    public string Id { get; }

    public string DefinitionId { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>명령 정의가 요구하는 이름별 인자. JSON에서는 키를 정렬해 저장한다.</summary>
    public Dictionary<string, string> Arguments { get; init; } = new(StringComparer.Ordinal);

    public string? Note { get; set; }

    public PresentationCommandInstance Clone()
    {
        return new PresentationCommandInstance(Id, DefinitionId)
        {
            IsEnabled = IsEnabled,
            Arguments = new Dictionary<string, string>(Arguments, StringComparer.Ordinal),
            Note = Note
        };
    }
}
