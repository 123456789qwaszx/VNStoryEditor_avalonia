using System;
using System.Collections.Generic;

namespace Ked.Progression
{
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

        public static ReachabilityResult Prove(ChapterProgression chapter)
        {
            if (chapter == null)
                throw new ArgumentNullException(nameof(chapter));

            var spans = new Dictionary<string, (int[] Min, int[] Max)>(StringComparer.Ordinal);
            var empty = new List<UnreachableEpisode>();

            if (chapter.Nodes.Count == 0)
            {
                return new ReachabilityResult(
                    new HashSet<string>(StringComparer.Ordinal), empty, true,
                    new Dictionary<string, IReadOnlyList<StatSpan>>(StringComparer.Ordinal));
            }

            IReadOnlyList<StatDefinition> stats = chapter.Stats;

            int[] maxSeen = Initial(stats);
            int[] minSeen = Initial(stats);

            // 관문이 (에피소드, 스탯 벡터)에만 의존하므로 탐색 결과가 탐색 입력으로
            // 되먹임되지 않는다 — 완전 탐색 한 번이면 끝난다.
            Explore(chapter, maxSeen, minSeen, spans,
                out HashSet<string> reachable, out bool complete);

            List<UnreachableEpisode> unreachable =
                CollectUnreachable(chapter, reachable, maxSeen, minSeen);

            return new ReachabilityResult(
                reachable, unreachable, complete, BuildSpans(stats, spans));
        }

        // ── 탐색 ────────────────────────────────────────────────────────────

        private static void Explore(
            ChapterProgression chapter,
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
                    if (!Satisfied(chapter, option.VisibleConditions, current.Value) ||
                        !Satisfied(chapter, option.Conditions, current.Value))
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
            int[] stats)
        {
            for (int i = 0; i < conditions.Count; i++)
            {
                if (!CompareStat(chapter, conditions[i], stats))
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
        /// 조건이 <b>탐색이 실제로 본 스탯의 겉둘레 안에서</b> 성립할 수 있는가.
        ///
        /// ⚠ 과대근사다 — 겉둘레는 에피소드별이 아니라 챕터 전체의 폭이고, 조건들이
        /// 한 상태에서 <b>동시에</b> 서는지도 보지 않는다. 그래서 도달 여부는 여기서
        /// 정하지 않는다(<see cref="Explore"/>의 완전 탐색만 정한다). 이미 못 간다고
        /// 판정된 에피소드에 대해 <b>어느 조건을 짚어 줄지</b> 고르는 진단 재료다.
        /// </summary>
        private static bool SatisfiableWithinEnvelope(
            ChapterProgression chapter,
            IReadOnlyList<ProgressionCondition> conditions,
            int[] maxSeen,
            int[] minSeen)
        {
            for (int i = 0; i < conditions.Count; i++)
            {
                ProgressionCondition condition = conditions[i];

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
                    FindBlocking(chapter, incoming, maxSeen, minSeen);

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
            int[] maxSeen,
            int[] minSeen)
        {
            for (int i = 0; i < incoming.Count; i++)
            {
                ProgressionCondition blocking =
                    FirstUnsatisfiable(chapter, incoming[i].Conditions, maxSeen, minSeen);

                if (blocking.IsConstructed) return blocking;

                blocking = FirstUnsatisfiable(
                    chapter, incoming[i].VisibleConditions, maxSeen, minSeen);

                if (blocking.IsConstructed) return blocking;
            }

            return default;
        }

        private static ProgressionCondition FirstUnsatisfiable(
            ChapterProgression chapter,
            IReadOnlyList<ProgressionCondition> conditions,
            int[] maxSeen,
            int[] minSeen)
        {
            for (int i = 0; i < conditions.Count; i++)
            {
                var one = new[] { conditions[i] };

                if (!SatisfiableWithinEnvelope(chapter, one, maxSeen, minSeen))
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