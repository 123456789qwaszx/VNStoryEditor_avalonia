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
/// 조건이 묻는 것은 <b>스탯뿐이고</b>, 스탯은 걷는 도중에 정해진다 — 그래서 한 번만 걷는다.
/// 2026-08-25까지는 고정점 반복이 있었는데, 도달 가능 집합 자체를 참조하는 <c>cleared:</c>
/// 조건 때문이었다. 그 조건이 폐지되면서 두 번째 바퀴가 새로 찾을 것이 없어졌다.
///
/// <para>
/// ⚠⚠ <b>이 구현은 2026-08-18부터 남의 오라클이다. 고치기 전에 알려야 한다.</b>
/// </para>
/// <para>
/// `ked-progression`이 이 증명기를 자기 쪽으로 옮기면서, 등가성을 주장이 아니라 <b>코퍼스</b>로
/// 고정했다: 여기 있던 그대로의 구현을 케이스들에 돌려 그때의 답을 저장했고
/// (저쪽 <c>Tests/Fixtures/reachability-oracle.json</c>), 저쪽 테스트가 매번 그 답과 대조한다.
///
/// 2026-08-25에 코퍼스가 <b>여섯으로 줄었다</b>(직선·분기수렴·관문·도달불가·간선없는노드·clamp) —
/// <c>cleared:</c> 고정점과 부착 케이스는 그 개념이 계약에서 사라지면서 함께 빠졌다.
/// </para>
/// <para>
/// 그래서 여기 동작을 바꾸면 — <b>버그 수정이라도</b> — 두 구현이 조용히 갈린다. 저쪽 코퍼스는
/// 옛 답을 들고 있고 저쪽 테스트는 계속 초록이라, <b>갈렸다는 것을 아무도 모른다.</b>
/// 바꿀 일이 생기면 저쪽에 말해 코퍼스를 다시 뽑고 함께 맞춘다.
/// </para>
/// <para>
/// 화면이 이 결과를 (내용해시 → 결과) 캐시 뒤에서 부르는 것은 <b>상관없다</b> — 캐시는
/// 화면의 사정이고 답을 바꾸지 않는다.
/// </para>
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

        int[] maxSeen = chapter.Stats.Select(stat => stat.Initial).ToArray();
        int[] minSeen = chapter.Stats.Select(stat => stat.Initial).ToArray();

        // 에피소드별 도착 시점 폭 (2026-08-17) — 걷는 김에 적는다.
        var spans = new Dictionary<string, (int[] Min, int[] Max)>(StringComparer.Ordinal);

        HashSet<string> reachable;
        bool complete;

        // 한 번만 걷는다 (2026-08-25). 고정점 반복이 있던 이유는 `cleared:`가 도달 가능
        // 집합 자체를 참조해서였는데, 그 조건이 폐지되면서 조건이 참조하는 것은 스탯뿐이
        // 됐다 — 스탯은 걷는 도중에 정해지므로 두 번째 바퀴가 새로 찾을 것이 없다.
        (reachable, complete) = Explore(chapter, start, maxSeen, minSeen, spans);

        ReportUnreachable(chapter, reachable, maxSeen, minSeen, complete, diagnostics);

        return new ChapterReachabilityResult(reachable, diagnostics, complete, BuildSpans(chapter, spans));
    }

    // ── 탐색 ────────────────────────────────────────────────────────────────

    private static (HashSet<string> Reachable, bool Complete) Explore(
        ChapterGraphModel chapter,
        ChapterEpisode start,
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
                if (!Satisfied(chapter, edge.VisibleConditionLabel, stats) ||
                    !Satisfied(chapter, edge.ConditionLabel, stats))
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

    /// <summary>
    /// 간선의 스탯 변화 커밋. `최소~최대`는 탐색 경계라 밖은 잘라낸다.
    ///
    /// <b>지정(Set)은 지금 값을 보지 않는다</b> (2026-08-19) — 깃발을 켜는 것이 "이전에
    /// 켜져 있었는가"에 따라 달라지면 그건 켜는 게 아니다. 증감만 있던 시절에는 이 함수가
    /// 언제나 단조로웠지만, 이제는 아니다.
    ///
    /// ⚠ 이 함수는 `ked-progression`의 <b>고정된 오라클</b>이다(파일 맨 위 참조). 지정을
    /// 더한 것은 동작 변경이므로 저쪽에 알렸다 — 다만 코퍼스 일곱 케이스에는 지정이 하나도
    /// 없어 <b>기존 답은 한 줄도 바뀌지 않는다.</b>
    /// </summary>
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
                    int raw = delta.IsSet ? delta.Amount : next[index] + delta.Amount;

                    next[index] = Math.Clamp(
                        raw,
                        chapter.Stats[index].Minimum,
                        chapter.Stats[index].Maximum);
                    break;
                }
            }
        }

        return next;
    }

    // 판정의 단일 구현은 ChapterGateJudge다 (2026-08-27) — 워커·무대 프리뷰와 한 벌.
    // 미정의·깨진 라벨(Broken)도 "지나갈 수 없음"이다: 깨진 조건을 통과시켜 도달 가능을
    // 부풀리지 않는다.
    private static bool Satisfied(
        ChapterGraphModel chapter,
        string? conditionLabel,
        int[] stats) =>
        ChapterGateJudge.Judge(chapter, conditionLabel, stats) == ChapterGateVerdict.Open;

    // ⛔ `AddReachableAttachments`·`SatisfiableWithinEnvelope`는 2026-08-25에 사라졌다.
    //    부착(Attachment) 에피소드를 "간선이 아니라 관문 만족 가능성으로" 따로 판정하던
    //    자리인데, 코어가 `EpisodeKind`를 통째로 지우면서 부착이라는 종류 자체가 계약에서
    //    없어졌다. 남겨 두면 툴만 부착을 도달 가능으로 세어 <b>증명과 플레이가 갈린다</b> —
    //    이 증명기가 있는 이유가 정확히 그것을 없애는 것이다(G7).
    //    저쪽 오라클(`reachability-oracle.json`)에서도 `부착` 케이스가 함께 빠졌다.

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
