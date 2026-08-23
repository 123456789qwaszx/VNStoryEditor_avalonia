namespace Ked.Progression
{
    public enum ProgressionDiagnosticSeverity
    {
        /// <summary>로드 실패. 부분 통과를 만들지 않는다.</summary>
        Error = 0,

        /// <summary>실을 수는 있지만 저작자가 봐야 한다.</summary>
        Warning = 1,
    }

    /// <summary>
    /// 데이터의 잘못 하나.
    ///
    /// <b><see cref="Path"/>가 이 타입의 존재 이유다.</b> "정의되지 않은 스탯"만 받으면
    /// 저작자는 워크북 전체를 뒤져야 한다. <c>Nodes[ep_03].NextOptions[1].Conditions[0]</c>이
    /// 있으면 바로 그 자리로 간다.
    ///
    /// 로더뿐 아니라 <b>불변식(Spec)과 세이브 복원(Save)도 낸다</b> — 그래서 경계가 아니라
    /// 어휘에 산다.
    /// </summary>
    public sealed class ProgressionDiagnostic
    {
        public ProgressionDiagnosticSeverity Severity { get; }

        /// <summary>예: <c>Nodes[ep_03].NextOptions[1].Conditions[0]</c></summary>
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
            new ProgressionDiagnostic(ProgressionDiagnosticSeverity.Error, path, message);

        public static ProgressionDiagnostic Warning(string path, string message) =>
            new ProgressionDiagnostic(ProgressionDiagnosticSeverity.Warning, path, message);

        public override string ToString() =>
            Path.Length == 0 ? Message : $"{Path}: {Message}";
    }
}
