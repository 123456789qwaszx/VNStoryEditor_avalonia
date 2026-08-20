using System;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// Unity의 RectTransform 계층 좌표 변환을 순수 C# Core로 포팅한 것.
    /// </summary>
    public static class RectChainMath
    {
        private const float DegToRad = (float)(Math.PI / 180.0);

        /// <summary>
        /// 노드의 rect 크기. 스트레치 앵커면 부모 크기를 따라가고 sizeDelta는 증감,
        /// 고정 앵커(min == max)면 sizeDelta가 크기 그 자체가 된다.
        /// </summary>
        public static Vec2 RectSize(Vec2 parentSize, in RectNodeState node)
        {
            return new(
                (node.AnchorMax.X - node.AnchorMin.X) * parentSize.X + node.SizeDelta.X,
                (node.AnchorMax.Y - node.AnchorMin.Y) * parentSize.Y + node.SizeDelta.Y);
        }

        /// <summary>
        /// 노드 pivot의 부모 로컬 좌표(= 유니티 localPosition의 xy).
        /// </summary>
        public static Vec2 LocalPosition(Vec2 parentSize, Vec2 parentPivot, in RectNodeState node)
        {
            // 부모 rect의 최소 모서리 (부모 pivot 기준 로컬).
            float rectMinX = -parentPivot.X * parentSize.X;
            float rectMinY = -parentPivot.Y * parentSize.Y;

            // 앵커 사각형.
            float anchorMinX = rectMinX + node.AnchorMin.X * parentSize.X;
            float anchorMinY = rectMinY + node.AnchorMin.Y * parentSize.Y;
            float anchorMaxX = rectMinX + node.AnchorMax.X * parentSize.X;
            float anchorMaxY = rectMinY + node.AnchorMax.Y * parentSize.Y;

            // 앵커 기준점: 앵커 사각형을 자기 pivot 비율로 보간.
            float refX = anchorMinX + (anchorMaxX - anchorMinX) * node.Pivot.X;
            float refY = anchorMinY + (anchorMaxY - anchorMinY) * node.Pivot.Y;

            return new(refX + node.AnchoredPosition.X, refY + node.AnchoredPosition.Y);
        }

        /// <summary>
        /// 계층 내 마지막 노드의 로컬의 특정 점 -> rootSpace 로컬("월드") 점.
        /// (= 유니티 rect.TransformPoint)
        /// </summary>
        public static Vec3 TransformPoint(
            ReadOnlySpan<RectNodeState> chain,
            RectSpace rootSpace,
            Vec3 localPoint)
        {
            if (chain.IsEmpty)
                return localPoint;

            // 1. (자식의 앵커 계산에 부모의 rect 크기·pivot이 필요하기에)
            // 루트 -> 끝으로 내려가며 각 노드의 localPosition 생성.
            Span<Vec2> localPositions = chain.Length <= 64
                ? stackalloc Vec2[chain.Length]
                : new Vec2[chain.Length];

            Vec2 parentSize = rootSpace.Size;
            Vec2 parentPivot = rootSpace.Pivot;

            for (int i = 0; i < chain.Length; i++)
            {
                localPositions[i] = LocalPosition(parentSize, parentPivot, in chain[i]);
                parentSize = RectSize(parentSize, in chain[i]);
                parentPivot = chain[i].Pivot;
            }

            // 2. 끝 -> 루트로 올라가며 로컬 변환을 적용한다.
            // p' = t + R(euler) · (scale ⊙ p)
            // (점 p에 축별 스케일을 적용하고, 회전시킨 다음, 위치 t만큼 이동)
            Vec3 p = localPoint;

            for (int i = chain.Length - 1; i >= 0; i--)
            {
                p = Vec3.Scale(chain[i].LocalScale, p);
                p = RotateByEuler(chain[i].LocalEulerAngles, p);
                p = new(localPositions[i].X + p.X, localPositions[i].Y + p.Y, p.Z);
            }

            return p;
        }

        /// <summary>
        /// rootSpace 로컬("월드") 점 → 체인 맨 끝 노드의 로컬 점.
        /// (= 유니티 rect.InverseTransformPoint)
        /// </summary>
        public static Vec3 InverseTransformPoint(
            ReadOnlySpan<RectNodeState> chain,
            RectSpace rootSpace,
            Vec3 worldPoint)
        {
            // 부모 크기·pivot을 내려가며 갱신하는 방향과 역변환 적용 방향이 같아서
            // 정변환과 달리 중간 저장이 필요 없음.
            // p' = (1/scale) ⊙ R(euler)ᵀ · (p - t)
            // (위치 이동을 먼저 빼고, 회전을 역으로 돌린 다음, 스케일로 나눠서 원래 로컬 좌표를 복원)
            Vec2 parentSize = rootSpace.Size;
            Vec2 parentPivot = rootSpace.Pivot;

            Vec3 p = worldPoint;

            for (int i = 0; i < chain.Length; i++)
            {
                Vec2 t = LocalPosition(parentSize, parentPivot, in chain[i]);

                p = new(p.X - t.X, p.Y - t.Y, p.Z);
                p = RotateByEulerInverse(chain[i].LocalEulerAngles, p);
                p = InverseScale(chain[i].LocalScale, p);

                parentSize = RectSize(parentSize, in chain[i]);
                parentPivot = chain[i].Pivot;
            }

            return p;
        }

        /// <summary>Unity Quaternion.Euler와 같은 적용 순서: Z, X, Y.</summary>
        internal static Vec3 RotateByEuler(Vec3 eulerDegrees, Vec3 v)
        {
            if (eulerDegrees == Vec3.Zero)
                return v;

            if (eulerDegrees.Z != 0f)
            {
                float s = (float)Math.Sin(eulerDegrees.Z * DegToRad);
                float c = (float)Math.Cos(eulerDegrees.Z * DegToRad);
                v = new(c * v.X - s * v.Y, s * v.X + c * v.Y, v.Z);
            }

            if (eulerDegrees.X != 0f)
            {
                float s = (float)Math.Sin(eulerDegrees.X * DegToRad);
                float c = (float)Math.Cos(eulerDegrees.X * DegToRad);
                v = new(v.X, c * v.Y - s * v.Z, s * v.Y + c * v.Z);
            }

            if (eulerDegrees.Y != 0f)
            {
                float s = (float)Math.Sin(eulerDegrees.Y * DegToRad);
                float c = (float)Math.Cos(eulerDegrees.Y * DegToRad);
                v = new(c * v.X + s * v.Z, v.Y, -s * v.X + c * v.Z);
            }

            return v;
        }

        /// <summary>RotateByEuler의 역: Y, X, Z 순으로 음의 각을 적용.</summary>
        internal static Vec3 RotateByEulerInverse(Vec3 eulerDegrees, Vec3 v)
        {
            if (eulerDegrees == Vec3.Zero)
                return v;

            return RotateByEuler(
                new Vec3(0f, 0f, -eulerDegrees.Z),
                RotateByEuler(
                    new Vec3(-eulerDegrees.X, 0f, 0f),
                    RotateByEuler(new Vec3(0f, -eulerDegrees.Y, 0f), v)));
        }

        // 스케일 0은 방어하지 않는다. 조용히 0을 돌려주면 잘못된 좌표가 그대로 흘러감.
        // 무한대가 나오도록 유도.
        private static Vec3 InverseScale(Vec3 scale, Vec3 v)
            => new(v.X / scale.X, v.Y / scale.Y, v.Z / scale.Z);
    }
}