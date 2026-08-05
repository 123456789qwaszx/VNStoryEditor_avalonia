using System;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// RectTransform 부모 사슬 좌표 계산의 순수 구현.
    /// Unity의 rect.TransformPoint / InverseTransformPoint와 같은 결과를,
    /// 유니티 없이 RectNodeState 체인에서 낸다.
    ///
    /// 이것이 U13-b의 바닥이다: CharacterPlacementTargetLedger가
    /// "부모들을 target 값으로 잠깐 세팅 → 측정 → 복원"으로 하던 일을,
    /// b-3에서 이 수학으로 직접 계산하게 바꾼다.
    ///
    /// 규약:
    /// - 체인은 루트에 가까운 쪽부터. chain[0]의 부모가 rootSpace다.
    /// - "월드" = rootSpace 소유자의 로컬 좌표 (Ledger의 stopRoot 공간).
    /// - 회전은 Unity Quaternion.Euler와 같은 순서: Z → X → Y.
    /// - 스케일 0인 노드의 InverseTransformPoint는 정의하지 않는다(유니티도 특이 행렬이다).
    ///
    /// 검증: 실제 RectTransform과의 대조는 Tests/EditMode/UnityParity 하네스가 한다.
    /// 허용 오차 정책은 Documentation~/transform-math-and-epsilon.md.
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
            return new Vec2(
                (node.AnchorMax.X - node.AnchorMin.X) * parentSize.X + node.SizeDelta.X,
                (node.AnchorMax.Y - node.AnchorMin.Y) * parentSize.Y + node.SizeDelta.Y);
        }

        /// <summary>
        /// 노드 pivot의 부모 로컬 좌표(= 유니티 localPosition의 xy).
        /// anchoredPosition은 "앵커 기준점에서 pivot까지"이고,
        /// 앵커 기준점은 앵커 사각형 안에서 pivot 비율로 보간한 점이다.
        /// </summary>
        public static Vec2 LocalPosition(Vec2 parentSize, Vec2 parentPivot, in RectNodeState node)
        {
            // 부모 rect의 최소 모서리(부모 pivot 기준 로컬).
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

            return new Vec2(refX + node.AnchoredPosition.X, refY + node.AnchoredPosition.Y);
        }

        /// <summary>
        /// 체인 맨 끝 노드의 로컬 점 → rootSpace 로컬("월드") 점.
        /// rect.TransformPoint 대응. 빈 체인이면 점을 그대로 돌려준다.
        /// </summary>
        public static Vec3 TransformPoint(
            ReadOnlySpan<RectNodeState> chain,
            RectSpace rootSpace,
            Vec3 localPoint)
        {
            if (chain.IsEmpty)
                return localPoint;

            // 1) 루트→끝으로 내려가며 각 노드의 localPosition을 만든다
            //    (자식의 앵커 계산에 부모 rect 크기·pivot이 필요하다).
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

            // 2) 끝→루트로 올라가며 로컬 변환을 적용한다: p' = t + R(euler) · (scale ⊙ p).
            Vec3 p = localPoint;

            for (int i = chain.Length - 1; i >= 0; i--)
            {
                p = Vec3.Scale(chain[i].LocalScale, p);
                p = RotateByEuler(chain[i].LocalEulerAngles, p);
                p = new Vec3(
                    localPositions[i].X + p.X,
                    localPositions[i].Y + p.Y,
                    p.Z);
            }

            return p;
        }

        /// <summary>
        /// rootSpace 로컬("월드") 점 → 체인 맨 끝 노드의 로컬 점.
        /// rect.InverseTransformPoint 대응.
        /// </summary>
        public static Vec3 InverseTransformPoint(
            ReadOnlySpan<RectNodeState> chain,
            RectSpace rootSpace,
            Vec3 worldPoint)
        {
            Vec2 parentSize = rootSpace.Size;
            Vec2 parentPivot = rootSpace.Pivot;

            // 루트→끝으로 내려가며 역변환을 차례로 적용한다:
            // p' = (1/scale) ⊙ R(euler)ᵀ · (p - t). 저장할 것이 없어 한 번에 간다.
            Vec3 p = worldPoint;

            for (int i = 0; i < chain.Length; i++)
            {
                Vec2 t = LocalPosition(parentSize, parentPivot, in chain[i]);

                p = new Vec3(p.X - t.X, p.Y - t.Y, p.Z);
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
                v = new Vec3(c * v.X - s * v.Y, s * v.X + c * v.Y, v.Z);
            }

            if (eulerDegrees.X != 0f)
            {
                float s = (float)Math.Sin(eulerDegrees.X * DegToRad);
                float c = (float)Math.Cos(eulerDegrees.X * DegToRad);
                v = new Vec3(v.X, c * v.Y - s * v.Z, s * v.Y + c * v.Z);
            }

            if (eulerDegrees.Y != 0f)
            {
                float s = (float)Math.Sin(eulerDegrees.Y * DegToRad);
                float c = (float)Math.Cos(eulerDegrees.Y * DegToRad);
                v = new Vec3(c * v.X + s * v.Z, v.Y, -s * v.X + c * v.Z);
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

        private static Vec3 InverseScale(Vec3 scale, Vec3 v)
            => new Vec3(v.X / scale.X, v.Y / scale.Y, v.Z / scale.Z);
    }
}
