using System.Reflection;
using Vn.Core.Diagnostics;

namespace Vn.Core.Yarn;

/// <summary>
/// Yarn 진단 객체가 Vn.Core의 공개 계약으로 새어나가지 않게 이 경계에서 변환한다.
/// 진단 코드 API의 버전 차이는 reflection으로 흡수하고, 나머지 앱은 자체 모델만 사용한다.
/// </summary>
internal static class YarnDiagnosticMapper
{
    public static VnDiagnostic Map(object diagnostic)
    {
        string code = ReadText(diagnostic, "Code");
        string message = ReadText(diagnostic, "Message");
        string filePath = NormalizeFilePath(
            ReadText(diagnostic, "FileName"));

        object? severityValue =
            ReadProperty(diagnostic, "Severity");

        DiagnosticSeverity severity = severityValue?
            .ToString()?
            .ToLowerInvariant() switch
        {
            "error" => DiagnosticSeverity.Error,
            "warning" => DiagnosticSeverity.Warning,
            _ => DiagnosticSeverity.Info
        };

        object? range = ReadProperty(diagnostic, "Range");
        object? start = range is null
            ? null
            : ReadProperty(range, "Start");

        int line = ReadInt(start, "Line");
        int column = ReadInt(start, "Character");

        return new VnDiagnostic(
            string.IsNullOrWhiteSpace(code)
                ? VnDiagnosticCodes.YarnUnclassified
                : NormalizeCode(code),
            severity,
            string.IsNullOrWhiteSpace(message)
                ? diagnostic.ToString() ?? "Yarn 진단 메시지"
                : message,
            filePath,
            line < 0 ? 0 : line + 1,
            column < 0 ? 0 : column + 1);
    }

    /// <summary>
    /// Yarn이 낸 진단 코드를 YS 접두사 아래로 통과시킨다.
    /// 3.2.1의 코드는 이미 YS0003 같은 형태라 그대로 쓰이고,
    /// 다른 형태가 오면 원본을 잃지 않도록 YS- 를 붙여 감싼다.
    /// 접두사가 곧 "이건 우리가 아니라 Yarn이 낸 진단"이라는 표시이며,
    /// 억제 규칙은 이 구분 위에서 동작한다.
    /// </summary>
    private static string NormalizeCode(string code)
    {
        string trimmed = code.Trim();

        return trimmed.StartsWith(
            VnDiagnosticCodes.YarnPrefix,
            StringComparison.OrdinalIgnoreCase)
            ? trimmed.ToUpperInvariant()
            : $"{VnDiagnosticCodes.YarnPrefix}-{trimmed}";
    }

    private static object? ReadProperty(
        object? target,
        string propertyName)
    {
        if (target is null)
        {
            return null;
        }

        PropertyInfo? property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public);

        return property?.GetValue(target);
    }

    private static string ReadText(
        object target,
        string propertyName)
    {
        return ReadProperty(target, propertyName)?.ToString()
            ?? string.Empty;
    }

    private static int ReadInt(
        object? target,
        string propertyName)
    {
        object? value = ReadProperty(target, propertyName);

        return value switch
        {
            int number => number,
            _ => -1
        };
    }

    private static string NormalizeFilePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
            uri.IsFile)
        {
            return uri.LocalPath;
        }

        try
        {
            return Path.GetFullPath(value);
        }
        catch
        {
            return value;
        }
    }
}
