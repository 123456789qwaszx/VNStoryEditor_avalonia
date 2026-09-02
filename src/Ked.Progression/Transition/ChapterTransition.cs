using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    // 지금 에피소드에서 갈 수 있는 길을 확정.
    // - 계획을 먼저 확정하고 그 뒤엔 고르기만 함.
    // - 여기서 한 번 판정하면 그 결과를 화면에 표시.
    // - 플레이어가 고른 뒤에야 값 변경.
    public static class ChapterTransition
    {
        private static readonly ResolvedOption[] None = new ResolvedOption[0];

        public static ChapterAdvance Resolve(ChapterProgression chapter, ProgressionState state)
        {
            if (chapter == null) throw new ArgumentNullException(nameof(chapter));
            if (state == null) throw new ArgumentNullException(nameof(state));
            
            if (!chapter.TryGetNode(state.CurrentEpisodeId, out EpisodeNode node))
                throw new ArgumentException(
                    $"지금 에피소드 '{state.CurrentEpisodeId}'가 챕터 '{chapter.ChapterId}'에 없다.", nameof(state));
            
            IReadOnlyList<EpisodeOption> options = node.NextOptions;

            // 자동 간선 — 불변식이 "유일한 간선·조건 없음"을 보장하므로 판정할 것이 없다.
            if (options.Count == 1 && options[0].IsAuto)
                return new ChapterAdvance(
                    ChapterAdvanceKind.AutoAdvance,
                    new[] { ResolvedOption.Shown(options[0], 0) },
                    0);

            var shownOrLocked = new List<ResolvedOption>();
            int hidden = 0;
            bool anySelectable = false;

            for (int i = 0; i < options.Count; i++)
            {
                EpisodeOption option = options[i];
                
                if (FirstUnmet(option.VisibleConditions, state).IsConstructed)
                {
                    // 표시조건 미달이면 목록에 만들지 않는다.
                    hidden++;
                    continue;
                }

                ProgressionCondition blocking = FirstUnmet(option.Conditions, state);

                if (!blocking.IsConstructed)
                {
                    shownOrLocked.Add(ResolvedOption.Shown(option, i));
                    anySelectable = true;
                    continue;
                }

                shownOrLocked.Add(ResolvedOption.Locked(option, i, blocking));
            }

            if (anySelectable)
                return new ChapterAdvance(
                    ChapterAdvanceKind.AwaitPlayerChoice, shownOrLocked, hidden);
            
            return new ChapterAdvance(
                ChapterAdvanceKind.ChapterEnded, None, hidden);
        }

        // 미달 조건 중 첫번째 것.
        private static ProgressionCondition FirstUnmet(
            IReadOnlyList<ProgressionCondition> conditions, ProgressionState state)
        {
            for (int i = 0; i < conditions.Count; i++)
            {
                if (!ConditionEvaluator.IsMet(conditions[i], state))
                    return conditions[i];
            }

            return default;
        }
    }
}