using System.Reflection;
using System.Text;

namespace Vn.Authoring.Definition;

/// <summary>
/// 연출 명령의 편집 범주 하나. 어떤 범주가 있는지는 코드가 아니라 게임 정의가 공급한다.
/// 범주를 C# enum으로 박으면 게임마다 다른 어휘(카메라, 뎁스 블러, 이모지…)를 담을 수 없다.
/// </summary>
public sealed record PresentationCategoryDefinition(string Id, string DisplayName);

/// <summary>
/// 명령 파라미터 하나. <b>목록 순서가 곧 Yarn 포지셔널 인자 순서다.</b>
/// 런타임 커맨드는 <c>&lt;&lt;place_br @2 bust -1&gt;&gt;</c>처럼 순서로 인자를 읽으므로
/// 이름 없는 사전이 아니라 순서 있는 목록이어야 한다.
/// </summary>
public sealed record PresentationCommandParameter(
    string Name,
    string Type,
    bool Required,
    string? Default,
    string? Description);

/// <summary>
/// UI와 Formatter가 공유하는 명령 정의. 실제 작성 데이터에는 Id와 인자만 저장하고,
/// 표시 이름·출력 명령 이름·범주·파라미터 순서는 게임 정의 또는 기본 카탈로그에서 읽는다.
/// </summary>
public sealed record PresentationCommandDefinition(
    string Id,
    string DisplayName,
    string CategoryId,
    string OutputCommandName,
    IReadOnlyList<PresentationCommandParameter> Parameters,
    string Execution = PresentationCommandDefinition.QueuedExecution,
    bool MainLaneOnly = false,
    string? Notes = null,
    string? Intensity = null)
{
    public const string ImmediateExecution = "immediate";
    public const string QueuedExecution = "queued";

    public PresentationCommandParameter? FindParameter(string name) =>
        Parameters.FirstOrDefault(parameter =>
            string.Equals(parameter.Name, name, StringComparison.Ordinal));

    /// <summary>기본값이 있는 파라미터를 파라미터 순서 그대로 name→value로 펼친다.</summary>
    public IReadOnlyDictionary<string, string> DefaultArgumentValues()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (PresentationCommandParameter parameter in Parameters)
        {
            if (parameter.Default is { } value)
            {
                values[parameter.Name] = value;
            }
        }

        return values;
    }
}

public sealed class PresentationCommandCatalog
{
    private readonly IReadOnlyList<PresentationCategoryDefinition> _categories;
    private readonly IReadOnlyList<PresentationCommandDefinition> _definitions;

    public PresentationCommandCatalog(
        IEnumerable<PresentationCategoryDefinition> categories,
        IEnumerable<PresentationCommandDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(definitions);

        _definitions = definitions
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        _categories = categories
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    /// <summary>정의 파일이 선언한 순서 그대로의 편집 범주 목록.</summary>
    public IReadOnlyList<PresentationCategoryDefinition> Categories => _categories;

    public IReadOnlyList<PresentationCommandDefinition> Definitions => _definitions;

    public IReadOnlyList<PresentationCommandDefinition> For(string categoryId) =>
        _definitions
            .Where(item => string.Equals(item.CategoryId, categoryId, StringComparison.Ordinal))
            .ToArray();

    public PresentationCommandDefinition? Find(string? definitionId) =>
        _definitions.FirstOrDefault(item =>
            string.Equals(item.Id, definitionId, StringComparison.Ordinal));

    /// <summary>
    /// outputCommand 이름으로 정의를 찾는다. 같은 이름이 여럿이면 <b>카탈로그 첫 정의</b> —
    /// 텍스트 입력 파싱과 직접 조작이 같은 결정 규칙을 쓴다.
    /// </summary>
    public PresentationCommandDefinition? FindByOutputCommand(string? outputCommand) =>
        _definitions.FirstOrDefault(item =>
            string.Equals(item.OutputCommandName, outputCommand, StringComparison.Ordinal));

    public PresentationCategoryDefinition? FindCategory(string? categoryId) =>
        _categories.FirstOrDefault(item =>
            string.Equals(item.Id, categoryId, StringComparison.Ordinal));

    public static PresentationCommandCatalog For(GameDefinition? definition)
    {
        GameDefinition source = definition ?? GameDefinition.Empty;

        List<PresentationCommandDefinition> custom = source.PresentationCommands
            .Select(TryConvert)
            .Where(item => item is not null)
            .Cast<PresentationCommandDefinition>()
            .ToList();

        if (custom.Count == 0)
        {
            return Default;
        }

        return new PresentationCommandCatalog(CategoriesOf(source, custom), custom);
    }

    /// <summary>
    /// 게임 정의가 없을 때 쓰는 기본 카탈로그. 코드에 박은 목록이 아니라
    /// 런타임 등록 테이블과 교차 검증된 데이터(docs/game.definition.json)를 내장 리소스로 읽는다.
    /// </summary>
    public static PresentationCommandCatalog Default => DefaultLazy.Value;

    private static readonly Lazy<PresentationCommandCatalog> DefaultLazy = new(LoadDefault);

    private const string DefaultResourceName = "Vn.Authoring.Definition.game.definition.default.json";

    private static PresentationCommandCatalog LoadDefault()
    {
        using Stream stream =
            Assembly.GetExecutingAssembly().GetManifestResourceStream(DefaultResourceName)
            ?? throw new InvalidOperationException(
                $"기본 연출 카탈로그 리소스 '{DefaultResourceName}'가 없습니다.");
        using var reader = new StreamReader(stream, new UTF8Encoding(false));

        GameDefinition definition = GameDefinition.Parse(reader.ReadToEnd())
            ?? throw new InvalidOperationException("기본 연출 카탈로그를 읽지 못했습니다.");

        List<PresentationCommandDefinition> commands = definition.PresentationCommands
            .Select(TryConvert)
            .Where(item => item is not null)
            .Cast<PresentationCommandDefinition>()
            .ToList();

        return new PresentationCommandCatalog(CategoriesOf(definition, commands), commands);
    }

    /// <summary>
    /// 선언된 범주 목록을 쓰되, 목록이 없으면 명령이 쓰는 범주 Id를 등장 순서대로 파생한다.
    /// 범주 선언이 없다고 명령이 드롭다운에서 사라지면 안 된다.
    /// </summary>
    private static IEnumerable<PresentationCategoryDefinition> CategoriesOf(
        GameDefinition definition,
        IReadOnlyList<PresentationCommandDefinition> commands)
    {
        if (definition.PresentationCommandCategories.Count > 0)
        {
            return definition.PresentationCommandCategories
                .Where(spec => !string.IsNullOrWhiteSpace(spec.Id))
                .Select(spec => new PresentationCategoryDefinition(
                    spec.Id,
                    string.IsNullOrWhiteSpace(spec.Name) ? spec.Id : spec.Name));
        }

        return commands
            .Select(command => command.CategoryId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Select(id => new PresentationCategoryDefinition(id, id));
    }

    private static PresentationCommandDefinition? TryConvert(PresentationCommandSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Id))
        {
            return null;
        }

        string output = string.IsNullOrWhiteSpace(spec.OutputCommand)
            ? spec.Id
            : spec.OutputCommand;

        PresentationCommandParameter[] parameters = spec.Parameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name))
            .Select(parameter => new PresentationCommandParameter(
                parameter.Name,
                parameter.Type,
                parameter.Required,
                parameter.Default,
                parameter.Description))
            .ToArray();

        return new PresentationCommandDefinition(
            spec.Id,
            string.IsNullOrWhiteSpace(spec.Name) ? spec.Id : spec.Name,
            spec.Category,
            output,
            parameters,
            string.IsNullOrWhiteSpace(spec.Execution)
                ? PresentationCommandDefinition.QueuedExecution
                : spec.Execution,
            spec.MainLaneOnly,
            string.IsNullOrWhiteSpace(spec.Notes) ? null : spec.Notes,
            string.IsNullOrWhiteSpace(spec.Intensity) ? null : spec.Intensity);
    }
}
