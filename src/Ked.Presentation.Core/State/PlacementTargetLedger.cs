using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// "트윈이 다 끝났다면 어디에 있을 것인가"의 예약 장부 — StageState의 원형.
    ///
    /// 런타임의 CharacterPlacementTargetLedger에서 승격했다(U13-b-3). 바뀐 것 둘:
    /// - 키가 RectTransform → 문자열 논리 식별자.
    /// - "적용→측정→복원" 트릭 대신, 라이브 상태에 target을 입힌 값(ApplyTo)을
    ///   b-1 수학(RectChainMath)에 넘겨 직접 계산한다. 아무것도 쓰지 않으므로
    ///   복원이 없고, 예외가 나도 무대가 더러워지지 않는다.
    ///
    /// 종전 구현은 노드당 종류 하나만 담았다(위치를 게시한 뒤 스케일을 게시하면
    /// 위치가 지워졌다). 실사용에서 종류가 한 노드에 겹친 적은 없어(위치는 track/
    /// depthY, 스케일은 scale/depthScale — 축 노드가 단일 목적이다) 동작은 같고,
    /// 여기서는 슬롯을 분리해 그 함정을 없앤다.
    /// </summary>
    public sealed class PlacementTargetLedger
    {
        private struct Slots
        {
            public Vec2? AnchoredPosition;
            public Vec2? LocalScaleXY;   // z는 라이브 값을 보존한다 (종전 규약)
            public Vec3? LocalEuler;

            public bool IsEmpty =>
                !AnchoredPosition.HasValue && !LocalScaleXY.HasValue && !LocalEuler.HasValue;
        }

        private readonly Dictionary<string, Slots> _targets =
            new Dictionary<string, Slots>(StringComparer.Ordinal);

        public bool IsEmpty => _targets.Count == 0;
        public int Count => _targets.Count;

        public void PublishAnchoredPosition(string key, Vec2 target)
        {
            Slots slots = GetOrDefault(key);
            slots.AnchoredPosition = target;
            _targets[Require(key)] = slots;
        }

        public void PublishLocalScale(string key, Vec2 targetXY)
        {
            Slots slots = GetOrDefault(key);
            slots.LocalScaleXY = targetXY;
            _targets[Require(key)] = slots;
        }

        public void PublishLocalEuler(string key, Vec3 target)
        {
            Slots slots = GetOrDefault(key);
            slots.LocalEuler = target;
            _targets[Require(key)] = slots;
        }

        /// <summary>리덕션 출력(StageNodeClaim)을 그대로 예약으로 받는다.</summary>
        public void Publish(in StageNodeClaim claim)
        {
            switch (claim.Kind)
            {
                case StageNodeClaimKind.AnchoredPosition:
                    PublishAnchoredPosition(claim.NodeKey, claim.Value.XY);
                    break;

                case StageNodeClaimKind.LocalScaleXY:
                    PublishLocalScale(claim.NodeKey, claim.Value.XY);
                    break;

                case StageNodeClaimKind.LocalEulerAngles:
                    PublishLocalEuler(claim.NodeKey, claim.Value);
                    break;

                default:
                    throw new ArgumentException($"모르는 클레임 종류: {claim.Kind}", nameof(claim));
            }
        }

        public bool HasTargets(string key)
            => key != null && _targets.ContainsKey(key);

        public void Clear(string key)
        {
            if (key != null)
                _targets.Remove(key);
        }

        public void ClearAll() => _targets.Clear();

        /// <summary>
        /// 라이브 상태에 예약된 target을 입힌다. 예약이 없으면 그대로 돌려준다.
        /// 이 결과를 RectChainMath에 넘기는 것이 "정착 상태 계산"이다.
        /// </summary>
        public RectNodeState ApplyTo(string key, in RectNodeState live)
        {
            if (key == null || !_targets.TryGetValue(key, out Slots slots) || slots.IsEmpty)
                return live;

            RectNodeState result = live;

            if (slots.AnchoredPosition.HasValue)
                result = result.WithAnchoredPosition(slots.AnchoredPosition.Value);

            if (slots.LocalScaleXY.HasValue)
            {
                Vec2 xy = slots.LocalScaleXY.Value;
                result = result.WithLocalScale(new Vec3(xy.X, xy.Y, live.LocalScale.Z));
            }

            if (slots.LocalEuler.HasValue)
                result = result.WithLocalEuler(slots.LocalEuler.Value);

            return result;
        }

        private Slots GetOrDefault(string key)
        {
            Require(key);
            return _targets.TryGetValue(key, out Slots slots) ? slots : default;
        }

        private static string Require(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("장부 키가 비어 있다.", nameof(key));

            return key;
        }
    }
}
