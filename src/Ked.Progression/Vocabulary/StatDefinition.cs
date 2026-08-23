using System;

namespace Ked.Progression
{
    public enum StatType
    {
        // 정수 스탯. 경계는 저작이 정함.
        Number = 0,

        // 참/거짓 스탯. 값 공간은 0·1이고 조건은 "ComparisonOp.Equal"만 사용
        // 런타임에 bool이라는 별도 종류 추가 안함.
        Bool = 1,
    }

    // 스탯 정의.
    public sealed class StatDefinition
    {
        public string Key { get; }
        public string DisplayName { get; }
        public StatType Type { get; }

        public int Initial { get; }
        public int Minimum { get; }
        public int Maximum { get; }

        // 불변식을 만족하지 않으면 만들어지지 않는다.
        // 잘못된 정의를 조용히 보정하면, 작가는 자기가 쓴 값이 아닌 것으로 플레이하게 된다.
        // 로더 없이 불러 온 것에 대한 방어.
        public StatDefinition(
            string key,
            string displayName,
            StatType type,
            int initial,
            int minimum,
            int maximum)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("스탯 키가 비어 있다.", nameof(key));

            if (minimum > maximum)
                throw new ArgumentException(
                    $"스탯 '{key}'의 경계가 뒤집혀 있다: 최소 {minimum} > 최대 {maximum}.",
                    nameof(minimum));

            if (type == StatType.Bool && (minimum != 0 || maximum != 1))
                throw new ArgumentException(
                    $"bool 스탯 '{key}'의 경계는 0..1이어야 한다 (§G4). 받은 값: {minimum}..{maximum}.",
                    nameof(type));

            if (initial < minimum || initial > maximum)
                throw new ArgumentException(
                    $"스탯 '{key}'의 초기값 {initial}이 경계 {minimum}..{maximum} 밖이다. " +
                    "조용히 clamp하면 작가가 쓴 값과 다른 값으로 시작하게 된다.",
                    nameof(initial));

            Key = key;
            DisplayName = displayName;
            Type = type;
            Initial = initial;
            Minimum = minimum;
            Maximum = maximum;
        }

        // 경계 안으로 자름. 커밋 시점에만 호출.
        // 조건 판정은 커밋 전 값으로 하기 때문.
        public int Clamp(int value)
        {
            if (value < Minimum)
                return Minimum;

            return value > Maximum ? Maximum : value;
        }
    }
}