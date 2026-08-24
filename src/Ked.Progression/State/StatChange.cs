namespace Ked.Progression
{
    public enum StatChangeKind
    {
        Add = 0,
        Set = 1, // Stat.bool에만 사용.
    }

    public readonly struct StatChange
    {
        public string Key { get; }
        public int Amount { get; }
        public StatChangeKind Kind { get; }

        private StatChange(string key, int amount, StatChangeKind kind)
        {
            Key = key;
            Amount = amount;
            Kind = kind;
        }

        public static StatChange Add(string key, int amount) => new(key, amount, StatChangeKind.Add);
        public static StatChange Set(string key, int value) => new(key, value, StatChangeKind.Set);

        public int ApplyTo(int current) =>
            Kind == StatChangeKind.Set 
                ? Amount 
                : current + Amount;

        public override string ToString() =>
            Kind == StatChangeKind.Set
                ? $"{Key} = {Amount}"
                : $"{Key} {(Amount >= 0 ? "+" : "")}{Amount}";
    }
}