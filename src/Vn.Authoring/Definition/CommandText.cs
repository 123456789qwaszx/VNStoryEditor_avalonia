using System.Globalization;
using System.Text;

namespace Vn.Authoring.Definition;

/// <summary>커맨드 텍스트의 인자 하나 — 카탈로그 파라미터 순서를 이미 지킨 값이다.</summary>
public sealed record OrderedCommandArgument(string Name, string Value);

/// <summary>
/// 텍스트 입력 파싱 결과. 실패면 <see cref="Error"/>에 이유가 있다 —
/// 비슷한 이름을 추측해 보정하지 않는다(원칙 §2.3). 무엇이 틀렸는지만 정확히 말한다.
/// </summary>
public sealed record CommandTextParseResult(
    PresentationCommandDefinition? Definition,
    IReadOnlyDictionary<string, string>? Arguments,
    string? Error)
{
    public bool Success => Definition is not null && Error is null;
}

/// <summary>
/// <c>&lt;&lt;이름 인자…&gt;&gt;</c> 텍스트와 구조화 커맨드 사이의 변환 규칙 단일 구현.
///
/// 조립(<see cref="ResolveOrdered"/>)은 이미터·Preview가 쓰던 규칙 그대로다 —
/// 카탈로그 파라미터 순서, 트레일링 기본값 생략, 정의 밖 인자는 이름순으로 뒤에.
/// 파싱은 그 역방향 <b>입력 방법</b>이다(산출물 되읽기가 아니므로 역파싱 금지 원칙과
/// 무관). 검증 기준도 같은 카탈로그 하나다.
/// </summary>
public static class CommandText
{
    /// <summary>
    /// 카탈로그의 파라미터 순서대로 인자 값을 해석한다. 작성 값이 없으면 정의의 기본값을 쓰고,
    /// 값이 아예 없는 파라미터부터는 트레일링 생략으로 자른다(뒤쪽부터만 생략 규칙).
    /// 정의를 모르는 명령이나 파라미터 밖의 인자는 버리지 않고 이름순으로 뒤에 붙인다.
    /// </summary>
    public static IReadOnlyList<OrderedCommandArgument> ResolveOrdered(
        PresentationCommandDefinition? definition,
        IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var ordered = new List<OrderedCommandArgument>();
        var consumed = new HashSet<string>(StringComparer.Ordinal);

        if (definition is not null)
        {
            foreach (PresentationCommandParameter parameter in definition.Parameters)
            {
                string? value = arguments.TryGetValue(parameter.Name, out string? provided)
                    ? provided
                    : parameter.Default;

                if (value is null)
                {
                    break;
                }

                ordered.Add(new OrderedCommandArgument(parameter.Name, value));
                consumed.Add(parameter.Name);
            }
        }

        foreach ((string key, string value) in arguments
                     .Where(pair => !consumed.Contains(pair.Key))
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            ordered.Add(new OrderedCommandArgument(key, value));
        }

        return ordered;
    }

    /// <summary>어떤 방식으로 만들었든 항상 병기되는 <c>&lt;&lt;…&gt;&gt;</c> 텍스트.</summary>
    public static string Format(
        PresentationCommandDefinition? definition,
        string fallbackName,
        IReadOnlyDictionary<string, string> arguments)
    {
        var builder = new StringBuilder("<<");
        builder.Append(definition?.OutputCommandName ?? fallbackName);

        foreach (OrderedCommandArgument argument in ResolveOrdered(definition, arguments))
        {
            builder.Append(' ').Append(argument.Value);
        }

        return builder.Append(">>").ToString();
    }

    /// <summary>
    /// 한 줄 텍스트를 카탈로그 기준으로 파싱한다. 꺾쇠는 있어도 없어도 된다.
    ///
    /// 커맨드명은 outputCommand 정확 일치(같은 이름이 여럿이면 카탈로그 첫 정의 —
    /// 결정적), 그다음 정의 Id 정확 일치만 본다. 인자는 포지셔널로 파라미터에 붙이고,
    /// 초과·필수 누락·숫자 타입 불일치는 즉시 오류다. 추측 보정은 하지 않는다.
    /// </summary>
    public static CommandTextParseResult Parse(string? input, PresentationCommandCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        string text = (input ?? string.Empty).Trim();

        if (text.StartsWith("<<", StringComparison.Ordinal))
        {
            text = text[2..];
        }

        if (text.EndsWith(">>", StringComparison.Ordinal))
        {
            text = text[..^2];
        }

        string[] tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
        {
            return new CommandTextParseResult(null, null, "커맨드 이름이 없습니다.");
        }

        string name = tokens[0];
        PresentationCommandDefinition? definition =
            catalog.FindByOutputCommand(name) ?? catalog.Find(name);

        if (definition is null)
        {
            return new CommandTextParseResult(
                null, null, $"카탈로그에 없는 커맨드입니다: '{name}'");
        }

        string[] values = tokens[1..];

        if (values.Length > definition.Parameters.Count)
        {
            return new CommandTextParseResult(
                definition,
                null,
                $"'{definition.OutputCommandName}'의 인자는 최대 {definition.Parameters.Count}개인데 " +
                $"{values.Length}개가 왔습니다.");
        }

        var arguments = new Dictionary<string, string>(StringComparer.Ordinal);

        for (int index = 0; index < definition.Parameters.Count; index++)
        {
            PresentationCommandParameter parameter = definition.Parameters[index];

            if (index < values.Length)
            {
                if (ValidateType(parameter, values[index]) is { } typeError)
                {
                    return new CommandTextParseResult(definition, null, typeError);
                }

                arguments[parameter.Name] = values[index];
            }
            else if (parameter.Required && parameter.Default is null)
            {
                return new CommandTextParseResult(
                    definition,
                    null,
                    $"필수 인자 '{parameter.Name}'이 없습니다.");
            }
        }

        return new CommandTextParseResult(definition, arguments, null);
    }

    /// <summary>
    /// 숫자 타입과 ease만 검증한다. 그 외 토큰 타입(slot·duration 등)의 어휘는 런타임
    /// 파서의 몫이다. ease를 여기서 잡는 이유: 런타임은 모르는 이름을 로그만 남기고
    /// OutCubic으로 조용히 굴러가므로(ease-open-orders §4), 오타를 저작에서 미리 짚는
    /// 것이 유일한 방어다 — 판정 규칙은 런타임 YarnEaseParser와 같다(대소문자 무시,
    /// 숫자 토큰 거부).
    /// </summary>
    private static string? ValidateType(PresentationCommandParameter parameter, string value)
    {
        return parameter.Type switch
        {
            "int" when !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) =>
                $"'{parameter.Name}'은 정수여야 하는데 '{value}'가 왔습니다.",
            "float" or "seconds" when !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) =>
                $"'{parameter.Name}'은 숫자여야 하는데 '{value}'가 왔습니다.",
            "bool" when !bool.TryParse(value, out _) && value is not ("0" or "1") =>
                $"'{parameter.Name}'은 true/false여야 하는데 '{value}'가 왔습니다.",
            "ease" when long.TryParse(value, out _) ||
                        !Enum.TryParse<Ked.Presentation.Core.EaseKind>(value, ignoreCase: true, out _) =>
                $"'{parameter.Name}'은 이징 이름이어야 하는데 '{value}'가 왔습니다 (예: OutCubic, Linear, InOutSine).",
            _ => null
        };
    }
}
