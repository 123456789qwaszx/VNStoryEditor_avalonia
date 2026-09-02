using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    // 챕터 하나의 진행 규칙을 가진 런타인 모델.
    //
    // 생성자 보장:
    // [1]에피소드 ID와 스탯 키에 중복이 없고,
    // [2]시작 에피소드가 실재하며,
    // [3]모든 간선이 실재하는 에피소드에 착지하고,
    // [4] 조건과 스탯변화가 정의된 스탯만 가리킴.
    // (구현 = ChapterInvariants)
    
    // - 위와 같은 구조적 무결성은 보장하지만,
    // - 진행 그래프의 유효성을 증명하진 않음.
    // (끊긴 노드, 불가능한 조건 등)
    public sealed class ChapterProgression
    {
        private readonly Dictionary<string, EpisodeNode> _nodesById;
        
        // Stats: 챕터에서 어떤 스탯을 쓰는지 확인.
        // StatsByKey: 특정 스탯의 정의 및 수치 확인.
        private readonly Dictionary<string, StatDefinition> _statsByKey;

        public string ChapterId { get; }
        public string DisplayName { get; }
        public string StartEpisodeId { get; }

        public IReadOnlyList<EpisodeNode> Nodes { get; } // 챕터 내 에피소드들

        // 챕터 고유의 스탯.
        public IReadOnlyList<StatDefinition> Stats { get; }

        // 특정 스탯 정의. "ProgressionState.Commit"이 쓰는 시스템 경계.
        // (진행 상태 시스템과 챕터 정의 사이의 공식 경계.)
        public IReadOnlyDictionary<string, StatDefinition> StatsByKey => _statsByKey;

        public ChapterProgression(
            string chapterId,
            string displayName,
            string startEpisodeId,
            IReadOnlyList<StatDefinition> stats,
            IReadOnlyList<EpisodeNode> nodes)
        {
            if (string.IsNullOrEmpty(chapterId))
                throw new ArgumentException("챕터 ID가 비어 있다.", nameof(chapterId));

            ChapterId = chapterId;
            DisplayName = displayName ?? string.Empty;
            StartEpisodeId = startEpisodeId ?? string.Empty;
            Stats = stats ?? Array.Empty<StatDefinition>();
            Nodes = nodes ?? Array.Empty<EpisodeNode>();

            var diagnostics = new List<ProgressionDiagnostic>();

            // 챕터 데이터 검증.
            ChapterInvariants.Collect(
                Stats, Nodes, StartEpisodeId,
                diagnostics, out _statsByKey, out _nodesById);

            // 오류가 있으면 생성 자체를 실패시킴.
            if (diagnostics.Count > 0)
                throw new ArgumentException(diagnostics[0].ToString());

            _sceneRoots = CollectSceneRoots();
        }

        // 장면 루트 = 밖에서 들어오는 간선이 착지하는 자리(챕터 시작 포함). 불변식이 장면마다
        // 하나임을 보장한다. 이어하기가 재개할 수 있는 자리는 이것뿐이다 — 무대 기준선이 여기 선다.
        private readonly HashSet<string> _sceneRoots;

        public bool IsSceneRoot(string episodeId) =>
            episodeId != null && _sceneRoots.Contains(episodeId);

        private HashSet<string> CollectSceneRoots()
        {
            var roots = new HashSet<string>(StringComparer.Ordinal) { StartEpisodeId };

            foreach (EpisodeNode node in Nodes)
            {
                IReadOnlyList<EpisodeOption> options = node.NextOptions;

                for (int i = 0; i < options.Count; i++)
                {
                    if (_nodesById.TryGetValue(options[i].TargetEpisodeId, out EpisodeNode target) &&
                        !string.Equals(node.SceneId, target.SceneId, StringComparison.Ordinal))
                    {
                        roots.Add(target.EpisodeId);
                    }
                }
            }

            return roots;
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

        // 두 에피소드가 한 장면 안에 있는가. 장면 경계를 판단하는 자리는 이 답 하나만 본다.
        //
        // 모르는 에피소드는 "다르다"로 읽는다 — 새 장면으로 여는 쪽이 안전하다.
        // 이어 놓고 틀리면 무대가 이전 장면의 것을 물고 가지만, 끊어 놓고 틀리면
        // 한 번 더 초기화될 뿐이다.
        public bool IsSameScene(string episodeId, string otherEpisodeId)
        {
            if (!TryGetNode(episodeId, out EpisodeNode node) ||
                !TryGetNode(otherEpisodeId, out EpisodeNode other))
            {
                return false;
            }

            return string.Equals(node.SceneId, other.SceneId, StringComparison.Ordinal);
        }

        public string SceneIdOf(string episodeId) =>
            TryGetNode(episodeId, out EpisodeNode node) ? node.SceneId : null;

        // 이 챕터에 들어설 때의 [2] 상태. 챕터 데이터가 챕터 수명 상태를 만든다.
        //
        // 실제 플레이와 도달성 증명이 같은 자리에서 출발한다 — 증명이 통과한 길은
        // 플레이에서도 통과한다.
        public ProgressionState CreateEntryState() =>
            ProgressionState.CreateInitial(Stats, StartEpisodeId);

        public override string ToString() =>
            $"{ChapterId}(에피소드 {Nodes.Count}, 스탯 {Stats.Count})";
    }
}