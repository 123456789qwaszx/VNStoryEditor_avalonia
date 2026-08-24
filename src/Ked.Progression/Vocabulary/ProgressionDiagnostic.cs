namespace Ked.Progression
{
    public enum ProgressionDiagnosticSeverity
    {
        Error = 0,   // Loading fails. Do not allow partial success.
        Warning = 1, // The data can still be used, but may indicate a problem.
    }

    /// <summary>
    /// Represents one problem found in the data.
    ///
    /// <b><see cref="Path"/> is the key part of this type.</b>
    /// Reporting only "undefined stat" would force the author to search the entire workbook.
    /// Providing an exact path such as
    /// <c>Nodes[ep_03].NextOptions[1].Conditions[0]</c> takes the author directly to the problem.
    ///
    /// This is used not only by the loader,
    /// but also by <b>invariant validation (Spec) and save restoration (Save)</b>.
    /// It is therefore part of the shared Progression domain vocabulary rather than
    /// belonging to any single boundary layer.
    /// </summary>
    public sealed class ProgressionDiagnostic
    {
        public ProgressionDiagnosticSeverity Severity { get; }

        // e.g.: Nodes[ep_03].NextOptions[1].Conditions[0]
        public string Path { get; }

        public string Message { get; }

        public ProgressionDiagnostic(
            ProgressionDiagnosticSeverity severity, string path, string message)
        {
            Severity = severity;
            Path = path ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public static ProgressionDiagnostic Error(string path, string message) =>
            new(ProgressionDiagnosticSeverity.Error, path, message);

        public static ProgressionDiagnostic Warning(string path, string message) =>
            new(ProgressionDiagnosticSeverity.Warning, path, message);

        public override string ToString() =>
            Path.Length == 0 
                ? Message 
                : $"{Path}: {Message}";
    }
}