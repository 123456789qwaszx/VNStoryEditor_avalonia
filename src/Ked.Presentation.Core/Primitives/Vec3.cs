using System;
using System.Globalization;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// UnityEngine.Vector3 대응 불변 값 타입.
    /// </summary>
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        // 2D 값을 z=0 평면으로 올림. 좌표 계산의 입출력용으로 변환한 것.
        public Vec3(Vec2 xy, float z = 0f)
        {
            X = xy.X;
            Y = xy.Y;
            Z = z;
        }

        public static readonly Vec3 Zero = new(0f, 0f, 0f);
        public static readonly Vec3 One = new(1f, 1f, 1f);

        public Vec2 XY => new(X, Y);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, float s) => new(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(float s, Vec3 a) => a * s;

        /// <summary>
        /// UnityEngine.Vector3.Scale 대응.
        /// </summary>
        public static Vec3 Scale(Vec3 a, Vec3 b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);

        public bool Equals(Vec3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

        public override bool Equals(object obj) => obj is Vec3 other && Equals(other);

        public override int GetHashCode()
        {
            int hash = X.GetHashCode();
            hash = (hash * 397) ^ Y.GetHashCode();
            hash = (hash * 397) ^ Z.GetHashCode();

            return hash;
        }

        public static bool operator ==(Vec3 a, Vec3 b) => a.Equals(b);
        public static bool operator !=(Vec3 a, Vec3 b) => !a.Equals(b);

        public override string ToString()
            => string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2})", X, Y, Z);
    }
}