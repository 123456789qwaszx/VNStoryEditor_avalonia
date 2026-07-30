using Vn.Core.Diagnostics;
using Vn.Core.Story;

namespace Vn.Core.Analysis;

public sealed record AnalysisReport(
    string ProjectPath,
    string SchemaPath,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<StoryNode> Nodes,
    IReadOnlyList<VnDiagnostic> Diagnostics)
{
    public bool HasErrors =>
        Diagnostics.Any(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
}
