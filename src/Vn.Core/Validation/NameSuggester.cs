namespace Vn.Core.Validation;

internal static class NameSuggester
{
    public static string? FindClosest(
        string unknown,
        IEnumerable<string> candidates)
    {
        return candidates
            .Distinct(StringComparer.Ordinal)
            .Select(candidate => new
            {
                Candidate = candidate,
                Distance = LevenshteinDistance(
                    unknown,
                    candidate)
            })
            .Where(item =>
                item.Distance <= GetThreshold(unknown.Length))
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.Candidate, StringComparer.Ordinal)
            .Select(item => item.Candidate)
            .FirstOrDefault();
    }

    private static int GetThreshold(int length)
    {
        return length switch
        {
            <= 4 => 1,
            <= 8 => 2,
            _ => 3
        };
    }

    private static int LevenshteinDistance(
        string left,
        string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (int column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (int row = 1; row <= left.Length; row++)
        {
            current[0] = row;

            for (int column = 1; column <= right.Length; column++)
            {
                int cost = left[row - 1] == right[column - 1]
                    ? 0
                    : 1;

                current[column] = Math.Min(
                    Math.Min(
                        current[column - 1] + 1,
                        previous[column] + 1),
                    previous[column - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
