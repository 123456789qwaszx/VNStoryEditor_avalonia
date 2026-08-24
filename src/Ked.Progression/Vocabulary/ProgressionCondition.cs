using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    /// <summary>
    /// 조건이 무엇을 묻는가.
    ///
    /// 시나리오 수명의 상태(클리어 이력/본 라인/화자)가
    /// [1] 영구 계층으로 제대로 서면 그때 갈래가 늘어날 예정.
    /// </summary>
    public enum ConditionKind
    {
        Stat = 0, // Compares a stat value using "ComparisonOp".
    }

    public readonly struct ProgressionCondition
    {
        public ConditionKind Kind { get; }
        public string Key { get; }
        public ComparisonOp Op { get; }

        // Value to compare against.
        // { "Kind": "Stat", "Key": "trust", "Op": "GreaterOrEqual" }
        public int Value { get; }

        private ProgressionCondition(ConditionKind kind, string key, ComparisonOp op, int value)
        {
            Kind = kind;
            Key = key;
            Op = op;
            Value = value;
        }

        public static ProgressionCondition Stat(string key, ComparisonOp op, int value = 0) =>
            new(ConditionKind.Stat, Require(key, nameof(key)), op, value);

        // Whether this value was created through one of the factory methods.
        // C# can always create default(ProgressionCondition).
        // If such a value appears in an array, the owning type rejects it;
        // the evaluator does not need to check it again.
        public bool IsConstructed => Key != null;

        // Ensures that no default values are present in the condition list.
        internal static void RequireAllConstructed(IReadOnlyList<ProgressionCondition> conditions, string paramName)
        {
            for (int i = 0; i < conditions.Count; i++)
            {
                if (conditions[i].IsConstructed)
                    continue;
                
                throw new ArgumentException(
                    $"{paramName}[{i}] is an unconstructed condition (default value). " +
                    "Create conditions using ProgressionCondition.Stat.",
                    paramName);
            }
        }

        public override string ToString() => $"{Key} {Op} {Value}";

        private static string Require(string value, string paramName)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("The condition target key cannot be empty.", paramName);

            return value;
        }
    }
}