using System.Reflection;
using Vn.Core.Diagnostics;

namespace Vn.Core.Yarn;

/// <summary>
/// Yarn 진단 객체가 Vn.Core의 공개 계약으로 새어나가지 않게 이 경계에서 변환한다.
///
/// 읽기는 reflection으로 하지만, 이것은 "버전 차이를 조용히 흡수하기 위한 것"이 아니다.
/// 조용한 흡수는 여기서 최악의 실패 모드다 — 속성 이름이나 enum 이름이 바뀌면
/// 모든 진단이 Info로 강등되고, HasErrors가 false가 되고, 깨진 스토리에 종료 코드 0이 나간다.
/// CI가 초록불을 켜는 것이 침묵의 대가다.
///
/// 그래서 여기서는 정반대로 간다.
///  - 해석하지 못한 심각도는 Error로 올린다. 못 알아들으면 시끄럽게 실패해야 한다.
///  - 진단 객체의 모양이 예상과 다르면 <see cref="FindMissingProperties"/>가 그것을 드러내고,
///    호출부가 VN2004를 따로 띄운다.
///  - 해석하지 못한 원본 값은 메시지에 남겨 사람이 원인을 볼 수 있게 한다.
/// </summary>
internal static class YarnDiagnosticMapper
{
    /// <summary>
    /// 이 매퍼가 존재를 전제하는 속성들.
    /// 하나라도 없으면 그 아래 결과는 전부 신뢰할 수 없다.
    /// </summary>
    private static readonly string[] RequiredProperties =
    {
        "Code",
        "Message",
        "FileName",
        "Severity",
        "Range"
    };

    /// <summary>
    /// Yarn 진단 타입에서 우리가 기대하는 속성 중 없는 것을 돌려준다.
    /// 비어 있지 않으면 컴파일러 버전이 우리가 아는 모양에서 벗어난 것이다.
    /// </summary>
    public static IReadOnlyList<string> FindMissingProperties(Type diagnosticType)
    {
        return RequiredProperties
            .Where(name => diagnosticType.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public) is null)
            .ToArray();
    }

    public static VnDiagnostic Map(object diagnostic)
    {
        string rawCode = ReadText(diagnostic, "Code");
        string message = ReadText(diagnostic, "Message");
        string filePath = NormalizeFilePath(ReadText(diagnostic, "FileName"));

        bool codeRecognized =
            VnDiagnosticCodes.TryNormalizeYarnCode(rawCode, out string code);

        bool severityRecognized =
            TryReadSeverity(diagnostic, out DiagnosticSeverity severity, out string rawSeverity);

        object? range = ReadProperty(diagnostic, "Range");
        object? start = range is null
            ? null
            : ReadProperty(range, "Start");

        int line = ReadInt(start, "Line");
        int column = ReadInt(start, "Character");

        string text = string.IsNullOrWhiteSpace(message)
            ? diagnostic.ToString() ?? "Yarn 진단 메시지"
            : message;

        // 해석에 실패한 값은 버리지 않고 메시지에 남긴다.
        // 코드 자리에 넣으면 코드 형태가 두 갈래로 갈라지므로 메시지가 맞는 자리다.
        if (!codeRecognized && !string.IsNullOrWhiteSpace(rawCode))
        {
            text += $" (Yarn 원본 코드: {rawCode.Trim()})";
        }

        if (!severityRecognized)
        {
            text += string.IsNullOrWhiteSpace(rawSeverity)
                ? " (Yarn 심각도를 읽지 못해 오류로 처리했습니다. YarnSpinner.Compiler 버전을 확인하세요.)"
                : $" (Yarn 심각도 '{rawSeverity}'를 해석하지 못해 오류로 처리했습니다. YarnSpinner.Compiler 버전을 확인하세요.)";
        }

        return new VnDiagnostic(
            code,
            severity,
            text,
            filePath,
            line < 0 ? 0 : line + 1,
            column < 0 ? 0 : column + 1);
    }

    /// <summary>
    /// 심각도를 읽는다. 아는 값이 아니면 <see cref="DiagnosticSeverity.Error"/>로 올리고 false를 돌려준다.
    /// Info로 내리면 종료 코드가 0이 되어 문제가 사라진 것처럼 보인다. 그쪽이 훨씬 위험하다.
    /// </summary>
    private static bool TryReadSeverity(
        object diagnostic,
        out DiagnosticSeverity severity,
        out string rawSeverity)
    {
        object? value = ReadProperty(diagnostic, "Severity");
        rawSeverity = value?.ToString() ?? string.Empty;

        switch (rawSeverity.Trim().ToLowerInvariant())
        {
            case "error":
                severity = DiagnosticSeverity.Error;
                return true;

            case "warning":
                severity = DiagnosticSeverity.Warning;
                return true;

            case "info":
            case "information":
                severity = DiagnosticSeverity.Info;
                return true;

            default:
                severity = DiagnosticSeverity.Error;
                return false;
        }
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