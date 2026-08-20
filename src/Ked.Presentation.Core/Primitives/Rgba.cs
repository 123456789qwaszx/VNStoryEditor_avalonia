using System;
using System.Globalization;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// UnityEngine.Color 대응 불변 값 타입.
    /// </summary>
    public readonly struct Rgba : IEquatable<Rgba>
    {
        public readonly float R;
        public readonly float G;
        public readonly float B;
        public readonly float A;

        public Rgba(float r, float g, float b, float a = 1f)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public static readonly Rgba White = new(1f, 1f, 1f, 1f);
        public static readonly Rgba Black = new(0f, 0f, 0f, 1f);
        public static readonly Rgba Clear = new(0f, 0f, 0f, 0f);

        public Rgba WithAlpha(float a) => new(R, G, B, a);

        public bool Equals(Rgba other)
            => R.Equals(other.R) && G.Equals(other.G) && B.Equals(other.B) && A.Equals(other.A);

        public override bool Equals(object obj) => obj is Rgba other && Equals(other);

        public override int GetHashCode()
        {
            int hash = R.GetHashCode();
            hash = (hash * 397) ^ G.GetHashCode();
            hash = (hash * 397) ^ B.GetHashCode();
            hash = (hash * 397) ^ A.GetHashCode();

            return hash;
        }

        public static bool operator ==(Rgba a, Rgba b) => a.Equals(b);
        public static bool operator !=(Rgba a, Rgba b) => !a.Equals(b);

        public override string ToString()
            => string.Format(CultureInfo.InvariantCulture, "rgba({0}, {1}, {2}, {3})", R, G, B, A);
    }
}