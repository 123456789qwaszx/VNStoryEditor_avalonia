using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    // 챕터 진행 상태.
    public sealed class ProgressionState
    {
        private readonly Dictionary<string, int> _stats;

        public string CurrentEpisodeId { get; }

        public IReadOnlyDictionary<string, int> Stats => _stats;

        private ProgressionState(string currentEpisodeId, Dictionary<string, int> stats)
        {
            CurrentEpisodeId = currentEpisodeId;
            _stats = stats;
        }

        public static ProgressionState CreateInitial(IEnumerable<StatDefinition> stats, string startEpisodeId)
        {
            var values = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (StatDefinition stat in stats)
            {
                if (values.ContainsKey(stat.Key))
                    throw new ArgumentException($"스탯 키 '{stat.Key}'가 중복 정의됐다.", nameof(stats));

                values[stat.Key] = stat.Initial;
            }

            return new ProgressionState(startEpisodeId, values);
        }
        
        public int GetStat(string key)
        {
            if (_stats.TryGetValue(key, out int value))
                return value;

            // - 정의되지 않은 키는 0으로 떨어뜨리지 않고 던짐.
            // - 작가가 오타 낸 조건이 언제나 true로써,
            // - "언제나 통과하는 관문"으로 조용히 바뀌기에 찾기 힘든 버그를 유발하기 때문.
            throw new KeyNotFoundException(
                $"정의되지 않은 스탯 '{key}'. 정의된 것: {string.Join(", ", _stats.Keys)}");
        }
        
        // 장면 내, 현재까지의 pending choice를 적용한 작업 상태를 계산
        // - 이후 선택지의 조건 판정에 사용.
        // - 롤백 시 pending을 줄여 다시 계산.
        // - Scene이 끝나면 최종 상태로 확정.
        public ProgressionState FoldChoices(ChapterProgression chapter, IReadOnlyList<EpisodeOption> choices)
        {
            ProgressionState state = this;

            for (int i = 0; i < choices.Count; i++)
                state = state.ApplyChoice(chapter, choices[i]);

            return state;
        }
        
        // 유일한 스탯 입력 자리.
        // 간선의 StatChange를 순서대로 반영하고,
        // 도착 에피소드로 이동한 새 ProgressionState를 반환.
        public ProgressionState ApplyChoice(ChapterProgression chapter, EpisodeOption choices)
        {
            RequireOutgoingEdge(chapter, choices);

            var stats = new Dictionary<string, int>(_stats, StringComparer.Ordinal);

            IReadOnlyList<StatChange> changes = choices.StatChanges;

            for (int i = 0; i < changes.Count; i++)
            {
                StatChange change = changes[i];
                StatDefinition definition = chapter.StatsByKey[change.Key];

                if (!stats.TryGetValue(change.Key, out int current))
                    throw new InvalidOperationException($"{chapter.ChapterId} 상태에 스탯 '{change.Key}'가 없다.");

                stats[change.Key] = definition.Clamp(change.ApplyTo(current));
            }

            return new ProgressionState(choices.TargetEpisodeId, stats);
        }
        
        // 고른 간선이 지금 에피소드에서 나가는 길인지 — 참조 동일성으로 본다.
        // 챕터 생성자는 "모든 간선이 실재하는 노드에 착지한다"까지만 보장한다. 호출자가 엉뚱한 노드의
        // 간선을 넘기면 그래프에 없는 경로로 이동한 상태가 생기고, 도달성 증명이 보증한 것과 실제
        // 플레이가 갈린다. 상태가 챕터 ID를 들지 않으므로 챕터가 짝이 맞는지도 여기서 함께 걸린다.
        private void RequireOutgoingEdge(ChapterProgression chapter, EpisodeOption chosen)
        {
            if (!chapter.TryGetNode(CurrentEpisodeId, out EpisodeNode node))
                throw new ArgumentException(
                    $"지금 에피소드 '{CurrentEpisodeId}'가 챕터 '{chapter.ChapterId}'에 없다.", nameof(chapter));

            IReadOnlyList<EpisodeOption> options = node.NextOptions;

            for (int i = 0; i < options.Count; i++)
            {
                if (ReferenceEquals(options[i], chosen))
                    return;
            }

            throw new ArgumentException(
                $"고른 간선({chosen})이 에피소드 '{CurrentEpisodeId}'에서 나가는 길이 아니다.", nameof(chosen));
        }

        public static ProgressionState Restore(
            ChapterProgression chapter, 
            string currentEpisodeId, 
            IReadOnlyDictionary<string, int> savedStats)
        {
            var values = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (StatDefinition stat in chapter.Stats)
            {
                values[stat.Key] = savedStats.TryGetValue(stat.Key, out int saved)
                    ? stat.Clamp(saved)
                    : stat.Initial;
            }

            return new ProgressionState(currentEpisodeId, values);
        }
    }
}