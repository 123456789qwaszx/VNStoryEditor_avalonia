using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    // 챕터 하나가 끝났을 때 무엇이 다음인가. 엔딩키는 지금 노드에서 곧장 읽는다.
    //
    // 상태를 만들지도 옮기지도 않는다 — "끝" 또는 "다음 챕터 ID"만 낸다.
    // 다음 챕터의 실재는 ScenarioInvariants가 이미 보장했다.
    public static class ScenarioTransition
    {
        public static ScenarioAdvance Resolve(ChapterProgression chapter, ProgressionState state)
        {
            if (chapter == null)
                throw new ArgumentNullException(nameof(chapter));

            if (state == null)
                throw new ArgumentNullException(nameof(state));

            if (!chapter.TryGetNode(state.CurrentEpisodeId, out EpisodeNode node))
            {
                throw new ArgumentException(
                    $"지금 에피소드 '{state.CurrentEpisodeId}'가 챕터 '{chapter.ChapterId}'에 없다.",
                    nameof(state));
            }

            if (!node.IsEndingCandidate)
                return ScenarioAdvance.DeadEnd();

            string endingKey = node.EndingKey;
            IReadOnlyList<EndingRule> rules = chapter.EndingRules;
            bool sawKey = false;

            for (int i = 0; i < rules.Count; i++)
            {
                EndingRule rule = rules[i];

                if (!string.Equals(rule.EndingKey, endingKey, StringComparison.Ordinal))
                    continue;

                sawKey = true;

                if (!AreMet(rule.Conditions, state))
                    continue;

                return rule.Outcome == EndingOutcome.ScenarioEnd
                    ? ScenarioAdvance.Ended(endingKey)
                    : ScenarioAdvance.ToChapter(endingKey, rule.NextChapterId);
            }

            if (!sawKey)
                return ScenarioAdvance.Ended(endingKey);
            
            // 여기 도달할 수 없다 — 같은 키의 마지막 규칙은 조건이 없어야 하고(무조건 성립),
            // 생성자가 그것을 강제한다. 도달했다면 불변식이 깨진 것이므로 조용히 끝내지 않는다.
            throw new InvalidOperationException(
                $"엔딩 '{endingKey}'의 규칙이 모두 미달이다. 같은 키의 마지막 규칙은 조건이 " +
                "없어야 한다(ChapterInvariants가 강제). 불변식을 우회해 만든 챕터가 아닌지 확인할 것.");
        }

        private static bool AreMet(IReadOnlyList<ProgressionCondition> conditions, ProgressionState state)
        {
            for (int i = 0; i < conditions.Count; i++)
            {
                if (!ConditionEvaluator.IsMet(conditions[i], state))
                    return false;
            }

            return true;
        }
    }
}