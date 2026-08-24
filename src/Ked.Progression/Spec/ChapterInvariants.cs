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
    // - 챕터 그래프와 스탯/엔딩 규칙간 모순을 찾아서 차단.
    internal static class ChapterInvariants
    {
        public static void Collect(
            IReadOnlyList<StatDefinition> stats,
            IReadOnlyList<EpisodeNode> nodes,
            IReadOnlyList<EndingRule> endingRules,
            string startEpisodeId,
            ICollection<ProgressionDiagnostic> into,
            out Dictionary<string, StatDefinition> statsByKey,
            out Dictionary<string, EpisodeNode> nodesById)
        {
            statsByKey = IndexStats(stats, into);
            nodesById = IndexNodes(nodes, into);

            VerifyStart(startEpisodeId, nodesById, into);
            VerifyEdges(nodes, nodesById, statsByKey, into);
            VerifyEndingRules(endingRules, nodes, statsByKey, into);
        }

        // 어느 엔딩인지는 노드의 EndingKey가 정하고, 규칙은 그 키로 조회.
        private static void VerifyEndingRules(
            IReadOnlyList<EndingRule> rules,
            IReadOnlyList<EpisodeNode> nodes,
            Dictionary<string, StatDefinition> statsByKey,
            ICollection<ProgressionDiagnostic> into)
        {
            var lastIndexByKey = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i] == null)
                {
                    into.Add(ProgressionDiagnostic.Error($"EndingRules[{i}]", "엔딩 규칙이 null이다."));
                    continue;
                }

                lastIndexByKey[rules[i].EndingKey] = i;
            }

            for (int i = 0; i < rules.Count; i++)
            {
                EndingRule rule = rules[i];

                if (rule == null)
                    continue;

                string at = $"EndingRules[{i}]";

                VerifyConditions(rule.Conditions, statsByKey, at + ".Conditions", into);

                bool isLastOfKey = lastIndexByKey[rule.EndingKey] == i;

                if (isLastOfKey && !rule.IsCatchAll)
                {
                    // 조건이 전부 미달이면 갈 곳이 없어진다.
                    // 모든 조건 검사 후, Catch-all 체크.
                    into.Add(ProgressionDiagnostic.Error(
                        at,
                        $"엔딩 '{rule.EndingKey}'의 마지막 규칙에 조건이 달렸다. " +
                        "조건이 전부 미달이면 엔딩에 도달하고도 갈 곳이 없다 — " +
                        "조건 없는 규칙을 마지막에 하나 두어 어디로 갈지(또는 끝난다고) 적을 것."));
                }

                if (!isLastOfKey && rule.IsCatchAll)
                {
                    into.Add(ProgressionDiagnostic.Error(
                        at,
                        $"엔딩 '{rule.EndingKey}'의 조건 없는 규칙 뒤에 같은 키의 규칙이 더 있다. " +
                        "무조건 성립하므로 뒤엣것은 영원히 타지 않는다."));
                }
            }

            VerifyEndingKeysMatch(rules, nodes, lastIndexByKey, into);
        }

        // 양방향 검사:
        // - EndingKey = "happy" 일 때, Rule에도 "EndingKey "happy"가 있는지
        private static void VerifyEndingKeysMatch(
            IReadOnlyList<EndingRule> rules,
            IReadOnlyList<EpisodeNode> nodes,
            Dictionary<string, int> lastIndexByKey,
            ICollection<ProgressionDiagnostic> into)
        {
            var producedKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (EpisodeNode node in nodes)
            {
                if (node == null || !node.IsEndingCandidate)
                    continue;

                producedKeys.Add(node.EndingKey);

                if (rules.Count > 0 && !lastIndexByKey.ContainsKey(node.EndingKey))
                {
                    into.Add(ProgressionDiagnostic.Error(
                        $"Nodes[{node.EpisodeId}]",
                        $"엔딩키 '{node.EndingKey}'에 맞는 규칙이 없다. " +
                        $"있는 것: {Join(lastIndexByKey.Keys)}"));
                }
            }

            foreach (KeyValuePair<string, int> pair in lastIndexByKey)
            {
                if (producedKeys.Contains(pair.Key))
                    continue;
                
                into.Add(ProgressionDiagnostic.Error(
                    $"EndingRules[{pair.Value}]",
                    $"엔딩키 '{pair.Key}'를 내는 노드가 없다. 이 규칙은 영원히 타지 않는다."));
            }
        }

        private static Dictionary<string, StatDefinition> IndexStats(
            IReadOnlyList<StatDefinition> stats, ICollection<ProgressionDiagnostic> into)
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
            IReadOnlyList<EpisodeNode> nodes, ICollection<ProgressionDiagnostic> into)
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

                    if (!nodesById.ContainsKey(option.TargetEpisodeId))
                    {
                        into.Add(ProgressionDiagnostic.Error(
                            where,
                            $"도착 '{option.TargetEpisodeId}'가 노드에 없다. " +
                            $"있는 것: {Join(nodesById.Keys)}"));
                    }

                    VerifyConditions(
                        option.VisibleConditions, statsByKey, where + ".VisibleConditions", into);
                    VerifyConditions(
                        option.Conditions, statsByKey, where + ".Conditions", into);
                    VerifyStatChanges(
                        option.StatChanges, statsByKey, where + ".StatChanges", into);
                }
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