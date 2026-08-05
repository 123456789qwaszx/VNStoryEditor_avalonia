using System;

namespace Ked.Presentation.Core
{
    /// <summary>목표 상태 변경분이 노드의 어느 성분을 겨누는가.</summary>
    public enum StageNodeClaimKind
    {
        AnchoredPosition,
        LocalScaleXY,   // z는 라이브 값을 보존한다 (Ledger와 같은 규약)
        LocalEulerAngles,

        /// <summary>
        /// CanvasGroup alpha (가시성 축). RectNodeState에 살지 않는다 —
        /// StageState의 alpha 저장소에 접히고, 호스트는 CanvasGroup에 쓴다.
        /// ApplyTo(RectNodeState)는 이 종류를 받으면 예외를 낸다.
        /// </summary>
        CanvasAlpha,
    }

    /// <summary>
    /// "스펙 → 목표 상태" 변환의 출력 단위 — 커맨드 하나가 노드 하나에 거는 목표값.
    ///
    /// U13-b-4의 타입 경계다. 각 커맨드의 ClaimTarget이 계산하던 목표값이
    /// 이 타입으로 표준화되고, 호스트는 이것을 받아
    ///   - PlacementTargetLedger에 게시하고 (정착 상태 예약)
    ///   - 트윈의 종점으로 쓰고 (트윈 자체는 호스트의 일)
    ///   - Commit에서 실제 트랜스폼에 쓴다.
    /// 코어(리듀서)는 이것을 RectNodeTree에 적용해 정지 프레임 상태를 접는다.
    /// </summary>
    public readonly struct StageNodeClaim
    {
        public readonly string NodeKey;
        public readonly StageNodeClaimKind Kind;
        public readonly Vec3 Value;

        private StageNodeClaim(string nodeKey, StageNodeClaimKind kind, Vec3 value)
        {
            if (string.IsNullOrEmpty(nodeKey))
                throw new ArgumentException("클레임의 노드 키가 비어 있다.", nameof(nodeKey));

            NodeKey = nodeKey;
            Kind = kind;
            Value = value;
        }

        public static StageNodeClaim AnchoredPosition(string nodeKey, Vec2 value)
            => new StageNodeClaim(nodeKey, StageNodeClaimKind.AnchoredPosition, new Vec3(value, 0f));

        public static StageNodeClaim LocalScaleXY(string nodeKey, Vec2 value)
            => new StageNodeClaim(nodeKey, StageNodeClaimKind.LocalScaleXY, new Vec3(value, 0f));

        public static StageNodeClaim LocalEuler(string nodeKey, Vec3 value)
            => new StageNodeClaim(nodeKey, StageNodeClaimKind.LocalEulerAngles, value);

        public static StageNodeClaim CanvasAlpha(string nodeKey, float alpha)
            => new StageNodeClaim(nodeKey, StageNodeClaimKind.CanvasAlpha, new Vec3(alpha, 0f, 0f));

        /// <summary>클레임을 상태 값에 적용한다. 스케일 z 보존 규약 포함.</summary>
        public RectNodeState ApplyTo(in RectNodeState state)
        {
            switch (Kind)
            {
                case StageNodeClaimKind.AnchoredPosition:
                    return state.WithAnchoredPosition(Value.XY);

                case StageNodeClaimKind.LocalScaleXY:
                    return state.WithLocalScale(new Vec3(Value.X, Value.Y, state.LocalScale.Z));

                case StageNodeClaimKind.LocalEulerAngles:
                    return state.WithLocalEuler(Value);

                case StageNodeClaimKind.CanvasAlpha:
                    throw new InvalidOperationException(
                        $"CanvasAlpha 클레임('{NodeKey}')은 RectNodeState에 적용할 수 없다 — " +
                        "alpha는 StageState의 가시성 축에 접힌다.");

                default:
                    throw new InvalidOperationException($"모르는 클레임 종류: {Kind}");
            }
        }

        /// <summary>트리의 해당 노드에 적용한다. 노드가 없으면 트리가 예외를 낸다(침묵 금지).</summary>
        public void ApplyTo(RectNodeTree tree)
        {
            if (tree == null)
                throw new ArgumentNullException(nameof(tree));

            tree.SetState(NodeKey, ApplyTo(tree.GetState(NodeKey)));
        }
    }
}
