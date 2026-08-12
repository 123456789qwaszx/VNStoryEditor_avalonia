namespace Vn.Authoring.Chapters;

/// <param name="EpisodeIds">지나간 에피소드, 순서대로. 첫 항목은 언제나 시작 에피소드다.</param>
/// <param name="StoppedBecause">멈춘 이유. 끝까지 갔으면 null이다.</param>
public sealed record FixtureWalkResult(
    IReadOnlyList<string> EpisodeIds,
    string? StoppedBecause);

/// <summary>
/// G6 — 픽스처("이 조건에서 어디로 가나")의 실제 경로를 계산한다.
///
/// 도달성 증명("어떤 조건에서든 갈 수 있나")과는 다른 도구다: 증명은 모든 가능성을 탐색하고,
/// 픽스처는 <b>고정된 스탯과 고정된 선택</b>으로 한 판을 걸어 본다. 스탯은 픽스처 값으로
/// 고정된다 — 재생루트를 눈으로 확인하는 도구라, 변화량까지 시뮬하면 "이 조건에서"가 아니게 된다.
/// </summary>
public static class ChapterFixtureWalker
{
    public static FixtureWalkResult Walk(ChapterGraphModel chapter, ChapterFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(chapter);
        ArgumentNullException.ThrowIfNull(fixture);

        var path = new List<string>();

        if (chapter.StartEpisode is not { } start)
        {
            return new FixtureWalkResult(path, "에피소드가 없습니다.");
        }

        int[] stats = chapter.Stats
            .Select(stat => fixture.Stats.GetValueOrDefault(stat.Key, stat.Initial))
            .ToArray();

        var cleared = new HashSet<string>(StringComparer.Ordinal);
        var fixedChoices = fixture.Choices.ToDictionary(
            choice => choice.From, choice => choice.To, StringComparer.Ordinal);

        string current = start.EpisodeId;

        while (true)
        {
            if (path.Contains(current))
            {
                return new FixtureWalkResult(path, $"'{current}'를 다시 만나 순환에서 멈췄습니다.");
            }

            path.Add(current);
            cleared.Add(current);

            List<ChapterEdge> passable = chapter.Edges
                .Where(edge => string.Equals(edge.FromEpisodeId, current, StringComparison.Ordinal))
                .Where(edge =>
                    chapter.FindEpisode(edge.ToEpisodeId) is { } target &&
                    Satisfied(chapter, edge.ConditionLabel, stats, cleared) &&
                    Satisfied(chapter, target.VisibleConditionLabel, stats, cleared) &&
                    Satisfied(chapter, target.UnlockConditionLabel, stats, cleared))
                .ToList();

            if (passable.Count == 0)
            {
                return new FixtureWalkResult(path, null); // 자연 종료 — 여기가 이 픽스처의 끝이다.
            }

            ChapterEdge? next = passable.Count == 1
                ? passable[0]
                : PickByFixture(passable, fixedChoices, current);

            if (next is null)
            {
                return new FixtureWalkResult(path,
                    $"'{current}'에서 갈래가 {passable.Count}개인데 픽스처의 고정 선택이 없습니다. " +
                    $"고정 선택 열에 '{current}→…'을 적어 주세요.");
            }

            current = next.ToEpisodeId;
        }
    }

    private static ChapterEdge? PickByFixture(
        IReadOnlyList<ChapterEdge> passable,
        IReadOnlyDictionary<string, string> fixedChoices,
        string current)
    {
        if (!fixedChoices.TryGetValue(current, out string? target))
        {
            // 고정 선택이 없어도 분기 없는 일반 진행이 하나뿐이면 그걸 탄다.
            List<ChapterEdge> plain = passable.Where(edge => edge.IsPlainAdvance).ToList();
            return plain.Count == 1 ? plain[0] : null;
        }

        return passable.FirstOrDefault(edge =>
            string.Equals(edge.ToEpisodeId, target, StringComparison.Ordinal));
    }

    private static bool Satisfied(
        ChapterGraphModel chapter, string? label, int[] stats, IReadOnlySet<string> cleared)
    {
        if (string.IsNullOrEmpty(label))
        {
            return true;
        }

        if (chapter.FindCondition(label) is not { IsValid: true } condition)
        {
            return false;
        }

        foreach (ConditionTerm term in condition.Parsed)
        {
            bool holds;

            if (term.Kind == ConditionTermKind.EpisodeCleared)
            {
                holds = cleared.Contains(term.Key);
            }
            else
            {
                int index = -1;

                for (int position = 0; position < chapter.Stats.Count; position++)
                {
                    if (string.Equals(chapter.Stats[position].Key, term.Key, StringComparison.Ordinal))
                    {
                        index = position;
                        break;
                    }
                }

                holds = index >= 0 && term.Comparison switch
                {
                    ConditionComparison.AtLeast => stats[index] >= term.Value,
                    ConditionComparison.AtMost => stats[index] <= term.Value,
                    _ => stats[index] == term.Value
                };
            }

            if (!holds)
            {
                return false;
            }
        }

        return true;
    }
}
