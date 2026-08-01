namespace Vn.Core.Story;

public sealed record StoryNode(
    string Title,
    string FilePath,
    int HeaderLine,
    int BodyStartLine,
    int BodyEndLine,
    IReadOnlyList<StoryReference> CommandCalls,
    IReadOnlyList<StoryReference> VariableReferences,
    IReadOnlyList<StoryJump> Jumps,

    /// <summary>평평한 라인 목록. 순수 텍스트 뷰와 CLI 픽스처가 이것에 의존한다.</summary>
    IReadOnlyList<StoryLine> Lines,

    /// <summary>
    /// 같은 본문을 분기 블록 트리로 본 것. 최상위에 라인과 블록이 원본 순서대로 섞여 있다.
    /// <see cref="Lines"/>를 대체하지 않는다. 같은 것의 다른 표현이다.
    /// </summary>
    IReadOnlyList<StoryElement> Body);
