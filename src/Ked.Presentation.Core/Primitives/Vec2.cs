using System;
using System.Globalization;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// UnityEngine.Vector2 대응 불변 값 타입. 코어는 UnityEngine을 참조하지 않는다.
    /// 호스트(유니티 / VnTool)가 자기 타입으로 변환해서 쓴다.
    /// </summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly float X;
        public readonly float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static readonly Vec2 Zero = new Vec2(0f, 0f);
        public static readonly Vec2 One = new Vec2(1f, 1f);

        /// <summary>가운데 pivot(0.5, 0.5)에 자주 쓰는 값.</summary>
        public static readonly Vec2 Half = new Vec2(0.5f, 0.5f);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator *(float s, Vec2 a) => a * s;

        /// <summary>성분별 곱. Unity Vector2.Scale 대응.</summary>
        public static Vec2 Scale(Vec2 a, Vec2 b) => new Vec2(a.X * b.X, a.Y * b.Y);

        public bool Equals(Vec2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Vec2 other && Equals(other);
        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();
        public static bool operator ==(Vec2 a, Vec2 b) => a.Equals(b);
        public static bool operator !=(Vec2 a, Vec2 b) => !a.Equals(b);

        public override string ToString()
            => string.Format(CultureInfo.InvariantCulture, "({0}, {1})", X, Y);
    }
}
