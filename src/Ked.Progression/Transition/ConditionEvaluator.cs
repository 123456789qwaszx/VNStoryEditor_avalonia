using System;

namespace Ked.Progression
{
    public static class ConditionEvaluator
    {
        public static bool IsMet(in ProgressionCondition condition, ProgressionState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            switch (condition.Kind)
            {
                case ConditionKind.EpisodeCleared:
                    return state.IsEpisodeCleared(condition.Key);

                case ConditionKind.ChapterCleared:
                    return state.IsChapterCleared(condition.Key);

                case ConditionKind.Stat:
                    return EvaluateStat(condition, state);

                default:
                    throw new NotSupportedException(
                        $"처리되지 않은 조건 종류 '{condition.Kind}'.");
            }
        }

        private static bool EvaluateStat(
            in ProgressionCondition condition, ProgressionState state)
        {
            // 규칙: 정의되지 않은 키면 GetStat 소관.
            int value = state.GetStat(condition.Key);

            switch (condition.Op)
            {
                case ComparisonOp.GreaterOrEqual: return value >= condition.Value;
                case ComparisonOp.LessOrEqual:    return value <= condition.Value;
                case ComparisonOp.Equal:          return value == condition.Value;
                case ComparisonOp.GreaterThan:    return value > condition.Value;
                case ComparisonOp.LessThan:       return value < condition.Value;

                case ComparisonOp.Exists:
                    return true;

                default:
                    throw new NotSupportedException(
                        $"처리되지 않은 비교 연산 '{condition.Op}'.");
            }
        }
    }
}