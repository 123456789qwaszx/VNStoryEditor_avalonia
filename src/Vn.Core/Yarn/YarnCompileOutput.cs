using Vn.Core.Diagnostics;
using Vn.Core.Story;

namespace Vn.Core.Yarn;

internal sealed record YarnCompileOutput(
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<StoryNode> Nodes,
    IReadOnlySet<string> ExplicitYarnVariables,
    IReadOnlyList<VnDiagnostic> Diagnostics);
