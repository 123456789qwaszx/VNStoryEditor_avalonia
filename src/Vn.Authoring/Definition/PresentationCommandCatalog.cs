namespace Vn.Authoring.Definition;

/// <summary>연출 명령의 큰 편집 범주.</summary>
public enum PresentationCategory
{
    Camera,
    ScreenEffect,
    CharacterActing
}

/// <summary>
/// UI와 Formatter가 공유하는 명령 정의. 실제 작성 데이터에는 Id와 인자만 저장하고,
/// 표시 이름·출력 명령 이름·범주는 게임 정의 또는 기본 카탈로그에서 읽는다.
/// </summary>
public sealed record PresentationCommandDefinition(
    string Id,
    string DisplayName,
    PresentationCategory Category,
    string OutputCommandName,
    IReadOnlyDictionary<string, string> DefaultArguments);

public sealed class PresentationCommandCatalog
{
    private readonly IReadOnlyList<PresentationCommandDefinition> _definitions;

    public PresentationCommandCatalog(IEnumerable<PresentationCommandDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _definitions = definitions
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    public IReadOnlyList<PresentationCommandDefinition> Definitions => _definitions;

    public IReadOnlyList<PresentationCommandDefinition> For(PresentationCategory category) =>
        _definitions.Where(item => item.Category == category).ToArray();

    public PresentationCommandDefinition? Find(string? definitionId) =>
        _definitions.FirstOrDefault(item =>
            string.Equals(item.Id, definitionId, StringComparison.Ordinal));

    public static PresentationCommandCatalog For(GameDefinition? definition)
    {
        List<PresentationCommandDefinition> custom = (definition ?? GameDefinition.Empty)
            .PresentationCommands
            .Select(TryConvert)
            .Where(item => item is not null)
            .Cast<PresentationCommandDefinition>()
            .ToList();

        return custom.Count > 0 ? new PresentationCommandCatalog(custom) : Default;
    }

    public static PresentationCommandCatalog Default { get; } = new(
        new[]
        {
            Definition("camera.closeup", "클로즈업", PresentationCategory.Camera, "camera", "preset", "closeup"),
            Definition("camera.medium", "미디엄", PresentationCategory.Camera, "camera", "preset", "medium"),
            Definition("camera.wide", "와이드", PresentationCategory.Camera, "camera", "preset", "wide"),
            Definition("screen.fade", "페이드", PresentationCategory.ScreenEffect, "screen_effect", "preset", "fade"),
            Definition("screen.shake", "화면 흔들림", PresentationCategory.ScreenEffect, "screen_effect", "preset", "shake"),
            Definition("screen.flash", "플래시", PresentationCategory.ScreenEffect, "screen_effect", "preset", "flash"),
            Definition("acting.neutral", "기본 표정", PresentationCategory.CharacterActing, "character_acting", "preset", "neutral"),
            Definition("acting.smile", "미소", PresentationCategory.CharacterActing, "character_acting", "preset", "smile"),
            Definition("acting.surprised", "놀람", PresentationCategory.CharacterActing, "character_acting", "preset", "surprised")
        });

    private static PresentationCommandDefinition Definition(
        string id,
        string name,
        PresentationCategory category,
        string output,
        string argumentName,
        string argumentValue) =>
        new(id, name, category, output,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [argumentName] = argumentValue
            });

    private static PresentationCommandDefinition? TryConvert(PresentationCommandSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Id) ||
            !Enum.TryParse(spec.Category, ignoreCase: true, out PresentationCategory category))
        {
            return null;
        }

        string output = string.IsNullOrWhiteSpace(spec.OutputCommand)
            ? spec.Id
            : spec.OutputCommand;

        return new PresentationCommandDefinition(
            spec.Id,
            string.IsNullOrWhiteSpace(spec.Name) ? spec.Id : spec.Name,
            category,
            output,
            new Dictionary<string, string>(spec.Arguments, StringComparer.Ordinal));
    }
}
