namespace Vn.Core.Story;

/// <summary>
/// 노드 본문에서 이름이 실제로 쓰인 한 지점.
/// 진단이 노드 헤더가 아니라 이 위치를 가리켜야 에디터에서 캐럿을 바로 옮길 수 있다.
/// Line과 Column은 1부터 시작하며, 위치를 알 수 없으면 노드 헤더 줄로 대체된다.
/// </summary>
public sealed record StoryReference(
    string Name,
    string FilePath,
    int Line,
    int Column);
