using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// U14 등가성 판정: (코어가 접은 상태) vs (실제 재생에서 캡처한 상태).
    ///
    /// ε는 b-1에서 정한 정책 그대로다(Documentation~/transform-math-and-epsilon.md):
    /// 위치 0.1px · 무단위 성분 1e-4 · 각도 0.01°. 불일치가 나면 ε를 늘리는 것이
    /// 아니라 리듀서를 고친다 — 불일치는 발견이다.
    ///
    /// 비교 방향: "캡처된 노드"를 기준으로 돈다. 접은 쪽에 없는 캡처 노드는 불일치다.
    /// 캡처가 안 덮는 축(접힌 쪽에만 있는 노드)은 침묵하지 않도록 개수로 보고한다.
    /// </summary>
    public static class StageStateComparer
    {
        public const float PositionEpsilon = 0.1f;   // px (기준 해상도 공간)
        public const float ScalarEpsilon = 1e-4f;    // 스케일·anchor·pivot 등 무단위
        public const float DegreesEpsilon = 0.01f;   // 각도
        public const float AlphaEpsilon = 1e-3f;

        public sealed class Result
        {
            public readonly List<string> Mismatches = new List<string>();
            public int ComparedNodes;
            public int SkippedFoldOnlyNodes;

            public bool IsEquivalent => Mismatches.Count == 0;

            public override string ToString()
                => IsEquivalent
                    ? $"등가 (노드 {ComparedNodes}개 비교, 접힌 쪽 전용 {SkippedFoldOnlyNodes}개)"
                    : $"불일치 {Mismatches.Count}건 (노드 {ComparedNodes}개 비교)";
        }

        /// <summary>captured의 노드·축을 folded와 비교한다.</summary>
        public static Result Compare(StageState folded, StageState captured)
        {
            if (folded == null) throw new ArgumentNullException(nameof(folded));
            if (captured == null) throw new ArgumentNullException(nameof(captured));

            Result result = new Result();

            foreach (string key in captured.Nodes.Keys)
            {
                if (!folded.Nodes.Contains(key))
                {
                    result.Mismatches.Add($"{key}: 캡처에는 있는데 접힌 상태에 없다");
                    continue;
                }

                result.ComparedNodes++;

                CompareNode(key, folded.Nodes.GetState(key), captured.Nodes.GetState(key), result);

                float foldedAlpha = folded.GetAlpha(key);
                float capturedAlpha = captured.GetAlpha(key);

                if (Math.Abs(foldedAlpha - capturedAlpha) > AlphaEpsilon)
                    result.Mismatches.Add(Diff(key, "alpha", foldedAlpha, capturedAlpha));
            }

            foreach (string key in folded.Nodes.Keys)
            {
                if (!captured.Nodes.Contains(key))
                    result.SkippedFoldOnlyNodes++;
            }

            CompareShot(folded.Shot, captured.Shot, result);

            return result;
        }

        private static void CompareNode(
            string key, in RectNodeState folded, in RectNodeState captured, Result result)
        {
            CompareVec2(key, "anchoredPosition", folded.AnchoredPosition, captured.AnchoredPosition, PositionEpsilon, result);
            CompareVec2(key, "anchorMin", folded.AnchorMin, captured.AnchorMin, ScalarEpsilon, result);
            CompareVec2(key, "anchorMax", folded.AnchorMax, captured.AnchorMax, ScalarEpsilon, result);
            CompareVec2(key, "pivot", folded.Pivot, captured.Pivot, ScalarEpsilon, result);
            CompareVec2(key, "sizeDelta", folded.SizeDelta, captured.SizeDelta, PositionEpsilon, result);
            CompareVec3(key, "localScale", folded.LocalScale, captured.LocalScale, ScalarEpsilon, result);
            CompareEuler(key, "localEulerAngles", folded.LocalEulerAngles, captured.LocalEulerAngles, result);
        }

        private static void CompareShot(in ShotIntentState folded, in ShotIntentState captured, Result result)
        {
            if (Math.Abs(folded.Zoom - captured.Zoom) > ScalarEpsilon)
                result.Mismatches.Add(Diff("shot", "zoom", folded.Zoom, captured.Zoom));

            CompareVec2("shot", "panInRigSpace", folded.PanInRigSpace, captured.PanInRigSpace, PositionEpsilon, result);
            CompareVec2("shot", "focusPointInRigSpace", folded.FocusPointInRigSpace, captured.FocusPointInRigSpace, PositionEpsilon, result);
        }

        private static void CompareVec2(
            string key, string field, Vec2 folded, Vec2 captured, float epsilon, Result result)
        {
            if (Math.Abs(folded.X - captured.X) > epsilon || Math.Abs(folded.Y - captured.Y) > epsilon)
                result.Mismatches.Add(Diff(key, field, folded, captured));
        }

        private static void CompareVec3(
            string key, string field, Vec3 folded, Vec3 captured, float epsilon, Result result)
        {
            if (Math.Abs(folded.X - captured.X) > epsilon ||
                Math.Abs(folded.Y - captured.Y) > epsilon ||
                Math.Abs(folded.Z - captured.Z) > epsilon)
            {
                result.Mismatches.Add(Diff(key, field, folded, captured));
            }
        }

        private static void CompareEuler(
            string key, string field, Vec3 folded, Vec3 captured, Result result)
        {
            if (AngleDelta(folded.X, captured.X) > DegreesEpsilon ||
                AngleDelta(folded.Y, captured.Y) > DegreesEpsilon ||
                AngleDelta(folded.Z, captured.Z) > DegreesEpsilon)
            {
                result.Mismatches.Add(Diff(key, field, folded, captured));
            }
        }

        /// <summary>0°와 360°는 같은 각이다.</summary>
        private static float AngleDelta(float a, float b)
        {
            float delta = Math.Abs(a - b) % 360f;
            return delta > 180f ? 360f - delta : delta;
        }

        private static string Diff(string key, string field, object folded, object captured)
            => string.Format(CultureInfo.InvariantCulture,
                "{0}.{1}: 접힘={2} vs 캡처={3}", key, field, folded, captured);
    }
}
