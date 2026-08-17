namespace Vn.Authoring.Chapters;

/// <param name="ReachableEpisodeIds">시작 에피소드에서 어떤 플레이로든 닿을 수 있는 에피소드.</param>
/// <param name="ExplorationComplete">
/// 상태공간을 끝까지 훑었는가. 상한에 걸려 중단했으면 false이고, 그때 "도달 불가"는
/// 단정이 아니라 경고로 낮춰 보고돼 있다 — 증명하지 못한 것을 오류라고 말하지 않는다.
/// </param>
/// <summary>
/// 그 에피소드에 <b>도착했을 때</b> 스탯 하나가 가질 수 있는 폭 (2026-08-17 소유자:
/// "간선을 따라 왔을 때 스탯의 변화량이 노드에 표시되도록. 여러 루트가 있을 때는 스탯의
/// 최소최대량을 표기").
///
/// 값은 <b>도착 직후</b>다 — 그 노드로 들어오는 간선의 증감까지 커밋한 뒤. 루트가 하나면
/// 최소·최대가 같고, 갈래가 여럿이면 벌어진다. 증명이 이미 (에피소드, 스탯 벡터)로 걷고
/// 있으므로 걷는 김에 적어 두는 것이고 따로 계산하지 않는다.
/// </summary>
public sealed record ChapterStatSpan(string Key, string DisplayName, int Minimum, int Maximum)
{
    /// <summary>어느 루트로 와도 같은 값인가.</summary>
    public bool IsFixed => Minimum == Maximum;
}

public sealed record ChapterReachabilityResult(
    IReadOnlySet<string> ReachableEpisodeIds,
    IReadOnlyList<ChapterDiagnostic> Diagnostics,
    bool ExplorationComplete,
    IReadOnlyDictionary<string, IReadOnlyList<ChapterStatSpan>>? StatSpans = null)
{
    public bool HasErrors =>
        Diagnostics.Any(item => item.Severity == ChapterDiagnosticSeverity.Error);

    /// <summary>그 에피소드 도착 시점의 스탯 폭. 못 가는 에피소드는 비어 있다.</summary>
    public IReadOnlyList<ChapterStatSpan> SpansFor(string episodeId) =>
        StatSpans is not null && StatSpans.TryGetValue(episodeId, out IReadOnlyList<ChapterStatSpan>? spans)
            ? spans
            : [];
}

/// <summary>
/// 정적 도달성 증명 (G7, §0.5 보증 2) — "작가가 무엇을 저장하든, 특정 에피소드로 절대 못 가는
/// 상태를 만들 수 없다"의 장치.
///
/// 상태 = (에피소드, 스탯 정수 벡터). 스탯이 2~5개·정수·`최소~최대` 유한 범위라 상태공간이
/// 유한하고 <b>완전 탐색이 된다</b> — 경계값 버그를 막으려던 정수 고정(G-3)이 이 증명을
/// 가능하게 만들었다. float이면 여기서 결정 불가능이다.
///
/// 에피소드 하나를 플레이하면 스탯이 그 에피소드의 증감 범위(<see cref="EpisodeStatRangeCalculator"/>)
/// 안에서 변한다. 범위는 과대근사이므로 "도달 가능" 판정은 넓게, 즉 <b>진짜 도달 불가를
/// 놓치는 일은 없게</b> 잡힌다.
///
/// <c>cleared:</c> 조건은 도달 가능 집합 자체를 참조하므로 고정점 반복으로 푼다 —
/// 집합은 단조 증가라 반드시 수렴한다.
/// </summary>
public static class ChapterReachabilityProver
{
    /// <summary>완전 탐색 상한. 스탯 5개 × 범위 0~10이라도 이 안에 넉넉히 든다.</summary>
    private const int StateLimit = 250_000;

    /// <summary>
    /// 스탯 증감의 원천은 <b>간선</b> 하나다 (2026-08-14 소유자 결정). 에피소드 안에서는
    /// 스탯이 변하지 않으므로, 옛 "에피소드별 증감 범위" 과대근사 없이 간선의 정확값으로
    /// 전이한다 — 증명이 근사가 아니게 됐다.
    /// </summary>
    public static ChapterReachabilityResult Prove(ChapterGraphModel chapter)
    {
        ArgumentNullException.ThrowIfNull(chapter);

        var diagnostics = new List<ChapterDiagnostic>();

        if (chapter.Episodes.Count == 0)
        {
            return new ChapterReachabilityResult(
                new HashSet<string>(StringComparer.Ordinal), diagnostics, ExplorationComplete: true);
        }

        // 시작 규칙은 모델의 것 하나를 쓴다 — G8의 StartEpisodeId와 갈리면 안 된다.
        ChapterEpisode start = chapter.StartEpisode!;

        var reachable = new HashSet<string>(StringComparer.Ordinal);
        bool complete = true;
        int[] maxSeen = chapter.Stats.Select(stat => stat.Initial).ToArray();
        int[] minSeen = chapter.Stats.Select(stat => stat.Initial).ToArray();

        // 에피소드별 도착 시점 폭 (2026-08-17) — 걷는 김에 적는다.
        var spans = new Dictionary<string, (int[] Min, int[] Max)>(StringComparer.Ordinal);

        // cleared:가 도달 가능 집합을 참조하므로, 집합이 자라지 않을 때까지 탐색을 반복한다.
        while (true)
        {
            (HashSet<string> found, bool finished) =
                Explore(chapter, start, reachable, maxSeen, minSeen, spans);

            complete &= finished;
            AddReachableAttachments(chapter, found, maxSeen, minSeen);

            if (found.Count == reachable.Count)
            {
                break;
            }

            reachable = found;
        }

        ReportUnreachable(chapter, reachable, maxSeen, minSeen, complete, diagnostics);

        return new ChapterReachabilityResult(reachable, diagnostics, complete, BuildSpans(chapter, spans));
    }

    // ── 탐색 ────────────────────────────────────────────────────────────────

    private static (HashSet<string> Reachable, bool Complete) Explore(
        ChapterGraphModel chapter,
        ChapterEpisode start,
        IReadOnlySet<string> clearedAssumption,
        int[] maxSeen,
        int[] minSeen,
        Dictionary<string, (int[] Min, int[] Max)> spans)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal) { start.EpisodeId };
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(string EpisodeId, int[] Stats)>();

        int[] initial = chapter.Stats.Select(stat => stat.Initial).ToArray();
        queue.Enqueue((start.EpisodeId, initial));
        visited.Add(StateKey(start.EpisodeId, initial));
        Observe(initial, maxSeen, minSeen);
        ObserveAt(start.EpisodeId, initial, spans);

        while (queue.Count > 0)
        {
            if (visited.Count > StateLimit)
            {
                return (reachable, false);
            }

            (string episodeId, int[] stats) = queue.Dequeue();

            foreach (ChapterEdge edge in chapter.Edges.Where(edge =>
                         string.Equals(edge.FromEpisodeId, episodeId, StringComparison.Ordinal)))
            {
                ChapterEpisode? target = chapter.FindEpisode(edge.ToEpisodeId);

                if (target is null)
                {
                    continue; // 없는 도착지는 구조 검증이 이미 오류로 잡았다.
                }

                // 관문 판정은 커밋 전 값으로 — 플레이어가 선택지를 보는 시점의 값이다.
                // 관문은 전부 간선이 갖는다 (v8) — 표시조건과 해금조건 둘 다 서야 탄다.
                if (!Satisfied(chapter, edge.VisibleConditionLabel, stats, clearedAssumption) ||
                    !Satisfied(chapter, edge.ConditionLabel, stats, clearedAssumption))
                {
                    continue;
                }

                // 간선을 타는 순간 증감이 1회 커밋된다 — 근사 없는 정확 전이.
                int[] next = ApplyDeltas(chapter, stats, edge.StatChanges);
                Observe(next, maxSeen, minSeen);
                ObserveAt(target.EpisodeId, next, spans); // 도착 시점 값 = 증감 커밋 뒤

                reachable.Add(target.EpisodeId);
                string key = StateKey(target.EpisodeId, next);

                if (visited.Add(key))
                {
                    queue.Enqueue((target.EpisodeId, next));
                }
            }
        }

        return (reachable, true);
    }

    /// <summary>간선 증감 커밋. `최소~최대`는 탐색 경계라 밖은 잘라낸다.</summary>
    internal static int[] ApplyDeltas(
        ChapterGraphModel chapter, int[] stats, IReadOnlyList<StatDelta> deltas)
    {
        if (deltas.Count == 0)
        {
            return stats;
        }

        var next = (int[])stats.Clone();

        foreach (StatDelta delta in deltas)
        {
            for (int index = 0; index < chapter.Stats.Count; index++)
            {
                if (string.Equals(chapter.Stats[index].Key, delta.Key, StringComparison.Ordinal))
                {
                    next[index] = Math.Clamp(
                        next[index] + delta.Amount,
                        chapter.Stats[index].Minimum,
                        chapter.Stats[index].Maximum);
                    break;
                }
            }
        }

        return next;
    }

    private static bool Satisfied(
        ChapterGraphModel chapter,
        string? conditionLabel,
        int[] stats,
        IReadOnlySet<string> cleared)
    {
        if (string.IsNullOrEmpty(conditionLabel))
        {
            return true;
        }

        ChapterCondition? condition = chapter.FindCondition(conditionLabel);

        if (condition is null || !condition.IsValid)
        {
            // 미정의·깨진 라벨은 구조 검증의 오류다. 여기서는 "지나갈 수 없음"으로만 다룬다 —
            // 깨진 조건을 통과시켜 도달 가능을 부풀리지 않는다.
            return false;
        }

        foreach (ConditionTerm term in condition.Parsed)
        {
            bool holds = term.Kind == ConditionTermKind.EpisodeCleared
                ? cleared.Contains(term.Key)
                : CompareStat(chapter, term, stats);

            if (!holds)
            {
                return false;
            }
        }

        return true;
    }

    private static bool CompareStat(ChapterGraphModel chapter, ConditionTerm term, int[] stats)
    {
        int index = IndexOfStat(chapter, term.Key);

        if (index < 0)
        {
            return false; // 미등록 스탯키 — 구조 검증이 이미 오류로 잡았다.
        }

        return term.Comparison switch
        {
            ConditionComparison.AtLeast => stats[index] >= term.Value,
            ConditionComparison.AtMost => stats[index] <= term.Value,
            ConditionComparison.Above => stats[index] > term.Value,
            ConditionComparison.Below => stats[index] < term.Value,
            _ => stats[index] == term.Value
        };
    }

    /// <summary>
    /// 부착(Attachment) 에피소드는 간선으로 들어가는 노드가 아니다 — 런타임이 부모 곁에
    /// 띄우는 사이드이며 `NextOption`의 대상이 될 수 없다(런타임 확인). 그래서 도달 판정도
    /// 간선이 아니라 <b>관문 조건이 어느 도달 상태에서든 만족될 수 있는가</b>로 한다.
    ///
    /// 만족 가능성은 탐색이 본 스탯의 겉둘레(최소·최대)로 판정한다 — 과대근사라 "도달 가능"이
    /// 넓게 잡히고, 부착의 진짜 도달 불가(예: 영원히 못 미치는 스탯 관문)는 그대로 잡힌다.
    /// v1의 Attachment은 읽고 표시까지다(D5) — 이 근사가 그 범위에 맞는 값이다.
    /// </summary>
    private static void AddReachableAttachments(
        ChapterGraphModel chapter,
        HashSet<string> reachable,
        int[] maxSeen,
        int[] minSeen)
    {
        foreach (ChapterEpisode episode in chapter.Episodes.Where(episode =>
                     string.Equals(episode.Kind, "Attachment", StringComparison.OrdinalIgnoreCase) &&
                     !reachable.Contains(episode.EpisodeId)))
        {
            // v8 — 관문은 들어오는 길이 갖는다. 부착은 간선이 없을 수 있는데(사이드),
            // 그때는 관문도 없으므로 겉둘레 안에서 언제나 성립한다.
            List<ChapterEdge> incoming = chapter.Edges
                .Where(edge => string.Equals(edge.ToEpisodeId, episode.EpisodeId, StringComparison.Ordinal))
                .ToList();

            bool satisfiable = incoming.Count == 0 || incoming.Any(edge =>
                SatisfiableWithinEnvelope(chapter, edge.VisibleConditionLabel, reachable, maxSeen, minSeen) &&
                SatisfiableWithinEnvelope(chapter, edge.ConditionLabel, reachable, maxSeen, minSeen));

            if (satisfiable)
            {
                reachable.Add(episode.EpisodeId);
            }
        }
    }

    private static bool SatisfiableWithinEnvelope(
        ChapterGraphModel chapter,
        string? conditionLabel,
        IReadOnlySet<string> cleared,
        int[] maxSeen,
        int[] minSeen)
    {
        if (string.IsNullOrEmpty(conditionLabel))
        {
            return true;
        }

        ChapterCondition? condition = chapter.FindCondition(conditionLabel);

        if (condition is null || !condition.IsValid)
        {
            return false;
        }

        foreach (ConditionTerm term in condition.Parsed)
        {
            if (term.Kind == ConditionTermKind.EpisodeCleared)
            {
                if (!cleared.Contains(term.Key))
                {
                    return false;
                }

                continue;
            }

            int index = IndexOfStat(chapter, term.Key);

            if (index < 0)
            {
                return false;
            }

            bool satisfiable = term.Comparison switch
            {
                ConditionComparison.AtLeast => maxSeen[index] >= term.Value,
                ConditionComparison.AtMost => minSeen[index] <= term.Value,
                ConditionComparison.Above => maxSeen[index] > term.Value,
                ConditionComparison.Below => minSeen[index] < term.Value,
                _ => minSeen[index] <= term.Value && term.Value <= maxSeen[index]
            };

            if (!satisfiable)
            {
                return false;
            }
        }

        return true;
    }

    // ── 보고 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 도달 불가를 <b>원인 조건까지 지목해</b> 보고한다 (Gate C 1번). 기본은 오류이고,
    /// `도달불가 허용`이 켜진 에피소드는 알림으로 낮춘다 — 의도임이 표시된 것이므로(D3).
    /// </summary>
    private static void ReportUnreachable(
        ChapterGraphModel chapter,
        IReadOnlySet<string> reachable,
        int[] maxSeen,
        int[] minSeen,
        bool complete,
        List<ChapterDiagnostic> diagnostics)
    {
        foreach (ChapterEpisode episode in chapter.Episodes.Where(episode =>
                     !reachable.Contains(episode.EpisodeId)))
        {
            string cause = DiagnoseCause(chapter, episode, reachable, maxSeen, minSeen);

            ChapterDiagnosticSeverity severity = episode.AllowUnreachable
                ? ChapterDiagnosticSeverity.Info
                : complete
                    ? ChapterDiagnosticSeverity.Error
                    : ChapterDiagnosticSeverity.Warning;

            string prefix = episode.AllowUnreachable
                ? $"'{episode.EpisodeId}'는 도달 불가이지만 `도달불가 허용`이 켜져 있습니다"
                : complete
                    ? $"'{episode.EpisodeId}'에 도달할 수 있는 경로가 없습니다"
                    : $"'{episode.EpisodeId}'에 도달하는 경로를 찾지 못했습니다(탐색이 상한에서 중단됨)";

            diagnostics.Add(new ChapterDiagnostic(
                severity,
                ChapterDiagnosticCode.EpisodeUnreachable,
                chapter.SourcePath,
                ChapterSheetNames.Episodes,
                episode.SourceRow,
                "A",
                $"{prefix}. {cause}"));
        }
    }

    private static string DiagnoseCause(
        ChapterGraphModel chapter,
        ChapterEpisode episode,
        IReadOnlySet<string> reachable,
        int[] maxSeen,
        int[] minSeen)
    {
        List<ChapterEdge> incoming = chapter.Edges
            .Where(edge => string.Equals(edge.ToEpisodeId, episode.EpisodeId, StringComparison.Ordinal))
            .ToList();

        if (incoming.Count == 0)
        {
            return "이 에피소드로 들어오는 간선이 `간선` 시트에 없습니다.";
        }

        if (incoming.All(edge => !reachable.Contains(edge.FromEpisodeId)))
        {
            return $"들어오는 간선의 출발점({string.Join(", ", incoming.Select(edge => edge.FromEpisodeId))})부터 " +
                "도달 불가입니다.";
        }

        // 관문이 되는 조건들에서 만족 불가능한 항을 찾는다 (v8 — 전부 간선의 것).
        var labels = incoming.Select(edge => edge.ConditionLabel)
            .Concat(incoming.Select(edge => edge.VisibleConditionLabel))
            .Where(label => !string.IsNullOrEmpty(label))
            .Distinct(StringComparer.Ordinal);

        foreach (string? label in labels)
        {
            ChapterCondition? condition = chapter.FindCondition(label!);

            if (condition is null)
            {
                continue;
            }

            foreach (ConditionTerm term in condition.Parsed)
            {
                if (term.Kind == ConditionTermKind.EpisodeCleared && !reachable.Contains(term.Key))
                {
                    return $"조건 '{label}'의 cleared:{term.Key}가 원인입니다 — '{term.Key}' 자체가 도달 불가입니다.";
                }

                int index = IndexOfStat(chapter, term.Key);

                if (index < 0)
                {
                    continue;
                }

                if (term.Comparison is ConditionComparison.AtLeast or ConditionComparison.Above &&
                    maxSeen[index] < term.Value + (term.Comparison == ConditionComparison.Above ? 1 : 0))
                {
                    string op = term.Comparison == ConditionComparison.Above ? ">" : ">=";
                    return $"조건 '{label}'의 '{term.Key} {op} {term.Value}'가 원인입니다 — " +
                        $"어떤 경로로도 {term.Key}는 최대 {maxSeen[index]}까지밖에 오르지 않습니다.";
                }

                if (term.Comparison is ConditionComparison.AtMost or ConditionComparison.Below &&
                    minSeen[index] > term.Value - (term.Comparison == ConditionComparison.Below ? 1 : 0))
                {
                    string op = term.Comparison == ConditionComparison.Below ? "<" : "<=";
                    return $"조건 '{label}'의 '{term.Key} {op} {term.Value}'가 원인입니다 — " +
                        $"어떤 경로로도 {term.Key}는 최소 {minSeen[index]} 아래로 내려가지 않습니다.";
                }
            }
        }

        return "관문 조건들의 조합을 만족하는 경로가 없습니다.";
    }

    private static void Observe(int[] stats, int[] maxSeen, int[] minSeen)
    {
        for (int index = 0; index < stats.Length; index++)
        {
            maxSeen[index] = Math.Max(maxSeen[index], stats[index]);
            minSeen[index] = Math.Min(minSeen[index], stats[index]);
        }
    }

    /// <summary>그 에피소드에 이 값으로 도착했다 — 에피소드별 폭을 넓힌다.</summary>
    private static void ObserveAt(
        string episodeId, int[] stats, Dictionary<string, (int[] Min, int[] Max)> spans)
    {
        if (!spans.TryGetValue(episodeId, out (int[] Min, int[] Max) span))
        {
            spans[episodeId] = ((int[])stats.Clone(), (int[])stats.Clone());
            return;
        }

        for (int index = 0; index < stats.Length; index++)
        {
            span.Min[index] = Math.Min(span.Min[index], stats[index]);
            span.Max[index] = Math.Max(span.Max[index], stats[index]);
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<ChapterStatSpan>> BuildSpans(
        ChapterGraphModel chapter, Dictionary<string, (int[] Min, int[] Max)> spans)
    {
        var built = new Dictionary<string, IReadOnlyList<ChapterStatSpan>>(StringComparer.Ordinal);

        foreach ((string episodeId, (int[] min, int[] max)) in spans)
        {
            var list = new List<ChapterStatSpan>(chapter.Stats.Count);

            for (int index = 0; index < chapter.Stats.Count && index < min.Length; index++)
            {
                ChapterStat stat = chapter.Stats[index];
                list.Add(new ChapterStatSpan(stat.Key, stat.DisplayName, min[index], max[index]));
            }

            built[episodeId] = list;
        }

        return built;
    }

    private static int IndexOfStat(ChapterGraphModel chapter, string key)
    {
        for (int index = 0; index < chapter.Stats.Count; index++)
        {
            if (string.Equals(chapter.Stats[index].Key, key, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static string StateKey(string episodeId, int[] stats) =>
        episodeId + "|" + string.Join(',', stats);

    private sealed class IntArrayComparer : IEqualityComparer<int[]>
    {
        public static IntArrayComparer Instance { get; } = new();

        public bool Equals(int[]? left, int[]? right) =>
            left is not null && right is not null && left.AsSpan().SequenceEqual(right);

        public int GetHashCode(int[] value)
        {
            var hash = new HashCode();

            foreach (int item in value)
            {
                hash.Add(item);
            }

            return hash.ToHashCode();
        }
    }
}
