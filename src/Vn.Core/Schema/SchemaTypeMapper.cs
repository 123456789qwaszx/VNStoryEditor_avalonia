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
    ///
    /// 예전에는 <c>Convert.ToSingle</c> 계열을 썼는데, 그것은 변환이 <em>가능하기만 하면</em>
    /// 통과시킨다. 그래서 bool 변수에 <c>1</c>, string 변수에 <c>true</c>, number 변수에 <c>"5"</c>가
    /// 조용히 들어가고, 작가는 자기가 쓴 것과 다른 값이 게임에 들어간 줄 모른다.
    /// 여기서는 JSON의 실제 종류(<see cref="JsonElement.ValueKind"/>)를 선언 타입과 직접 대조한다.
    ///
    /// 부수 효과로 컬처 문제도 사라진다. JsonElement는 JSON 문법에 따라 파싱되므로
    /// 소수점을 쉼표로 쓰는 로케일에서도 결과가 같다. 문자열을 거치지 않는 것이 가장 강한 보증이다.
    ///
    /// default를 아예 쓰지 않았거나 null이면 오류가 아니다. 타입별 영값을 쓴다.
    /// </summary>
    /// <param name="problem">
    /// 실패한 이유를 사람이 읽을 문장으로 돌려준다. 성공하면 null이다.
    /// 호출부가 진단 메시지에 그대로 이어 붙일 수 있는 형태여야 한다.
    /// </param>
    public static bool TryGetDefaultValue(
        VariableDefinition definition,
        out IConvertible value,
        out string? problem)
    {
        value = GetFallbackValue(definition.Type);
        problem = null;

        if (definition.Default is not JsonElement element ||
            element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return true;
        }

        switch (Normalize(definition.Type))
        {
            case "string":
                if (element.ValueKind != JsonValueKind.String)
                {
                    problem = $"문자열이 필요한데 {Describe(element)}이(가) 있습니다.";
                    return false;
                }

                value = element.GetString() ?? string.Empty;
                return true;

            case "number":
            case "float":
                if (element.ValueKind != JsonValueKind.Number)
                {
                    problem = $"숫자가 필요한데 {Describe(element)}이(가) 있습니다.";
                    return false;
                }

                if (!element.TryGetSingle(out float number))
                {
                    problem = "숫자가 float으로 표현할 수 있는 범위를 벗어납니다.";
                    return false;
                }

                value = number;
                return true;

            case "int":
                if (element.ValueKind != JsonValueKind.Number)
                {
                    problem = $"정수가 필요한데 {Describe(element)}이(가) 있습니다.";
                    return false;
                }

                if (!element.TryGetDouble(out double integral) ||
                    Math.Floor(integral) != integral)
                {
                    problem = "정수가 필요한데 소수점 이하가 있는 값입니다.";
                    return false;
                }

                if (integral is < float.MinValue or > float.MaxValue)
                {
                    problem = "숫자가 float으로 표현할 수 있는 범위를 벗어납니다.";
                    return false;
                }

                value = (float)integral;
                return true;

            case "bool":
            case "boolean":
                if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    problem =
                        $"true 또는 false가 필요한데 {Describe(element)}이(가) 있습니다. " +
                        "따옴표 없이 true 또는 false로 적으세요.";
                    return false;
                }

                value = element.GetBoolean();
                return true;

            default:
                // 여기 오는 경우는 호출부가 IsSupported를 먼저 보지 않은 것이다.
                problem = $"지원되지 않는 타입 '{definition.Type}'입니다.";
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

    private static string Describe(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => "문자열",
            JsonValueKind.Number => "숫자",
            JsonValueKind.True or JsonValueKind.False => "true/false 값",
            JsonValueKind.Array => "배열",
            JsonValueKind.Object => "객체",
            _ => "알 수 없는 값"
        };
    }

    private static string Normalize(string? type)
    {
        return type is null
            ? string.Empty
            : type.Trim().ToLowerInvariant();
    }
}