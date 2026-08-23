using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    // 챕터 하나의 진행 규칙.
    //
    // 생성자 보장:
    // [1]에피소드 ID와 스탯 키에 중복이 없고,
    // [2]시작 에피소드가 실재하며,
    // [3]모든 간선이 실재하는 에피소드에 착지하고,
    // [4] 조건과 스탯변화가 정의된 스탯만 가리킴.
    //
    // 그 규칙의 구현 = ChapterInvariants
    // 
    // 어느 엔딩인지는 "EpisodeNode.EndingKey"가 정하고,
    // "EndingRules"는 그 키로 조회되는 표.
    public sealed class ChapterProgression
    {
        private readonly Dictionary<string, EpisodeNode> _nodesById;
        private readonly Dictionary<string, StatDefinition> _statsByKey;

        public string ChapterId { get; }
        public string DisplayName { get; }

        public string StartEpisodeId { get; }

        // 이 챕터에서 쓰는 스탯의 정의
        public IReadOnlyList<StatDefinition> Stats { get; }

        // 에피소드들. 순서 무관. 간선을 통해 연결되기 때문.
        public IReadOnlyList<EpisodeNode> Nodes { get; }

        public IReadOnlyList<EndingRule> EndingRules { get; }

        // 키로 찾는 스탯 정의. "ProgressionState.Commit"이 쓰는 시스템 경계.
        public IReadOnlyDictionary<string, StatDefinition> StatsByKey => _statsByKey;

        public ChapterProgression(
            string chapterId,
            string displayName,
            string startEpisodeId,
            IReadOnlyList<StatDefinition> stats,
            IReadOnlyList<EpisodeNode> nodes,
            IReadOnlyList<EndingRule> endingRules = null)
        {
            if (string.IsNullOrEmpty(chapterId))
            {
                throw new ArgumentException("챕터 ID가 비어 있다.", nameof(chapterId));
            }

            ChapterId = chapterId;
            DisplayName = displayName ?? string.Empty;
            StartEpisodeId = startEpisodeId ?? string.Empty;
            Stats = stats ?? Array.Empty<StatDefinition>();
            Nodes = nodes ?? Array.Empty<EpisodeNode>();
            EndingRules = endingRules ?? Array.Empty<EndingRule>();

            var diagnostics = new List<ProgressionDiagnostic>();

            ChapterInvariants.Collect(
                Stats, Nodes, EndingRules, StartEpisodeId,
                diagnostics, out _statsByKey, out _nodesById);

            if (diagnostics.Count > 0)
                throw new ArgumentException(diagnostics[0].ToString());
        }

        // 에피소드를 ID로 찾는다. 생성자 보장.
        public bool TryGetNode(string episodeId, out EpisodeNode node)
        {
            if (episodeId == null)
            {
                node = null;
                return false;
            }

            return _nodesById.TryGetValue(episodeId, out node);
        }

        public EpisodeNode StartNode => _nodesById[StartEpisodeId];

        /// <summary>
        /// <b>증명 진입 가정</b> — 이 챕터를 단독으로 걸을 때 "여기 들어온 순간"으로
        /// 삼는 상태다. 챕터의 스탯 초기값으로 세운다.
        ///
        /// ⚠ <b>플레이 시작이 아니다.</b> 실제 플레이의 시작값은 시나리오가 한 번만
        /// 세운다(D1) — <see cref="ScenarioProgression.CreateInitialState"/>. 둘은 다른
        /// 값이고(픽스처가 일부러 trust 0 vs 5로 갈라 둔다), 잘못 부르면 예외 없이
        /// 다른 초기값으로 게임이 시작된다. 타입이 못 막는 자리라 이름으로 가른다.
        /// </summary>
        public ProgressionState CreateProofEntryState() =>
            ProgressionState.CreateInitial(Stats, ChapterId, StartEpisodeId);

        public override string ToString() =>
            $"{ChapterId}(에피소드 {Nodes.Count}, 스탯 {Stats.Count})";
    }
}