using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    /// <summary>챕터 하나를 어느 엔딩으로 끝냈는가. 세이브에 남고 시나리오 간선이 본다.</summary>
    public readonly struct ChapterEnding
    {
        public string ChapterId { get; }
        public string EndingKey { get; }

        public ChapterEnding(string chapterId, string endingKey)
        {
            ChapterId = chapterId;
            EndingKey = endingKey;
        }

        public override string ToString() => $"{ChapterId}:{EndingKey}";
    }

    /// <summary>
    /// 진행 상태 — "지금 어디까지 왔나".
    ///
    /// 불변이다. 바꾸지 않고 새 상태를 돌려준다(규율 4) —
    /// <c>StageState</c>가 <c>Clone()</c> 후 새 것을 내는 것과 같은 규율이다.
    /// 그래서 "선택지를 눌렀을 때의 값"과 "누른 뒤의 값"을 동시에 들고 비교할 수 있다.
    ///
    /// <b>이 타입이 세이브가 담을 내용이다.</b> 반대로 여기 없는 것은 저장되지 않는다 —
    /// 잠김·표시·도달 가능 집합은 <c>ChapterTransition.Resolve</c>의 <i>출력</i>이지 상태가
    /// 아니므로 일부러 없다. 구 런타임 <c>EpisodeSelectionStateData</c>가 그 둘을 한 자루에
    /// 넣었고, 그래서 세이브에 옛 판정이 섞여 들어갈 길이 열려 있었다.
    /// </summary>
    public sealed class ProgressionState
    {
        private readonly Dictionary<string, int> _stats;
        private readonly HashSet<string> _clearedEpisodes;
        private readonly HashSet<string> _clearedChapters;
        private readonly List<ChapterEnding> _endingHistory;

        public string CurrentChapterId { get; }
        public string CurrentEpisodeId { get; }

        public IReadOnlyDictionary<string, int> Stats => _stats;
        public IReadOnlyCollection<string> ClearedEpisodeIds => _clearedEpisodes;
        public IReadOnlyCollection<string> ClearedChapterIds => _clearedChapters;

        /// <summary>끝낸 챕터와 그 엔딩키. 순서대로 쌓인다.</summary>
        public IReadOnlyList<ChapterEnding> EndingHistory => _endingHistory;

        private ProgressionState(
            string currentChapterId,
            string currentEpisodeId,
            Dictionary<string, int> stats,
            HashSet<string> clearedEpisodes,
            HashSet<string> clearedChapters,
            List<ChapterEnding> endingHistory)
        {
            CurrentChapterId = currentChapterId;
            CurrentEpisodeId = currentEpisodeId;
            _stats = stats;
            _clearedEpisodes = clearedEpisodes;
            _clearedChapters = clearedChapters;
            _endingHistory = endingHistory;
        }

        /// <summary>
        /// 스탯 정의의 <see cref="StatDefinition.Initial"/>로 세운 시작 상태.
        ///
        /// ⚠ <b>D1 — 실제 플레이의 시작값은 시나리오가 소유한다.</b> 챕터를 단독으로 세워
        /// 여기 오는 경우(테스트·도달성 증명)는 그 챕터의 초기값이 <b>진입 가정</b>으로 쓰인
        /// 것이다.
        /// </summary>
        public static ProgressionState CreateInitial(
            IEnumerable<StatDefinition> stats,
            string startChapterId,
            string startEpisodeId,
            IEnumerable<string> clearedChapterIds = null)
        {
            if (stats == null)
                throw new ArgumentNullException(nameof(stats));

            var values = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (StatDefinition stat in stats)
            {
                if (values.ContainsKey(stat.Key))
                    throw new ArgumentException($"스탯 키 '{stat.Key}'가 중복 정의됐다.", nameof(stats));

                values[stat.Key] = stat.Initial;
            }

            var chapters = clearedChapterIds == null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(clearedChapterIds, StringComparer.Ordinal);

            return new ProgressionState(
                startChapterId ?? string.Empty,
                startEpisodeId,
                values,
                new HashSet<string>(StringComparer.Ordinal),
                chapters,
                new List<ChapterEnding>());
        }

        /// <summary>
        /// 세이브에서 되살린다. <b>internal이다</b> — 임의의 상태를 손으로 만드는 길을 열면
        /// "그래프에 없는 자리에 있는 상태"가 생긴다. 정상 경로는 <see cref="CreateInitial"/>과
        /// <see cref="Commit"/> 둘뿐이고, 여기는 <see cref="ProgressionSave.Restore"/>가
        /// 검증을 마친 값으로만 부른다.
        /// </summary>
        internal static ProgressionState FromSave(
            string chapterId,
            string episodeId,
            Dictionary<string, int> stats,
            HashSet<string> clearedEpisodes,
            HashSet<string> clearedChapters,
            List<ChapterEnding> endingHistory)
        {
            return new ProgressionState(
                chapterId, episodeId, stats, clearedEpisodes, clearedChapters, endingHistory);
        }

        /// <summary>
        /// 스탯 값. <b>정의되지 않은 키는 0으로 떨어뜨리지 않고 던진다</b> — 규율 1.
        ///
        /// 조용히 0을 주면 <c>trust &gt;= 0</c>이 언제나 참이 되어, 작가가 오타 낸 조건이
        /// "언제나 통과하는 관문"으로 조용히 바뀐다. 그런 종류의 버그는 재생해 봐도 안 보인다.
        /// (코어의 <c>RectNodeTree.GetState("없는키")</c>와 같은 규율이다.)
        /// </summary>
        public int GetStat(string key)
        {
            if (_stats.TryGetValue(key, out int value))
                return value;

            throw new KeyNotFoundException(
                $"정의되지 않은 스탯 '{key}'. 정의된 것: {string.Join(", ", _stats.Keys)}");
        }

        public bool IsEpisodeCleared(string episodeId) => _clearedEpisodes.Contains(episodeId);

        public bool IsChapterCleared(string chapterId) => _clearedChapters.Contains(chapterId);

        /// <summary>
        /// 선택지 하나를 <b>커밋한다</b> — 스탯 증감을 원자적으로 1회 반영하고, 지금 에피소드를
        /// 클리어로 표시하고, 도착 에피소드로 옮긴 새 상태를 돌려준다.
        ///
        /// <b>이 셋이 한 연산인 것이 이 타입의 핵심이다(§G6-1).</b> 전에는 스탯 반영과 이동이
        /// 각각 공개 메서드라 따로 부를 수 있었다. 따로 부를 수 있으면 언젠가 따로 불리고,
        /// 그 순간 <b>"스탯만 바뀌고 안 옮겨 간" 상태</b>가 생긴다 — 플레이어가 에피소드
        /// 중간에 나갔다 다시 들어오면 절반 반영된 채 처음부터 다시 가산되는, §3.3이 막으려던
        /// 바로 그 버그다. 합치면 그 상태가 존재할 수 없다.
        ///
        /// <b>에피소드 = 트랜잭션 경계</b>가 문서가 아니라 타입이 되는 자리다.
        /// </summary>
        public ProgressionState Commit(ChapterProgression chapter, EpisodeOption chosen)
        {
            if (chapter == null)
                throw new ArgumentNullException(nameof(chapter));

            if (chosen == null)
                throw new ArgumentNullException(nameof(chosen));

            VerifyReachableFromHere(chapter, chosen);

            var stats = new Dictionary<string, int>(_stats, StringComparer.Ordinal);

            IReadOnlyList<StatChange> changes = chosen.StatChanges;

            for (int i = 0; i < changes.Count; i++)
            {
                StatChange change = changes[i];

                if (!chapter.StatsByKey.TryGetValue(change.Key, out StatDefinition definition))
                {
                    throw new KeyNotFoundException(
                        $"정의되지 않은 스탯 '{change.Key}'을(를) 변경하려 한다. " +
                        $"정의된 것: {string.Join(", ", chapter.StatsByKey.Keys)}");
                }

                if (!stats.TryGetValue(change.Key, out int current))
                    current = definition.Initial;

                stats[change.Key] = definition.Clamp(change.ApplyTo(current));
            }

            var clearedEpisodes = new HashSet<string>(_clearedEpisodes, StringComparer.Ordinal);

            if (!string.IsNullOrEmpty(CurrentEpisodeId))
                clearedEpisodes.Add(CurrentEpisodeId);

            return new ProgressionState(
                CurrentChapterId,
                chosen.TargetEpisodeId,
                stats,
                clearedEpisodes,
                new HashSet<string>(_clearedChapters, StringComparer.Ordinal),
                new List<ChapterEnding>(_endingHistory));
        }

        /// <summary>
        /// 챕터 하나를 끝내고 다음으로 넘어간다 — <b>챕터 경계의 트랜잭션이다.</b>
        ///
        /// 지금 챕터를 클리어로 표시하고, 엔딩을 이력에 남기고, 다음 챕터의 시작 에피소드로
        /// 옮긴다. <see cref="Commit"/>과 같은 이유로 셋이 한 연산이다 — 표시만 하고 안
        /// 옮겨 간 상태가 있으면 "어느 챕터를 하는 중인가"의 답이 둘이 된다.
        ///
        /// 무엇이 다음인지는 <see cref="ScenarioTransition.Resolve"/>가 먼저 정한다.
        /// 여기서는 그 결정을 적용만 한다(판단과 실행의 분리).
        /// </summary>
        public ProgressionState CommitChapterEnding(
            ScenarioProgression scenario, in ScenarioAdvance advance)
        {
            if (scenario == null)
                throw new ArgumentNullException(nameof(scenario));

            if (string.IsNullOrEmpty(CurrentChapterId))
            {
                throw new InvalidOperationException(
                    "지금 챕터가 비어 있다. 챕터를 끝낼 수 없다.");
            }

            var clearedChapters = new HashSet<string>(_clearedChapters, StringComparer.Ordinal)
            {
                CurrentChapterId
            };

            var history = new List<ChapterEnding>(_endingHistory)
            {
                new ChapterEnding(CurrentChapterId, advance.EndingKey)
            };

            string nextChapterId = CurrentChapterId;
            string nextEpisodeId = CurrentEpisodeId;

            if (advance.Kind == ScenarioAdvanceKind.NextChapter)
            {
                if (!scenario.TryGetChapter(advance.NextChapterId, out ChapterProgression next))
                {
                    // 시나리오 생성자가 모든 NextChapterId의 실재를 이미 확인했다.
                    // 여기 오면 다른 시나리오의 판정을 넘긴 것이다.
                    throw new ArgumentException(
                        $"다음 챕터 '{advance.NextChapterId}'가 시나리오 " +
                        $"'{scenario.ScenarioId}'에 없다.",
                        nameof(advance));
                }

                nextChapterId = next.ChapterId;
                nextEpisodeId = next.StartEpisodeId;
            }

            return new ProgressionState(
                nextChapterId,
                nextEpisodeId,
                new Dictionary<string, int>(_stats, StringComparer.Ordinal),
                new HashSet<string>(_clearedEpisodes, StringComparer.Ordinal),
                clearedChapters,
                history);
        }

        /// <summary>
        /// 고른 선택지가 <b>지금 있는 에피소드에서 나가는 길</b>인지 확인한다.
        ///
        /// 챕터 생성자는 "모든 간선이 실재하는 노드에 착지한다"까지만 보장하지, 호출자가
        /// 엉뚱한 노드의 선택지를 넘기는 것은 막지 못한다. 그대로 통과시키면 그래프에 없는
        /// 경로로 이동한 상태가 만들어지고, 도달성 증명이 보증한 것과 실제 플레이가 갈린다.
        /// </summary>
        private void VerifyReachableFromHere(ChapterProgression chapter, EpisodeOption chosen)
        {
            if (CurrentChapterId.Length != 0 &&
                !string.Equals(CurrentChapterId, chapter.ChapterId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"지금 챕터는 '{CurrentChapterId}'인데 '{chapter.ChapterId}'의 선택지를 커밋하려 한다.",
                    nameof(chapter));
            }

            if (!chapter.TryGetNode(CurrentEpisodeId, out EpisodeNode node))
            {
                throw new ArgumentException(
                    $"지금 에피소드 '{CurrentEpisodeId}'가 챕터 '{chapter.ChapterId}'에 없다.",
                    nameof(chapter));
            }

            IReadOnlyList<EpisodeOption> options = node.NextOptions;

            for (int i = 0; i < options.Count; i++)
            {
                if (ReferenceEquals(options[i], chosen))
                    return;
            }

            throw new ArgumentException(
                $"고른 선택지({chosen})가 에피소드 '{CurrentEpisodeId}'에서 나가는 길이 아니다.",
                nameof(chosen));
        }
    }

    /// <summary>스탯 변화의 종류.</summary>
    public enum StatChangeKind
    {
        /// <summary>현재 값에 더한다. <c>Op</c>가 비어 있으면 이것이다.</summary>
        Add = 0,

        /// <summary>현재 값을 보지 않고 정한다. bool 스탯(깃발)에만 쓴다.</summary>
        Set = 1,
    }

    /// <summary>선택지가 커밋될 때 적용되는 스탯 변화.</summary>
    public readonly struct StatChange
    {
        public string Key { get; }

        /// <summary>
        /// <see cref="StatChangeKind.Add"/>면 증감량, <see cref="StatChangeKind.Set"/>면
        /// <b>정할 값</b>이다. 칸을 늘리지 않는 이유 — 둘이 되면 "어느 칸이 사는가"를
        /// <see cref="Kind"/>와 따로 기억해야 하고, 안 사는 칸에 적힌 값이 조용히 사라진다.
        /// </summary>
        public int Amount { get; }

        public StatChangeKind Kind { get; }

        // 공개 생성자를 열지 않는다(P1) — 종류를 안 적고 만드는 길을 없앤다.
        // 타입이 bool 여부를 모르므로 Set에 2가 들어오는 것까지는 못 막는다.
        // 그 값 검사는 ChapterInvariants가 하고 로더가 앞당긴다.
        private StatChange(string key, int amount, StatChangeKind kind)
        {
            Key = key;
            Amount = amount;
            Kind = kind;
        }

        /// <summary>현재 값에 더한다.</summary>
        public static StatChange Add(string key, int amount) =>
            new StatChange(key, amount, StatChangeKind.Add);

        /// <summary>현재 값을 보지 않고 정한다. bool 스탯에만.</summary>
        public static StatChange Set(string key, int value) =>
            new StatChange(key, value, StatChangeKind.Set);

        /// <summary>
        /// 이 변화를 현재 값에 적용한 결과. <b>경계 자르기는 부르는 쪽이 한다</b> —
        /// 플레이는 <see cref="StatDefinition.Clamp"/>로, 증명은 탐색 경계로 자른다.
        ///
        /// 적용 규칙이 여기 하나만 있는 이유: 커밋과 도달성 증명이 각자 더하면
        /// 언젠가 갈린다. 갈리면 "툴은 열린다는데 실제로는 안 열리는 관문"이 된다.
        /// </summary>
        public int ApplyTo(int current) =>
            Kind == StatChangeKind.Set ? Amount : current + Amount;

        public override string ToString() =>
            Kind == StatChangeKind.Set
                ? $"{Key} = {Amount}"
                : $"{Key} {(Amount >= 0 ? "+" : "")}{Amount}";
    }
}
