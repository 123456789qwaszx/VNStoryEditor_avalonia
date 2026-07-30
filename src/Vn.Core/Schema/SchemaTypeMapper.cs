using System.Globalization;
using System.Text.Json;
using Yarn;

namespace Vn.Core.Schema;

internal static class SchemaTypeMapper
{
    public static bool IsSupported(string type)
    {
        return Normalize(type) is
            "string" or "number" or "int" or "float" or "bool" or "boolean";
    }

    public static IType GetYarnType(string type)
    {
        return Normalize(type) switch
        {
            "string" => Types.String,
            "number" or "int" or "float" => Types.Number,
            "bool" or "boolean" => Types.Boolean,
            _ => throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "지원되지 않는 Yarn 변수 타입입니다.")
        };
    }

    /// <summary>
    /// 스키마의 default를 Yarn 값으로 변환한다.
    /// 작가가 손으로 편집한 스키마에서 default가 타입과 맞지 않을 수 있으므로
    /// 예외를 던지는 대신 false를 돌려주고, 호출부가 진단으로 알린다.
    /// </summary>
    public static bool TryGetDefaultValue(
        VariableDefinition definition,
        out IConvertible value)
    {
        object? raw = definition.Default is JsonElement element
            ? ConvertJsonElement(element)
            : null;

        try
        {
            value = Normalize(definition.Type) switch
            {
                "string" =>
                    Convert.ToString(raw, CultureInfo.InvariantCulture)
                    ?? string.Empty,

                "number" or "int" or "float" =>
                    Convert.ToSingle(raw ?? 0, CultureInfo.InvariantCulture),

                "bool" or "boolean" =>
                    Convert.ToBoolean(raw ?? false, CultureInfo.InvariantCulture),

                _ => string.Empty
            };

            return true;
        }
        catch (Exception exception) when (
            exception is FormatException or
                InvalidCastException or
                OverflowException)
        {
            value = GetFallbackValue(definition.Type);
            return false;
        }
    }

    /// <summary>default를 해석할 수 없을 때 쓰는 타입별 영값.</summary>
    public static IConvertible GetFallbackValue(string type)
    {
        return Normalize(type) switch
        {
            "number" or "int" or "float" => 0f,
            "bool" or "boolean" => false,
            _ => string.Empty
        };
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetSingle(out float number)
                ? number
                : element.ToString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.ToString()
        };
    }

    private static string Normalize(string? type)
    {
        return type is null
            ? string.Empty
            : type.Trim().ToLowerInvariant();
    }
}
