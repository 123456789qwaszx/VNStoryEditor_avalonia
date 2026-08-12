namespace Vn.Authoring.Chapters;

/// <summary>한 에피소드가 스탯 하나에 줄 수 있는 증감의 범위. 갈래별 최대·최소다 (G7).</summary>
public sealed record StatDeltaRange(int Minimum, int Maximum)
{
    public static StatDeltaRange Zero { get; } = new(0, 0);

    public StatDeltaRange Plus(StatDeltaRange other) =>
        new(Minimum + other.Minimum, Maximum + other.Maximum);

    /// <summary>이 갈래를 타거나 안 타거나 — 0과의 합집합 범위.</summary>
    public StatDeltaRange OrSkip() => new(Math.Min(Minimum, 0), Math.Max(Maximum, 0));
}

/// <summary>
/// 에피소드 워크북에서 "이 에피소드를 한 번 플레이하면 각 스탯이 얼마나 변할 수 있는가"를
/// 계산한다 (G7 도달성 증명의 간선 가중치).
///
/// 규격의 계산법 그대로다 — "그 에피소드 엑셀의 `스탯변화` 합(갈래별 최대·최소)":
/// 주 흐름의 합은 언제나 적용되고, 조건 구간은 타거나 안 타거나, 선택지는 옵션 중 하나다.
/// 결과는 <b>과대근사</b>다(실제로 불가능한 조합도 범위에 들 수 있다) — 도달성 증명에서
/// 과대근사는 "도달 가능"을 넓게 잡으므로, 진짜 도달 불가는 절대 놓치지 않는 안전한 방향이다.
/// </summary>
public static class EpisodeStatRangeCalculator
{
    /// <returns>스탯키 → 증감 범위. 워크북에 없는 스탯은 (0,0)이다.</returns>
    public static IReadOnlyDictionary<string, StatDeltaRange> Calculate(
        EpisodeWorkbookModel model,
        IReadOnlyCollection<string> statKeys)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(statKeys);

        var ranges = statKeys.ToDictionary(
            key => key, _ => StatDeltaRange.Zero, StringComparer.Ordinal);

        var inSection = model.Sections.Values
            .SelectMany(section => section.Rows)
            .Select(row => row.Index)
            .ToHashSet();

        // 주 흐름(구간 밖) — 언제나 적용된다.
        foreach (EpisodeRow row in model.Rows.Where(row =>
                     !inSection.Contains(row.Index) && row.Kind == EpisodeRowKind.Dialogue))
        {
            foreach (StatDelta delta in row.StatChanges)
            {
                Add(ranges, delta.Key, new StatDeltaRange(delta.Amount, delta.Amount));
            }
        }

        // 조건 구간 — 타거나 안 타거나.
        foreach (EpisodeRow caller in model.Rows.Where(row =>
                     row.Kind == EpisodeRowKind.If && row.In is not null))
        {
            foreach ((string key, StatDeltaRange sum) in SectionSums(model, caller.In!.Value, statKeys))
            {
                Add(ranges, key, sum.OrSkip());
            }
        }

        // 선택지 — 옵션 중 정확히 하나. 옵션마다 구간 합(없으면 0)을 구해 최소·최대를 취한다.
        List<EpisodeRow> options = model.Rows
            .Where(row => row.Kind == EpisodeRowKind.Option)
            .ToList();

        if (options.Count > 0)
        {
            foreach (string key in statKeys)
            {
                var perOption = options
                    .Select(option => option.In is int start
                        ? SectionSums(model, start, statKeys).GetValueOrDefault(key, StatDeltaRange.Zero)
                        : StatDeltaRange.Zero)
                    .ToList();

                Add(ranges, key, new StatDeltaRange(
                    perOption.Min(range => range.Minimum),
                    perOption.Max(range => range.Maximum)));
            }
        }

        return ranges;
    }

    /// <summary>구간 안 행들의 스탯변화 합. 구간 안은 전부 함께 재생되므로 단순 합이다.</summary>
    private static Dictionary<string, StatDeltaRange> SectionSums(
        EpisodeWorkbookModel model, int sectionStart, IReadOnlyCollection<string> statKeys)
    {
        var sums = statKeys.ToDictionary(
            key => key, _ => StatDeltaRange.Zero, StringComparer.Ordinal);

        if (!model.Sections.TryGetValue(sectionStart, out EpisodeSection? section))
        {
            return sums;
        }

        foreach (EpisodeRow row in section.Rows)
        {
            foreach (StatDelta delta in row.StatChanges)
            {
                Add(sums, delta.Key, new StatDeltaRange(delta.Amount, delta.Amount));
            }
        }

        return sums;
    }

    private static void Add(
        Dictionary<string, StatDeltaRange> ranges, string key, StatDeltaRange amount)
    {
        if (ranges.TryGetValue(key, out StatDeltaRange? current))
        {
            ranges[key] = current.Plus(amount);
        }
    }
}
