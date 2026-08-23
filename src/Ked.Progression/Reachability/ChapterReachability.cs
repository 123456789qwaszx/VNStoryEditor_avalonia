using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    // 특정 에피소드 도달 시,
    // 가능한 스탯 폭 계산.
    public sealed class StatSpan
    {
        public string Key { get; }
        public string DisplayName { get; }
        public int Minimum { get; }
        public int Maximum { get; }

        // 모든 루트에서 동일한 지.
        public bool IsFixed => Minimum == Maximum;

        public StatSpan(string key, string displayName, int minimum, int maximum)
        {
            Key = key;
            DisplayName = displayName;
            Minimum = minimum;
            Maximum = maximum;
        }

        public override string ToString() =>
            IsFixed ? $"{Key}={Minimum}" : $"{Key}={Minimum}~{Maximum}";
    }

    public enum UnreachableCause
    {
        // 들어오는 간선이 아예 없음.
        NoIncomingEdge = 0,

        // 들어오는 간선의 출발점부터 도달 불가.
        SourcesUnreachable = 1,

        // 관문 조건이 어떤 경로로도 만족되지 않음.
        BlockedByCondition = 2,

        // 기타 - 탐색이 상한에서 끊겼을 때 주로 나올 것으로 예상 됨.
        Undetermined = 3,
    }

    public sealed class UnreachableEpisode
    {
        public string EpisodeId { get; }
        public UnreachableCause Cause { get; }

        // UnreachableCause.BlockedByCondition
        public ProgressionCondition BlockingCondition { get; }

        public UnreachableEpisode(
            string episodeId, UnreachableCause cause, ProgressionCondition blockingCondition)
        {
            EpisodeId = episodeId;
            Cause = cause;
            BlockingCondition = blockingCondition;
        }

        public override string ToString() =>
            BlockingCondition.IsConstructed
                ? $"{EpisodeId}: {Cause}({BlockingCondition})"
                : $"{EpisodeId}: {Cause}";
    }

    public sealed class ReachabilityResult
    {
        private readonly Dictionary<string, IReadOnlyList<StatSpan>> _spans;
        private readonly HashSet<string> _reachable;

        // 어떤 플레이로든 닿을 수 있는 에피소드.
        public IReadOnlyCollection<string> ReachableEpisodeIds => _reachable;

        public IReadOnlyList<UnreachableEpisode> Unreachable { get; }

        // 상태공간을 끝까지 훑었는지 체크. 상한에 걸려 중단 시, false.
        public bool ExplorationComplete { get; }

        public ReachabilityResult(
            HashSet<string> reachableEpisodeIds,
            IReadOnlyList<UnreachableEpisode> unreachable,
            bool explorationComplete,
            Dictionary<string, IReadOnlyList<StatSpan>> spans)
        {
            _reachable = reachableEpisodeIds;
            Unreachable = unreachable;
            ExplorationComplete = explorationComplete;
            _spans = spans;
        }

        // 특정 에피소드 도착 시 가능한 스탯 폭.
        public IReadOnlyList<StatSpan> SpansFor(string episodeId) =>
            episodeId != null && _spans.TryGetValue(episodeId, out IReadOnlyList<StatSpan> spans)
                ? spans
                : Array.Empty<StatSpan>();

        public bool IsReachable(string episodeId) =>
            episodeId != null && _reachable.Contains(episodeId);
    }

    // "작가가 무엇을 저장하든 특정 에피소드로 절대 못 가는 상태를 만들 수 없도록 스탯의 계층을 분리.
    //
    // 상태 = (에피소드, 스탯 정수 벡터). 스탯이 2~5개·정수·유한 범위라 상태 공간이 정적이고 유한함.
    // 완전 탐색이 되도록 설계.
    //
    // 스탯 증감의 원천은 간선 하나.
    public static class ChapterReachability
    {
        // 완전 탐색 상한. 스탯 5개 × 범위 0~10정도로 가정.
        public const int StateLimit = 250_000;

        public static ReachabilityResult Prove(
            ChapterProgression chapter, IEnumerable<string> clearedChapterIds = null)
        {
            if (chapter == null)
                throw new ArgumentNullException(nameof(chapter));

            var clearedChapters = clearedChapterIds == null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(clearedChapterIds, StringComparer.Ordinal);

            var spans = new Dictionary<string, (int[] Min, int[] Max)>(StringComparer.Ordinal);
            var empty = new List<UnreachableEpisode>();

            if (chapter.Nodes.Count == 0)
            {
                return new ReachabilityResult(
                    new HashSet<string>(StringComparer.Ordinal), empty, true,
                    new Dictionary<string, IReadOnlyList<StatSpan>>(StringComparer.Ordinal));
            }

            IReadOnlyList<StatDefinition> stats = chapter.Stats;

            var reachable = new HashSet<string>(StringComparer.Ordinal);
            bool complete = true;

            int[] maxSeen = Initial(stats);
            int[] minSeen = Initial(stats);

            // cleared:가 도달 가능 집합을 참조하므로, 집합이 자라지 않을 때까지 반복한다.
            while (true)
            {
                HashSet<string> found;
                bool finished;

                Explore(chapter, reachable, clearedChapters, maxSeen, minSeen, spans,
                    out found, out finished);

                complete &= finished;
                AddReachableAttachments(chapter, found, clearedChapters, maxSeen, minSeen);

                if (found.Count == reachable.Count)
                {
                    reachable = found;
                    break;
                }

                reachable = found;
            }

            List<UnreachableEpisode> unreachable =
                CollectUnreachable(chapter, reachable, clearedChapters, maxSeen, minSeen);

            return new ReachabilityResult(
                reachable, unreachable, complete, BuildSpans(stats, spans));
        }

        // ── 탐색 ────────────────────────────────────────────────────────────

        private static void Explore(
            ChapterProgression chapter,
            HashSet<string> clearedAssumption,
            HashSet<string> clearedChapters,
            int[] maxSeen,
            int[] minSeen,
            Dictionary<string, (int[] Min, int[] Max)> spans,
            out HashSet<string> reachable,
            out bool complete)
        {
            reachable = new HashSet<string>(StringComparer.Ordinal) { chapter.StartEpisodeId };

            var visited = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<KeyValuePair<string, int[]>>();

            int[] initial = Initial(chapter.Stats);

            queue.Enqueue(new KeyValuePair<string, int[]>(chapter.StartEpisodeId, initial));
            visited.Add(StateKey(chapter.StartEpisodeId, initial));
            Observe(initial, maxSeen, minSeen);
            ObserveAt(chapter.StartEpisodeId, initial, spans);

            while (queue.Count > 0)
            {
                if (visited.Count > StateLimit)
                {
                    complete = false;
                    return;
                }

                KeyValuePair<string, int[]> current = queue.Dequeue();

                if (!chapter.TryGetNode(current.Key, out EpisodeNode node))
                {
                    continue;
                }

                IReadOnlyList<EpisodeOption> options = node.NextOptions;

                for (int i = 0; i < options.Count; i++)
                {
                    EpisodeOption option = options[i];

                    // 관문 판정은 커밋 전 값으로 — 플레이어가 선택지를 보는 시점의 값이다.
                    // 표시조건과 해금조건 둘 다 서야 탄다.
                    if (!Satisfied(chapter, option.VisibleConditions, current.Value,
                            clearedAssumption, clearedChapters) ||
                        !Satisfied(chapter, option.Conditions, current.Value,
                            clearedAssumption, clearedChapters))
                    {
                        continue;
                    }

                    // 간선을 타는 순간 증감이 1회 커밋된다 — 근사 없는 정확 전이.
                    int[] next = ApplyChanges(chapter, current.Value, option.StatChanges);

                    Observe(next, maxSeen, minSeen);
                    ObserveAt(option.TargetEpisodeId, next, spans);   // 도착 시점 = 커밋 뒤

                    reachable.Add(option.TargetEpisodeId);

                    if (visited.Add(StateKey(option.TargetEpisodeId, next)))
                    {
                        queue.Enqueue(new KeyValuePair<string, int[]>(option.TargetEpisodeId, next));
                    }
                }
            }

            complete = true;
        }

        /// <summary>
        /// 간선의 스탯 변화 커밋. 경계는 탐색 경계라 밖은 잘라낸다.
        ///
        /// ⚠ <b>이 함수는 단조가 아니다.</b> <see cref="StatChangeKind.Set"/>이 현재 값을
        /// 보지 않고 대입하므로, "간선을 지날수록 값이 한 방향으로만 간다"가 성립하지
        /// 않는다. 스탯 폭(min/max)을 "지나온 증감의 합"으로 접는 최적화를 넣고 싶어지면
        /// 여기를 먼저 볼 것 — Set이 그 접기를 무효로 만든다.
        ///
        /// 지금 탐색이 상태를 통째로 방문 집합에 넣는(State 키) 이유가 이것이다.
        /// 폭만 들고 걸으면 Set이 지나간 뒤의 값이 틀린다.
        /// </summary>
        private static int[] ApplyChanges(
            ChapterProgression chapter, int[] stats, IReadOnlyList<StatChange> changes)
        {
            if (changes.Count == 0)
            {
                return stats;
            }

            var next = (int[])stats.Clone();

            for (int c = 0; c < changes.Count; c++)
            {
                int index = IndexOfStat(chapter.Stats, changes[c].Key);

                if (index < 0)
                {
                    continue;   // 미등록 키 — 생성자·로더가 이미 오류로 잡았다
                }

                next[index] = chapter.Stats[index].Clamp(changes[c].ApplyTo(next[index]));
            }

            return next;
        }

        private static bool Satisfied(
            ChapterProgression chapter,
            IReadOnlyList<ProgressionCondition> conditions,
            int[] stats,
            HashSet<string> clearedEpisodes,
            HashSet<string> clearedChapters)
        {
            for (int i = 0; i < conditions.Count; i++)
            {
                ProgressionCondition condition = conditions[i];

                bool holds;

                switch (condition.Kind)
                {
                    case ConditionKind.EpisodeCleared:
                        holds = clearedEpisodes.Contains(condition.Key);
                        break;

                    case ConditionKind.ChapterCleared:
                        holds = clearedChapters.Contains(condition.Key);
                        break;

                    default:
                        holds = CompareStat(chapter, condition, stats);
                        break;
                }

                if (!holds)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool CompareStat(
            ChapterProgression chapter, ProgressionCondition condition, int[] stats)
        {
            int index = IndexOfStat(chapter.Stats, condition.Key);

            if (index < 0)
            {
                return false;   // 미등록 스탯키 — 생성자가 이미 오류로 잡았다
            }

            int value = stats[index];

            switch (condition.Op)
            {
                case ComparisonOp.GreaterOrEqual: return value >= condition.Value;
                case ComparisonOp.LessOrEqual: return value <= condition.Value;
                case ComparisonOp.GreaterThan: return value > condition.Value;
                case ComparisonOp.LessThan: return value < condition.Value;
                case ComparisonOp.Exists: return true;   // 여기 왔으면 정의돼 있다
                default: return value == condition.Value;
            }
        }

        /// <summary>
        /// 부착 에피소드는 간선으로 들어가는 노드가 아니다 — 부모 곁에 뜨는 사이드다.
        /// 그래서 도달 판정도 간선이 아니라 <b>관문 조건이 어느 도달 상태에서든 만족될 수
        /// 있는가</b>로 한다. 탐색이 본 스탯의 겉둘레로 판정하므로 <b>과대근사</b>이고,
        /// "도달 가능"이 넓게 잡혀 진짜 도달 불가를 놓치지 않는다.
        /// </summary>
        private static void AddReachableAttachments(
            ChapterProgression chapter,
            HashSet<string> reachable,
            HashSet<string> clearedChapters,
            int[] maxSeen,
            int[] minSeen)
        {
            foreach (EpisodeNode node in chapter.Nodes)
            {
                if (node.Kind != EpisodeKind.Attachment || reachable.Contains(node.EpisodeId))
                {
                    continue;
                }

                List<EpisodeOption> incoming = IncomingTo(chapter, node.EpisodeId);

                bool satisfiable = incoming.Count == 0;

                for (int i = 0; !satisfiable && i < incoming.Count; i++)
                {
                    satisfiable =
                        SatisfiableWithinEnvelope(chapter, incoming[i].VisibleConditions,
                            reachable, clearedChapters, maxSeen, minSeen) &&
                        SatisfiableWithinEnvelope(chapter, incoming[i].Conditions,
                            reachable, clearedChapters, maxSeen, minSeen);
                }

                if (satisfiable)
                {
                    reachable.Add(node.EpisodeId);
                }
            }
        }

        private static bool SatisfiableWithinEnvelope(
            ChapterProgression chapter,
            IReadOnlyList<ProgressionCondition> conditions,
            HashSet<string> clearedEpisodes,
            HashSet<string> clearedChapters,
            int[] maxSeen,
            int[] minSeen)
        {
            for (int i = 0; i < conditions.Count; i++)
            {
                ProgressionCondition condition = conditions[i];

                if (condition.Kind == ConditionKind.EpisodeCleared)
                {
                    if (!clearedEpisodes.Contains(condition.Key)) return false;
                    continue;
                }

                if (condition.Kind == ConditionKind.ChapterCleared)
                {
                    if (!clearedChapters.Contains(condition.Key)) return false;
                    continue;
                }

                int index = IndexOfStat(chapter.Stats, condition.Key);

                if (index < 0)
                {
                    return false;
                }

                bool satisfiable;

                switch (condition.Op)
                {
                    case ComparisonOp.GreaterOrEqual: satisfiable = maxSeen[index] >= condition.Value; break;
                    case ComparisonOp.LessOrEqual: satisfiable = minSeen[index] <= condition.Value; break;
                    case ComparisonOp.GreaterThan: satisfiable = maxSeen[index] > condition.Value; break;
                    case ComparisonOp.LessThan: satisfiable = minSeen[index] < condition.Value; break;
                    case ComparisonOp.Exists: satisfiable = true; break;
                    default:
                        satisfiable = minSeen[index] <= condition.Value &&
                                      condition.Value <= maxSeen[index];
                        break;
                }

                if (!satisfiable)
                {
                    return false;
                }
            }

            return true;
        }

        // ── 보고 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 못 가는 에피소드와 <b>원인</b>. 저작 도구가 문장을 만들 때 쓰는 재료다 —
        /// 여기서 문장까지 만들면 규약 사본이 둘이 된다.
        /// </summary>
        private static List<UnreachableEpisode> CollectUnreachable(
            ChapterProgression chapter,
            HashSet<string> reachable,
            HashSet<string> clearedChapters,
            int[] maxSeen,
            int[] minSeen)
        {
            var found = new List<UnreachableEpisode>();

            foreach (EpisodeNode node in chapter.Nodes)
            {
                if (reachable.Contains(node.EpisodeId))
                {
                    continue;
                }

                List<EpisodeOption> incoming = IncomingTo(chapter, node.EpisodeId);

                if (incoming.Count == 0)
                {
                    found.Add(new UnreachableEpisode(
                        node.EpisodeId, UnreachableCause.NoIncomingEdge, default));
                    continue;
                }

                if (!AnySourceReachable(chapter, node.EpisodeId, reachable))
                {
                    found.Add(new UnreachableEpisode(
                        node.EpisodeId, UnreachableCause.SourcesUnreachable, default));
                    continue;
                }

                ProgressionCondition blocking =
                    FindBlocking(chapter, incoming, reachable, clearedChapters, maxSeen, minSeen);

                found.Add(new UnreachableEpisode(
                    node.EpisodeId,
                    blocking.IsConstructed
                        ? UnreachableCause.BlockedByCondition
                        : UnreachableCause.Undetermined,
                    blocking));
            }

            return found;
        }

        private static ProgressionCondition FindBlocking(
            ChapterProgression chapter,
            List<EpisodeOption> incoming,
            HashSet<string> reachable,
            HashSet<string> clearedChapters,
            int[] maxSeen,
            int[] minSeen)
        {
            for (int i = 0; i < incoming.Count; i++)
            {
                ProgressionCondition blocking =
                    FirstUnsatisfiable(chapter, incoming[i].Conditions, reachable, clearedChapters,
                        maxSeen, minSeen);

                if (blocking.IsConstructed) return blocking;

                blocking = FirstUnsatisfiable(chapter, incoming[i].VisibleConditions, reachable,
                    clearedChapters, maxSeen, minSeen);

                if (blocking.IsConstructed) return blocking;
            }

            return default;
        }

        private static ProgressionCondition FirstUnsatisfiable(
            ChapterProgression chapter,
            IReadOnlyList<ProgressionCondition> conditions,
            HashSet<string> reachable,
            HashSet<string> clearedChapters,
            int[] maxSeen,
            int[] minSeen)
        {
            for (int i = 0; i < conditions.Count; i++)
            {
                var one = new[] { conditions[i] };

                if (!SatisfiableWithinEnvelope(
                        chapter, one, reachable, clearedChapters, maxSeen, minSeen))
                {
                    return conditions[i];
                }
            }

            return default;
        }

        // ── 잔손 ────────────────────────────────────────────────────────────

        private static List<EpisodeOption> IncomingTo(ChapterProgression chapter, string episodeId)
        {
            var incoming = new List<EpisodeOption>();

            foreach (EpisodeNode node in chapter.Nodes)
            {
                IReadOnlyList<EpisodeOption> options = node.NextOptions;

                for (int i = 0; i < options.Count; i++)
                {
                    if (string.Equals(options[i].TargetEpisodeId, episodeId, StringComparison.Ordinal))
                    {
                        incoming.Add(options[i]);
                    }
                }
            }

            return incoming;
        }

        private static bool AnySourceReachable(
            ChapterProgression chapter, string episodeId, HashSet<string> reachable)
        {
            foreach (EpisodeNode node in chapter.Nodes)
            {
                IReadOnlyList<EpisodeOption> options = node.NextOptions;

                for (int i = 0; i < options.Count; i++)
                {
                    if (string.Equals(options[i].TargetEpisodeId, episodeId, StringComparison.Ordinal) &&
                        reachable.Contains(node.EpisodeId))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int[] Initial(IReadOnlyList<StatDefinition> stats)
        {
            var values = new int[stats.Count];

            for (int i = 0; i < stats.Count; i++)
            {
                values[i] = stats[i].Initial;
            }

            return values;
        }

        private static void Observe(int[] stats, int[] maxSeen, int[] minSeen)
        {
            for (int i = 0; i < stats.Length; i++)
            {
                if (stats[i] > maxSeen[i]) maxSeen[i] = stats[i];
                if (stats[i] < minSeen[i]) minSeen[i] = stats[i];
            }
        }

        /// <summary>그 에피소드에 이 값으로 도착했다 — 에피소드별 폭을 넓힌다.</summary>
        private static void ObserveAt(
            string episodeId, int[] stats, Dictionary<string, (int[] Min, int[] Max)> spans)
        {
            (int[] Min, int[] Max) span;

            if (!spans.TryGetValue(episodeId, out span))
            {
                spans[episodeId] = ((int[])stats.Clone(), (int[])stats.Clone());
                return;
            }

            for (int i = 0; i < stats.Length; i++)
            {
                if (stats[i] < span.Min[i]) span.Min[i] = stats[i];
                if (stats[i] > span.Max[i]) span.Max[i] = stats[i];
            }
        }

        private static Dictionary<string, IReadOnlyList<StatSpan>> BuildSpans(
            IReadOnlyList<StatDefinition> stats,
            Dictionary<string, (int[] Min, int[] Max)> spans)
        {
            var built = new Dictionary<string, IReadOnlyList<StatSpan>>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, (int[] Min, int[] Max)> pair in spans)
            {
                var list = new List<StatSpan>(stats.Count);

                for (int i = 0; i < stats.Count && i < pair.Value.Min.Length; i++)
                {
                    list.Add(new StatSpan(
                        stats[i].Key, stats[i].DisplayName,
                        pair.Value.Min[i], pair.Value.Max[i]));
                }

                built[pair.Key] = list;
            }

            return built;
        }

        private static int IndexOfStat(IReadOnlyList<StatDefinition> stats, string key)
        {
            for (int i = 0; i < stats.Count; i++)
            {
                if (string.Equals(stats[i].Key, key, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string StateKey(string episodeId, int[] stats) =>
            episodeId + "|" + string.Join(",", stats);
    }
}
