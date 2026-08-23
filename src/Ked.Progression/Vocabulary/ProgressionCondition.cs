using System;

namespace Ked.Progression
{
    public enum ConditionKind
    {
        Stat = 0, // 스탯 값을 "ComparisonOp"로 비교.
        EpisodeCleared = 1, // 에피소드를 클리어했는가. "ComparisonOp.Exists" 조회
        ChapterCleared = 2, // 챕터를 클리어했는가. "ComparisonOp.Exists" 조회
    }

    // 진행 조건.
    public readonly struct ProgressionCondition
    {
        public ConditionKind Kind { get; }
        public string Key { get; }
        public ComparisonOp Op { get; }

        // 비교 대상 값.
        // { "Kind": "Stat", "Key": "trust", "Op": "GreaterOrEqual" }
        public int Value { get; }

        private ProgressionCondition(ConditionKind kind, string key, ComparisonOp op, int value)
        {
            Kind = kind;
            Key = key;
            Op = op;
            Value = value;
        }

        // 스탯 비교. 연산 6종 전부 사용
        public static ProgressionCondition Stat(string key, ComparisonOp op, int value = 0) 
            => new (
                ConditionKind.Stat, 
                Require(key, nameof(key)), op, value);

        public static ProgressionCondition EpisodeCleared(string episodeId) 
            => new (
                ConditionKind.EpisodeCleared, 
                Require(episodeId, nameof(episodeId)), 
                ComparisonOp.Exists, 
                0);

        public static ProgressionCondition ChapterCleared(string chapterId)
            => new (
                ConditionKind.ChapterCleared,
                Require(chapterId, nameof(chapterId)),
                ComparisonOp.Exists,
                0);

        /// <summary>
        /// 팩토리를 거쳐 만들어진 값인가.
        ///
        /// ⚠ <c>default(ProgressionCondition)</c>은 C#이 언제나 만들 수 있다 — struct의
        /// 한계이고 막을 방법이 없다. 그 값은 <see cref="Key"/>가 <c>null</c>이므로
        /// <b>여기서 판별된다.</b> 배열에 그런 값이 섞이는 것은 소유 타입의 생성자가
        /// 거부하고, 평가기는 다시 확인하지 않는다 — 경계에서 좁혔으므로 안쪽은 전체 함수다.
        /// </summary>
        public bool IsConstructed => Key != null;

        // 조건 목록에 default가 섞이지 않았는지 확인.
        // 조건을 품는 타입이 여럿이므로(간선 · 엔딩 규칙) 검사를 여기 둠.
        internal static void RequireAllConstructed(
            System.Collections.Generic.IReadOnlyList<ProgressionCondition> conditions,
            string paramName)
        {
            for (int i = 0; i < conditions.Count; i++)
            {
                if (conditions[i].IsConstructed)
                {
                    continue;
                }

                throw new ArgumentException(
                    $"{paramName}[{i}]가 만들어지지 않은 조건이다(default 값). " +
                    "ProgressionCondition.Stat/EpisodeCleared/ChapterCleared로 만들 것.",
                    paramName);
            }
        }

        public override string ToString()
        {
            return Kind == ConditionKind.Stat
                ? $"{Key} {Op} {Value}"
                : $"{Kind}({Key})";
        }

        private static string Require(string value, string paramName)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("조건의 대상 키가 비어 있다.", paramName);

            return value;
        }
    }
}