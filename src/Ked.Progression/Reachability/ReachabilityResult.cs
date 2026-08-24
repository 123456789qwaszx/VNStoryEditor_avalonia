using System;
using System.Collections.Generic;

namespace Ked.Progression
{
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
}