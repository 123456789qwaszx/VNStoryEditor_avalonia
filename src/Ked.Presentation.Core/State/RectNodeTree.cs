using System;
using System.Collections.Generic;
// RectNodeState
// = 노드 하나의 상태
//
// RectChainMath
// = 상태 체인을 받아 좌표를 계산하는 수학
//
// RectNodeTree
// = 노드들을 실제 부모-자식 트리로 관리하고,
//   필요한 체인을 만들어 RectChainMath에 넘겨주는 관리자
namespace Ked.Presentation.Core
{
    // RectNodeState들을 부모-자식 관계로 보관하고,
    // 특정 노드의 좌표 계산을 RectChainMath에 연결해주는 리그 트리 모델
    // (좌표 계산은 전부 RectChainMath에 위임.)
    public sealed class RectNodeTree
    {
        private struct Node
        {
            public string ParentKey; // null = 루트에 직접 생성
            public RectNodeState State;
        }

        private readonly Dictionary<string, Node> _nodes = new(StringComparer.Ordinal);

        /// <summary>트리가 딛고 서는 공간. 이 트리의 "월드"가 이 공간의 로컬이다.</summary>
        public RectSpace RootSpace { get; }

        public RectNodeTree(RectSpace rootSpace)
        {
            RootSpace = rootSpace;
        }

        public int Count => _nodes.Count;

        public IEnumerable<string> Keys => _nodes.Keys;

        public void Add(string key, string parentKey, in RectNodeState state)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("노드 키가 비어 있다.", nameof(key));

            if (_nodes.ContainsKey(key))
                throw new ArgumentException($"노드 '{key}'가 이미 있다.", nameof(key));

            if (parentKey != null && !_nodes.ContainsKey(parentKey))
            {
                throw new ArgumentException(
                    $"노드 '{key}'의 부모 '{parentKey}'가 아직 없다. 부모를 먼저 넣을 것.",
                    nameof(parentKey));
            }

            _nodes[key] = new Node { ParentKey = parentKey, State = state };
        }

        public bool Contains(string key) => key != null && _nodes.ContainsKey(key);

        public RectNodeState GetState(string key) => Require(key).State;

        public bool TryGetState(string key, out RectNodeState state)
        {
            if (key != null && _nodes.TryGetValue(key, out Node node))
            {
                state = node.State;
                return true;
            }

            state = default;
            return false;
        }

        public string GetParentKey(string key) => Require(key).ParentKey;

        // 있는 노드의 상태만 바꾼다. 없는 키를 조용히 새로 만들지 않음.
        public void SetState(string key, in RectNodeState state)
        {
            Node node = Require(key);
            node.State = state;
            _nodes[key] = node;
        }

        // 구조와 상태를 통째로 복제
        public RectNodeTree Clone()
        {
            RectNodeTree clone = new(RootSpace);

            foreach (KeyValuePair<string, Node> pair in _nodes)
                clone._nodes[pair.Key] = pair.Value;

            return clone;
        }

        // ---- 조회 ----

        /// <summary>노드 로컬 점 -> 루트 공간("월드") 점.</summary>
        public Vec3 TransformPoint(string key, Vec3 localPoint)
            => RectChainMath.TransformPoint(BuildChain(key), RootSpace, localPoint);

        /// <summary>루트 공간("월드") 점 -> 노드 로컬 점.</summary>
        public Vec3 InverseTransformPoint(string key, Vec3 worldPoint)
            => RectChainMath.InverseTransformPoint(BuildChain(key), RootSpace, worldPoint);

        /// <summary>노드의 rect 크기. 앵커가 Stretch모드면, 부모 크기에서 파생.</summary>
        public Vec2 GetRectSize(string key)
        {
            RectNodeState[] chain = BuildChain(key);

            Vec2 parentSize = RootSpace.Size;
            Vec2 size = parentSize;

            for (int i = 0; i < chain.Length; i++)
            {
                size = RectChainMath.RectSize(parentSize, in chain[i]);
                parentSize = size;
            }

            return size;
        }

        /// <summary>
        /// 루트→노드 순서의 상태 사슬과 (원하면) 같은 순서의 키 목록.
        /// SettledFocusMath처럼 체인 인덱스가 필요한 계산에 쓴다 —
        /// 호스트 어댑터의 CaptureSettledChain(chainRects)과 같은 역할이다.
        /// </summary>
        public RectNodeState[] BuildChainTo(string key, List<string> chainKeys = null)
        {
            if (chainKeys == null)
                return BuildChain(key);

            chainKeys.Clear();

            List<string> reversedKeys = new(16);
            string current = key;

            while (current != null)
            {
                reversedKeys.Add(current);
                current = Require(current).ParentKey;
            }

            for (int i = reversedKeys.Count - 1; i >= 0; i--)
                chainKeys.Add(reversedKeys[i]);

            return BuildChain(key);
        }

        private RectNodeState[] BuildChain(string key)
        {
            List<RectNodeState> reversed = new(16);

            string current = key;

            while (current != null)
            {
                Node node = Require(current);
                reversed.Add(node.State);
                current = node.ParentKey;
            }

            RectNodeState[] chain = new RectNodeState[reversed.Count];

            for (int i = 0; i < chain.Length; i++)
                chain[i] = reversed[chain.Length - 1 - i];

            return chain;
        }

        private Node Require(string key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (!_nodes.TryGetValue(key, out Node node))
                throw new KeyNotFoundException($"노드 '{key}'가 트리에 없다.");

            return node;
        }
    }
}