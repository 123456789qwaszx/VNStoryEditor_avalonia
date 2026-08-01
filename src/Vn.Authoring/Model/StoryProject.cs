namespace Vn.Authoring.Model;

/// <summary>
/// 저작 도구가 다루는 공식 원본.
///
/// 이 객체가 진실이고, Yarn을 비롯한 게임별 형식은 여기서 내보내는 결과물이다.
/// 예전 구조에서는 <c>.yarn</c> 원문이 진실이고 화면 모델이 그것의 손실 압축이었다.
/// 그래서 화면에서 만든 구조를 원문에 되쓸 방법이 없었고, 편집은 "한 줄만 안전하게
/// 갈아 끼우기"에 갇혀 있었다. 방향을 뒤집었기 때문에 조건 갈래·출구·노드 추가처럼
/// 구조를 바꾸는 편집이 자연스러워진다.
///
/// <see cref="Nodes"/>의 순서가 곧 파일 순서다. 새 노드는 언제나 맨 뒤에 붙는다.
/// 그래프에서의 위치는 <see cref="StoryNode.Layout"/>이며 순서와 무관하다.
/// </summary>
public sealed class StoryProject
{
    /// <summary>파일 형식 버전. 마이그레이션 판단에 쓴다.</summary>
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public string Title { get; set; } = "새 프로젝트";

    public List<StoryNode> Nodes { get; init; } = new();

    /// <summary>이야기가 시작되는 노드. null이면 아직 정하지 않은 것이다.</summary>
    public string? StartNodeId { get; set; }

    public StoryNode? FindNode(string? nodeId)
    {
        return nodeId is null
            ? null
            : Nodes.FirstOrDefault(node => string.Equals(node.Id, nodeId, StringComparison.Ordinal));
    }

    public DialogueNode? FindDialogue(string? nodeId) => FindNode(nodeId) as DialogueNode;

    /// <summary>
    /// 프로젝트 안의 모든 조건. <see cref="SetNode"/>가 선언한 순서를 지킨다.
    /// 조건 드롭다운과 그래프 간선 라벨이 같은 목록을 본다.
    /// </summary>
    public IEnumerable<ConditionDefinition> EnumerateConditions()
    {
        return Nodes.OfType<SetNode>().SelectMany(node => node.Conditions);
    }

    public ConditionDefinition? FindCondition(string? conditionId)
    {
        return conditionId is null
            ? null
            : EnumerateConditions()
                .FirstOrDefault(item => string.Equals(item.Id, conditionId, StringComparison.Ordinal));
    }

    public StoryProject Clone()
    {
        return new StoryProject
        {
            FormatVersion = FormatVersion,
            Title = Title,
            StartNodeId = StartNodeId,
            Nodes = Nodes.Select(node => node.Clone()).ToList()
        };
    }
}
