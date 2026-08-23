using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    // 챕터가 끝난 뒤 동작.
    // 빈 NextChapterId를 "시나리오 종료"의 뜻으로 쓰지 않음.
    public enum EndingOutcome
    {
        // 다음 챕터로.
        NextChapter = 0,

        // 시나리오 끝. 종착점.
        ScenarioEnd = 1,
    }

    // 시나리오 층의 간선에 대응.
    // 어느 엔딩인지는 이 타입이 정하지 않음.
    public sealed class EndingRule
    {
        public EndingOutcome Outcome { get; }

        /// <summary><see cref="EpisodeNode.EndingKey"/>와 맞물린다.</summary>
        public string EndingKey { get; }

        public string DisplayName { get; }

        /// <summary>
        /// 같은 엔딩키의 규칙이 여럿일 때 갈래를 고른다(AND). <b>비어 있으면 무조건 성립</b>
        /// 하며, 같은 키의 <b>마지막</b> 규칙은 반드시 비어 있어야 한다 — 그래야 "엔딩에
        /// 도달했는데 갈 곳이 없다"가 생기지 않는다.
        /// </summary>
        public IReadOnlyList<ProgressionCondition> Conditions { get; }

        // "EndingOutcome.ScenarioEnd"면 빈 문자열이다.
        public string NextChapterId { get; }

        public string DesignerNote { get; }

        // 조건이 없어 무조건 성립하는 규칙 — 같은 키의 마지막 자리.
        public bool IsCatchAll => Conditions.Count == 0;

        private EndingRule(
            EndingOutcome outcome,
            string endingKey,
            string nextChapterId,
            IReadOnlyList<ProgressionCondition> conditions,
            string displayName,
            string designerNote)
        {
            if (string.IsNullOrEmpty(endingKey))
            {
                throw new ArgumentException(
                    "엔딩 규칙의 엔딩키가 비어 있다. 어느 엔딩의 길인지 알 수 없다.",
                    nameof(endingKey));
            }

            Outcome = outcome;
            EndingKey = endingKey;
            NextChapterId = nextChapterId ?? string.Empty;
            Conditions = conditions ?? Array.Empty<ProgressionCondition>();
            DisplayName = displayName ?? string.Empty;
            DesignerNote = designerNote ?? string.Empty;

            ProgressionCondition.RequireAllConstructed(Conditions, nameof(conditions));
        }

        // 이 엔딩에서 다음 챕터로 이어짐.
        public static EndingRule To(
            string endingKey,
            string nextChapterId,
            IReadOnlyList<ProgressionCondition> conditions = null,
            string displayName = null,
            string designerNote = null)
        {
            if (string.IsNullOrEmpty(nextChapterId))
            {
                throw new ArgumentException(
                    $"엔딩 '{endingKey}'의 다음 챕터가 비어 있다. " +
                    "여기서 시나리오가 끝나는 것이라면 EndingRule.Ends()를 쓸 것.",
                    nameof(nextChapterId));
            }

            return new EndingRule(
                EndingOutcome.NextChapter, endingKey, nextChapterId,
                conditions, displayName, designerNote);
        }

        // 이 엔딩에서 시나리오 종료.
        public static EndingRule Ends(
            string endingKey,
            IReadOnlyList<ProgressionCondition> conditions = null,
            string displayName = null,
            string designerNote = null)
        {
            return new EndingRule(
                EndingOutcome.ScenarioEnd, endingKey, string.Empty,
                conditions, displayName, designerNote);
        }

        public override string ToString() =>
            Outcome == EndingOutcome.ScenarioEnd
                ? $"{EndingKey} → (시나리오 종료)"
                : $"{EndingKey} → {NextChapterId}";
    }
}