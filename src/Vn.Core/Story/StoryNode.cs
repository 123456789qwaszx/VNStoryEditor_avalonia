namespace Vn.Core.Story;

public sealed record StoryNode(
    string Title,
    string FilePath,
    int HeaderLine,
    int BodyStartLine,
    int BodyEndLine,
    IReadOnlyList<StoryReference> CommandCalls,
    IReadOnlyList<StoryReference> VariableReferences,
    IReadOnlyList<StoryJump> Jumps);
