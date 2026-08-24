using System;

namespace Ked.Progression
{
    public enum StatType
    {
        Number = 0,
        Bool = 1,
    }

    public sealed class StatDefinition
    {
        public string Key { get; }
        public string DisplayName { get; }
        public StatType Type { get; }

        public int Initial { get; }
        public int Minimum { get; }
        public int Maximum { get; }

        // The definition cannot be created unless all invariants are satisfied.
        // This also protects against constructing a definition without going through the loader.
        public StatDefinition(
            string key,
            string displayName,
            StatType type,
            int initial,
            int minimum,
            int maximum)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException(
                    "The stat key cannot be empty.",
                    nameof(key));

            if (minimum > maximum)
                throw new ArgumentException(
                    $"The bounds for stat '{key}' are reversed: minimum {minimum} > maximum {maximum}.",
                    nameof(minimum));

            if (type == StatType.Bool && (minimum != 0 || maximum != 1))
                throw new ArgumentException(
                    $"The bounds for bool stat '{key}' must be 0..1 (§G4). " +
                    $"Received: {minimum}..{maximum}.",
                    nameof(type));

            if (initial < minimum || initial > maximum)
                throw new ArgumentException(
                    $"The initial value {initial} of stat '{key}' is outside the bounds " +
                    $"{minimum}..{maximum}. Clamping it silently would cause the stat to start " +
                    "with a different value than the one authored.",
                    nameof(initial));

            Key = key;
            DisplayName = displayName;
            Type = type;
            Initial = initial;
            Minimum = minimum;
            Maximum = maximum;
        }

        public int Clamp(int value)
        {
            if (value < Minimum)
                return Minimum;

            return value > Maximum ? Maximum : value;
        }
    }
}