namespace Vn.Core.Story;

public sealed record StoryNode(
    string Title,
    string FilePath,
    int HeaderLine,
    IReadOnlyList<string> CommandCalls,
    IReadOnlyList<string> VariableReferences,
    IReadOnlyList<StoryJump> Jumps);
