using Vn.Authoring.Results;

namespace Vn.Authoring.Model;

/// <summary>
/// <b>발행된 대사 결과 하나</b>를 읽고 그 줄에 카메라·표정·화면 효과를 덧붙이는 노드.
///
/// 입력은 지금 편집 중인 DialogueNode가 아니라 얼어붙은
/// <see cref="Results.DialogueResult"/>다. 편집 중인 노드를 실시간으로 읽으면 연출가가
/// 작업하는 동안 발밑의 대사가 바뀌고, 완성한 연출표가 어느 대사에 맞는 것인지 아무도
/// 말할 수 없게 된다.
///
/// 대사와 화자를 복사하지 않는다. 결과의 안정된 LineId만 참조한다.
/// </summary>
public sealed class PresentationNode : StoryNode
{
    public PresentationNode(string? id = null, string name = "새 연출")
        : base(id, name)
    {
    }

    /// <summary>
    /// 읽고 있는 DialogueResult. null이면 아직 입력을 고르지 않은 것이다.
    /// Id·Version·Hash를 모두 기억하므로 나중에 정확히 같은 입력을 다시 찾을 수 있다.
    /// </summary>
    public DialogueResultReference? Source { get; set; }

    /// <summary>
    /// LineId 없는 노드 수준 커맨드. 슬롯·캐스팅·배경 스폰·리셋처럼 장면에 들어가기 전에
    /// 한 번 실행할 준비 작업이다. 이미터에서 Set_ 노드 본문이 된다(계약서 A1·A2).
    /// 목록 순서가 곧 실행·출력 순서다.
    /// </summary>
    public List<PresentationCommandInstance> SetupCommands { get; init; } = new();

    /// <summary>
    /// LineId별 연출 데이터. 목록 순서는 사람이 파일에서 읽는 순서일 뿐이고,
    /// 대사 줄과의 결합은 반드시 <see cref="PresentationLineBinding.LineId"/>로 한다.
    /// </summary>
    public List<PresentationLineBinding> Bindings { get; init; } = new();

    public PresentationLineBinding? FindBinding(string? lineId)
    {
        return lineId is null
            ? null
            : Bindings.FirstOrDefault(
                binding => string.Equals(binding.LineId, lineId, StringComparison.Ordinal));
    }

    public override StoryNode Clone()
    {
        return new PresentationNode(Id, Name)
        {
            Layout = Layout.Clone(),
            Source = Source,
            SetupCommands = SetupCommands.Select(command => command.Clone()).ToList(),
            Bindings = Bindings.Select(binding => binding.Clone()).ToList()
        };
    }
}

/// <summary>
/// 대사 결과의 한 LineId에 붙는 ordered command 목록.
///
/// 대상 줄이 없어져도 이 객체를 자동 삭제하지 않는다. 현재 입력 결과에서 LineId를 찾을 수
/// 있는지는 <c>PresentationBindingResolver</c>가 파생 상태로 계산한다.
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
