using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    public enum ScenarioAdvanceKind
    {
        /// <summary>다음 챕터로 이어진다.</summary>
        NextChapter = 0,

        /// <summary>엔딩에 도달했고 그 길이 종착이다. <b>의도한 끝이다.</b></summary>
        ScenarioEnded = 1,

        /// <summary>
        /// 엔딩키가 없는 노드에서 멈췄다 — 나가는 길이 하나도 없는데 엔딩도 아니다.
        ///
        /// <b>따로 둔 이유가 있다.</b> 이것을 <see cref="ScenarioEnded"/>에 섞으면 미완성
        /// 노드에서 게임이 끝난 것과 작가가 의도한 종착이 화면에서 같아 보인다. 도달성 증명이
        /// 잡는 자리이기도 하지만, 런타임에서도 구별될 수 있어야 한다.
        /// </summary>
        DeadEnd = 2,
    }

    /// <summary>
    /// 챕터가 끝난 뒤 무엇이 일어나는가. <b>해석 결과이지 상태가 아니다</b> —
    /// 저장하지 않는다(그래서 <c>[Serializable]</c>도 붙이지 않는다).
    /// </summary>
    public readonly struct ScenarioAdvance
    {
        public ScenarioAdvanceKind Kind { get; }

        /// <summary>
        /// 어느 엔딩으로 끝났는가. <b>노드가 정한다(D2).</b>
        /// <see cref="ScenarioAdvanceKind.DeadEnd"/>면 빈 문자열이다.
        /// </summary>
        public string EndingKey { get; }

        /// <summary>
        /// 고른 규칙. 챕터에 엔딩 규칙이 아예 없으면(단일 챕터 시나리오) <c>null</c>이다.
        /// </summary>
        public EndingRule MatchedRule { get; }

        /// <summary><see cref="ScenarioAdvanceKind.NextChapter"/>일 때만 채워진다.</summary>
        public string NextChapterId { get; }

        private ScenarioAdvance(
            ScenarioAdvanceKind kind, string endingKey, EndingRule matchedRule, string nextChapterId)
        {
            Kind = kind;
            EndingKey = endingKey;
            MatchedRule = matchedRule;
            NextChapterId = nextChapterId;
        }

        internal static ScenarioAdvance ToChapter(string endingKey, EndingRule rule) =>
            new ScenarioAdvance(
                ScenarioAdvanceKind.NextChapter, endingKey, rule, rule.NextChapterId);

        internal static ScenarioAdvance Ended(string endingKey, EndingRule rule) =>
            new ScenarioAdvance(
                ScenarioAdvanceKind.ScenarioEnded, endingKey, rule, string.Empty);

        internal static ScenarioAdvance DeadEnd() =>
            new ScenarioAdvance(
                ScenarioAdvanceKind.DeadEnd, string.Empty, null, string.Empty);

        public override string ToString() =>
            Kind == ScenarioAdvanceKind.NextChapter
                ? $"{Kind}({EndingKey} → {NextChapterId})"
                : $"{Kind}({EndingKey})";
    }

    /// <summary>
    /// 챕터 런이 끝났을 때 다음을 정한다. 순수 함수다 — 상태를 읽기만 한다(규율 4).
    ///
    /// <b>D2가 이 시그니처에 들어 있다.</b> 엔딩키를 인자로 받지 않고 <b>지금 노드에서
    /// 읽는다</b> — 호출자가 엉뚱한 키를 넘길 자리가 아예 없다. 엔딩을 정하는 곳이
    /// 하나뿐임을 타입이 보증한다.
    /// </summary>
    public static class ScenarioTransition
    {
        public static ScenarioAdvance Resolve(
            ScenarioProgression scenario, ProgressionState state)
        {
            if (scenario == null)
                throw new ArgumentNullException(nameof(scenario));

            if (state == null)
                throw new ArgumentNullException(nameof(state));

            if (!scenario.TryGetChapter(state.CurrentChapterId, out ChapterProgression chapter))
            {
                throw new ArgumentException(
                    $"지금 챕터 '{state.CurrentChapterId}'가 시나리오 '{scenario.ScenarioId}'에 없다.",
                    nameof(state));
            }

            if (!chapter.TryGetNode(state.CurrentEpisodeId, out EpisodeNode node))
            {
                throw new ArgumentException(
                    $"지금 에피소드 '{state.CurrentEpisodeId}'가 챕터 '{chapter.ChapterId}'에 없다.",
                    nameof(state));
            }

            if (!node.IsEndingCandidate)
            {
                return ScenarioAdvance.DeadEnd();
            }

            string endingKey = node.EndingKey;
            IReadOnlyList<EndingRule> rules = chapter.EndingRules;
            bool sawKey = false;

            for (int i = 0; i < rules.Count; i++)
            {
                EndingRule rule = rules[i];

                if (!string.Equals(rule.EndingKey, endingKey, StringComparison.Ordinal))
                {
                    continue;
                }

                sawKey = true;

                if (!AreMet(rule.Conditions, state))
                {
                    continue;
                }

                return rule.Outcome == EndingOutcome.ScenarioEnd
                    ? ScenarioAdvance.Ended(endingKey, rule)
                    : ScenarioAdvance.ToChapter(endingKey, rule);
            }

            if (!sawKey)
            {
                // 챕터에 엔딩 규칙이 아예 없다 — 단일 챕터 시나리오의 정상적인 종착이다.
                // (규칙이 하나라도 있으면 노드의 키가 그 안에 있음을 생성자가 보장한다.)
                return ScenarioAdvance.Ended(endingKey, null);
            }

            // 여기 도달할 수 없다 — 같은 키의 마지막 규칙은 조건이 없어야 하고(무조건 성립),
            // 생성자가 그것을 강제한다. 도달했다면 불변식이 깨진 것이므로 조용히 끝내지 않는다.
            throw new InvalidOperationException(
                $"엔딩 '{endingKey}'의 규칙이 모두 미달이다. 같은 키의 마지막 규칙은 조건이 " +
                "없어야 한다(ChapterInvariants가 강제). 불변식을 우회해 만든 챕터가 아닌지 확인할 것.");
        }

        private static bool AreMet(
            IReadOnlyList<ProgressionCondition> conditions, ProgressionState state)
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
