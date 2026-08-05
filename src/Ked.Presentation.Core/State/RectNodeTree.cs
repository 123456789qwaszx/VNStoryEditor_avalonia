using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 리그 노드 트리 — StageState의 뼈대.
    ///
    /// CharacterPlacementTargetLedger가 current.parent로 유니티 계층을 타고 올라가던
    /// 그 구조를 데이터로 세운 것이다. 키는 문자열 논리 식별자다 —
    /// RectTransform 참조는 코어에 들어오지 않는다.
    ///
    /// 불변식:
    /// - 부모가 먼저 있어야 자식을 넣을 수 있고, 재부모화 API가 없다.
    ///   따라서 순환이 생길 방법이 없다(BuildChain이 순환 검사를 안 해도 되는 근거).
    /// - 없는 키의 조회·갱신은 조용히 0을 돌려주지 않고 예외를 던진다.
    ///   침묵은 이 프로젝트가 피하는 실패 모양이다.
    /// </summary>
    public sealed class RectNodeTree
    {
        private struct Node
        {
            public string ParentKey; // null = 루트 공간 직속
            public RectNodeState State;
        }

        private readonly Dictionary<string, Node> _nodes = new Dictionary<string, Node>(StringComparer.Ordinal);

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
                throw new ArgumentException(
                    $"노드 '{key}'의 부모 '{parentKey}'가 아직 없다. 부모를 먼저 넣을 것.", nameof(parentKey));

            _nodes[key] = new Node { ParentKey = parentKey, State = state };
        }

        public bool Contains(string key) => _nodes.ContainsKey(key);

        public RectNodeState GetState(string key) => Require(key).State;

        public bool TryGetState(string key, out RectNodeState state)
        {
            if (_nodes.TryGetValue(key, out Node node))
            {
                state = node.State;
                return true;
            }

            state = default;
            return false;
        }

        /// <summary>부모 키. null이면 루트 공간 직속이다.</summary>
        public string GetParentKey(string key) => Require(key).ParentKey;

        /// <summary>있는 노드의 상태만 바꾼다. 없는 키를 조용히 새로 만들지 않는다.</summary>
        public void SetState(string key, in RectNodeState state)
        {
            Node node = Require(key);
            node.State = state;
            _nodes[key] = node;
        }

        public RectNodeTree Clone()
        {
            RectNodeTree clone = new RectNodeTree(RootSpace);

            foreach (KeyValuePair<string, Node> pair in _nodes)
                clone._nodes[pair.Key] = pair.Value;

            return clone;
        }

        // ── 조회 (b-1 수학) ──────────────────────────────────────────

        /// <summary>노드 로컬 점 → 루트 공간("월드") 점.</summary>
        public Vec3 TransformPoint(string key, Vec3 localPoint)
            => RectChainMath.TransformPoint(BuildChain(key), RootSpace, localPoint);

        /// <summary>루트 공간("월드") 점 → 노드 로컬 점.</summary>
        public Vec3 InverseTransformPoint(string key, Vec3 worldPoint)
            => RectChainMath.InverseTransformPoint(BuildChain(key), RootSpace, worldPoint);

        /// <summary>노드의 rect 크기. 스트레치 앵커면 부모 크기에서 파생된다.</summary>
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
        /// SettledFocusMath처럼 체인 인덱스가 필요한 계산에 쓴다.
        /// </summary>
        public RectNodeState[] BuildChainTo(string key, List<string> chainKeys = null)
        {
            if (chainKeys == null)
                return BuildChain(key);

            chainKeys.Clear();

            List<string> reversedKeys = new List<string>(16);
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

        /// <summary>루트→노드 순서의 상태 사슬. RectChainMath 규약(chain[0]의 부모가 RootSpace)과 같다.</summary>
        private RectNodeState[] BuildChain(string key)
        {
            List<RectNodeState> reversed = new List<RectNodeState>(16);

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
