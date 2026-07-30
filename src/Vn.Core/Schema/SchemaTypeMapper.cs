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

    public static IConvertible GetDefaultValue(VariableDefinition definition)
    {
        object? value = definition.Default;

        if (value is JsonElement element)
        {
            value = ConvertJsonElement(element);
        }

        return Normalize(definition.Type) switch
        {
            "string" => Convert.ToString(value, CultureInfo.InvariantCulture)
                ?? string.Empty,

            "number" or "int" or "float" =>
                Convert.ToSingle(
                    value ?? 0,
                    CultureInfo.InvariantCulture),

            "bool" or "boolean" =>
                Convert.ToBoolean(
                    value ?? false,
                    CultureInfo.InvariantCulture),

            _ => string.Empty
        };
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetSingle(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    private static string Normalize(string type)
    {
        return type.Trim().ToLowerInvariant();
    }
}
