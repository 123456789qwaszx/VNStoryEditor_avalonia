namespace Ked.Progression
{
    // 특정 에피소드 도달 시,
    // 가능한 스탯 폭 계산.
    public sealed class StatSpan
    {
        public string Key { get; }
        public string DisplayName { get; }
        public int Minimum { get; }
        public int Maximum { get; }

        // 모든 루트에서 동일한 지.
        public bool IsFixed => Minimum == Maximum;

        public StatSpan(string key, string displayName, int minimum, int maximum)
        {
            Key = key;
            DisplayName = displayName;
            Minimum = minimum;
            Maximum = maximum;
        }

        public override string ToString() =>
            IsFixed 
                ? $"{Key}={Minimum}" 
                : $"{Key}={Minimum}~{Maximum}";
    }
}