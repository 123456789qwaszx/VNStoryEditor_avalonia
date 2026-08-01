namespace Vn.Authoring.Editing;

public enum ProjectChangeKind
{
    /// <summary>
    /// 노드·줄·조건·출구가 생기거나 사라지거나 순서가 바뀌었다.
    /// 계산되는 갈래와 포트가 달라지므로 화면을 다시 만들어야 한다.
    /// </summary>
    Structure,

    /// <summary>
    /// 화자·대사처럼 글자만 바뀌었다. 갈래도 포트도 그대로다.
    /// 화면은 이미 그 값을 보여 주고 있으므로 다시 만들 이유가 없다.
    /// </summary>
    Content,

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

    /// <summary>구조가 바뀌었다면 화면을 다시 만들어야 한다.</summary>
    public bool NeedsRebuild => Kind == ProjectChangeKind.Structure;
}
