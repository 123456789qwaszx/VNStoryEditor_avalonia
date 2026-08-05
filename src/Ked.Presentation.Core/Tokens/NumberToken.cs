using System.Globalization;

namespace Ked.Presentation.Core
{
    public static class NumberToken
    {
        public static bool TryParseFloat(string value, out float result)
            => float.TryParse(
                (value ?? "").Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result);
    }
}