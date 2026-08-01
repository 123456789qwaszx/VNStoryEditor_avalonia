namespace Vn.Authoring.Model;

/// <summary>
/// 하나의 시나리오 파일이 소유하는 노드 묶음.
///
/// 지금 단계에서는 프로젝트의 단일 JSON 안에 논리적인 파일 경계만 저장한다.
/// 이후 manifest + 여러 실제 파일로 저장 방식이 바뀌더라도 도메인의 소유 관계는 그대로 유지된다.
/// 파일 이름은 작가가 바꿀 수 있으므로 연결에는 <see cref="Id"/>를 사용한다.
/// </summary>
public sealed class StoryFile
{
    public StoryFile(string? id = null, string name = "새 파일")
    {
        Id = id ?? Identifier.File();
        Name = name;
    }

    public string Id { get; }

    public string Name { get; set; }

    /// <summary>이 파일 안에서의 순서. 새 노드는 언제나 마지막에 추가된다.</summary>
    public List<StoryNode> Nodes { get; init; } = new();

    public StoryFile Clone()
    {
        return new StoryFile(Id, Name)
        {
            Nodes = Nodes.Select(node => node.Clone()).ToList()
        };
    }
}
