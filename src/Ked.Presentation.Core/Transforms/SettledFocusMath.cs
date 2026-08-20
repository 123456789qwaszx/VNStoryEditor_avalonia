using System;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 정착 체인 위의 focus 지점 계산.
    /// </summary>
    public static class SettledFocusMath
    {
        // 지금 정착 상태에서 focus가 rig space 어디에 있는지 체크.
        // (focus의 최종 실제 좌표)
        public static Vec2 FocusPointInRigSpace(
            ReadOnlySpan<RectNodeState> settledChain,
            RectSpace space,
            Vec2 focusLocalOffset)
        {
            return RectChainMath.TransformPoint(
                settledChain, space, new Vec3(focusLocalOffset, 0f)).XY;
        }

        /// <summary>
        /// focus를 원하는 곳으로 보내려면,
        /// 이동 노드의 Position을 얼마로 해야 하는지 계산.
        /// (원하는 focus 위치 -> 이동 노드가 얼마만큼 움직여야 하는가)
        ///
        /// 현재 focus와 원하는 focus를 둘 다 이동 노드의 부모 좌표계로 바꾼 뒤,
        /// 그 차이만큼 이동 노드의 anchoredPosition을 조정한다.
        ///
        /// 원리: 원하는 지점과 현재 지점을 이동 노드의 "부모" 공간으로 내려 델타를 얻는다.
        /// 두 점이 같은 변환을 타므로 부모 사슬의 평행이동은 상쇄되고 스케일·회전만 남는다.
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

            // 이동시킬 노드의 부모까지의 체인만 역변환해서 잘라냄.
            // anchoredPosition은 해당 노드의 부모 좌표계에서 표현되는 위치이기에, 부모값이 필요.
            ReadOnlySpan<RectNodeState> parentChain = settledChain.Slice(0, moveNodeIndex);

            Vec3 currentInParent = RectChainMath.InverseTransformPoint(
                parentChain, space, new Vec3(currentFocusInRigSpace, 0f));

            Vec3 desiredInParent = RectChainMath.InverseTransformPoint(
                parentChain, space, new Vec3(desiredFocusInRigSpace, 0f));

            Vec2 deltaInParent = desiredInParent.XY - currentInParent.XY;

            return currentMoveAnchoredPosition + deltaInParent;
        }

        /// <summary>
        /// depth/scale을 바꿔도 focus가 안 움직이게 하려면,
        /// depthY를 얼마나 보정해야 하는지 계산.
        ///
        /// 원리: (현재 정착 focus) − (목표 depth를 입힌 focus) 만큼을
        /// depthY 부모 공간의 벡터로 바꿔 rawDepthY에 더한다.
        /// </summary>
        public static Vec2 SolveDepthYPreservingFocus(
            ReadOnlySpan<RectNodeState> settledChain,
            RectSpace space,
            int depthYIndex, // 위치 보정 담당 노드
            int depthScaleIndex, // depth에 따라 scale이 바뀌는 노드
            Vec2 focusLocalOffset,
            Vec2 rawDepthY, // 보정 전 원래 적용하려던 depthY
            Vec2 targetDepthScale // 새로 적용할 depth scale
            )
        {
            RequireIndex(settledChain.Length, depthYIndex, nameof(depthYIndex));
            RequireIndex(settledChain.Length, depthScaleIndex, nameof(depthScaleIndex));

            // depth를 변경하기 전, 현재 정상적인 focus 위치
            Vec2 currentFocus = FocusPointInRigSpace(settledChain, space, focusLocalOffset);

            // 체인에 가상의 목표 depth 상태를 적용할 것이기에, 사본 복사.
            RectNodeState[] targetChain = settledChain.ToArray();

            // depthY가 일단 rawDepthY가 되었다고 가정
            targetChain[depthYIndex] =
                targetChain[depthYIndex].WithAnchoredPosition(rawDepthY);
            
            // 목표 scale도 가상 적용
            RectNodeState scaleNode = targetChain[depthScaleIndex];
            targetChain[depthScaleIndex] = scaleNode.WithLocalScale(
                new Vec3(targetDepthScale.X, targetDepthScale.Y, scaleNode.LocalScale.Z));

            // 가상의 새 focus 위치를 계산
            Vec2 targetFocus = FocusPointInRigSpace(targetChain, space, focusLocalOffset);

            // 원래 focus 자리로 돌려보내기 위한, 반대로 보정량 계산.
            Vec2 compensationInRigSpace = currentFocus - targetFocus;

            // rig-space 벡터 -> depthY 부모 공간 벡터.
            // 벡터라 평행이동이 빠져야 한다: 원점과 끝점을 같은 역변환에 태워 뺀다
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