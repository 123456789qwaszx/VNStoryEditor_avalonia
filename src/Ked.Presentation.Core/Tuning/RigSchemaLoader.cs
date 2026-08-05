using System;
using System.Collections.Generic;
using System.Text;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// U12-전체 리그 스키마 덤프 → RectNodeTree.
    ///
    /// 스키마는 tuning 인자로 온다 — 코어에 리그 구조를 하드코딩하지 않는다.
    /// 여기 있는 유일한 하드코딩은 덤프 "형식"의 지식이다(__root 키, 캡처 pivot).
    /// 게임 데이터의 지식(노드 이름·구조)은 전부 인자에서 온다.
    /// </summary>
    public static class RigSchemaLoader
    {
        /// <summary>덤프에서 리그 루트 노드의 키. 익스포터와의 규약이다.</summary>
        public const string RootKey = "__root";

        /// <summary>
        /// 덤프 파일에서 rigKind 하나를 골라 트리로 만든다.
        /// keyPrefix는 리그 인스턴스 구분용이다(예: "c1/") — 같은 리그를 여러 슬롯에
        /// 세울 때 키가 충돌하지 않게 한다.
        /// </summary>
        public static RectNodeTree BuildTree(
            RigSchemasFileDto file,
            string rigKind,
            string keyPrefix = "")
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));

            if (file.rigs == null || file.rigs.Count == 0)
                throw new ArgumentException("덤프에 rigs가 없다. 파일이 비었거나 역직렬화가 실패했다.", nameof(file));

            if (file.capturedUnderParentSize == null)
                throw new ArgumentException("덤프에 capturedUnderParentSize가 없다.", nameof(file));

            RigSchemaRigDto rig = FindRig(file, rigKind);

            // 익스포터는 가운데 pivot 스테이지 밑에서 캡처했다 (schema.md).
            RectSpace space = new RectSpace(file.capturedUnderParentSize.ToVec2(), Vec2.Half);

            return BuildTree(rig, space, keyPrefix);
        }

        /// <summary>리그 하나를 지정한 공간 밑에 세운다. 노드는 덤프 순서대로(부모 먼저) 들어간다.</summary>
        public static RectNodeTree BuildTree(
            RigSchemaRigDto rig,
            RectSpace rootSpace,
            string keyPrefix = "")
        {
            RectNodeTree tree = new RectNodeTree(rootSpace);
            AddRigTo(tree, rig, keyPrefix, attachParentKey: null);
            return tree;
        }

        /// <summary>
        /// 이미 있는 트리에 리그 인스턴스를 붙인다(StageReducer의 slot 폴드용).
        /// attachParentKey가 null이면 트리의 루트 공간 직속이다.
        /// </summary>
        public static void AddRigTo(
            RectNodeTree tree,
            RigSchemaRigDto rig,
            string keyPrefix = "",
            string attachParentKey = null)
        {
            if (tree == null)
                throw new ArgumentNullException(nameof(tree));

            if (rig == null)
                throw new ArgumentNullException(nameof(rig));

            if (rig.nodes == null || rig.nodes.Count == 0)
                throw new ArgumentException($"리그 '{rig.rigKind}'에 노드가 없다.", nameof(rig));

            keyPrefix ??= "";

            for (int i = 0; i < rig.nodes.Count; i++)
            {
                RigSchemaNodeDto node = rig.nodes[i];

                if (node == null || string.IsNullOrEmpty(node.id))
                    throw new ArgumentException($"리그 '{rig.rigKind}'의 노드 [{i}]에 id가 없다. 덤프가 손상됐다.", nameof(rig));

                string key = keyPrefix + node.id;
                string parentKey = string.IsNullOrEmpty(node.parent)
                    ? attachParentKey
                    : keyPrefix + node.parent;

                tree.Add(key, parentKey, ToState(node, rig.rigKind));
            }
        }

        /// <summary>덤프 노드 → b-1 상태 값. 필드 해석은 여기 한 곳뿐이다.</summary>
        public static RectNodeState ToState(RigSchemaNodeDto node, string context = null)
        {
            if (node == null)
                throw new ArgumentNullException(nameof(node));

            if (node.anchoredPosition == null || node.anchorMin == null || node.anchorMax == null ||
                node.pivot == null || node.sizeDelta == null ||
                node.localScale == null || node.localEulerAngles == null)
            {
                throw new ArgumentException(
                    $"노드 '{node.id}'(리그 {context ?? "?"})에 빈 트랜스폼 필드가 있다. " +
                    "덤프가 손상됐거나 역직렬화 설정(필드 포함)이 빠졌다.", nameof(node));
            }

            return new RectNodeState(
                anchoredPosition: node.anchoredPosition.ToVec2(),
                anchorMin: node.anchorMin.ToVec2(),
                anchorMax: node.anchorMax.ToVec2(),
                pivot: node.pivot.ToVec2(),
                sizeDelta: node.sizeDelta.ToVec2(),
                localScale: node.localScale.ToVec3(),
                localEulerAngles: node.localEulerAngles.ToVec3());
        }

        private static RigSchemaRigDto FindRig(RigSchemasFileDto file, string rigKind)
        {
            for (int i = 0; i < file.rigs.Count; i++)
            {
                if (string.Equals(file.rigs[i]?.rigKind, rigKind, StringComparison.Ordinal))
                    return file.rigs[i];
            }

            // 못 찾으면 무엇이 있었는지까지 말한다 — "비슷한 이름"으로 잇지 않는다.
            StringBuilder available = new StringBuilder();

            for (int i = 0; i < file.rigs.Count; i++)
            {
                if (i > 0)
                    available.Append(", ");

                available.Append(file.rigs[i]?.rigKind ?? "(null)");
            }

            throw new ArgumentException(
                $"덤프에 rigKind '{rigKind}'가 없다. 있는 것: [{available}]", nameof(rigKind));
        }
    }
}
