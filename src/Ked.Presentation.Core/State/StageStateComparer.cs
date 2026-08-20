using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 등가성 판정: (코어가 접은 상태) vs (실제 재생에서 캡처한 상태).
    /// </summary>
    public static class StageStateComparer
    {
        /// <summary>px, 기준 해상도 공간. 잡음 상한 0.09px < ε < 신호 바닥 1px.</summary>
        public const float PositionEpsilon = 0.1f;

        /// <summary>스케일·anchor·pivot 등 무단위 성분.</summary>
        public const float ScalarEpsilon = 1e-4f;

        /// <summary>각도(°). 360° 순환으로 비교한다.</summary>
        public const float DegreesEpsilon = 0.01f;

        public const float AlphaEpsilon = 1e-3f;

        public sealed class Result
        {
            public readonly List<string> Mismatches = new();

            public int ComparedNodes;

            /// <summary>접힌 쪽에만 있는 노드 수. 캡처가 안 덮는 축의 크기다.</summary>
            public int FoldOnlyNodes;

            public bool IsEquivalent => Mismatches.Count == 0;

            public override string ToString()
                => IsEquivalent
                    ? $"등가 (노드 {ComparedNodes}개 비교, 접힘 전용 {FoldOnlyNodes}개)"
                    : $"불일치 {Mismatches.Count}건 (노드 {ComparedNodes}개 비교)";
        }

        /// <summary>captured의 노드·축을 folded와 비교한다.</summary>
        public static Result Compare(StageState folded, StageState captured)
        {
            if (folded == null) throw new ArgumentNullException(nameof(folded));
            if (captured == null) throw new ArgumentNullException(nameof(captured));

            Result result = new();

            foreach (string key in captured.Nodes.Keys)
            {
                if (!folded.Nodes.Contains(key))
                {
                    // 실제 무대에 있는데 접지 못한 노드 — 폴드에 빠진 축이다.
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
                    result.FoldOnlyNodes++;
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

        /// <summary>0°와 360°는 같은 각이다. 유니티가 오일러를 [0,360)으로 정규화해 돌려주기도 한다.</summary>
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