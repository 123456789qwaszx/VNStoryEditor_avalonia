namespace Vn.Core.Diagnostics;

/// <summary>
/// UI와 특정 컴파일러 구현에 의존하지 않는 도구 공용 진단 모델.
/// Line과 Column은 사용자가 읽기 편하도록 1부터 시작한다.
/// 위치를 알 수 없으면 0이다.
/// </summary>
public sealed record VnDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    string FilePath,
    int Line,
    int Column);
