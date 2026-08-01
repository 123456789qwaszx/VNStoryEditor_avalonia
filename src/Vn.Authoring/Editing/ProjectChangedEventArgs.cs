namespace Vn.Authoring.Editing;

public enum ProjectChangeKind
{
    /// <summary>
    /// 노드·줄·조건·assignment·출구가 생기거나 사라지거나 순서가 바뀌었다.
    /// 계산되는 갈래와 포트, 편집 행 자체가 달라지므로 화면 구조를 다시 만들어야 한다.
    /// </summary>
    Structure,

    /// <summary>
    /// 화자·대사·assignment 값처럼 현재 편집 컨트롤이 이미 보여 주는 값만 바뀌었다.
    /// 파생되는 그래프 라벨이나 조건 선택지는 달라지지 않는다.
    /// </summary>
    Content,

    /// <summary>
    /// 조건의 작가용 이름이나 평가식이 바뀌었다.
    /// SetNode의 편집 행 구조는 그대로지만 그래프 조건 라벨과 Dialogue 조건 선택지는 새 값을 읽어야 한다.
    /// </summary>
    ConditionDefinition,

    /// <summary>
    /// 노드의 이름처럼 그래프와 참조 표시에는 영향을 주지만 현재 편집 행을 다시 만들 필요는 없는 값이 바뀌었다.
    /// </summary>
    NodeMetadata,

    /// <summary>그래프에서의 좌표만 바뀌었다.</summary>
    Layout
}

public sealed class ProjectChangedEventArgs : EventArgs
{
    public ProjectChangedEventArgs(ProjectChangeKind kind)
    {
        Kind = kind;
    }

    public ProjectChangeKind Kind { get; }

    /// <summary>편집 행이나 포트의 개수가 바뀌어 현재 Inspector를 다시 만들어야 하는가.</summary>
    public bool NeedsInspectorRebuild => Kind == ProjectChangeKind.Structure;

    /// <summary>그래프 카드·포트·간선 또는 그 라벨을 다시 계산해야 하는가.</summary>
    public bool NeedsGraphRebuild => Kind is
        ProjectChangeKind.Structure or
        ProjectChangeKind.ConditionDefinition or
        ProjectChangeKind.NodeMetadata;
}
