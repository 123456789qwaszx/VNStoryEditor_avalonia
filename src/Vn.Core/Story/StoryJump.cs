namespace Vn.Core.Story;

public sealed record StoryJump(
    string SourceNodeTitle,
    string DestinationNodeTitle,
    string FilePath,
    int Line,
    int Column);
