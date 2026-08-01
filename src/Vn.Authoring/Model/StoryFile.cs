namespace Vn.Authoring.Model;

/// <summary>
/// 하나의 시나리오 파일이 소유하는 노드 묶음.
///
/// 프로젝트 manifest는 이 객체의 상대 경로만 보관하고,
/// 노드 본문은 해당 <see cref="RelativePath"/>의 독립된 .vnstory.json에 저장된다.
/// 파일 이름은 작가가 바꿀 수 있으므로 연결에는 <see cref="Id"/>를 사용한다.
/// </summary>
public sealed class StoryFile
{
    public StoryFile(string? id = null, string name = "새 파일", string? relativePath = null)
    {
        Id = id ?? Identifier.File();
        Name = name;
        RelativePath = relativePath ?? $"story/{Id}.vnstory.json";
    }

    public string Id { get; }

    public string Name { get; set; }

    /// <summary>
    /// 프로젝트 manifest를 기준으로 한 상대 경로. 파일명과 표시 이름은 서로 독립적이다.
    /// 슬래시(`/`) 형태로 정규화하여 저장한다.
    /// </summary>
    public string RelativePath { get; set; }

    /// <summary>이 파일 안에서의 순서. 새 노드는 언제나 마지막에 추가된다.</summary>
    public List<StoryNode> Nodes { get; init; } = new();

    public StoryFile Clone()
    {
        return new StoryFile(Id, Name, RelativePath)
        {
            Nodes = Nodes.Select(node => node.Clone()).ToList()
        };
    }
}
