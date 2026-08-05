using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// set_anchor / show — 캐릭터를 역할 기본 자리로 세우고 연기 축들을 초기화한다.
    /// (SetAnchorCommandCharR의 "스펙 → 목표 상태" 변환부)
    ///
    /// 리셋 대상 노드 목록은 리그 스키마 지식이므로 코드에 박지 않고 인자로 받는다 —
    /// 호스트는 refs에서, 리듀서는 tuning의 리그 스키마에서 만든다.
    /// </summary>
    public static class SetAnchorReduction
    {
        /// <summary>role-anchor 튜닝 값 (RoleAnchorTuningDBSO 엔트리의 데이터 부분).</summary>
        public readonly struct RoleAnchorTuning
        {
            public readonly Vec2 Offset;
            public readonly float VisualScale;

            public RoleAnchorTuning(Vec2 offset, float visualScale)
            {
                Offset = offset;
                VisualScale = visualScale;
            }

            /// <summary>엔트리가 없는 캐릭터의 기본값: 오프셋 0, 배율 1.</summary>
            public static readonly RoleAnchorTuning Default = new RoleAnchorTuning(Vec2.Zero, 1f);
        }

        /// <summary>종전 규약: visualScale 하한 (Mathf.Max(0.0001f, v)).</summary>
        public const float MinVisualScale = 0.0001f;

        /// <summary>
        /// 클레임 순서: 위치 리셋들 → 회전 리셋들 → 스케일 리셋들 → 앵커 위치 → 앵커 스케일.
        /// 호스트 어댑터는 같은 순서로 rect 목록을 만들어 zip으로 적용한다.
        /// </summary>
        public static StageNodeClaim[] Reduce(
            string anchorNodeKey,
            in RoleAnchorTuning tuning,
            IReadOnlyList<string> resetPositionKeys,
            IReadOnlyList<string> resetEulerKeys,
            IReadOnlyList<string> resetScaleKeys)
        {
            if (string.IsNullOrEmpty(anchorNodeKey))
                throw new ArgumentException("앵커 노드 키가 비어 있다.", nameof(anchorNodeKey));

            resetPositionKeys ??= Array.Empty<string>();
            resetEulerKeys ??= Array.Empty<string>();
            resetScaleKeys ??= Array.Empty<string>();

            float visualScale = Math.Max(MinVisualScale, tuning.VisualScale);

            StageNodeClaim[] claims = new StageNodeClaim[
                resetPositionKeys.Count + resetEulerKeys.Count + resetScaleKeys.Count + 2];

            int i = 0;

            for (int k = 0; k < resetPositionKeys.Count; k++)
                claims[i++] = StageNodeClaim.AnchoredPosition(resetPositionKeys[k], Vec2.Zero);

            for (int k = 0; k < resetEulerKeys.Count; k++)
                claims[i++] = StageNodeClaim.LocalEuler(resetEulerKeys[k], Vec3.Zero);

            for (int k = 0; k < resetScaleKeys.Count; k++)
                claims[i++] = StageNodeClaim.LocalScaleXY(resetScaleKeys[k], Vec2.One);

            claims[i++] = StageNodeClaim.AnchoredPosition(anchorNodeKey, tuning.Offset);
            claims[i] = StageNodeClaim.LocalScaleXY(anchorNodeKey, new Vec2(visualScale, visualScale));

            return claims;
        }
    }

    /// <summary>fade_in — 가시성 축을 1로. 트윈은 호스트의 일이고, 정지 프레임엔 목표값뿐이다.</summary>
    public static class FadeInReduction
    {
        public const float TargetAlpha = 1f;

        public static StageNodeClaim Reduce(string nodeKey)
            => StageNodeClaim.CanvasAlpha(nodeKey, TargetAlpha);
    }

    /// <summary>fade_out — 가시성 축을 0으로.</summary>
    public static class FadeOutReduction
    {
        public const float TargetAlpha = 0f;

        public static StageNodeClaim Reduce(string nodeKey)
            => StageNodeClaim.CanvasAlpha(nodeKey, TargetAlpha);
    }
}
