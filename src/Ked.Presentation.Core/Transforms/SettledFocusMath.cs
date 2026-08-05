using System;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 정착 체인 위의 focus 지점 수학 (U13-b-5 depth 묶음).
    ///
    /// 종전에는 이 계산이 유니티를 거쳐야 했다:
    /// - CharacterDepthResolver.TryMeasureFocusWithTemporaryDepthTransform이
    ///   목표 depth를 실제 트랜스폼에 잠깐 써서 측정하고 되돌렸다(마지막 남은
    ///   "적용→측정→복원" 트릭 — 예외가 나면 리그가 더러워졌다).
    /// - CharacterFocusPlacementSolver가 TransformPoint/InverseTransformPoint로
    ///   부모 공간 변환을 했다.
    /// 이제 정착 체인(RectNodeState[])만 있으면 전부 순수 계산이다.
    ///
    /// 체인 규약은 RectChainMath와 같다: 루트(rigSpaceRoot의 자식)부터,
    /// 맨 끝이 focus 측정 노드(CharacterPortrait_VisualOffset 상당).
    /// "rig space" = rootSpace 로컬.
    /// </summary>
    public static class SettledFocusMath
    {
        /// <summary>정착 체인에서 focus 지점의 rig-space 좌표.</summary>
        public static Vec2 FocusPointInRigSpace(
            ReadOnlySpan<RectNodeState> settledChain,
            RectSpace space,
            Vec2 focusLocalOffset)
        {
            return RectChainMath.TransformPoint(
                settledChain, space, new Vec3(focusLocalOffset, 0f)).XY;
        }

        /// <summary>
        /// place — 현재 focus 지점을 원하는 rig-space 지점으로 보내는
        /// 이동 노드의 목표 anchoredPosition.
        ///
        /// 원리: 원하는 지점과 현재 지점을 이동 노드의 "부모" 공간으로 내려 델타를
        /// 얻는다. 두 점이 같은 변환을 타므로 부모 사슬의 평행이동은 상쇄되고
        /// 스케일·회전만 남는다 — 종전 ConvertPointFrom...ParentSpace 두 번과 같은 값.
        /// </summary>
        public static Vec2 SolveFocusPlacement(
            ReadOnlySpan<RectNodeState> settledChain,
            RectSpace space,
            int moveNodeIndex,
            Vec2 focusLocalOffset,
            Vec2 desiredFocusInRigSpace,
            Vec2 currentMoveAnchoredPosition)
        {
            RequireIndex(settledChain.Length, moveNodeIndex, nameof(moveNodeIndex));

            Vec2 currentFocusInRigSpace =
                FocusPointInRigSpace(settledChain, space, focusLocalOffset);

            // 이동 노드의 부모 공간 = 체인에서 이동 노드 앞까지의 역변환.
            ReadOnlySpan<RectNodeState> parentChain = settledChain.Slice(0, moveNodeIndex);

            Vec3 currentInParent = RectChainMath.InverseTransformPoint(
                parentChain, space, new Vec3(currentFocusInRigSpace, 0f));

            Vec3 desiredInParent = RectChainMath.InverseTransformPoint(
                parentChain, space, new Vec3(desiredFocusInRigSpace, 0f));

            Vec2 deltaInParent = desiredInParent.XY - currentInParent.XY;

            return currentMoveAnchoredPosition + deltaInParent;
        }

        /// <summary>
        /// set_depth — depth를 목표값으로 바꿔도 focus 지점이 제자리에 남도록
        /// 보정한 depthY.
        ///
        /// 종전과 같은 원리: (현재 정착 focus) − (목표 depth를 입힌 focus) 만큼을
        /// depthY 부모 공간의 벡터로 바꿔 rawDepthY에 더한다. 목표 depth 적용은
        /// 실제 트랜스폼이 아니라 체인 사본에 한다 — 복원이 없고 예외에 안전하다.
        /// </summary>
        public static Vec2 SolveDepthYPreservingFocus(
            ReadOnlySpan<RectNodeState> settledChain,
            RectSpace space,
            int depthYIndex,
            int depthScaleIndex,
            Vec2 focusLocalOffset,
            Vec2 rawDepthY,
            Vec2 targetDepthScale)
        {
            RequireIndex(settledChain.Length, depthYIndex, nameof(depthYIndex));
            RequireIndex(settledChain.Length, depthScaleIndex, nameof(depthScaleIndex));

            Vec2 currentFocus = FocusPointInRigSpace(settledChain, space, focusLocalOffset);

            // 목표 depth가 적용된 체인 사본.
            RectNodeState[] targetChain = settledChain.ToArray();

            targetChain[depthYIndex] =
                targetChain[depthYIndex].WithAnchoredPosition(rawDepthY);

            RectNodeState scaleNode = targetChain[depthScaleIndex];
            targetChain[depthScaleIndex] = scaleNode.WithLocalScale(
                new Vec3(targetDepthScale.X, targetDepthScale.Y, scaleNode.LocalScale.Z));

            Vec2 targetFocus = FocusPointInRigSpace(targetChain, space, focusLocalOffset);

            Vec2 compensationInRigSpace = currentFocus - targetFocus;

            // rig-space 벡터 → depthY 부모 공간 벡터.
            // 벡터라 평행이동이 빠진다: 원점과 끝점을 같은 역변환에 태워 뺀다
            // (종전 ConvertVectorFromRigSpaceToTargetPositionParentSpace와 같은 원리).
            ReadOnlySpan<RectNodeState> parentChain = settledChain.Slice(0, depthYIndex);

            Vec3 originInParent = RectChainMath.InverseTransformPoint(
                parentChain, space, Vec3.Zero);

            Vec3 tipInParent = RectChainMath.InverseTransformPoint(
                parentChain, space, new Vec3(compensationInRigSpace, 0f));

            Vec2 compensationInParent = tipInParent.XY - originInParent.XY;

            return rawDepthY + compensationInParent;
        }

        private static void RequireIndex(int chainLength, int index, string name)
        {
            if (index < 0 || index >= chainLength)
            {
                throw new ArgumentOutOfRangeException(
                    name, index,
                    $"체인 길이 {chainLength} 밖의 인덱스다. 노드가 측정 체인의 조상이 아니다.");
            }
        }
    }
}
