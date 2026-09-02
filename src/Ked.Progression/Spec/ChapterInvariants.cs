using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    // 전 챕터 공통 규칙:
    // [1]ID는 유일.
    // [2]참조는 반드시 실재.
    // [3]데이터 표현의 자유는 제한.
    // [4]런타임 암묵적 결정 제거.
    
    // ChapterProgression, ProgressionLoader가 사용.
    // - 챕터 그래프와 스탯 간 모순을 찾아서 차단.
    internal static class ChapterInvariants
    {
        public static void Collect(
            IReadOnlyList<StatDefinition> stats,
            IReadOnlyList<EpisodeNode> nodes,
            string startEpisodeId,
            ICollection<ProgressionDiagnostic> into,
            out Dictionary<string, StatDefinition> statsByKey,
            out Dictionary<string, EpisodeNode> nodesById)
        {
            statsByKey = IndexStats(stats, into);
            nodesById = IndexNodes(nodes, into);

            VerifyStart(startEpisodeId, nodesById, into);
            VerifyEdges(nodes, nodesById, statsByKey, into);
            VerifySceneEntries(nodes, nodesById, startEpisodeId, into);
        }

        private static Dictionary<string, StatDefinition> IndexStats(
            IReadOnlyList<StatDefinition> stats, 
            ICollection<ProgressionDiagnostic> into)
        {
            var byKey = new Dictionary<string, StatDefinition>(StringComparer.Ordinal);

            for (int i = 0; i < stats.Count; i++)
            {
                StatDefinition stat = stats[i];

                if (stat == null)
                {
                    into.Add(ProgressionDiagnostic.Error($"Stats[{i}]", "스탯 정의가 null이다."));
                    continue;
                }

                if (byKey.ContainsKey(stat.Key))
                {
                    // 뒤엣것이 이기면 초기값/경계가 조용히 갈린다.
                    into.Add(ProgressionDiagnostic.Error(
                        $"Stats[{i}]", $"스탯 키 '{stat.Key}'가 중복 정의됐다."));
                    continue;
                }

                byKey[stat.Key] = stat;
            }

            return byKey;
        }

        private static Dictionary<string, EpisodeNode> IndexNodes(
            IReadOnlyList<EpisodeNode> nodes, 
            ICollection<ProgressionDiagnostic> into)
        {
            var byId = new Dictionary<string, EpisodeNode>(StringComparer.Ordinal);

            for (int i = 0; i < nodes.Count; i++)
            {
                EpisodeNode node = nodes[i];

                if (node == null)
                {
                    into.Add(ProgressionDiagnostic.Error($"Nodes[{i}]", "에피소드가 null이다."));
                    continue;
                }

                if (string.IsNullOrEmpty(node.EpisodeId))
                {
                    into.Add(ProgressionDiagnostic.Error($"Nodes[{i}]", "에피소드 ID가 비어 있다."));
                    continue;
                }

                if (byId.ContainsKey(node.EpisodeId))
                {
                    // 같은 ID가 둘이면 간선이 어느 쪽으로 가는지가 사전 구현에 달린다.
                    into.Add(ProgressionDiagnostic.Error(
                        $"Nodes[{i}]", $"에피소드 ID '{node.EpisodeId}'가 중복이다."));
                    continue;
                }

                byId[node.EpisodeId] = node;
            }

            return byId;
        }

        private static void VerifyStart(
            string startEpisodeId,
            Dictionary<string, EpisodeNode> nodesById,
            ICollection<ProgressionDiagnostic> into)
        {
            if (!string.IsNullOrEmpty(startEpisodeId) && nodesById.ContainsKey(startEpisodeId))
                return;

            into.Add(ProgressionDiagnostic.Error(
                "StartEpisodeId",
                string.IsNullOrEmpty(startEpisodeId)
                    ? "시작 에피소드가 비어 있다. 챕터를 시작할 자리가 없다."
                    : $"시작 에피소드 '{startEpisodeId}'가 노드에 없다. " +
                      $"있는 것: {Join(nodesById.Keys)}"));
        }

        private static void VerifyEdges(
            IReadOnlyList<EpisodeNode> nodes,
            Dictionary<string, EpisodeNode> nodesById,
            Dictionary<string, StatDefinition> statsByKey,
            ICollection<ProgressionDiagnostic> into)
        {
            foreach (EpisodeNode node in nodes)
            {
                if (node == null)
                    continue;
                
                IReadOnlyList<EpisodeOption> options = node.NextOptions;

                for (int i = 0; i < options.Count; i++)
                {
                    EpisodeOption option = options[i];
                    string where = $"Nodes[{node.EpisodeId}].NextOptions[{i}]";

                    if (!nodesById.TryGetValue(option.TargetEpisodeId, out EpisodeNode target))
                    {
                        into.Add(ProgressionDiagnostic.Error(
                            where,
                            $"도착 '{option.TargetEpisodeId}'가 노드에 없다. " +
                            $"있는 것: {Join(nodesById.Keys)}"));
                    }

                    if (option.IsAuto)
                        VerifyAuto(node, options.Count, option, target, where, into);

                    VerifyConditions(
                        option.VisibleConditions, statsByKey, where + ".VisibleConditions", into);
                    VerifyConditions(
                        option.Conditions, statsByKey, where + ".Conditions", into);
                    VerifyStatChanges(
                        option.StatChanges, statsByKey, where + ".StatChanges", into);
                }
            }
        }

        // 장면에 밖에서 들어오는 자리는 하나다.
        //
        // 그 자리가 장면 루트이고, 롤백이 되돌아가는 곳과 이어하기가 재개하는 곳이
        // 전부 그것이다. 착지점이 여럿이면 "이 장면은 어디서 시작하는가"가 경로마다
        // 달라져 데이터로 정해지지 않는다.
        //
        // 이 규칙 하나가 연결성까지 함께 본다 — 밖에서 오는 길이 루트뿐이므로 도달
        // 가능한 에피소드는 전부 루트에서 장면 안 간선으로 이어진다. 아무 데서도
        // 안 들어오는 고아 노드는 여기서 보지 않는다(도달성의 일이고, 이 클래스는
        // 구조적 무결성만 본다).
        //
        // 장면을 나갔다 되돌아오는 것은 막지 않는다 — 허브 구조(교실 ↔ 복도)가
        // 그것이고, 재진입은 루트에서 다시 여는 새 장면 방문일 뿐이다.
        private static void VerifySceneEntries(
            IReadOnlyList<EpisodeNode> nodes,
            Dictionary<string, EpisodeNode> nodesById,
            string startEpisodeId,
            ICollection<ProgressionDiagnostic> into)
        {
            var landings = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            // 규칙을 깬 자리 = 그 장면에 두 번째 착지점을 만든 간선.
            var offending = new Dictionary<string, string>(StringComparer.Ordinal);

            // 챕터 시작도 밖에서 들어오는 길이다 — 챕터가 그 장면을 여는 자리.
            if (!string.IsNullOrEmpty(startEpisodeId) &&
                nodesById.TryGetValue(startEpisodeId, out EpisodeNode start))
            {
                Land(landings, offending, start.SceneId, start.EpisodeId, "StartEpisodeId");
            }

            foreach (EpisodeNode node in nodes)
            {
                if (node == null)
                    continue;

                IReadOnlyList<EpisodeOption> options = node.NextOptions;

                for (int i = 0; i < options.Count; i++)
                {
                    // 도착이 실재하지 않는 간선은 VerifyEdges가 이미 잡았다.
                    if (!nodesById.TryGetValue(options[i].TargetEpisodeId, out EpisodeNode target))
                        continue;

                    // 장면 안에서 움직이는 간선은 들어오는 길이 아니다.
                    if (string.Equals(node.SceneId, target.SceneId, StringComparison.Ordinal))
                        continue;

                    Land(
                        landings, offending, target.SceneId, target.EpisodeId,
                        $"Nodes[{node.EpisodeId}].NextOptions[{i}]");
                }
            }

            foreach (KeyValuePair<string, HashSet<string>> scene in landings)
            {
                if (scene.Value.Count <= 1)
                    continue;

                into.Add(ProgressionDiagnostic.Error(
                    offending[scene.Key],
                    $"장면 '{scene.Key}'에 밖에서 들어오는 자리가 {scene.Value.Count}개다: " +
                    $"{Join(scene.Value)}. 장면은 한 자리에서만 시작해야 한다 — " +
                    "롤백이 되돌아갈 곳과 이어하기가 재개할 곳이 그 자리다. " +
                    "나머지 착지점은 다른 장면으로 나눌 것."));
            }
        }

        private static void Land(
            Dictionary<string, HashSet<string>> landings,
            Dictionary<string, string> offending,
            string sceneId,
            string episodeId,
            string path)
        {
            if (!landings.TryGetValue(sceneId, out HashSet<string> into))
            {
                into = new HashSet<string>(StringComparer.Ordinal);
                landings[sceneId] = into;
            }

            // 같은 자리로 여러 간선이 들어오는 것은 정상이다 — 자리의 수만 센다.
            if (into.Add(episodeId) && into.Count == 2)
                offending[sceneId] = path;
        }

        // 자동 간선 — 묻지 않고 지나가는 길은 넷을 지켜야 한다.
        // 유일한 간선(고를 것이 없어야 묻지 않는 게 맞다) · 조건 없음(판정할 것이 없어야 한다) ·
        // 스탯 변화 없음(스탯 효과는 플레이어에게 보이는 선택지에서만 일어난다 — 도달성 증명과 UX의 근거) ·
        // 같은 장면(장면 경계는 무대가 갈리는 자리라 모르게 넘어가면 이상하다).
        private static void VerifyAuto(
            EpisodeNode node,
            int siblingCount,
            EpisodeOption option,
            EpisodeNode target,
            string where,
            ICollection<ProgressionDiagnostic> into)
        {
            if (siblingCount != 1)
            {
                into.Add(ProgressionDiagnostic.Error(
                    where,
                    $"자동 간선은 그 에피소드의 유일한 간선이어야 한다. 지금 {siblingCount}개 — " +
                    "고를 것이 있으면 묻는 선택지로, 없으면 나머지를 지울 것."));
            }

            if (option.VisibleConditions.Count > 0 || option.Conditions.Count > 0)
            {
                into.Add(ProgressionDiagnostic.Error(
                    where, "자동 간선에는 조건을 둘 수 없다 — 판정할 것이 있으면 묻는 선택지다."));
            }

            if (option.StatChanges.Count > 0)
            {
                into.Add(ProgressionDiagnostic.Error(
                    where,
                    "자동 간선에는 스탯 변화를 둘 수 없다 — 스탯 효과는 플레이어에게 보이는 선택지에서만 " +
                    "일어난다. 스탯을 바꾸려면 문구를 주고 Auto를 끌 것."));
            }

            if (target != null &&
                !string.Equals(node.SceneId, target.SceneId, StringComparison.Ordinal))
            {
                into.Add(ProgressionDiagnostic.Error(
                    where,
                    $"자동 간선은 장면을 나갈 수 없다 ('{node.SceneId}' → '{target.SceneId}'). " +
                    "장면 경계는 무대가 갈리는 자리다 — 묻는 선택지로 둘 것."));
            }
        }

        private static void VerifyConditions(
            IReadOnlyList<ProgressionCondition> conditions,
            Dictionary<string, StatDefinition> statsByKey,
            string where,
            ICollection<ProgressionDiagnostic> into)
        {
            for (int i = 0; i < conditions.Count; i++)
            {
                ProgressionCondition condition = conditions[i];
                string at = $"{where}[{i}]";

                if (condition.Kind != ConditionKind.Stat)
                {
                    into.Add(ProgressionDiagnostic.Error(
                        at, $"처리되지 않은 조건 종류 '{condition.Kind}'."));
                    continue;
                }

                if (!statsByKey.TryGetValue(condition.Key, out StatDefinition stat))
                {
                    // 없는 키를 0으로 읽으면 오타 낸 조건이
                    // "언제나 통과하는 관문"이 되고, 그 버그는 재생해 봐도 안 보인다.
                    into.Add(ProgressionDiagnostic.Error(
                        at,
                        $"정의되지 않은 스탯 '{condition.Key}'. " +
                        $"정의된 것: {Join(statsByKey.Keys)}"));
                    continue;
                }

                if (stat.Type != StatType.Bool)
                    continue;
                
                if (condition.Op != ComparisonOp.Equal)
                {
                    into.Add(ProgressionDiagnostic.Error(
                        at,
                        $"'{condition.Key}'는 bool 스탯이라 Equal만 쓸 수 있다(§G4). " +
                        $"받은 연산: {condition.Op}."));
                    continue;
                }

                if (condition.Value != 0 && condition.Value != 1)
                {
                    into.Add(ProgressionDiagnostic.Error(
                        at,
                        $"'{condition.Key}'는 bool 스탯이라 비교값이 0 또는 1이어야 한다(§G4). " +
                        $"받은 값: {condition.Value}."));
                }
            }
        }

        // 선택지 골랐을 때의 스탯 변경 구조 검사
        // [1]동일 키 금지.
        // - 같은 키를 두 번 '정하면' 어느 쪽이 사는지가 배열 순서에 달리고,
        // - 시트에서 행을 옮기는 것만으로 결과가 바뀜.
        
        // [2]숫자에 Set금지.
        // - 스탯 변화의 연속적인 범위 추적이 복잡해짐. 데이터 분석 난이도 보존.
        
        // [3]bool에 Add금지, 한 간선에 Bool 두 번 금지.
        // - add연산을 허용하면 clamp를 거치며 의도가 불분명해짐.
        // - 중복 시 마지막 것이 이기는데, 시트에서 행을 옮기는 것만으로 결과가 바뀜.
        private static void VerifyStatChanges(
            IReadOnlyList<StatChange> changes,
            Dictionary<string, StatDefinition> statsByKey,
            string where,
            ICollection<ProgressionDiagnostic> into)
        {
            var setKeys = new HashSet<string>(StringComparer.Ordinal);
            var reportedDuplicates = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < changes.Count; i++)
            {
                StatChange change = changes[i];
                string at = $"{where}[{i}]";

                if (!statsByKey.TryGetValue(change.Key, out StatDefinition stat))
                {
                    into.Add(ProgressionDiagnostic.Error(
                        at,
                        $"정의되지 않은 스탯 '{change.Key}'을(를) 변경하려 한다. " +
                        $"정의된 것: {Join(statsByKey.Keys)}"));
                    continue;
                }

                if (change.Kind == StatChangeKind.Set)
                {
                    VerifySet(change, stat, at, setKeys, reportedDuplicates, into);
                    continue;
                }

                // bool 스탯에 증감은 의미가 없다(0/1 사이를 +1로 오가면 clamp에 걸려
                // 한 방향으로만 간다). 켜고 끄는 것은 Set이 한다.
                if (stat.Type == StatType.Bool)
                {
                    into.Add(ProgressionDiagnostic.Error(
                        at,
                        $"'{change.Key}'는 bool 스탯이라 증감을 쓸 수 없다(§G4). " +
                        "켜고 끄려면 Op를 \"Set\"으로 둘 것."));
                }
            }
        }

        private static void VerifySet(
            StatChange change,
            StatDefinition stat,
            string at,
            HashSet<string> setKeys,
            HashSet<string> reportedDuplicates,
            ICollection<ProgressionDiagnostic> into)
        {
            // 숫자 스탯을 '정하면' 도달성 증명의 스탯 폭이 뜻을 잃는다 — 이 간선을
            // 지난 순간 앞의 모든 경로가 하나로 접히기 때문이다. 깃발에만 연다.
            if (stat.Type != StatType.Bool)
            {
                into.Add(ProgressionDiagnostic.Error(
                    at,
                    $"'{change.Key}'는 숫자 스탯이라 지정(Set)을 쓸 수 없다(§G4). " +
                    "지정은 bool 스탯(깃발)에만 쓴다 — 증감이면 Op를 비우거나 \"Add\"로 둘 것."));

                return;
            }

            if (change.Amount != 0 && change.Amount != 1)
            {
                into.Add(ProgressionDiagnostic.Error(
                    at,
                    $"'{change.Key}'는 bool 스탯이라 정할 값이 0 또는 1이어야 한다(§G4). " +
                    $"받은 값: {change.Amount}. " +
                    "조용히 clamp하면 작가가 쓴 값과 다른 값으로 켜진다."));

                return;
            }

            if (!setKeys.Add(change.Key) && reportedDuplicates.Add(change.Key))
            {
                into.Add(ProgressionDiagnostic.Error(
                    at,
                    $"한 간선에서 '{change.Key}'를 두 번 정한다. 어느 쪽이 사는지가 " +
                    "행 순서에 달리므로 시트에서 행을 옮기는 것만으로 결과가 바뀐다 — " +
                    "하나만 남길 것."));
            }
        }

        private static string Join(IEnumerable<string> keys) => string.Join(", ", keys);
    }
}