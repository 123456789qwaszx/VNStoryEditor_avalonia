using Vn.Authoring.Definition;
using Vn.Authoring.Model;

namespace Vn.Authoring.Flow;

/// <summary>공급 노드가 제공하는 프리셋 하나와 그 출처.</summary>
public sealed record AvailablePreset(CommandPreset Preset, string SupplyNodeId)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Preset.DisplayName)
        ? Preset.Id
        : Preset.DisplayName;
}

/// <summary>
/// PresentationNode 하나가 지금 쓸 수 있는 커맨드 범주·정의·프리셋.
/// 편집기 드롭다운이 이것만 본다.
/// </summary>
public sealed class AvailablePresentationCommands
{
    public AvailablePresentationCommands(
        PresentationCommandCatalog catalog,
        IReadOnlyList<PresentationCategoryDefinition> categories,
        IReadOnlyList<AvailablePreset> presets,
        bool isRestricted)
    {
        Catalog = catalog;
        Categories = categories;
        Presets = presets;
        IsRestricted = isRestricted;
    }

    /// <summary>전체 게임 카탈로그. 정의 조회는 범위와 무관하게 여기서 한다.</summary>
    public PresentationCommandCatalog Catalog { get; }

    /// <summary>드롭다운에 보일 범주. 공급 노드가 연결되어 있으면 그 합집합이다.</summary>
    public IReadOnlyList<PresentationCategoryDefinition> Categories { get; }

    /// <summary>연결된 공급 노드들의 프리셋. 공급 순서 그대로다.</summary>
    public IReadOnlyList<AvailablePreset> Presets { get; }

    /// <summary>공급 노드가 하나라도 연결되어 있는지. 아니면 전체 카탈로그 폴백이다.</summary>
    public bool IsRestricted { get; }

    public IReadOnlyList<PresentationCommandDefinition> For(string categoryId) =>
        Catalog.For(categoryId);

    public AvailablePreset? FindPreset(string? presetId)
    {
        return presetId is null
            ? null
            : Presets.FirstOrDefault(preset =>
                string.Equals(preset.Preset.Id, presetId, StringComparison.Ordinal));
    }

    public IReadOnlyList<AvailablePreset> PresetsFor(string categoryId) =>
        Presets
            .Where(preset =>
                string.Equals(
                    Catalog.Find(preset.Preset.CommandDefinitionId)?.CategoryId,
                    categoryId,
                    StringComparison.Ordinal))
            .ToArray();
}

/// <summary>
/// PresentationNode가 쓸 수 있는 커맨드의 범위를 계산한다.
/// <see cref="AvailableConditionResolver"/>와 같은 모양이다 — 연결된 공급 노드들의
/// 범주 합집합과 프리셋이 후보가 되고, <b>공급 노드가 하나도 없으면 전체 카탈로그로
/// 폴백한다</b>(저작을 막지 않는다는 기존 원칙).
/// </summary>
public static class AvailablePresentationCommandResolver
{
    public static AvailablePresentationCommands Resolve(
        StoryProject project,
        string presentationNodeId,
        GameDefinition? definition = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        PresentationCommandCatalog catalog = PresentationCommandCatalog.For(definition);

        IReadOnlyList<CommandSupplyNode> supplies = ConnectedSupplyNodes(project, presentationNodeId);

        if (supplies.Count == 0)
        {
            return new AvailablePresentationCommands(
                catalog,
                catalog.Categories,
                Array.Empty<AvailablePreset>(),
                isRestricted: false);
        }

        var categoryIds = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var presets = new List<AvailablePreset>();

        foreach (CommandSupplyNode supply in supplies)
        {
            foreach (string categoryId in supply.Categories)
            {
                if (!string.IsNullOrWhiteSpace(categoryId) && seen.Add(categoryId))
                {
                    categoryIds.Add(categoryId);
                }
            }

            foreach (CommandPreset preset in supply.Presets)
            {
                presets.Add(new AvailablePreset(preset, supply.Id));
            }
        }

        // 카탈로그가 아는 범주만 보여 준다. 데이터 오타는 조용히 빈 드롭다운이 되기보다
        // 목록에서 빠지는 편이 낫다 — 어차피 정의가 없어 후보 커맨드도 없다.
        IReadOnlyList<PresentationCategoryDefinition> categories = categoryIds
            .Select(catalog.FindCategory)
            .Where(category => category is not null)
            .Cast<PresentationCategoryDefinition>()
            .ToArray();

        return new AvailablePresentationCommands(catalog, categories, presets, isRestricted: true);
    }

    /// <summary>이 연출 노드에 활성 CommandSupply link로 연결된 공급 노드들. 공급 순서대로.</summary>
    public static IReadOnlyList<CommandSupplyNode> ConnectedSupplyNodes(
        StoryProject project,
        string presentationNodeId)
    {
        return project.Links
            .Select((link, index) => (Link: link, Index: index))
            .Where(item =>
                item.Link.Kind == NodeLinkKind.CommandSupply &&
                item.Link.IsEnabled &&
                string.Equals(item.Link.TargetNodeId, presentationNodeId, StringComparison.Ordinal))
            .OrderBy(item => item.Link.Order)
            .ThenBy(item => item.Index)
            .Select(item => project.FindNode(item.Link.SourceNodeId) as CommandSupplyNode)
            .Where(node => node is not null)
            .Cast<CommandSupplyNode>()
            .ToArray();
    }

    /// <summary>
    /// 현재 연출 범위와 무관하게 프리셋 Id가 어디엔가 정의되어 있는지 찾는다.
    /// 발행 동결과 "연결이 끊긴 프리셋 vs 삭제된 프리셋" 구분에 쓴다.
    /// </summary>
    public static AvailablePreset? FindKnown(StoryProject project, string? presetId)
    {
        if (string.IsNullOrEmpty(presetId))
        {
            return null;
        }

        foreach (CommandSupplyNode supply in project.EnumerateNodes().OfType<CommandSupplyNode>())
        {
            if (supply.FindPreset(presetId) is { } preset)
            {
                return new AvailablePreset(preset, supply.Id);
            }
        }

        return null;
    }
}
